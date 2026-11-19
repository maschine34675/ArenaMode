# Rewards paused at start: native checks (2026-11-19)

Source: branch `feature/fail-soft-startup-v070` at `3d044be6` (on top of master `6e9a7f15`).
Policy: [Startup failure policy](../../REWARDS-RUNTIME.md#startup-failure-policy) and
[Rewards paused at start](../../REWARDS-RUNTIME.md#rewards-paused-at-start).

## 1. Normal server with an unsettled session of another folder (VM)

- `D:\SPT41\SPT_Runtime` (10.3 GB, without `user\logs`) was copied with robocopy `/E /XJ` to
  `D:\SPT41-Backups\nativetest-failsoft-20261119\SPT_Runtime`, a folder Syncthing does not share.
  The copy carried the dev install's 46 finished Arena sessions; every lease names another folder.
- Staged failure: `completion.json` of session `52600b4b…` (closed, lease root
  `C:\SPT41\SPT_Runtime`) was moved out of the copy. That session is then unfinished and foreign:
  `offline-history-binding-mismatch`. On master `6e9a7f15` this stops SPT (the recovery throws and
  `OnLoadAsync` let it escape; pinned offline by `RewardOfflineMovedInstallTests`). No master build
  was run natively.
- The server DLL `0.6.0+3d044be6` was put into the copy only; the server ran from the copy, server only,
  port 6969 free.

| Run | Records | Result |
|---|---|---|
| Paused | staged as above | Listening on 6969 after 31 s. Exactly one ArenaMode Warning: "Arena rewards are paused for this start: the last Arena session could not be settled (offline-history-binding-mismatch: the session was recorded by another installation folder). SPT starts normally. No reward was paid, ...", followed by the full remedy that names both folders. `user\arena-rewards\rewards-paused.json` was written with `reason offline-history-binding-mismatch`, `recoveryPending true` and the pid and start time of that very server process. The reward tree changed by that one file only (913 files hashed before and after, lock files excluded); no profile file changed (SPT rotated its own backup folder). |
| Retried | `completion.json` put back | Listening after 25 s. The note was removed. The startup pass ran as in the moved-install report: the main profile's old reward records were archived (`...archived-20261119T132825Z-c5ae249e`) and it enrolled again ("Startup reward: Deferred"), the two test profiles kept their existing quarantine warning. |

Raw output: `artifacts/failsoft-native-20261119/` (gitignored): `server-paused.out.txt`,
`server-retried.out.txt`, the copy's server log `spt-copy-SPT-VM-20261119.log`. The copy was removed
afterwards.

## 2. Private Arena session round trip with the new helper (GPU-2)

- Installed into `D:\SPT41` (Syncthing to both hosts; no other session ran): client, helper
  (`maschine-ArenaMode.Session.exe`), BigBrainCompat and server, all `0.6.0+3d044be6`. Backup of the
  release files: `D:\SPT41-Backups\failsoft-v070\20261119-143136`. Redeploy Core unchanged (1.0.24).
- GPU-2 has no whitelist conflict, so Arena would run in the normal session without the helper. A
  temporary user file `BepInEx/config/com.maschine.ArenaMode.whitelist.json` denying
  `com.maschine.ModProfiler` forced the private session; it was removed afterwards.
- START ARENA · RESTART: the helper entered in 19 s (`enter finished`, arena server on 6970 after
  7.1 s), the game came up with `-arena-session` and without ModProfiler. The new local check for an
  unsettled earlier session passed silently (no pointer before the entry).
- RETURN TO SPT · RESTART: the normal server accepted connections after 11.2 s, the helper found the
  session settled (no paused note, no Warning in `helper.log`), started the game and finished. Session
  `1d79c891…` has `closed.json` and `completion.json`, `offline-active.json` is gone, no
  `rewards-paused.json` exists. The normal server log has no ArenaMode Warning; the startup pass logged
  "Startup reward: AlreadyPaid (reward-inbox-already-imported)" and "ClosedWithoutPayment" at Debug.
- Profile SPT-GPU-2 (`6ab7db1c…`) against SPT's backup taken at the first start of the test: two
  timestamps differ (`Health/UpdateTime`, `Hideout/sptUpdateLastRunTimestamp`), nothing else.
- Afterwards EFT and the normal server were stopped (both were off before) and the release files
  `0.6.0+72273354` / `0.6.0+39dfcf77` restored and confirmed identical on host 2.

Raw output: `artifacts/failsoft-native-20261119/host2-helper-20261119.log`, `host2-profile-diff.txt`.

## Not covered natively

- A return whose normal server pauses its recovery (the helper keeps the server, opens the game and
  shows the "rewards paused" dialog), and an entry refused because an earlier session is unsettled.
  Both are covered offline in `tools/OfflineSessionChecks/HelperTests.cs` against a real stand-in
  server process and the real `RewardPausedNote` writer.
- A Tarkova Cola recovery failure, a file error that persists through the three attempts, and the
  hard stops (live private process, held locks). Covered by the xunit suites and
  `tools/OfflineStartupChecks` (10 mutations).
- No Arena raid was played; the round trip carried no reward result.

## Offline verification

`tools/Run-ArenaVerification.ps1 -ChangedSince 6e9a7f15` on `3d044be6`: 54 of 54 steps passed;
xunit 4,160 tests, 4,157 passed, 3 environment-gated skips.
