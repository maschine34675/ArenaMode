# Ported-map Domination native acceptance — 2026-10-26

Four short native matches passed on SPT-GPU-2: Equator and Bay5 each use their
original X objective; AirPit and Bowl each use their separate original A/B
objectives. Gates released, the runtime verified routes from both opened team
boxes, bots reached/captured objectives, and objective income appeared in the
HUD and MapPort monitor projection. These are gameplay observations, separate
from the parent's build and source-linked checks.

## Candidate and method

- Client: `4E4EAC24D76513510B78F6D80E0030884E812C80CA0EB169C57A7F9EB74AC256`.
- BigBrain companion: `F43F074CADD68A8E8EEF78CF6148FA6261A00F10B0D7091A418F7F9E2705D8F1`.
- Server: `9CB6025157617A9B8314979A851AC1441D925EB449EA9974FBD34C90DB61F274`.
- `gpu-run start` started a fresh server and EFT with the designated SPT-GPU-2
  profile. Process instance `7ee14a9c312a4d379e06668b49e05e2c` reported all
  16 checked client plugins/libraries identical across workspace, host and
  loaded process. The freshly started server held the matching server DLL.
- Native menu clicks selected Domination and each map. Capture times, scoring,
  teams, bot counts and difficulty remained unchanged. DeveloperMode alone was
  temporarily enabled for diagnostics. Godmode protected the local observer;
  there was no infinite-ammo hook, artificial kill, capture invocation, private
  state write or time/weather override.
- After gates opened, bots played for 40 seconds without moving the player.
  The player was then teleported to each actual objective for 12 seconds.
  All six teleports passed native NavMesh, zone, room and reachability checks.
  Bots kept fighting, so local occupancy did not guarantee uncontested capture.
  Each scenario finished by disabling godmode and leaving normally to MainMenu.

Raw evidence is under `artifacts/domination-ported-20261026/gpu/`. The four
scenario JSON files, `<map>/report.md`, results and native screenshots are kept
there. `start-status.json` identifies the loaded process. Screenshots are actual
game frames at 1920×1080.

## Native results

Scores below are BEAR:USEC; the local player retained the original BEAR choice.

| Map | Actual objective centers, rounded to millimetres | Autonomous observation before player teleport | Later local observation |
| --- | --- | --- | --- |
| Equator | X `(1.280, 0.253, 4.220)` | BEAR had captured and earned 7:0; X was being neutralized at the snapshot. | X BEAR, 16:0. Kill scoreboard version remained 11 while objective income rose. |
| Bay5 | X `(91.260, -3.890, 29.440)` | USEC capture and 0:4; contested at the snapshot. | X remained USEC with 51% neutralization progress; no claim of an uncontested player-only capture. |
| AirPit | A `(18.960, 0.003, -1.240)`; B `(-45.380, 0.003, 3.820)` | A BEAR, B neutral/capturing, 20:0. | A BEAR/B USEC at 31:6; after visiting B, B became BEAR. Final sampled income 45:7. |
| Bowl | A `(-20.510, 0.072, -37.120)`; B `(16.990, 0.010, -30.720)` | Both A/B USEC, 0:35. | Local presence advanced A neutralization; B subsequently became BEAR. Screenshot then showed 1:61. |

All maps started in Assembling with gates held and became Live only after the
new opened-box connectivity check. Autonomous bot snapshots show both factions,
active combat and the Arena Combat Domination layer; native arrival/movement and
ownership logs accompany them. This proves practical objective participation in
these runs, not flawless navigation for every possible combat situation.

The Bay5 floor projection moved the original entity Y from -4.366 to -3.890,
within the accepted half-metre correction; its horizontal coordinates remained
unchanged. AirPit uses its two distant playable original entities, not their
midpoint under the aircraft. `native-points.json` contains precise AirPit/Bowl
coordinates; the earlier maps' verified coordinates are in their Domination logs.

## Rings, HUD, monitors and pickups

- `equator-point-0.png` shows X with the red owner ring. `airpit-point-0.png` and
  `airpit-point-1.png` show the two distinct red rings and the two-objective HUD.
  Bowl's A screenshot shows a blue USEC ring; its B screenshot shows the red
  BEAR ring after capture. Existing green map floor artwork remains independent.
