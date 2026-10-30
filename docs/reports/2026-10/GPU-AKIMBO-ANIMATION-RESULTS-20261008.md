# Akimbo: independent presentation and doubled cadence — GPU-2, 2026-10-08

## Build and scope

Implementation `5bf58d7`; native event-order correction `5327906`.
Client loaded as `0.1.0+532790615b7a95e612d714e49bd6d03c4081441a`, MVID
`4480c9a2-b8cf-440b-b6de-1fa284faa75a`, SHA256
`DE9C48C3FCBC282C8BCA67E0137BB7C42FF6E1D50A46F599DDB5A6912A40CC93`.
UiBridge confirmed local DLL = GPU disk = loaded process. Client-only deployment
to development installation and GPU-2; no server/Core/companion or asset changes.
Untracked Molotov assets were not added or deployed. Isolated build manifest:
`artifacts/double-jump-build/1bd63d2e50b141778d6a030df45bd76c/manifest.json`.
Zero build errors; four existing nullable warnings.

One live Gun Game / Factory day raid on SPT-GPU-2, approximately 10:32–10:40
local time. The fresh ladder selected a Stechkin APS. Native hands used
UnityAnimatorWrapper / Auto, not FastAnimator. UiBridge godmode isolated weapon
tests; shots went into the surrounding walls/ceiling, zero player kills.
No profile fixture, server catalogue or runtime object mutation was needed.

Raw evidence is retained under `artifacts/akimbo-animation-rate/` (ignored).

## Observed results

- **Twice the automatic rate:** identical 1,000 ms native trigger holds, same
  APS. Akimbo ON produced 26 shots (13 right, 13 left);
  OFF consumed 13 cartridges / 13 native shots. Both began with 20+1 loaded.
  The ON ammunition snapshot was read before that trigger hold had finished
  (8+1 remaining); only the later counter snapshot is final. It is not used
  as an ON/OFF cartridge-consumption comparison. The separate single-fire
  sequence below verifies the two-shots-per-cartridge rule.
  The first eight ON shot timestamps span 269 ms, mean interval **38.43 ms**
  (native FireRate remained 750 RPM / 80 ms). This is a bounded frame-quantized
  comparison, not a claim of an exact 40 ms interval on every frame.
- **Independent visible firing:** eight successive single-shot screenshots
  `single-frames-0.png` through `-7.png`. Frames 0/2 show the right pistol kicked
  up with its flash while the left remains down; 1/3 show the reverse. Native
  slide sampling follows the confirmed firing side after EmitEvents. The
  support-hand fingers still use EFT's existing grip; this is not new animation
  artwork or a second native weapon controller.
- **Single-fire semantics and ammunition:** eight individual shots consumed
  four of eight loaded cartridges. A subsequent 1,000 ms held trigger produced
  only one shot, leaving its free half reserved. OFF/ON did not reset that
  credit: rapid input then produced seven remaining shots, starting left, and
  stopped empty. In total those eight cartridges yielded 16 alternating shots;
  surplus dry triggers produced no extra shot. The seven rapid live shots
  span 409 ms, mean **68.17 ms**, versus the unchanged native SingleFireRate
  of 450 RPM / 133.33 ms. Input itself averaged 75.69 ms including the later
  dry-fire attempts; queued native input semantics remain unchanged.
- **Reload and context restore:** an empty reload was observed in native
  ReloadExternalMagOperation with animator speed 1 and no Akimbo view; it
  finished in Idling and rebound both replicas. No hands softlock occurred.
- **OFF during automatic firing:** native AutomaticFireOperation was observed
  at animator speed 2 with 17 originals hidden. OFF restored speed 1 while
  that operation was still executing, disposed the view and restored all
  **17/17** renderer flags to false. Native firing settled to Idling. ON
  subsequently rebound normally, at idle speed 1.

The attempted SelectKnife/SelectFirstPrimaryWeapon commands did **not** yield
a verified KnifeController transition in this Gun Game kit (the observation
remained FirearmController/Idling); this run does not certify a knife/redraw.
It also does not certify revolver presentation, FastAnimator/Manual at runtime,
every pistol model, third-person/death/respawn transitions, wall fallback or an
ordinary PMC/Scav control raid. Those guards have offline/native-source evidence,
not new native acceptance here. No claim of separately animated reloading.

## Preservation, logs and cleanup

Fresh profile baseline was taken after startup reached MainMenu and before
entering the raid. Post-raid comparison passed all seven protected domains:
Inventory, Skills, Quests, TradersInfo, Encyclopedia, XP, Health (excluding only
Health.UpdateTime). No profile was restored to erase a discrepancy.

Own config ledger restored the prior zombie mode; AkimboEnabled=true and empty
GunGameLadder were already back at their original values. No conflicts/missing
settings. The test client was gracefully closed; the already-running server
was left running. Final host status confirmed game not running.

Export contains 3,551 lines (#1–3551), no lost prefix, one stored line truncated
at UiBridge's 2,000-character bound. **Zero Error entries and zero ArenaMode
warnings**, including no Akimbo quarantine. `test-log.json`, `loaded-status.json`,
`transitions.json`, `profile-check.json`, `config-ledger.json` contain the evidence.

## Offline checks and independent review

`tools/AkimboChecks/Verify.ps1`: production-linked lease/credit (2,127
assertions), cylinder (23), timing/presentation (30), native-view boundary (38).
29 deliberately broken variants fail their intended assertions: 15 lease,
three cylinder, seven timing/presentation, four view.

Independent reviews identified and fixed ownership of pooled cartridge meshes,
stale direct/root parent references, and Auto-mode sampling before fire events.
Final reviews found no further concrete blockers. View doubles model native
ownership/destruction, not Unity rendering or player-loop timing; the APS run
above supplies the bounded native evidence. No push performed.
