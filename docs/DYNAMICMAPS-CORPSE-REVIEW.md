# Arena corpse markers in optional DynamicMaps

Snapshot: 2026-09-12. Source implementation on `feature/arena-visibility-movement`.
Native GPU acceptance remains pending; no DLL deployment belongs to this change.

## Problem and outcome

Arena trims old settled corpses instead of destroying their native bodies. The
installed DynamicMaps still discovers those Players through
`GameWorld.AllPlayersEverExisted` and retains a static corpse marker even after
the body's renderers and colliders have been hidden. A long match therefore
accumulates visible skulls for bodies Arena has already removed from view.

`DynamicMapsCorpseMarkers` removes only the marker belonging to an exact
successfully trimmed corpse. It calls the map provider's own UI removal method.
A prefix on the provider's central creation method stops that same corpse from
returning when the map reopens, changes, refreshes, enables a category, or receives
a later death notification. Untrimmed corpses keep DynamicMaps' own admission,
category, intelligence and visibility rules.

## Installed contract and native ownership evidence

Read-only inspection used the installed assembly, not just reference source:

- File: `D:\SPT41\BepInEx\plugins\mpstark-dynamicmaps\DynamicMaps.dll`.
- BepInEx identity: `com.mpstark.dynamicmaps`, version `1.2.1`.
- Assembly version: `1.2.1.0`; product version:
  `1.2.1+99200c60f084d1f64491dee93ceb09351bb8a2cd`.
- SHA256: `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8`.
- Provider: `DynamicMaps.DynamicMarkers.CorpseMarkerProvider`.
- Private instance methods: `void TryAddMarker(EFT.Player)` and
  `void TryRemoveMarker(EFT.Player)`.
- Private table: `Dictionary<EFT.Player, DynamicMaps.UI.Components.MapMarker>
  _corpseMarkers`.
- Installed IL confirms creation from `TryAddMarkers`, `RefreshMarkers`,
  `OnMapChanged` and `OnUnregisterPlayer` reaches `TryAddMarker`. Initial/reopened
  `OnShowInRaid` and category enable reach `TryAddMarkers`.
- Installed removal calls `MapView.RemoveMapMarker` and removes that table key.
  The map removes the marker from its UI list, unsubscribes its position callback,
  kills its UI tween, deactivates the marker GameObject and destroys that UI
  GameObject. Neither removal method calls native GameWorld, Player, Corpse or
  BotSpawner lifetime methods.

`tools/DynamicMapCorpseChecks/Verify-InstalledContracts.ps1` checks those metadata
and call relationships against the installed DLL and native Assembly-CSharp.
The original integration code was written for Arena; the local DynamicMaps
reference was used to understand its technique and is not copied or bundled.

Native reference evidence under `../_decompiled`:

- `EFT/Player.cs:23931`: protected `Corpse` stores the exact body. `OnDead`
  assigns `CreateCorpse()` at line 30198. `CreateCorpse<T>` at line 30256 passes
  the Player GameObject to the corpse factory.
- `EFT.Interactive/Corpse.cs:103`: the factory attaches the Corpse component to
  that GameObject. Profile id alone is not body identity.
- `EFT/GameWorld.cs:661`: `AllPlayersEverExisted` enumerates retained Players.
- `EFT/BaseLocalGame.cs:1150`: native cleanup still disposes each retained Player
  before returning its GameObject to the pool. Arena's map adapter must not
  shortcut that owner.
- Redeploy creates a new local Player for respawn
  (`../Redeploy/Redeploy.Core/Engine/RespawnEngine.cs:195`); matching the old exact
  Corpse does not hide a new Player that shares a profile id.

The existing corpse cleanup and its separate AI inspection-record removal are
unchanged. This feature never destroys a native corpse, removes world loot,
rewrites equipment, disposes a Player or alters spawner registrations. It also
does not change Redeploy's separate live-player map-marker rebind.

