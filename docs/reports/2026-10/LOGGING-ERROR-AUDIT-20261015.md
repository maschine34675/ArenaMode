# Runtime warning/error audit — 2026-10-15

Read-only diagnosis for the logging cleanup based on source baseline `9f29f49`.
No gameplay changes, global log filtering, config edits, builds, deployment or GPU
actions were made for this audit. Line numbers below refer to captured log bytes,
not to a later appended/synchronized version. Raw logs and IP/profile data remain
private in ignored artifacts; this report deliberately omits those values.

## Result and limits

The two newest clients emit **no ArenaMode Warning/Error entry**. This is **not**
an Arena gameplay acceptance result: MASCHINE11 is an ordinary Air Pit visit with
Redeploy free-camera use; GPU2 only reaches menu/hideout and later shuts down.
Neither log contains an Arena arm/match start. Older actual Zombie tests still
contain a real damage-callback exception whose cause is not settled.

The current conspicuous error burst is **630 identical native decal exceptions
in 0.313 seconds during Air Pit shutdown**, followed by one weapon animator
exception. The decal subsystem is owned by **ArenaMapPort**, not ArenaMode. It
should be corrected at its teardown boundary, not hidden by ArenaMode logging.
There is also one unrelated UiBridge network-binding error on MASCHINE11.

## Evidence identity and chronology

All four files were copied once into `artifacts/logging-20261015/audit/`.
`analyze.py` reparses those snapshots and emits per-signature counts, timestamps,
first/last lines, stacks and a SHA256 manifest. It parses actual log severity
headers rather than counting words such as `Error` inside Harmony method names.

Client timestamps are Europe/Berlin, **UTC+02:00** on this date. Server lines are
UTC, so add two hours to correlate. The server files span multiple sessions for
the entire day; their totals are not errors from the latest client alone.

| Captured file | Bytes | Source last-write UTC | Warning | Error |
| --- | ---: | --- | ---: | ---: |
| `BepInEx/LogOutput-MASCHINE11-2026-10-15_17-31-27.log` | 4,008,946 | 15:43:40.214 | 9 | 632 |
| `BepInEx/LogOutput-SPT-GPU-2-2026-10-15_15-49-44.log` | 1,839,619 | 15:30:54.568 | 13 | 0 |
| `SPT_Runtime/user/logs/spt/spt-MASCHINE11-20261015.log` | 368,029 | 15:47:23.049 | 29 | 0 |
| `SPT_Runtime/user/logs/spt/spt-SPT-GPU-2-20261015.log` | 663,168 | 15:30:53.833 | 141 | 0 |

Source creation UTC times, useful to identify synchronization rather than game
startup: respectively 15:43:51.925, 15:30:57.998, 15:47:42.147 and 15:31:07.853.

SHA256 in the same order:

```text
62D91EDCE1299F3C8F2B4F3C2AEF7CA5D441DBA64BA03C6EEBCAB9F72CA687D1
B84907DAC6D9BD2C273C3205C28008BA643A48E51E8882A9DD211431A3EBB8A1
F4B01409363E4037C5C22DBA3CA6EB7F92D8AE18450397DF7630DF9F78756EB2
71B46FA76712F1969E96E3D37C1DB10898A2C3EA0EF76DA20B2146DDD50BEA44
```

Both clients identify ArenaMode `0.1.0` and Redeploy.Core `1.0.7.0`. LogTools lists
the client file date as 2026-10-14 12:49 and companion as 2026-10-12 11:19. They do
**not** log Arena's informational build commit or loaded SHA256. The current disk
DLL or a prior handoff hash cannot establish what these exact processes loaded.
Do not label these logs `9f29f49` or claim they test this logging change. A single
startup build identifier is useful player-support Info for future reports.

## Complete current client signatures

Counts are MASCHINE11 / GPU2. Shared warning families below retain their distinct
member names, so grouped totals cover every captured Warning/Error header.

