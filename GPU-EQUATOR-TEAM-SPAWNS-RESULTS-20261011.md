# Equator Team DM faction boxes — GPU2, 2026-10-11

Result: initial and replacement Team DM actors use their faction's original
team box. Native 3v3 and a same-process nonteam control passed on the exact
deployed build. Capacity is 4–20 total, default four; saved six was preserved.
USEC is north, BEAR south. Movement after spawning remains unrestricted.
Equator Tactical and PFT Zombies remain excluded; their admission was not widened.

All times below are Europe/Berlin (UTC+2). JSON and server logs use UTC.
Evidence root: `artifacts/equator-team-native-20261011/`.

## Source, staged build and deployment

- Implementation `381f7846cdc1f8ad88b02ac20230dbbb33ea1548`; nullable declaration
  follow-up `96ed83ba640004f75ddce3466cb751b483829bf0` is the final build source.
- Final manifest:
  `artifacts/area-map-delivery/ccee66ac4e6348b09aa8121f8f615f36/manifest.json`.
  Build: zero errors, four pre-existing client warnings and two server warnings.
  The staging check preserved 84 installed files. Earlier staging was not deployed.
- Only Arena client/server DLLs were installed locally and on GPU2 at 14:55:11,
  with both game and server stopped. No Core, MapPort, PFT, SAIN, UiBridge or
  other plugin rebuild/deployment. Unrelated Molotov assets remained untouched.
- Both ProductVersions: `0.1.0+96ed83ba640004f75ddce3466cb751b483829bf0`.
  Client SHA256:
  `7E43D1E4732258DE81C0CB759B09A066A1EF0582FF2E610A4B341C5ADC471A9D`.
  Server SHA256:
  `8652E73A4A7E9B57783034547264B5D9042A4C4611AE8BEB1FFDF019FC2E0659`.
- EFT restarted at 14:55:42, instance `7c77d5bfc6c747df999ad0529194f2fd`.
  UiBridge verified 43 plugins and five libraries identical between local disk,
  GPU disk and loaded code. Profile `SPT-GPU-2`, USEC,
  `6ab7db1c64f04b565cf78be9`. Final status confirms the same process/build.

## Native Team DM 3v3

The native map menu displayed six participants / 3v3, recommended four / 2v2.
No participant setting was changed. Screenshots of the menu, initial box and
respawn box were visually inspected at 1920x1080.

- 14:57:40.159 / #2942: initial human USEC placed at `north-06`,
  `(4.553, 0.08, 45.44)`. MapPort's general booth repair runs first; only the
  twenty private Team DM markers are restored afterward.
- 14:57:54.658 / #3047–3049: actual scene capacity is USEC 10/10 and BEAR 10/10
  safe and connected. Required formation 3v3 accepted.
