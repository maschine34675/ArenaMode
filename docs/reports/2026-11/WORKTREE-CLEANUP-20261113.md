# Branch and worktree collection - 2026-11-13

Request of the user on 2026-11-13: merge every ArenaMode branch and worktree
except the old Fika branch, delete them, and deploy `master`; the same for
Redeploy. This record covers the merging, the removal and what was kept of the
local evidence. What was deployed afterwards is recorded in
[HANDOFF.md](../../HANDOFF.md) and in Redeploy's `docs/REPOSITORY-STATUS.md`.
The earlier cleanup of the same kind is
[WORKTREE-CLEANUP-20261025.md](../2026-10/WORKTREE-CLEANUP-20261025.md).

Nothing was pushed. `dev/master` stands at `99260275`; Redeploy's `origin/main`
at `b1d69e1`.

## Merged

Two branches still carried a commit that `master` did not have:

| Branch | Commit | Merge | Note |
|---|---|---|---|
| `fix/equator-map-checks` | `2192ebe8` | `727e6802` | `ArenaDirectStartTests.Dispose` retries the delete of its probe copy for up to ten seconds; this is the cause of the sporadic failure of `AlreadyStartingServerIsReusedAndForeignImageIsRefused` that later contracts list as a known flake. No conflict. |
| `feature/launcher-thumbnail` | `a1030869` | `1a776650` | The session helper's compact profile picker shows the ArenaMode emblem. Conflicts in `CHANGELOG.md` and `docs/HANDOFF.md` (both sides had added an entry at the top of the same list); both entries are kept, `master`'s first. |

Every other branch was already contained in `master` (`git rev-list
master..<branch>` empty) and was only deleted.

## Removed branches

`git branch -d` (the safe form, which refuses an unmerged branch) for all of
them. The tips stay reachable from `master`.

| Branch | Tip | Worktree that was removed with it |
|---|---|---|
| `compat/four-mods-20261113` | `9f5b51d3` | `ArenaMode-four-mods` |
| `feature/chop-shop` | `99260275` | (removed on 2026-11-12) |
| `feature/kit-picker` | `79d1c58f` | `ArenaMode-kit-picker` |
| `feature/launcher-thumbnail` | `a1030869` | (was checked out in the primary checkout) |
| `feature/spawn-rows` | `7d47f03b` | (removed earlier on 2026-11-13) |
| `fix/equator-map-checks` | `2192ebe8` | `ArenaMode-equator-fix` |
| `fix/extraction-isolation-gate` | `327d8854` | `ArenaMode-extract-gate` |
| `fix/reward-zero-round-final` | `87d2946a` | `ArenaMode-reward-zero-round` |
| `fix/solo-squad-dispatch-order` | `28de1529` | `ArenaMode-squad-order` |
| `fix/spare-magazine-fill` | `8fe9891f` | `ArenaMode-spare-fill` |
| `fix/tracer-checks-population-policy` | `80c75d86` | `ArenaMode-tracer-checks` |

Also removed: the worktree `ArenaMode-ui-whitelist`, which had held `master`,
and the two detached build worktrees `_squadorder-build/ArenaMode` (`ba06ac49`)
and `_squadorder-build/Redeploy` (`b1d69e1`) with their folder.

## What remains

- ArenaMode: `master` in the primary checkout `D:\SPT41\Development\ArenaMode`,
  and `feature/fika-admission` (`a8e9c2af`, unmerged, left out at the user's
  request) in its worktree `D:\SPT41\Development\ArenaMode-fika-m1`.
- Redeploy: `main` only (`f092f98`, three commits ahead of `origin/main`), one
  checkout. It had no branch to merge.
- No stash in either repository.

## Preserved local evidence

Every worktree was clean (no tracked change, no untracked file that is not
ignored) before it was removed. Its ignored files were copied into the primary
checkout of its repository under `artifacts/archived-worktrees/`, each copy
compared with its source by SHA-256, with an `archive-manifest.json` (original
path, branch, commit, and per file the relative name, length and hash).
Unlike the cleanup of 2026-10-25, reproducible build caches were left out:
every `obj` folder, the `bin` folders of the harness projects under `tools/`,
`.vs` and `__pycache__`. The `bin` folders of the mod projects (the binaries
that were tested or installed) and everything under `artifacts/` and `build/`
are in the archives.

