# Seven additional mod compatibility reviews

Reviewed on 2026-10-25 against ArenaMode `d353c02` and Redeploy `e3c2b36`
(Core 1.0.12). This is a source and released-binary review, not native acceptance.
No game installation, GPU host, default/user whitelist or runtime code was changed.
Another development session was active during this review.

## Recommendation

| Mod / reviewed release | Finding | Recommended next action |
|---|---|---|
| [Use Items Anywhere 2.1.6](https://sp-mod.com/mod/2386/use-items-anywhere) | Confirmed competing magazine-query transpilers; runtime mutator toggles cannot prevent installation-time failure. | Resolve patch ownership in Arena before admission. |
| [ImpactCue 2.0.3](https://sp-mod.com/mod/3029/impactcue-ui-mod) | The telemetry player can remain the retained corpse after a death respawn. Also duplicates HUD/audio features. | Shared-body correction in Core, then native tests. |
| [Enemy Markers 1.4.0](https://sp-mod.com/mod/1858/enemy-markers) | Existing billboards retain the destroyed FPS camera; dynamic registration also admits the replacement local player. | Shared-body correction in Core or upstream, then native tests. |
| [Amand's Graphics 1.8.0](https://sp-mod.com/mod/592/amandss-graphics) | Player and camera rebinding paths exist. No concrete body-replacement blocker found. | Visual acceptance tests before admission; no speculative adapter. |
| [HUD Breakout 1.2.0](https://sp-mod.com/mod/3148/hud-breakout) | Reads the current MainPlayer every frame. Mainly competing HUD/audio and Arena damage semantics. | Native tests with one chosen HUD/audio owner. |
| [Manimal's Quick Cancel 2.0.0](https://sp-mod.com/mod/2969/manimals-quick-cancel) | Uses the current input owner's hands and native item throwing. Interrupted-operation cleanup remains a test case. | Test cancellation and body replacement; no proven need for an adapter yet. |
| [Picture in Picture Disabler 2.1.0](https://sp-mod.com/mod/2667/picture-in-picture-disabler) | Foreign optic callbacks can alter/hide the local reticle; per-weapon mesh caches survive until plugin shutdown. | Resolve callback ownership and cache lifetime before admission. |

All seven remain **unlisted**, rather than newly added to the explicit deny list.
An unlisted mod is excluded by the default private Arena-session policy; this is
not a global prohibition on ordinary PMC/Scav raids. Explicit user overrides
can still load them. The Use Items Anywhere collision happens while plugins
install patches, so Arena's admission policy cannot undo it in a process that
has already loaded both plugins.

## Use Items Anywhere: installation-time collision

The released DLL uses `com.cj.useFromAnywhere` (Forge lowercases the displayed
GUID). Its `InventoryQueryPatches` rewrites fourteen concrete query callers,
including the same three patched by `ArenaBackpackReload`:

- `FirearmHandsInputTranslator.ReloadExternalMagazine`;
- `AmmoSelector.CG_FindAmmoForWeapon.method_0`;
- `ItemUiContext.FindSuitableMagazine`.

Both transpilers require exactly one original `InventoryController` magazine
query. Each substitutes its own helper. The second transpiler consequently sees
zero matching calls and throws. The source and released DLL agree on this
mechanism. Arena catches its installation exception and warns; the foreign
installer unpatches its inventory-query group on failure. The exact resulting
partial feature set depends on load/patch order, so this review does **not**
claim that every combination necessarily aborts the whole game or Arena.

`Plugin.cs:164-165` installs Arena reach/reload patches unconditionally.
`Settings.UseItemsAnywhere` is checked when the resulting helper runs, too late
to avoid this collision. Patch priority alone would only choose which
transpiler loses.

There is also a policy overlap: the foreign mod's configurable reach/bind
postfixes assign their result; Arena widens selected results during Arena.
The foreign default reload/medical slots are rig and pockets, so coexistence
must define which slot configuration wins instead of silently depending on
postfix order. UIA already detects a changed physical input-owner player and
cleans delayed use state. A missing general respawn rebind was **not** found.

Recommended implementation boundary: make Arena's overlapping patches compose
with UIA or yield ownership explicitly, including startup and normal-raid
behavior. This is an Arena feature overlap, not a generic Core body fix.
Do not patch a constructed generic inventory query: Mono sharing previously
broke magazine, loose-ammo and grenade paths here.

Sources: [UIA query transpiler](https://github.com/CJ-SPT/UseItemsAnywhere/blob/fc190e6b56dd12173e1e43de22c231d3759fc640/UseItemsAnywhere/Patches/InventoryQueryPatches.cs#L13-L103),
`ArenaMode.Client/Kits/ArenaBackpackReload.cs:25-142`,
`ArenaMode.Client/Kits/ArenaReachPatches.cs` and
[the earlier Mono correction](BACKPACK-RELOAD-REVIEW.md).

## ImpactCue: retained corpse can remain the telemetry source

`HudTelemetryRuntime.EnsureRaidReady` validates the cached `_raidPlayer` against
Unity lifetime, local-player flag, world and scene. It does not compare that
body with the current `GameWorld.MainPlayer`. Core creates a new physical player
inside the same world; Arena deliberately retains the newest own corpse.
That old body can therefore continue satisfying the cache checks. If it becomes
inactive instead, the method hides the HUD without necessarily rebinding it.

The `BattleUI.Show` notification restores UI references, not `_raidPlayer`.
Other assignments investigated belong to BTR entry/exit recovery. They do not
constitute a normal respawn recovery path. Live-kit replacement can recover
once its old body is destroyed; that does not prove death-respawn compatibility.
The release DLL corroborates the affected control flow. Native reproduction
and the visible extent of stale telemetry remain pending.

A future Core adapter should rebind the exact replacement body and its telemetry
state without resetting the whole match, and fence callbacks from the retired
body. Test both Arena and standalone Redeploy. Do not work around this by
deleting the newest corpse or changing Arena's corpse-retention contract.

ImpactCue also supplies hitmarkers, killfeed, health/ammo displays and sounds.
Its raw damage reporting is not automatically Arena's effective health damage
after shield absorption. Choosing the foreign HUD does not make it aware of
Arena team/assist/powerup semantics.

Both reviewed HUD mods also observe native kill notifications independently of
Arena's administrative-death gate. If a watchdog-retired bot still names the
local player as its last aggressor, a foreign feed/sound can report that removal
as a kill while Arena awards none. This conditional mismatch needs a targeted
native test; it does not change Arena's own scoring.

Source: [ImpactCue 2.0.3 release source and binaries](https://github.com/Dizzlow1/ImpactCue-UI-MOD/releases/tag/v2.0.3),
`Source/Combat/HudTelemetryRuntime.cs:667-687,1485-1640,2063,2420,2562`;
local `ArenaMode.Client/Bots/CorpseCleanupService.cs:1140` and
`../Redeploy/Redeploy.Core/Engine/RespawnEngine.cs:280-360`.
The release also contains a separate web-config server component,
`com.dizzlow.impactcue.webconfig` 2.0.3. A future complete-package admission
must consider that GUID as well as the client GUID.
The tag points to older repository content; the separately published **2.0.3
source ZIP**, cross-checked against the release DLL, was used for this review.

## Enemy Markers: camera lifetime and local-player exclusion

The 1.4.0 `Billboard` component stores `Camera.main` in `Start` and never
refreshes it. Core's `CameraRebindHelper.RebindToPlayer` destroys the old player
camera controller and creates a fresh FPS camera. Surviving bot markers then
stop following the viewing camera's rotation.

This is distinct from the marker-update coroutine: its camera-null break and
restart path **does** reacquire the camera. The finding is not that all marker
updates permanently stop.

The initial marker scan excludes `IsYourPlayer`, but the `OnPersonAdd` handler
does not. Vanilla `Player.Init` registers the replacement player, and
`GameWorld.RegisterPlayer` invokes `OnPersonAdd` for that body too. The handler
therefore creates a marker entry for the replacement local player. The visible
effect still needs native validation. Markers use faction/group rules rather
than Arena's team roster, so also test PFT/Team DM friendliness and duplicate
friendly diamonds.

Recommended corrections belong upstream or in Core's shared replacement scope:
refresh all surviving billboard cameras and exclude the exact current/retired
local bodies from dynamic registration. Any additional Arena team-color policy
would be a separate Arena concern.

Source: [Enemy Markers repository](https://gitlab.com/flir063-spt/enemymarkers),
tag `v1.4.0`, revision `489f67e3fe2c4eaf3345b1665f5228d4ea74409d`,
`Component.cs:266-303,392-422,700-723`;
vanilla `EFT/Player.cs:28272`, `EFT/GameWorld.cs:2012-2057`;
`../Redeploy/Redeploy.Core/View/CameraRebindHelper.cs:31-69`.

## Amand's Graphics: visual test candidate

The released plugin is `com.Amanda.Graphics`, version 1.8.0. Its `Player.Init`
postfix reassigns the local player. Camera activation can reacquire the destroyed
FPS camera and its postprocessing components. This contradicts a blanket claim
that it is stuck on the first player or first camera.

The remaining overlap is visual: Arena lighting on Factory/Labyrinth, NVG and
pain/blur effects, UI depth of field, killcam, spectator view and the replacement
camera. The Forge page also lists Labyrinth as absent from its map list. Test
those paths without assuming unsupported map settings work like Factory.
No source-based reason for a blanket block or speculative body adapter was found.

Sources: [player rebind](https://github.com/ArchangelWTF/AmandsGraphics/blob/5ad92998bc6acdaf9ba90286485e9a96d9c3d962/AmandsGraphics/Patches/AmandsPlayerPatch.cs),
[camera activation](https://github.com/ArchangelWTF/AmandsGraphics/blob/5ad92998bc6acdaf9ba90286485e9a96d9c3d962/AmandsGraphics/AmandsGraphicsClass.cs#L610),
[release](https://github.com/ArchangelWTF/AmandsGraphics/releases/tag/v1.8.0).

## HUD Breakout: current-player polling, overlapping presentation

`HudRoot.Update` resolves `MainPlayer` every frame and sends it to the vitals,
effects, equipment and movement views. It does not have ImpactCue's identified
retained-body cache. Its displayed counters can persist across bodies sharing
the same profile ID; that is not by itself a bug for a continuing Arena match.

Killfeed, damage numbers, health/ammo panels, vanilla-HUD suppression and hit
audio need an explicit presentation choice. Arena's **Arena HUD** mutator
disables its combat panels, killfeed and hitmarker visuals, but intentionally
leaves score, round notices, team markers **and audio** independent. Turning it
off alone does not prevent double hit sounds. HUD Breakout's damage input also
does not guarantee Arena effective-HP/shield semantics.

Source: [HUD Breakout 1.2.0](https://github.com/Niharkanta1/SPT.HudBreakout/tree/87d5537b14bae99b99182ff5f98b757588e79456),
`Core/HudRoot.cs:146-249`, `Core/Patches.cs:14-103`;
local `ArenaMode.Client/Config/Settings.cs:1563-1573` and
[HUD mutator boundaries](HUD-MUTATORS-REVIEW.md).

## Manimal's Quick Cancel: narrow hooks; test interrupted throws

The quick-reload command checks the current `GamePlayerOwner.Player` and its
current medical hands controller. It starts native `TryThrowItem`, then a
`MedsController.Drop` prefix shortens the matching consumable outro. It does not
keep one global player reference or directly bypass native item restrictions.

A static pending-controller set is normally drained by the drop/failure path.
It has no explicit raid/body retirement cleanup. A cancelled operation that
never reaches those paths could retain a controller; this is a conditional
lifetime risk, not a reproduced leak or demonstrated corruption. Check it with
death/live-kit replacement, Continuous Healing, HealingAutoCancel, accelerated
surgery and backpack consumables before deciding whether Core needs an adapter.

Source: [Quick Cancel 2.0.0](https://github.com/danauraborealis/ManimalQuickCancel/tree/2003d9e4280dadb8f00f388e14be39d0152ca138),
released `GamePlayerOwnerTranslateCommandPatch`, `MedsControllerDropPatch` and
`QuickCancelState`.

## Picture in Picture Disabler: foreign optic callbacks and retained meshes

The 2.1.0 postfixes process every `OpticSight.OnEnable/OnDisable`, without
checking local ownership or whether another prefix skipped the original.
Arena's `KitOpticPreloadGuard` deliberately skips those original callbacks for
unmounted role models created while preloading kit resources. Harmony still
runs postfixes, so PiP receives these foreign optics.

While scoped, `OnOpticEnabled` can switch the active optic and alter reticle,
lens, mesh and camera state before its later local-scope check. More directly,
**any** `OnOpticDisabled` hides the reticle and releases its command buffer.
If the local player remains scoped, the regular state check does not perform a
new scope entry, and `UpdateTransform` returns when the buffer is null. This
supports a conditional lost-reticle path during cold preload; exact native
reproduction is pending. Scope re-entry or another rebuild path can recover it.

Separately, `MeshSurgeryManager._raidCaches` is keyed by weapon ID and retains
weapon/profile/mesh state. The only complete clear is reached from plugin
shutdown, not ordinary scope exit or raid end. Arena's many new kit item IDs
can accelerate accumulation. This is a source lifetime finding, not a measured
memory cost or FPS regression. Normal enabled scopes use mesh surgery; the
reviewed defaults enable the mod and bypass NV/thermal scopes.

Prefer a general local-optic ownership filter and bounded cache retirement
upstream or in shared Core compatibility. An exemption specific to Arena's
preloader would stay Arena-owned. Test with the already admitted FOV Fix,
ADS during cold bot/kit spawns, repeated weapon changes, death/live replacements,
NVG/thermal bypass, killcam and a second raid in the same process.

Source: [PiP Disabler 2.1.0 source](https://github.com/Fiodorwellfme/PiP-Disabler/tree/3258ebc65cdfffc454f045544e40d31f102d2c45),
`Patches/ScopeDetectionPatches.cs:13-42`,
`ScopeLifecycle.cs:134-216,315-324`,
`ReticleRenderer.cs:250-275,506-525`,
`MeshSurgeryManager.cs:236-278,511-525`, `PiPDisablerPlugin.cs:52-62`;
local `ArenaMode.Client/Kits/KitOpticPreloadGuard.cs:145-153,191-218`.

## Follow-up order and acceptance boundary

1. Fix Use Items Anywhere patch composition and verify ordinary PMC behavior
   as well as Arena, with both configuration orders and Arena mutator states.
2. Add the demonstrated ImpactCue and Enemy Markers body corrections to Core;
   test standalone Redeploy and Arena, including a retained own corpse.
3. Resolve PiP callback ownership/cache lifetime before a long kit-changing raid.
4. Test Amand's Graphics, HUD Breakout and Quick Cancel without inventing adapters
   first. Separate one-mod controls from combinations with admitted mods.
5. Admit exact client/server identities only after those native checks pass.

For body-sensitive mods, acceptance must include initial supplied-kit replacement,
at least two death respawns, a living round/wave refill where applicable,
post-respawn real firing/reloading/healing, leaving the raid and a second normal
raid. A menu launch or quiet log alone does not establish feature correctness.

Release archives, decompilations, source snapshots and per-group reviews are
retained locally under ignored `artifacts/compat-seven-20261025/`. No downloaded
third-party implementation or DLL is part of this documentation commit.
Released-binary control-flow inspection supports these findings; it does not
certify clean reproducible builds. In particular UIA carries a `-dirty` stamp,
and Quick Cancel's informational hash names a parent of its release tag.

## Released DLL identities

Hashes identify the files statically inspected, not files installed or loaded
on a GPU host. Plugin versions differ from AssemblyVersion for HUD Breakout
(0.0.0.0) and PiP (1.0.0.0); admission should use the plugin metadata below.

| Plugin GUID | Plugin version | DLL SHA-256 |
|---|---|---|
| `com.cj.useFromAnywhere` | 2.1.6 | `51B9CB02DFDC69568299988EAC37BD6C8F5732D49FE1E04DAB8D6592F87F2346` |
| `com.dizzlow.impactcue` | 2.0.3 | `2053A276C950583436F7675A1B76BD3F8E032267013345258B746E0D97D6071C` |
| `com.dizzlow.impactcue.webconfig` (server) | 2.0.3 | `1FC20AECE2987445724564022FAC90B5A63D43CCBCEAE418918B1D2C2C5DCF11` |
| `flir.enemymarkers` | 1.4.0 | `CB1CD678019A32D1B8095B94C406E07AD3C5E2A5CE08FEFA2A92FC17C6FDA415` |
| `com.Amanda.Graphics` | 1.8.0 | `0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD` |
| `com.deadwolf.hudbreakout` | 1.2.0 | `317BAD15E91848B6ACEF387CF9EF3F48C9E0A4AED63328E03155BFE194560A34` |
| `com.manimal.quickcancel` | 2.0.0 | `3C6C87112A7C0739C505A7F204DB2E3F13932DFDC8C568519E34B8BF685463BC` |
| `com.fiodor.pipdisabler` | 2.1.0 | `12712B3FDE8BC5C24031B2BFB2B54FF9383582E72F2608148CD391E2E4B83B44` |