- First two allied bots and three enemies arrive in their own north/south boxes
  (#3150, #3160, #3179, #3188, #3204). Native ready/countdown completes;
  Team DM becomes Live at 14:58:17.000 (#3218).
- Natural bot deaths produce replacements for both factions at 14:58:33–34
  (#3252, #3261), again in their own boxes. Across the raid: 17 admitted bot
  arrivals, including twelve replacements after the initial five; no wrong side.
- Both teams leave the boxes and fight. Awareness diagnostics show direct
  displacement of 55.5 m for a USEC and 42.6 m for a BEAR within 30 seconds.
  These are direct position differences, not accumulated walking distances.
- The player walked to the atrium using the existing UiBridge walk helper,
  then died naturally. No teleport, godmode, health setter or forced kill was
  used in this Equator run. Respawn reserved `north-05` at 14:59:44.967 (#3313);
  actual replacement body arrived at `(-6.23, 0.08, 48.59)` at 14:59:45.041
  (#3340), with 440 HP. The visually inspected screenshot shows the box again.
- Protected native Disconnect returned to MainMenu. No pending bot requests
  remained (#3576); server snapshot restored at 15:03:11.807 and protected end
  persisted at 15:03:11.838.

Receipts: `select/`, `ready/`, `respawn-proof/`, `live-log-final.json`
(3,615 entries, no missing sequence coverage). An independent agent audited
all starts/arrivals and the exit against that export.

## Nonteam control in the same process

Without restarting EFT, a second protected raid selected Deathmatch/Equator.
Only `Match/GameMode` was temporarily pinned, with its prior Team DM value
recorded before the change and restored afterward.

- Armed Deathmatch at 15:05:08.576 (#3687), active at 15:05:18.472.
- Human starts at `(-4.5, 0.1, 38.5)`, outside the Team DM box. The configured
  four-person population starts three bots (#3870–3878); live reads show all
  three active across the map. No private-team-marker restore, capacity gate,
  team-box placement or arrival message appears in this control's loading/raid.
- Protected Disconnect again returned to MainMenu; server restored the snapshot
  at 15:06:17.385 and completed the protected end at 15:06:17.497.
- Config restoration reports one restored value, zero conflicts/missing keys.
- Independent audit of control sequences 3616–3947 found 332 contiguous entries,
  zero Error/Fatal or Arena warnings, and no team-spawn hook activity. Four LOD
  warnings, the intentional API probe and Culling cleanup were the only warnings.

Receipts: `control-config.json`, `control-select/`, `control-ready/`,
`control-live-state.json`, `nonteam-control-final-log.json` (3,947 entries).
Six stored MapPort/other long log entries are already cut at 2,000 characters;
the spawn, damage, exit and profile evidence used here is not among them.

## Errors and preservation

No Error/Fatal, Arena warning, no-contest or failed spawn was observed in the
Team DM raid. Other diagnostics include the deliberate RespawnApiProbe
AlwaysThrow exception test, stale PerfectCulling sampler cleanup and a negative
BoxCollider scale on an MCX handguard asset; none caused a spawn failure.

A fresh baseline was made before root deployment/actions, after the earlier
Friendly Fire tests had ended. At 15:07:01, the full comparison after both raids
found only `Health/UpdateTime` and `Hideout/sptUpdateLastRunTimestamp` changed in
the profile. All twelve PFT files remained byte-identical, every existing config
assignment was restored, and all 138 recorded DLL paths were checked: only the
two intended Arena DLLs on each machine changed. These are 137 distinct paths;
the ledger contains an alternate-slash duplicate of the GPU server path.
No profile JSON or companion file was overwritten by the tester.

Evidence: `baseline/`, `deployment.json`, `preservation.json`, `profile-after.json`,
`server-log-20261011.log`, `final-status.txt`. The game was left at MainMenu,
server running; walk helper stopped, no active test behavior. No Git push.

## Offline checks and limits

Full xUnit: 959 passed, one pre-existing APBS integration skip, zero failed;
selected server checks 158 passed. Final Equator runtime: 100 assertions,
eleven compiled negative controls, restored green baseline. Burst integration
93/17 controls, TDM integration 247/20, map admission 540/9, native menu 640,
participant UI 3,730 plus native hooks 195 and area menu 29/six controls;
bounded area runtime 459 plus thirteen client contracts. Independent source
review found no remaining concrete P0–P2 issue. The final build changed no
runtime logic after those checks; the nullable follow-up reran Equator checks.

Actual 10v10 occupancy, local BEAR initial placement, every individual candidate
occupied by a body, intentionally crowded-box failure and ordinary PMC were
not exercised natively. Both-side scope, capacity, occupancy, stale-raid and
protected-failure cases have production-linked offline coverage. Do not convert
the scene's 10/10 capacity logs into claims of native 10v10 match acceptance.

The preceding Friendly Fire work has its separate report,
`GPU-FRIENDLY-FIRE-RESULTS-20261011.md`. Its OFF veto and ON actual native damage
record are observed; a paired sampled ON HP comparison remains unproven.
