# RaidInfoPanels absorption

2026-09-11. The user requested absorbing their RaidInfoPanels project into
ArenaMode and retiring the standalone local project so both HUDs can share a
visual design. Source: `maschine34675/RaidInfoPanels-dev`, commit
`55ee6009592431167fefbbacca9faae3ccf8f51a`. This is the author's own code and
purpose-built generated artwork. No external mod code or DLL was added.
`tools/RaidInfoPanelChecks/SOURCE-IMPORT.json` records source-file hashes;
all 26 runtime PNGs are preserved byte for byte. The four source generators
are retained under `tools/RaidInfoPanelIcons` with their output paths updated.

## Preserved functionality

- Weapon name, magazine/chamber count, cartridge, caliber, fire mode and
  underbarrel-launcher information.
- Spare magazines in rig/pockets and backpack, with full/partial indication;
  the existing loose-round fallback and secure-container exclusions remain.
- Total HP, carried weight, front/back armor-zone puppet, damaged-zone rows,
  and bleeding/fracture badges. No hydration/energy panel is reintroduced.
- Both panel positions, scale, opacity, colors, visibility, warning thresholds,
  refresh interval and weapon-name preference.

The modules live in `ArenaMode.Client/Hud/InfoPanels` under Arena's existing
match canvas, font resolver and text-outline style. There is no additional
BepInEx plugin, standalone assembly dependency, Harmony patch or persistent
Update component. This preserves the existing panel layout; a wider visual
redesign can now happen inside ArenaMode.

**Armored rigs are included in reserve counts.** The retired Claude context's
"Armor-Vest-Grids ignored" wording refers only to the separate `ArmorVest`
equipment slot, not to armored rigs. Native `EFT.InventoryLogic/Inventory.cs`
lines 193–197 defines quick access as `Pockets` and `TacticalVest`;
`InventoryController.cs` lines 882–902 uses these same slots. The installed
database puts AVS (`544a5caa4bdc2d1a388b4568`, 12 grids) and TV-110
(`5c0e746986f7741453628fe5`, 9 grids) under the Vest parent accepted by the
`TacticalVest` slot, despite their armor components/plate slots. Both magazine
and loose-round counters already include descendants of that slot. The imported
behavior needs no functional change for armored rigs; an offline counter
regression protects that boundary.

## Boundaries and lifecycle

Only the current Arena match can display the panels. Data additionally requires
the exact active game/world, native Started state, a usable living local player
in the world's alive list and first-person view. Loading, ordinary raids,
death, body replacement and pending/active spectating hide them. New bodies
refresh immediately. The panels read inventory/health state; they do not edit
items, profiles, weight limits or health.

Each panel handles its own update failure and stays hidden until a successful
refresh, so stale values cannot reappear on the next throttled frame. Warnings
are bounded per panel/match. Refresh intervals are clamped to 0.05–2 seconds;
NaN/infinity use 0.1 seconds. Theme events are unsubscribed on teardown; static
inventory scratch buffers are cleared after reads, including failed reads.
Match-owned sprites/textures are destroyed and caches released, while borrowed
game icons and fonts are never destroyed. Weight warnings now use the actual
player's Physical overweight state/limits, including Arena's carry allowance,
instead of the original global threshold.

## Settings and migration

F12 entries move to ArenaMode's **HUD Panels - General**, **Weapon**, **Status**
and **Appearance** groups. All 17 current keys are imported as initial defaults
from `BepInEx/config/com.maschine.RaidInfoPanels.cfg`. Existing Arena values win,
including already persisted but not yet bound entries. Old unused Armor/Health
groups are ignored. Import opens the legacy configuration with saving disabled;
the legacy file is deliberately retained at that path for the next game start.
In this installation the Status panel's custom offset `(24, 245)` is preserved.
Arena's overall HUD switch also controls these modules.

If a synchronized test host still loads the retired DLL, the Arena module
temporarily disables only `RaidInfoPanels.HudOverlayController` and its named
`RaidInfoPanelsCanvas`, restoring their prior states at teardown. It does not
destroy or disable the shared BepInEx plugin host or another mod's canvas.
Removing the standalone DLL is still the permanent installation change.

## Verification and retirement

`tools/RaidInfoPanelChecks/Verify.ps1` exercises linked production lifecycle,
context and settings code against controlled external boundaries. It does not
simulate Unity rendering or certify native BepInEx file serialization.
`Build-Verify.ps1` compiles the actual client against installed EFT/Unity APIs,
then reads PE metadata without loading game code: all 26 resource byte hashes
must match the original source, exactly one Arena plugin must be registered,
and no RaidInfoPanels assembly reference may remain. The staged build keeps
installed client/server/dependency DLLs unchanged.

Independent comparison/review found no unresolved P0–P2 issue in the original
functionality, match/body gates, migration, cleanup or legacy suppression.
Parent verification passed 180 assertions and seven controlled source faults,
including armored-rig counting. A separate copied DLL with one corrupted
embedded icon (and a recomputed outer DLL hash) was correctly rejected by the
resource-content check. Real client build: zero errors, four existing CS8632
warnings. None of these checks certifies rendering or native audio playback.

Before local retirement a complete ZIP (including ignored files and `.git`),
verified all-ref Git bundle and installed DLL/config backups were created at:

`D:\SPT41\Development\_archive\RaidInfoPanels-2026-09-11-f8c969a4dffa4335987a02ade57e66fe`

The archive contains all 394 original files. Its manifest records every hash,
and ZIP readback verified the exact set and bytes. Final delivery rechecks the
source against that manifest before deleting only the explicitly named local
`D:\SPT41\Development\RaidInfoPanels` checkout and standalone installed DLL.
The remote GitHub repository is unchanged. Keep the legacy CFG for migration.
The final delivery receipt and client build identity are recorded in HANDOFF.md.
Delivery from clean commit `1e6d350` completed on 2026-09-11: installed client
SHA256 `6C660F3B3F8B39ED6B54F3E9960605D4A437FA63CE277A29DC0D7DC7FB190B72`,
standalone local checkout/DLL absent, legacy CFG retained, protected files
unchanged. Receipt: `artifacts/raid-panel-delivery/9b26d7afe2414856bc9d2e5c62b24440/receipt.json`.

GPU acceptance still belongs to a later user test: verify both panels after game
restart, custom positions and live F12 changes, kit swaps/reload/reserve counts,
armor damage/effects, death/killcam/spectating/respawn, a second raid, and no panel
in an ordinary raid. Check at the actual screen resolution for collisions with
native or Arena HUD elements. Only a game restart is needed; no server change.
