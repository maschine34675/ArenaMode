# Branch and worktree collection - 2026-11-20

Request of the user on 2026-11-20: merge every ArenaMode branch and worktree that
still exists except the Fika one, delete them, and push `master`. This record
covers the merging, the removal, what was kept of the local evidence, the checks
on the merged `master` and the push. Nothing was built for installation and
nothing was deployed. The earlier collections of the same kind are
[WORKTREE-CLEANUP-20261113.md](WORKTREE-CLEANUP-20261113.md) and
[WORKTREE-CLEANUP-20261025.md](../2026-10/WORKTREE-CLEANUP-20261025.md).

`dev/master` (GitHub) stood at `0d596980` before. `master` was pushed with the
commit that adds this record: the 38 commits from `0d596980` to `02511f69` and
that commit.

## Merged

One branch still carried commits that `master` did not have:

| Branch | Commits | Merge | Note |
|---|---|---|---|
| `fix/reward-history-foreign-entry` | `da490c56`, `6f3041b0`, `91136045` (on `e57c85fe`) | `bc1d912f` | A file in the reward history that this installation did not write is named instead of being quarantined as an invalid result ([report](REWARD-HISTORY-CONFLICT-20261120.md)). One conflict, `docs/README.md`: both sides had added an entry at the top of the 2026-11 list; both are kept, `master`'s first. The heading count of the Reports section had drifted in earlier merges (301 for 305 entries on `master`) and was set to the number of entries the section lists. |

Every other branch was already contained in `master` (`git rev-list
master..<branch>` empty) and was only deleted. Most of them had been merged by
their own sessions between 2026-11-15 and 2026-11-20.

## Removed branches

`git branch -d` (the safe form, which refuses an unmerged branch) for all of
them. The tips stay reachable from `master`.

| Branch | Tip | Worktree that was removed with it |
|---|---|---|
| `codex/mod-refresh-20261120` | `08a09104` | `ArenaMode-mod-refresh` |
| `codex/movement-transitions-20261115` | `99f9bd69` | (none) |
| `codex/translocator-20261115` | `872a4ae3` | (none) |
| `codex/zombie-flow-20261115` | `5be2fd3f` | (none) |
| `feature/difficulty-model` | `f5a5a52e` | (none) |
| `feature/fail-soft-startup-v070` | `a056694c` | (none) |
| `feature/moved-install-v070` | `55141ddb` | (none) |
| `feature/opening-rounds-zombie-waves` | `7479c07b` | (none; `ArenaMode-opening` had moved on to the two branches stacked on it) |
| `feature/pap-save-pipeline-v070` | `675bf772` | (none) |
| `feature/verification-speed-2` | `3a587217` | (none) |
| `feature/verification-speed-3` | `00154342` | `ArenaMode-verifyspeed` |
| `feature/zombie-audio-container-guard` | `e57c85fe` | `ArenaMode-opening` |
| `feature/zombie-profile-supply` | `5aabf15c` | (none) |
| `fix/reward-history-foreign-entry` | `91136045` | `ArenaMode-rewardhistory` |

## What remains

