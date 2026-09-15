# Arena pickup checks

`Verify.ps1` compiles the actual pickup runtime, appearance state, layout parser
and scene placement geometry. Controlled doubles provide native world/player
identities, Physics/NavMesh results, the local inventory transaction callback,
the effect recipient, clocks and visual lifetime observations. They do not
reimplement the pickup lifecycle or range policy.

The scenarios cover exact body/raid/mode admission, phase and round transitions,
fresh-spawn and reappearance leave/reenter rules, swept contact without teleport
or floor/wall shortcuts, a single nearest winner, blocked/ambiguous/late inventory
results, deadline retirement, old callback isolation, bounded grounded-anchor
retry, layout validation/freezing and scene/effect cleanup. Compiled negative
controls include the outward-sweep retry and zero-length projected ray defects
found by this harness. Every mutant must build successfully and fail its named
behavioral assertion; a compile failure is not a passing fault control.

HUD, authoring and plugin wiring have source contracts only. In-game readability,
Factory floor/mesh geometry and native inventory animation require GPU tests.
Effects and the weapon transfer adapter have their own separate harnesses.

`Verify-Regressions.ps1` runs selected production-source assist/combat, mode,
movement and HUD suites, the existing TDM lifecycle runner and server/data unit
tests. Native mod projects are not built by the console suites. The unit suite's
server deployment properties point to unique absent paths and are checked again
afterwards. Neither script deploys or changes installed DLLs. Source and installed
hashes plus logs are retained under `artifacts/pickup-*`; JSON exercise drafts use
unique temporary directories and are never written to the installed Arena folder.

Run from this repository with PowerShell:

```powershell
pwsh -NoProfile -File tools/ArenaPickupChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaPickupChecks/Verify-Regressions.ps1
```
