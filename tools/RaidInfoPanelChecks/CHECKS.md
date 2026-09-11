# Absorbed Arena info-panel checks

Run `pwsh -NoProfile -File tools/RaidInfoPanelChecks/Verify.ps1` from this repo.
The full success marker is `ARENA_INFO_PANEL_CHECKS_PASSED`. The optional
`-ProductionOnly` mode explicitly reports that negative controls did not run.

The net10 executable links these production files unchanged:

- `ArenaInfoPanels.cs`: ownership, refresh scheduling, independent toggles,
  per-module failures, stale-content invalidation, theme subscription and disposal.
- `RaidPlayerContext.cs`: ordinary-raid exclusion, exact game/world identity,
  loading/stopping, death, unready hands/body/inventory, alive roster, respawn,
  first-person view and spectator exclusion.
- `InfoPanelSettings.cs`: all 17 original setting mappings, existing/unbound and
  already-bound Arena values taking precedence, missing/invalid legacy entries,
  and read-only legacy binding. Test fixture bytes stay unchanged after imports.
- `MagazineInventoryCounter.cs`: armored CompoundItem rigs in **TacticalVest**
  count loaded full and partial magazines as ready, together with pockets.
  Backpack magazines count separately. Separate ArmorVest and SecuredContainer
  contents, empty/incompatible magazines and inserted magazines of equipped or
  carried assembled weapons are excluded. Exception cleanup releases scratch
  references and the following refresh recovers.
- `GrenadeInventoryCounter.cs` (added 2026-09-11): "ready" comes from the
  native quick-throw list and "stored" is the carried rest (nested pouches,
  backpack), with frag/flash/smoke classification (stun joins flash, effect
  fallback for unlisted types). The stand-in `InventoryController` models the
  local body's controller: a static, mutable `Inventory.FastAccessSlots`,
  slots before grids, direct children only, appending without clearing, and the
  owner override that skips an unsearched rig/pockets. Cases cover vanilla
  slots, the installed UseItemsAnywhere 1.4.0 slot list (backpack and
  secure-container grenades ready), unexamined and unsearched exclusion,
  ArmorVest/secure exclusion, repeated refreshes, a classification fault fired
  with both scratch lists filled, an examine fault after one append, the
  summary accessors the UI reads, and native-faithful missing-controller
  handling (`Player.Inventory` reads through the controller, as natively).

The suite includes fast repeated ticks, immediate hide/recovery before the next
refresh deadline, replacement physical bodies, independent weapon/status faults,
invalid intervals, bounded warnings, partial construction and cleanup after a
hide failure. Disposal also exercises the real magazine scratch reset.

Fourteen controlled source faults must compile and fail at their intended behavioral
assertion: omitted early inactive-session guard, removed exact-world check, stale weapon content,
leaked theme handler, legacy config writes, omitted TacticalVest counting,
magazine scratch retention, and for grenades: scratch retention, a leak on the
exception path only, stored grenades counted as ready, ready grenades dropped
when outside the carried walk, the native reach list replaced by a hand-written
walk, stun no longer grouped with flash, and secure-container grenades counted. Each run saves copied mutants and output under this
project's ignored `obj/negative/<unique-id>` directory; production source is never
modified. The verifier reruns the unmodified production sources afterward.

These are **offline contract checks**, not Unity GPU or in-raid acceptance.
Stand-ins model the external Unity lifecycle, EFT data access, BepInEx Bind
contract and UI modules; actual text, textures, rendering, visibility hierarchy,
native inventory converters and other mods' behavior require the separate real
build/resource checks and GPU test. The migration fixtures use simplified
vector/color text syntax: they verify mapping and ownership, not BepInEx's real
parser. Item ancestry follows the inspected native contract (ordinary ancestry
for IsChildOf, merged ancestors for GetRootItem) but does not replace a live
inventory check with actual database-created armored rigs.

This verifier does not deploy, modify legacy configs, retire another mod, alter
the source-import record, or launch the game. `Build-Verify.ps1` and
`SOURCE-IMPORT.json` are independently owned build/import evidence.
`Build-Verify.ps1` also checks icons authored inside ArenaMode against their own
record, `ARENA-ICONS.json`, so the import provenance of the 26 imported icons
stays exactly as strict. Never rerun the four imported generators: that would
re-encode imported icons. New icons get their own generator (for example
`tools/RaidInfoPanelIcons/gen_grenades.ps1`) and an `ARENA-ICONS.json` entry.
