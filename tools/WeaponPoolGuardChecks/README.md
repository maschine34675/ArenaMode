# WeaponPoolGuardChecks

Offline harness for the Gun Game pool guard (`ArenaMode.Server/Services/ArenaPresetGuard.cs` and the
wiring in `LadderGenerator.cs`, see docs/reviews/WEAPON-VARIANTS-REVIEW.md, "Preset guard").

`Program.cs` links both production files (with a two-member stand-in for `ArenaContext`) and checks
(1) the guard's reason table on a small hand-made database and (2) the generator over the installed
vanilla database (`SPT_DATABASE`; a missing database is a failure, not a skip): no Warning and an identical
pool with and without the guard, an injected broken "default" preset that costs nothing, build and consider
isolation, dense rung numbers, a failing report, null elements.

`Verify.ps1` runs it, then 23 mutants through `Test-ArenaSourceFaults` (each one literal line of
`ArenaPresetGuard.cs` or `LadderGenerator.cs` changed; each must fail the check it names), then the xunit
side (`ArenaPresetGuardTests`, `LadderGeneratorTests`) with a TRX judged by `Assert-ArenaTestRun`, which
tolerates no skip for a missing database and requires the data-bound families to have passed.
