# Domination objectives on DynamicMaps

Date: 2026-11-01. User choice: only during Domination, with live possession.

## Behavior and ownership

Optional DynamicMaps shows the current round's compact objective IDs on its map
and shared minimap view: neutral light grey, USEC blue, BEAR red, contested yellow.
Equator and Bay5 retain their original `X`; Air Pit and Bowl have A/B; Iceberg and
Factory have A/B/C. The round's verified floors are authoritative, including
Factory's randomly selected layout. No duplicated coordinates, new settings,
map-package changes, external assets or mandatory dependency are introduced.

`DominationRuntime` owns one `DynamicMapsDominationMarkers` instance after complete
placement. Each existing 0.2-second objective sample updates it **after** advancing
the rules. `ObjectiveHudVisible && IsSearchActive` supplies exact raid/match and
Live-phase admission; the same sample reaching the score/time limit clears the
overlay before the result hold. Respawns retain the round and its objectives.
Native stop, failure and normal end dispose the same instance. No gameplay,
objective placement, profile, scoring, bot or saved configuration state is written.

## Optional API and UI lifecycle

Reviewed DynamicMaps 1.2.1, product
`1.2.1+99200c60f084d1f64491dee93ceb09351bb8a2cd`, SHA256
`c41507eb1edb6d6c8101314fedf9eb52c874c980e757cbc8348f084d2231b1b8`.
The adapter binds public exact signatures and uses a soft dependency already
declared by ArenaMode. Absent DynamicMaps is silent; an incompatible API disables
only this overlay. Future versions must still pass the API checks and are not
claimed tested by this baseline.

- `MapView.LoadMap` postfix discovers new views; `UnloadMap` prefix removes only
  owned markers through native `RemoveMapMarker`. Existing inactive views are
  discovered once because a retained map can reopen without another LoadMap call.
- Location names must match the current raid. Native projection receives world
  `(x,z,y)` and owns rotation, zoom and floor visibility. The map's own markers and
  global category/opacity settings remain unchanged.
- `MapMarkerDef` uses an owned category. Its backdrop is a solid native UI quad
  rotated into a diamond (the native dot's large transparent border was unreadable
  in the first GPU test). No new texture or sprite asset is needed. Its ID label
  is centred and visible without hovering. Ownership repaint is followed by the
  native position/layer callback, preserving hidden floors.
- The user's larger presentation uses a 42px marker/label frame, a 30px square
  rotated 45 degrees (about 42px corner-to-corner) and 27px ID text: 50% larger
  in each linear dimension than the initial accepted presentation.
- The instance owns its hook ID and exact marker handles. Late disposal of a
  previous round cannot remove a successor's markers. Destroyed views are pruned.
- Optional binding, rendering and removal errors cannot abort a match. Cleanup
  attempts native removal; even fallback hiding is guarded against an already
  invalidated Unity object. One warning per adapter bounds failures.

The DynamicMaps source was read to verify contracts; no code, DLL or artwork was
copied into ArenaMode.

## Verification

`tools/DominationMapMarkerChecks/Verify.ps1` exercises the production adapter with
external Unity/DynamicMaps/Harmony boundaries doubled, deliberate source defects
and installed-DLL metadata checks. `DominationRuntimeChecks` verifies the actual
runtime-to-overlay data and cleanup. Native rendering is verified separately on
GPU-1; the loaded-build evidence and its limits follow below.

Offline results: 3,620 unit tests passed (three environment-gated cases skipped),
632 Domination runtime assertions and 13 rejected mutations. The map adapter's
final presentation passed 28 behavioral assertions, seven rejected mutations and
the installed public Image/marker contracts. Existing DynamicMaps corpse cleanup
and its regression harness passed unchanged. Both client assemblies build in
Release with deployment suppressed; the installed payload stayed unchanged during
these checks. Independent source and installed-UI reviews found no remaining issue.

