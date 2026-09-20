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

## Integrated GPU acceptance — 2026-09-20 23:02-23:18 CEST

The preceding candidate checkpoint is now complete. Clean source commit
`22ff21655c4620d429c5b4585074baa273b7ef9b` was built with the isolated client
builder and Release server build. Only the two ArenaMode DLLs were promoted;
123 other inventoried plugin/server/config files stayed unchanged. No installed
config was manually edited. MapPort `9547b0b` was deployed through its own
script, including ten bot/four player markers. Server and EFT were restarted.

| Deployed artifact | SHA256 |
|---|---|
| ArenaMode client | `3E9E1DE4B96402EECBE18FD029280C67F282C410576117713BF8A750DCDD2266` |
| ArenaMode server | `F0F33D562B9F5339B9E85A0B20E110B382FCD1EA72ED1DC98D7532C7BC920A0E` |
| Companion MapPort server | `73D51278422D0C814A0D4F2DF9D2ED76828CE82237BCF98B381F2371214C8D6B` |
| Companion Equator base.json | `8C2D5F007F52C6BA79FD2AE47B4304CFFEF55C76E413963675978585EF5D0830` |

GPU host `192.168.178.114:C:\SPT41`: SPT 4.1.6, EFT 0.16.9.5-40743,
Unity 2022.3.43f1. UiBridge instance `4a8bd804`, started 23:02:19, confirmed
all 25 maschine plugins and Redeploy Core identical across VM, host disk and
loaded assemblies. The map loaded ten NavMesh-valid bot markers, 98 patrol
subpoints and one connected ground component. MapPort's existing placement
repair shifts the south-west bot point 1.5 m to clear its booth bounds; the
resulting point passed an additional native reachability/standing-room probe.

Native ARENA menu selection showed Equator for Deathmatch and Gun Game.
Changing a selected Equator to Tactical hid/deselected it and removed NEXT;
Team DM also hid it. Switching back restored the tile. These checks used the
native mode buttons, not the ordinary EQUATOR DEBUG shortcut.

| Mode | Observed lifecycle |
|---|---|
| Deathmatch | Armed 23:05:43.994 (#2908), active 23:06:13.682 (#3019), round 1 began. From atrium (0, 0.2, 2), four legal distant spawn candidates supported two-PMC bursts at 23:06:39 and 23:06:42. Up to eight bots active. Natural deaths 23:07:14.941 and 23:07:39.542 respawned in 0.52/0.16 s with fresh kits and 440 HP. Further replenishment requested 23:08:01, dispatched 23:08:04. Stop(Left) 23:08:35, MainMenu about 23:09:22. |
| Gun Game | Armed 23:14:40.895 (#3885), active 23:14:57.483 (#3967), HUD tier 1/24 and PB pistol. Two-PMC atrium replenishment 23:15:53 (#4142-4149). Natural death 23:16:24.478 (#4179), respawn 0.51 s with 440 HP and unchanged PB rung. Stop(Left) 23:16:47.869 (#4227), MainMenu about 23:17:21. |

### Profile return evidence

Independent read-only comparison and parent recheck compared the complete saved
profile JSON before/after both raids. Profile `6a9fc7d99347b1276407f8ad` backups
under `SPT_Runtime/user/profiles/backups/2026-09-20_21-05-44`, `_21-08-36`
and `_21-16-49`, the pre-Gun-Game copy and fresh live profile after MainMenu
match except `/characters/pmc/Hideout/sptUpdateLastRunTimestamp`. Removing
only that background field and serializing sorted compact UTF-8 JSON yields
SHA256 `7f5c0e9da100ef9dfa8f785c20b37f91377e85c94604edc2e6a20c1bbc2fde37`
for every snapshot. Health 440, level 2/1500 XP, 446169 roubles, 149 inventory
entries and all skills, quests and PMC/Scav counters were preserved. The UI's
55 raids/32 AWOL sum PMC 54/31 and Scav 1/1.

The client `raid-end` response was initially deferred in state `InRaid` before
native Stop finished. That intermediate response does not prove a failed or
completed restore. Fresh persisted profile backups establish the final result;
the mirrored server log was buffered and not used as fresh restore evidence.

### Limits

This is short integration acceptance, not long-session/performance acceptance.
Gun Game was tested at tier 1; no kill-driven rung change or completed ladder.
Tactical/Team DM are intentionally unavailable on Equator; PitFireTeam rejection
was checked offline. Existing two-second fast deploy and ExitStatus screen
remain. EQUATOR DEBUG retains its separate ordinary-raid shortcut behavior.
The installed `RespawnApiProbe.AlwaysThrow(8.5)` diagnostic threw as designed
and was caught during body replacement; existing PerfectCulling cleanup warnings
also appeared. No ArenaMode/MapPort blocker occurred, but this is not a claim
that the entire installed mod stack logged no warnings.
