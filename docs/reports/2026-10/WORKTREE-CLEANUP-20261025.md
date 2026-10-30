# Worktree cleanup — 2026-10-25

All feature commits were already reachable from the primary branches. There
were no pending source changes, stashes or remote feature branches to integrate.
Both primary branches were pushed without force before removing local references.
Release and archive tags were retained; no release or deployment was performed.

| Repository | Removed branch | Preserved tip |
|---|---|---|
| ArenaMode | `compat/radar-crosshair-20261025` | `a6b23dd` |
| Redeploy | `compat/four-mods-20261025` | `2e8cb76` |
| Redeploy | `compat/magcheck-20261025` | `e3c2b36` |
| Redeploy | `compat/hud-20261025` | `9d1672b` |

The Arena `master` at `9231f16` and Redeploy `main` at `9d1672b` already
contained these revisions. The cleanup documentation adds no runtime changes.
Only each repository's primary branch and primary checkout remain.

## Preserved local evidence

All ignored files were copied, including exact candidate binaries, test reports,
native screenshots/logs and task ledgers. Every source and destination SHA-256
was checked before deleting the exact named worktree. Tracked source is preserved
in Git; root worktree `.git` pointer files were not copied.

Paths below are relative to each repository's primary checkout:

| Repository | Archive under `artifacts/archived-worktrees/` | Files | Bytes |
|---|---|---:|---:|
| ArenaMode | `compat-hud-20261025` | 702 | 268,041,377 |
| Redeploy | `compat-four-and-magcheck-20261025` | 985 | 118,138,737 |
| Redeploy | `compat-hud-20261025` | 606 | 89,290,784 |

Total: **2,293 files / 475,470,898 bytes**. Each archive has an
`archive-manifest.json` with its original worktree path, branch, commit, relative
file names, lengths and hashes. These archives remain ignored and local; they
must not be bundled or pushed. Existing primary-checkout artifacts and unrelated
repositories were untouched.

Verification used clean-source checks, local/remote commit ancestry, complete
archive hash comparisons and final branch/worktree inventories. No gameplay
tests were repeated for this repository-only cleanup.
