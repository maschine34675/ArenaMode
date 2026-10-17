# Bay5 original TeamFight boxes — 2026-10-17

Scope: independent Team Deathmatch only. Other Bay5 modes, PFT/Tactical,
AirPit, ordinary PMC/Scav raids and MapPort bundles are unchanged.

## Formation and ownership

`ArenaTeamBoxCatalog` supplies map-specific immutable data to the common
`ArenaTeamBoxRuntime`. Equator retains its accepted original coordinates.
Bay5 uses all twenty level88 TeamFight starts from `SPAWNS/Usec house` and
`SPAWNS/Bear house`: ten USEC starts in the eastern warehouse, ten BEAR
starts in the southwestern depot. The depot's two authored elevations are
preserved; source positions alone are not proof of live navigability.

Bay5 TDM accepts explicit even participant totals from 4 through 20. Its
default remains 8. Other modes retain their own participant limits.

The per-raid server clone gains versioned private markers with 0.8 m sphere
colliders. The original `arena_bay5-bot-west-centre` donor at
`(63.497,-3.8574,42.2217)` and all common markers remain unchanged.
The separately surveyed outside path destination is the central yard at
`(89.159,-3.693,35.203)`. The donor is not the path destination.

Authored headings remain unchanged in the catalog. Backend marker rotations
use Unity's Euler representation (including its small negative zero tolerance):
EFT `SpawnPointMarker.FixParams` compares scalar yaw, so sending the warehouse's
original -90 degrees instead of its equivalent 270 produced false map errors.
The native 3v3 trial exposed this; validation remains strict and no log is filtered.

Both client and server reject foreign map prefixes, missing/duplicate markers
and drifted headers. Native floor, headroom, occupancy and NavMesh checks
remain mandatory. Loading/initial selection/respawn and pending reservations
share the exact current game, world, map definition, arm epoch and identity.
Missing preparation never permits fallback to map-wide starts.

## Gates and lifecycle

ArenaMode requests MapPort's existing public gate hold only for its exact
loading raid, after native gate parts exist. It never borrows another hold.
While its own gates remain shut, a canonical start can use a complete real
path to an anchor in the same original box; every corner must stay inside
that box. Partial paths after release are never accepted.

At the end of the preparation countdown, gates open. Live scoring, match
time and the start announcement wait for complete native paths from all
twenty starts to the yard. Respawns keep the gates open. Stale callbacks
cannot release a later world's gates. Native startup failures retain the
protected early-abort path.

Bay5's two native exits are outside these boxes, so neither is suppressed.
Equator's narrowly scoped deferred booth exits remain Equator-only.

## Verification

Read-only source review matched all twenty coordinates/headings, original
box volumes, faction anchors and installed donor headers. The shared client
harness now exercises both maps; gate checks include Bay5's two elevations,
cross-map rejection, countdown, respawn and untouched Bay5 exits. Existing
Equator negative controls are retained.

Offline verification after integration with release-layout changes:

- Full server/data suite: 1081 passed, one explicitly skipped installed-APBS
  probe. Bay5 fixtures also consume the real installed donor via admission checks.
- Both-map linked runtime: 212 assertions, twelve rejected behavioral mutants.
- Gates/countdown/exits: 144 assertions, twenty-one rejected behavioral mutants.
- Participants: 4244 assertions; map/mode admission: 648.
- Native-menu model: 717; TDM mode/relations: 340; extracted burst preparation:
  93; give-up: 626; failed-start abort: 112; Customs: 392; area menu: 66;
  native area hook: 195. These are production-linked offline models, not native
  gameplay proof.
- TDM match/exit model: 247 assertions plus its existing fault controls.
- Final isolated client/server build passed on clean `4d5565f`; staged manifest
  `artifacts/area-map-delivery/9a5563f15d1049aa91b06194939e780d/manifest.json`.
  Staging verified 89 installed files unchanged. Only existing compiler warnings.
- Independent read-only source and native-log reviews found no outstanding
  defect in the scoped box/gate implementation.

## Native acceptance and delivery — GPU2, 2026-10-17

The final source build is `4d5565f10e41b8ba4897bffb19b91375e58f52ec`:

| Artifact | SHA256 |
|---|---|
| Client | `07186D3395CB7D273DD85098B1E44E971B2E10E984695CE2B5B3A60F2CB52359` |
| Server | `0D532C499742950F55178971F1921D5DA3772F734232151AE3A349347AA1DA41` |

Only these two DLLs were deployed locally and synchronized to GPU2. UiBridge
confirmed all 20 `maschine-*` plugins matched VM, host disk and loaded process;
44 plugins loaded successfully. Core, MapPort and other mod DLLs were preserved.
Concurrent release-layout work was merged before building, not overwritten.

- **Bay5 3v3, preliminary `6e2849c`, instance `24ea0706`:** both 10-place
  formations passed native floor, headroom and navigation checks. All five bots
  appeared on their faction's original starts. After the countdown, all twenty
  outside paths completed. Both bot teams left their boxes; the player reached
  the yard by real walking. A death with replay returned the player to the USEC
  warehouse, followed by another successful walk out. Natural USEC/BEAR losses
  produced replacements in the correct boxes. This trial exposed the harmless
  scalar-yaw mismatch, corrected in the final build.
- **Bay5 10v10, final build, instance `7eff8fc9`:** nine allied and ten enemy
  bots became active. Both formations reported 10/10 safe places, ten required.
  Exactly one hold at 15:32:53.707 reclosed twenty parts and installed seven
  carvers. Assembly completed at 15:33:42.574; release at 15:33:45.584 disabled
  all twenty parts and removed all seven carvers. At 15:33:46.590 all twenty
  outside paths passed and Live began. Both bot teams entered the arena and
  fought. Player walks of 64.6 m and, after respawn, 69.3 m both arrived in the
  yard. The replacement player arrived at warehouse-03 at 15:34:23.458. No new
  hold or closure occurred before the protected exit; natural bot replacement
  continued. The warehouse rotation errors were absent.
- **Equator 3v3, same final process after Bay5:** all twenty places passed;
  original faction starts, countdown and opening remained correct. Nine
  probeable doors changed from partial paths while held to complete paths
  after release. All twenty outside paths passed at 15:36:18.140. All five bots
  left their respective booths, and the player walked 45.5 m to the atrium.
- **Protection:** all three native raids returned to the menu normally. Each
  actual server entry backup compared with the post-raid profile had 314/314
  items; only the normal Hideout timestamp differed. The broader pre-start
  baseline additionally differs in the menu's health-update timestamp. All
  twelve PFT follower files and the server config were unchanged. The sole
  changed client preference, Bay5's test participant count, was restored to 8.
  Game and server were stopped after testing.

Bay5's MapPort doorway probe could not grade individual openings because its
probe endpoints were already disconnected on that map's mesh. Do not treat that
probe as a success: the acceptance above uses Arena's complete paths, active
gate/collider logs (including blocked depot rays while held), real departures
and correct lifecycle. No claim is made that every possible bottleneck is free
of congestion at 10v10, or that 10v10 is a performance recommendation.

No ArenaMode warning/error occurred during the final Bay5 raid. EFT still logs
existing common MapPort marker reconciliation, `JetMaterial == null`, weather
and scene-singleton messages; these were not suppressed or attributed to this
change. Ordinary raids, other modes and PFT were covered by offline scope checks,
not a new native gameplay run in this acceptance.

Evidence is retained under `artifacts/bay5-native-20261017/`: both client logs,
merged native/server logs, loaded-build status, bot positions, walk results,
per-raid profile comparisons, baseline/config/PFT audit and delivery manifests.
