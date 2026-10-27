# Reward recovery engine — 2026-10-27

Uncertain old Arena operations are discarded automatically **without another payment** at normal server startup. The current native profile remains authoritative. This is neither profile restoration nor a reconstruction of a lost payment. Rewards opt-in is unchanged. The [automatic policy](REWARD-DISCARD-20261027.md) supersedes the manual workflow originally implemented on this date.

## Authority and scope

Only the ordinary startup processor creates a case-specific discard decision. No recovery HTTP routes or player confirmation remain. The engine accepts pending schema-1 snapshot/run guards and exact committed RUB/XP journal entries absent from the profile ledger. It validates their result-decision sequence and payment history before closure. Unknown schemas, mismatched history, linked paths, true offline ownership recovery, and unresolved Cola transactions are not treated as disposable old claims.

The review binds installation/profile/epoch, the Arena profile extension, prior recovery outcomes, exact affected journal entries, and hashes of original guard/run/receipt/allocation/import/decision/checkpoint files. Unrelated profile progression and autosave fields do not invalidate the displayed review. A change to affected evidence or Arena metadata does. Scans and responses are bounded; one review contains at most 64 cases and a profile retains at most 64 recovery attempts.

Original journal, receipt, decision, imported, snapshot and run bytes remain unchanged. The new profile field is an optional `recoveries` array inside `com.maschine.ArenaMode.rewards`, containing only review/request fingerprints. It does not add a paid receipt, mail, XP, money, restored flag, consent change, or new enrollment epoch.

## Durable boundary

`recovery/<review-id>/` retains immutable records:

1. `request.json`: the exact server-owned discard decision, written at startup without a profile edit. The record remains compatible with pending requests from the former manual workflow.
2. `before-profile.json`: the entire current profile at the native pre-load boundary, retained for audit only.
3. `plan.json`: request fingerprint and whole-profile before/after hashes. The after document differs only by the recovery marker.
4. `completion.json`: either `closed-without-payment` after durable profile readback, or `stale` when another review is necessary.

The startup processor first finishes actual Cola recovery, then reconciles positive restoration checkpoints, then resumes interrupted discard work and automatically closes remaining supported uncertain cases before the rewards-OFF early return. Actual offline ownership recovery precedes this callback. No live profile is written.

A crash before profile replacement retries the same exact plan. A later normal save before replacement makes that plan stale; it cannot replace or rebase onto the newer profile. A crash after marker replacement can finish acknowledgement if the complete owned extension is unchanged, even if unrelated native progression has since changed. That path writes no profile. Every durable boundary has a test injection point.

A completed sidecar whose marker disappeared after a profile backup rollback is revalidated through a fresh server-owned decision, including when that backup contains the originally missing paid ledger entry. That decision may cover the identical old cases while preserving prior records. Changed original source bytes fail closed rather than becoming a generic reset opportunity.

## Permanent rejection and future rewards

The kernel, startup store, approval producer, inbox and stale candidate/approval fallback accept only validated completed decisions with a matching native marker. Missing payment exceptions require the identical committed journal entry. Late signed receipts for the closed snapshot generation or exact run/arm identity are retained and rejected without allocating a sequence, paying, or writing an imported-as-paid marker. Replays return `ClosedWithoutPayment`, never `Paid`, `AlreadyPaid` or `Restored`.

Checkpoint publication uses a separate `ReadCheckpointClosures` query because it runs before recovery commit. That query keeps requested/closed generations untouched across a marker-before-completion crash or missing-marker review. It provides **no payment authority**. The ordinary kernel/inbox always use the strict `ReadClosures` query.

New unrelated runs retain their normal validation and can pay. New unresolved generations and changed unrelated journal history remain errors. Exact historical exceptions do not waive a genuine Cola transaction or profile ownership recovery.

## Verification inventory

`RewardRecoveryTests` covers read-only preview, stable review after native progress, immutable source retention, explicit OFF preservation, future payment after legacy and missing-ledger cases, all six durable crash boundaries, post-crash normal saves, lost markers with and without returned paid ledger, stale reviews, changed original evidence/payment amounts, unrelated pending generations, late signed receipts, unknown schemas, true recovery barriers, and closed/native-loaded startup windows.

The two optional real-data cases use `SPT_REWARD_RECOVERY_BASELINE` pointing to the private baseline directory with `local/` and `gpu2/`. Each test copies only the selected profile and its Arena namespaces into an owned temporary root. Only copied enrollment/handoff/delegation root paths are relocated; signed receipt payloads, keys, allocation, result and payment evidence remain exact. Tests assert the original copied case bytes and unrelated profile fields survive closure, then verify a fresh reward and replay. Omission is an explicit skipped test, never a silent pass. The task verification runner supplies the baseline and requires zero skips for this family.

Four implementation passes: authority/schema scope; durable-write/crash transitions; kernel/inbox/producer replay exceptions; preservation and future-operation behavior. Builds, test execution, independent review, deployment and native acceptance are owned by the integrating root task and are recorded separately; source implementation alone is not native acceptance.

The root run passed all 37 engine checks, including both real isolated baselines.
See [integration acceptance](REWARD-RECOVERY-ACCEPTANCE-20261027.md) for the full
regression, deployed builds, native lifecycle and unchanged original claims.
