# Block Arena integration

Date: 2026-11-21, branch `feature/yard` from `master` `6b3ba6a4`, commits `30892f9a` (admission), `c071cb81` (Amand's
Graphics pin and comments) and `fb2b3af6` (seven south start heights carry the measured floors). This adds
`arena_yard` (Arena's own name: **Block**) as an optional ArenaMapPort location the way Chop Shop was added on 12
November ([contract](CHOP-SHOP-INTEGRATION-20261112.md), [follow-up](SPAWN-ROWS-FOLLOWUP-20261113.md)), after Iceberg
([contract](ICEBERG-INTEGRATION-20261031.md)) and Bowl ([admission](BOWL-INTEGRATION-20261023.md),
[team boxes](BOWL-TEAM-BOXES-20261023.md), [Domination](DOMINATION-PORTED-20261026.md)). ArenaMode neither
distributes nor changes the map. The location id is MapPort's and fixed; "Block" is the player-facing name, the label
on Arena's location screen ("BLOCK") and the name in MapPort's locale ("Arena: Block"). **The map has nothing to do
with the Customs yard area** (`ArenaAreaCatalog`'s Customs Yard, `CustomsYardId`): the catalogue members for this map
are named `YardId` and `IsYard` after MapPort's id, and every test, harness and log label uses "Block".

**Status in one line.** Admission, team boxes and Domination are implemented and were accepted natively on GPU-2 on
2026-11-21 with the installed build `0.6.0+fb2b3af6` ([native report](../reports/2026-11/GPU-BLOCK-20261121.md)):
Deathmatch, Team DM 4v4 and 10v10 with held gates, Domination, solo Zombies, solo Tactical and an ordinary PMC control
ran with no error line and no warning from ArenaMode or ArenaMapPort in each accepted run (Unity's own warnings
appeared in three of the raids; native report, "Warnings"). The first Team DM start on build `0.6.0+c071cb81` was refused
(`USEC 0/10 safe connected places`); the cause was the start heights of seven south points, fixed in `fb2b3af6`. The
first offline pass had 105 of 109 steps green and four red steps with named causes; the final offline pass has 109
of 109 green, one of them on a second run ("Verification"). What was not run is under "Not claimed". Every decision under "Decisions" is the
orchestrator's and has not been confirmed by the owner.

**MapPort requirement and detection.** Block exists only in an ArenaMapPort build that ships `arena_yard`. Today that
is the unreleased `feature/yard` build installed on this machine and on GPU-2, `0.6.0+1d8faa8b` (the commit messages
and `DESIGN.md` write the shorter `0.6.0+1d8faa8`; client SHA-256
`083f09cf6890c02bbdd2ea8a050bdb54415c02a15ce7ce3181eb7fc799d84b7f`, server
`d3d821b23d0c8c003d3b2c74e674e28c53545ae1994f60d1acfcd9f3e61c5219`, both from the native facts). MapPort's `HEAD`
`3310232` differs from `1d8faa8` only in documents (read in the MapPort tree during the survey). The published
ArenaMapPort 0.6.0 has no Block and carries the same version string, so ArenaMode detects the map by an enabled live
location with the exact id (server: `ArenaOptionalMapPolicy` on every client-settings request; client: the MapPort
plugin identity), never by version. Without such a build the map is simply not offered. **Announcement rule:**
`README.md`, `docs/FORGE-DESCRIPTION.md` and `assets/arena-whitelist.json` stay untouched, and no ArenaMode release may
announce the map, until an ArenaMapPort release ships it under a new version number. MapPort's own notes call 0.7.0
"by pattern"; the number and the form are the owner's, and nothing here assumes them. This document is a record of
work on an unreleased map, not an announcement.

## Admission

Commit `30892f9a`.

- Catalogue: `YardId = "arena_yard"`, `IsYard`, the canonical lookup and `AnyMapAvailable` (`ArenaMapCatalog.cs`).
  Unknown spellings stay unsupported; `BlockMapTests` keeps five look-alikes out and accepts `ARENA_YARD`.
- Modes: Deathmatch, Gun Game, Team DM (the team boxes below), Domination (two points), Zombies solo and with a
  PitFireTeam squad, Tactical solo and with a PitFireTeam squad. **No mode is refused and the picker shows no hint.**
  Domination with a PitFireTeam squad keeps the generic refusal text. Block is the last link of the explicit whole-map
  Domination list in `ArenaAreaCatalog.TryResolve`, so a future port is still never admitted by default. Natively, the
  tile was offered under Deathmatch, Team DM, Domination, Tactical and Zombies; Gun Game and the PitFireTeam variants
  did not run.
- Participants: default **eight** (`ArenaParticipantPolicy.DefaultTotal`, the group of Bay5, Air Pit, Bowl, Iceberg and
  Chop Shop). Ranges stay generic: 2-24 solo, 4-20 through the boxes (`MaximumFor`).
- Server defaults (`ArenaModeConfig`): `BotZone_Main`, `BotMax 8`, `MaxBotPerZone 8`, `BurstMinAliveBots 4`, as for
  every port. `ArenaOptionalMapPolicy` removes the map from the client view when the live location is not available.
- Presentation: gold **BLOCK** label (`ArenaPortedMapPresentation.DisplayName`) at MapPort's native anchor. MapPort
  places the marker at 691/857 itself (its own selection data moves it from the measured 691/799, which covered THE
  LAB; `markers.json` lines 174-186); there is no `_reposition` entry. Natively the label stands directly above THE
  LAB, clear of the Arena panel and of the right-hand panels (see the native report). Server time, no relabel.
- Pickups (`ArenaPickupMapLayout.Supports`, built only from the current raid's Bot/BotPmc rows), phase lighting
  (`ArenaLightingPolicy.SupportsMap`) and both PFT coverless adapters (spawn core and combat) accept the exact id. Both
  PFT modes are admitted on this map, so the adapters can run on it. The two compiled faults
  `YardMissingPftSpawnAdapter` and `YardMissingPftCombatAdapter` in `tools/AirPitChecks/NegativeControls.ps1` cover the
  entries; both were rejected in the first offline pass (`BlockChecks.log`: "Rejected compiled fault:
  YardMissingPftSpawnAdapter" and "...YardMissingPftCombatAdapter").
- Text: the F12 `General/Maps` description and the upgrade log line name Block; the Tactical `Objective` description
  lists it.
- Commit `c071cb81`: `AmandsGraphicsSceneChecks` pins the number of catalogue ids, and `arena_yard` makes them twelve;
  the pin moved from eleven to twelve. The scene repair itself follows `IsSupportedMap`, so it covers Block as it
  covers the other ports; it was not run on Block (code and offline checks only).

## Migration

Optional-map revision **7**. New `AutoServiceDefaultClientAllowlist` (the former default); `DefaultClientAllowlist` is
that plus `arena_yard` (`ArenaMapCatalog.cs`).

| Saved list | Revision | Result |
|---|---|---|
| Exact Chop Shop default (all six ports) | < 7 | gains Block |
| Exact Chop Shop default | 7 or later | unchanged (Block removed by the user) |
| Exact Iceberg default (Bowl + Iceberg) | < 6 | gains Chop Shop and Block |
| Exact Iceberg default | 6 | unchanged: Chop Shop was removed by the user, so neither Chop Shop nor Block is added |
| Exact Bowl default | < 5 | gains Iceberg, Chop Shop and Block |
| Exact Bowl default | 5 | unchanged: Iceberg was removed by the user |
| Exact AirPit default | < 4 | gains Bowl, Iceberg, Chop Shop and Block |
| Exact AirPit default | 4 | unchanged (Bowl removed by the user) |
| Exact Bay5 / Equator / native defaults | their old cutoffs (< 3 / < 2 / <= 0) | gain everything up to Block |
| Custom lists, disabled entries, per-map tuning | any | user-owned, unchanged |
| Malformed or future revision marker | - | Server: a malformed marker is read as the highest revision (`int.MaxValue`), so nothing is added and a removed map stays removed. Client: the marker is a typed BepInEx entry; a future revision (above 7) skips the migration, and a value that does not parse as an integer falls back to the entry's default 0, which is not a "future" marker (the same reading as for Chop Shop) |

The server's `ConfigStore` mirrors it (`autoServiceDefault = revision < 7 && ExactMapSet(AutoServiceDefault)`, a
removal clause for `YardId` that also tests `!icebergDefault` and the older flags) and records revision 7. Removing
Block after the upgrade survives the next repair and a real `ConfigStore.Reload`
(`BlockMapTests.RealReloadPersistsBlockAndHonorsItsRemovalOnNextReload`). The client binds the same policy through the
real BepInEx `Settings.Init` (`tools/AreaConfigChecks`; new rows `block-release` (Chop Shop default at 6 gains Block)
and `block-removed` (Chop Shop default at 7 stays); `future-version` moved from revision 7 to 8). `BlockMapTests`
holds the table as a 26-row theory.

**Read back on the installed configuration (GPU-2, build `0.6.0+c071cb81`, first start).** Client `General/Maps`
gained `arena_yard` and `OptionalMapsRevision` went 6 to 7; server `config.json` `optionalMapsRevision` went 6 to 7 and
a new `arena_yard` row appeared (enabled, `spawnZones` `BotZone_Main`, `botMax` 8, `maxBotPerZone` 8,
`burstMinAliveBots` 4). Both lists were the exact Chop Shop default before. Only that row of the table was read back;
the other rows are held by the xunit theory and `AreaConfigChecks`.

## Team boxes

The pockets are the map's two authored TeamFight bounds (`D:/ArenaMapPort/tools/maps/yard.modes.json` lines 110-159,
scene `Arena_Yard_TeamFight_DesignStuff`, package `yard-native-v2`). Both have yaw 0.0 and rotation `[0,0,0,1]`, so the
boxes are world-aligned and their min and max are exact. **USEC is the south pocket**, the raised platform (y 6.9 ..
7.5) behind the rising gondola `DOORS/Construction_Cradle_02`; **BEAR is the north pocket**, the room behind the two
rotating leaves `DOORS/Metal_gate_1`. That matches MapPort's rows: `arena_yard-player-usec` and `arena_yard-player-all`
stand in the south pocket, `arena_yard-player-bear` and `arena_yard-player-north-all` in the north one
(`yard.bots.json` lines 5-62). MapPort marks that split as a copy of its released maps ("whether ArenaMode's team boxes
agree is for the host", `yard.bots.json` line 2); ArenaMode follows it. In the scene the south markers are named `Usec
house (5)` to `(14)` and the north markers `Bear house (5)` to `(14)` (`yard.modes.json`, field `stem`); the formation
uses the containing bound, not the name. The formation `arena_yard` / "Block" / "USEC south / BEAR north" adds twenty
private `arena-yard-tdm-v1-south-01..10` and `north-01..10` records (infiltration `ArenaYard`, `BotZone_Main`, core 0)
only to the protected raid clone, USEC first, with the authored X, Z and yaw in sidecar order; marker `(n)` is point
`n - 4`. Seven south heights are not the markers' (see "Heights"). No installed row starts with `arena-yard-tdm-v1-`.

Installed Player rows (`SPT_Runtime/user/mods/ArenaMapPort/db/arena_yard/base.json`, identical to the repository copy):
`arena_yard-player-all` [All] (-12.68, 7.27, 9.88) south, `arena_yard-player-usec` [Usec] (-14.86, 7.27, 13.3) south,
`arena_yard-player-bear` [Bear] (-10.83, 6.18, 145.09) north, `arena_yard-player-north-all` [All] (-7.22, 6.19, 146.59)
north, all infiltration `ArenaYard`. **Two rows have Sides "All"**, which Chop Shop did not have, so the harness
classifies a Player row by the box that contains it, not by its Sides.

**Interiors.** Each horizontal face is the authored AABB inset by the 0.35 m capsule radius `FloorAndClear` uses,
rounded to three decimals (`ArenaTeamBoxCatalog.Yard`). **No face is moved to a gate**, unlike Iceberg and Chop Shop.

| Face | Authored bound (world) | Used | Reason |
|---|---|---|---|
| USEC (south) x min | -25.4259 | -25.076 | authored, inset 0.35 |
| USEC x max | -4.9974 | -5.347 | authored, inset 0.35 |
| USEC z min | 0.7234 | 1.073 | authored, inset 0.35 |
| USEC z max (toward the gondola) | 16.7244 | 16.374 | authored, inset 0.35; no gate clip, see below |
| USEC y min / max | 6.3275 / 11.9159 | 6.3275 / 11.9159 | authored band kept, see "Heights" |
| BEAR (north) x min | -15.0885 | -14.738 | authored, inset 0.35 |
| BEAR x max | 0.2525 | -0.098 | authored, inset 0.35 |
| BEAR z min (toward the leaves) | 141.1174 | 141.467 | authored, inset 0.35; the leaves stand 13.25 m south of it, no clip |
| BEAR z max | 155.6910 | 155.341 | authored, inset 0.35 |
| BEAR y min | 5.3275 | 5.3275 | authored |
| BEAR y max | 10.9159 | **8.6** | cut below a separate deck, see "Heights" |

**Why no gate-face clip.** On Iceberg and Chop Shop the authored bound reaches past the closed gate, so the face toward
the gate was held at the gate leaf's pocket-side face plus the radius. Here the arithmetic runs the other way. South:
the gondola's closing cube spans z 16.199 .. 17.065 over x -24.345 .. -18.25 and the rest of that edge is the
building's wall (`Respawn_Outdoor_COLLIDER`, z 16.2 .. 16.3), so the inset face (16.374) ends inside the cube and the
wall, and the yard begins at 17.065, beyond even the authored face (16.7244). The gate rule of the other ports would
put the face at 16.199 - 0.35 = 15.849. The built NavMesh of the sealed room reaches z 15.875 along that wall, so a
held path corner there (0.026 m beyond 15.849) would count as outside the box. North: the leaves' closing cubes stand
at z 127.77 .. 127.864, 13.253 m south of the authored bound's z min (141.1174), so no gate face touches the box at
all. Sources: the face and cube numbers are the survey's reading of the package scene
(`work/extract/yard-native-v2/scenes/Arena_Yard_TeamFight_DesignStuff.json`); the 15.875 is the survey's reading of the
built bundle; both are quoted in the comment above `ArenaTeamBoxCatalog.Yard`.

**The north corridor.** The sealed north pocket is the room (built mesh x -14.38 .. -0.62, z 142 .. 154.6, y 6.15 ..
6.55) plus a corridor of 3.5 m agent-centre width (x -11.38 .. -7.88, z 128.6 .. 142, 13.4 m long) that leads to the
leaves; together 179.5 m2 (survey read of the built bundle). The authored box covers the room only. It is not extended
to the leaves: a world-aligned box from the leaves to the room would take in the ground on both sides of the corridor.
The corridor therefore counts as outside the box while the gate is held. That is harmless for the formation, because
every start point, both anchors and the exit stand in the room, so a held path between them has no reason to leave it.
`BlockTeamSpawnTests` pins the point (-9.6, 6.2, 135) as outside both boxes. Natively the formation held with this
box (see "Values the native run settled"); a player walking the corridor during a hold was not run.

**Heights.** Both authored bounds are 5.5884 m tall.

- South keeps the authored band 6.3275 .. 11.9159. Read from the built mesh, the platform and its ramp lie at 6.5 ..
  8.65 (marker heights 6.8833 .. 7.5003; collision floors 6.883 .. 7.483 as measured). The walled-off yard-level band
  south of the building wall (component 1, y 6.15 .. 6.20, about 99.5 m2 inside the footprint) ends 0.13 m below the
  band, and the only roof island (y 12.12 .. 12.21) starts 0.2 m above it, so the authored band excludes both.
- North is cut at **8.6**. The room's floor is 6.15 .. 6.55 (points 6.1773 .. 6.1923). A separate deck (component 43, y
  9.16 .. 9.72, about 18 m2 at x -13.75 .. -10.25, z 150.25 .. 154.75) lies inside the authored band. 8.6 is 2.05 m
  above the room's highest surface (6.55) and 0.56 m below the deck. `BlockTeamSpawnTests` pins y 9.2 as outside and y
  8.5 as inside.
- Two small islands lie inside the bands and are not connected to the pocket floors on the built mesh: south y 7.4 ..
  7.76 at x -12.25 .. -10.75, z 13.25 .. 14.25 (about 2.4 m2, component 108) and north y 7.04 .. 7.50 at x -6.25 ..
  -3.25, z 153.75 .. 154.25 (component 98). So no held path and no formation point can use them. This is a reading of
  the built mesh only; no native survey looked at them (see "Not claimed"). The north footprint's south-east corner (x
  -6.75 .. 0.25, z 141.25 .. 145.75, y 6.15 .. 6.19, component 4) holds ground outside the room's wall on a separate
  component.
- Smallest marker margins to their own box, pinned in `BlockTeamSpawnTests` from the literals and checked by
  subtraction here: south 0.5558 vertically (the markers at y 6.8833 over the floor of the box) and 2.1948
  horizontally (x -22.8812 against -25.076); north 0.8498 vertically (y 6.1773 over 5.3275) and 1.369 horizontally (z
  142.836 against 141.467).

**Seven substituted heights.** Seven south points carry the collision floor measured under them, not the height the
marker is authored at. Arena authors the six platform markers `Usec house (5)`, `(6)`, `(9)`, `(10)`, `(11)` and `(12)`
(south-01, -02, -05, -06, -07, -08) at y 7.2673 (7.2653 for `(6)`) on the floor piece `Respawn_Flat`, whose collision
floor is 7.368, so 0.101 to 0.103 m under it. Marker `(8)` (south-04) is authored at 7.2693 on the entrance piece
`Respawn_entrance_indoor`, whose collision floor is 7.483, 0.214 m under it. `FloorAndClear`
(`ArenaTeamBoxRuntime.cs`) refuses a point in two ways:

1. Its floor ray starts 0.25 m above the point, reaches 0.5 m down and refuses a hit more than 0.15 m from the point's
   y. Only `(8)` fails this (0.214 m).
2. Its clearance capsule has end-sphere centres 0.45 m and 1.45 m above the point and radius 0.35 m, so it reaches down
   to 0.10 m above the point, and any collider but a player's inside it fails the point. A floor 0.101 m above the
   point is already inside it. The six `Respawn_Flat` markers fail this.

`30892f9a` saw only the first rule and substituted `(8)` alone, on the argument that the other platform markers were
within 0.11 m of their floors. The first native Team DM start then reported `Block team-box capacity: USEC 0/10 safe
connected places, 4 required`: the USEC anchor `south-05` is one of the six, and a held path needs a safe anchor, so
none of the ten places counted (see "The first Team DM start"). `fb2b3af6` gives all seven points the measured floor;
X, Z and yaw stay the markers'. The unchanged `FloorAndClear` tolerance is not loosened. The capsule is the cause named
in `fb2b3af6` and in the native facts; the logs show the count 0/10 before and 10/10 after, and no capsule query was
logged. The measurement (GPU-2, 2026-11-21, ordinary PMC raid, the body stood on the point, one ray down from the
camera; see "Native pre-survey"):

| Point | Authored y | Catalogue y | First ray hit y (collider) | Authored - floor | Catalogue - floor |
|---|---|---|---|---|---|
| **south-01** | 7.2673 | **7.368** | 7.368 (`Respawn_Flat`) | -0.101 | 0.000 |
| **south-02** | 7.2653 | **7.368** | 7.368 (`Respawn_Flat`) | -0.103 | 0.000 |
| south-03 | 7.5003 | 7.5003 | 7.483 (`Respawn_entrance_indoor`) | +0.017 | +0.017 |
| **south-04** | 7.2693 | **7.483** | 7.483 (`Respawn_entrance_indoor`) | -0.214 | 0.000 |
| **south-05** | 7.2673 | **7.368** | 7.368 (`Respawn_Flat`) | -0.101 | 0.000 |
| **south-06, -07, -08** | 7.2673 | **7.368** | 7.368 (`Respawn_Flat`) | -0.101 | 0.000 |
| south-09, -10 | 6.8833 | 6.8833 | 6.883 (`Respawn_entrance_indoor`) | +0.000 | +0.000 |
| north-01 | 6.1873 | 6.1873 | 6.134 (`Yard_courtyard_01`) | +0.053 | +0.053 |
| north-03, -04 | 6.1773 | 6.1773 | 6.134 (`Yard_courtyard_01`) | +0.043 | +0.043 |
| north-02, -05 .. -10 | 6.1923 / 6.1773 | unchanged | not measured | - | - |

Source: `artifacts/yard-20261121/native/survey/rays-run/results/ray-*.json`, first hit of each ray (the hit lies within
0.08 m horizontally of the point because the ray runs from the camera). Signed differences are by subtraction from
those hits. The three unchanged south points stand on or above their floors as authored, and the three measured north
points stand 0.043 to 0.053 m above theirs. The seven north points without a ray carry no floor number; the formation
accepted all ten north points natively (10/10). The height at which the NavMesh sample put the body on each point
(`floors-run`, table below) is within 0.063 m of the catalogue height for all 20 points (largest: `south-05` 7.431
against 7.368, `south-10` 6.944 against 6.8833 = 0.061), and `Connected` allows 0.3 m. The comment above
`ArenaTeamBoxCatalog.Yard` says 0.07 m. No red proof was recorded for the seven heights; `BlockTeamSpawnTests` now pins
them in its literal table, and the native refusal is the observation that they matter. The xunit suite and the
harnesses cannot see this defect, because `FloorAndClear` needs the game's physics.

**Donor, destination, anchors, exits.** Donor and outside destination: the installed `arena_yard-bot-core-04` row at
(-12.88, 6.17, 87.03), exact floats, `BotZone_Main`, categories exactly Bot/BotPmc, core 0, delay 4, collider
`SpawnSphereParams` radius 25, infiltration "", Sides [Savage]. It is one of Arena's own AI core points (`yard.bots.json`:
14 of the 18 Bot rows are native points; the other four are `fill` rows without an Arena source) and has 5.23 m of
clearance to the nearest mesh edge, where MapPort's other proposal for a donor, `arena_yard-bot-fill-04`, has 2.01 m
(`data.md`, from `yard-release.md` lines 556-562). It lies between the pockets, inside neither box. MapPort froze nothing
yet ("rows freeze at publication"), and ArenaMode pins the row by id and exact floats, so a changed row fails
`BlockTeamSpawnTests` and `BlockTeamBoxChecks`. Anchors: USEC = `south-05` (index 4, `Usec house (9)`, MapPort
`player-usec`), BEAR = `north-03` (index 12, `Bear house (7)`, MapPort `player-bear`); MapPort's two "All" rows are
`south-06` (`player-all`) and `north-02` (`player-north-all`). The runtime anchor check needs `FloorAndClear`, a NavMesh
sample within 0.4 m, at most 0.3 m horizontally and at most 0.3 m vertically from the anchor (`Connected` and the
anchor check in `ArenaTeamBoxRuntime.cs`).

Exits (data only; `ArenaExtractionIsolation` matches by object and exit name): `Formation.BoothExits` lists "Block
South Gate" and "Block North Gate", USEC first. Both stand inside the pockets (`yard.exits.json`): the south gate at
(-20.75, 7.48, 11.25), box x -22.75 .. -18.75, z 9.25 .. 13.25; the north gate at (-3.25, 6.15, 150.5), box x -5.25 ..
-1.25, z 148.5 .. 152.5; both 8 s, chance 100. Points `south-03` and `south-04` lie inside the south box and `north-05`
and `north-08` inside the north box (MapPort's marker indices `(7)`, `(8)`, `(9)`, `(12)`). In an ordinary PMC raid both
exits were seen as MapPort's authored exits and the player extracted from the south one (native report); whether
standing on a formation point inside an exit box ever extracts in an Arena raid was not run.

**Gates (15 s watchdog, no minimum).** MapPort's `opened_barriers` and `start_gates` are the two gates; nothing stays
closed after a release. The gondola is authored at 8.0 s (ease OutBack); the north leaves at 1.7 s each. In a plain raid
MapPort parks both gates open at raid start (its log). `MinimumGateOpeningSeconds` is 0 because every MapPort build that
ships the map exposes `GatesOpen`. `GateOpeningTimeoutSeconds` is **15 s**, the same as Chop Shop (20 s for Bowl, 10 s
for the rest). The reason is the one Chop Shop's contract gives: the match flow polls the opening once a second, a drive
of 8.0 s at scale 1.0 would leave two polls under the ordinary ten seconds, and a missed poll ends the protected match;
fifteen seconds leave seven. The completion contract still requires `GatesOpen` and all twenty complete outside paths
with floor and capsule checks before combat. `SpawnGateChecks` holds a Block opening (no abort at 14.99 s, fail closed
at 15 s) and passed in the first offline pass. Natively, ArenaMode's own hold and release ran on Block at both drive
scales: MapPort drove the three gate parts open in 4.0 s at scale 0.50 and ArenaMode's revalidation followed 5.01 s
after the release; at scale 1.0 the drive took 8.0 s and the revalidation came 8.03 s after the release. Both are under
the ordinary ten seconds, so the run does not show the 15 s to be needed; it shows that 15 s is not too short. The bot
spawn shield and the 20-participant cap follow from `ArenaTeamBoxCatalog.Applies`.

### The first Team DM start (build `0.6.0+c071cb81`)

The first Team DM 4v4 start on Block was refused. The log (GPU-2, 2026-11-21, 13:42:46 local; the first line was read in the live log, the exported evidence
`native/tdm-4v4/report.md` holds the two error lines):
`Block team-box capacity: USEC 0/10 safe connected places, 4 required.`, then `[Error] Team DM box initialization
failed: Block USEC box has 0/4 safe connected places.` and `[Error] Arena start is invalid; protected exit queued after
the startup frame`. The protected start aborted and returned to the menu, which is the designed fail-closed behaviour;
gpu-run's raid step waited 460 s and reported that the raid did not start. The cause was the seven heights above. The
fix touches `ArenaTeamBoxCatalog.cs` and `BlockTeamSpawnTests.cs` only. Build `0.6.0+fb2b3af6` ran the same scenario to
a pass.

## Domination

`DominationMapLayout.Block` (private) has **two** points, like AirPit and Bowl: A (-29.3245, 6.1262, 71.6287) =
`CAPTURE_POINT/YARD_CAPTURE_POINT_LIGHT_01` (east) and B (-62.5245, 6.1262, 72.1887) =
`CAPTURE_POINT/YARD_CAPTURE_POINT_LIGHT_02` (west) of `yard.modes.json`. X/Z and the height are the light roots' own,
both y 6.1262; the survey read the built-mesh yard floor there at 6.15. The constant `BlockControlPointY` is the one
place to change if a later map build refuses a point; X/Z are not moved, no relocation is added and the shared
projection (`SamplePosition` within 0.75 m, |dy| <= 0.5, support ray, capsule clearance) is not loosened, so a refused
point keeps aborting the protected match. Natively the points were accepted and projected to A=(-29.325, 6.168, 71.629)
and B=(-62.524, 6.134, 72.189) (`Domination points verified`), 0.042 m and 0.008 m from the catalogue height by
subtraction; `BlockControlPointY` stays. Menu text: "Hold 2 points" (`DominationMapLayout.ControlPointCount` gives 2).
Block is in `ArenaAreaCatalog`'s explicit whole-map Domination list, now its last link.

**Why two points and not the centroid.** The two lights are 33.2 m apart along x at z about 72 (`yard.modes.json`), and
each has its own pair of ring decals on the floor (7.9 m and 5.6 m wide, within 0.7 m of the root; the width figures
are from the comment above the layout row, the survey files do not list them). The sidecar's `capture_point` (-45.9245,
6.1262, 71.9087) is the centroid of the two lights, 16.6 m from either ring; a point there would stand between the
rings. The scene's `Capture_Zone` object (-10.461, 6.1226, 83.9136) is a root with 13 `DecalGraffiti` children and no
collider; it is the decal parent, not an objective. The pickups use the Bot rows only: by subtraction from the sidecar
rows no Bot row lies within 6 m of A or B (the nearest is `fill-04` at 8.7 m from A), so the 6 m clearance around the
points removes no row. Natively Domination logged `placed=6/6 ... refusals=[another pickup is within 3m=3]`. Domination shares the team boxes, the gate completion, the physical roster and the spawn-shield ownership of the
team-box formation. MapPort ships no DynamicMaps overview for the map (its plan lists Yard in `NO_OVERVIEW`); natively
`Arena DynamicMaps: Domination objective overlay ready` was logged and nothing is drawn.

## Decisions

Every decision below is the orchestrator's. **The owner has not confirmed any of them.**

1. **Names.** Location id `arena_yard`; player-facing name Block; C# members `YardId`, `IsYard`,
   `AutoServiceDefaultClientAllowlist`, `ArenaTeamBoxCatalog.Yard` (DisplayName "Block"), `DominationMapLayout.Block`,
   `BlockControlPointY`. Tests and harnesses carry the player-facing name, as Chop Shop's do (`BlockMapTests`,
   `BlockTeamSpawnTests`, `tools/BlockChecks`, `tools/BlockTeamBoxChecks`, labels `BLOCK-...`). Argument: the Customs
   yard area already owns "Yard" in this code base (`CustomsYardId`, `AddOrValidateYardStarts`), and a test class
   called `YardMapTests` would read as Customs.
2. **Detection by the live location, not by version.** Argument: the build that carries the map and the published 0.6.0
   without it share the version string `0.6.0`; the rule is the one of Iceberg and Chop Shop.
3. **Optional-map revision 7** with the exact-default rules in "Migration"; an Iceberg default at revision 6 gains
   neither Chop Shop nor Block, and a Chop Shop default at revision 7 without Block stays. Argument: each earlier
   admission treated an exact default lacking the newest map as a deliberate removal.
4. **Every mode admitted, no refusal and no picker hint, solo and with a PitFireTeam squad.** Argument: 18 Bot/BotPmc
   rows on a main walkable component of 4,548.2 m2 (survey read of the built mesh; Chop Shop 1,915.6, Iceberg 4,788,
   Bowl 4,987 from Chop Shop's contract, methods not confirmed identical); MapPort's own coverage check on the built
   mesh (`spawn_coverage.py --check`, 4,416 cells outside the pockets, 0 violations) found at least 11 Bot rows beyond
   25 m on every cell (median 15) and 100 percent of cells with at least 5; MapPort's offline model on Arena's own
   mesh (first layout) gave at least 17 from each Player row. Both
   are MapPort's tools, not ArenaMode's selector. ArenaMode's own selector was run offline (`BLOCK-GRID`,
   `BurstSpawnPointSelector.CountUsable` at least 5: "Grid minimum 9 near (-11.0, 62.5)", `MapModeAdmissionChecks.log`)
   and natively for solo Tactical and Zombies (see "Values the native run settled"). PitFireTeam on this map did not run;
   if a later `BLOCK-GRID` fails, the PitFireTeam decision in `ArenaAreaCatalog` is to be re-taken, not the number
   re-pinned.
5. **Both PitFireTeam adapter chains list `arena_yard`** (`PitFireTeamPortedMapSpawns`, `PitFireTeamPortedMapCombat`),
   because both PFT modes are admitted.
6. **Default eight participants.** Argument: 18 Bot rows leave eleven spare after seven bots, on a component of
   Iceberg's and Bowl's order (comment above `DefaultTotal`). A menu default, not a performance guarantee. Natively
   Deathmatch ran with eight.
7. **Server defaults** `BotZone_Main`, `BotMax 8`, `MaxBotPerZone 8`, `BurstMinAliveBots 4`, as every port; the default
   never installs a map.
8. **Gate timing: minimum 0, watchdog 15 s**, as Chop Shop (argument and native result under "Gates").
9. **Time panel: server time, no relabel.** Argument: MapPort's descriptor has `UsesServerTime` true for this map
   (`ArenaMapDescriptor.cs` line 1683), like the other released maps.
10. **Lighting and pickups:** the id is added to `ArenaLightingPolicy.SupportsMap` and `ArenaPickupMapLayout.Supports`.
11. **Team-box interiors: authored bounds inset 0.35 m on every horizontal face, no gate-face clip, the south band
    authored, the north top cut at 8.6** (arguments under "Interiors" and "Heights").
12. **Seven substituted heights:** `south-01`, `-02`, `-04`, `-05`, `-06`, `-07` and `-08` carry the measured floors
    (7.368 and 7.483) instead of the authored 7.2673, 7.2653 and 7.2693, with no change to `FloorAndClear` (argument
    and measurement under "Seven substituted heights"). This replaces the first version of the decision, which
    substituted `south-04` only.
13. **Donor and outside destination `arena_yard-bot-core-04`** (argument under "Donor, destination, anchors, exits").
14. **Anchors `south-05` (index 4) and `north-03` (index 12)**, MapPort's `player-usec` and `player-bear`.
15. **Domination with two points A and B at the light roots' own coordinates; the centroid and `Capture_Zone` are not
    used** (argument under "Domination").
16. **Gold label BLOCK at MapPort's native marker position (691/857), no `_reposition`.** Argument: MapPort already moved
    the marker off THE LAB and host-checked the toggle `ARENA: BLOCK` against it (`markers.json` lines 180-186).
17. **Installed Block data is a harness dependency.** `tools/BlockChecks`, `tools/BlockTeamBoxChecks`,
    `ArenaPickupChecks` and through it every full run read `db/arena_yard/base.json` and fail loudly without it, naming
    the requirement: the two Block harnesses with "Installed Block data is missing: <path>. Install an ArenaMapPort
    build that ships arena_yard.", `ArenaPickupChecks` with a message of its own that names itself. No
    silent skip. `SoloTacticalSpawnChecks` lists `arena_yard` among its optional ports and does not require it, so it
    stays green on an ArenaMapPort build that predates the map. `BlockChecks` is registered in
    `tools/verification-steps.txt`, and since the review `BlockTeamBoxChecks` is too, as `IcebergTeamBoxChecks` is
    (`ChopShopTeamBoxChecks` is not; each of the three runs the whole xunit suite itself). The first pass, before
    that, still printed "UNREGISTERED tools/BlockTeamBoxChecks changed and is not in tools/verification-steps.txt".
18. **Announcement.** `README.md`, `docs/FORGE-DESCRIPTION.md` and `assets/arena-whitelist.json` stay untouched (see the
    introduction).
19. **Monitors and crowd are MapPort-side and need no change.** ArenaMode binds crowd reactions by `ReactionKinds`, not
    by map id, and `ArenaExtractionIsolation` matches exits by object and exit name (the survey's touch-point list,
    `touch.md`). MapPort ships 8 monitors and 27 crowd fans for Block (`yard.monitors.json`; `data.md`). Not in
    `DESIGN.md`; recorded here because no production file changed for it.

## Native pre-survey

What was measured on GPU-2 (UiBridge host `host2`) on 2026-11-21 **before any ArenaMode build that knows Block was
installed** (installed ArenaMode `0.6.0+552abd3a`, no Block entry). The runs are ordinary PMC offline raids on
`arena_yard` through EFT's own raid screens (tile "ARENA: BLOCK", bots none, bosses unchanged); ArenaMode took no part.
Plugin 0.10.1 (`0.10.1+8f58ec77`). Result files: `artifacts/yard-20261121/native/survey/{run1,floors-run,rays-run}/`
(git-ignored; this section and the native report are the summary).

| Run | Time (UTC) | What it did | Log since raid start |
|---|---|---|---|
| `run1` (`block-survey`) | 11:54:08.17 to 11:54:08.92 | read-only dry runs from the player's start (-14.9, 7.3, 13.3), which is `south-05`, and from `north-03`; nothing moved | not read |
| `floors-run` (`block-floors`) | 11:58:33 to 12:00:04 | stood the body on each of the 20 markers, the donor and both capture points and read `Transform.position` after 1 s | warnings 0, errors 0; location loaded 4.7 s, game started 26.0 s |
| `rays-run` (`block-floor-rays`) | 12:00:47 to 12:02:01 | stood the body on the 10 south and three north points and cast one ray down from the camera (layer mask `player`, triggers ignored) | warnings 0, errors 0; location loaded 4.7 s, game started 25.7 s |

**`run1`: the twenty markers judged against the NavMesh.** Positions are the tool's one-decimal rounding.

| South point | Snapped to (y) | Distance to NavMesh | Path from `south-05` |
|---|---|---|---|
| south-01 | 7.4 | 0.1 | complete 8.5 m, 2 corners |
| south-02 | 7.4 | 0.1 | complete 3.8 m, 2 corners |
| south-03 | 7.6 | 0.1 | complete 8.8 m, 4 corners |
| south-04 (authored 7.2693) | 7.5 | 0.3 | complete 6.8 m, 5 corners |
| south-05 | 7.5 | 0.2 | complete 0.0 m, 2 corners |
| south-06 | 7.4 | 0.1 | complete 4.1 m, 2 corners |
| south-07 | 7.4 | 0.1 | complete 11.2 m, 4 corners |
| south-08 | 7.4 | 0.1 | complete 9.0 m, 2 corners |
| south-09 | 6.9 | 0.0 | complete 8.1 m, 2 corners |
| south-10 | 6.9 | 0.1 | complete 5.1 m, 2 corners |

All ten south points and all ten north points were accepted (checks navmesh, zone, reachable, room), none rejected. The
north points all snapped to y 6.2 at distance 0 and have complete paths from `south-05` of 139.4 (`north-01`), 140.9,
139.5, 142.8, 144.9, 143.9, 147.8, 148.0, 145.2 and 137.2 m (`north-10`), 15 to 20 corners. The run used the authored
height of `south-04`; the 0.3 m is the distance for that authored height, not for the catalogue height 7.483. The 0.2 m
for `south-05` rounds a snapped y of 7.5 against 7.2673, so the true distance is at least 0.18 m; the files carry no
more decimals. (An earlier comment in the catalogue said the NavMesh lay within 0.17 m of every catalogue height; that
figure is not reproduced by these files and was replaced in `fb2b3af6`.)

| Target (from the south start / from `north-03`) | Accepted | Path from `south-05` | Path from `north-03` |
|---|---|---|---|
| donor `arena_yard-bot-core-04` (-12.88, 6.17, 87.03) | yes | complete 83.0 m, 15 corners | complete 61.7 m, 7 corners |
| point A (-29.3245, 6.1262, 71.6287) | yes | complete 65.9 m, 14 corners | complete 81.1 m, 12 corners |
| point B (-62.5245, 6.1262, 72.1887) | yes | complete 84.0 m, 16 corners | complete 103.2 m, 15 corners |
| `fill-04` (-35.5, 6.15, 65.5) | yes | complete 61.1 m, 9 corners | not run |
| `core-03` (-5.08, 6.15, 57.92) | yes | complete 50.4 m, 8 corners | not run |
| `south-05` (from `north-03`) | yes | - | complete 138.9 m, 13 corners |

Every target except `south-05` snapped to y 6.2 at distance 0 (the two points: requested y 6.1262 shown as 6.1,
snapped 6.2); `south-05` snapped to y 7.5 (requested 7.3, 0.2 m). Each path
was swept with a line at 1.3 m above each corner against static walls and fences: "no wall found" on every stretch (14
of 14, 13 of 13, 15 of 15, 8 of 8, 7 of 7 from the south; 6, 11, 14 and 12 from the north). The sweep does not see
terrain, doors, interactive objects or triggers.

**`floors-run`: where the body settled.** The body was teleported to the authored coordinates (all 23 teleports
accepted, each path complete) and `Transform.position` read after 1 s. The column "authored y" is the y of the
teleport.

| Point | Authored y | Body y after 1 s | Point | Authored y | Body y after 1 s |
|---|---|---|---|---|---|
| south-01 | 7.2673 | 7.400 | north-01 | 6.1873 | 6.150 |
| south-02 | 7.2653 | 7.400 | north-02 | 6.1923 | 6.150 |
| south-03 | 7.5003 | 7.539 | north-03 | 6.1773 | 6.150 |
| south-04 | 7.2693 | 7.533 | north-04 | 6.1773 | 6.173 |
| south-05 | 7.2673 | 7.431 | north-05 | 6.1773 | 6.150 |
| south-06 | 7.2673 | 7.400 | north-06 | 6.1773 | 6.150 |
| south-07 | 7.2673 | 7.400 | north-07 | 6.1773 | 6.150 |
| south-08 | 7.2673 | 7.400 | north-08 | 6.1773 | 6.150 |
| south-09 | 6.8833 | 6.904 | north-09 | 6.1773 | 6.150 |
| south-10 | 6.8833 | 6.944 | north-10 | 6.1773 | 6.150 |
| donor (-12.88, 87.03) | 6.17 | 6.167 | point A / B | 6.1262 | 6.150 / 6.150 |

The body stands 0.016 to 0.063 m above the ray's first hit where both exist (for example `south-04` 7.533 against 7.483,
`south-05` 7.431 against 7.368, `north-01` 6.150 against 6.134). The reports do not say why; the catalogue uses the ray's
hit, which is what `FloorAndClear` tests, and not the body's y. The path lengths of the `floors-run` teleports are from
the previous point; one crosses the map: `south-10` to `north-01` is a complete path of 136.2 m, 15 corners (with both
gates open in a plain raid). `rays-run` found 136.3 m, 12 corners from `south-10` to `north-03`. Neither run read the
gate state.

**`rays-run`: the collision floors** are in the table under "Seven substituted heights". Each south point has two hits
on the ray: the first (7.368, 7.483 or 6.883) is the building's floor piece, the second (6.134) the courtyard mesh
`Yard_courtyard_01` below it. The three north points have the courtyard only.

What these runs show: the 23 points are on the NavMesh with complete paths, the 13 measured floors are as listed, and
the donor row and both capture points exist at the stated coordinates on walkable ground. What they did not show, and
what the first ArenaMode start then did: whether the runtime accepts the points (the clearance capsule of
`FloorAndClear` was not measured), the formation capacity, anything under a held gate, and any ArenaMode behaviour.

## Values the native run settled

The values that were provisional before the native cycle, as the run on GPU-2 showed them ([native
report](../reports/2026-11/GPU-BLOCK-20261121.md); builds `0.6.0+c071cb81` and `0.6.0+fb2b3af6`):

- **The platform start heights: not held as written.** Build 1 was refused with USEC 0/10; build 2 with the seven
  measured floors reached `USEC 10/10 safe connected places` and `BEAR 10/10`, with 4 required (4v4) and with 10
  required (10v10). The three unchanged south points and all ten north points were accepted as authored.
- **The team-box interiors (inset faces, no gate clip, north cut at 8.6):** held. `Block Team DM: restored 20 box
  markers (USEC south / BEAR north)`, capacity 10/10 on both sides, formation ready at 4v4 and 10v10, all twenty
  formation paths revalidated after the release. The north corridor was not walked during a hold.
- **Donor and outside destination `arena_yard-bot-core-04`, anchors `south-05` and `north-03`:** held. The formation
  passed the anchor check on both sides; from the north room the routes were A 90.2 m, B 112.2 m and the donor 70.0 m,
  all complete (4v4 run).
- **Gate timing:** held, with the limits stated under "Gates": opening confirmed 5.01 s (drive scale 0.50) and 8.03 s
  (scale 1.0) after the release; MapPort's drive 479 frames (mean 8.4 ms, max 17.7 ms) and 946 frames (mean 8.5 ms,
  max 18.4 ms). `navmesh probe: every probed gate sealed (3 probed)` and `25 carver(s)` installed and removed.
- **Domination points A and B and their height:** held. Both projected under the protected match
  (`Domination points verified: A=(-29.325, 6.168, 71.629); B=(-62.524, 6.134, 72.189)`), the objective routes from both
  opened boxes were verified, the ownership went `neutral/neutral`, `USEC/neutral` (29 s after the gates), `USEC/BEAR`.
  `BlockControlPointY` is unchanged. Pickups `placed=6/6`.
- **Solo Tactical (`BLOCK-GRID` argument):** held for the rounds that ran. Rounds 1 to 3 each went live with 5 active
  opponents; the burst plans selected 5 from the player's position with `points 5 preferred + 13 distant`, `11 preferred
  + 6 distant` and `5 preferred + 13 distant` (north "All" row, south Usec row, north Bear row); no `no usable spawn
  point`, no `no partial squad dispatched`.
- **Solo Zombies:** 32 of 32 infected alive after 35 s from the south row and again 20 s after a move to the north
  row; `points 11 preferred + 6 distant` from the south row; no `no usable spawn point`.
- **Deathmatch with eight:** held after one empty start: the first raid after a server start had 0 bots after 30 s
  (`Arena solo population: total=8 ... pending=7`; the server was building its first profiles, "150 bot profiles
  built") and the scenario's three-bot check failed; the second raid had its first bot 9 s after the match start and
  `alive=6 ... pending=1` after 30 s. The native facts attribute the empty first start to the server's profile build.
  After the review the scenario was changed to wait for the first bot arrival instead of a fixed 30 s, and it passed
  in that form on build 2 as the first raid after a server start (`native/dm-b2`, 14 steps, 6 expectations): first
  arrival 38 s after the match start, all seven bots within 3.1 s of it, `alive=5 ... pending=2` after 60 s, pickups
  `placed=6/6`, 0 errors, no ArenaMode or ArenaMapPort warning.
- **Label and tile:** the gold **BLOCK** label at MapPort's anchor, toggle box [807,102 129x32] directly above THE LAB,
  clear of the Arena panel (ends x 416) and of the right-hand panels (start x 1264); the tile was offered under all five
  modes that ran.
- **Respawn:** a test-hook death in Team DM respawned the local player on a formation point
  (`Block team player respawn: arena-yard-tdm-v1-north-07, (-8.39, 6.18, 153.51)` in the 4v4 run,
  `arena-yard-tdm-v1-north-08, (-2.05, 6.18, 152.17)` in the 10v10 run) and the player read back there.
- **Gate sounds:** `not played (the listener is beyond 30 m / 20 m ...)` for all three rows from the north room; nothing
  was heard.

## Verification

Offline; every dotnet call used `tools/AirPitChecks/NoDeploy.targets` and an absent `SPTUserMods` sentinel, and the
sentinel `must-not-deploy-xunit` was absent after every xunit run (source: the xunit editor's report).

**Final offline pass: 109 of 109 steps green over `96a51cea`,** the last commit that changes compiled code, tests or
harnesses. The commit after it holds documents and two code comments ("After the final pass" below). One step
needed a second run ("Final offline pass" below). The first pass further down ran before `fb2b3af6` and before the native
cycle and is kept as the first pass.

### The xunit suite and the red proofs

- xunit with only the production change and the old tests: 21 failed, 4,401 passed, 3 skipped, which shows that the old
  tests pinned revision 6 and six ports. After the test edits: one unfiltered run, **4,565 tests, 4,562 passed, 0
  failed, 3 skipped**. The three skips are the known environment skips: two reward-recovery baseline cases
  (`SPT_REWARD_RECOVERY_BASELINE`) and the APBS test root (`ARENA_APBS_TEST_ROOT`). The 98 Block tests (`BlockMapTests`
  41, `BlockTeamSpawnTests` 57) and about 758 related filtered tests also passed in their own
  runs. The first pass's `ArenaMode.Tests (xunit, once)` step reports `total=4565 passed=4562 skipped=3`.
- New test classes: `BlockMapTests` (the revision-7 theory with 26 rows, idempotence and user-owned entries, a real
  `ConfigStore.Reload`, optional-map identity, every mode admitted solo and with a squad, whole-map Domination,
  `Applies` and `MaximumFor` 20, the default of eight, `ServerDefaultMapSetEqualsTheClientDefault`, the discovery order
  with Chop Shop at `calls[^2]` and Block last) and `BlockTeamSpawnTests` (the twenty points from their own literal
  table, ids, prefix, infiltration, team description, zone, both anchors, the donor id and floats, `Destination`
  outside both boxes, `BoothExits`, gate timing 0 and 15, `Find`/`Owns`/`Applies` scope, the interior probes named
  above, the margins, the protected clone, the fault theories including `foreign-chopshop`, other modes keeping native
  markers). Ten neighbour files moved their old rules to the new boundary (revision 7, seven ports, Chop Shop no longer
  last) without weakening them. `fb2b3af6` changed seven literals and one probe in `BlockTeamSpawnTests`; the
  final pass ran the suite after it (4,565 tests, 4,562 passed, 3 skipped).
- **Six red proofs.** Each mutation was made on the production file at the state of `30892f9a`, run with a filter, then
  restored byte-identically (SHA-256 equal before and after, time stamp refreshed; copies in
  `artifacts/yard-20261121/red/`). They were not repeated after `fb2b3af6`.

| # | Mutation | Failing test |
|---|---|---|
| A | `ArenaTeamBoxCatalog.cs`: north box top `8.6f` to `10.9159f` | `BlockTeamSpawnTests.WorldAlignedInteriorRejectsCornersJustOutside(usec: False)`, `Assert.False` got True (1 failure) |
| B | bear anchor `4, 12` to `4, 11` | `BlockTeamSpawnTests.CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws`, expected `north-03`, got `north-02` |
| C | watchdog clause names `"arena_yardx"` (Block falls to 10 s) | the same catalogue test, expected 15, actual 10 |
| D | `ConfigStore.cs`: `revision < 7` to `revision < 6` | `BlockMapTests`, 2 failures, among them `RealReloadPersistsBlockAndHonorsItsRemovalOnNextReload` (Block not enabled); the second test's name was cut off and not re-read |
| E | donor z `87.03f` to `87.031f` | 24 `BlockTeamSpawnTests` failures, among them the catalogue test (87.0299988 against 87.0309982), the clone theory and the Domination test |
| F | `ArenaAreaCatalog.cs`: `YardId` in the Domination chain replaced by `"none"` | `BlockMapTests.BlockAdmitsWholeMapDominationWithoutPitFireTeamOrAreas`, `Assert.True` got False |

Mutation A fails as soon as the top is raised; whether it was the y 9.2 probe or the `maxY + .01f` probe that fired was
not confirmed. All twelve test files and all harness files touched are CRLF with no bare LF (byte counts by the
editors).

### Final offline pass (over `96a51cea`)

`tools/Run-ArenaVerification.ps1 -ChangedSince master` from the worktree, six worker worktrees, the live installation
hashed before and after every step. It printed `ARENA_VERIFICATION_CHANGED 80 files since master`,
`ARENA_VERIFICATION_SELECTION selected=108 of 175 registered` and
`ARENA_VERIFICATION_SUMMARY passed=108 failed=1 seconds=10292 cached=0`.

- **The one red step was `IcebergTeamBoxChecks`,** which runs the whole xunit suite itself: 4,565 tests, 1 failed,
  4,561 passed, 3 skipped. The failed test is
  `OfflineSessionHelperTests.ANoteThatDoesNotNameTheStartedServerNeverExcusesAnUnsettledSession(kind: "other-pid")`
  (`Expected: 1, Actual: 0`), a third case of the load-dependent class that made two wrappers red in the first pass.
  The same test passed in the same pass wherever else the whole suite ran: `ArenaMode.Tests (xunit, once)`,
  `BlockTeamBoxChecks`, `EquatorMapChecks` and `tools/PftTacticalKitChecks/Verify-Integration.ps1`.
- **The runner was started again at the same head.** The 108 green steps came from its result cache and
  `IcebergTeamBoxChecks` ran alone: `ICEBERG_TEAM_BOX_CHECKS_PASSED`,
  `ARENA_VERIFICATION_SUMMARY passed=109 failed=0 seconds=356 cached=108`.
- The four steps that were red in the first pass are green: `EquatorMapChecks`,
  `tools/PftTacticalKitChecks/Verify-Integration.ps1`, `ColaReleaseChecks` (the fixture folder
  `artifacts/optional-mod-releases` was copied into the worktree) and `AmandsGraphicsSceneChecks`
  (`Source faults rejected: 22`).
- xunit: `ArenaMode.Tests (xunit, once)` reports `total=4565 passed=4562 skipped=3`. It is the first recorded run of
  the suite after `fb2b3af6`.
- Block's harnesses: `BLOCK_INTEGRATION_CHECKS_PASSED`; `BLOCK_TEAM_BOX_CHECKS_PASSED` with
  `BLOCK_INSTALLED_DONOR arena_yard-bot-core-04 (-12.88, 6.17, 87.03); south=2 north=2 bots=18` and
  `BLOCK_UNIT total=4565 passed=4562 skipped=3`; `MAP_MODE_ADMISSION_CHECKS_PASSED (1088 assertions)`, 23 more than
  in the first pass (the pin `BLOCK-POCKETS`: twenty start points and three corridor points), with
  `Grid minimum 9 near (-11.0, 62.5)` and `MAP_MODE_ADMISSION_FAULTS_PASSED (18 compiled faults)`;
  `EQUATOR_SPAWN_GATE_CHECKS_PASSED (449 assertions)`; `DOMINATION_RUNTIME_CHECKS_PASSED (1127 assertions)` with
  `Source faults rejected: 38`; `DOMINATION_ADMISSION_VERIFIED: 20 compiled mutants, 8 wiring controls, installed
  hashes unchanged`.
- The runner printed `UNREGISTERED tools/AirPitChecks changed and is not in tools/verification-steps.txt`, as in the
  first pass. That folder has no step of its own; its `NegativeControls.ps1`, which this branch extended by Block's
  fault pair, runs through the map harnesses (`BlockChecks` rejected `YardMissingPftSpawnAdapter` and
  `YardMissingPftCombatAdapter`).
- Sources: `artifacts/yard-20261121/verify-2.clean.log` (the full run) and `verify-3.clean.log` (the second start).

### After the final pass: the document fact check

Four readers then checked the documents of this change against the code, the logs and the native evidence, and a second
reader tried to refute each finding: 25 findings, all confirmed (one in part), none about the behaviour of the code. All
are corrected in the last commit:

- numbers and attributions in this contract (the body-above-ray range, the snapped height of `south-05`, the source of
  the "17 from each Player row", the three skips, 98 Block tests, which Player rows Zombies and Tactical used);
- the claim "0 warnings": the exports hold no error line and no ArenaMode or ArenaMapPort warning, but Unity's own
  warnings appeared in three raids (native report, "Warnings");
- the profile comparison (which copy was compared with which; the two `StackObjectsCount` entries are back after the
  last Deathmatch raid and older than this branch);
- a wrong sentence about GPU-1 (it holds the same ArenaMapPort files and the same build; nothing was run there);
- stale sentences about the gpu-run commit and about which scenarios ran on the first build, the review count in the
  hand-off (eight applied, not seven; the message of `96a51cea` has the same slip), and two stale "six ArenaMapPort
  maps" in `AREA-RULES.md` and `DEPENDENCIES.md`.

Two code comments were corrected with them, without changing the number of lines: `DominationMapLayout.cs` says "the
seven ports", and `ArenaAreaCatalog.cs` names MapPort's offline model as the source of the 17. After these edits the
client and the server were built (0 errors) and three harnesses that read those files were run again by hand:
`DOMINATION_RUNTIME_CHECKS_PASSED (1127 assertions)` with `Source faults rejected: 38`,
`MAP_MODE_ADMISSION_CHECKS_PASSED (1088 assertions)` with `MAP_MODE_ADMISSION_FAULTS_PASSED (18 compiled faults)`, and
`DOMINATION_ADMISSION_VERIFIED: 20 compiled mutants, 8 wiring controls, installed hashes unchanged`. The full runner
was not started again after the comment edits.

### First offline pass of the harnesses (before `fb2b3af6`)

The runner (`tools/Run-ArenaVerification.ps1`, six worker worktrees) printed `ARENA_VERIFICATION_CHANGED 69 files since
master`, `ARENA_VERIFICATION_SELECTION selected=107 of 174 registered` and, at the end,
`ARENA_VERIFICATION_SUMMARY passed=105 failed=4 seconds=9632 cached=0`: **105 of 109 steps passed.** (The summary counts
109 steps against 107 selected; the log does not say which two steps the runner added, one is the line `ArenaMode.Tests
(xunit, once)`.) It also printed `UNREGISTERED tools/AirPitChecks changed and is not in tools/verification-steps.txt`
and the same for `tools/BlockTeamBoxChecks` (decision 17). Source: `artifacts/yard-20261121/verify-1.clean.log`; commit
`c071cb81` names the causes.

**The four red steps and their causes.** The step logs of the first pass were overwritten by the later runner starts.
The test names and messages in this table were read from them before that and cannot be re-read; `verify-1.clean.log`
keeps one shortened line per step.

| Step | Cause |
|---|---|
| `EquatorMapChecks` | The wrapper runs the whole xunit suite: 4,565 tests, 1 failed, 4,561 passed, 3 skipped. The failed test is `OfflineSessionHelperTests.NormalReturnUsesOriginalArgumentsAndRequiresCommittedImportAfterListening` (`Expected: 1, Actual: 0`). `c071cb81` calls it a known load-dependent case; the same test passed in the `ArenaMode.Tests (xunit, once)` step. |
| `tools/PftTacticalKitChecks/Verify-Integration.ps1` | The same kind of wrapper: 4,565 tests, 1 failed, 4,561 passed, 3 skipped. The failed test is `OfflineSessionHelperTests.ALeaveWhoseNormalServerPausedItsRecoveryOpensTheGameOnceAndShowsOneDialog`, another case named load-dependent in `c071cb81`. |
| `ColaReleaseChecks` | A missing fixture folder in the worktree: the log says `FAIL ColaReleaseChecks: reviewed release DLL is available: TarkovaCola.Client.dll`. Not caused by Block. |
| `AmandsGraphicsSceneChecks` | The real one: the check pinned eleven catalogue ids, `arena_yard` makes twelve. Fixed in `c071cb81` (the pin and the check text now say twelve); the step passed in the final pass. |

**The harnesses that hold the map, with their PASSED lines from the first pass** (logs under
`artifacts/run-arena-verification/`):

| Harness | What it holds for Block | First-pass result |
|---|---|---|
| `BlockChecks` (new, registered) | installed data: id and enabled, 4 Player rows (infiltration `ArenaYard`), at least 18 Bot/BotPmc rows in `BotZone_Main`, the donor once; the filtered xunit classes; the console programs; `ArenaPickupChecks`; the PFT negative controls | `BLOCK_INSTALLED_SPAWNS player=4 bot=18`; filtered xunit `Fehler: 0, erfolgreich: 428, gesamt: 428`; `ARENA_MENU_CHECKS_PASSED (1177 assertions)`; `AREA CONFIG PRODUCTION CHECKS PASSED (427 assertions; real BepInEx bind/save/reload)`; `AREA MAP PREVIEW PRODUCTION CHECKS PASSED (4534 assertions)`; `PARTICIPANT_SOURCE_CHECKS_PASSED (7654 assertions)`; `PFT_PORTED_MAP_CHECKS_PASSED: 366 assertions`; `PORTED_COVER_CHECKS_PASSED: 285 assertions`; `ARENA_LIGHTING_CHECKS_PASSED (914 assertions)`; `ARENAEXTRACTIONCHECKS_PASSED (186 assertions)`; `ARENA_PICKUP_BEHAVIOR_PASSED: 547 assertions` (`ALL_MAP_NATIVE_CANDIDATES: arena_yard: 14`); 10 compiled PFT adapter faults rejected, among them both `Yard` ones; `BLOCK_INTEGRATION_CHECKS_PASSED` (133 s) |
| `BlockTeamBoxChecks` (new) | installed snapshot guard; the donor float-exact with its header; no installed `arena-yard-tdm-v1-*` row; the two literal boxes; every Player row inside exactly one box (two per box, one "All" row in each); no Bot row inside a box; the full xunit suite once; the follow-up harnesses and Release builds | `BLOCK_INSTALLED_DONOR arena_yard-bot-core-04 (-12.88, 6.17, 87.03); south=2 north=2 bots=18`; `BLOCK_UNIT total=4565 passed=4562 skipped=3`; `EQUATOR_TEAM_SPAWN_CHECKS_PASSED (2772 assertions)`; `EQUATOR_SPAWN_GATE_CHECKS_PASSED (449 assertions)`; `ARENA_TDM_RUNTIME_CHECKS_PASSED`; `MAP_MODE_ADMISSION_CHECKS_PASSED (1065 assertions)`; three Release builds with `0 Fehler`; `BLOCK_TEAM_BOX_CHECKS_PASSED` (430 s) |
| `MapModeAdmissionChecks` | `minimumRows` 18, `BLOCK-GRID`, `BLOCK-PLAYER-ROWS` (exactly 4 Player rows and five usable distant rows from each), `OPENING-SQUAD` for all four Player rows, `EXIT-NAMES`, three new faults | `Installed arena_yard: 4 player markers, 18 bot markers; opening squad capacity verified. Grid minimum 9 near (-11.0, 62.5).`; `MAP_MODE_ADMISSION_CHECKS_PASSED (1065 assertions)`; `MAP_MODE_ADMISSION_FAULTS_PASSED (18 compiled faults)`, including `BlockPftTacticalRefused`, `BlockSoloTacticalRefused`, `RenamedBlockExitLiteral` rejected |
| `SpawnGateChecks` | `BlockOpening` (no abort at 14.99 s, fail closed at 15 s), the formation loop for Block | `EQUATOR_SPAWN_GATE_CHECKS_PASSED (449 assertions)`, 26 `NEGATIVE CONTROL PASSED` lines (the log does not print the Block labels separately) |
| `DominationRuntimeChecks` | the Block layout row with A and B, mutant `block-lifted-point` | `DOMINATION_RUNTIME_CHECKS_PASSED (1127 assertions)`; `Fault rejected: block-lifted-point`; `Source faults rejected: 38` |
| `DominationAdmissionChecks` | Block among the supported maps, mutant `reject-arena_yard`, the Domination chain mutants moved to the `YardId` link | `DOMINATION_ADMISSION_MUTANT_REJECTED: reject-arena_yard (source pin: False)`; `DOMINATION_ADMISSION_VERIFIED: 19 compiled mutants, 8 wiring controls, installed hashes unchanged` (the printed 19 was a stale literal: twenty ran, as its README says and the 20 non-pin `MUTANT_REJECTED` lines of the log show; corrected after the review, and the final pass prints `20 compiled mutants`). |
| `AreaConfigChecks`, `NativeMenuChecks`, `ParticipantMenuChecks`, `AreaMapPreviewChecks` | migration rows `block-release` / `block-removed` / `future-version` at 8; PFT admission; Tactical offer; default 8; cap 20; label "BLOCK" and the native marker position | `PASS` in the first pass (counts under `BlockChecks` above) |
| `CompactModeUxChecks`, `DominationPolicyChecks`, `TeamDeathmatchRuntimeChecks`, `EquatorTeamSpawnChecks`, `SpawnShieldScopeChecks`, `ArenaPickupChecks`, `ArenaExtractionChecks`, `ArenaLightingChecks`, `SainExtractGuardChecks`, `PftPortedMapChecks`, `PortedCoverChecks`, `SoloTacticalSpawnChecks` | "Hold 2 points", Block among the ports, shield scope lists, map lists, exit literals, look-alike negatives, Block as an optional port | `PASS` in the first pass |

Native scenarios: `tools/BlockChecks/scenarios/` holds `block-dm`, `block-tdm`, `block-dom`, `block-zombie`, `block-pmc`
and `block-tactical-solo`; their results are in the native report. The PMC scenario places the player in the box of
the exit "Block South Gate" and ends by extraction, so the test profile keeps its gear; the Zombie scenario starts at
the installed `player-usec` row and moves to `player-bear`, because no yard-centre coordinate was measured.

## Review

An adversarial review ran over the branch after the native cycle: four lenses (migration and catalogue; team boxes,
gates and Domination geometry; modes, admission and isolation; tests and harnesses), each finding re-checked by a
second reader who tried to refute it. Ten findings, eight confirmed and two confirmed in part. **None was critical or
major.** The reviewers also listed what they examined and found correct, among it: every start point inside its own
box and outside the other with the substituted heights, the donor row as outside destination, the 15 s watchdog at
drive scale 1.0, the gates of the changed client lists (lighting, pickups, both PitFireTeam adapters run only in the
exact Arena raid), and the migration table case by case.

- **Applied (code comments and harness):**
  - The catalogue comment gave the north room one floor height from three measured points and the gap as 0.04 to
    0.05 m; the data give 0.043 to 0.058 m under those three, and the other seven were not ray-measured. Reworded.
  - `ArenaAreaCatalog` said that `RequestTeamEnemies` "always finds its five". That is argued from the rows and the
    mesh; neither PitFireTeam mode ran on the map. Reworded.
  - `ArenaPortedMapPresentation` still gave Chop Shop's marker as 870/428; ArenaMapPort moved it to 743/428 on
    2026-11-12. Corrected in the line this branch edited.
  - `BLOCK-GRID` scans the bounding box of the Bot rows only (z 23.5 to 116.9), so the pockets and the north corridor
    were held by the four Player rows alone. New pin `BLOCK-POCKETS` in `MapModeAdmissionChecks`: five distant rows at
    each of the twenty start points and at three points of the corridor. The reviewer's own count on the installed
    rows gives a minimum of 10 over the whole area and 17 or 18 at the start points, so the data were never short;
    the pin closes the gap for a later row change. It has no compiled fault of its own.
  - `DominationAdmissionChecks` printed "19 compiled mutants" although Block's `reject-arena_yard` makes twenty.
  - The registered step `BlockChecks` did not run the two test classes that carry Block's foreign-marker rows
    (`BowlTeamSpawnTests`, `IcebergTeamSpawnTests`); they are in its filter now. `BlockTeamBoxChecks`, which holds the
    installed Player rows in their boxes and the Bot rows out of them, is registered in
    `tools/verification-steps.txt` as `IcebergTeamBoxChecks` is (its Chop Shop twin is not).
  - `tools/BlockChecks/README.md` read as if `block-dm` had passed as a scenario. It had failed on the first raid
    after a server start, and the passing raid was by hand. The scenario now waits for the first bot arrival instead
    of a fixed 30 s and passed in that form on the installed build, again as the first raid after a server start
    (first arrival 38 s after the match start).
- **Not changed, with reasons:**
  - A hand-corrupted `OptionalMapsRevision` in the client's .cfg reads as 0 on the client and as "highest revision"
    on the server, so the client list can regain Block while the server keeps it removed. The map is then still not
    offered (the server lacks the row). The Iceberg and Chop Shop admissions have the same property; see the
    migration table's last row.
  - The client matches a historical default as an exact string, the server as a set, so a reordered or padded list
    with the Chop Shop set gains Block on the server only. Deliberate since the first optional map: an edited list is
    the user's.

## Not claimed

From the native cycle ([report](../reports/2026-11/GPU-BLOCK-20261121.md)):

- PitFireTeam on this map (Tactical and Zombies with a squad are admitted by the catalogue; neither ran).
- Gun Game (it takes the Deathmatch path).
- The local player on a south formation point: the test profile is BEAR, so in Team DM and Domination the player always
  started north and the south points were used by USEC bots only. The player did stand in the south pocket: Deathmatch
  and Zombies started it on the installed south Player rows (Zombies then moved it to the north Bear row), and Tactical
  placed it on the north "All", the south Usec and the north Bear row in rounds 1 to 3. None of these runs went through
  the Team DM formation.
- A match played to its result and its reward; Deathmatch above 8 participants; a held gate walked into by a player; the
  north corridor walked during a hold.
- Nothing was heard (gate sounds, crowd, announcer) and no monitor was read by a person.
- Night, weather, a long session, performance (only MapPort's drive-frame lines).
- The DynamicMaps overlay (no map pack for `arena_yard`), the killcam, the Fika fork, AmandsGraphics (code and offline
  checks only).
- Anything on GPU-1: it holds the same ArenaMapPort files and the same ArenaMode build, and nothing was run there.
- Whether standing on a formation point inside an exit box (`south-03`, `south-04`, `north-05`, `north-08`) extracts in
  an Arena raid. The 10v10 respawn read back at `north-08`, which lies inside the north exit box by the coordinates
  above; the native facts record no extraction from it.
- The PMC control used the exits of an ordinary raid; Arena's own extraction isolation was not exercised on this map.

Not measured, or measured only by argument:

- **The migration table was read back only for the Chop Shop default at revision 6.** The other rows are held by the
  xunit theory and `AreaConfigChecks`, not by an installed configuration.
- **The swing direction of the north leaves** (toward -z, the yard side) is derived from Unity's rotation convention;
  the native facts do not state that it was observed. The north room was held natively with bots and the player inside
  it.
- The gondola's open pose (an absolute local z of 2.596, a rise of 2.446 m, open y 9.546, or a relative one, open y
  9.80) was never measured, nor a carver under the hovering gondola, nor a player standing under it.
- The south pocket's only way out is the gondola lane, about 1.0 m of agent-centre space (1.6 m clear) on Arena's own
  mesh at z 16.0 and about 3 m wide at z 16.5 .. 17.4 (`yard-release.md` line 261; the port's model is wider). A player
  walked it on the host (7.5 s, 24.9 m). In the 10v10 run 10 USEC bots were alive at the release and 19 of 19 bots were
  in the field 20 s later; a player leaving the pocket with them was not run.
- The capsule half of `FloorAndClear` was measured only through its result (capacity 10/10 on build 2); no capsule
  query was logged. The floors of seven north points (`north-02`, `-05` to `-10`) were not cast.
- The two islands inside the bands (south y 7.4 .. 7.76, north y 7.04 .. 7.50) were read from the built mesh only, with
  no teleport or ray on them.
- Bots spawning at each of the 18 rows were not seen individually (MapPort saw `markers=18 onNavMesh=18`, a wave and PMC
  squads in combat). Deathmatch or Gun Game with more than eight participants (up to 23 bots from 18 rows) did not run.
- The number of Domination pickups on a longer match, and MapPort's own count (13 of 18 rows qualify), which used the
  `Capture_Zone` root, not these points.
- The pre-survey ran with no bots (`bots=None`). The whitelist, the package and the Forge text are untouched.
- **MapPort release:** Block must not be announced in an ArenaMode release before an ArenaMapPort release ships it; the
  whitelist, the Forge text and the release notes move together when it does. Data dependency: MapPort freezes the row
  ids and floats at its publication and appends only afterwards, so a change to a row before then fails the donor pins.
