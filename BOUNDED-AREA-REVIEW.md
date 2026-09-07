# Bounded Team Deathmatch prototype

Recorded 2026-09-07. Implementation `0c54b77` has offline verification and a
successful user GPU playtest of its core boundary/spawn behavior. The confirmed
coverage and remaining checks are recorded below. No raid was started by an
agent on the development host.

The subsequent first Customs preset is documented separately in
[CUSTOMS-AREA-REVIEW.md](CUSTOMS-AREA-REVIEW.md). The Factory geometry and the
historical measurements below are retained; use the Customs document for the
latest combined build/verification and pending new GPU checks.

## Scope and enablement

The first preset is `factory-south-v1`: Factory **daytime**, independent Team
Deathmatch, fixed **2v2** (the player plus one ally against two opponents).
It adds a cyan grid, physical enclosure and navigation restrictions. It does
not change the native mode sidebar, add Customs to the map list, implement an
area editor or add objective modes. PFT Tactical is not supported by this preset.

Restart both server and game after deployment. Before starting a match, choose
F12 -> ArenaMode -> Team Deathmatch -> `ExperimentalArea = factory-south-v1`,
then native Arena -> Team Deathmatch -> Factory daytime. A selected area on a
different TDM map is rejected before loading instead of silently using the
whole map. Other modes ignore this setting.

The default is `full-map`, which retains the previous TDM size/settings. Restore
that value to disable the prototype. The area and its 2v2 size are frozen for
the match; changing the F12 setting during a raid applies next match only.
An eventual native area picker should wait for a verified useful preset.

## Why this candidate

`ArenaAreaCatalog` defines a convex 3D box:

| Axis | Minimum | Maximum |
| --- | ---: | ---: |
| X | -25 | 40 |
| Y | -1 | 10 |
| Z | -46 | 26 |

Spawn foot positions additionally require Y in [-0.5, 2] and a 0.75 m horizontal
inset. Navigation uses a 0.4 m horizontal inset. The separate spawn-height band
excludes tunnel markers without placing a low ceiling over ordinary jumps.
The bottom barrier closes descent below the area; it might also cut a useful
route, which is one of the things the actual scene must validate.

The installed Factory database contains 29 eligible markers: 20 Player markers
and nine distinct Bot positions. Player markers form several close clusters,
so raw marker count is not treated as player capacity. Every eligible player
marker has another player marker at least 40 m away and at least three bot
positions at least 25 m away. A sampled ground-level grid also retained enough
distant choices; that is data evidence, **not proof of live connected routes**.
The previously excluded Main Gate courtyard spawn lies outside this box.

Database: `SPT_Runtime/SPT_Data/database/locations/factory4_day/base.json`.
SHA256: `9826CA3EFFD4F925E8DC123CA1BAFD8469B24BEDF3C0F2C2D41270550ABA430D`.
Change the versioned area ID whenever its geometry/rules change so mismatched
client and server definitions cannot negotiate as the same preset.

## Ownership and native contracts

- The request freezes `areaId` alongside map, mode and exact arm identity. The
  server validates it before preparing a snapshot; replays require the same
  identity. A bounded client requires an explicit matching server area response.
- Location filtering changes only the per-raid clone. All marker categories are
  filtered; their original objects and flags are not changed. Distinct IDs and
  minimum per-faction Player/Bot positions are required. Native `Pmc` side masks
  count for both USEC and BEAR. The final native response must contain the same
  validated clone; replacement or later outside markers fail closed.
- Native `SpawnPointsCollection.CreateFromScene` can prefer scene markers over
  server data based on the scene export date. Its exact postfix applies the
  same filter before native initial player creation. This binds the actual
  `LocalGame`, `GameWorld`, map and native arm token, not just a global toggle.
- The actual `BaseLocalGame<EftGamePlayerOwner>.Run` task is observed. Synchronous
  and later asynchronous loading failures cancel the matching unconfirmed arm;
  a delayed old failure cannot cancel a different current world/game. Vanilla
  `OnReadyToStartMatchingAsync` error handling returns to menu and unloads the
  failed local game. Confirmed Arena failures use the existing protected native
  stop pipeline. No snapshot or exact native-end protection was weakened.
