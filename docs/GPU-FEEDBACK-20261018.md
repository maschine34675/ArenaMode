# Native feedback verification — 2026-10-18

Status: native samples completed on final candidate `0913670`, after correcting the preview Animator defect found in the first run. Original preferences are restored and GPU2 is back at MainMenu. This report is evidence of the observations below, not exhaustive map/model coverage or native collateral acceptance. One native animation-handler log message remains unresolved.

## Isolation and identity

- GPU2, `192.168.178.109`, test profile **SPT-GPU-2** only. EFT 0.16.9.5-40743, Unity 2022.3.43f1, UiBridge 0.9.4+42bbfb38.
- First candidate source `200b1b2`; Arena client SHA256 `99E015F9D7303507AAA69511430613EAC3031CFB64275BC53902EDCC928E7E84`, MVID `88ea1cdb-0e53-4944-9c4c-f87265e1bd70`. Parent independently measured the MVID. Native status verified all 18 maschine plugins plus Core equal between VM, host disk and loaded process, both before and after raids.
- Process `26f9f06d226a47c7b68f732fb0b392fd`, started 10:19:56 host time. Started through `gpu-run start --host host2` using its default test profile.
- Native leaf did not build, deploy, change source or commit. Parent owns those actions and protected-file hash comparison. Existing `protected-files-before.json` was preserved.
- All 223 Arena settings captured before the first menu click. Temporary DeveloperMode was enabled for logs. Arena HUD was disabled during bot-effect tests; godmode protected only the tester. Bot effects were obtained exclusively through real collections; no grants, fabricated bullets or damage callbacks were invoked.

All evidence paths below are relative to the retained directory `D:/SPT41/Development/ArenaMode-powerup-feedback/artifacts/powerup-feedback-native/`. Raw capture directories remain in this worktree; merging this report into another checkout does not move those artifacts.

## Planned modes

`mode-previews.png` was visually inspected at 1920×1080. Seven rows fit above the facts divider without overlap: 320×25 pixels at top y = 252, 279, 306, 333, 360, 387, 414. Real modes retain Gun Game, Deathmatch, Team DM, Tactical and Zombies order; Capture the Flag and Domination follow.

Native Button inspection found both previews `interactable=false`, navigation `None`, zero runtime listeners and zero persistent listeners. Real clicks on both disabled rows left `Match/GameMode=teamdeathmatch`. The two preview entries remained disabled on the snapshot. `preview-snapshot.json` and `preview-config-after.json` preserve the UI/config responses.

On reopening after the raids, both CustomTextMeshProUGUI colors were RGBA **(0.48235, 0.4902, 0.45098, 1)** rather than the intended neutral (0.48, 0.48, 0.48, 1); both cloned Animators remained enabled. The grayish visual still conveyed disabled state, but the exact neutral-gray implementation contract failed. Parent was notified for a correction. Evidence: `preview-color-evidence.json`, `mode-previews-final.png`.

## Akimbo — Equator Gun Game, four participants

The random ladder selected **PB 9×18PM silenced pistol** for tier 1. No weapon attachment or profile edit was made. Both roots, `Arena Akimbo left aim laser` and `Arena Akimbo right aim laser`, appeared automatically, with green beams and surface points.

- `akimbo-first.png` shows both held pistols; the left beam ends on the nearby wall, while the right extends into the open passage. `feedback-akimbo-open.png` shows both beams terminating at sandbags after movement and a view turn.
- Three native navmesh walks arrived. The third was a 14.2 m route. Reviewed native capture frames 25930, 25960 and 26020 show both beams following the changing gun pose while walking (`akimbo-walk-capture/`). The earlier isolated `akimbo-moving.png` has no beam during a near-cover camera turn and is not the movement-visibility proof.
- Inventory inspection found both laser roots inactive, with no visual lasers in `feedback-akimbo-inventory.png`. Renderer.enabled itself remains true; root activation owns the hide state.
- Two actual trigger holds produced one native shot each: frame 17950 / t=280.766 and frame 18708 / t=287.090. Arena logged right=1,left=0, then right=1,left=1; the first shot left the ammunition count unchanged, then one cartridge was paid on the second. This supports native alternation; no 50 m ballistic accuracy claim is made.
- Reload retired the original roots. In `akimbo-reload-capture/`, frames 28831, 28871 and 28931 show the actual lifted/rotated single pistol with the left replica and both lasers absent. The late still `feedback-akimbo-reload.png` already shows restored beams and is not evidence for the busy interval.
- Akimbo OFF yielded zero laser roots; ON yielded exactly two new roots. After leaving the raid, zero remained. `akimbo-off-roots.json`, `akimbo-on-roots.json`, `akimbo-exit-roots.json`.

Both bounded native captures reached their requested frame cap (120 and 240) and their manifests accordingly label the window `complete=false, reason=frame_limit`. The specifically reviewed PNG frames exist; this report does not present the capped windows as complete unbounded recordings.

