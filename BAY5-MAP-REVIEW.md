# Optional Bay5 map integration

Date: 2026-09-21. Base: `8c05327`; branch: `feature/bay5-map-integration`.

## Result and boundary

Bay5 (`arena_bay5`) from ArenaMapPort joins the native Arena map selection for
**Deathmatch and Gun Game**. Equator remains separately selectable. The shared
area contract rejects Tactical, Team Deathmatch and PitFireTeam for both maps
at discovery, final request, server arm/replay and location preparation. Team
starts have not been certified. No other imported maps are admitted.

The client requires the loaded `com.maschine.ArenaMapPort` plugin. For each
optional map, the server independently resolves that map's canonical ID from
the live location table and requires an enabled location with matching identity.
The other optional map cannot substitute for missing data. Queries remain late
and per request. Availability filters a detached client-config transport copy;
neither the live config nor the shared location database is mutated. Rejection
still happens before stale-snapshot recovery and arm preparation.

The native row owner already tracks each map's ID. Bay5 now participates in that
existing mechanism. Mode changes hide both incompatible rows and clear the
selected row/toggle/Ready state. Removing or disabling one map leaves the other
map's selected row intact. Native selection is not rebuilt and subscriptions
are not recreated. Direct stale clicks and final requests remain guarded.

Bay5 defaults use **BotZone_Main**, an eight-bot cap and a four-alive
replenishment target. The two-bot burst and 25m minimum spawn distance are
unchanged. ArenaMapPort owns the actual spawn positions and native navigation
proof. Map data and real playtests are required before claiming playability.
No profile transaction, kit, score, death, loot, Scav or normal-raid behavior
is changed here.

## Upgrade from the already deployed Equator release

The old client release wrote `General/OptionalMapsMigrated=true`; the server
wrote `optionalMapsRevision=1`. Reusing either old guard would skip current
default users. Revision 2 therefore makes a new, once-only decision:

| Saved state | Result |
|---|---|
| Exact old Equator client string or server map-ID set, revision below 2 | Add Bay5; keep all existing map settings |
| Unmigrated historical native defaults | Add Equator and Bay5 |
| Previously migrated native list missing Equator | Preserve the removal; add neither map |
| Customized list/set | Preserve optional-map membership |
| Existing disabled/customized optional entry | Preserve its values; fill only missing fields |
| Bay5 removed after revision 2 | Keep it absent on subsequent startup/reload |
| Future revision | Preserve that marker and membership |

Client matching remains exact-string matching, including ordering, whitespace
and case. Server matching compares the exact map-ID set before generic native
field repair runs. The old client boolean remains meaningful when the new
revision has not yet been written. The new revision is recorded even when a
custom list is preserved, preventing later generic repair from turning it into
a new automatic upgrade. Installed configs are never manually edited by the
development workflow.

## Offline checks

`tools/EquatorMapChecks/Verify.ps1` retains its existing entry point and success
sentinel and now verifies both optional maps. It runs the full Release server
suite against the installed database; native-menu, native-hook, map-view,
Labyrinth and actual BepInEx configuration harnesses; five compiled negative
controls; and the isolated native client builder. The server deploy target
points to a required nonexistent sentinel. The client builder uses the installed
Core API, overrides deployment targets and verifies installed/external hashes.

New coverage includes revision-1 upgrades and repeated removals, real saved
server reloads, real BepInEx `Settings.Init`/save/rebind, independent late
availability, wrong-map identity, no database/config mutation, no snapshot
recovery for unavailable maps, exact arm-token replay and single snapshots,
both generated wave zones, two simultaneous optional native rows, mode-change
deselection, strict stale requests, ordinary native controls and Bay5-only Arena
entry. Five fault controls remove the shared client guard, shared mode guard,
Bay5-only client guard, Bay5-owned row, and restore the old boolean migration
guard. Each must fail its specific new assertion rather than merely fail to
compile.

The initial complete oracle passed with zero native build errors and the four
existing nullable-context client warnings. Targeted checks passed 51 server
cases, 109 native-hook assertions and 168 real BepInEx configuration assertions.
This is offline evidence; the final clean commit/staged hashes and deployment
belong in the root integration receipt.

## Review and remaining integration

Implementation reread checked the old boolean/int migration boundary, exact
matching before generic repair, preservation of disabled map values, per-map
availability identity and the owned row lifecycle. Defect-oriented checks use
the previous Equator behavior as a negative control and test the two optional
rows together, so one functioning row cannot conceal the other's omission.
The existing profile snapshot and end-raid interception are unchanged.

Parent independent review is required before promotion. Root owns the companion
Bay5 spawn data and final clean build/deployment. After server and EFT restart,
verify native ARENA entry, the actual loaded identities, connected placement,
replenishment near the center, combat death/respawn and protected return.
Record Gun Game smoke separately from Deathmatch and do not substitute an
ordinary PMC/debug raid for ArenaMode acceptance.