The first GPU-1 cycle (source `beb13a87`) verified the exact live round, ownership
updates, reopening and immediate marker removal at the 120-second terminal sample.
It exposed the padded dot's poor readability, fixed by `01f5bb04`. GPU-1 has no
EFT Arena installation, so its map gate correctly refused the initial Iceberg
attempt; the native cycles use Factory. No install gate or third-party setting was
changed to bypass that boundary. The first scenario's automatic-menu wait timed out
on the normal ExitStatus screen; the official leave recipe completed the return.
These are test setup/presentation findings, not accepted final visual evidence.

The corrected client (`01f5bb0484468ac0b02c1d8139c466b7cacc94f8`) was deployed with
its matching BigBrain companion, leaving the server and installed Core unchanged.
Fresh GPU-1 process `0710faa7d7354e9a9f04454044e51d74` loaded the client SHA256
`2DECAFEDF80659AAEACB84CAA068BF874B5175E6F88DF989FD05078A5F1E2E53`, MVID
`62bdd3ff-8461-405e-ac9b-1503a4b0af88`; companion SHA256
`6F7210261520D3DA8ACCFC0B43533DFE0C6E1E78421BA4F6F2E13910FE5FA958`.
UiBridge 0.9.7 reported all 16 scoped plugins and both scoped libraries matching
disk, and all 40 discovered plugins loaded without an error.

`artifacts/domination-map-markers/final-diamond/report.md` records the complete
20-step Factory Domination cycle: live verified objectives, ownership update,
map close/reopen, the real 120-second terminal transition, and the official
post-raid return. `diamond-terminal.png` shows removal while native player/exfil
markers remain. The overlay reports three points, one map view and no failure
during play, then `_current=null` in MainMenu. The ArenaMode warning/error query
returned zero lines. The player's arrow obscures the captured A in these images;
an offset view is recorded separately for the final readability check.

The same loaded process then ran a second round with a different random layout.
`readability/diamond-offset.png` shows A as a legible, upright dark letter on its
BEAR-red diamond after capture, with the player five metres away. Native floor
filtering selects this round's lower level. The 11-step `readability/report.md`
also verifies manual disconnect cleanup and `_current=null` after the return;
the ArenaMode warning/error query remains empty.

After both corrected-build raids, all **240** configuration value lines match the
pre-test snapshot exactly (score limit 200, time limit 600, Factory participants 10).
Only 11 generated range comments changed decimal comma to period when BepInEx
saved the file. Whole-profile comparison against the final process's pre-raid
snapshot differs only in `characters/pmc/Hideout/sptUpdateLastRunTimestamp`;
the PitFireTeam companion database retains SHA256
`EF83B5B5E99D401ECA0951E308476A5B0AE1E931195EEADF3731B97A87157B02`.
`preservation.json` records these checks. `cleanup-status.json` confirms MainMenu,
no raid, no control lease, godmode off, no pending clock/weather overrides,
and the exact candidate still loaded. Temporary test controls are restored.

The GPU-1 native scope is Factory Domination on DynamicMaps 1.2.1: capture, readable
possession, reopen, normal end and manual disconnect. Ported maps, the minimap
presentation and an ordinary non-Domination raid were not separately exercised
on this host. Their shared data/view path and inactive-mode boundary have source,
contract and behavioral coverage; this is not a claim of native acceptance there.

## Supplementary Iceberg acceptance on GPU-2

The user released GPU-2 later on 2026-11-01. After a normal restart, process
`9e6845019f324c32846bc443e2d6f883` loaded the same client MVID and client/companion
hashes above, with all 16 scoped plugins and both libraries matching disk. ArenaMapPort
was `0.4.0+880002b9`, MVID `6928d93f-7f1f-4c1e-a3ed-1826e98fe400`. No rebuild,
redeployment or gameplay/source change was needed for this additional check.

Evidence is under `artifacts/domination-map-markers/gpu2-iceberg/`:

- `start-r2/report.md`: a 5v5 Iceberg Domination raid, clear noon, with the
  test-only score limit disabled and time limit 180 seconds. The first start
  (`start/report.md`) was refused before any click by gpu-run's old map catalogue;
  documented `mapFromMenu` selected the real available Iceberg tile on retry.