No second pistol/revolver or full mode matrix was exercised in this first run. Native 50 m convergence, every possible cover/pose and exact surface-point ballistic alignment remain outside the sample; production-linked offline checks are separate evidence owned by the parent.

## Natural bot effects — Air Pit independent Team DM, 2v2

The run placed 6/6 pickups on its first attempt and entered live play at 10:29:28. Local godmode does not grant pickups and cannot be used as proof of local collection. Bots were neither frozen nor synthetically granted effects.

- **Double Damage:** CheekiBreeki, physical profile `6ad483591f53770dc8bbf40b`, selected the objective and naturally collected at **10:29:34.052**. The pink `ArenaBotDoubleDamage` root was present. `bot-dd-first.png` (frame 50020) clearly shows the moving bot inside the pink cage with Arena HUD disabled. The bot had moved from the north pickup at (-11.010,0.003,60.460) through the Burger area. After the 20 s window the root query found zero, while the same bot was still alive and had moved onward to (14.6,0,3.9).
- **Shield:** rzambol, profile `6ad483591f53770dc8bbf50c`, naturally collected at **10:31:06.508**. `ArenaBotPickupShield` was active with its three ring children. `bot-shield-near.png` (frame 60754) shows the blue/cyan cage following that bot through a doorway; the brick wall occludes the left portion. The bot moved from the north pickup to the Burger corridor, then to (14.8,0,29). Shield was absent after its 30 s window.
- The same rzambol naturally collected Double Damage at **10:31:26.307**. `feedback-bot-shield-moving.png` is deliberately retained under its capture-time filename but shows the later **pink Double Damage** cage, after the shield had disappeared. It does not prove a simultaneous blue/pink overlap.
- The local M4A1 fired three actual rounds and killed rzambol by a headshot at approximately **10:31:48.6**, 4.9 m. That is after Double Damage's 20 s expiry, so the subsequent zero-root query does **not** certify removal specifically caused by death or shield depletion.

HUD.Enabled=false removed the optional combat panels while essential match notices remained; the world cages stayed visible. No same-bullet two-enemy kill was observed. Native collateral/DoubleKill acceptance is unclaimed. No simultaneous-cage, depletion, exact death-frame or ordinary-raid matrix acceptance is inferred from these samples.

## Logs and first-process restoration

Whole BepInEx file query found **zero Error-level entries** across 17,606 physical lines. The server stream also returned zero Error entries. `pre-fix-full-process-log.json` preserves all 4,314 buffered entries from sequence 1; nine existing long diagnostics had already been truncated to 2,000 characters, which the export records. The whole-file Error query is independent of that buffer truncation.

EFT's separate trace contained **65 Error-labelled entries**, preserved in `final-eft-errors.json`. They include JetMaterial, weather/season helpers, spawn-marker reconciliation, one weapon UtilityOperationEvent and four `FATAL There is no handler for hash:<b>0</b>` messages (10:26:05, 10:26:16, 10:26:27, 10:26:57). The hash messages have no stack in that stream; parent found the native source logger but no matching entry in the immediately preceding trace. Their cause is unresolved. No claim that every log stream is error-free, or that these messages are proven unrelated, is made.

Restoration compared **223/223** entries with **zero differences** (`config-restore-validation.json`, `config-restored.json`). Seven remaining differences were restored: DeveloperMode, two ReplaySeconds values, Equator solo/AirPit team participant values and the two Team DM limits. HUD and Akimbo were restored in their tests; GameMode returned to its original Team DM value through the second raid. The diagnostics ledger closed with reset 0 / already 1 / conflict 0 / missing 0.

Final first-process state: MainMenu, no raid, no active hooks, control free, zero ArenaBot roots and zero aim-laser roots. All loaded modules still matched. `pre-fix-final-status.json`, `pre-fix-final-scene.json`, `pre-fix-final-lasers.json`.

An initial GunGame recipe was called from SelectRaidSide rather than its required MainMenu and timed out without starting a raid. Backspace did not leave the live GunGame sample, so the normal Escape→DISCONNECT→result-screen flow was used. These are recorded test-flow limitations, not asserted candidate defects.

## Final-candidate retest

Final source is `09136704fad84c83b957f1b1e767120720e3251a`, Arena client SHA256 **`1D2464EEAE7E4DB642D2C730B1FB1C1C6F1612C74FB718FC915D8A61ECDF292B`**, MVID **`d1d8d6c1-bca5-43ce-b729-81eb60cee3a5`**. The parent measured the build MVID; native loaded-versus-host-versus-VM status verified the final process **`6a23896f`**, started **10:41:12**, with all 18 maschine plugins and Core identical (`final-candidate-start-status.json`, `final-native-status.json`). The host client SHA was also independently read over the host share.

An earlier restart raced Syncthing and loaded the previous candidate into intermediate process `5d6e69ec` (10:39:25). It was rejected as final-candidate evidence and no gameplay test was done there. After the host disk had the final SHA, a second `gpu-run restart --host host2` established the identity above. No manual file copying or unrelated deployment was performed.

