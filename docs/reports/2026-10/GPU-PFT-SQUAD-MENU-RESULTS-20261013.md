# Variable PFT squad / Zombie defaults — native menu check

Date: 2026-10-13, approximately 17:48–17:57 CEST. Result: **menu checks passed;
native PFT raid acceptance is still pending**.

## Runtime and scope

- Source: `8b5c772`; client `maschine-ArenaMode.Client.dll` SHA256
  `786C6643A53C0CB4830F54598E71C33BA1EAE7D3AF808ABAEBEE4AD5B3824377`.
  VM and GPU1 disk hashes matched. UiBridge's loaded-MVID comparison reported
  **43/43 plugins and 5/5 libraries identical** across VM, host disk and running
  process; all 43 plugins loaded successfully. Server/Core were unchanged.
- Host: SPT-GPU-1, native 1920×1080, default test profile `SPT-GPU-1`.
  EFT was freshly started; instance `7c46308b`, started 17:48:12.
- EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`, UiBridge
  `0.9.1+7b94a3f551e9bdb0fab3545c29e0c84f6f7ff593`.
- Real menu navigation and native backbuffer captures. No raid, simulated
  followers, item actions, profile writes, foreign profile/store copies or
  gameplay hooks were used. GPU1's test profile has no PFT companion store and
  no carried firearm; it was not repurposed for a protected squad raid.

## Observations

| Check | Native result |
|---|---|
| Zombie settings | `MAX ALIVE 32`, `WAVE START SIZE 50`, `WAVE GROWTH 20` visible; `Own gear` selected. Live BepInEx metadata independently reports defaults 32 / 50 / 20 / true. |
| Preserve existing preferences | These four values were already saved before startup. Other preferences, including ShooterPercent 20 and SpawnIntervalSeconds 0.2, were not reset. This validates new default metadata and preservation, not first-ever configuration creation. |
| Solo Zombie hint | Two readable lines: “Ammo refills each wave. Optional PitFireTeam support (1–4 companions).” Objective, respawn, equipment and sliders remain clear. |
| Tactical without squad | “TACTICAL / 1v5 (WIP)” remains available. Full two-line text: “Eliminate the enemy squad. PitFireTeam supports 1–4 companions.” No accidental switch to a PFT raid was made. |
| Factory play area | Only the `Whole map` choice remains; redundant availability/explanation text is gone. |
| Customs play areas | Factory Center, Factory Side and Customs Yard remain selectable; redundant surveyed-area/availability text is gone. |
| Customs preview | AREA and OVERVIEW both render the selected contour. “Blue boundary…” explanation is gone; map attribution remains readable. |
| Layout | Both left panels fit, all 22 mutators fit on the right, no overlap with lower navigation. Labels were inspected in original unscaled crops: no missing-glyph issue. Downscaled full-image previews can misleadingly omit thin strokes. |

## Evidence

Local, ignored evidence directory:
`artifacts/pft-squad-menu-gpu-20261013/`.

- `factory-zombie.png` — full native 1920×1080 capture.
- `factory-zombie-left-original-crop.png` — unscaled crop of the left panel.
- `customs-zombie.png` — full native capture with Customs AREA preview.
- `customs-zombie-left-original-crop.png` and
  `customs-zombie-preview-original-crop.png` — unscaled original crops.
- `customs-tactical.png` — solo Tactical label and PFT hint.
- `customs-overview.png` — Customs OVERVIEW preview and retained attribution.
- `arena-before.cfg`, `arena-after.cfg`, `cfg-restoration-proof.json` — settings
  ledger. **222 entries before, 222 after, zero value differences.**
- `profile-before.json`, `profile-after.json`, `profile-differences.json` — private
  test-profile evidence, intentionally ignored and not for publication.

## Cleanup and log

The temporary Tactical selection was returned to the original `GameMode=zombie`
through the menu. Selecting Customs had automatically changed
`Team Deathmatch / ExperimentalArea` from `full-map` to
`customs-factory-center-polygon-v2`; after leaving the location screen it was
restored through the ordinary BepInEx setting API. Final saved-value comparison
is exact (zero differences), without overwriting the config file or its new
default metadata. EFT was left in **MainMenu, not in a raid**, with the server
running and no other active test session.

The profile was never directly changed or restored by the test. A structural
before/after comparison found only passive Energy/Hydration and update-time
changes, plus previously absent `StackObjectsCount` fields becoming `1` on the
Dogtag and Scabbard items. No item IDs, templates, locations or other progression
fields changed. These menu-time differences are not claimed as proof of raid
profile protection.

Current-process file:
`C:\SPT41\BepInEx\LogOutput-SPT-GPU-1-2026-10-13_17-47-56.log`.
Final error scan of this fresh log found one Error and no Arena error:

```text
17:48:51.561 [Error Unity Log] Starting microphone failed: "Error initializing output device. " (60)
```

This names the host audio-device initialization path; the menu test did not
demonstrate a relationship to this Arena change. Earlier-process errors were not
included as regressions.

## Still pending

The menu result does **not** accept variable-size protected PFT gameplay. On
GPU2, with its existing legitimate companions, verify exactly one invited
companion in Tactical versus five opponents, readiness, local death/respawn and
round rebind; then one companion in Zombies with cleared-wave rebind and squad
survival. Also inspect the actual invited-squad menu text. Follow
[the GPU2 test instructions](GPU-PFT-SQUAD-TEST-INSTRUCTIONS-20261013.md).
