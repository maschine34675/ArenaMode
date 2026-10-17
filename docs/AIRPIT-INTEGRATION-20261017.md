# AirPit Arena admission

2026-10-17. This change registers the optional `arena_airpit` location already
provided by ArenaMapPort. It does not create a location, alter its scene or
publish additional spawn records.

## Scope

AirPit uses the existing full-map Deathmatch, Gun Game, Team DM, Tactical and
Zombie paths. Authenticated PitFireTeam Tactical and Zombies use the existing
protected roster and coverless-map adapters. The native `BotZone_Main` zone is
used; the default selectable population is eight participants including the
human, with the ordinary supported total range still available. Tactical and
Zombie rosters retain their separate fixed/wave rules.

This admission does not add original team-box placement to AirPit. In
particular, it makes no claim that the northern original booth is navigable.
Generic scene markers remain the only placement source. The separate Equator
Tactical and PFT-Zombie exclusions remain intact.

The ported-map label is gold and reads `AIR PIT`. Unlike the earlier Bay5 and
Equator positional overrides, AirPit retains the native ArenaMapPort anchor and
offset. Refresh and close never write its geometry, including when the native
map owner changes the position during the screen's lifetime. Native text and
visibility/input leases are restored when Arena's presentation closes.

## Optional presence and saved preferences

Client discovery requires ArenaMapPort's existing plugin identity as well as
both client and server map preferences. The server independently queries the
current enabled `arena_airpit` location, using its exact canonical ID; Bay5 or
Equator's availability cannot stand in for AirPit. The detached client-config
view removes unavailable optional maps without changing persisted settings or
database data. Final arm admission retains the existing availability check.

Optional-map revision 3 adds AirPit only to recognized former defaults:

- Native defaults before the Equator migration can acquire all optional maps.
- The exact Equator default before revision 2 can acquire Bay5 and AirPit.
- The exact complete Bay5 default before revision 3 can acquire AirPit.
- Customized lists, later removals and future revision markers are preserved.
  An Equator-only list already marked revision 2 remains without Bay5 or AirPit.
- Existing disabled/customized map entries retain their values; a present but
  disabled AirPit is not re-enabled. Migration decisions are persisted even for
  edited lists, preventing later generic map-field repair from reintroducing it.

Server migration inspects the saved map set before repairing missing native
fields and works on detached defaults. Client migration retains the earlier
BepInEx boolean marker and requires the exact former default string. Both use
their historical migration cutoffs instead of widening old migrations whenever
the current revision advances.

## Four review passes

1. **Admission and optional absence.** Followed the production catalog through
   discovery, local-start negotiation, server arm and detached client settings.
   AirPit has all five existing generic modes; legacy wire modes remain rejected
   by the existing optional-map mode gate. Missing, disabled, wrong-ID, other-map
   and throwing database providers remain closed. Ordinary unselected raids do
   not enter this admission path.
2. **Persistence and compatibility.** Exercised old revisions 0/1/2, revision 3,
   future and malformed markers, exact defaults, custom subsets, removals and
   existing disabled values. Tests call actual `ConfigStore.Reload` and real
   BepInEx bind/save/reload, not only a reproduced migration formula.
3. **PFT ownership and geometry.** Only added exact AirPit names to the two
   existing adapters. Supported exact PFT versions, current world/game/body and
   controller identities, loading/active phase guards, finalization and empty
   cover requirements are unchanged. Existing real core points and native path
   results remain authoritative; no fabricated cover, complete path, alternate
   teleport or generic-map fallback is introduced. See
   [PFT startup](PFT-PORTED-MAPS-REVIEW.md) and
   [coverless combat](PORTED-COVER-REVIEW.md) for the native contracts.
4. **Presentation ownership and rollback.** Executed the production presentation
   owner with modeled Unity/TMP surfaces. AirPit supports mixed-case canonical
   IDs, gold/selected colors, exact label cleanup, preserved native coordinates
   across refresh and close, and untouched ordinary-map rows. Earlier Bay5 and
   Equator positioning remains unchanged. This caught and removed an initial
   stale-coordinate restore on closing an AirPit label-only lease.

## Reproducible offline verification

Run `pwsh -NoProfile -File tools/AirPitChecks/Verify.ps1`. The script imports
`NoDeploy.targets`, supplies a non-existent `SPTUserMods` sentinel, restores the
temporary database environment variable and fails if a deployment directory was
created. It does not build the main client or deploy anything.

The gate runs the real server unit tests for optional maps, migration, all
supported quiet-clone mode pairs and route admission; production-linked native
menu, real BepInEx configuration, map presentation and both PFT adapter
harnesses. Compiled negative controls separately remove AirPit from each PFT
adapter and require a failure at the intended positive AirPit assertion, then
rerun the unmodified production harness. Ordinary maps, absent plugin,
unauthenticated roster, stale/wrong body/controller/world, native cover data and
lookalike map names are covered by negative cases.

Current agent-run evidence is under ignored
`artifacts/airpit-integration-20261017/verify.log`:

| Gate | Result |
|---|---|
| Filtered server unit suite | 120 passed, 0 failed/skipped |
| Native menu production harness | 711 assertions |
| Real BepInEx configuration harness | 275 assertions |
| Map presentation production harness | 4,446 assertions |
| PFT startup production harness | 260 assertions |
| PFT coverless combat harness | 177 assertions |
| AirPit compiled negative controls | Both rejected at the intended assertion; production reruns passed |

The final marker was `AIRPIT_INTEGRATION_CHECKS_PASSED`; no deployment sentinel
was created. Existing unrelated server nullable/MudBlazor and test-double
warnings remain. Parent integration re-verifies the final combined source.
Server/TMP stand-ins and source checks do not prove
Unity rendering, real spawn occupancy, PFT follower activation, combat or round
transitions. Native testing, staged client/server builds, deployment and commits
are separate parent-integration steps; this document does not claim them.