Only `ArenaModeSelectionView.cs` changed in production code between the two tested candidates: preview clones disable their inherited Animators. The other changes were the corresponding documentation and menu harness. Akimbo, powerup and collateral source stayed identical, so the detailed first-process movement, reload, toggle and natural-effect evidence is retained with its original candidate identity rather than falsely presented as a full repeat.

- **Previews:** the corrected screenshot `mode-preview-corrected.png` was visually inspected. All seven rows retain the 25 px layout and both previews remain disabled (`final-preview-snapshot.json`). Read-only native inspection after the menu settled reports both text colors exactly RGBA **(0.48, 0.48, 0.48, 1)** and both preview Animators **enabled=false** (`final-preview-color.json`). This resolves the observed color overwrite.
- **Second pistol:** Equator Gun Game, four participants, randomly selected **PM (t)**. `final-akimbo.png` was visually inspected: both green beams are visible, with the left stopped by the nearby wall. Two named Arena aim-laser roots were present (`final-akimbo-roots.json`). Real shots at frame 24446 / t=396.015 and frame 26572 / t=413.786 again changed ammunition 9→9 then 9→8; `final-pm-shot.json` preserves the second trigger result. No attachment, inventory grant or broad profile edit was used. The roots were absent after exit.
- **Natural effects:** Air Pit independent Team DM, four participants/2v2, entered live play at **10:50:18.127**, with 6/6 pickups placed on the first attempt. The approximately seven-minute repeat recorded **three natural Double Damage collections and five Shield collections**, all actor=bot (`final-candidate-natural-pickups.json`). Read-only indicator-body and root inspection observed active effects; no Arena grant methods were called. The clean first-run pink image remains the visual Double Damage proof; final Double Damage collection/active-body evidence (`final-double-damage-active-body.json`) is a smoke repeat, not another pink screenshot.
- **Final shield image:** MiseryMachinery, profile `6ad488a41f53770dc8bd9c21`, naturally collected Shield at **10:57:21.014**, buffer sequence 4030. The read-only active indicator-body list identified that bot; the supported teleport/look APIs moved only the tester. `final-natural-shield.png`, native frame **87468**, was visually inspected and clearly shows its blue/cyan cage. `ArenaBotPickupShield` was present at capture. Arena HUD.Enabled was false; the essential match banner remains by design. `final-effect-follow.json` and `final-shield-capture.json` preserve provenance.

No additional same-bullet multi-enemy kill, simultaneous-cage, shield-depletion, death-frame or 50 m convergence acceptance is claimed by this repeat. The first and final raids used the SPT-GPU-2 test profile exclusively.

## Final cleanup and logs

The final raid was left via Escape→DISCONNECT and the normal result screens. Godmode was explicitly disabled and aim tracking stopped before exit. A fresh comparison against the **original** pre-test baseline found **223 settings on each side, zero differences and zero extra keys**. Seven values were restored in this final process: Gun Game ReplaySeconds, HUD Enabled, AirPit team and Equator solo participants, Team Deathmatch KillLimit, ReplaySeconds and TimeLimitSeconds. `final-restore-deltas.json`, `final-restore-results.json`, `final-config-restored.json` and `final-config-restore-validation.json` retain the evidence. DeveloperMode and the first-process diagnostics ledger were already restored and were not reopened for this repeat.

Final native state: **MainMenu, no raid, no active test hooks, zero ArenaBot objects and zero aim-laser objects** (`final-native-status.json`, `final-native-bot-roots.json`, `final-native-laser-roots.json`). The stopped-tracking label is historical status, not an active hook. GPU2 control was explicitly returned to the parent after these checks. The parent then independently confirmed MainMenu/no raid/control free, all 223 original values, both zero-root searches, final host SHA and all loaded module identities; it also viewed the final PM and Shield images. No personal profile, server, companion, Core or map-port deployment was changed by this leaf.

The final whole BepInEx file query found **zero Error entries across 17,375 physical lines**; the server stream also found **zero Error entries**. The full buffer export contains **4,091 entries starting at sequence 1**, with nine long existing diagnostics already capped at 2,000 characters (`final-candidate-full-process-log.json`). The independent file Error query is unaffected by that cap.

The separate final EFT trace contains **54 Error-labelled entries**, including the same categories described above and **one recurrence** of `FATAL There is no handler for hash:<b>0</b>` at **10:47:33.804**. This has no attached stack in the trace and remains **unresolved**, rather than silently dismissed as unrelated. The final candidate did not crash in these samples. Evidence: `final-candidate-bep-errors.json`, `final-candidate-server-errors.json`, `final-candidate-eft-errors.json`. The absence of BepInEx/server errors does not mean every native trace was clean.

Parallel changes to main's BigBrain library lifecycle were explicitly outside this deployed candidate and were not built or deployed by this leaf. Native acceptance here belongs to `0913670` with the installed legacy companion, not to an untested subsequent main build.
