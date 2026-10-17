# GPU map features — 2026-10-17

Native verification completed on SPT-GPU-2. Pickup placement/collection, TDM bot shields and the scoped MapPort follow-up passed the samples below. Test preferences were restored and the host was returned to MainMenu. The explicit untested boundaries remain listed below.

## Candidates and isolation

- Arena client: candidate `da437d3`, version 0.1.0, MVID `c7405380-9168-44f1-9a01-78320b87e886`, SHA256 `E224D3A79FB8BB641B4222F62DDD2CA5052FFC1F34613082C646C8A2A5285C95`.
- UiBridge 0.9.4+42bbfb38; EFT 0.16.9.5-40743; Unity 2022.3.43f1. Only test profile **SPT-GPU-2** was used.
- First process: `d101ff36c8db402793323eadc40a3801`, started 22:35:28. All 19 maschine modules were identical between the build machine, host disk and loaded process.
- Second process: `fdfd21184d5948699bb76153cef58c08`, started 22:56:34. Same Arena client; companion correction `dab2760`, SHA256 `496016C0271072070F0F6D63DC7DD90939FD7FD7D33682930CEBB3A095571984`. All 19 modules again loaded identically. MapPort was still the old build in these two processes.
- Temporary parent pins: **30 s** spawn protection, developer diagnostics on, friendly fire on. This does not establish native acceptance of the production **4 s** duration.
- Source, builds and deployment belong to the parent. This leaf only changed test artifacts and this report. No bot freeze or synthetic damage callback was introduced.

## Pickup samples before the companion correction

| Map and mode | Placement | Collection and visual evidence |
|---|---|---|
| AirPit independent TDM, 4 participants / 2v2 | 6/6 on first attempt; 7 geometry probes; one spacing refusal | Local Jump Boots collection at 22:42:43 after walking from outside. Green beam visible. Multiple bot Health, Shield and DoubleDamage collections and live rotations. |
| Customs Yard DM, 4 participants | 6/6 on first attempt; 6 probes; 16 candidates; no refusal | Local Shield collected at 22:44:44, position (-208.840, 1.087, -137.961), following a walk from 3 m outside. Cyan beam visible inside the warehouse. |
| Labyrinth DM, 4 participants | 6/6 on first attempt; 9 alternatives and 12 probes | Boots and Health each safely relocated by 0.5 m because pedestal clearance was obstructed. Boots at (19.500, 0.100, 10.910) collected at 22:46:12.927 after a real walk. Green beam visible against the brick wall. |
| Equator DM, 4 participants | 6/6 on first attempt; 10 alternatives/probes; four spacing refusals | InfiniteAmmo at (11.400, 0.031, 1.500) collected at 22:47:43.130 after a 4.1 m navmesh walk around a plywood barrier. A bot also collected DoubleDamage. No usable beam screenshot before stopping for the rendering errors below. |

The owned `ArenaMode_PhaseLighting` object was found on all four maps. This is runtime ownership/initialization evidence, not a photographic proof of each team's brief color pulse. Other Customs contours, Labs, Bay5 and both Factory variants were not retested natively by this leaf.

Images are genuine game captures: `airpit-beam.png`, `customs-beam.png`, `labyrinth-beam.png` in `artifacts/map-features-native/`. Indoor beam height remains ceiling-limited; the pictures show visibility, not a ruler measurement of the exact 6 m maximum. The 50% increase from 4 m to 6 m is covered offline.

AirPit player pickup attempts initially failed because the test's godmode set `DamageCoeff=0`, and later because the temporary 30 s player respawn protection was still active. Collection succeeded once those conditions ended. This was an experimental confound, not a placement defect. Labyrinth's existing trap-disable mechanisms remained active; this was not an exhaustive hazard sweep.

## Log findings and the companion follow-up

The first AirPit run contained three caught Debug-level pickup-objective NullReferenceExceptions for one bot. The parent added a narrowly scoped safe-path fallback correction and bounded diagnostic stack output in companion commit `dab2760`. The old message lacked a stack, so the exact cause of those three original exceptions was not proven.

After restarting with that companion, AirPit TDM ran live from **22:57:59 to at least 23:04:39 (6 min 40 s)**, including native shots/kills, replacement bots and ongoing pickup contests. The complete Arena export contained 654 lines without buffer loss:

- 11 bot pickup collections;
- 38 pickup rotations;
- 36 objective selections;
- zero pickup failure/exception matches;
- zero Error-level entries in the process query.

Evidence: `airpit-soak-final.json`, `airpit-soak/report.json`. These observations support the correction under this sample; they do not prove all possible navigation failures are impossible.

## Controlled TDM bot shield test

The second AirPit run used 30 s protection and friendly fire enabled. Arena HUD was temporarily disabled; local godmode and infinite ammo protected the tester. The primary comparison used the same physical **Zersus** body (`94f54d9b:29`, profile `6ad3e1b23292051d3864800f`) and its Head state (`94f54d9b:31`).

1. `airpit-cage-fresh.png` clearly shows the pale cyan/white three-ring cage around the moving ally with the Arena HUD disabled.
2. Before the protected shot: **Head 35 HP**. ADAR 2-15 shot #4, game frame 17388, t = 198.446 s, tracked visible head at approximately 4 m. Exactly one real shot was observed; Head remained **35 HP**.
3. Once no current shield lease remained, the same ally was approached again. ADAR shot #5, frame 19625, t = 217.095 s, visible tracked head at 4 m; the same Head state fell to **0 HP** and the bot died. This supplies the positive damage control.

The complete tool responses are in `airpit-shield-damage-evidence.json`. An earlier comparison against nader also produced 35 → 35 and then 35 → 0, but its first shot involved a moving/occluded target and is not the primary proof. Earlier screenshots that contain no visible cage do not count as visual acceptance.

