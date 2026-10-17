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

Build and native acceptance evidence will be recorded after the final
integrated source is frozen. No offline test is a substitute for live
floor/clearance/path and actual departure checks.
