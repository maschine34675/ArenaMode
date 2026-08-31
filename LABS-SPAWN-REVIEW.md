# Arena Labs: exclude extraction gate bot spawns

Implemented 2026-08-31 after the user confirmed that only bots, not player respawns, were getting trapped behind the extraction gates.

## Scope and evidence

Arena Labs excludes `BotZoneGate1` and `BotZoneGate2`. `BotZoneFloor1`, `BotZoneFloor2` and `BotZoneBasement` remain available. Ordinary raids, other Arena maps, player respawns, extraction doors and both profile protection layers are unchanged.

The installed Labs database contains 170 spawn markers. Gate1 has four and Gate2 has five; all nine have only `Bot`/`Boss` categories. The remaining 161 markers include 75 `Player` and 40 cooperative/group markers. Tests compare every remaining marker's serialized data, not only those counts.

## Implementation

- `ArenaBotSpawnPolicy` is shared by client and server, keeping the exact map/zone exclusion in one place. Matching is case-insensitive and does not reject similarly named zones such as Gate10.
- Labs defaults and the installed `config.json` now list the three accessible zones. The policy also rejects gate entries in older saved configurations; deploying the DLLs does not depend on replacing a user's config.
- `ArenaLocationMutator` removes only bot-exclusive gate markers from SPT's per-raid location clone. Mixed, player, cooperative, unknown and unclassified markers are preserved. Existing waves, boss/extract-trigger entries and generated waves lose gate references. A gate-only named entry is removed, rather than converted to a blank zone that could select an arbitrary location.
- The client filters `BotSpawner.SpawnZones`, which supplies Arena's direct bursts and Tactical squads. The shared zone and patrol arrays are not modified.
- The public `SpawnSystem.SelectAISpawnPoints` overload rejects excluded zones and returned batches containing gate points. This runs before the interface wrapper applies spawn cooldowns. Native callers already handle a null result as no available AI spawn.
- The `BotSpawner.SpawnBotsInZoneOnPositions` prefix rejects an entire gate-containing batch before native spawn counters or profile positions change. It does not partially remove points, cancel shared creation data or edit player markers.

Client guards require an armed or active Arena session on Labs, including loading/countdown spawns. With those flags off, or on another map, the original choices are returned unchanged. The player selector and its spawn collection are never patched.

## Review and verification

Independent adversarial review traced the real installed game's call chain, including initial bot setup, AI selection, forced dispatch, scene-marker caching and raid cleanup. No required correction remained. Server mutation is guarded by the active Arena session and operates on SPT's cloned location data.

Verified locally without launching the game:

- Release solution build succeeds, with five pre-existing warnings and no errors.
- All 63 server tests pass, with no skips. The installed Labs fixture is exercised with vanilla waves, retained ABPS waves and Tactical mode; it changes from 170 to 161 markers while the original fixture, all remaining markers and the input configuration stay unchanged.
- A separate project using a copied pre-change mutator fails 12 of the new checks, including all three real-fixture cases. Production sources were never reverted for this test.
- The client harness passes 65 assertions using the production policy, filter and hook methods, covering loading/active sessions, both gates, the three retained zones, unchanged player markers and rejection before bot accounting. Its game/Harmony stand-ins do not replace a live raid test.
- Two separate copied-source client regressions (disabled shared exclusion and disabled client session guard) fail at the armed/loading zone-filter boundary. The unchanged production harness passes all 65 assertions again afterward.
- Existing menu negotiation (31), side button (29), map view (40) and announcer (17) checks pass again.
- Installed configuration verification confirms that only `maps.laboratory.spawnZones` changed. The previous file is saved under ignored `artifacts/labs-bot-exclusion/config.before.json`.
- Release and installed DLLs are SHA-256 identical. The deployed client was also decompiled to confirm the new bot hooks are present.

Installed client SHA-256: `BBD1F8B7A3AFA476EED2E3F0709BCFF306865213E3440BD36F01C25800778320`.

Installed server SHA-256: `7352DBE3597AB9AAF1DDAB4884F2539850BEEE7F054912067662F4FE3E65DBC0`.

## Remaining live check

Restart the server and game with both updated DLLs. In Arena Labs, verify that bots spawn on the two floors/basement and no longer appear behind either gate, including after activating an extract. Check initial pressure, repeated reinforcements and a Tactical squad. Player respawns and an ordinary Labs raid should behave as before.

This server has no GPU; no game was launched. Offline checks cannot prove actual Harmony execution, navigation or bot movement. An unrelated mod that directly creates/teleports bots while bypassing these native spawn methods is outside the verified call chain.
