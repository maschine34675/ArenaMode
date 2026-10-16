# ArenaMode

Arena matches against AI for **SPT 4.1**. ArenaMode adds an **ARENA** option next
to PMC and Scav, with mode, map, equipment and mutator settings in the normal
raid setup screens. It is a single-player mod; **Fika is not supported**.

Current source version: **0.1.0 (development)**. Release packaging is still being
prepared. Tactical, Zombies, Akimbo and Molotov are marked **WIP**.

## Modes

| Mode | Rules |
|---|---|
| **Gun Game** | Each kill advances your weapon. The ladder repeats after the last weapon. Respawn at your current stage. |
| **Deathmatch** | Free-for-all or all bots against the player. Optional kill/time limits per round and a finite or unlimited number of rounds. Without limits, play continues without round breaks. |
| **Team DM** | USEC against BEAR, with continuous respawns and team kill/time limits. Does not require PitFireTeam. |
| **Tactical (WIP)** | Round-based elimination. Solo defaults to 1v5 with a kit buy menu. With PitFireTeam, bring your own gear and 1–4 companions against five enemies. Watch surviving teammates after death; return next round. |
| **Zombies (WIP)** | Increasing waves of infected. Solo death ends the match. With PitFireTeam, surviving teammates can finish the wave; fallen members return for the next wave. A team wipe ends the match. Ammunition is replenished between waves. |

Zombie defaults are **32 alive at once, 50 in the first wave, +20 per wave** and
**own gear**. Population and starting equipment are selectable in the menu.
Protected PFT squads use their entry gear, restored between rounds/waves,
including the surviving player in Zombies.

## Maps

| Location | Available modes |
|---|---|
| Factory, day and night | All five modes |
| Labs / Labyrinth | All five modes |
| Customs: Factory Center, Factory Side, Customs Yard | All five modes, within the selected boundary; no whole-map Customs option |
| Bay5, with ArenaMapPort | All five modes |
| Equator, with ArenaMapPort | Gun Game, Deathmatch, Team DM and solo Zombies; no Tactical or PFT Zombies |

Deathmatch and Gun Game let you select the participant total, including yourself.
Team DM offers team sizes within the selected map/area's limits. Customs areas
have separate team starts and a visible boundary when approached.

## Features and mutators

- Scoreboard, damage assists, readable killfeed, hitmarkers, damage numbers,
  health/armor and weapon panels. Friendly bots have an overhead marker.
- Announcer cues for kills, streaks and match events. Announcer and hitmarker
  volume have their own controls; announcer volume is independent of Interface
  volume by default.
- Arena kits and a generated weapon ladder, including optics and other
  attachments. Own-gear options depend on the mode.
- Faster movement, double jump, timed bunnyhops, air control, sliding, airborne
  hip fire and vaulting within reach. The movement master switch disables the
  group; individual switches remain available.
- Faster handling, reloading, magazine loading and healing; backpack item and
  magazine access, automatic medical quickslots and instant search.
- Team-colored tracers and lasers, configurable friendly fire, and independent
  bot movement settings. Friendly fire defaults off for PFT Zombies, on for Team
  DM and PFT Tactical.
- **Factory day/night, Deathmatch and Team DM:** rotating pickups for health,
  temporary shield, double damage, jump boots, unlimited ammunition or an M60.
  Built-in pickup layouts are not provided for the other maps. BigBrain
  integration lets bots deliberately seek health, shield and damage pickups.
- **Akimbo (WIP):** a second pistol with alternating fire. **Molotov (WIP):** an
  extra fire bottle, without replacing normal grenades; requires the BigBrain
  integration and is unavailable in Zombies.
- Optional KillcamReplay integration. Team spectating works separately from
  recorded killcam replays.

Most gameplay switches are in the map-screen **Mutators** panel. Match rules
are fixed when entering a raid; some advanced preferences are available during
play. Normal PMC/Scav raids are not intended to use Arena's combat rules.

