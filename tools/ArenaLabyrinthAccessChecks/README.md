# Self-contained Arena Labyrinth access

Arena shows its allowlisted Labyrinth tile and waives its entry card already.
The missing native values are the menu position and forced-online PVE flag:
the stock SPT map has `Enabled=false`, `IconY=325`, and
`ForceOnlineRaidInPVE=true`. Arena now projects a **separate client location
object** with `Enabled=true`, `IconY=350`, and `ForceOnlineRaidInPVE=false` for
the exact selected Arena PMC setup only. The button and selected-location
setter receive it before native rendering / `LocationInfoPanel.Set` and
`RaidSettings.NeedToGoOnlineInPve` read it. The native location database is never
modified. The real menu's existing `Clear` restores the former location reference
on return to PMC/Scav or exit; no field snapshots can overwrite external changes.

The server mirrors `Enabled=true` and `ForceOnlineRaidInPVE=false` only on its
existing exact Arena raid clone. On that Labyrinth clone it also zeros both
Way Up timers: `MinTime`/`MaxTime` control availability, while `ExfiltrationTime`
controls the extraction countdown; all three PVE counterparts are zero as well.
Only the exact native exit name `labir_exit` is affected. Its native extraction
type, requirements, chance and every other field remain intact. The matching
exit record is copied before changing its timers, protecting shared aliases even
if another consumer has retained references into the raid clone. No exit is
created when Way Up is absent. Map cards remain governed by the existing
`MapAccessPatch`/`ArenaClaim` checks: known enabled server map, exact PMC selection,
current group eligibility, server availability and no Fika. User online-PVE
preferences are neither read into a replacement preference store nor written;
clearing the map's mandatory-online flag allows the native local option.

Co-installed EnableLabyrinth metadata already has all three menu values. It is
reused unchanged instead of cloned again. Its optional card requirements, Scav
entry setting and extra exits stay intact. Arena overrides its Way Up timers
only for the Arena raid, without changing its database/configuration values.
Arena creates **no** Scav scene exit,
boss key loot, scav raid-time table, or Blazor configuration. Existing Arena scene
trap/alarm/light handling remains separate. Without EnableLabyrinth, ordinary
raids keep the stock map restrictions; with it, ordinary raids keep that mod's
configuration.

Only three scalar fields differ in the client projection. `MemberwiseClone`
preserves all other native metadata and scene/resource references; shared key,
exit and configuration arrays are never edited. The server's native deep clone
is independently verified. There is no global unlock to restore after a crash.

Reference reviewed: `../_reference/enablelabyrinth-csharp-master`, both MIT
licenses, `Server/EnableLabyrinthOnLoad.cs` and
`Client/Patches/LocationSceneAwake.cs`. Thanks to acidphantasm for the reference
mod. This access projection is original code, not copied upstream implementation;
no upstream assets, binaries or config app are included. The unchanged reference
license is retained here for attribution.

Native evidence in `../_decompiled/`:

- `JsonType/LocationSettings.cs`: `Enabled`, `IconY`, `RelativeMapPos`,
  `ForceOnlineRaidInPVE`, `AccessKeys`.
- `EFT.UI/LocationButton.cs`: exact nine-argument `Show`, position and selection
  callback capture.
- `EFT.UI.Matchmaker/MatchMakerSelectionLocationScreen.cs`: button callback bound
  to that screen's `OnLocationSelected`; selected setter forwards the object to
  raid settings and `LocationInfoPanel.Set`.
- `EFT.UI/LocationInfoPanel.cs`: `SetupPveServerModeToggle` reads the location's
  forced-online flag. `EFT/RaidSettings.cs`: `NeedToGoOnlineInPve` combines it with
  the existing user's preference.
- `EFT/MainMenuShowOperation.cs`: `method_54` checks `AccessKeys` and clears KeyId;
  `method_27` consumes the local/online decision.
- SPT `LocationLifecycleService.GenerateLocationAndLoot` performs its own deep
  clone before the synchronous Arena postfix runs.
- `EFT.Interactive/ExitTriggerSettings.cs`: `Load` reads server `MinTime` and
  `MaxTime`, selects integer `StartTime` with `UnityEngine.Random.Range`, and
  separately copies `ExfiltrationTime`.
- `EFT.Interactive/TimerRequirement.cs`: `Met` compares elapsed game seconds
  with that `StartTime`. Zero removes the native 900–905 second availability
  delay; eligibility begins after the first positive game-time update.
- `CommonAssets.Scripts.Game/EndByExitTrigerScenario.cs`: `Update` separately
  consumes `ExfiltrationTime` for individual and shared extraction countdowns.
  Zero removes the stock five-second countdown; native trigger, living-player
  and requirement checks still apply, and extraction completes on a native
  update. Arena does not bypass the extraction lifecycle.

Run in the repo with PowerShell 7:

```powershell
pwsh -NoProfile -File tools/ArenaLabyrinthAccessChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/ArenaLabyrinthAccessChecks/Verify-Regression.ps1
pwsh -NoProfile -File tools/ArenaLabyrinthAccessChecks/Verify-Timers.ps1
```

The installed-assembly check validates method signatures, parameter names and
the reviewed native readers, plus production source hook wiring. The executable
links actual projection, menu selection, Arena claim and keycard code against
small data/transport stand-ins; it does not run Unity or the Harmony map-screen
hooks. It covers standalone and co-installation, idempotence, eligibility,
unavailable server/maps, Fika, PMC/Scav restoration, external source changes and
the unchanged user preference. Server xUnit tests use actual raid-session and
clone mutation code. Six temporary mutants must fail targeted checks; the
unaltered production, existing native-map/menu and server mutator tests then run.
Every server test build targets a verified nonexistent `SPTUserMods` path.

The timer check separately verifies the installed native loader and timer
consumers. Actual server/session tests cover all five Arena modes, standalone
and co-installed metadata, ordinary/armed/foreign sessions, raid exit, disabled
maps, other maps/exits, absent exits, idempotence and shared exit aliases. Four
temporary mutants must fail: a nonzero availability timer, a nonzero PVE
countdown, changes to every exit and an in-place write through a shared alias.
The unmodified timer tests and existing location mutator suite then run again.

GPU handoff: restart client/server; test Arena Labyrinth with EnableLabyrinth
absent and present, menu highlight after Back, no entry card, local map load and
return to ordinary PMC/Scav. Confirm ordinary metadata/exits match the relevant
stock or upstream-mod configuration. In Arena, confirm Way Up is immediately
eligible and extracts without a countdown, while other exits retain their
configuration. Offline passing checks are not gameplay proof.
