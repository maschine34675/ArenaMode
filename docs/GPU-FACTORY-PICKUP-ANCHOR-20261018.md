# Factory pickup anchor — native GPU2 regression, 2026-10-18

## Result and tested identity

The previously failing isolated Factory origin no longer rejects all pickups.
Factory night independent Team DM placed **6/6 on the first attempt while the
player was at the exact problem coordinates**. Factory day Deathmatch also
placed 6/6 immediately; after moving the player to the same island, two available
pickups exchanged positions at their normal 60-second deadline.

- Commit: `321644aeb23243b819b4e025c247a40c5eecd637`.
- Host/profile: SPT-GPU-2, disposable test profile SPT-GPU-2.
- EFT 0.16.9.5-40743, Unity 2022.3.43f1; UiBridge
  `0.9.4+42bbfb38e6ea3536b579db4312880ebda1f7b0cf`.
- EFT instance `e1155abe`, started at 23:48:26 host local time (UTC+02:00).
- UiBridge verified VM build = host disk = loaded process for all 17 listed
  maschine plugins and both libraries before testing and after both raids.
- BepInEx disk/console logging already used `All`. DeveloperMode remained false;
  LogDamageExceptions remained true. No diagnostic setting was changed.

| Module | SHA256 | MVID |
|---|---|---|
| maschine-ArenaMode.Client.dll | `6594288AD5CF52D01E22F2E7E96B03174FFDAA859E7C43B6308B519BC34091FA` | `be2866a9-b7f4-4727-8661-777d11a78ad6` |
| maschine-ArenaMode.BigBrainCompat.dll | `2AB90BBB8680510C9455ED424F12334E8AB1112953FBF42AF5F4BC41B7014E54` | `ab2a926f-ccc5-41d7-869f-6736a0d1edd0` |

The module IDs were read from the matching host files; UiBridge's loaded-module
comparison establishes the same IDs in the running process. Both ProductVersions
are `0.1.0+321644aeb23243b819b4e025c247a40c5eecd637`.

## Factory night: initial placement from the isolated origin

The native menu selected independent Team DM, 4 participants (2v2), 50 kills,
5 minutes, pickups on and killcam off. The scene was `factory4_night`.
During team assembly, the existing UiBridge teleport moved the test player from
the ordinary start to `(12.611, 1.186, 60.104)`. Only its reachability requirement
was explicitly disabled for this setup; its NavMesh, hazardous-zone and standing
clearance checks still passed. No production spawn record, world field or runtime
method was changed. UiBridge godmode protected the stationary test actor.

At **23:49:54.008**, the first placement pass logged:

```text
actor=(12.611, 1.186, 60.104)
anchor=(-32.087, -2.653, 29.973)
placed=6/6; alternatives=19; geometryProbes=191
refusals=[another pickup is within 3m=8,
          no nearby floor on the navigation mesh=15,
          pedestal clearance obstructed=162]
```

The upper authored point still required the existing 0.5m repair from
`(35.924, 4.554, 35.339)` to `(35.570, 4.533, 35.693)` because its original pedestal
clearance was obstructed. Native geometry rejection and repair remained active.

A read-only teleport probe from the problem origin to the known interior point
`(2.411, -2.640, 49.400)`, with ordinary reachability required, was refused with
`422 unsafe_position: not reachable on foot from your position`. This confirms
the local-origin connectivity problem still existed during the successful pickup
placement. The scene/navigation was not repaired or bypassed by the test.

## Factory day: normal placement and rotation from the island

The second native menu selected Deathmatch, 4 participants, free-for-all,
continuous play without kill/time limits, pickups on and killcam off. The scene
was `factory4_day`.

At **23:50:58.814**, actor `(72.104, 0.231, -73.881)` used the same independent
anchor and placed **6/6 in one pass**: 18 alternatives, 182 geometry probes.
The same upper point needed its 0.5m clearance repair. A read-only runtime
inspection confirmed six `ArenaPickupRuntime.Entries` during the raid.

The player was then moved to `(12.611, 1.186, 60.104)` and remained there until
normal exit. A second ordinary reachability probe to the interior was refused.
At **23:51:58.816**, exactly two distinct available appearances rotated:

| Entry | Previous floor | New floor |
|---|---|---|
| factory-boots | `(-5.890, 0.031, -27.460)` | `(-32.087, -2.669, 29.973)` |
| factory-ammo | `(-32.087, -2.669, 29.973)` | `(-5.890, 0.031, -27.460)` |

Both advanced to appearance 1. This proves the live exchange path no longer
depends on reaching the pickups from the isolated local actor.

This is **not** acceptance of all six rotation paths. Bots collected damage at
23:51:34.217 and shield at 23:51:51.322, so these entries were cooling when the
exchange occurred. Health correctly retained its previous position because the
candidate probes found missing navigation, obstructed pedestals and reserved
positions within 3m. The weapon's later rotation deadline was not observed.
The bounded run ended after the first valid exchange; no second full rotation
cycle or local weapon/powerup collection was tested.

## Logs, protection and cleanup

Both raids exited through normal Disconnect and the native result flow. Server
logs confirm persisted protected snapshot restoration and protected Arena end
at 23:49:56 and 23:52:01 host local time. The test profile's complete PMC Inventory
was identical before/after, with 314 items on each side. Health and Hideout
sections differed; no whole-profile byte identity is claimed, and no raw profile
backup was written over the running server's profile.

Read-only inspection after return showed zero Arena pickup entries. Test godmode
was switched off before each disconnect. EFT then quit gracefully; UiBridge
confirmed it unreachable for five seconds. Only after EFT stopped, the original
Arena configuration was restored and its SHA256 matched the saved baseline:
`BB6F8E68B59BF4ECB5C264DA3F1030A08BB329A2AA3B6FB722AA3147598686DB`.
The existing server remained running (PID 13496); no server/Core binaries,
profile inventory or server configuration were changed.

No BepInEx Error or Arena warning was recorded in this process. The broader logs
are not warning/error free: EFT reports Factory spawn-marker category repairs,
transit diagnostics, `JetMaterial == null` and missing `mod_stock_003` bones in an
870 stock model; Unity reports the Woods transit's negative BoxCollider scale and
missing extractor-smoke scripts. The server reports profile lookup fallbacks and
Fence armor-slot warnings. No pickup exception or failure accompanies these
messages. Their broader causes were not investigated in this scoped regression.

## Evidence and limits

Local evidence is under
`artifacts/factory-pickup-anchor-20261018/native/`: both scenario JSON files and
their reports, BepInEx/server/EFT logs, module identity JSON, two game frames,
configuration before/after/restore receipts and profile comparison evidence.
The game frames show the stationary actor in the enclosed exterior origin;
they do not visually survey all six pickup markers.

The generic scenario reports say `FAILED` because they treat the intentionally
refused native path probe as a failed tool step. The day scenario also records a
50-second wait ending before the 60-second rotation deadline; the following wait
matched the real exchange nine seconds later. Their placement and rotation
assertions passed, and every normal-exit/cleanup step passed. The original reports
are retained without rewriting their results.

This evidence covers full-map Factory day Deathmatch and Factory night independent
Team DM on this installation. It does not certify custom Pickups.json layouts,
bounded areas, every Factory spawn, all six cooldown/rotation cases, or long-run
performance. No subjective audio or local pickup-transfer acceptance is claimed.
