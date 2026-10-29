# ArenaMode

**0.4.0 for SPT 4.1.x** — fast arena matches against AI, with instant respawns,
equipment kits and six modes. Select **ARENA** beside PMC and Scav. Single-player;
**Fika is not supported**.

[Download](https://github.com/maschine34675/ArenaMode/releases/tag/v0.4.0) ·
[Changelog](CHANGELOG.md) · [Release notes](docs/RELEASE-0.4.0.md) ·
[Full feature inventory](docs/FEATURES.md)

## Modes

| Mode | Play |
|---|---|
| **Team DM** | USEC vs BEAR, continuous respawns and team kill/time limits. Default for new installations. |
| **Domination (WIP)** | Capture and hold control points. Separate point/time limits, continuous team respawns and bot capture/defence assignments. Requires BigBrain and the bundled Arena companion. |
| **Gun Game** | Each kill advances your weapon; the ladder repeats after its final stage. Losing a stage on death is optional. |
| **Deathmatch** | Free-for-all or all bots against you, optional kill/time limits, finite or unlimited rounds. |
| **Tactical (WIP)** | Elimination rounds with a kit buy phase. Optional PitFireTeam squad play with 1–4 companions. |
| **Zombies (WIP)** | Increasing infected waves, ammunition refills and own gear or supplied kits. Optional PitFireTeam squad survival. |

Choose USEC or BEAR for Team DM and Domination. Team colors and the killfeed
follow your selection; your saved PMC faction stays unchanged.

## Maps

- Factory day/night, Labs, Labyrinth and three bounded Customs areas support
  Gun Game, Deathmatch, Team DM, Tactical and Zombies.
- [ArenaMapPort](https://github.com/maschine34675/ArenaMapPort/releases/latest)
  adds Equator, Bay5, Air Pit and Bowl. It requires your own supported Arena
  installation. Equator excludes Tactical and PitFireTeam Zombies.
- Domination supports Factory day/night and all four ported maps. Factory picks
  three separated points each match; Equator/Bay5 use one original point each,
  Air Pit/Bowl two. Labs, Labyrinth and Customs do not offer Domination.

## Features

- Faster movement/handling, double jump, bunnyhops, sliding and configurable
  stamina, medical and reload speeds.
- Twenty kits, a generated weapon ladder, backpack item/reload access and
  configurable player spawn protection.
- Health, shield, damage, movement, ammunition and weapon pickups; bots can seek
  useful pickups with BigBrain.
- Scoreboard, killfeed, health/armor/ammo panels, hitmarkers, damage numbers and
  announcer audio. Domination objectives share the main score panel.
- Optional Akimbo and Molotov mutators, both WIP. Molotov requires BigBrain;
  neither is available in Zombies.
- Optional integrations for AI, maps, replay and body/camera replacement.
  [Reviewed versions and limits](docs/DEPENDENCIES.md).

Gameplay settings and the compact mutator controls are in the Arena menus;
advanced options remain in F12. Existing saved choices are preserved. Arena
match HUD and session indicators stay within their own Arena contexts.

## Install or update

1. Close EFT and SPT. Back up your profile and customized Arena files.
2. Extract **ArenaMode-0.4.0.zip** into the SPT installation root, merging
   `BepInEx/` and `SPT_Runtime/`.
3. Keep one copy of each Arena DLL and **Redeploy.Core 1.0.17**. This shared
   library is included; the standalone Redeploy mod is optional. Redeploy 1.1.0
   ships the same Core. Never replace a newer Core with an older copy.
4. Start SPT and EFT, then select **ARENA**, or use the direct start below.

The Arena client, companion and helper belong in
`BepInEx/plugins/maschine-ArenaMode/`; the gate belongs in
`BepInEx/patchers/maschine-ArenaMode/`; the server belongs in
`SPT_Runtime/user/mods/ArenaMode/`. Core is directly under `BepInEx/plugins/`.

When upgrading a much older installation, remove duplicate flat Arena DLLs
from `BepInEx/plugins/`. Merge the former `BepInEx/plugins/ArenaMode/` folder
into `maschine-ArenaMode/`, preserving custom pickups/drafts. Preserve customized
`kits.json` and `ladder.json` before overwriting their supplied defaults. User
settings and profiles are not included in the ZIP.

BigBrain **1.5.0** is the reviewed baseline for Domination, directed bot pickup
pursuit and Molotov. Install BigBrain and its own prerequisites separately.
Arena's bundled companion remains inactive when BigBrain is absent. Other
optional mods and ported maps are separate downloads.

## Arena sessions and direct start

If your normal mod set prevents Arena play, **START ARENA SESSION** restarts EFT
with the allowed plugins and a private local server/profile copy. The supported
local normal server saves and stops first. **RETURN TO SPT** restores the normal
server/game launch. Closing EFT also asks the helper to stop the private server.

You can start without opening normal EFT first: run
`BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe`, select a
profile and choose **START ARENA**. The picker can create a desktop shortcut.
A brief normal-server start followed by the private-server start is expected:
normal SPT prepares and saves the profile before transfer, even with rewards off.
The direct helper requires the local SPT installation; it does not manage a
server running on another machine. [Direct-start details](docs/DIRECT-START-20261028.md).

The shipped `arena-whitelist.json` records reviewed mods and versions. Recognized
versions that differ are still attempted in direct play and new private sessions;
the tooltip/log advisory does not exclude them. Identity, dependencies, deny
rules and mandatory protection/API checks still apply.

Put personal allow/deny choices in
`BepInEx/config/com.maschine.ArenaMode.whitelist.json`; updates preserve it.
`"unlisted": "allow"` permits unlisted mods except explicit denies. The shipped
denies are ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces and
Aishi. An explicit user allow can lift a default deny. Whitelisting is not a
compatibility guarantee. [Policy details](docs/ARENA-SESSION-20261021.md).

The helper log is `%LOCALAPPDATA%\maschine-ArenaMode\session\helper.log`.
Routine link details require `ARENAMODE_SESSION_DEBUG=1`; failures and lifecycle
events remain visible. [Logging](docs/LOGGING.md).

## Profile and rewards

Arena restores your entry profile after each protected raid. Temporary kits,
loot and ordinary raid progression are discarded. Use the supported raid exit
or session return controls.

Optional **PROFILE REWARDS** default on for eligible newly registered profiles;
existing OFF choices stay off. Eligible kills award **1,000 RUB + 100 PMC XP**;
DM/TDM/Tactical wins add **10,000 RUB + 1,000 XP**, capped at **200,000 RUB +
20,000 XP per match**. Gun Game/Zombies award kills only. Domination currently
has no persistent rewards. Private-session return processes accepted rewards
before normal EFT opens; direct play pays at the next normal server start.
RUB arrives by mail; XP is already credited and is not granted again on collection.

Uncertain old reward records are automatically discarded during normal startup.
This does not reset the normal profile. Recovery also handles stopped sessions
whose disposable data went missing. [Reward details](docs/REWARDS-RUNTIME.md) ·
[Stopped-session recovery](docs/OFFLINE-STARTUP-RECOVERY-20261028.md).

Tarkova Cola **2.2.1+**, installed on both sides, can additionally transfer eligible
Zombie progress and newly earned achievements through the same reward switch.
[Scope](docs/TARKOVA-COLA-COMPAT.md).

## Verification and known limits

The [29 October native sweep](docs/RELEASE-SWEEP-20261029.md) exercised all six
modes, all four ported maps, direct entry/return and standalone Redeploy on two
hosts. It covers representative short scenarios, not every mod combination,
full Tactical match, Gun Game ladder or long Zombie session.

A single native BotFirstAid exception occurred at one round end without a hang;
its exact cause remains unresolved and it did not recur in the final check.
Ported-map missing Books impact sounds and intermittent audio noise are recorded
in ArenaMapPort's limits. WIP features remain WIP. See the release page for
final package hashes and any hash-specific scanner results; older scans do not
describe new binaries.

## License

Code is MIT. Audio, artwork and bundled notices have their own terms; retain
`docs/licenses/` with the installed package. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

