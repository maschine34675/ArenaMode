# Factory pickup connectivity independent of the player entry

## Problem and scope

The audio-layout native check on 2026-10-18 reproduced all six pickups being
skipped from player position `(12.611, 1.182, 60.104)` / sampled NavMesh
`(12.611, 1.186, 60.104)`. All fifteen attempts failed complete paths from that
origin. A second start at `(2.411, -2.640, 49.400)` placed all six immediately.
The failed origin is near native player marker
`4755c538-4ee5-4125-8cd3-885787da7ad5`, already excluded for bots/PFT as part of
the penned Main Gate courtyard. A human entry is not reliable navigation
authority for every pickup on the map.

Only the built-in **full Factory day/night** pickup layout changes. Player and
bot spawn selection, custom `Pickups.json`, other maps, bounded areas, mode
admission and native inventory ownership remain unchanged.

## Correction

- Require the same grounded, live local actor and no respawn/weapon swap.
- Resolve the eight existing authored candidate positions through the shared
  NavMesh, floor, clearance and bounded nearby-repair checks.
- Deduplicate resolved navigation positions at three metres in XZ. At least
  three separated positions must exist.
- Compare native complete paths in **both** directions. Elect a deterministic
  anchor with the largest connected group, requiring a strict majority of valid
  positions. Self paths, tied islands, partial paths and collapsed samples do
  not establish authority.
- Validate every actual pickup destination with the unchanged complete-path,
  floor, clearance, relocation and spacing checks. Nothing is placed solely
  because it contributed a seed.
- Use the same fresh election for initial placement, live rotation and reciprocal
  exchanges. A failed rotation retains the existing appearance. No cached
  navigation success survives calls or changes in the scene.

Existing one-second full-attempt throttling and fifteen-second placement window
remain. Each election adds at most 264 floor probes, eight NavMesh re-samples and
56 directed path calculations; it does no whole-scene search. Normal diagnostics
remain Debug, actual skipped placements remain Warning. Path diagnostics now
refer to the placement anchor instead of always claiming the local actor.

## Validation

The source-linked pickup harness covers isolated Factory day/night origins,
all six placements, subsequent rotations, lost-network retention, deterministic
selection, strict majority, minimum size, directionality, collapsed positions,
floor/clearance/height rejection and unchanged custom/other-map/area behavior.
Four compiled fault controls separately restore the faulty local initial anchor,
restore local rotation, permit minority islands, and remove the reverse path.

Offline results (2026-10-18):

- Pickup checks: 500 assertions, 30 wiring contracts, all 41 compiled faults
  rejected (including the four new controls).
- Pickup bot-objective integration: 650 assertions, including the new Factory
  cases and the installed native candidate catalog checks.
- Targeted regressions: assists 431, capture 137, Deathmatch 163, bot movement
  131, air control 155, native HUD 44, info panels 368; TDM 247 plus 20 rejected
  controls. Server/data units: 1179 passed, two existing skips, zero failures.
- Client and companion Release builds succeed with deployment disabled. The
  client retains four pre-existing CS8632 nullable-context warnings.
- Independent adversarial review found no concrete P0-P2 blocker.

Evidence: `artifacts/pickup-checks/00b013605b3a47a4ba8d677624850669/`,
`artifacts/pickup-regressions/5a5aaf328fdb48c988ac6497b6168b7a/` and
`artifacts/tdm-match-checks/58b597f7cf2f4d49a543e46e31a40647/`.

Native results and exact deployed identities are recorded separately in
`GPU-FACTORY-PICKUP-ANCHOR-20261018.md`. Offline checks are not Unity navigation
or pickup collection acceptance.
