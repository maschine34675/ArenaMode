# Domination on the vanilla maps (2026-11-13)

Contract for Domination on the vanilla maps Arena offers: **The Lab** and **The Labyrinth** (whole map,
stage 2) and the three **Customs areas** (stage 3, [its section](#stage-3-the-customs-areas) below). Factory and
the six ported maps are unchanged. The numbers are the lead's native measurements; the code and its offline
harnesses are what this document pins.

## What was asked

User request, 2026-11-13: extend Domination to all vanilla maps, with the Labyrinth getting only one control
point. "Vanilla" in Arena means four tiles: Factory (already had Domination), The Lab (`laboratory`), The
Labyrinth (`labyrinth`) and Customs (`bigmap`), which Arena offers only as three bounded areas. This
document covers The Lab, The Labyrinth and the three Customs areas; whole Customs does not exist in Arena and
stays refused.

## The layout kind: surveyed slots

`DominationMapLayout` had two kinds: Factory (a random three of eight surveyed seeds) and the ports'
exact authored points (a refused point aborts the match, nothing is substituted). A vanilla map without
team boxes fits neither: it has no box to take a navigation anchor from, and nobody had measured an exact
point. The third kind is a list of **slots**. Each slot has an id (`A`, `B`, `C`, `X`), a primary seed and
an ordered list of alternates; the layout may carry **extra network seeds** that are never points.
`Factory` stays the Factory flag and `Surveyed` is a second flag (no enum, so every line the harness mutants
anchor on kept its meaning).

`DominationRuntime.Prepare` for a surveyed layout:

1. **Navigation anchor** from the layout's own seeds (every slot candidate plus the extra seeds):
   `ArenaPickupGeometry.TryPoolAnchor`, the pool-based sibling of `TryFactoryAnchor`. Each seed is floored with
   the 2 m relocation (inside the active area only), seeds within 3 m of an earlier node are dropped, every
   pair of nodes must have a complete path both ways, and the best-connected node of a strict majority of at
   least three nodes is the anchor. Seeds are ordered by position first, at most 16 are accepted. Never the
   local player's position (it may stand on an island or not have landed) and never a team-box catalogue.
   `TryFactoryAnchor` and its Factory-only guard are untouched; both share one election routine.
2. **Slots in order**: the first candidate (primary, then the alternates) that passes
   `ArenaPickupGeometry.TryPlacePoint(candidate, anchor, allowNearby: true, ...)` (the 2 m relocation, native
   floor, support, clearance and the complete path from the anchor), is `ConnectedBothWays` with the anchor
   and lies at least **15 m horizontally** (`SurveyedMinimumSeparation`) from the floors already chosen. The
   same scene gives the same points. A slot without a passing candidate **aborts the match** with a message
   naming the slot, the last refusal and the number of candidates tried. No partial layout, no markers.
3. Inside a bounded area the existing contour rules apply unchanged (floor inset, path inside the polygon);
   the anchor proof floors seeds inside the area only.
4. `VerifyTeamPaths`: on a whole map a surveyed layout returns like Factory (no boxes, no gates, both factions
   spawn from the same native zones). Inside a bounded area it proves every point connected both ways with at
   least one connected start marker of each faction (`ArenaAreaRuntime.TeamStartPositions(usec)`, a read-only
   accessor over the markers the area runtime has already proved); a failure aborts the match naming the point
   and the faction. Stage 2 implemented and unit-pinned this on a synthetic layout in a synthetic area
   (`DominationRuntimeChecks`, `AreaTeamRuntimeChecks`); the three Customs rows of stage 3 use it.
5. The Debug line `Domination points verified: ...` stays; when a slot used an alternate it ends with
   `; alternates used: B=#1` (the candidate index).

Layout resolution is by (map, active area id): `DominationMapLayout.ResolveDomination(map, areaId)` and
`DominationPointCount(map, areaId)`, used by `DominationRuntime`, `TeamDeathmatchMode.Domination` and the
Domination menu copy. `Resolve(map)` and `ControlPointCount(map)` keep their meaning and map list: they are
Tactical's capture point, and **Tactical gets no point on The Lab, The Labyrinth or Customs**
([TACTICAL-OBJECTIVE-20261108.md](TACTICAL-OBJECTIVE-20261108.md)). The Tactical menu copy therefore still
promises nothing there.

## The measured seeds

Measured natively by the lead on GPU-2 on 2026-11-13 (installed master build 0.5.0+c64846fb, Team Deathmatch
raids, UiBridge teleport dry runs: NavMesh sample, room to stand, complete walkable path). Every coordinate
was accepted by the dry run; y is the NavMesh height it reported. They are **seeds for the runtime's own
proof**, not a substitute for it: the match proves each of them again against the loaded scene.

**The Lab**, floor 1, the main hall level (every point also has a complete path from floor 2 and to the
basement). Three points A/B/C, default participants stay 16:

| Slot | Primary | Alternates |
|---|---|---|
| A north-west atrium (native row cdbfeb06) | (-224.879, 0.082, -279.867) | (-231.636, 0.082, -286.694) |
| B centre, loading hall (Coop row 9896b358, measured floor 0.0) | (-171.54, 0.0, -312.18) | (-175.02, 0.1, -311.28), (-166.02, 0.1, -314.36) |
| C south (row 530af38b) | (-213.124, 0.0, -378.755) | (-205.29, 0.1, -390.55), (-202.231, 0.1, -393.884) |

Path lengths B-A 68.0 m, B-C 95.7 m, A-C 110.9 m; from the centre to the floor-1 rim 99 to 147 m.

**The Labyrinth**, single level, **one** point X, default participants stay 8:

| Slot | Primary | Alternates |
|---|---|---|
| X centre (row b99646de) | (-9.92, 0.1, 23.92) | (5.92, 0.1, 20.22), (-11.18, 0.2, 35.7) |

Extra network seeds, never points: (-23.83, 0.1, 20.08), (-10.91, 0.1, 6.08), (15.23, 0.1, 27.16). Paths from the
centre: 12 to 39 m to the other rows, 73 to 85 m to four of the five entrances; the fifth entrance
(-37.4, 0.5, 73.8) is a separate NavMesh region and is deliberately not a seed.

## Admission

`ArenaAreaCatalog.TryResolve`, Domination block: PitFireTeam stays refused. Whole map: Factory, the six
ports, **`laboratory` and `labyrinth`**; whole Customs is not on that list and must never be added to it. A
**bounded id does not return from the block**: it continues into the area table below (after the mode filter
of Customs and the ports, which now lists `domination`), so only the three Customs ids on `bigmap` are admitted
and an area id on any other map, an unknown or retired id, and a differently spelled id are refused by the same
rules that govern Team Deathmatch. Every other map stays refused. The refusal text is "Domination is not
available for this map or area, or with a PitFireTeam squad." (it was "requires Factory or a supported Arena
port, whole map, without PitFireTeam."); a Customs area id on a wrong map gets the area table's own text
("Customs Yard requires map bigmap."). The menu shows The Lab, The Labyrinth and Customs tiles under
Domination; under Customs it offers the three areas and no whole map. Participants: whole-map defaults as they
were (The Lab 16, The Labyrinth 8, 4 to 24 in even steps); there is no bot spawn shield on these maps (no team
boxes, as in Team Deathmatch there). Pickups, rewards (kills only), HUD, DynamicMaps and the announcer are
map-generic and were not changed.

## What is pinned

- `DominationRuntimeChecks`: the layout rows (primary and ordered alternates, extra seeds, the pool being every
  candidate plus the extras), the whole-map key, the pool anchor and the 2 m relocation, the primary taken when
  it passes, fallback in listed order, the 15 m separation (14.99 refused, 15.01 accepted, any direction),
  abort when a slot has no passing candidate or the network cannot be proved (no markers, no pickup
  authority), determinism, the whole-map early return of `VerifyTeamPaths`, the area team-start proof on a
  synthetic area, Tactical's `Resolve` still refusing The Lab, The Labyrinth, Customs. Fourteen new mutants. Added in the
  review round: for 15 map ids times 13 area ids, the shared catalogue admits Domination exactly where the
  layout has points (`TryResolve` against `DominationPointCount`), with the mutant
  `catalogue-admits-map-without-layout`.
- `ArenaPickupChecks`: the pool anchor over the real geometry (quorum, majority, one-way, partial, floor,
  clearance, the cap of 16 seeds, independent of the local player and of the seed order).
- `DominationAdmissionChecks`: the ten admitted maps, PFT refused on the new maps, whole Customs and the Customs
  areas refused, the refusal text, the Lab and Labyrinth rows staying visible under Domination.
- `CompactModeUxChecks`: Domination counts (Lab 3, Labyrinth 1) while Tactical's count stays 0 there.
- `TdmMatchMenuChecks`: the one menu line that picks the count per mode
  (`ArenaModeSelectionView.ControlPointCount`), read through the real description text: Domination says 3
  points on The Lab and in a Customs area and 1 point on The Labyrinth, Tactical promises no point there and
  keeps its point on Factory; mutants `menu-tactical-uses-domination-count` and
  `menu-domination-uses-control-count`.
- `NativeMenuChecks`, `NativeAreaHookChecks`, `ParticipantMenuChecks`, `AreaTeamRuntimeChecks` and the xunit
  tests that pinned the old refusal text.
- Stage 3 (the Customs areas), see the pins listed in that section.

## What is not claimed

- The offline checks are no native acceptance; what was run natively is under `NATIVE:` below. The seeds
  passed the lead's dry runs, the runtime proves them again, and a match that cannot prove its network or a
  slot aborts instead of playing a smaller layout.
- Bot behaviour on these maps is measured only as far as that paragraph says (first captures, no refusal
  line in the first one to two minutes). The 160 m route limit of the objective layer from far starts, doors,
  elevators and the other two Lab levels are unmeasured.
- The Labyrinth needs no third-party unlock mod: the client projects its menu fields (`ArenaLabyrinthAccess`)
  and the server enables the raid clone (`ArenaLocationMutator`). EnableLabyrinth is optional (docs/FEATURES.md).
- Customs: see stage 3 below.

NATIVE: run on GPU-2 on 2026-11-13 with the test builds `c5b1ed96` and `a7965541`
([report](../reports/2026-11/GPU-PRE-RELEASE-POLISH-20261113.md)). The Lab, The Labyrinth and each of the three Customs areas started a Domination
match on their primaries (no alternate was needed), logged `Domination points verified` with the rows above,
in the areas also `Arena area armed: <id>; mode=domination, ...`, READY and `Domination objective routes from
both area team starts verified.`, placed 6 of 6 pickups and saw points taken within the first minute (by USEC
on The Lab, where BEAR took its first point at 92 s; by both teams in Industrial Center and Industrial Side; by
BEAR on The Labyrinth and in Customs Yard), with no ArenaMode warning. The menu
offered ten maps under Domination, exactly the three areas and the size row on Customs, and the 3-point and
1-point copy. Team Deathmatch in Customs Yard started unchanged. Not run: a match to its limit, the other Lab
levels, the far starts of the areas against the 160 m route limit, PitFireTeam (refused by rule).

## Stage 3: the Customs areas

Domination is admitted in exactly the three surveyed Customs areas, `bigmap` only, never whole and never with
PitFireTeam. The layout kind and the runtime proof are the ones above; what stage 3 adds is the three rows, the
admission, and the team machinery that was keyed to the literal mode id `teamdeathmatch`.

### The rows

The lead's measured seeds (GPU-2, 2026-11-13, installed master build 0.5.0+c64846fb, Team Deathmatch raids,
UiBridge teleport dry runs; y is the NavMesh height the dry run reported). Seeds for the runtime's own proof, not a
substitute for it; each area has three slots and no extra network seeds (the pool is every slot candidate: 9, 7 and
8 positions).

| Area | Slot | Primary | Alternates |
|---|---|---|---|
| Industrial Center (`customs-factory-center-polygon-v2`; USEC west, BEAR east) | A (USEC side) | (355.0, 1.1, -80.0) | (350.4, 1.2, -70.1), (352.0, 1.1, -95.0) |
| | B (middle) | (395.0, 1.2, -60.0) | (405.9, 1.1, -72.8), (378.0, 1.1, -65.0) |
| | C (BEAR side) | (435.0, 1.1, -75.0) | (440.0, 2.1, -90.0), (425.0, 1.4, -100.0) |
| Industrial Side (`customs-factory-side-polygon-v2`; USEC east, BEAR west, about 40 m apart, so the points form a line across the front) | A north (native row ae0c5e1d) | (588.0, 1.2, -64.4) | none |
| | B middle (a lower strip, y -0.6) | (590.0, -0.6, -100.0) | (583.1, 1.2, -100.0), (590.0, -0.4, -110.0) |
| | C south | (600.0, 1.2, -125.0) | (590.0, 1.1, -135.0), (590.0, 1.3, -120.0) |
| Customs Yard (`customs-yard-polygon-v1`; USEC south-west, BEAR north-east, 120 m of path apart; open ground) | A west | (-204.3, 0.9, -77.9) | (-206.1, 0.9, -82.6) |
| | B middle | (-185.0, 0.9, -75.0) | (-186.0, 1.0, -80.0), (-185.7, 0.9, -85.3) |
| | C east | (-165.0, 0.8, -75.0) | (-168.0, 0.9, -82.0), (-167.1, 0.9, -92.7) |

Paths: Center from a USEC rear start (314.9, 1.2, -93.1): A 55.8 m, B 98.5 m, C 135.7 m; from a BEAR rear
start (478.0, 2.7, -83.1): B 123.4 m, the x 378 column 138 to 142 m. Side USEC (615.8, -0.4, -96.5) / BEAR
(572.0, 1.5, -110.0): A 50.3 / 59.6 m, B 40.2 / 25.8 m, C 38.0 / 32.9 m. Yard USEC / BEAR: A 61.4 / 60.3 m,
B 64.5 / 64.2 m, C 75.6 / 83.4 m. Minimum separation 15 m. The rows live in `DominationMapLayout`
(`CustomsCenter`, `CustomsSide`, `CustomsYardLayout`), resolved by `ResolveDomination("bigmap", areaId)`; the
three area ids are repeated there (the harnesses link the file without the catalogue) and a harness pins them
equal to `ArenaAreaCatalog`'s. Domination counts three points in each area (`DominationPointCount`); a map other
than Customs, a differently spelled id and the whole map count 0, and Tactical's `Resolve`/`ControlPointCount`
still know no Customs point.

### What the runtime does inside an area

`DominationRuntime.Prepare` accepts an active area when the layout was resolved for exactly that (map, area)
(stage 2); it refuses an area without a layout and a layout resolved for another area. The anchor proof floors the
seeds inside the contour only (`TryFloor`'s 0.5 m inset), each placement needs the complete path from the anchor
to stay inside the polygon (`TryPlace`, inset 0.1), and `VerifyTeamPaths` (called once after the countdown, as on
the ports) proves that every point is connected both ways with at least one verified start of each faction, or
aborts the match naming the point and the faction. No partial layout, no loosened proof.

### Where the mode id decides (traced for one Domination match in an area)

The trace: menu candidate list (`ArenaAreaSelectionState.Create` over `TryResolve`), team size
(`HasTeamSizeChoice`, `Settings.AreaTeamSize`, `TrySelectAreaTeamSize`), final validation
(`ArenaMenuSelection.HasValidAreaTeamSize`), native start (`ArenaLocalRaidStartedPatch`, `PostRaidStart`), arm on the
server (`ArenaModeRouter`, `ArenaSessionService`, the shared `TryResolve`/`TryResolveTeamSize`), location mutation
(`ArenaLocationMutator.GetAreaSpawns`, quiet map as every Domination), `ArenaAreaRuntime.Arm`, the initial team
spawn of the local PMC (`TryInitialTeamSpawn`), readiness (`ValidateTeamStarts`), bot starts per faction
(`BurstSpawnService` through `HasTeamStarts`, `TryPlanTeamBot`), respawn (`ArenaSpawnSelector` to
`ArenaAreaRuntime.PickRespawn`), the enclosure (mode-agnostic) and the match (`TeamDeathmatchMode.TeamSize` through
`CurrentTeamSize`, `DominationRuntime`). The places where the **mode id decided** and were widened from "Team
Deathmatch" to "an independent team mode" (`ArenaPopulationPolicy.IsIndependentTeam`, Team Deathmatch or
Domination), Team Deathmatch unchanged:

| Site | Rule |
|---|---|
| `ArenaAreaCatalog.TryResolve`, Domination block and the mode filter of Customs and the ports | admission, above |
| `ArenaAreaCatalog.TryResolveTeamSize(area, mode, pft, ...)` | the area team size is negotiated (2 to 5, explicit) by the independent team modes; every other mode still sends 0 |
| `ArenaAreaRuntime.HasTeamStarts` (`ArenaAreaTeamRuntime.cs`) | faction start reservations, `CurrentTeamSize`, the bot and respawn picks |
| `ArenaAreaRuntime.LocalSpawnSide` (moved next to `HasTeamStarts` so the harness compiles the production line) | the local PMC's virtual team side; other modes keep the profile side |
| `ArenaAreaSelectionState.HasTeamSizeChoice` | the team-size row of the area panel |
| `ArenaLocalRaidStartedPatch` (the size read of the native start) and `ArenaMenuSelection.HasValidAreaTeamSize` (the final validation) | the F12 size is the wire value for these modes, null for the rest |
| `DominationMapLayout.ResolveDomination` / `DominationPointCount` | the three rows |

Not changed, and why: participants (a bounded team mode never takes a participant override,
`ArenaParticipantPolicy.IsSelectable`, so the arm carries none and the area's capacity is the team size); team
boxes (`ArenaTeamBoxCatalog.Applies` is false on Customs); the server (it follows the shared catalogue; the
location mutator already validates the area's faction starts for every mode that arms an area); bot spawn shield
(none on these maps, as in Team Deathmatch there); pickups, rewards, HUD, DynamicMaps and the announcer (generic).

### What is pinned (stage 3)

- `DominationAdmissionChecks`: whole Customs refused (also with an empty or blank area id), each of the three
  areas admitted with the shared definition and its faction starts, area ids exact and map identity case-insensitive,
  PFT refused in an area with the Domination text, an area on every other map refused, retired and unknown ids
  refused, the team size negotiated like Team Deathmatch (2 to 5, explicit; a squad and the other modes refused),
  the native area model (exactly the three areas, size choice 2 to 5), no participant override, the Customs row
  staying visible under Domination. Nineteen compiled mutants (eighteen at stage 3, then `bounded-id-admitted`: the
  whole-map branch returning for every id), among them whole Customs admitted, PFT admitted, the
  area map check removed, the mode filter without Domination and the size rule back to Team Deathmatch only.
- `CustomsAllModesChecks`: the mode matrix now has Domination (admission, size UI and wire size shared with Team
  Deathmatch, no whole Customs, PFT refused); mutants for the catalogue's size rule, the mode filter and the menu's
  size row.
- `AreaTeamRuntimeChecks`: both independent modes get the faction starts, the chosen virtual side, the negotiated
  size and the release after assembly; mutants for `HasTeamStarts` and `LocalSpawnSide` restored to Team Deathmatch
  only. Every other mode keeps its plain behaviour (existing cases).
- `DominationRuntimeChecks`: the nine rows against independent literals (primaries, ordered alternates, no extra
  seeds), every seed inside its contour with the 0.5 m floor inset (the real polygons), the primaries 15 m apart,
  counts and exact area/map keys, prepare inside each area, the team-start proof on both factions, a point cut
  off from one faction aborting the match, fallback and the single-candidate abort, a layout not borrowed by
  another area; seven mutants.
- `CompactModeUxChecks`: three points per area, 0 for every wrong key, Tactical's count still 0; two mutants.
- `NativeMenuChecks` (inside `AreaTeamMenuChecks`): the real native start arms the exact area and the chosen size
  for each area and size 2, 3, 5, sends no participant count, refuses sizes 1 and 6 before any arm, refuses PFT
  and whole Customs; two mutants (start path, final validation) in `AreaTeamMenuChecks/Verify.ps1`.
- `NativeAreaHookChecks`: the Customs tile stays under Domination and an allowlist-disabled Customs is cleared.
- The xunit suite once, and the no-deploy compile check of the whole solution.

### What is not claimed (stage 3)

- The offline checks are no native acceptance; `NATIVE:` above says what was run. The seeds passed the lead's
  dry runs; the runtime proves them again and aborts instead of playing a smaller layout.
- The objective layer refuses a route longer than 160 m (`ArenaDominationRuntime.RouteAllowed`): from the BEAR rear
  start of Industrial Center the path to A is probably longer, so bots there may reject that trip and fall back to
  native behaviour for 12 s. Unmeasured.
- The team-start proof samples a start marker with `ConnectedBothWays` (0.75 m, 0.5 m vertical tolerance); if a
  marker sits higher above the NavMesh on a real area, the match aborts naming the point and the faction. The
  offline harness uses doubles for the routes; only a native run decides it.
- Yard start positions in the harness are stand-ins; the real starts are the area's surveyed markers.
- Tactical in the Customs areas, and every other mode there, behaves as before.
