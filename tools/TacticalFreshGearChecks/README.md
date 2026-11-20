# Tactical opening rounds: pool purge checks

Run `pwsh -NoProfile -File tools/TacticalFreshGearChecks/Verify.ps1` from the repo (or the harness through
`tools/Run-ArenaVerification.ps1 -Harness TacticalFreshGearChecks`). Nothing here builds into or touches the game.

The program compiles the production `ArenaTacticalFreshGearPatch`, `ArenaTacticalOpening`, `TacticalOpeningBandGate`
and `ArenaDifficultyPolicy` against the host stubs of `tools/ArenaBalanceFixtures.cs` and drives the Harmony prefix with
the gate answering as the server does (acknowledged, late, `applies:false`, refused). It covers the purge of pooled PMC
profiles outside `[centre - 5, centre + 5]` of the acknowledged round (both sides, own role and difficulty only), the
round 1, 2, 3 and falling sequence, the budget of two purges per (role, difficulty, centre) with a new centre, a late
acknowledgement, a new raid (world object) and `Reset()` starting over, every guard (not the exact raid, another mode,
protected team, no acknowledged round, a refused or not applying feature, a scav squad, a map without tier scaling, an
unreadable config, a gate of another world) and the wiring in `Plugin.cs` and `ArenaRespawnDriver.cs`.

`ArenaGunGameFreshGearPatch` is deliberately a different file with its own harness (`GunGameFreshGearChecks`), which
stays green unchanged: this patch is its sibling, not an edit of it.

`Verify.ps1` runs the baseline and then the mutants through `Test-ArenaSourceFaults`; each must fail the assertion it names.
