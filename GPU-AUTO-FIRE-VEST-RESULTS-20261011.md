# Default fire mode and vest icon — GPU2 results

2026-10-11; user explicitly released SPT-GPU-2. Passed the focused native test.
Evidence root: `artifacts/autofire-vest-native-20261011/` (ignored, local).

## Exact build and isolation

- Fire-mode implementation: `436c51c`; vest icon: `9eaa904`.
- Running client: `0.1.0+9eaa90418ffe0effb93c98f77027fe8fb1c1327c`.
- SHA256: `6BCA5C490564240F004E8B1C2176375DDC95A642D0C0907F4139B633927249A3`.
- Staged manifest: `artifacts/double-jump-build/a03f10ab33f543f69bab34b39fce3904/manifest.json`.
- Fresh process `9df8d8ade7c841a0b891e3a983392f06`, started 17:19:50 local.
  Final UiBridge check: 43 plugins and five libraries matched local disk,
  GPU disk and loaded MVIDs. Server/Core/other mods were not deployed.
- Standalone `com.maschine.DefaultAutoFireMode/General/Enabled=false` before
  startup and throughout the raid, verified live and in the saved scenario
  reports. Its DLL/patches remained installed but its own enable guard made them
  inert. Restored to `true` after exiting. This test cannot be credited to it.
- Temporarily pinned fixed `spartan` kit, Deathmatch and two Factory participants.
  Godmode was used for deterministic input checks; the native kill hook triggered
  two respawns. Every test setting was restored; godmode ended off.

## Native observations

Factory day, continuous FFA, 17:21:18–17:26:09 local:

1. First M4A1 hands changed `single -> fullauto`; Arena log #2981 at 17:21:19.117.
2. `ChangeWeaponMode` selected single. It stayed single across several reads and
   more than 100 seconds while held; no per-frame reset occurred.
3. Using Analgin holstered and drew the same M4 again: `single -> fullauto`,
   #3054 at 17:23:41.440, same weapon ID. Grenade hands and return to the rifle
   were also exercised. A 450 ms held trigger consumed seven rounds (31 -> 24).
4. First native death/respawn produced a new M4 item/body and full-auto selection:
   #3093 at 17:24:22.786, new item ID ending `005a58` versus initial `0059fd`.
5. Second respawn used the `overwatch` kit. SVDS stayed `single` initially and
   after grenade-to-rifle redraw. A 450 ms held trigger consumed exactly one
   round (11 -> 10), with no Arena auto-selection log for that weapon.
6. New body-armor silhouette was visibly present at its normal 20 px HUD size,
   beside 100% body armor. Helmet, layout, tint and other HUD content unchanged.
   Screenshots in `manual-single/`, `redraw/`, `respawn/`, `semiauto/`.

An initial `SelectKnife` could not switch because this kit has no knife. The
legacy `ThrowGrenade` command did not equip a grenade; the actual native command
`PressThrowGrenade` did, confirmed as `GrenadeHandsController` via read-only
inspection. Neither no-op is counted as a redraw test. No gameplay code was
changed to compensate for those test inputs.

Protected Disconnect returned to MainMenu. Client export contains #1–#3204,
no dropped coverage, one unrelated stored line already truncated at 2,000 chars.
Zero Error/Fatal and no Arena warnings. Existing diagnostics: three Harmony
declaration warnings, the intentionally throwing RespawnApiProbe subscriber
and stale PerfectCulling cleanup on body replacement, plus Factory's native
`TRANSITS/Road_to_woods` negative BoxCollider scale.

## Preservation and startup normalization

Baseline was captured with both game and server stopped. The strict whole-profile
comparison is retained unchanged in `preservation.json`: two bookkeeping
timestamps plus eight previously missing `StackObjectsCount` values became `1`.
These eight entries belong to a pre-existing stash MDR 5.56 and its attachments;
its ammunition count stayed 30, and item IDs, parents and all other data match.

This was independently investigated, not silently ignored: server-profile.log
lines 17287–17294 explicitly records SPT's `ProfileFixerService` repairing exactly
those eight IDs at **15:20:01.995–.997 UTC**, before Arena took the snapshot at
**15:21:01.649 UTC** (line 17395). Restore was logged at **15:26:09.388 UTC** and
protected persistence at **15:26:09.422 UTC** (17694/17698). Native server source
`GameController.GameStart` calls `FixProfileBreakingInventoryItemIssues`, which
fills these missing values. No tester profile write or generalized stack-field
exception was used. `classify-preservation.mjs` validates exactly these eight
IDs, before/after values and pre-arm log ordering; its accepted result is kept
separately in `preservation-adjudicated.json`.

All twelve companion files are byte-identical. Every existing configuration
assignment matches the baseline. Across 107 recorded DLL paths only the intended
Arena client changed locally and on GPU2. The new HUD attribution file was copied
alongside the client in both installations; unrelated Molotov assets were not
deployed or committed. Game left at MainMenu, server running; no active test hook.

## Offline checks and scope

Production-linked fire-mode checks: **46 assertions + 11 compiled negative
controls**, restored green baseline. Real staged client: zero errors, four old
nullable warnings. Built resources: nine source-identical imported icons and five
current generated icons matched their recorded bytes; seventeen retired icons
absent, one Arena plugin, no standalone panel dependency. Root independently
reviewed the icon diff, 20 px preview and license record; helmet hash unchanged.

This is native acceptance for the tested M4A1/SVDS draws, two replacement bodies
and the visible vest HUD, not every weapon, pickup, own-gear/PFT path or resolution.
Ordinary-raid/bot exclusion and other unsupported modes retain source and
production-linked harness coverage; no separate ordinary PMC raid was run here.
