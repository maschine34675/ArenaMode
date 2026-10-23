# Bowl original team boxes

Independent Team Deathmatch uses the original east USEC and west BEAR pockets,
ten places each, for initial placement and respawns. Default: eight participants;
even totals 4–20. Existing 22/24 TDM preferences fall back to eight without
rewriting the saved preference. DM/Gun Game retain their own 24-player ceiling;
Tactical/PFT, Zombies and ordinary raids retain generic placement and no hold.

## Source and geometry

The twenty positions and yaws come from `bowl-native-v2` TeamFight extraction
in `D:/ArenaMapPort/tools/maps/bowl.modes.json`. Its north/south labels mean
east/west; all source marker names use `Bear house`, irrespective of faction.
The assignment matches MapPort's installed player-usec/player-bear markers.
Heights remain .047 m. Original SPAWN_BOUND_FIGHTER volumes have yaw318.9815:
inverse-yaw bounds retain the actual interiors instead of their larger world
AABB; horizontal interiors are inset .35 m. Anchors are original source
markers7 and11 (indices2 and16).

The immutable donor `arena_bowl-bot-core-02` at (2.5,.11,-21.1), BotZone_Main/Core0,
is also the previously host-tested field destination. The capture-light centroid
under the helicopter is not navigable and is not used. Twenty private
`arena-bowl-tdm-v1-` records are added only to the protected raid clone. Common
markers, bundles, navigation and user configuration are not overwritten.
Strict headers, actual-body geometry, occupancy and reservations remain required;
there is no map-wide fallback after box negotiation.

## Gate opening

The existing exact game/world/arm/token lease holds Bowl's four gate parts during
assembly and countdown. One release opens them; respawns never re-close them.
Bot shields follow the existing box-map TDM policy: finite exact-body protection,
visible contour and first-attack release.

MapPort0.3.0+0dd0b2a has no public pose-ready signal. IsHeld becomes false at
release; ClosedParts counts registered parts, not still-closed parts. Each pocket
has two exits, so an outside path can exist while the other gate is still moving.
Bowl waits at least **13.5 scaled seconds**, its longest authored drive, before
accepting **all twenty complete outside paths and floor/capsule checks**. The
watchdog is twenty scaled seconds; a paused animation does not time out in real
time. Countdown remains active with OPENING SPAWN GATES, damage/scoring/match
clock and the start cue blocked. No private MapPort state is accessed.

This conservative minimum also applies if opening is configured instantaneous
or an older build switches the gates off. Without an acquired optional hold,
existing open-box geometry checks apply without the artificial wait. A future
public pose-ready capability can replace this minimum; recheck it if Bowl's
animation durations change. Neither time nor IsHeld=false proves navigation.

Bowl BTR Gate and Bowl Army Gate extracts are outside the formation pockets and
are not suppressed. Cancellation/failure/teardown releases the owned hold; stale
owners cannot release a successor raid. Existing Equator/Bay5/AirPit behavior
is unchanged.

## Verification

`pwsh -NoProfile -File tools/BowlTeamBoxChecks/Verify.ps1` runs no-deploy Release
builds, server/unit tests and production-linked runtime checks for all four maps.
Compiled fault controls detect skipped drive time and the obsolete ten-second
timeout, alongside existing owner/reservation/geometry/cleanup controls. Frozen
source fixtures separately verify positions, rotated boundaries, single-axis
donor drift, foreign private markers and quiet-clone isolation.

Independent read-only review found no remaining code or test blocker after
correcting single-axis fixtures, Bowl/AirPit cross-map coverage and the menu's
twenty-participant expectation. Final unit result: 2,276 passed, one existing
APBS prerequisite skip, zero failures; all targeted harnesses and Release builds
passed. [GPU-2 results and delivered identities](GPU-BOWL-TEAM-BOXES-20261023.md)
cover 4v4, 10v10, player/bot departures, a player respawn and a DM control.
