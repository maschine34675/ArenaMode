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

## Native ArenaMode acceptance — 2026-09-21, 07:38-07:45 CEST

The preceding implementation checkpoint has now passed native Arena lifecycle
acceptance. Clean ArenaMode source `75a7430e6b82617a21ec9c7b0ee4527d16d6826f`
was independently reviewed, built, deployed and loaded on the restarted SPT
4.1.6 host (`192.168.178.114:C:\SPT41`, EFT 0.16.9.5-40743, Unity 2022.3.43f1).
UiBridge instance `aed4e141`, started 07:17:51, reported all 25 maschine plugins
and Redeploy Core identical between VM, host disk and loaded assemblies.

| Artifact used for this acceptance | SHA256 |
|---|---|
| ArenaMode client, `75a7430` | `37C8D08D8B1D47846FD09A4856F7A9BD5DC23902B2C4BF6BECF06DBDCA801E27` |
| ArenaMode server, `75a7430` | `B87F965583766E6C0E9A62476AA08E433E8419B6B58047F7C0D763495BC78F29` |
| Companion MapPort client, `3a6fc42` | `DD2F77DEB5A394A77B7A2FFD312F914C98615FB8F0F035ADA57F01808C20601C` |

The ArenaMode deployment receipt records two promoted DLLs and 124 other
inventoried files unchanged, with no manual config edits. Redeploy Core was
unchanged. No further ArenaMode code changes were needed during the host tests.
The clean combined oracle passed **691 server tests**, with one optional APBS
skip; native menu 456, native hooks 109, map view 62, Labyrinth 50 and real
BepInEx config 168 assertions; all five fault controls rejected their intended
defects. Native client build: zero errors and four existing nullable warnings.

### Native menu and raid observations

The real ARENA `SelectLocation` screen showed both **Bay5 and Equator** for
Deathmatch and Gun Game. Both rows were absent for Tactical and Team Deathmatch.
PitFireTeam rejection remains covered offline. These were native ARENA raids,
not the ordinary EQUATOR DEBUG shortcut.

| Mode | Observed lifecycle |
|---|---|
| Deathmatch | Arena session active 07:38:22.785; round 1 started 07:38:24.006. Combat death 07:39:08.405 followed by automatic respawn in 0.63 s with full health. At 07:39:23.899 the spawn plan selected two points from six preferred/three distant candidates; two pmcUSEC arrived and were dispatched by 07:39:23.947. Protected session end 07:40:29.695, followed by return to the menu. |
| Gun Game | Arena session active 07:42:11.420; Gun Game match started 07:42:11.460 at tier 1/24. Combat death at 07:43:42 followed by automatic respawn in 0.62 s with full health, still at tier 1/24. Leave requested 07:44:06.253; protected session end 07:44:07.391, followed by return to the menu. |

### Profile restoration

The root captured the complete persisted profile before Arena, after Deathmatch
and after Gun Game's completed persistence. The only removed field was
`/characters/pmc/Hideout/sptUpdateLastRunTimestamp`. Sorted compact UTF-8 JSON
for all three accepted snapshots has the same SHA256:

`eda7e20adae0fe7a7a62ae3102e0f5cfbb3b0168ed0f89002fe68660855ac3a7`

An independent reread confirmed that exact hash for `arena-before.profile.json`,
`arena-after-dm.profile.json` and `arena-after-gg-flushed.profile.json`. The
earlier `arena-after-gg.profile.json` was read before the final synchronized
write and still differed under `/characters/pmc/Skills/Common`. It is retained
as intermediate evidence and is **not** the accepted post-raid snapshot. After
the write completed, the entire normalized profile matched, covering inventory,
health, skills and counters rather than only a few selected fields.

### Evidence and limits

Local evidence is under `D:\.unlazy\bay5-reflections\`:

- `arena-deployment.json` and `arena-clean-verify.txt`: exact ArenaMode source,
  promotion and offline checks.
- `mapport-final-artifacts.json`: companion MapPort artifacts used by this
  historical test checkpoint; its filename is not a claim about later builds.
- `native-aed4e141-evidence.json`: loaded identity, native menu filters and
  placement probes.
- `native-aed4e141.log`: full synchronized client log through both Arena raids.
- The three accepted profile snapshots named above and `profile_check.py`:
  canonicalization and protected-return evidence.

Gun Game was a tier-1 smoke test; no complete ladder or advancement test was
run. Movement included coordinate-assisted probes and test positioning, so this
is not an exhaustive walk-through of every Bay5 passage. The companion MapPort
build exposed separate NavMesh ghost spawn-box/north-exit and Equator donor
startup issues. Root is addressing those separately and must validate the final
MapPort build after deployment. This checkpoint establishes the ArenaMode
integration's native behavior; it does not certify those later map or rendering
changes.
