# Repository consolidation — 2026-10-11

User-authorized scope: merge all outstanding ArenaMode branches, remove obsolete
worktrees/branches and push `master` to the existing private `dev` remote.
No other repository, game installation, configuration or profile was changed.

## Integration

Starting `master`: `5dde3d239f285afd574ad7fc317d743ace9231ad`.
All feature code was already integrated. The only missing commit was `ac5803d`,
containing the final historical Molotov report, deployment JSON and handoff note.
Merge `1c34469` preserves it and clarifies the obsolete separate-branch advice:
the prototype code had already entered `master` through `de25153`.

The historical native run still applies to `bfdf327`, not the subsequently
deployed cleanup build `f3183a2`. This consolidation adds no gameplay acceptance.
Independent merge review found no lost facts or source/asset changes. Only
documentation and historical deployment evidence changed, so no rebuild,
deployment or new raid was needed or performed.

## Removed branches and worktrees

Each original tip was checked as an ancestor of the merged `master` before
removal. All branch deletions used `git branch -d`; no forced deletion or reset.

| Removed branch | Original tip | Removed worktree |
|---|---|---|
| `feature/additional-announcer-lines` | `da22be1` | `D:\SPT41\Development\ArenaMode-VoiceLines` |
| `feature/bay5-map-integration` | `895d86a` | none |
| `feature/equator-map-integration` | `8c05327` | none |
| `feature/menu-polish` | `2e31ada` | `D:\SPT41\Development\ArenaMode-zombie` |
| `feature/molotov-prototype` | `ac5803d` | `D:\SPT41\Development\ArenaMode-molotov` |
| `feature/zombie-prototype` | `5a6c609` | none |
| `fix/announcer-pickup-followup` | `42a9782` | none |
| `fix/announcer-prepare-cutoff` | `4bca31c` | none |
| detached HEAD, no branch | `fcfcd71` | `C:\Users\Administrator\Documents\Codex\ArenaDoubleJump` |

Only `master` and `D:\SPT41\Development\ArenaMode` remain. The remote had no
obsolete feature branches; `dev/master` remains the publication target. The
earlier dated handoff entries saying “no push” describe their own delivery steps,
not a restriction on the user-authorized consolidation push.

## Recovery and local files

Local archive, deliberately outside the repository and removed worktrees:

`D:\SPT41\Development\_archives\ArenaMode-worktrees-20261011-212002`

It contains a verified complete Git bundle, the original branch/worktree inventory
and five ZIPs with per-file SHA256 manifests. All 16,559 ignored files from the
four auxiliary worktrees were preserved, including build products, native test
evidence and negative-control fixtures. ZIP entries were read back and hashed;
each live worktree's HEAD, status, ignored file list and hashes were checked again
immediately before its normal, non-forced `git worktree remove`.

The 15 untracked files under `ArenaMode.Client/Assets/ArenaMode/molotov/` were
also backed up, remain byte-identical in the main checkout and were neither
committed nor pushed. Their provenance review remains separate work.

For a historical absolute path under one of the removed worktrees, its ignored
file is recoverable at the same relative path in the matching ZIP; tracked files
remain in Git. See the archive's `README.md` and `baseline.json` for recovery.
Cleanup verification scripts and their gate evidence are retained locally under
`.unlazy/git-cleanup-20261011/`.
