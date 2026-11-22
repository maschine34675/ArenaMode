# Branch and worktree collection - 2026-11-22

Request of the user on 2026-11-22: merge every ArenaMode and Redeploy branch and
worktree, delete them, push, bring the documents up to date and create the GitHub
releases; on the side, lower Chop Shop's recommended participant count to six
without running every test again. This record covers the merging, the removal and
what was kept of the local evidence. The release itself is recorded in
[RELEASE-0.7.0.md](../../RELEASE-0.7.0.md) and [HANDOFF.md](../../HANDOFF.md). The
earlier collections of the same kind are
[WORKTREE-CLEANUP-20261120.md](WORKTREE-CLEANUP-20261120.md),
[WORKTREE-CLEANUP-20261113.md](WORKTREE-CLEANUP-20261113.md) and
[WORKTREE-CLEANUP-20261025.md](../2026-10/WORKTREE-CLEANUP-20261025.md).

`dev/master` (GitHub) stood at `a3959393` before; `master` stood at `e3d8b326`, nine
commits ahead (the documents note `3b999c07` of 2026-11-20 and eight commits of other
sessions between 2026-11-20 and 2026-11-22).

## Merged

| Branch | Commits | Merge | Note |
|---|---|---|---|
| `feature/yard` | `30892f9a`, `c071cb81`, `fb2b3af6`, `96a51cea`, `fc86818f` (on `6b3ba6a4`) | `a28d2a5a` | Block (`arena_yard`), ArenaMapPort's seventh map, in every mode ([contract](../../reviews/BLOCK-INTEGRATION-20261121.md)). Two conflicts, both in documents: `CHANGELOG.md` "Unreleased" and `docs/HANDOFF.md` "Released and installed" had new entries on both sides; both are kept, `master`'s first. `ArenaMode.Client/Config/Settings.cs` merged without a conflict. The solution builds with 0 errors (no deployment). |

Then, on `master`, `36b05655` lowers Chop Shop's participant recommendation from eight
to six (the owner's decision of 2026-11-22), as asked without the full verification
runner: the client and the server build with 0 errors, the 15 test classes that use the
participant policy or settings (525 tests) pass, and `ParticipantMenuChecks/Verify.ps1`
passes with its seven negative controls.

## Removed branches and worktrees

`git branch -d` (the safe form, which refuses an unmerged branch). The tips stay
reachable from `master`.

| Branch | Tip | Worktree that was removed with it |
|---|---|---|
| `feature/yard` | `fc86818f` | `D:\SPT41\Development\ArenaMode-yard` |
| `fix/dynamicmaps-130` | `026d4c44` (already contained in `master`) | `D:\ArenaMapPort\work\dynamicmaps130-impl\ArenaMode` |

Six more ArenaMode worktrees, `D:\ArenaMapPort\work\dynamicmaps130-impl\ArenaMode-vw1`
to `-vw6`, were the verification runner's workers of the DynamicMaps 1.3 session of
2026-11-20 (detached at `3b999c07`). Each carried the same ten changed tracked files the
runner had mirrored from the checkout under test; every one of them was compared with
`git hash-object` and equals the blob of `794453e7` or `026d4c44`, both in `master`, so
the workers were removed with `git worktree remove --force`. The Redeploy checkout has one
removed worktree, `D:\ArenaMapPort\work\dynamicmaps130-impl\Redeploy` (detached at
`32336f4`, clean). The folder `D:\ArenaMapPort\work\dynamicmaps130-impl\ArenaMode` itself
remains with empty sub-folders (no file) under `artifacts/deploy-layout-check-*`; the rest
of `D:\ArenaMapPort\work\dynamicmaps130-impl` (that session's evidence and scripts) belongs
to ArenaMapPort's work area and was not touched.

## What remains

- `master` in the primary checkout `D:\SPT41\Development\ArenaMode`.
- `feature/fika-admission` (`a8e9c2af`, unmerged, left out at the user's standing request)
  in its worktree `D:\SPT41\Development\ArenaMode-fika-m1`.
- The verification runner's six worker checkouts `ArenaMode-vw1` to `-vw6`.
- No stash. Redeploy: `main` only, one checkout, no stash.

## Preserved local evidence

Before removal the ignored files of every worktree were copied into
`artifacts/archived-worktrees/` of the primary checkout (Redeploy's into its own
`artifacts/archived-worktrees/`), each copy compared with its source by SHA-256, with an
`archive-manifest.json` (original path, branch, commit, the tracked changes at removal,
and per file the relative name, length and hash). As in the earlier collections,
reproducible build caches were left out: every `obj` folder, the `bin` folders of the
harness projects under `tools/`, `.vs` and `__pycache__`.

| Archive | Worktree | Branch / commit | Files | Bytes | Build-cache files left out |
|---|---|---|---:|---:|---:|
| `yard-20261122` | `ArenaMode-yard` | `feature/yard` `fc86818f` | 1,935 | 174,188,077 | 685 |
| `dynamicmaps130-20261122` | `dynamicmaps130-impl\ArenaMode` | `fix/dynamicmaps-130` `026d4c44` | 870 | 156,228,793 | 362 |
| `dynamicmaps130-vw1-20261122` | `dynamicmaps130-impl\ArenaMode-vw1` | detached `3b999c07` | 375 | 91,247,550 | 797 |
| `dynamicmaps130-vw2-20261122` | `dynamicmaps130-impl\ArenaMode-vw2` | detached `3b999c07` | 254 | 24,519,864 | 1,046 |
| `dynamicmaps130-vw3-20261122` | `dynamicmaps130-impl\ArenaMode-vw3` | detached `3b999c07` | 461 | 72,850,461 | 981 |
| `dynamicmaps130-vw4-20261122` | `dynamicmaps130-impl\ArenaMode-vw4` | detached `3b999c07` | 527 | 101,902,828 | 981 |
| `dynamicmaps130-vw5-20261122` | `dynamicmaps130-impl\ArenaMode-vw5` | detached `3b999c07` | 141 | 23,447,248 | 1,317 |
| `dynamicmaps130-vw6-20261122` | `dynamicmaps130-impl\ArenaMode-vw6` | detached `3b999c07` | 930 | 51,212,666 | 959 |
| Redeploy `dynamicmaps130-20261122` | `dynamicmaps130-impl\Redeploy` | detached `32336f4` | 1 | 281 | 0 |

ArenaMode total: **5,493 files / 695,597,487 bytes**. A document of the Block session
that names an ignored `artifacts/<name>/` folder (for example `artifacts/yard-20261121/`)
means `artifacts/archived-worktrees/yard-20261122/artifacts/<name>/` now. The archives
stay ignored and local; they must not be bundled or pushed.

## Push and release

`master` was pushed to `dev/master` with the release record commit `9091abad` (18
commits from `a3959393`), with the tag `v0.7.0` on the build commit `259d6dfb`; Redeploy
`main` was pushed to `origin/main` with `8f54f3c` (6 commits from `32336f4`), with the tag
`v1.1.4` on `50c3df0`. Both GitHub releases were created the same day. Before the push the
commits to be published were searched for keys, tokens, private addresses and binaries
(none) and carry one author identity. What was checked for the release is in
[RELEASE-0.7.0.md](../../RELEASE-0.7.0.md), "Package checks" and "Native release check":
the full offline verification register was not run on the release tree, at the owner's
request for the Chop Shop change; the package's isolated snapshot ran the unit test suite.
This record and the publication note in `HANDOFF.md` were pushed in a commit of their own.
