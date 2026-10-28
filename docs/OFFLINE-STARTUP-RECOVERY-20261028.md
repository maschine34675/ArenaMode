# Lost private session data at normal startup

A stopped Arena session whose disposable private marker, lease or signed choice
has disappeared or become malformed no longer permanently blocks normal SPT.
Normal startup discards that session's rewards, keeps the ordinary profile intact,
and continues. It prints one short notice after cleanup. There is no repair dialog.
An unknown last private choice becomes rewards OFF; the player may enable future
rewards again in the existing Arena menu.

## Boundary

The automatic path requires both ordinary startup/lifetime locks, the exact valid
ordinary lease and activation record, and positive exit/PID-reuse evidence for
the private process. It runs before native profiles load and before any closure
plan exists. Only the three disposable metadata reads can trigger this path.
Temporary file locks, access failures, links, malformed ordinary authority,
missing activation and contradictory session history still refuse recovery.
Existing closed imports and real Tarkova Cola transactions retain their rules.

Windows process probing uses limited query and synchronization rights on one
handle. Failed queries remain errors, not evidence of exit. Missing directory
attributes now raise their actual missing-path error instead of reporting a
nonexistent directory as a junction; existing ancestors are checked first.

## Durable discard and replay

`offline-sessions/<id>/discarded.json` is an immutable discard plan bound to the
lease and activation hashes, with the before/after consent records. It is not
restoration evidence. After OFF is written and read back,
`discard-completion.json` commits that plan's hash, then the active pointer is
removed. No `closed.json`, successful-import marker, payment or profile replacement
is invented. Lost pointers reconstruct unfinished discard plans; completed history
does not undo a later explicit ON choice.

The session delegation remains permanently rejected. Valid signed receipts that
reappear are retained and skipped before allocation, including during preflight
and automatic reward review. Unexpected allocation/import history is not waived.
Checkpoints from that delegation cannot publish new receipts. The exact retained
checkpoint/run binding also rejects old candidate/approval fallback payments.
Other sessions retain their usual reward processing.

The helper accepts completed discards as terminal, verifies pointer retirement
and private-process absence before rebuilding, and skips the discarded outbox
only for that exact tree. A missing private lease may use a fully matching marker
to find its original lease. If both bindings are unusable it cannot select an
arbitrary historical session from the reused directory. A stopped tree with a
lost marker can reach ordinary startup through direct-start cleanup; cleanup
never kills a process based on that damaged marker.

## Verification

The focused tests cover missing roots/markers/leases/state, malformed signatures,
all discard write boundaries with lost pointers, later ON, live-process ownership,
locked/linked paths, old receipt and candidate/approval replay, and a new healthy
session payment. Helper checks cover the actual serialized server records,
incomplete/changed discard records, old outboxes, missing marker/lease and early
direct-start cleanup. Windows checks exercise live/missing/reused PIDs and an
exited process whose numeric exit code is 259.

Final verification on 2026-10-28: 379 focused tests passed; the full suite passed
3,394 tests with three expected environment skips. The two historical recovery
fixtures skipped in that full run were then supplied explicitly: all 56 recovery
engine tests passed, including both original real-data cases. The third skip
requires a separate APBS installation. The shipping .NET Framework helper built
without warnings or errors. Three injected faults failed their named assertions;
the restored production baseline passed all nine selected cases.

Run `tools/OfflineStartupChecks/Verify.ps1` with `-Stage Focused`, `Helper` and
`Regression`; `Mutations.ps1` injects three faults into copied production sources
and requires named test failures plus a passing restored baseline. All builds
disable deployment. Detailed ignored results are in
`artifacts/offline-startup-checks/` and `artifacts/offline-startup-mutations/`.
These are fixture/native-API checks, not a new full game session or GPU deployment.
Installed binaries, ordinary profiles and Syncthing settings were not changed.
Deploy the server and helper together; older helpers do not understand the new
terminal discard records.
