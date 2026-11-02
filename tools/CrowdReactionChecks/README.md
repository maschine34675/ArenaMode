# Crowd reaction checks

From the repository root, through the verification entry point (never a bare `dotnet run` here):

```powershell
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness CrowdReactionChecks -SkipUnitTests
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness CrowdReactionChecks:Baseline -SkipUnitTests
```

The program links the three production files of the crowd presentation unchanged
(`Compat/ArenaMapPortSpectatorCrowd.cs`, `Audio/ArenaCrowdReactionPolicy.cs`,
`Audio/ArenaCrowdReactions.cs`) together with `ArenaKill`, `ArenaAnnouncerCue` and
`ArenaAnnouncementSnapshot`. `StandIns.cs` doubles only what they read (BepInEx's
`Chainloader`, the logger, `Time`, `Singleton`, the EFT game/world/player, `ArenaMatch`,
`ArenaMatchService`, `ArenaSession`, `ArenaEvents`, `ArenaHitHistory`, the announcer's
interlock flag and the announcement director's snapshot). `Surfaces.cs` holds the stand-in
ArenaMapPort `SpectatorCrowd` surfaces: the planned phase A surface, the published 0.4.0
surface and the broken ones (wrong or convertible signatures, a field, an extra overload,
contract version 2, no version, a throwing version).

It drives the service through the same events the game raises (`OnCallout` before `OnKill`
before the result cue, `ArenaEvents.OnPlayerDied`, the Deathmatch round events) and asserts
the binding outcomes, the mapping table, coalescing, the local-death deduplication, gating,
every exit path, the retry window, the excitement mapping and the wiring of the files it cannot
link (text pins of `ArenaMatchService`, `ArenaController`, `Plugin`, `ArenaSession`,
`Settings`, `ArenaConfigMenuLayout`). `Verify.ps1` then runs 27 hand-made mutants, each anchored
on exactly one LF site of the production text and each failing its named assertion; every case
builds with the no-deploy targets into its own folder under `artifacts/crowd-reaction-checks/`.

Not covered: a real ArenaMapPort, EFT, Unity audio and anything audible. See
[CROWD-REACTIONS-20261102.md](../../docs/reviews/CROWD-REACTIONS-20261102.md).