- On game start, Arena creates its own unparented scene root, six colliders,
  six carving obstacles, one grid mesh and one material. No shared map object,
  collision matrix, NavMesh area cost, SAIN preset or PFT setting is changed.
  `LowPolyCollider` follows native player body collision; shader is the installed
  `Sprites/Default`. The root uses world coordinates without inheriting another
  mod's GameWorld transform. Reset disables objects immediately and destroys
  only those owned resources.
- Carving is asynchronous. Preparation blocks movement/respawn/spawn dispatch,
  preserves Escape and protects scoped bodies against damage. For up to 12 s it
  checks enclosure presence, crossing probes and complete NavMesh connections
  from the actual initial player spawn. Only connected candidate markers are
  retained. It requires at least two player choices, three bot positions and
  adequate separation at each retained player marker. Failure reports its
  reason and stops; it never falls back to unbounded spawns.
- Player respawns and initial/live kit replacement resolve and validate their
  selected position **before** Redeploy deregisters the old body or mutates its
  profile. The prepared selector retains the exact game/world/area and checks
  again at native release. A failed partial replacement stops the area match
  instead of rewiring an uncertain old body. The full-map selector stays lazy
  and retains its existing fallback.
- Fourteen verified native navigation targets cover goals, direct routes,
  accepted path setters and player/mover teleports. Every accepted corner and
  the segment from the current body stay inside the convex volume. Native
  partial paths wholly inside may remain partial; no fake completion is returned.
  Spawn/readiness/teleport connectivity separately requires `PathComplete`.
  Rejected inner requests propagate `PathInvalid` to outer goal calls. Existing
  safe paths and native null-path cleanup remain valid. Rejected teleports also
  restore `PositionOnWayInner` to the actual body position.
- A 0.2 s watchdog checks enclosure presence, current paths and current living
  bodies. It catches path arrays modified without a setter. Unexpected escape
  permits at most two recoveries to that same body's last verified inner point;
  missing/failed/repeated recovery stops the match. Three consecutive 20 s
  windows with repeated forbidden requests, no movement and no match kills also
  stop instead of spinning indefinitely. Dead/replaced bodies are pruned.

Native source anchors: `BaseLocalGame`, `TarkovApplication`,
`EFT.Game.Spawning/SpawnPointsCollection`, `NavMeshDoorLink`, `BotDoorsController`,
`BotPathFinderCorePoints`, `BotPathController`, `BotMover`, `EFT/Player`, and
`Redeploy.Core/Engine/RespawnEngine.cs`. Checked against this SPT 4.1 installation;
none of the optional AI mods' code or binaries were copied or bundled.

## Review and automated evidence

Independent adversarial passes covered both server and client, then rechecked
the fixes. Findings corrected before delivery: native `Pmc` side masks; a scene
load failure after successful server start; loading dispatch needing exact
current ownership; stale loading exceptions; physical path/replacement bypasses.
The final code review reported no remaining P0-P2 finding. That is not a gameplay
acceptance claim.

`tools/ArenaAreaChecks/Verify.ps1` runs Rules, Server, Integration, Navigation,
Regression and Build stages. Pass `-SptRoot C:\SPT41` on the GPU host. Tests set
both `SPT_DATABASE` and `SPT_RUNTIME` and reject skipped/unpassed results,
including dynamic skips hidden by VSTest's aggregate skip counter.

- Full server/data suite: 269 passed, zero skipped, including 13 geometry/data
  and 34 exact server-area cases and the installed optional PFT contract.
- Production-linked respawn/selector harness: 13 assertions passed. It covers
  full-map laziness/fallback, bounded preflight, no outside fallback, frozen
  position and rejection after area/raid drift. Host doubles do not prove EFT
  body creation or physics.
- Three standalone source-copy negative controls fail their intended assertions:
  allowing the full-map fallback, skipping final area identity, and accepting an
  outside intermediate path corner. Restored positive controls pass; production
  source hashes remain unchanged. Mutant projects have no deployment targets.
