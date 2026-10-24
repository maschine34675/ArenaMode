# Server result validation and payout approval (2026-10-24)

The startup pipeline can now **derive an approval from a bound combat report**.
It no longer requires the final RUB claim to have been prepared separately.
`RewardApprovalProducer` runs inside the existing guarded ordinary-server startup
window, before `RewardStartupProcessor` hands its approval to the settlement core.

This is a server-side protocol stage, not enabled gameplay rewards. No client
route, match evidence collector, enrollment UI or private-session transport was
added. Existing players are not enrolled and existing profiles are untouched.
No deployment or native gameplay test is part of this change. The current game
cannot produce `candidate.json`; only synthetic tests exercise the full new path.

## What the server can establish

SPT does not simulate Arena combat. `ArenaMatch`, `ArenaScoreboard`, round wins,
Gun Game progression and zombie wave completion live in the game client. Native
`EndLocalRaidRequestData.Results.Profile` is also a client report, deliberately
excluded from Arena's normal-profile restore. Redeploy preserves victim records
across respawns, but that does not turn them into independent combat evidence or
provide Arena's round/no-contest semantics.

Consequently this layer validates **identity, consistency and plausibility**. It
owns the amount calculation and payment decision; it cannot prove physical kills
against a malicious local combat client. Nothing here weakens snapshot restoration
or imports XP, inventory, quests, health or client-chosen currency into the profile.

Three inputs have different trust levels:

| Input | Authority and content |
|---|---|
| `RewardRunAuthorization` | Future server collector: installation/profile/epoch, issued run ID and consecutive sequence, arm token, exact native-end identity, restoration generation, clean snapshot hash, local participant, mode, frozen rates/rules, ready round rosters and registered lives. |
| `RewardMatchReport` | Untrusted combat data: exact run/arm/native-end identity, completion state, ordered rounds and fatal events. Contains **no amount, score or asserted winner**. |
| `RewardRestorationEvidence` | Future protected lifecycle collector: the same identities, positive completed restoration, expected snapshot hash and independent durable read-back hash. |

`candidate.json` is an **internal trusted envelope**, not a player import format
or HTTP payload. A future endpoint must not accept authorization/restoration from
the client. Copying the ordinary profile into a private session, `HasSnapshot ==
false`, a completed session tombstone, or `SaveProfileAsync` returning is not proof.
The existing restore swaps RAM state, and the native save may skip disk or update
its hash cache before a failed write; real evidence capture remains necessary.

The ordinary runtime root still comes from the enrollment and startup boundary,
never from the report. `CleanProfileRevision` must match the positive restoration
evidence and the actual durable normal profile. Legitimate ordinary progress that
changes this hash defers/quarantines the candidate; it is never silently rebased.
Confirmation of an already executed identical payment is the sole hash exception.

## Result rules

The server-owned rate policy supplies a version, RUB per eligible kill and per
round win, and count/amount ceilings. It is frozen with the run. There are **no
player-facing defaults yet**; the test rates are arbitrary fixtures, not proposed
balancing. Arithmetic is checked; an exceeded ceiling rejects rather than clips
the result or changes the policy. The settlement core's 10,000,000-RUB engineering
ceiling remains in force.

The first protocol bounds one run to 100 rounds, 20,000 registered physical lives
and 24 hours of active round time. Every life is unique throughout a run. A player's
respawn may retain its participant ID, but a replacement FFA bot must retain its
own native individual identity, not inherit the previous occupant's scoring slot.

- Only a hostile final kill by the local participant contributes personal RUB.
  Allies' kills, assists, self-kills, friendly kills and environmental deaths do not.
- Every victim may die once. Event sequence is consecutive across rounds; times
  must be ordered, within the observed round, and after both bodies were registered.
  Delayed grenade/fire attribution may name an attacker who has since died.
- Round IDs, ordering and counts must match the authorization. Future schema,
  duplicate JSON fields, unknown members/enums, missing required fields, changed
  identities, impossible counts and events after a terminal result are refused.
- An aborted/no-contest run or administrative retirement receives no payment in
  this first version, **including earlier rounds of that run**. This intentionally
  does not yet pay partial valid rounds from a later failed match.

| Frozen rule | Win derivation |
|---|---|
| Deathmatch FFA | First individual at the kill limit, or unique positive kill leader at timeout. Ties/no leader do not earn a win. Points are irrelevant. |
| Deathmatch all vs player | Local kill target reached. A timeout before that target is a loss; time-only completion earns no win bonus. |
| Team Deathmatch | Team kill target/time comparison. Ally kills help the team score but are not local kill rewards. Only an explicit **local give-up** adds the virtual opposing-team kill; ordinary self/environment deaths do not. |
| Tactical solo | All five registered opponents eliminated before timeout while the player survives. |
| Tactical PFT | Five opponents; player plus one to four companions. Team elimination first, then timeout compares surviving bodies; simultaneous total wipe is a loss. |
| Continuous DM, Gun Game, Zombies | `KillsOnly`: eligible kills, **no inferred ladder/wave/round win bonus**. |

The future adapter must capture fully ready rounds. Missing tactical spawns cannot
be mistaken for a smaller victorious roster; the validator requires five opponents.
A solo round that continued via a partial-spawn fallback therefore earns no reward
through this first protocol. Gun Game demotion/skipped unusable tiers and zombie
administrative retirements prevent deriving a real ladder/wave victory from kill
totals alone; richer evidence is needed before adding those bonuses.