## Installation

Use a matching ArenaMode client/server release for **SPT 4.1.x**. Native testing
has used SPT 4.1.6; other SPT major/minor versions are not supported by this build.

1. Back up your SPT profile and existing Arena configuration. Close the game and
   server before replacing files.
2. Extract the release into the SPT installation containing
   `EscapeFromTarkov.exe`, preserving its folder structure. Do not install the
   GitHub source archive as a mod.
3. Check that both halves, the assets and the matching shared Core are present:

```text
SPT installation/
  BepInEx/plugins/
    maschine-ArenaMode.Client.dll
    maschine-Redeploy.Core.dll
    ArenaMode/                         audio, map previews and license notices
  SPT_Runtime/user/mods/ArenaMode/
    maschine-ArenaMode.Server.dll
    config/
      kits.json
      ladder.json
```

`maschine-Redeploy.Core.dll` is a required shared library, not the full Redeploy
mod. Use the version supplied for the Arena build, keeping only one copy in the
plugin tree. The current source builds against Core **1.0.7**. An older installed
Arena build may have been compiled against a different Core version.

The optional `maschine-ArenaMode.BigBrainCompat.dll` belongs next to the client
when using **BigBrain 1.5.0**. Other mods and their dependencies are installed
separately. Preserve the package's documentation and license notices.

4. Start the SPT server, then the game. Select **ARENA**, choose the mode, map,
   area and mutators, then start the raid. For a PFT squad, invite **1–4
   companions** before starting Tactical or Zombies.

Arena uses profile snapshots and a protected exit path to restore entry gear
and discard Arena loot, XP and quest changes. This is not a guarantee against
crashes or conflicting mods; keep a profile backup, especially while testing
WIP features. Leaving through the game's raid menu is supported.

## Controls and configuration

| Default control | Action |
|---|---|
| **F9** | Scoreboard and live Arena settings |
| **Backspace** | Give up the current life; counts as a death, and ends solo Zombies |
| **Escape** | Raid menu and leave match |
| **Left/right mouse button** while team spectating | Cycle living targets: left = next, right = previous; enemies can also be observed |
| Native jump / crouch bindings | Movement features; crouch while sprinting to slide |
| **F12**, with a BepInEx configuration UI installed | Advanced Arena settings |

- Client configuration: `BepInEx/config/com.maschine.ArenaMode.cfg`.
- Server configuration: `SPT_Runtime/user/mods/ArenaMode/config/`. The server
  creates `config.json` and `client-settings.json`; kits and ladder use the
  supplied JSON files.
- Server configuration page: the SPT server's HTTPS address plus `/arenamode`,
  normally `https://127.0.0.1:6969/arenamode`. The server must be running.
- Restart the game after changing DLLs/audio; restart the server after changing
  its DLL or the kit/ladder catalogs. Preserve customized configuration when
  updating.

## Optional mods and compatibility

These are implemented integration targets, not a claim that every combination
or future version has been tested. Optional mods retain their own prerequisites.

| Mod | Integration target / purpose |
|---|---|
| **PitFireTeam** client and server | Exactly **0.10.0 or 0.10.3**; 1–4 companions for Tactical and Zombies. Not required for independent Team DM. |
| **SAIN** | **4.5.1** adapters for personalities, pursuit and movement. Native AI remains available without it. |
| **BigBrain** | Exactly **1.5.0**, with ArenaMode's own companion DLL: bot pickup objectives, post-death pursuit and Molotov fire avoidance. |
| **APBS** | **2.4.1** equipment/progression adapter. Not required for kit or bot generation. |
| **ArenaMapPort** | Optional Bay5/Equator client, server and map assets. |
| **KillcamReplay** | Compatible API and enabled DeathReplay required for replays; shortened Arena live tail supported from **0.9.13**. |
| **DynamicMaps** | Corpse-marker cleanup adapter for **1.2.1**; not required for Arena's static Customs preview. |

