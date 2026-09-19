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

Built-in prototype fallback is tested separately from exact custom layouts:
unchanged valid originals, deterministic clear alternatives, all original geometry
vetoes, at most 32 retries, final 2m radius/0.5m height bounds after NavMesh/floor
snapping, frozen layout provenance and logged displacement. Compiled faults force
custom relocation and remove the final-radius check. Beam ceiling clipping is
executable geometry coverage; availability/owned-material cleanup are source
contracts only. The real LineRenderer and its color/occlusion need a GPU check.

HUD, authoring and plugin wiring have source contracts only. In-game readability,
Factory floor/mesh geometry and native inventory animation require GPU tests.
Effects and the weapon transfer adapter have their own separate harnesses.

Four Factory points and the appended JumpBoots enum round-trip are covered without
changing legacy numeric kinds. The real runtime rejects bot/disabled-movement
Boots collection and selects the separate M60 definition even with an empty normal
kit catalogue. HUD label/color and hook checks are source contracts, not rendering.

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
