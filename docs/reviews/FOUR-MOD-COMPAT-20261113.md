# Callsigns, encumbrance, shoulder swap and resolution

Review date: 13 November 2026. Native acceptance is recorded separately in
[the GPU report](../reports/2026-11/GPU-FOUR-MOD-COMPAT-20261113.md).

| Mod | Released identity | Reviewed version |
| --- | --- | --- |
| [Bot Callsigns Reloaded](https://sp-mod.com/mod/1873/bot-callsigns-reloaded) | `com.harmonyzt.botcallsigns`, server | 2.0.6 |
| [Player Encumbrance Bar](https://sp-mod.com/mod/1374/player-encumbrance-bar) | `com.mpstark.PlayerEncumbranceBar`, client | 1.3.0 |
| [Auto Shoulder Swap](https://sp-mod.com/mod/2639/auto-shoulder-swap) | `hazelify.StanceSync`, client | 1.1.1 |
| [Dynamic External Resolution Patch](https://sp-mod.com/mod/2200/dynamic-external-resolution-patch-derp) | `com.Shibatsu.DynamicExternalResolution`, client | 1.1.2 |

Use the actual released plugin metadata. Forge's identifiers for Encumbrance
Bar and Auto Shoulder Swap differ from their BepInEx GUIDs. DERP's assembly
file version is not its plugin version. The existing policy's deny, dependency
and advisory-version rules are unchanged.

## Auto Shoulder Swap

The unchanged 1.1.1 DLL was previously rejected after the native knife/left-lean
failure recorded in [the October review](../reports/2026-10/INPUT-MOD-COMPAT-20261025.md).
It reads `ProceduralWeaponAnimation.CurrentScope` without checking the scope
list and assumes firearm hands. There is no body reference to hand over.

Arena guards the foreign lean and camera callbacks only inside its exact active
local game, world and current physical body. Non-firearm hands and incomplete
scope state skip those foreign callbacks. Native leaning and camera updates
continue. Ordinary PMC/Scav callbacks retain the mod's original behavior.
This is not a general fix for Auto Shoulder Swap outside Arena.

## Player Encumbrance Bar

The bar's weight comes from the local body's inventory, but its profile getter
used for event subscriptions returns the backend session profile. Arena kits
and replacement bodies can have a different inventory and skill collection.
The shared Core adapter binds the foreign panel to the exact physical body
and prevents callbacks from a retired panel from affecting its successor.
This lifetime correction also applies to standalone Redeploy.

The bar also reconstructs overweight limits from shared game configuration.
Arena deliberately changes only the local player's physical limits. An
Arena-only adapter supplies those effective limits when the foreign panel
lays out its thresholds. It does not change shared configuration or ordinary
raid limits.

## Dynamic External Resolution Patch

DERP keeps a private static player reference while the old body can remain as
a corpse. Core transfers that cache to the committed replacement and fences
callbacks from the retired body's optics. It does not globally change the
mod's settings or replace its rendering implementation. Menu and raid exit
must release owned state, and an absent mod must not add per-frame reflection
or Harmony-wide unpatch walks.

## Verification boundaries

Offline tests cover ownership, unavailable scope state, identity and policy
decisions. They cannot prove Mono detours, visible UI updates or renderer
handoff. Native checks must include real inventory changes after a death,
knife and firearm lean, optic ADS with different outside/inside resolutions,
normal raid controls and a standalone Redeploy replacement. Foreign DLLs,
test profiles and probes are not distributed.