- Existing production-linked client harnesses: NativeMenu 87, LabsBotSpawn 288,
  TDM Runtime 209 and TeamGiveUp 385 assertions. New cases cover area negotiation,
  missing/mismatched acknowledgement, pending/outside dispatch, fixed size and
  respawn preparation; existing full-map assertions are retained.
- Release solution and final explicit Release client/companion build succeed
  with four pre-existing nullable warnings, no errors. Building the companion
  last also leaves the external Redeploy dependency in Release. Build and
  installed client, companion, server and Redeploy DLLs are SHA256-compared.
  Local TRX and hash evidence is under ignored `artifacts/area-checks/`.

Verified Release / local installed SHA256 identities:

| DLL | SHA256 |
| --- | --- |
| ArenaMode.Client | `3AAD1B09B8A5755172148F9D901B4A48964382F356328AD798F39F4839D585C5` |
| ArenaMode.BigBrainCompat | `5C1B2605C23E32C15C8001C765EC2865F593807EC12C5C71B1AF46F122036173` |
| ArenaMode.Server | `25F610B261B30084A7685F74B6DB7D9B19B002479785061C54BF3C6A33AE5C2B` |
| Redeploy.Core | `EFBA4A93354A28E7F6A21B619C36D96A06F3CCDB9CAF70704F5D158987251362` |

Deployment hashes above refer to D:\SPT41. At initial delivery Syncthing reported
SPT-GPU disconnected. The subsequent user playtest confirms the prototype ran
on the GPU host; it is not a new remote hash or synchronization-status check.
Compare these hashes there if there is doubt about the loaded version.

## User GPU playtest and remaining acceptance

User report on 2026-09-07, following delivery of `0c54b77`:

- The grid was visible and could not be crossed.
- The player and bots always spawned within the zone during the test.
- Bots stayed inside the zone; no bots were observed getting stuck.

This confirms the core prototype behavior in the reported playtest. It does not
establish exhaustive edge/corner/vault coverage, a tested match count or duration,
or successful cleanup in subsequent ordinary raids. This update records the
user's observations; it adds no new log analysis or automated test results.

Still explicitly unconfirmed: Escape/give-up during preparation and combat,
re-entry into another bounded match, switching back to full-map TDM, and a
subsequent ordinary PMC/Scav raid with clean enclosure/input/navigation teardown
and intact profile isolation. The combined manual acceptance gate therefore
remains partially satisfied rather than being marked fully passed.

Retained test checklist (core behavior above is already user-confirmed):

1. Start the configured Factory-day TDM. Expect `Arena area READY:` in the log,
   the cyan boundary and 2v2. If preparation aborts, keep the specific
   `Arena area stopped:` / loading failure and nearby log lines.
2. Walk, sprint, jump and attempt vaulting at reachable edges/corners and tunnel
   entrances. Bodies must not cross; there must be enough headroom and usable
   connected fighting space. Inspect grid visibility from multiple elevations.
3. Watch both teams navigate, use cover and fight. Test several player and bot
   respawns, including after fighting near a wall. No actor should spawn outside
   or repeatedly request forbidden paths without progress.
4. Confirm Escape/give-up works during preparation and combat. End the raid;
   test a second bounded match, full-map TDM and an ordinary PMC/Scav raid.
   Enclosure/navigation/input state must be gone and normal profile isolation
   must remain intact.

Limits: barriers currently block **bodies**, not promised projectiles, grenades
or AI line of sight. These use other native masks and are a separate design
choice. Crossing probes are sampled, not a mathematical proof of the entire
baked NavMesh; real routes, setter guards and physical bounds complement them.
This prototype does not teach SAIN new tactical goals or cover points. The
watchdog catches specific boundary failures, not every possible AI stall.

After a successful small-map test, choose a useful Customs area using scene
coordinates and connectivity, then add its versioned preset and map admission.
Concave/multiple-floor areas, native area selection, PFT and CTF remain future work.