UIFixes' alternate reload path is handled. Arena includes its own relevant
RaidInfoPanels and CombatSlide functionality; neither is a required separate
download. **Fika is blocked**, rather than merely untested. See
[the dependency inventory](docs/DEPENDENCIES.md) for more specific boundaries.

## Known limitations and reporting problems

- Tactical, Zombies, Akimbo and Molotov are unfinished features. Large populations
  depend heavily on the map, hardware and other AI mods; 32 zombies is a default,
  not a performance guarantee.
- Some kit/weapon combinations cannot fit large spare magazines or medical items.
  M60 spare boxes and the Enforcer kit's Grizzly are recorded examples.
- Bots still rely on EFT navigation. A bounded arena does not add vaulting or
  create missing cover on a map.
- For a report, include the Arena/SPT versions, map/area, mode, optional mods and
  the time of the problem. Client log: `BepInEx/LogOutput.log`; server logs:
  `SPT_Runtime/user/logs/spt/`. Remove tokens, account identifiers and other
  private information before sharing logs. Do not upload your full profile.

To uninstall, close the game/server and remove only ArenaMode's DLLs, its
`BepInEx/plugins/ArenaMode/` assets and its server mod folder. Back up customized
files first. Keep Redeploy.Core if another installed mod uses it. Do not remove
the BepInEx or SPT user directories themselves.

## Building from source

Windows, the **.NET 10 SDK**, an installed **SPT 4.1** game and NuGet restore are
required. The client targets netstandard2.1; the server targets net10.0. Game
assemblies must come from your own installation.

The source also requires a sibling `../Redeploy` checkout containing
`Redeploy.Core/`, its `CoreVersion.props`, and `drivers/CoreVersionGate.cs`.
This checkout is not self-contained; the matching Core source must be made
available alongside a public Arena release. Do not substitute an arbitrary Core
DLL for the source dependency.

**Ordinary builds automatically deploy into the configured SPT installation.**
Use a development installation and close its game/server first. Pass its paths
explicitly to every command, including the referenced Core project. Replace
the example paths below with your development installation:

```powershell
dotnet build ArenaMode.Client/ArenaMode.Client.csproj -c Release "-p:SPTPath=C:\SPT41" "-p:SPTUserPath=C:\SPT41\SPT_Runtime"
dotnet build ArenaMode.Server/ArenaMode.Server.csproj -c Release "-p:SPTPath=C:\SPT41" "-p:SPTUserPath=C:\SPT41\SPT_Runtime"
dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release "-p:SPTPath=C:\SPT41" "-p:SPTUserPath=C:\SPT41\SPT_Runtime"
```

The test command also builds the server project and can deploy it; the explicit
paths keep all three commands on the same development installation.

For the optional companion, install BigBrain 1.5.0 in that development
installation and build `ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj`
with the same properties. A full `ArenaMode.sln` build includes that project,
so it also needs the BigBrain reference.

Client code needs native game tests in addition to offline checks. Build output
is **not** a release package: do not ship copied game/Unity/SPT/BepInEx assemblies,
other mods, test tooling, local settings or profiles. The release archive must
include the kit/ladder defaults, assets and their notices. See
[development documentation](docs/README.md) for feature reviews and dated tests.

## Licenses and credits

Original ArenaMode code and documentation are under the [MIT License](LICENSE).
**This does not license every bundled asset under MIT.** The
[license and source overview](THIRD-PARTY-NOTICES.md) covers Drexira's adapted
DragonDen code, game-icons.net artwork, the Customs map, audio and Redeploy.Core.
The Customs artwork is **CC BY-NC-SA 4.0**; announcer recordings have separate
terms and are not an unrestricted reusable voice pack.

ArenaMode is an unofficial mod, not affiliated with or endorsed by Battlestate
Games. EFT game content and trademarks remain the property of their owners.
