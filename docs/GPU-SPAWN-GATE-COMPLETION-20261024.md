# Spawn-gate completion and DynamicMaps: GPU-2 results

Date: 2026-10-24. Host/profile: SPT-GPU-2. Source: `73c61f75988a3576e7e5b6c0299f9ef387819f37`.
Seven native raids: four independent Team DM 4v4 runs, one Bowl death/respawn
within its TDM run, and three short Deathmatch runs for complete map overviews.
See [the integration contract and offline checks](SPAWN-GATE-COMPLETION-20261024.md).

## Delivery

Client and BigBrain companion built in Release, deployed to `D:/SPT41` and
GPU-2 `C:/SPT41`, then EFT restarted through gpu-run. No server code, map bundles
or MapPort settings were changed. Build succeeded with four existing nullable
annotation warnings and no errors. Built/installed/GPU files matched by SHA-256;
UiBridge confirmed both loaded modules match disk and the development install.

| Artifact | Identity |
|---|---|
| ArenaMode client | `0.1.3+73c61f7`; MVID `76f4411c-8c94-4c2c-beb6-67f88243c014`; SHA-256 `2eef664c6837ed889fc8fed89df09fef6dbf546729c647a22eb06c496f18789e` |
| BigBrain companion | `0.1.3+73c61f7`; MVID `8c99333e-9771-4d81-9144-03ec931c2930`; SHA-256 `79db1dde6071bc3b61a9a713e0b1cd225c0a33a63d2acac0337a6594ccb33a5c` |
| ArenaMapPort | Loaded `0.3.0+e652fc9`; MVID `85bf8a31-966a-4ded-9f70-a47ed6890659`; completion properties verified in the actual process |
| Redeploy.Core | `1.0.9+85be3fb` |
| DynamicMaps | `1.2.1`; MVID `9afffeff-fdf2-4246-a235-bc7c2dd40fd0`; loaded/disk/local identical |
| UiBridge | `0.9.6+0978cfe` |

The full offline runner passed: 2,307 unit tests, one known environment skip;
324 gate, 424 formation and 382 TDM runtime assertions, including the compiled
gate/formation negative controls. Offline verification did not change installed
payload hashes. The separate native evidence verifier also passed.

## Team DM gates

MapPort settings: `SpawnGates=true`, `SpawnGateCarve=true`,
`SpawnGateDrive=true`, `SpawnGateDriveScale=0.5`, developer hold off.
Times below are host local time. The delay measures completion notification to
Arena's successful path validation and transition to Live, not map loading or
team assembly.

| Map | MapPort complete | Arena Live | Release to Live | Delay after completion |
|---|---|---|---:|---:|
| Bowl | 15:02:03.791 | 15:02:04.049 | **7.01 s** | 258 ms |
| Equator | 15:05:39.051 | 15:05:39.552 | 4.00 s | 501 ms |
| Bay5 | 15:07:03.025 | 15:07:03.525 | 4.00 s | 500 ms |
| AirPit | 15:08:34.944 | 15:08:35.449 | 2.00 s | 505 ms |

Every run observed held gates during assembly (`IsHeld=true`, `GatesOpen=false`),
then `IsHeld=false`, `GatesOpen=true`, `IsOpening=false`, no hold carvers and no
pose fallbacks. Production code logged complete paths and physical clearance
for **all twenty** formation points before Live. Seven bots were present at the
open-state snapshot. Later snapshots show both factions outside their boxes.
Native player walks from the USEC box to the field arrived on all four maps:
Bowl approximately 10 seconds, Equator 6, Bay5 9 and AirPit 17.

Bowl's longest animation finished in approximately 6.8 seconds. The old
13.5-second minimum no longer applies: this run saved about 6.5 seconds after
release. The short remaining delay includes polling and scene validation.

The Bowl death was induced by UiBridge, followed by the actual enabled Killcam
and Redeploy pipeline. The player respawned at `arena-bowl-tdm-v1-east-02`
at 15:04:02.797; the subsequent inspection still showed open gates and no hold
carvers. The complete timeline contains exactly four owned holds, one per TDM
run, with no Bowl re-hold and none during the three later DM controls.

## DynamicMaps

