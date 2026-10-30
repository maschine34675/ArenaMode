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

## 2026-09-01 five-opponent allocation follow-up

The first PitFireTeam M2 run that reached Labs proved the gate exclusions and team safety,
but exposed a separate Arena point-planning defect. Arena requested five cached hard-USEC
profiles and bypassed the vanilla population cap as intended. The old log did not print the
pre-trim profile and point counts, but the source, cache and direct-dispatch evidence traced
the three-bot result with high confidence to the planner: three preferred 25-70 m points
caused it to ignore valid points beyond 70 m. It then dispatched three opponents, and the
exact 5v5 readiness guard correctly ended the raid as no-contest at 3/5.

The planner now keeps the preferred band first and fills the remainder from distinct farther
points in the same allowed zone. Zone rotation skips a zone that cannot hold the complete
requested squad when another allowed zone can. It replans after asynchronous profile loading
so player movement cannot leave it on a newly undersized zone. The installed Labs data also
contains two different Floor1 markers at one exact coordinate; capacity and selection now
deduplicate physical positions so two opponents cannot be stacked there. PitFireTeam team
Arena additionally requires an exact five-profile/five-point plan and dispatches no partial
squad. Diagnostic logging records the zone, requested and available profile counts, preferred
and distant capacity, and the final selected count.

The production point selector is linked into the Labs client harness. Its new cases prove
three preferred plus five distant points yield exactly five distinct selections, blocked and
under-25-m points stay excluded, ambient pairs remain inside the preferred band when possible,
a duplicate coordinate counts once, a too-small zone is skipped, and movement during profile
loading triggers a fresh complete-zone choice. The harness passes 75/75 assertions. A separate installed-
database test checks all 75 Labs player markers and proves each has at least one of the three
allowed bot zones with five points at least 25 m away. The complete unit suite passes 92/92.

Current build/deployed client SHA-256:
`7DC1679E3B0C47890CC311F38107D4A74C9E780EFBF36BD4A3E59E23EABCDA85`.

Current build/deployed server SHA-256:
`2528835212D286D90D84184C932EFB2129E86C91B012F407ED51E2DD35DB1B8A`.

The remaining live check is M2: expect one spawn-plan line with `selected 5`, then
`dispatched 5x`, five arrival callbacks and `Tactical team Arena live`. EFT was not launched
on this GPU-less server.