| Signature / source | Count | Attribution and evidence |
| --- | ---: | --- |
| LogTools cannot create `LogOutput.log` symbolic link | 1 / 0 | Host permission/configuration. Explains why there is no current alias; unrelated to ArenaMode. Do not elevate the game merely to silence it. |
| FixPluginTypesSerialization excludes optional Fika assemblies | 4 / 4 | `maschine-MedicRound.Client.Fika`, `Tyfon.UIFixes`, `Tyfon.UIFixes.Net`, `WTT-ClientCommonLibFika`; missing Fika is explicit. No Arena assembly involved. |
| HarmonyX inherited-method warning: `BattleUIScreenShowPatch` | 1 / 1 | DynamicMaps; reference source `SPT-DynamicMaps-main/Plugin/Patches/BattleUIScreenPatches.cs`. |
| HarmonyX inherited-method warning: `DialogWindowPatch` | 1 / 1 | UIFixes confirmation-dialog patch; `UIFixes-main/src/Patches/ConfirmationDialogKeysPatches.cs`. |
| HarmonyX inherited-method warning: `WeaponModdingScreenCursorPatch` | 1 / 1 | UIFixes weapon-pan patch; `UIFixes-main/src/Patches/WeaponPanPatches.cs`. |
| HarmonyX inherited-method warning: `com.wtt.commonlib`, quest `IsConditionDone` | 1 / 1 | Explicit other-mod patch identity, before any raid. |
| UiBridge `BindAddress names no address of this machine` | 1 / 0 | MASCHINE11 line 9066, 17:31:36.507. Configured host addresses do not belong to this PC. No Arena failure, but automation cannot bind that endpoint. No address/config changed here. |
| `StaticDeferredDecal.OnDisable` → `RemoveDecal` → `ComputeHash` → `Material.get_mainTexture` NRE | 630 / 0 | MASCHINE11 lines 31689–38608, 17:43:38.982–17:43:39.295. Native decal lifetime failure in the ArenaMapPort-owned renderer, detailed below. |
| `Player.OnDestroy` → `FirearmController.Destroy` → `SetBoltCatch` → `Animator.SetBool` NRE | 1 / 0 | MASCHINE11 line 38619, 17:43:39.305. Native player/hands disposal during process teardown; underlying destroyed animator is unproven. No Arena callback frame in throwing stack. |
| Unity renderer registered with two LODGroups, `light_CellPlafonds_holder_LOD0` | 0 / 4 | GPU2 lines 15450–15453 at 15:50:34.092–.095, before hideout body creation. Asset/scene warning, no Arena match running. |
| Unity `Can not play a disabled audio source` | 0 / 1 | GPU2 line 15461 at 15:50:41.704, during hideout construction. No stack identifies an audio owner; cannot specifically blame/clear every other mod. Arena announcer/pickup playback has not started. |

### Air Pit decal shutdown: source-backed failure path

At 17:34:05.388, ArenaMapPort reports its private native renderer created with
722 source decals, 705 active/registered, 70 source materials and 26 draw groups.
The whole visit has no ArenaMode arm or match start. At application exit the 630
errors occur, and only afterwards, at **17:43:39.310**, ArenaMapPort reports its
renderer removed, releasing 26 owned materials and 30 command buffers.

Relevant inspected source:

- `../_decompiled/StaticDeferredDecal.cs`: `OnDisable` invokes the static
  unregister callback unconditionally.
- `../_decompiled/StaticDeferredDecalRenderer.cs:269–280,341–343`: unregister
  calls `RemoveDecal`, which recomputes a hash from `decal.DecalMaterial.mainTexture`
  before checking membership. There is no lifetime check.
- `D:/ArenaMapPort/ArenaMapPort.Client/NativeDecalRepairs.cs`: installs a private
  renderer and subscribes via native creation. Cleanup runs from the runner's
  `OnDestroy` or `SceneManager.sceneUnloaded`; the helper's `OnDisable` only
  captures resources. Native unsubscription happens as that renderer is destroyed.

The observed ordering allows scene decal/material destruction while the renderer
still receives unregister events. The exact component/material instance that died
first is not recorded, so material destruction ordering is a supported explanation,
not an individually identified object. Ownership and failing callback chain are
established. A future **ArenaMapPort** fix should detach only its owned renderer
before scene resources are destroyed, retain resource capture/disposal, and verify
both raid-to-menu and application-exit paths. A blanket exception finalizer, clearing
global decal delegates, or skipping all native deregistration in ArenaMode would be
the wrong scope and could break other maps/renderers.

### Weapon disposal exception: keep attribution conservative

Native `EFT/Player.cs:13495–13516` checks `_firearmsAnimator != null` then calls
`SetBoltCatch(false)`, but the wrapper's Unity animator can already be destroyed.
The stack ends in `Player.OnDestroy`/`LocalPlayer.Dispose`, in the same shutdown
burst. Arena's `ArenaHandlingPatches.ArenaHandlingControllerDestroyPatch` is indeed installed
on that method; its prefix releases only previously owned handling leases and does
not write bolt-catch state or destroy the native animator. The current visit is not
an Arena session, so no corresponding movement lease was acquired. Presence of the
Harmony wrapper alone does not prove causality. Classify **unknown native shutdown
lifetime interaction, no demonstrated ArenaMode cause**, not a confirmed fixed bug.

## Complete current server signatures

All are warnings; neither server snapshot has an Error/Fatal/Critical header.

| Signature | MASCHINE11 | GPU2 | Assessment |
| --- | ---: | ---: | --- |
| FenceService missing `Front_plate` slot while randomizing armor | 5 | 56 | Native Fence assortment generation, including server startup before any client/Arena arm. Generated item IDs do not identify the donor template. No evidence that Arena causes the malformed rows; exact mod/database donor remains unknown. |
| FenceService missing `Back_plate` slot | 6 | 51 | Same family; do not conflate with Arena's front-plate display calculation or suppress in Arena. |
| TraderHelper missing trader; temporary refresh interval | 2 | 2 | Trader ID matches `FriendlyCourierTraderProfile.CourierTraderIdValue` in the installed-version PFT reference. Initialization order of PFT courier trader; not an Arena-defined trader. |
| ProfileFixerService repairs undefined `StackObjectsCount` | 2 | 8 | Login/profile repair, before Arena selection. The original donor of the malformed item is not identified. No Arena kit/profile claim justified. |
| ProfileController cannot show requested account, falls back to own | 14 | 24 | Repeated `/client/profile/view` requests during startup/PFT teammate setup, before Arena arm. Account lookup issue; no Arena runtime involvement demonstrated. |

