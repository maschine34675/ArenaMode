# Domination on the vanilla maps (2026-11-13)

Contract for Domination on the vanilla maps Arena offers: **The Lab** and **The Labyrinth** (whole map,
stage 2, below) and the three **Customs areas** (stage 3, section marked below). Factory and the six
ported maps are unchanged. The numbers are the lead's native measurements; the stage 2 code and its
offline harnesses are what this document pins.

## What was asked

User request, 2026-11-13: extend Domination to all vanilla maps, with the Labyrinth getting only one control
point. "Vanilla" in Arena means four tiles: Factory (already had Domination), The Lab (`laboratory`), The
Labyrinth (`labyrinth`) and Customs (`bigmap`), which Arena offers only as three bounded areas. This
document covers The Lab and The Labyrinth; whole Customs does not exist in Arena and stays refused.

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
   and the faction. **Stage 2 implemented and unit-pinned this with a synthetic layout in a synthetic area
   (`DominationRuntimeChecks`, `AreaTeamRuntimeChecks`); no Customs row uses it before stage 3.**
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
ports, **`laboratory` and `labyrinth`**. Every other map and every bounded area stays refused, whole Customs
included (the Domination block returns before the whole-Customs rule, so Customs must never be added there
by map id). The refusal text is now "Domination is not available for this map or area, or with a PitFireTeam
squad." (it was "requires Factory or a supported Arena port, whole map, without PitFireTeam."). The menu
shows The Lab and The Labyrinth tiles under Domination; Customs is still hidden. Participants: whole-map
defaults as they were (The Lab 16, The Labyrinth 8, 4 to 24 in even steps); there is no bot spawn shield on
these maps (no team boxes, as in Team Deathmatch there). Pickups, rewards (kills only), HUD, DynamicMaps and
the announcer are map-generic and were not changed.

## What is pinned

- `DominationRuntimeChecks`: the layout rows (primary and ordered alternates, extra seeds, the pool being every
  candidate plus the extras), the whole-map key, the pool anchor and the 2 m relocation, the primary taken when
  it passes, fallback in listed order, the 15 m separation (14.99 refused, 15.01 accepted, any direction),
  abort when a slot has no passing candidate or the network cannot be proved (no markers, no pickup
  authority), determinism, the whole-map early return of `VerifyTeamPaths`, the area team-start proof on a
  synthetic area, Tactical's `Resolve` still refusing The Lab, The Labyrinth, Customs. Fifteen new mutants.
- `ArenaPickupChecks`: the pool anchor over the real geometry (quorum, majority, one-way, partial, floor,
  clearance, the cap of 16 seeds, independent of the local player and of the seed order).
- `DominationAdmissionChecks`: the ten admitted maps, PFT refused on the new maps, whole Customs and the Customs
  areas refused, the refusal text, the Lab and Labyrinth rows staying visible under Domination.
- `CompactModeUxChecks`: Domination counts (Lab 3, Labyrinth 1) while Tactical's count stays 0 there.
- `NativeMenuChecks`, `NativeAreaHookChecks`, `ParticipantMenuChecks`, `AreaTeamRuntimeChecks` and the xunit
  tests that pinned the old refusal text.

## What is not claimed

- Nothing here is a native acceptance. The seeds passed the lead's dry runs, the runtime proves them again,
  and a match that cannot prove its network or a slot aborts instead of playing a smaller layout.
- Bot behaviour on these maps (the 160 m route limit of the objective layer, doors, elevators and the three
  Lab levels, the maze) is unmeasured; the native run decides it.
- The Labyrinth needs the third-party mod that unlocks it on the host.
- Customs: see stage 3 below.

NATIVE: (lead fills in)

## Stage 3: the Customs areas

(Stage 3 completes this section: the three area rows, the area team-start modes, the matching admission
and pins. Stage 2 leaves the hooks: `ResolveDomination` / `DominationPointCount` already take the area id and
return "no layout" for every area, `DominationRuntime` keys its layout by (map, area), and
`VerifyTeamPaths` has the area team-start proof.)

The lead's measured seeds, for stage 3:

| Area | Slot | Primary | Alternates |
|---|---|---|---|
| Industrial Center (`customs-factory-center-polygon-v2`; USEC west, BEAR east) | A (USEC side) | (355.0, 1.1, -80.0) | (350.4, 1.2, -70.1), (352.0, 1.1, -95.0) |
| | B (middle) | (395.0, 1.2, -60.0) | (405.9, 1.1, -72.8), (378.0, 1.1, -65.0) |
| | C (BEAR side) | (435.0, 1.1, -75.0) | (440.0, 2.1, -90.0), (425.0, 1.4, -100.0) |
| Industrial Side (`customs-factory-side-polygon-v2`; USEC east, BEAR west, about 40 m apart) | A north | (588.0, 1.2, -64.4) | none |
| | B middle (a lower strip, y -0.6) | (590.0, -0.6, -100.0) | (583.1, 1.2, -100.0), (590.0, -0.4, -110.0) |
| | C south | (600.0, 1.2, -125.0) | (590.0, 1.1, -135.0), (590.0, 1.3, -120.0) |
| Customs Yard (`customs-yard-polygon-v1`; USEC south-west, BEAR north-east) | A west | (-204.3, 0.9, -77.9) | (-206.1, 0.9, -82.6) |
| | B middle | (-185.0, 0.9, -75.0) | (-186.0, 1.0, -80.0), (-185.7, 0.9, -85.3) |
| | C east | (-165.0, 0.8, -75.0) | (-168.0, 0.9, -82.0), (-167.1, 0.9, -92.7) |

Paths: Center from a USEC rear start (314.9, 1.2, -93.1): A 55.8 m, B 98.5 m, C 135.7 m; from a BEAR rear
start (478.0, 2.7, -83.1): B 123.4 m. Side USEC (615.8, -0.4, -96.5) / BEAR (572.0, 1.5, -110.0): A 50.3 / 59.6 m,
B 40.2 / 25.8 m, C 38.0 / 32.9 m. Yard USEC / BEAR: A 61.4 / 60.3 m, B 64.5 / 64.2 m, C 75.6 / 83.4 m.