- Bay5's close downward-facing screenshot does not clearly show our perimeter;
  it is not used as a visual ring-color proof. Its native objective state and
  capture progress were recorded. Bowl was naturally dark; no global lighting
  change was made for the photographs.
- Equator monitor readbacks matched 7:0 and 16:0 (9 displays); Bay5 matched 0:4
  (8 displays). AirPit and Bowl additionally read both gameplay counters and the
  monitor snapshot **in the same Unity frame**: 45:7 and 0:61 respectively, with
  red BEAR/blue USEC colors. The AirPit photograph aimed at a monitor was blocked
  by geometry and is not claimed as visual monitor proof.
- `bowl-monitor.png` is the clear visual acceptance: the actual wall score display
  and HUD both show **BEAR 1 : 61 USEC**, and its timer matches **08:54**. It was
  photographed after the preceding 0:61 same-frame read, across a normal income tick.
- Actual placement logs prove all six pickup types on each map, not merely six
  configuration entries. Bay5 also has a live floor/phase inspection: five were
  Available and Shield was cooling down after collection. AirPit/Bowl each have
  six individual floor/phase/beam readbacks: all six Available, all beacon objects
  active. Beams were not suppressed near objectives.
- `check-evidence.py` independently compares native objective centers with all
  24 initial placements and 13 recorded rotations: **37 events, zero clearance
  violations**, minimum horizontal distance **10.708 m**. The required exclusion
  is 6 m horizontally when within 2 m vertically. Evidence and exact floors are
  in `evidence-check.json`. This verifies recorded placements, not every future
  random selection.

## Logs and limits

`all-warnings.json` contains the merged client/server/EFT Warning+ export:
175 entries, no page or text truncation. No Domination preparation failure,
route failure, no-contest termination, exception or null-reference was observed.
The inspected gameplay proceeded normally. The environment is **not log-clean**:

- At server startup, before these tests, ArenaRewardStartup reported
  `reward-snapshot-restoration-unresolved` and `reward-import-not-settled`; it
  explicitly performed no profile repair. These predate the first test raid.
- Native/model messages include 50 `JetMaterial == null`, missing
  `extractor_smoke` scripts, absent weather/seasons objects, normal spawn-marker
  backend synchronization/repairs, missing weapon `UtilityOperationEvent`, and
  the existing dependency/Harmony declaration warnings.
- AirPit at 17:35:32 local logged four R870 `mod_stock_003` missing-bone lines;
  at 17:36:10 it logged one `FATAL There is no handler for hash:<b>0</b>` without
  a stack. Bowl also logged `no foldable found` for a Benelli model. These did
  not abort a match. Their text/timing alone does not establish causation by
  Domination, ArenaMode, Redeploy or Killcam, and they are not claimed fixed.

These were representative short matches, not performance benchmarks or full
200-point/end-clock matches. No new exhaustive death/respawn, terminal reward,
ordinary-PMC isolation, night/weather or capture-through-wall/floor negative
acceptance is claimed here. The earlier Factory acceptance and the parent's
offline checks remain separate evidence. Full-round balance still needs play.

## Cleanup

- All **228 Arena settings** equal the original readback. The three changed
  entries were restored: DeveloperMode=false, GameMode=teamdeathmatch,
  ExperimentalArea=customs-factory-center-polygon-v2.
- VM and host config bytes both exactly equal the original backup:
  `C482A82FA32B288D20FDD1BFD1525BA3DEA79D71E3729BC40ED36AEB262BF70D`.
- The complete canonical test-profile PMC inventory is semantically and
  serially unchanged: **303 items**. No profile reset or inventory write was used.
  Local private inventory snapshots are retained only as ignored evidence.
- Before exit, Current match was null, pickup entries empty, both cached pickup
  layouts null, monitors unclaimed, gates not held. Godmode/infinite ammo,
  tracking/walking/fire and time/weather overrides were off; control was free.
  All 41 inspected handles were released.
- EFT quit gracefully; the bridge then remained unreachable for five seconds.
  The server stopped through gpu-run. Final host status reports **server off,
  game off, no second server/EFT/launcher**, restoring the initial state.

See `cleanup-inspect.json`, `end-mainmenu-status.json`, `config-restored.json`,
`inventory-comparison.json`, `evidence-check.json` and `end-server-status.txt`.