- `validated-anchors.json`: live A `(16.770,4.403,-91.390)`, B
  `(10.614,3.130,-77.727)`, C `(21.910,3.107,-47.300)`. All three appear together on
  Ground Floor. `native/iceberg-initial.png` shows the three neutral IDs in the
  expected arrangement; `native/iceberg-ownership.png` shows A blue and B/C red.
  Each point was visited, and the same map view reopened between visits.
- `native/report.md`: all 35 steps passed. The runtime reports `arena_iceberg`,
  three points, one map view and no adapter failure. At the real time limit,
  `native/iceberg-terminal.png` shows all three objective diamonds removed while
  player, corpse and exfil markers remain. After the official return, `_current`
  is null. ArenaMode and DynamicMaps warning/error queries, plus the final
  Error query across all BepInEx sources, returned no entries.
- `preservation.json`: all 240 config values restored, the complete config file
  byte-identical, only the allowed Hideout bookkeeping timestamp changed in the
  whole-profile comparison, and all 49 companion/backup files byte-identical.
  `cleanup-status.json` confirms MainMenu, no raid/control lease, godmode off,
  time/weather overrides cleared and the exact candidate still loaded.

The additional native acceptance covers Iceberg on DynamicMaps 1.2.1. Other
ported maps, the separate minimap presentation and an ordinary non-Domination
raid remain outside the exercised native scope; their shared integration and
mode boundaries retain the offline coverage described above.

## Larger markers requested by the user

Source `73e0c773e4017b3d809c46586c1253fb7f078403` scales the marker/label frame
28 to 42, diamond square 20 to 30 and ID font 18 to 27. Only these dimensions
changed; projection, ownership and lifecycle code remain as previously verified.
The existing marker assertions were updated. Verification passed 3,620 unit
tests (three environment skips), 28 marker assertions, all seven rejected source
faults and installed contracts. Independent source review found no clipping,
centering or zoom regression; the Release client/companion build passed without
writing the live install, then both client assemblies were explicitly deployed.

Fresh GPU-2 process `3778383a4fd74cd5b7d45ae6912e5ad1` loaded client MVID
`c5699ee6-a6f7-4b74-bfe1-f09380200515`, SHA256
`749408D9A69B5E7149ACCA90E05FB2E1C2A86AD9FDF2A292A2460343464F2381`; companion
MVID `ab74ba80-62af-47d6-8cf2-43f5741d9e41`, SHA256
`F60B3866E744A63E59DDA335FA90477931AA67B756378E6B3C520B1A273D2B00`.
Evidence is in `artifacts/domination-map-marker-size/`. The 16-step native
scenario passed: all three enlarged Iceberg IDs are centered and unclipped,
captured A turns red, reopening keeps the presentation, and manual disconnect
returns to MainMenu with `_current=null`. ArenaMode and DynamicMaps warning/error
queries are empty. Independent image review agrees. This larger-build native
check covers Iceberg overview/capture/reopen/disconnect; it does not repeat the
earlier build's full time-limit or Factory cases.

The first disk-profile comparison also found a new `spt/mods` entry for
PitFireTeam 1.1.0, beyond the two permitted bookkeeping timestamps. Its registration
timestamp matches `/singleplayer/clientmods` at 20:42:07 UTC, before Arena's
20:42:43 entry snapshot; it was not removed or hidden by relaxing the comparator.
A separate short raid/disconnect control in `profile-control/` used the saved
baseline containing that startup entry and passed the unchanged whole-profile
check with only `Hideout/sptUpdateLastRunTimestamp` differing. The original
comparison remains in `profile-diff.json`.

`profile-control/preservation.json` verifies all 240 configuration values restored
and all 49 companion/backup files byte-identical to the original pre-start
baseline. The config file differs only in 11 BepInEx range comments using decimal
commas instead of periods. The final status confirms MainMenu, no control lease,
godmode and time/weather hooks off, and the exact larger candidate still loaded;
the Error query across BepInEx sources is empty.
