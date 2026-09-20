# Optional Equator map integration

Date: 2026-09-20. Base: `649a773`; branch: `feature/equator-map-integration`.

## Contract

Equator (`arena_equator`) from ArenaMapPort is available through the native Arena
choice for **Deathmatch and Gun Game**. Tactical, Team Deathmatch and PitFireTeam
are rejected by the shared area contract at menu selection, final request,
server arm/replay and location preparation. No team starts have been certified.

The client requires the loaded `com.maschine.ArenaMapPort` plugin. The server
requires an enabled Equator location with the matching ID in its live
`LocationTable`. The table lookup happens per request after database loading,
not at the early patch-manager startup. Missing data and lookup failure hide
Equator from the detached client config response and reject an arm before
snapshot recovery or preparation. No hard package dependency is introduced.

The existing owned conditional map-row mechanism now tracks its map ID, covering
both Customs and Equator. Mode changes hide and deselect incompatible rows;
direct clicks and final start remain strictly validated. No native screen is
rebuilt. The area hint lists only the two admitted modes. Ordinary native
selection and the original shared database are unchanged.

Equator defaults use its authored **`BotZone_Main`**, eight bots maximum and a
four-alive target for replenishment. The existing two-bot burst size and 25m
minimum spawn distance remain unchanged. ArenaMapPort owns the actual ground
and perimeter spawn points; the companion map data change is required for
useful replenishment near the center (the reviewed companion data contains ten
bot markers and four player markers). A zone string alone is not navigation
proof. No loot, Scav raids, kits, score rules, death handling, normal raid hooks
or either profile-protection layer are changed here.

## Persisted settings

- A client list upgrades only when it exactly equals one of the three historical
  default strings. `General/OptionalMapsMigrated` records that decision once,
  including when a customized list was preserved. Subsequent removal stays removed.
- A server config gains Equator only when its map-ID set exactly equals a
  historical native default set and `optionalMapsRevision` is absent. Existing
  map values stay unchanged. The new revision is recorded even for a customized
  set; generic missing-field repair cannot add Equator on a later reload.
- An existing Equator `enabled=false`, edited zone/population or an explicit
  missing map after migration remains authoritative. No installed config is
  manually rewritten by the development workflow.

## Offline evidence

`tools/EquatorMapChecks/Verify.ps1` runs Release server tests against the installed
database, production-linked menu/native-hook/map-view/Labyrinth regressions,
two compiled fault controls, and the established isolated native client builder.
Server builds point `SPTUserMods` at a required nonexistent sentinel. The client
builder redirects staging, overrides deploy targets and checks installed hashes.
There is no generic `DeployToSpt=false` flag in the real targets.

The combined oracle passed on 2026-09-20: **662 server tests passed**, one optional
installed-APBS test skipped; menu 456, native area hooks 90, map-view 60 and
Labyrinth-access 50 assertions passed. Both compiled fault controls were rejected
at their intended assertions. Native Release client: zero errors, four existing
nullable-context warnings. Installed client/server/Core hashes were unchanged.
The test discovery also reports one existing duplicate area-team case ID.

The new tests cover exact and customized migrations over repeated reloads,
dependency absence/disabled/wrong identity/failure, no stale-snapshot recovery on
unavailable maps, one snapshot and exact-token replay on accepted requests,
mode rejection, exact generated wave zones, native mode transitions, stale map
selection, server disable and an ordinary UI control. Fault controls remove the
client plugin and map-mode guards from copied sources; each must fail its named
new assertion. These are not native rendering, navigation or profile-return
acceptance evidence.

## Review passes and runtime work

Implementation pass is complete. Expert reread traced both catalog consumers,
server admission order, transport-copy ownership and optional migration reloads.
Defect hunt added the one-time migration markers (otherwise deleting Equator
would recreate an old default), generalized conditional rows (otherwise mode
switching would leave an invalid tile visible), and included the existing
Labyrinth harness because it compiles the modified menu source. Polish names
the two supported modes and distinguishes the population target from burst size.
Independent adversarial review completed against `649a773` with no open P0-P2
finding. It traced exact default migration and persistent removal, detached
config transport, late live lookup, start-time refresh/revalidation, plugin
identity, mode-switch deselection and the unchanged profile transaction. Root
accepted that review before commit. No native raid result is implied.

Root integration must record the final verified build identities, deploy the
matching MapPort data and both ArenaMode halves, restart server and EFT, then
test actual Arena admission, replenishment from the center, death/respawn and
protected return. The separate EQUATOR DEBUG button starts an ordinary PMC raid
and cannot certify this integration. Gun Game and team-mode rejection should be
recorded separately from the first Deathmatch test.