This verifies protection/release on a real respawned ally and a HUD-independent world indicator. It does **not** isolate whether the tested lease ended by elapsed time or the bot's attack, does not separately prove grenade release, and does not replace the offline 4 s/default lifetime checks. Initial protection was observed as three cage objects; the stronger photographic/damage comparison is a respawned body.

After leaving the second run, the HUD pin was restored with no conflicts. Menu inspection found zero shield cages and zero pickup markers.

## Equator rendering error boundary

The first Error-level entry in the full initial process log was **22:47:14.330**, during Equator loading: `WeatherController.ControlFogginess` NRE. The raid then repeatedly logged `DistantShadow.SetupComputeShadowMaskCommandBuffer` and `AmbientHighlight.UpdateAmbientBuffer` NREs. This followed AirPit → Customs → Labyrinth → Equator in one process. The earlier three raids had no Error-level entries in that file-first query; the caught Debug pickup exceptions are separate.

The parent traced MapPort's new rig reactivation to 22:47:14.263, 67 ms before the first weather error. Arena's new owned light only armed at 22:47:26.368. Thus the error began before that light existed. The separate MapPort correction passed the follow-up sequence below; this report does not assign the weather error to pickup or Arena lighting code.

Testing stopped and returned to MainMenu. `equator-first-errors.json` holds the first 12 errors; `equator-log.json` holds the latest 3,169 buffered lines and explicitly records earlier buffer loss. The complete session log remains on GPU2. At MainMenu after these four samples, inspection found zero shield cages, zero PhaseLighting objects and zero pickup markers.

## Native countdown

Screen history reports `FinalCountdown (1 s)` between TimeHasCome and BattleUI in both tested processes. This confirms the shortened countdown ran natively, separately from longer map loading and team assembly. This leaf did not perform a controlled cancellation test or compare an ordinary PMC countdown.

## MapPort regression follow-up

Third process: `dc8d8d22033641dbb4f0ac3147a7c862`, started 23:09:28. MapPort client commit `6c6a38c`, SHA256 `E4E917381CC524AD224947CF0EF7E530021D8A88899FDC0B0141EE9D75BBAF0B`; Arena client and companion unchanged. Status confirmed all 19 maschine modules identical between VM, disk and process. Parent separately verified 2,239 non-client MapPort files unchanged; this leaf did not repeat that file-hash audit.

The same process completed this sequence:

1. **Fresh Equator DM:** 6/6 placements on the first attempt; local DoubleDamage collection after an 8 m navmesh route. Boots beam photographed at close range (`equator-boots-near.png`). Forced local test death at 23:12:06.391 was followed by a successful normal Arena respawn in 0.23 s. No BepInEx Error entries afterward.
2. **Labyrinth DM:** 6/6 placement, owned light present, native screenshot and normal exit.
3. **Equator DM again:** short start/placement/rendering sample after Labyrinth, again 6/6 and owned light present, native screenshot and normal exit. No second local respawn was exercised in this short revisit.
4. **Customs Yard DM:** native-map follow-up after the ported-map reuse; 6/6 placement, owned light present, warehouse/sky rendering visible in `customs-native-control.png`, normal exit.
5. **Hideout:** the overview loaded and rendered, then first-person entry also rendered normally. Both screenshots were reviewed (`hideout-smoke.png`, `hideout-first-person.png`); returned to MainMenu without buying, building or changing Hideout facilities.

The final **whole BepInEx process file** query covered 17,512 physical log lines and found **zero Error-level entries**, including all loading and respawn phases. `final-process-log.json` exports all 4,302 buffered entries from sequence 1 without a buffer gap; two long pre-existing-in-buffer diagnostic entries had already been cut at 2,000 characters, which the manifest records. The independent file-level Error query avoids relying on truncated diagnostics or just the latest tail. Evidence is in `final-native-state.json` and the per-scenario reports.

An additional read of EFT's separate traces stream found **68 Error-labelled native entries** (including `JetMaterial == null`, missing weather/season helpers, wind settings, item metadata and spawn-marker reconciliation). They are preserved in `final-eft-errors.json`; no baseline attribution was attempted. None was the repeated `WeatherController.ControlFogginess`, `DistantShadow` or `AmbientHighlight` exception from the initial failed sequence. Therefore this report claims the regression did not recur in the tested sequence, not that every log stream is error-free. Native cache/provider object identities were not directly measured.

## Restoration and handoff

- Captured and compared all **223** Arena configuration values. Eight menu preferences changed by the raid recipes were restored; HUD was restored separately. Parent pins restored spawn protection from 30 s to **4 s** and DeveloperMode to **false**; friendly fire was already at its original value.
- Final comparison against the original preference capture plus the parent pin originals: **223/223 present, zero differences**. Evidence: `config-restore-validation.json`, `preferences-restore.json`, `config-restored.json` and the parent's pin ledger. GameMode is back to `zombie`; HUD is enabled.
- Final status: MainMenu, no active raid or active test hook, all 19 modules still identical. The inactive HideoutGameWorld remains allocated as vanilla normally allows.
- Final scene inspection: **zero** Arena bot shield cages, PhaseLighting objects and pickup markers.
- GPU2 was returned to the parent after all restoration; no code changes or commits were made by this native-test leaf.

No performance guarantee, full map/mode matrix, ordinary PMC damage immunity check, first-attack-versus-expiry distinction, grenade-release test, or native 4 s timing certification is claimed. Those boundaries do not invalidate the directly observed pickup collection, real protected/unprotected shot comparison, scene cleanup or successful map-transition regression sequence.
