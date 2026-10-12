# Arena menu polish: native GPU result, 2026-10-12

Result: **PASS for the tested solo menu scope at 1920x1080**. No raid was
started; this is not gameplay acceptance of Molotov, Akimbo or Zombie waves.

## Build and host

- Source: `a4ebe18c31386bd6da46f6c60e73326e3aa71cf2`.
- Client ProductVersion: `0.1.0+a4ebe18c31386bd6da46f6c60e73326e3aa71cf2`.
- Client SHA256: `EB3939749E4701264090BEDF67B85163A84D7643E112693B757E1988BA2AB4DB`.
- GPU1 was idle with EFT and SPT stopped. Synced host client hash matched before
  `gpu-run start --host host1`; its configured test profile was used.
- Fresh process instance `b00cd7e2`; UiBridge reported all 19 `maschine-*` plugins
  identical between VM, host disk and loaded code, and Redeploy.Core identical.
- EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`, 1920x1080 fullscreen window.
- UiBridge `0.8.0+1f62c6375543487c8c518f40369664029712fd4e`, SHA256
  `300D369A8F270AD7090733C4366E59842EA67A2E4FD952A3886BE86BE4674391`.
- GPU2 was not operated. No server, Core or unrelated plugin was deployed here.

## Native checks

| Check | Observed result |
|---|---|
| Mode order | Gun Game, Deathmatch, Team DM, Tactical / 1v5 (WIP), Zombies (WIP). All five buttons selected successfully. |
| WIP labels | Tactical, Zombies, Akimbo and Molotov suffixes visible and complete. |
| Mutator layout | 22 switches, 11 rows in each column. Last FAST BOTS row and descriptions fit above native navigation. |
| Molotov | Existing true preference displayed ON. Actual button click changed `Arena incendiary prototype / Enabled` to false. Available with installed companion; Zombies showed disabled N/A and the exclusion explanation. |
| Fast bots | Existing true preference displayed ON. Actual click changed `Bot movement / BotMovementEnabled` to false. Zombies showed disabled N/A without changing the preference. |
| Zombie controls | Only Zombies displayed three native integer sliders, with visible numeric values. Switching to Gun Game hid the controls. |
| MaxAlive | Native Slider bounds 4–56; mouse clicks at both ends produced exactly 4 and 56 in the UI and the existing config entry. |
| WaveStartSize | Bounds 1–200; both mouse-click endpoints produced exactly 1 and 200. |
| WaveGrowth | Bounds 0–100; both mouse-click endpoints produced exactly 0 and 100. |
| Persistence | 56 / 1 / 100 remained after other-mode selection, BACK to character selection and reopening the map screen. Selected Customs Factory Side also remained on reopening. |
| Customs layout | All three areas remained selectable below the sliders. Panel starts at y776; last description stays above native navigation. Area preview remained above mutators. |
| Factory layout | Whole-map controls, thumbnail, time strip and navigation did not overlap the three sliders or mutators. |
| Text diagnostics | Targeted new labels and Tracers reported no TMP truncation. Original-pixel inspection confirmed complete text. |

The test host already had non-default Zombie settings 56 / 56 / 10 and Molotov
enabled before this run. Fresh default Molotov=true and preservation of an
existing false setting belong to the offline contract checks, not this profile's
native evidence.

## Rendering false alarm resolved

Large screenshot previews in the agent tools intermittently appeared to omit
parts of labels, including ARENA, Deathmatch and COLORED TRACERS. This initially
triggered a rendering investigation. It was **not a reproduced in-game defect**:
lossless 1:1 crops from the same unchanged PNG contain the complete glyphs.
The original `03-zombie/step-9-screenshot.png` SHA256 is
`D33AFE9D25B1F7BFAB28AE4CA07B3234A0801D605854858E5AD06F7CB9134A43`.
Independent crop review and explicit original-detail viewing resolved the
discrepancy; no source/material/font changes were made to conceal it.

Read-only TMP diagnostics were also normal: no overflow/truncation, first visible
character zero, large visible-character/word/line limits, Render mode, no canvas
rect clipping, no parent mask, no shader keywords, opaque vertex alpha and
visible non-space characters. A fresh menu and repeated DM/Zombies selection
were additionally captured without Object Inspector access. No `ForceMeshUpdate`
or runtime method invocation was attempted; UiBridge's inspector is read-only.

## Cleanup, logs and limits

- All changed Arena settings restored: mode Deathmatch, Molotov=true,
  BotMovementEnabled=true, MaxAlive=56, WaveStartSize=56, WaveGrowth=10.
  Complete before/after section-key-value comparison returned zero differences.
- Returned to MainMenu, no raid running; game and SPT server left available.
  No profiles, follower files or inventory were edited; no test hooks were used.
- One Error was recorded during startup: native microphone output-device
  initialization. No Arena errors were observed during menu actions.
- No invited PFT squad was present; PFT-specific labels, equipment-row layout
  and armed/live locking remain offline-covered rather than freshly native-tested.
- Missing-companion behavior was not tested by uninstalling plugins. No other
  resolution, in-raid behavior or restart-level persistence was claimed.
- Two automation-only interruptions occurred: an initial wait used the wrong
  screen enum (`MatchMakerSide` instead of `SelectRaidSide`), and the generic
  scenario runner refused BACK without explicit safe-menu handling. Both were
  recovered by observed native navigation; neither was a game failure.

Local, ignored evidence is under `artifacts/menu-polish-gpu-20261012/`: numbered
scenario reports, PNGs, the settings baseline and the limited visibility JSON.
Useful screenshots: `02-map/step-6-screenshot.png`,
`03-zombie/step-9-screenshot.png`, `05-persistence/step-6-screenshot.png`,
`06-factory/step-5-screenshot.png`, `09-no-inspector/step-13-screenshot.png`,
plus `debug-mode-crop.png` and `debug-mutator-crop.png`. These local artifacts are
not release content; raw settings/logs must not be published blindly.