- `master` in the primary checkout `D:\SPT41\Development\ArenaMode`.
- `feature/fika-admission` (`a8e9c2af`, unmerged, left out at the user's request)
  in its worktree `D:\SPT41\Development\ArenaMode-fika-m1`.
- The verification runner's six worker checkouts `ArenaMode-vw1` to `-vw6`
  (detached, kept identical to the checkout under test by the runner).
- No stash.
- Not part of this collection: the sibling Redeploy checkout. Its `main`
  (`ecf5659`) is three commits ahead of `origin/main`, among them Core 1.0.25
  (`430657c`), which the optional-mod refresh on this `master` names for
  VisualAssist; its branch `codex/visualassist-20261120` (contained in `main`)
  and the worktree `Redeploy-visualassist` still exist. Until Redeploy is
  pushed, the published Redeploy `main` builds Core 1.0.24, so the published
  ArenaMode `master` describes a Core that is not published yet.

## Preserved local evidence

Every worktree was clean (no tracked change, no untracked file that is not
ignored) before it was removed. Its ignored files were copied into the primary
checkout under `artifacts/archived-worktrees/`, each copy compared with its
source by SHA-256, with an `archive-manifest.json` (original path, branch,
commit, and per file the relative name, length and hash). As in the collection
of 2026-11-13, reproducible build caches were left out: every `obj` folder, the
`bin` folders of the harness projects under `tools/`, `.vs` and `__pycache__`.
The `bin` folders of the mod projects and everything under `artifacts/` and
`build/` are in the archives.

| Archive | Worktree | Branch | Commit | Files | Bytes | Build-cache files left out |
|---|---|---|---|---:|---:|---:|
| `mod-refresh-20261120` | `ArenaMode-mod-refresh` | `codex/mod-refresh-20261120` | `08a09104` | 1,320 | 198,593,602 | 328 |
| `opening-20261120` | `ArenaMode-opening` | `feature/zombie-audio-container-guard` | `e57c85fe` | 2,603 | 369,391,704 | 2,761 |
| `rewardhistory-20261120` | `ArenaMode-rewardhistory` | `fix/reward-history-foreign-entry` | `91136045` | 191 | 29,917,632 | 40 |
| `verifyspeed-20261120` | `ArenaMode-verifyspeed` | `feature/verification-speed-3` | `00154342` | 1,034 | 74,366,897 | 84 |

Total: **5,148 files / 672,269,835 bytes**. A document of one of these sessions
that names an ignored `artifacts/<name>/` folder means the folder under
`artifacts/archived-worktrees/<archive>/artifacts/<name>/` now, for example
`opening-20261120/artifacts/zombie-supply-20261120/`,
`opening-20261120/artifacts/audio-occluder-20261120/` and
`opening-20261120/artifacts/opening-rounds-20261119/`. The archives stay
ignored and local; they must not be bundled or pushed.

## Checks on the merged `master`

Offline, through `tools/Run-ArenaVerification.ps1 -ChangedSince dev/master` (no
deployment; every step compares the installed files before and after), which
selected 110 of the 173 registered steps plus the xunit suite.

- First run, on the merge `bc1d912f`: 107 steps passed, 4 failed (the suite and
  three wrappers that run the whole suite themselves), each of them with this
  test of the suite:
  `PitFireTeamPersistenceGuardTests.InstalledPitFireTeamContractAcceptsEveryTransactionTargetOfItsBuild`,
  "Expected one installed target for ...RegisterProtectedRaidItems, found 0".
  Not a result of the merge: the test resolves its own copy of the PitFireTeam
  transaction contracts in whichever server build is installed, `6645c27d` had
  moved the production table to the 1.1.2 layout without that copy, and 1.1.2
  has been the installed build on this machine since the optional-mod refresh
  (the refresh's own run of 15:14 passed this test, which no run against
  the installed 1.1.2 can, so it had still seen an older installed build).
  Inside one of the wrappers (`IcebergTeamBoxChecks`) two tests of
  `OfflineSessionHelperTests` failed as well, the group that fails
  sporadically under load. The second run overwrote the per-step logs of this
  one; what was read from its `xunit.log` before that is written down in
  `first-run-xunit-failure.txt` next to the run logs.
- Repaired in `02511f69` (test only, no production change): the test mirrors
  `PitFireTeamVersions.ExplicitRosterVersion` (the registration returning
  `string[]`, the two new private raid transactions, 13 instead of 11 raid
  targets). Against the installed 1.1.2: all 108 PitFireTeam tests pass.
  Negative control: with the `BeginCompanionRaid` contract removed from the
  production table the test fails at `AssertHarmonyPrefix`; the file was
  restored byte for byte.
- Second run, on `02511f69`: all 111 steps passed (110 harness steps and the
  suite, none taken from the cache); xunit 4,425 tests, 4,422 passed, 3 known
  environment skips. The installed files were unchanged after every step.
- With this record and the document corrections in the working tree, the 19
  steps that read the document index (`docs/README.md`) were run again
  (`-SkipUnitTests -ChangedSince 02511f69`): all passed.
- The runner reported `tools/Verify-ArenaEndPersistence.ps1` as changed and not
  registered (`08b1bb88`). It is a wrapper that runs a filtered part of the
  suite; the suite step runs those tests.
- A read-only review of `bc1d912f` by three readers, each serious finding read
  again by a second one:
  - Reward code of both sides (the Cola 3.0.1 catalog of `d7681d4b`, the
    foreign history entry of `da490c56`): no conflict. One note, not changed
    here: a decision in the history whose Cola catalog this build does not
    know fails `ReadDecisions` with `cola-delta-version`, which is not one of
    the history reasons of `RewardUnavailableText.IsHistoryReason`, so it is
    reported as temporarily unavailable and logged at Debug only. It needs a
    receipt of a newer or foreign build to occur and damages nothing.
  - What the push publishes (37 commits, 129 files, about 7,100 added lines at
    the time of the review): no credential, no personal data, no binary or
    evidence dump, one author identity throughout.
  - The merged documents: index, counts and links were right; nothing was
    doubled or dropped. Found and corrected with this record: branch
    statements in [HANDOFF.md](../../HANDOFF.md) that the merges had made false
    ("local commits only", "not merged", "not pushed" for work that is on
    `master`), one "see the next entry" that pointed at the wrong entry after
    the merge, and one branch name in [TRAPS.md](../../TRAPS.md).

The logs of the runs, the list of branch tips and the review's findings are
under the ignored `artifacts/master-collect-20261120/`.
