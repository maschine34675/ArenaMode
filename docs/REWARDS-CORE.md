# Reward settlement core (2026-10-24)

Historical implementation-stage notes below. Gameplay capture, explicit consent,
startup enrollment and private-session transport are now connected; see
[REWARDS-RUNTIME.md](REWARDS-RUNTIME.md) for the current flow and its verification.

Status: **no gameplay reward production yet**. The isolated kernel now has a
[guarded ordinary-server startup consumer](REWARDS-SERVER.md), registered before
native profile loading. There are no client endpoints, kill/win hooks, live-profile
replacement or automatic enrollment. Existing installations without the separate
trusted enrollment/approval store remain unchanged. This document records the
kernel contract and its original synthetic verification; the startup document
describes the additional native boundary, assumptions and tests. The subsequent
[server result validator and approval producer](REWARDS-RESULTS.md) calculates
amounts from separately trusted frozen rules and checked reports; real evidence
capture and transport are still pending.

## Scope

The first step toward optional RUB rewards is to settle an already-authorized
receipt safely. This kernel does not decide which kills or wins qualify, or their
value. The result validator handles the server-owned calculation separately;
a future client request must never be allowed to supply its own payout amount.

- A receipt is bound to the installation, native profile ID, explicit profile
  generation (epoch), run ID, sequence and reward-policy version.
- The stable run/sequence key identifies retries. A SHA-256 fingerprint also
  covers identity, amount and rules. Reusing the key with different content is
  quarantined. This is integrity checking, not authentication or anti-cheat.
- RUB arrives as native system-mail attachments, split into bounded stacks.
  No stash placement, equipment, XP, quests or Arena-profile fields are imported.
- The mail and permanent paid receipt are composed in one detached profile
  document and committed together. Deleting, collecting or expiring the mail
  never removes the paid receipt and cannot trigger another payout.
- An independent journal covers all receipts for that profile generation. It
  must live outside the disposable private-server tree. Every settlement checks
  the entire journal against the profile ledger, including earlier runs.
- Missing enrollment is not an empty ledger. Unknown schemas, malformed data,
  mismatched identities and divergent histories are refused without replacing
  unrelated profile state. No automatic full-profile rollback is performed.

The namespace is `com.maschine.ArenaMode.rewards`. Its schema 1 contains the
installation/profile/epoch binding and permanent payment records. Explicit
`RewardProfileDocument.Enroll` is a detached setup operation used by fixtures;
normal settlement never calls it or replaces existing metadata. Journal JSON
has schema 1, the same binding, and prepared/attempting/committed records.

## Commit protocol and failures

`IRewardSettlementStore` grants an exclusive lease. A missing lease means
deferred, not rejected or paid. Under that lease the core:

1. Reads the durable profile and journal, validates their complete history and
   composes the new mail plus ledger on a detached document.
2. Durably records `Prepared` with the original profile revision, then
   `Attempting`, verifying each journal write by reading it back.
3. Replaces the profile with a compare-and-swap revision check and reads the
   persisted profile again. Only the exact expected serialized document proves
   this write succeeded; a save method returning success is insufficient.
4. Durably records and reads back `Committed`. Only then returns `Paid` with the
   message ID. A future caller may acknowledge/notify after this result.

An exception after a successful profile replacement can be resolved by read-back.
If this invocation can still prove the original profile remains under the same
exclusive lease, it records `Prepared` again and defers for a safe retry. If a
later process sees `Attempting` with no matching profile receipt, it cannot tell
an interrupted write from a restored backup: it quarantines instead of repaying.
If mail and ledger are present but the final journal acknowledgement is missing,
recovery requires the original, exact, uncollected attachment. Once committed,
mail lifecycle changes do not matter. A lost caller acknowledgement simply
returns `AlreadyPaid` next time.

Unchanged/failed writes, unavailable disk reads and cancellation before entry
return `Deferred`. Cancellation after the first mutation does not interrupt the
bounded synchronous transaction. Corrupt/conflicting/ambiguous histories return
`Quarantined`; the core itself does not erase them or repair a real profile.
Confirmed missing enrolled files are also quarantined; a temporary I/O failure
remains deferred. Neither case creates a replacement profile or empty journal.
Invalid inputs and full ledgers return `Rejected`. It never acknowledges a
pending or quarantined result as paid.