`Completed` means a fully captured, normally finalized report, not `Mode.IsComplete`
or a win. Gun Game and continuous DM can finish their raid report without winning
a finite match. The future collector must distinguish that from a crash/failure.
Observed durations are **active simulation time**, excluding countdowns/pauses;
they must not be inferred from the server's ordinary wall clock. DM/TDM and solo
Tactical exclude events at `time >= deadline`; team Tactical resolves elimination
before its timeout. The evaluator preserves that distinction. Multiple events
after a kill-target winner are conservatively rejected, even at the same timestamp.

## Durable decisions and retry behavior

Under the already enrolled ordinary profile directory:

| File | Purpose |
|---|---|
| `candidate.json` | One current complete envelope from the future trusted coordinator. |
| `decisions/<runId>.json` | Permanent result decision: profile binding, consecutive sequence, fingerprint of all three semantic inputs, evaluated units/amount and optional approval. |
| `approved.json` | Existing kernel format, now generated only after the decision is durable. |
| `journal.json`, `acknowledged.json` | Existing settlement protocol; no changes to its payment commit ordering. |

The producer creates only its own decision directory/files and approval. It never
creates enrollment, replaces the profile, empties a journal or writes a native
live object. A per-profile approval lock coordinates producer calls in addition
to the normal startup gate. The whole producer/payment pipeline is run serially
inside that startup window; this is not a general concurrent runtime API.

Before approval it checks:

1. The candidate and positive restoration evidence match the enrolled origin.
2. The normal durable profile is clean and unchanged, and journal/profile histories
   agree. A copied or advanced profile is not accepted as the original snapshot.
3. Every prior producer decision with a payment has a matching committed journal
   and profile entry. Conversely, a journal payment without its original decision
   is refused. Lost latest history does not free its sequence for reuse.
4. A previous approval slot is advanced only after its payment is positively
   committed. An acknowledgement alone does not release it. An interrupted prior
   decision whose approval was not yet published also blocks the next run.
5. The complete semantic fingerprint is recorded **before** approval, followed by
   read-back. Whitespace/property order changes are harmless; changed events,
   rules or restoration identity conflict even if they yield the same RUB amount.

Accepted zero-payment decisions and well-formed terminal rejected reports also
remain recorded. This first internal protocol treats the supplied report as final;
it has no partial-upload correction API. Malformed JSON/authorization is rejected
before a decision is created. The future collector must assemble and validate its
packet before publishing it, rather than exposing this slot to partial traffic.

Identical retry can finish after decision publication but before approval, or
confirm an already committed payment. Historical/out-of-order candidates cannot
move the active slot backwards. Confirming a payment remains profile-read-only;
an interrupted journal commit still requires the exact original uncollected mail.
Permanent decisions are bounded to 4,096 per enrollment generation and never
pruned automatically. Even zero-payment runs use a slot; retention/rollover is a
later explicit design task.

Atomic sibling staging uses the existing file helper. Exact unpublished
`.arena-reward-<guid>.tmp` leftovers are ignored; other unexpected history files,
path traversal and reparse points are refused. Startup locks and path/revision
checks retain the cooperative-writer limitations in [REWARDS-SERVER.md](REWARDS-SERVER.md).
External file editors or joint rollback of profile, journal and authority are not
made tamper-proof. No claim of independent combat verification or universal
exactly-once payment follows from these local files.

Older candidate-free trusted approvals still use the preceding startup consumer.
Adopting the new producer on top of payments without result decisions is refused;
there is no inferred migration. No real profiles were enrolled by the earlier
prototype, so no production migration was needed for this change.

## Verification and remaining integration

`RewardResultTests` independently exercises rule/identity/time/count validation.
`RewardApprovalTests` uses unique, owned temporary profile trees for the complete
candidate → decision → approval → payment → retry path, unchanged unrelated
profile data, restore mismatch, invalid inputs, interruption, stale/missing history,
source conflicts, locked files and linked paths. Existing native-load tests remain
part of the suite. No fixture reads a real player's profile.

Final run on 2026-10-24: **145/145 new tests passed** (97 result rules, 48 approval
and integration cases), **283/283 reward tests passed**. The full suite passed
**2,609 tests**, with zero failures and the one existing environment-gated APBS
native-pipeline test skipped. The unified runner's installed-artifact hash checks
passed. TRX: `artifacts/run-arena-verification/run-arena-verification.trx` (ignored).

Two fault-injection checks ran in an isolated generated test project, without
editing the production sources or loading native profiles: removing victim
deduplication failed its duplicate-victim case; accepting deadline events failed
all three score-deadline cases. The same project's positive baseline passed all
97 result tests. Local evidence is under `artifacts/rewards-authority/mutants/`.
Independent review also corrected the real native-end token format, the separate
solo/team Tactical deadline ordering, environmental TDM death scoring and missing
decision-history detection before this final run.

Still required before player use:

- Explicit opt-in and safe ordinary-profile enrollment, policy selection and
  server-issued run sequencing.
- Real ready-roster/life capture and client event reporting with the exact rules
  above, bounded active-time accounting, and reliable final-report publication.
- Positive protected snapshot/restoration observation and durable read-back;
  separately bound transfer from a private session to the ordinary authority store.
- Native copied-profile startup/mail-collection testing and user-facing pending,
  rejected and paid status. Payout remains at the next real ordinary server start.
