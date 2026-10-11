# Equator Team Deathmatch team boxes — 2026-10-11

## Scope and contract

Independent Team Deathmatch on `arena_equator` starts and respawns the human and
each bot in its faction's original TeamFight box: USEC north, BEAR south. After
arrival, movement is unrestricted. No wall, movement containment, invented
NavMesh result or extra combat actor is introduced. Deathmatch, Gun Game, solo
Zombies and ordinary raids keep their existing general markers. Equator remains
unavailable to Tactical and PitFireTeam Zombies.

The source supplies ten distinct positions per box, so this exact format accepts
even totals 4–20 (one human included), retaining the default of four. The old
maximum of 24 cannot be promised honestly with ten places per faction. Omitted
participant counts fail before arming this format. Other maps and formats retain
their existing limits and legacy behavior.

## Source evidence and native constraints

`ArenaMode.Shared/ArenaEquatorTeamSpawns.cs` is the single immutable formation.
Its twenty coordinates and yaws are copied from native TeamFight's `SPAWNS /
ScavPMCspawn (1..20)` in the installed Arena `level57`, SHA256
`dbc821d5f3dac69a41e20b86f3d4f02dacf38f36b9fdaaf12497f0ce115753bb`.
The ignored `artifacts/equator-team-starts-20261011/recon.json` records each
original GUID, hierarchy, path ID, exact extracted floats, booth AABBs and the
blocking mesh paths. Eight accepted Equator v5 manifest samples match this set.

North positions occupy x −7.337..6.520, z 44.489..48.589; south positions occupy
x −7.215..6.510, z −41.990..−38.368; y is 0.08. Minimum same-side separation is
2.619 m north and 2.790 m south. Native `SPAWN_BOUND_FIGHTER` volumes in the
TeamFight design source enclose x −10.024..10.128, y 0.020..4.303 and
z 39.208..55.549 north / −46.652..−33.239 south. The native box volumes are
evidence of the intended starts, not collision volumes added by ArenaMode.

Relevant read-only MapPort sources:

- `D:/ArenaMapPort/tools/maps/equator.bots.json`: current spread-out generic
  player/bot starts; these original records stay unchanged.
- `D:/ArenaMapPort/ArenaMapPort.Client/SpawnPlacementRepairs.cs`: `_Respawn_`
  mesh AABBs, expanded 0.75 m, conservatively cover all twenty original points
  even when their floor/standing volume is clear.
- `D:/ArenaMapPort/ArenaMapPort.Client/SpawnInfiltrationRepairs.cs`: collection
  postfix applies placement repairs, then opens the booth doors.
- `D:/ArenaMapPort/docs/progress/stage1-combat.md`, lines 38–83, and
  `UnityProject/Assets/ArenaMapPort/Editor/NavMesh/BakeNavMesh.cs`: the old booth
  island comments are historical. The accepted rebake excluded spawn bounds,
  door leaves and LevelBorder sources. Historical GPU samples establish the
  exterior starts and USEC niche, not all twenty exact TeamFight positions.

Native EFT sources under `D:/SPT41/Development/_decompiled` establish that
`SpawnPointMarker.Create` adds new markers to the native zone's marker list,
`FixParams` does not reassign existing marker zones, and `BotZone.SpawnPoints`
caches its first read. Zone `Awake/OffAllColliders` skips spawn marker children.
`SpawnSystem.IsValidSpawn` rejects nearby living human bodies and bodies inside
the point collider; MapPort's general 25–30 m spheres would cover the whole box.
The new markers therefore have a 0.8 m trigger sphere and independent IDs.

## Ownership and failure behavior

The server appends only `arena-equator-tdm-v1-north-01..10` and
`arena-equator-tdm-v1-south-01..10` to the protected per-raid location clone. It
deep-copies the exact native atrium donor (`BotZone_Main`, core 0), uses all
three native categories `Player/Bot/BotPmc`, and assigns the exact faction.
The native donor, database records and unrelated markers are never edited.
Duplicate/unknown owned IDs or modified headers/positions/colliders are refused.
`ValidateParticipantPopulation` revalidates all twenty once in the final response
without repairing a missing or changed marker. Donor y uses a float literal to
match the actual SPT `Vector3` property type.

The client collection postfix explicitly runs after Harmony owner
`com.maschine.ArenaMapPort`. It first validates the entire owned set, native
zone marker-list membership and collider shape, then restores only these twenty
positions. It does not read the zone's cached point array during initial loading.
Native initial player selection requires the exact armed map, mode, token,
epoch, game, world, collection, profile and Player category. This early phase
checks real floor and standing volume; NavMesh is loaded later by native bots.

`Begin` requires the exact active raid and checks both teams' physically clear,
fully connected capacity before the match starts. It also validates the actual
local player's settled position. One-time capacity logs report `USEC 10/10` and
`BEAR 10/10` when every candidate is safe. Initial hook availability is checked
before the protected arm request. Missing preparation remains *required*, so it
cannot fall back to a general map spawn.

Every bot and player respawn requires real NavMesh samples and a complete path
to the known atrium anchor, a floor and standing capsule, canonical point/header
identity, living-body separation and pending-reservation separation. Selection
ranks enemy distance inside the correct box; it does not apply the generic
25 m exclusion around the human. Both teams use the existing director's roles.
Unresolved native requests retain reservations until explicit arrival/release;
there is no timeout that could reuse an outstanding promised place.

Player respawns reserve before releasing the old owner and recheck immediately
before handover. Bot and player arrivals verify the actual registered body,
faction, exact raid ownership, displacement, actual floor/path and occupancy.
Failure triggers the existing protected abort; it cannot report a successful
respawn. Fixed in-place loadout swaps are not converted to team-box respawns.
Successful arrivals log profile, side, point ID and actual position once.

The native load task has synchronous and asynchronous cleanup guards. A failed
matching load cancels the arm/compatibility state and clears references. A stale
failure cannot cancel a newer token/epoch/game/world. Reset invalidates all old
leases; plugin shutdown also removes the three hook owners.

## Offline validation and native acceptance

`tools/EquatorTeamSpawnChecks` links the production client runtime and shared
formation. Engine physics/navigation and external lifecycle are explicit test
seams, not native acceptance. The final run passed 100 assertions and all eleven
source-mutant negative controls, then restored and reran the baseline. Logs are
under `tools/EquatorTeamSpawnChecks/artifacts`. Its checks cover all scope/header gates, both
initial factions, absent preparation, unavailable capacity, disconnected boxes,
pending occupancy, body replacement, actual arrival geometry, player handover,
load cancellation and same-object rearming. `Verify.ps1` additionally mutates
isolated source copies to ensure the named behavioral protections fail when
removed; it never edits production source or deploys.

The independent runtime also compiles against the installed EFT/Unity/SPT
assemblies through the isolated ignored `compile/Compile.csproj` (0 errors;
eight expected unassigned external-fixture fields). Server xUnit coverage is
in `EquatorTeamSpawnTests`, with admission matrix adjustments in
`EquatorMapTests`, `MapModeAccessTests`, `MapModeAdmissionChecks` and
`ParticipantMenuChecks`.

Parent integration checks reported for this source change: burst integration
93 assertions / 17 negative controls; TDM match/respawn integration 247 / 20;
native menu 640; bounded area runtime 459 and 13 client contracts; participant
count negative controls. Independent review also passed 959 xUnit tests with one
existing APBS skip, 158 selected server checks, and map admission 540 assertions /
nine mutants. Participant menu passed 3,730 assertions, native hooks 195 and area
menu 29 with six mutants and a restored green baseline. Exact build/native
results and final artifact paths belong to the dated handoff after the final
source has been staged and retested.

The formation worker did not build/deploy; the parent subsequently staged the
final `96ed83b`, deployed both Arena DLLs and restarted EFT. See
`GPU-EQUATOR-TEAM-SPAWNS-RESULTS-20261011.md` for exact hashes and the October 11
native evidence. A 3v3 raid confirmed 10/10 physically safe connected candidates
per box, both faction bot starts and subsequent replacements, local USEC entry
and a natural death/respawn, plus both sides leaving their boxes and fighting.
Same-process Deathmatch used its ordinary nonteam starts without team-box state.

The native scope is deliberately narrower than exhaustive scene coverage:
local BEAR entry, actually occupying all twenty candidates simultaneously,
crowded/no-safe-point failure, each individual door leaf and ordinary PMC were
not exercised in this run. Scope rejection, occupancy and failure behavior have
production-linked offline checks. Neither those checks nor the capacity log
certify a played 10v10 or a live actor placed on every candidate.