MASCHINE11 has two startup groups (09:33 UTC and 15:31 UTC). The latest client
starts at 17:31 local / 15:31 UTC. Mixing the earlier nine-o'clock warnings into
that visit would falsely increase its error count. GPU2 similarly contains several
login/test sessions earlier than its 15:49 local startup.

## Older actual Arena gameplay: unresolved Zombie exception

The newest two logs do not supersede the real damage exception documented in
[GPU-ZOMBIE-REVIEW-20261014.md](GPU-ZOMBIE-REVIEW-20261014.md). Its private
`artifacts/zombie-review-20261014/process1-log.json` was inspected again here;
it contains **seven Warning entries with real NREs**, despite zero Error headers.
Six describe an already released/destroyed aggressor, one a living aggressor.
The earlier report also records two matching NREs in the separate fresh ON run.
These belong to the older reviewed benchmark builds, not today's menu-only log.

The stack is `Player.ApplyDamageInfo` → event delegate →
`BotMemory.GoalTargetBeingHitAction`, native offset `0x0005e`; it is **not**
`BotMemory.GetHitInner`, which is the different deferred bullet path protected by
the existing two-second corpse grace period.

Native source verifies:

- `BotMemory.GoalEnemy` subscribes this callback to the target's `BeingHitAction`
  (around lines 220–227).
- `GoalTargetBeingHitAction` (803 onward) dereferences owner, melee state and
  `GoalEnemy.Person`, with no null guard, after comparing attacker/owner ID.
- `Player.BaseKnifeController` constructs melee `DamageInfo.Player` using
  `GameWorld.GetEverExistedBridgeByProfileID(_player.ProfileId)` (Player.cs:18344).
- Arena's damage finalizer reports and **returns the original exception**. It does
  not fix it or manufacture successful damage.

The logged victim/aggressor pair does not identify the callback-owning BotMemory.
A stale retired callback, a missing current goal, and a reused profile ID resolving
to an old ever-existed bridge remain distinguishable hypotheses. Destroyed
aggressors ten or twelve seconds after release deserve investigation; merely
extending corpse grace or declaring SAIN responsible would not explain the source.
Native melee is deliberately enabled by Arena's Zombie path, so an **indirect
Arena lifecycle contribution is possible and must not be dismissed**.

Recommended bounded next diagnosis: on a real callback exception, additionally
record callback owner's live body/state, current goal/person, melee object and
native knife owner versus current/ever-existed bridge identity. Keep this safety
exception visible without DeveloperMode; gate only expensive elective probes.
Do not suppress the native exception or alter damage/rosters without evidence.

The old benchmark's deliberate RespawnApiProbe throw is a separate test-hook
fixture; the current two logs contain no such probe-thrown error. It must not be
counted as a newly introduced production Arena failure.

## Log volume outside Arena's own severity calls

The latest MASCHINE11 log contains 14,653 Info headers: **13,154 HarmonyX** and
**1,133 ModulePatch** alone. GPU2 has 7,695 Info headers, including 6,245 HarmonyX
and 1,131 ModulePatch. MASCHINE11 additionally enables ModProfiler instrumentation
while in the map, which explains another patch-trace burst. Reclassifying Arena's
own messages cannot remove these other logger sources; do not silently install
a global logger filter to make the cleanup appear successful.

The local `BepInEx/config/BepInEx.cfg` was also inspected: Harmony `LogChannels`
includes `Info, Warn, Error, Debug`, and both console/disk `LogLevels` are `All`.
Thus moving an Arena message to Debug changes its category but does not remove
it from this installation's file. These global user settings were left intact.
Developer-only probes are still absent unless Arena's own switch is enabled.

Server `SptLoggerMiddleware` still reports `/arenamode/client-settings` every ten
seconds at Information. This is the server request logger, not the client
RequestHandler. Any future quiet-route treatment must be scoped to this endpoint
and preserve request failures; it is separate from changing ArenaMode's own
Info calls. No server logging configuration was changed for this diagnosis.

## Disposition

- No demonstrated current ArenaMode Warning/Error regression warrants a gameplay
  patch based on these newest logs alone.
- Confirmed Air Pit shutdown decal failure: record in ArenaMapPort, fix ownership
  ordering there; do not mask from ArenaMode.
- Single weapon animator teardown NRE: retain, investigate if it occurs during
  live raids or repeats after the decal lifecycle fix.
- Historical Zombie damage NRE: keep the safety alarm, gather callback-owner
  evidence during the next actual Zombie run. Not cleared by current menu logs.
- Unrelated startup/asset warnings retain their real ownership and severity.

This report certifies analysis of the captured bytes and local source only, not
native acceptance of the logging changes or absence of bugs in unexercised modes.