Safety bounds in this prototype: at most 10,000,000 RUB per receipt, at most 100
stacks of no more than 500,000 RUB, 4,096 permanent receipts per generation,
30-day attachment lifetime. These are engineering ceilings, **not reward rates
or a promised final reward policy**. Production must freeze the native item
stack limit and policy for the enrollment. Reaching the ledger limit refuses new
receipts; it never silently prunes duplicate protection. Per-match batching and
a durable longer-term retention policy are still needed before live adoption.

## What live integration must still establish

The synthetic store's file lock is not a solution for native SPT writers. The
startup adapter avoids regular live writers by running before native load; it
does not prove an all-writer lock for arbitrary mod threads. The following still
applies before enabling runtime rewards or a future live-server adapter:

- Explicit opt-in and enrollment of the ordinary profile; trustworthy origin and
  epoch binding, including wipe/import and private-session copies.
- Ordinary server only, no active ordinary/Arena raid, protected full-profile
  restoration durably finished, no stale pending end transaction.
- A common boundary for native autosaves, profile mutations, mailbox/attachment
  collection and foreign writers. Arena's own start/end semaphore alone does
  not cover these. A snapshot clone followed by blind replacement could lose
  ordinary progress, even when reward requests serialize perfectly.
- Durable reads rather than a `SaveServer` cache; cache/live-object consistency
  after replacement; explicit handling of failed saves. Native SPT's hash cache
  can already match the requested state after an unsuccessful write.
- Native mail/UI acceptance with copied test profiles; delayed notification
  after commit. `MailSendService` currently mutates the live object and notifies
  before saving, so calling it before/after a separate paid flag is not atomic.
- A durable receipt outbox and origin acknowledgement outside the helper's
  disposable tree; authoritative outcome validation and recovery UI. No arbitrary
  profile paths or client-chosen payout values may enter that interface.

Restoring only a profile or only a journal is detectable as divergence. Restoring
**both together to the same older state** removes the evidence of later payouts;
this two-file design cannot detect it without an additional non-rollbackable
authority. A local owner can also edit files or code. The core therefore does not
promise globally exactly-once payments across arbitrary backup restores or
tampering. Quarantined payments require a separately designed reconciliation
flow, not clearing the journal or resetting the profile epoch automatically.

## Offline verification

`RewardSettlementCoreTests` exercises real, newly generated temporary profile
and journal files, separate store instances, atomic replacement, concurrency,
lost acknowledgements, phase-specific I/O failures and abrupt-exception crash
simulation. Fixtures do not open existing SPT profiles. Every fixture owns a
unique directory and validates its ownership marker before cleanup.

`RewardProfileDocumentTests` verifies native mail shape, bounded currency stacks,
unrelated JSON preservation, corrupt metadata refusal and serialization through
the referenced SPT models and converters (not a running native save service).
The unified verification runner builds without deployment and checks installed
artifact hashes before/after:

```powershell
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1
```

Final run on 2026-10-24: **93/93 reward tests passed**. Complete unit suite:
2,419 passed, zero failed, one existing environment-gated APBS native-pipeline
test skipped (`InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline`).
The runner's installed-artifact hash checks passed. Local detailed results are
in `artifacts/run-arena-verification/run-arena-verification.trx` (ignored).

Independent review caught missing journal constructor fields being defaulted to
`Prepared`; required constructor-member validation and explicit missing-phase
regressions now refuse that ambiguity. The first executable run also caught a
fresh JSON `int` versus parsed `long` mismatch when creating a new system dialogue;
the corrected parser accepts integral values and rejects numeric strings/floats.
Deleted enrolled files are tested separately from malformed JSON and transient
I/O failures. The final suite includes all these corrections.

These tests prove the isolated protocol under its storage contract. They do not
prove native concurrent-writer exclusion, actual OS/power-failure durability,
mail collection in-game, or trustworthy gameplay reward accounting. There was
no deployment or GPU test as part of this step.
