# AirPit original team boxes — 2026-10-17

Scope: independent Team Deathmatch only, original north USEC / south BEAR
enclosures, ten authored places per faction. Explicit even totals 4–20;
default eight. A saved 22/24-player team preference falls back to eight on read
without rewriting it. Solo modes retain their own 24-player ceiling; Tactical,
PFT, Zombies, normal raids and common MapPort markers are unchanged.

## Map contract

Requires ArenaMapPort 0.2.2 (extended north navigation and passport held gates).
The version is a documented asset prerequisite, not permission to bypass real
geometry validation. Older or changed map assets fail closed when their native
navigation cannot support the formation. No map-wide fallback is introduced.

Source: original AirPit TeamFight DesignStuff scene, north markers
6–10/16–20 and south 1–5/11–15. Coordinates, authored heights and headings are
retained. Versioned private IDs are inserted only into the protected raid clone;
the donor is unchanged `arena_airpit-bot-center-east` at (-3.21,.1,-12.09).
The external path destination (-15.51,.1,-37.64) is independently navigable;
it is not the donor. Native box bounds and anchors (indices 1/17) constrain
the existing closed-gate path exception.

MapPort 0.2.2's initially closed passport doors do not mean a foreign hold:
`IsHeld` remains false until a caller holds the gates. Arena's existing exact
world/game/arm ownership can therefore hold both passport and bus doors.
Its countdown release opens both; all twenty complete outside paths must pass
before combat. Respawns use the same boxes and never re-close the gates.
AirPit exits are outside the boxes and are never suppressed.
The user explicitly accepts jumping over low turnstiles; no extra barrier or
jump restriction has been added.

## Geometry review and validation

The v9 TeamFight scene is byte-identical to v8. Current U8 navigation has SHA256
`2A72A71BA0C959A816C26885C58EA50412075625AE62E4CFDB420B54D185D275`.
All twenty points and the destination lie on its main component, offline.
Smallest marker edge clearance is 1.1503 m; anchor clearance 6.3892/3.1502 m;
destination clearance 4.4519 m. These facts do not replace live floor, headroom,
occupancy or complete-path checks.

The frozen 0.2.2 release ZIP contains U7; current installed v9/U8 also restores
three distant crates. Native acceptance must identify the actual tested assets,
not claim that the frozen ZIP and current installation are identical.

Automated validation: full suite 1,151 passed / one installed-APBS probe skipped;
three-map runtime318 assertions and12 rejected mutants; gate suite197 and21
rejected mutants; installed map admission801; participant menu4,245; native
menu725. Independent static review found no outstanding defect.
GPU1 delivery evidence: pending. Acceptance ledger:
[AIRPIT-TEAM-BOXES-GATES.md](AIRPIT-TEAM-BOXES-GATES.md).
