# Offline harness repairs — 2026-09-12

Test-only follow-up to the three failures reported after Redeploy stage 0.
Started from clean `b5ff258` on `feature/arena-air-control`; corrections are on
`fix/offline-harness-contracts`. No production, audio, config or GPU evidence edits.
The three harness directories were unchanged between `67d27ca` and `b5ff258`.
Failures were freshly reproduced on the current starting checkout; this task did
not repeat Claude's earlier temporary-worktree execution at `67d27ca`.

| Check | Cause | Verified result | Commit |
|---|---|---|---|
| PitFireTeamChecks | Source assertion still required two MarkArmed arguments; the current third argument freezes Deathmatch rules | 248 assertions, including canonical modes and arm-contract counterexamples | `14d7d56` |
| TeamArenaExpansionChecks | Required WARLORD CC wording after those recordings were replaced | 774 assertions, including current manifest/hash/loading scope and 13 in-memory negative controls; subsequent PFT/SAIN contracts run again | `f82f980` |
| TeamGiveUpChecks | Ambiguous local-death mutation plus two later stale terminal-guard targets | 618 baseline assertions, 16 intended behavioral failures, fresh 618 | `42d2400` |
| ArenaMode.Tests | No failing unit test; two fixtures were not enabled in the default environment | Default 617 passed / 2 skipped; installed fixtures enabled: 619 passed / 0 skipped / 0 failed | unchanged |

The skipped tests were `InstalledFactoryCandidateHasSeparatedGroundSpawns`
(SPT_DATABASE missing) and `InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline`
(ARENA_APBS_TEST_ROOT missing). The full run used the existing read-only APBS
verification script, which sets its fixture root, and supplied the installed
database to the child process. No permanent environment/config edit:

```powershell
$arenaPreviousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = 'D:\SPT41\SPT_Runtime\SPT_Data\database'
    pwsh -NoProfile -File tools/ApbsCompatibilityChecks/Verify.ps1 -SptRoot D:\SPT41
} finally { $env:SPT_DATABASE = $arenaPreviousDatabase }
```

Use the corresponding `C:\SPT41` paths on the GPU PC. The snippet restores the
caller's previous SPT_DATABASE value afterward. The verifier redirects
SPTUserPath to its ignored stage, so the server build used by the unit tests is
not installed. A bare dotnet test follows the server project's deployment target;
do not use it during a bound GPU test without that redirection. A freshly built
repo server artifact is not evidence that the installed server was replaced.

Detailed boundaries and commands are in each harness README. The PFT call matcher
is a lexical source contract, not execution of native loading. Its independent
review caught a mixed-duplicate-call gap; the final predicate counts all qualified
MarkArmed calls as well as the exact argument match and rejects that case.
The audio check confirms recorded provenance fields and fingerprint format, not
original-source ownership/hash, legal authority or audible speech. Existing cue
source checks remain. The separate Creator decoder verification passed 46 clips
and matched all 61 installed asset files. Historical license text is not treated
as a license for the replacement recordings.

The give-up mutation selects only the exact TDM block. The solo Deathmatch call
and all other source text remain intact. Missing, nested or ambiguous scopes
fail before compilation. The other two stale targets now address the shared
ArenaMatchResult guard while retaining their original behavioral assertions.
All 16 targets are prepared before any mutation run. Compilation errors never
count as detecting the intended fault. Independent review found no remaining
established defect in the three corrected harnesses.

Evidence under ignored artifacts:

- Default suite TRX/log and 73-file installed before/after inventory:
  `artifacts/harness-contract-repair/40005f7dcdc94d61bc269abeb28055c4/`.
- Full 619-test installed-fixture run:
  `artifacts/apbs-compat/tests/4eea724a122248588dcbc86e43c9bb9f/`.
- Give-up scratch copies remain under `tools/TeamGiveUpChecks/obj/giveup-regression-*`.

All 73 inventoried installed files stayed identical, including Arena client/server,
Redeploy Core/Client, current settings and packaged audio. The APBS verifier also
checks its installed DLL/config and companion inventory. No DLL was deployed and
no restart is needed for these corrections. Existing r6/refinements r4 GPU
preflight blockers remain unresolved: this test repair does not rebind those
instructions or certify the newer Redeploy/client build in a raid.
