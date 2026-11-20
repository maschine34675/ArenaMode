# PitFireTeam 1.1.2 contracts and regressions

```powershell
pwsh -NoProfile -File tools/PitFireTeam112Checks/Verify.ps1 -ReleaseRoot <extracted-official-1.1.2-archive>
```

Without the parameter, the expected fixture is
`artifacts/pft-100-contracts/release-1.1.2`. Missing or changed release DLLs fail
the check; they are never downloaded, installed or bundled by this harness.

The wrapper verifies published client/server/LiteDB SHA-256 values and runs the
production-linked `PitFireTeamRoundLifecycleChecks` and `PitFireTeam100Checks`.
Those projects own their fixtures; this folder owns the combined 1.1.2 regression
gate and uses the common `Test-ArenaSourceFaults` runner. Real Harmony patches
run against temporary companion caches/databases, never an installed profile.

Nine compiled faults must fail their named behavior:

- foreign same-count account selection, duplicate accounts, null task and
  silently replaced task;
- null dropped-item result, unprotected companion registration and raid-start
  clearing, changed inventory parameter and static/instance contract drift.

The ordinary lifecycle fixture also checks in-place account-array changes after
prefetch. Released DLL metadata mutations cover missing/changed methods, while
the 1.0.0 baseline and 0.10.0/0.10.3 metadata controls remain in
`PitFireTeam100Checks/Verify.ps1`. This wrapper does not replace those legacy
checks or claim native raid, round transition or profile-restoration acceptance.

Success marker: `PFT_112_CHECKS_PASSED`.
