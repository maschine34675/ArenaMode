# Factory courtyard spawn exclusion

User-reported vault-only spawn near the Main Gate / Woods transit:
`c671337f-e58f-4de6-a387-307d877954a3`, at world
`(14.018, 1.18999994, 58.119)`. The installed Factory day and night databases
contain the same 145 markers; this particular record is `Player`, with an empty
`BotZoneName`. No nearby marker or coordinate radius is excluded.

## Why a bot-only filter is insufficient

PitFireTeam's `SpawnGroupBots` captures the leader's current position, then
activates each follower with that position. It does not use the Arena bot point
selector or `SpawnBotsInZoneOnPositions`. Protected Tactical 5v5 calls the same
PFT entry point during round rebuilding. A player spawn can therefore also place
all four companions in the courtyard pocket.

## Scope and implementation

- The exact ID is excluded from direct Arena bot planning on Factory day/night,
  before both capacity counting and point deduplication. The AI selection and
  direct-dispatch hooks also reject an unsafe whole batch before native counters
  change. Profiles and points are never partially removed at that boundary.
- Protected Tactical 5v5 also excludes the ID from the player's initial spawn
  and respawn choices. `ArenaTeamSpawnPointsPatch` projects the result of native
  `SpawnPointsCollection.CreateFromScene` into a copied dictionary. Both native
  raid entry and Redeploy's cached respawn system consume this result.
- The source dictionary, scene markers, categories, `SpawnBlocked` flags and
  `BotZone.SpawnPointMarkers` remain untouched. Player spawns in solo Arena and
  every spawn in ordinary raids retain the original collection.
- Guards require the actual world location to match the armed Factory map. An
  active raid additionally needs the exact recorded game/world pair; a newly
  armed raid takes precedence over a stale previous active latch. Player filtering
  additionally requires the frozen PFT roster and Tactical mode.
- All Factory bot zones remain available. The existing server-facing
  `IsRestrictedMap` contract remains Labs-only, preserving its wave and location
  mutation behavior. This fix does not edit the database or server configuration.

## Verified native contracts

Checked against the local SPT 4.1 decompilation and installed PFT 0.10.0:

- `EFT.BaseLocalGame.Run` sets `GameWorld.LocationId`, obtains the scene
  collection, creates the native spawn system and selects the player start.
- `SpawnPointsCollection.CreateFromScene(DateTime?, SpawnPointParams[])` may
  ignore server parameters on an export-date mismatch. Filtering its result
  still covers that case without destroying or modifying scene objects.
- `SpawnPointsCollection(Dictionary<ISpawnPoint, SpawnPointMarker>, Logger)`
  stores only the supplied index and logger.
- `Redeploy.Core.Spawning.SpawnSystemCache.GetOrCreate` uses the same scene
  collection method; its cache is scoped to the `LocalGame` instance.
- PFT captures `player.Position` before profile loading and uses it for each
  follower's `PositionNote`. No PFT source, binary or asset is copied or bundled.

## Validation and limits

The production-linked `tools/LabsBotSpawnChecks` harness passed 265 assertions covering the Factory rule,
native hook signatures, initial/respawn collection views, bot planning and batch
accounting alongside the existing Labs protections. All six Factory and both
existing Labs negative controls failed their intended behavioral assertions,
followed by passing unmodified production runs. The installed
database fixture checks that only the identified marker is removed from each
145-marker map. See the harness README for runnable checks.

An independent adversarial native/source review found no remaining actionable
issues. Validation on 2026-09-06 also passed 207 server tests (none skipped),
47 solo Tactical, 143 TDM, 231 PFT compatibility and 767 team-expansion checks.
Installed-assembly inspection confirmed the new hook signature, constructor
and registration. Release client/server and Redeploy.Core DLLs were hash-matched
to the live installation. Offline tests do not execute Unity or a raid: the host
has no GPU. Test initial 5v5 entry and a post-death next round on both Factory
variants, plus a following ordinary raid.

This is an exact spawn exclusion, not an area barrier or navigation repair. If
the player manually walks into the pocket before followers are rebuilt, PFT can
still use that current position. The user must confirm that the nominated marker
is the one associated with the observed stuck bots.