The refreshed packages from MapPort commit `e652fc9` were already installed.
All eight JSON/SVG files matched local and GPU-2 copies before testing and
remained unchanged. Every map loaded its Ground layer, compass, player marker
and extraction markers. Full overviews were visually inspected, including
Bowl's east/west areas, Bay5's outer lanes and AirPit's two outer boxes and
turnstile rows. No DynamicMaps/tessellation failure appeared in the merged log.

Default player-centering clipped portions of the first Bowl/Bay5/AirPit screen
captures. Three extra DM runs captured the full view with zoom zero and
auto-centering temporarily disabled through non-persistent UiBridge settings.
Both settings were restored afterwards. These were rendering controls, not
extra combat/long-session tests. The maps depict the static open combat layout;
they are not a live display of individual gate states. No claim is made that
every contour or every heading was resurveyed in this test.

Local captures (ignored test artifacts):

- [Bowl full overview](../artifacts/gate-completion-20261024/native/bowl-overview/bowl-full-dynamicmaps.png)
- [Equator overview during TDM](../artifacts/gate-completion-20261024/native/equator/equator-dynamicmaps.png)
- [Bay5 full overview](../artifacts/gate-completion-20261024/native/bay5-overview/bay5-full-dynamicmaps.png)
- [AirPit full overview](../artifacts/gate-completion-20261024/native/airpit-overview/airpit-full-dynamicmaps.png)

## Logs: separate findings

The complete BepInEx/server/EFT warning/error export contains **208 entries**,
including startup, seven raids, exits and final menu navigation. This is not a
clean-log claim. No completion-contract error, gate timeout or failed TDM start
occurred. Other findings remain separate from this gate change:

- Native material/weather/season/spatial-audio helpers, marker reconciliation,
  missing scripts, Benelli foldable and unknown-item messages remain visible.
- Bowl leave at 15:04:08.554: a `SmallPhysicsObject` collision tries to load shell
  audio after `weaponsounds.bundle` has been unloaded. The stack is native
  collision/teardown, without an Arena gate frame; causality is not established.
- Bay5 at 15:07:19.089: one `FATAL There is no handler for hash:0` animation
  message, without a useful caller stack. The match and exit continued.
- AirPit leave at 15:09:06: Surv12 and a PP19 magazine from the **temporary
  supplied SD mod.2 kit** do not fit reconstructed grid indices; FastAccess9 then
  cannot find the Surv12. The server had already restored the original snapshot
  at 15:09:02.784 and persisted it at .818. None of those temporary IDs exists in
  the before/after original profile. UIFixes' enabled grid reorder combined with
  EFT's ordinal `GridNumber` serialization is a concrete hypothesis, not proven
  causation. Reproduce the same kit with inventory-open/leave and reorder on/off;
  do not hide the errors as a fix.
- Seventeen `Arena solo preparation ... The operation was canceled` warnings
  follow the early ends of the three DM overview runs. `BurstSpawnService` logs
  expected pending-preparation cancellation as Warning in its generic catch.
  This is separate cleanup log noise; the TDM gate tests did not emit it.

## Restoration and limits

All seven raids left via native Disconnect/Next with protected Arena restoration.
The final state is MainMenu, no raid, Godmode/infinite ammo/fire off. UiBridge's
pre-existing `killGuard=true` is unchanged. Arena and DynamicMaps values match the
initial live settings exactly; Arena and MapPort disk settings also match their
initial values. Equator's TDM count was restored from eight to six through the
native menu; its hidden config entry was not bypassed. No complete config or
profile file was overwritten to restore test state.

All **303 original inventory items** retain their IDs, templates, parents, slots,
contents and quantities. Two originally absent `StackObjectsCount` fields became
`1`; the startup ProfileFixer log explicitly records those normalizations before
the first raid. Inventory objects are otherwise identical. Skills, Stats, Quests,
Achievements and all other progress sections match. Body-part health matches;
energy changed 81 to 100, hydration 84 to 100, and health/hideout timestamps
advanced. These are reported rather than claiming byte-identical profiles.

Native scope: four 4v4 runs, one forced death/respawn, default animated drive
scale and three short non-team controls. Maximum population, legacy API,
malformed/throwing getters, instantaneous drive and timeout behavior were not
rerun natively; relevant contracts have offline coverage. Raw evidence lives in
`artifacts/gate-completion-20261024/native/`, including `verified-summary.json`,
`delivery.json`, all scenario reports, `gate-timeline-final.json`,
`warnings-final.json`, profile differences and saved final status/configs.