## Admission and lifetime rules

`CorpseCleanupService.TrimState` now distinguishes successful completion from a
failed trim remembered to prevent repeated attempts. Suppression requires all
of: active Arena, live exact Corpse reference, a completed trim record and the
same current GameWorld. A catch-recorded failure is never suppression evidence.
Existing limits and protections still decide whether a body is trimmed: corpse
budget, settled ragdoll, proximity, newest own body, protected PitFireTeam member,
open loot and current interaction.

Providers are tracked weakly when their central creation method runs in Arena.
Successful trimming immediately removes matching existing markers; the existing
five-second cleanup sweep retries transient UI removal failures. Provider table
keys are snapshotted before invoking its own removal. Failures are isolated per
provider/player and logged once per reset; marker failures cannot turn a
successful native trim into a failed one. No extra per-frame scan is introduced.

Cleanup reset clears both the corpse evidence and weak provider registry. The
world-identity check also rejects evidence from a different GameWorld. Both
creation and removal hooks return without suppression outside Arena. Shutdown
removes only this adapter's Harmony patches and clears its state.

Compatibility is optional and currently reviewed for DynamicMaps **1.2.1 only**.
Missing plugin leaves native behavior alone without a warning. Other versions,
changed contracts or patch failure leave the adapter disabled and produce a
bounded warning. No DynamicMaps assembly/project reference or bundled file is
added. Parent-owned Plugin wiring uses a soft BepInEx dependency, `Enable()` in
Awake and `Shutdown()` in OnDestroy; it does not make DynamicMaps mandatory.

## Verification and remaining native acceptance

Run `pwsh -NoProfile -File tools/DynamicMapCorpseChecks/Verify.ps1`.
The helper links the actual cleanup service and adapter into an isolated console
project; it has no mod project imports, project references or deployment target.
Its stubs model observable contracts, not native Unity/Harmony execution.

Passed: 57 assertions covering existing and later markers, reopen/rebuild,
multiple providers, same-profile different bodies, living/null/destroyed bodies,
trim exclusions and failures, optional absence/version/instance/patch failures,
ordinary/reset/world boundaries, transient UI failures and weak provider lifetime.
Seven compiled defect controls are rejected at their named behavioral assertion:
missing removal, missing creation suppression, treating failed trim as success,
missing world gate, stale reset evidence, unsupported-version admission and
suppression of every body. Final production rerun passes. Existing corpse tests
also pass unchanged behavioral coverage: 44 assertions and all four compiled
inventory-notification regressions.

Evidence from the completed run:
`C:\Users\Administrator\AppData\Local\Temp\arena-dynamicmap-corpse-checks-7185462962f445069b0930cd9a966c03`.
Success marker: `ARENA_DYNAMICMAP_CORPSE_CHECKS_PASSED`.

Self-review and independent parent adversarial review checked native owners,
exact identity, optional failure boundaries, reset paths, provider recreation,
Plugin wiring and existing cleanup semantics; no established defect remains.
The parent reran the complete installed-contract/behavior/mutation/regression
helper successfully. The integrated staged build is recorded in the handoff.
The first full native build caught the `EFT.Version`/`System.Version` namespace
collision omitted by the narrow doubles. Explicit `System.Version` and its
matching compiled mutation anchor resolve it; independent review confirmed
the comparison semantics and the 57-assertion baseline passed again.
No live GPU raid was run for this leaf and no runtime success is claimed.

Future native acceptance should exceed the visible corpse budget in Arena with
DynamicMaps 1.2.1, observe the cleanup log and corresponding skull removal, reopen
full map/minimap and change map/category state, then confirm remaining/own/team
protected bodies retain their expected markers. A later ordinary raid must keep
normal DynamicMaps corpse behavior, and raid exit must remain free of corpse or
Player disposal errors. Existing r3 GPU instructions and installed files remain
the current separate test baseline until an authorized later delivery.