| Repository | Archive | Worktree | Branch | Commit | Files | Bytes | Build-cache files left out |
|---|---|---|---|---|---:|---:|---:|
| ArenaMode | `equator-fix-20261113` | `ArenaMode-equator-fix` | `fix/equator-map-checks` | `2192ebe8` | 290 | 57,938,157 | 632 |
| ArenaMode | `extract-gate-20261113` | `ArenaMode-extract-gate` | `fix/extraction-isolation-gate` | `327d8854` | 2,919 | 168,702,405 | 2,661 |
| ArenaMode | `four-mods-20261113` | `ArenaMode-four-mods` | `compat/four-mods-20261113` | `9f5b51d3` | 2,164 | 163,420,820 | 383 |
| ArenaMode | `kit-picker-20261113` | `ArenaMode-kit-picker` | `feature/kit-picker` | `79d1c58f` | 3,406 | 202,963,460 | 3,548 |
| ArenaMode | `reward-zero-round-20261113` | `ArenaMode-reward-zero-round` | `fix/reward-zero-round-final` | `87d2946a` | 562 | 40,814,738 | 70 |
| ArenaMode | `spare-fill-20261113` | `ArenaMode-spare-fill` | `fix/spare-magazine-fill` | `8fe9891f` | 175 | 28,394,571 | 120 |
| ArenaMode | `squad-order-20261113` | `ArenaMode-squad-order` | `fix/solo-squad-dispatch-order` | `28de1529` | 460 | 40,414,723 | 952 |
| ArenaMode | `squadorder-build-20261113` | `_squadorder-build/ArenaMode` | detached | `ba06ac49` | 98 | 29,497,624 | 35 |
| ArenaMode | `tracer-checks-20261113` | `ArenaMode-tracer-checks` | `fix/tracer-checks-population-policy` | `80c75d86` | 2 | 9,058 | 25 |
| ArenaMode | `ui-whitelist-master-20261113` | `ArenaMode-ui-whitelist` | `master` | `379a1878` | 6,654 | 457,823,897 | 5,596 |
| Redeploy | `squadorder-build-20261113` | `_squadorder-build/Redeploy` | detached | `b1d69e17` | 126 | 37,942,041 | 17 |

Total: **16,856 files / 1,227,921,494 bytes**. A document of one of these
sessions that names an ignored `artifacts/<name>/` folder means the folder
under `artifacts/archived-worktrees/<archive>/artifacts/<name>/` now, for
example `four-mods-20261113/artifacts/four-mods-20261113/`. The archives stay
ignored and local; they must not be bundled or pushed.

For two worktrees (`ArenaMode-four-mods`, `ArenaMode-ui-whitelist`) git removed
the registration but stopped deleting the folder at a path that was too long
for it. What was left was classified file by file before it was deleted with
long-path support: tracked in the worktree's commit, present in the archive,
or one of the build caches named above; no file fell outside these three.

## Checks on the merged `master`

Offline, through `tools/Run-ArenaVerification.ps1` (no deployment; every step
compares the installed files before and after):

- xunit on `1a776650`: 3,907 tests, 3,904 passed, 3 known environment skips.
- Harnesses of the day's merges, passed: `SessionMenuArtChecks`,
  `ArenaSessionHelperNet472Checks`, `SoloTacticalSpawnChecks`
  (`Verify-Regression.ps1`), `TacticalPolishChecks`, `EncumbranceBarChecks`,
  `ShoulderSwapChecks`, `MapModeAdmissionChecks`, `ArenaExtractionChecks`,
  `SainExtractGuardChecks`, `ArenaTracerChecks`, `NativeMenuChecks`,
  `AreaConfigChecks`.
- `OptionalModAdmissionChecks` did not compile on `master`: the four-mod
  admission had added `ArenaShoulderSwapCompat` and `ArenaEncumbranceBarCompat`
  to the chain in `ArenaOptionalMods`, and this harness, which compiles that
  file against stand-ins, did not know them. Repaired in `379a1878` (stand-ins,
  assertions for the two providers' place in the chain, three negative
  controls): 78 assertions, 18 negative controls. No production change.
- The other harnesses that read a production file changed by the day's merges,
  passed: `AnnouncerCombatChecks`, `ArenaInfiniteAmmoChecks`,
  `ArenaKillcamChecks`, `ArenaPickupChecks`, `ArenaSessionClientChecks` (174
  mutants), `CrowdReactionChecks`, `FailedStartAbortChecks`,
  `FriendlyFireRuntimeChecks`, `PitFireTeamChecks`, `SoloPopulationChecks`,
  `SurgicalWalkChecks`, `TacticalCorpseChecks`, `TeamArenaExpansionChecks`.
- `PitFireTeamRoundLifecycleChecks` stopped after 149 passed assertions at the
  reference tree `_reference\SPT-PitFireTeam-beta-0.10.0`, which this machine
  no longer has (the handoff names that gap); not a result of the merges.
- A read-only review of the two merges and the harness repair found no defect.
  It found one older stale anchor outside this work (`CustomsEastAreaChecks`,
  fault `menu-missing`, label renamed on 2026-10-26), filed as a separate task.

The logs of both runs and the list of branch tips are under the ignored
`artifacts/master-collect-20261113/`.
