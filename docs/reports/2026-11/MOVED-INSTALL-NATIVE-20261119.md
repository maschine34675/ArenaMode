# Moved installation: native server start before and after the fix (2026-11-19)

Source: branch `feature/moved-install-v070` at `eb7423bd` (fix commits `d469119f`, `53e3d2ed`,
`eb7423bd` on top of master `dda1d4a6`). Report: GitHub maschine34675/ArenaMode#3.

## Setup

- On the VM, `D:\SPT41\SPT_Runtime` (10.3 GB, without `user\logs`) was copied with robocopy
  `/E /XJ` to `D:\SPT41-movedtest\SPT_Runtime`, a folder Syncthing does not share. The copy
  carried the dev install's real Arena history: 46 session folders under
  `user\arena-rewards\offline-sessions`, every one finished (closed and completed, or a completed
  discard), no `offline-active.json`. Every lease names `D:\SPT41\SPT_Runtime` as its root.
- The SPT server was started from the copy (`SPT.Server.exe`, working directory = the copy),
  server only, no game, port 6969 free on the VM. The live install was not changed: its server DLL
  stayed `0.6.0+72273354`, and no file under its `user\arena-rewards` or `user\profiles` changed
  during the runs.
- Raw output: `artifacts/moved-install-native-20261119/` (gitignored): the console output of both
  runs and the copy's server log `spt-SPT-VM-20261119.log`.

## Results

| Run | Server DLL in the copy | Result |
|---|---|---|
| Baseline | `0.6.0+72273354` (same recovery code as v0.6.0) | Startup stopped after 47 s: "Offline Arena recovery is incomplete; normal startup stopped without replacing a profile." from `RewardOfflineSession.RecoverBeforeStartup` in `ArenaRewardStartup.OnLoadAsync`, followed by SPT's Critical "The server has unexpectedly stopped". This is the report's failure. |
| Fix | `0.6.0+eb7423bd` | The server listened on 6969 after 28 s. The player profile `6a6f5c04…` logged "the installation was moved or copied, and no reward was unsettled, so its old reward records were archived at `user/arena-rewards/6a6f5c046536d896fc7500bd.archived-20261119T091631Z-8492bd93`", followed by "Startup reward: Deferred (rewards-enrolled-no-pending-result)". No `offline-active.json` was written, and the 46 history folders stayed as they were. |

Two test profiles of the dev install got the existing quarantine warning instead of the archive:
`6ab7db11…` ("the profile's ledger and the journal disagree") and `6ab7db1c…` (an unrecognised
`candidate.sync-conflict-…` file, left by Syncthing in its reward folder). Both say "Nothing is
paid and nothing is deleted. Arena sessions still work." This is the designed behaviour for a
moved folder whose records are not clean, and SPT started anyway. No other ArenaMode warning or
error appeared in the fix run.

## Not covered

- The helper side (the first private Arena session after a move, with the kept private tree
  naming the old folder) was not run natively; it is covered by the offline tests in
  `tools/OfflineSessionChecks/HelperTests.cs`, including a real Build over a kept tree with a
  dangling junction into the moved-away folder.
- An unfinished foreign session was not staged natively; the offline tests in
  `ArenaMode.Tests/RewardOfflineMovedInstallTests.cs` cover it.
- The test copy was removed afterwards; the empty folder `D:\SPT41-movedtest` is left (the shell
  tooling refused to remove a drive-root folder).

## Offline verification

`tools/Run-ArenaVerification.ps1 -ChangedSince dda1d4a6` on `eb7423bd`: 53 of 53 steps passed;
xunit 4,102 tests, 4,099 passed, 3 environment-gated skips.
