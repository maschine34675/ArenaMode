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
ArenaMapPort `SpectatorCrowd` surfaces: the phase A surface, the published 0.4.0
surface and the broken ones (wrong parameter, return or convertible signatures, an internal
member, a field, an extra overload, contract version 2, no version, a throwing version).

It drives the service through the same events the game raises (`OnCallout` before `OnKill`
before the result cue, `ArenaEvents.OnPlayerDied`, the Deathmatch round events) and asserts
the binding outcomes, the mapping table, coalescing, the local-death deduplication, gating,
every exit path, the retry window, the excitement mapping, the Lull trigger (decision D14: the
60 s quiet clock on live combat, its resets, the 90 s gap from a sent Lull, the mask bit 6,
coalescing with round events, the withdrawal by a kill or a local death) and the wiring of the
files it cannot link (text pins of `ArenaMatchService`, `ArenaController`, `Plugin`,
`ArenaSession`, `Settings`, `ArenaConfigMenuLayout`).

**Real surface (installed data).** The last stage loads the installed ArenaMapPort client
`D:\SPT41\BepInEx\plugins\maschine-ArenaMapPort\maschine-ArenaMapPort.Client.dll` read-only
(`RealMapPort.cs`; `-SptRoot` of the runner, which forwards it to `Verify.ps1`, or the
`MapPortClientDll` property name another one) and needs crowd reactions phase A (ArenaMapPort
`d70acd2` or later, `feature/iceberg`, not released). A missing DLL, a missing `SpectatorCrowd` member or a missing internal model type
fails the run with that requirement; it never skips. MapPort's contract and its state
(`CrowdReactionQueue`, `CrowdBedState`) are Unity-free, so the stage executes them: the binder
binds the real `SpectatorCrowd`, the kind numbers, the implemented mask 62, the owner limit 64
and `MinStrength` 0.05 are read from MapPort's own constants, and the service runs against the
real queue (claim with ArenaMode's owner, an own kill's Approve, the weakest strength sent, no
Lull without bit 6, Start and Finish, excitement into `State.Excitement` and back to 0, release
on detach, the developer demo yielding, another plugin's claim refusing, a second match, a
`Reset` mid-match, a no contest after a Start whose accepted request MapPort keeps, kinds
withdrawn mid-match). Only state is touched (a fresh queue per
scenario, `SetKinds`, `Reset`, the bed's `Available` flag, `Drain`); `Verify.ps1` checks the
DLL's hash before and after.

`Verify.ps1` then runs a fault control (an absent client must fail naming the requirement) and
51 hand-made mutants, each anchored on exactly one LF site of the production text and each
failing its named assertion; every case builds with the no-deploy targets into its own folder
under `artifacts/crowd-reaction-checks/`.

Not covered: MapPort's Unity side (`SpectatorAudio`, the planner's playback, the bed), EFT, Unity
audio and anything audible. See
[CROWD-REACTIONS-20261102.md](../../docs/reviews/CROWD-REACTIONS-20261102.md).
