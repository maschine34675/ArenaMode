# Map admission and concrete blockers — 2026-10-09

## Current contract

| Map / area | Deathmatch | Gun Game | Tactical solo / PFT | Team DM | Zombies solo / PFT |
|---|---|---|---|---|---|
| Bay5, whole map | Yes | Yes | Yes | Yes | Yes |
| Equator, whole map | Yes | Yes | **No: five-opponent spawn capacity** | Yes | Yes |
| Customs, whole map | Yes | Yes | Yes | Yes | Yes |
| Customs Center / Side / Yard | No | No | No | Yes, existing 2v2–5v5 | No |

Optional maps still require the exact loaded ArenaMapPort client identity and a
matching enabled server location. Existing map allowlists, server switches and
user participant/team-size choices remain authoritative. No config migration,
new starts, widened contours, relaxed distance/occupancy checks or PFT roster
changes are introduced. Whole-map Customs is an additional fourth TDM area;
recognized selected contours remain selected. A saved `full-map` now really
selects the whole map instead of proposing Center.

## Why these boundaries

All full maps use `BurstSpawnPointSelector` over the actual native scene zones.
Tactical requests five distinct points as one squad; full-map independent TDM
owns faction slots and requests individual bodies; Zombies requests generic
infected individually. These routes do not require the bounded area's surveyed
faction formations. PFT owns the four followers around the native leader.

Installed ArenaMapPort data contains four player markers and ten bot markers on
each port. Every native entry provides at least five distinct markers outside
the unchanged 25 m floor. However, **Equator at `(0, 0.2, 2)` only provides four**.
A surviving Tactical leader stays in place between rounds; opening-start
capacity alone is therefore insufficient. Both Tactical variants remain refused
by the same shared resolver at menu, arm and response validation. No silent
smaller squad, player relocation or closer bot spawn is substituted.

Bay5 has no analogous static five-point blocker: independent review enumerated
its 25 m planar circle centers/intersections and found at most four of its ten
markers inside any such disk. Live `SpawnBlocked` and navigation still decide
runtime availability. Customs has 120 player and 157 bot markers in the installed
database; the actual production selector finds a complete five-bot opening from
each player entry. Its full-map selection may choose distant zones; this is not
evidence of balanced travel times or live path connectivity.

The three surveyed Customs contours have complete TDM formations, but their
`ArenaAreaRuntime` loading lease, actor containment, initial local-side spawn,
readiness and respawn reservations are deliberately tied to independent TDM.
Opening another mode through the catalog alone would bypass that area owner.
Those modes need a real runtime adapter before they can safely use a contour.
Unknown and retired contour IDs remain invalid. The three expanded maps also
retain rejection of old/unidentified `null`, `endless`, and `rounds` wire modes;
the existing legacy contract on Factory/Labs/Labyrinth is unchanged.

## Server and isolation

Every newly admitted current mode follows its existing quiet-clone ownership:
negotiated DM/Gun Game/TDM keep exact participant caps and zero native waves,
bosses and continuous pumps; Tactical owns its squad; Zombie also applies the
native infection fields and cap while keeping native crowds disabled. The
Customs historical `ZoneFactoryCenter` ambient config value is preserved;
current direct spawning reads all native scene zones independently. Ordinary
raids and the shared source location are not mutated by selection.

`HasConfiguredMap` now permits discovery on any actually configured map,
including a PFT flow whose only configured choice is an imported map. Exact
roster/mode admission and server/plugin availability remain later mandatory
checks. Equator-only PFT discovery is valid because Zombies is supported.

## Evidence and native boundary

- `tools/MapModeAdmissionChecks/Verify.ps1`: 501 assertions against the real
  shared resolver/participant policy and production spawn planner using the
  installed databases; six separately compiled faults rejected.
- Native area hooks: 189 assertions, including all new PFT map pairs, actual
  Ready/null selection ordering, stale owner/writer handling, missing optional
  plugin, disabled map and preserved participant callbacks.
- Real BepInEx area configuration: 250 assertions. Rendered area view: 30.
  Refined Customs geometry/capacity/migration fixture: 4492 assertions.
- Server route matrix: 24 cases; quiet clone/isolation: 14 cases. Whole-map
  Customs retains all native spawn records while ordinary/wrong-owner requests
  still leave the source unchanged. Parent rerun: 909 passed, two existing skips.

These checks execute production rules with native boundary doubles; they do not
certify live navigation or multi-round gameplay. Parent-owned GPU checks must
verify map discovery/Ready, four Customs choices, whole-map totals versus bounded
team sizes, representative newly enabled modes and protected leave. Deployment,
loaded build identities and native results belong in the dated delivery report.

Parent native checks are now recorded in
`GPU-MODE-COPY-MAP-ACCESS-RESULTS-20261009.md`: menus, Equator TDM live/refill,
Bay5 Tactical five active opponents and whole-Customs Zombie approach/melee.
They are representative smoke checks, not every pairing or PFT/multi-round
acceptance. The report preserves the distant-Customs-spawn observation and
existing melee teardown exception instead of declaring the run error-free.

## Four implementation passes

1. Traced shared selection, server clone ownership and generic native spawning;
   opened full maps while preserving bounded TDM ownership.
2. Independent adversarial review found Equator's mid-map five-bot deficit;
   retained the narrow Tactical blocker rather than relying on opening entries.
3. Added installed-data planning, quiet clone/source isolation and strict stale
   area/participant controls. Kept legacy population formats closed on new maps.
4. Updated four-choice UI/config fixtures and compiled rejection controls,
   checked stored choices/optional availability and reviewed the final matrix.
   No remaining source-level issue found; native acceptance stays separate.
