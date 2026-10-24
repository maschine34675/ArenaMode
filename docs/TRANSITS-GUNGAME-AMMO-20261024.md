# Arena transits, Gun Game opening warm-up and loose ammunition

## Behavior

- Arena's confirmed per-raid location copy keeps its transit IDs but sets every
  transit record inactive. Native `TransitController.InitTransitPoints` then
  disables the matching scene objects and does not register them. Do not clear
  the list: missing IDs would leave the scene defaults active. Regular extracts
  and ordinary PMC/Scav location data are unchanged. Factory day and night are
  covered by the same policy; maps without transits need no special case.
- `InitialKit` now loads the opening kit plus the first three **selected and
  validated** Gun Game rungs in one opening batch. Custom ordering and the
  full-kit fallback use `GunGameMode.BuildLadder`, just like gameplay. Extra rungs
  do not draw another random base kit. The existing arm/world identity, discard,
  adoption, cancellation and required cold-load fallback remain authoritative.
  The slightly larger opening batch may add loading work; it removes that bundle
  work from the first two promotions. Later rungs still use the existing preload.
  `Off` stays off; `WholeCatalogue` reuses the same selected ladder and deduplicates
  the templates already covered by the opening batch.
- Full Arena kits and living Gun Game weapon swaps give half the former loose
  ammunition reserve for fixed-magazine weapons. Round the total up once and
  distribute it over the requested stacks (`45 -> 23`, as `8/8/7`). Loaded rounds,
  chambers, detachable spare magazines, own gear and zero-reserve pickups are
  unchanged. No catalog data or saved-setting migration is required.

## Offline evidence

- `Run-ArenaVerification.ps1 -Harness tools/KitPolishChecks/Verify-Supplies.ps1`:
  2,326 unit tests passed; one existing environment-gated APBS test skipped.
  Real catalog supplies checks and their compiled negative controls passed.
- `ArenaTransitTests` runs the real server patch for ordinary, merely armed,
  foreign-session, disabled-map and finalized-session controls, plus copied
  transit records, day/night and all five Arena modes.
- `KitPreloadPlanTests` covers three-stage ordering, shared-template deduplication,
  short/repeated ladders, Off, InitialKit and WholeCatalogue.
- The supplies harness builds actual kits and weapon-only promotions from the
  installed item database, including odd stack sizes, zero spares, unchanged
  chambers and filled detachable magazines.

- `ZombieKitWarmupChecks`: 96 assertions and ten compiled negative controls
  passed. The real asynchronous preloader covers promotion during adoption,
  warm second/third stages, and the fourth stage's required cold-load fallback.
- Release client/companion and server builds passed. The existing compiler
  warnings were four nullable-context warnings in client shared models and two
  Razor warnings on the server. No new compile errors.

## Native GPU-2 validation

Tested 2026-10-24, 19:54–19:59 local time, source `131b9a9`, version 0.2.0.
Client MVID `4c626e35-3b77-48d5-bd6d-70c81f8e64ec`; client, companion and server
SHA-256 agree between build, VM installation and GPU-2 disk. UiBridge reports
16/16 plugin/library identities matching, including the loaded client/companion.
The SPT server was started after the new server DLL synchronized.

- Factory day, Gun Game, four participants: all three actual scene transit
  objects inactive, and native `TransitController.pointsById.Count == 0`.
  At the Woods transit center `(23.7, 1.1, 61.6)`, after transit availability
  and for longer than its normal 20-second countdown, the player remained on
  Factory for 23 seconds without a transit. The test teleport used the bridge's
  supported `reachable=false` because this exterior pocket has an incomplete
  navigation path; floor/room/zone checks still passed.
- Loading-screen warm-up: **49 templates in one opening batch, 1.1 seconds**,
  completed before raid start. The adopted warm set contained 50 templates
  after the separate Molotov supply. The test initially specified old static
  catalog IDs (`rung048/rung008/rung074`); the live generator uses `rung01...24`.
  Their three warnings were expected invalid-ID fallback, and the generated
  first three stages were what loaded. Custom valid ordering is covered offline,
  not claimed as a native result of this run.
- Two real player kills advanced PM (t) → KS-23M → MP5K-N. Both completed with
  `_loadoutDirty == false` and `_preloading == false`, and no cold load of
  rung02/rung03 was required. One surplus scripted trigger landed while the
  second weapon was being holstered; UiBridge refused it, and the subsequent
  probe confirmed a normally drawn MP5K-N. No weapon-change delay benchmark is
  claimed from that scripted sequence.
- The live KS-23M promotion carried **23** reserve cartridges (`8/8/7`) with
  three requested reserves, versus 45 previously. Factory night, Deathmatch,
  ten participants: all three transits inactive and full Comrade kit carried
  **150** loose rounds (`5 × 30`), versus 300 previously.
- Both protected exits returned to the main menu. All **303** original inventory
  items match, allowing only two independently logged startup ProfileFixer
  additions of `StackObjectsCount=1` before either raid. All saved Arena config
  values match the pre-test snapshot. Godmode, infinite ammo and tracking are off.
  A protected hidden participant-setting write was refused; the night test kept
  its existing ten-player setting, and no hidden-setting bypass was used.

Ignored evidence: `artifacts/transits-warmup-ammo-20261024/native/`, especially
`verified-summary.json`, `verify-evidence.mjs`, the two raid reports,
`transit-stand-accepted`, inventory probes, build identity snapshots and merged
logs. Verification token: `NATIVE_TRANSIT_WARMUP_AMMO_VERIFIED`.

## Log findings, separately scoped

The merged client/server/EFT logs are **not error-free**. EFT unconditionally
logs its `[Transit] Flag... Count...` state at Error level in
`LocalTransitController` even without an exception; that line is not a failed
transit shutdown. Other entries include native spawn-category reconciliation,
`JetMaterial == null` during model loading, one Model 700 animation event warning,
and Fence armor-slot warnings.

At 19:57:56, during the first protected exit, one native `BotFirstAid` completion
callback raised a null reference through `MedsInHandsOperation.Finish` and health
effect updates (duplicated across EFT and Unity logs). Attribution to Arena,
Redeploy/Core or another patch is unproven; none of the three changed paths
appears in the stack. Keep this as a focused lifecycle follow-up. The early
night exit also reproduced six already recorded canceled-solo-preparation
warnings. Neither should be hidden by these changes.

Implementation commits: `6b09c84` (transits), `6380270` (reserve ammunition),
`131b9a9` (Gun Game warm-up).
