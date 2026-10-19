# ArenaMode

Arena matches against AI for **SPT 4.1**. ArenaMode adds an **ARENA** option next
to PMC and Scav, with mode, map, equipment and mutator settings in the normal
raid setup screens. It is a single-player mod; **Fika is not supported**.

Current version: **0.1.0**. Download the installable ZIP from
[GitHub Releases](https://github.com/maschine34675/ArenaMode/releases).
Tactical, Zombies, Akimbo and Molotov are marked **WIP**.

## Modes

| Mode | Rules |
|---|---|
| **Gun Game** | Each kill advances your weapon. The ladder repeats after the last weapon. Respawn at your current stage. |
| **Deathmatch** | Free-for-all or all bots against the player. Optional kill/time limits per round and a finite or unlimited number of rounds. Without limits, play continues without round breaks. |
| **Team DM** | USEC against BEAR, with continuous respawns and team kill/time limits. Does not require PitFireTeam. |
| **Tactical (WIP)** | Round-based elimination. Solo defaults to 1v5 with a kit buy menu. With PitFireTeam, choose own gear or a supplied PMC kit and bring 1–4 companions against five enemies. Companions keep their gear. Watch surviving teammates after death; return next round. |
| **Zombies (WIP)** | Increasing waves of infected. Solo death ends the match. With PitFireTeam, surviving teammates can finish the wave; fallen members return for the next wave. A team wipe ends the match. Ammunition is replenished between waves. |

Zombie defaults are **32 alive at once, 50 in the first wave, +20 per wave** and
**own gear**. Population and starting equipment are selectable in the menu.
PFT companions keep their entry gear. A dead Tactical PMC receives the selected entry loadout again next round;
a surviving Tactical PMC keeps the current loadout. Zombies refills the combat
loadout between cleared waves, including for the surviving player.

## Maps

| Location | Available modes |
|---|---|
| Factory, day and night | All five modes |
| Labs / Labyrinth | All five modes |
| Customs: Factory Center, Factory Side, Customs Yard | All five modes, within the selected boundary; no whole-map Customs option |
| Bay5 / Air Pit, with ArenaMapPort | All five modes |
| Equator, with ArenaMapPort | Gun Game, Deathmatch, Team DM and solo Zombies; no Tactical or PFT Zombies |

Deathmatch and Gun Game let you select the participant total, including yourself.
Team DM offers team sizes within the selected map/area's limits. Customs areas
have separate team starts and a visible boundary when approached.
**Air Pit Team DM requires ArenaMapPort 0.2.2 or newer** for its team-start boxes.

## Features and mutators

The [complete feature inventory](docs/FEATURES.md) also covers smaller changes,
current defaults, mode restrictions, all mutators and the supporting source.

### Movement and handling

- Faster walk/sprint, less inertia and weight slowdown, quicker strafing/leaning,
  faster ADS and direct sprint-to-aim transitions.
- Double jump, including after stepping off an edge; timed bunnyhops, air
  control, sliding, airborne hip fire and vaulting within reach.
- Higher jumps, faster vaulting, optional unlimited stamina, no bush slowdown
  and no fall damage; hard-landing slowdown lasts half as long by default.
- **Walk while using CMS or Surv12**, at up to **1 m/s**. Requires Fast Movement;
  surgery still blocks sprint, jump, prone and vaulting.
- Reduced broken/blacked-leg walking penalties, with a separate control from
  Fast Movement. Injuries still need treatment.
- Faster drawing, holstering and reloading, plus separate medical-use and
  magazine-packing speeds. The movement master disables its bonuses and
  abilities without deleting the individual preferences.

### Equipment and combat

- Twenty kits, fixed/random kit selection and a generated weapon ladder with
  compatible optics/attachments. Solo Zombies and PFT Tactical offer own gear
  or kits for the PMC. PFT Tactical defaults to own gear; its kit is issued once
  before entry, without a buy phase. PFT Zombies keeps own gear automatically.
- Supported weapons select full-auto when drawn; manual changes remain possible.
- Backpack medical-item use and magazine reload access, stable medical
  quickslots, instant search and automatic searching of opened corpses.
- Two RGD-5s in supplied kits where space permits, temporary elite skills/mastery,
  stronger bleeding and configurable spawn protection.
- **Akimbo (WIP):** eligible pistols alternate one shot per trigger, with doubled
  cadence and two shots per cartridge. Shared reload, hip fire only (no ADS);
  off by default and unavailable in Zombies.
- **Molotov (WIP):** an extra dedicated fire bottle in free rig/pocket space,
  initially preferred by G, without converting normal grenades. Requires the
  BigBrain companion and is unavailable in Zombies.

### Pickups and bots

- **Every supported Arena map, Deathmatch and Team DM:** rotating health, shield,
  double-damage, jump-boots, unlimited-ammo and M60 pickups. Factory keeps its
  eight reviewed spots; other maps use checked native positions, within the
  selected Customs area where applicable. Ammo and shield effects have
  weapon/damage-type limits detailed in the feature inventory.
- Colored pickup beams up to six metres, collection sound and active-effect
  timers. The M60 transfers into a free compatible inactive weapon slot;
  it is not drawn for you.
- Bots can collect health/shield/damage pickups; BigBrain integration lets them
  deliberately seek useful powerups.
- Independent bot movement/agility and reduced weight penalties, more active
  pursuit, and optional exclusion of passive SAIN personalities.
- Fixed participant/faction slots, including a retained Labyrinth boss slot.
  Difficulty pressure and bot gear progression are separate; timed Team DM
  scales both time curves to match duration.
- Team DM bots starting in the fixed team boxes on Equator, Bay5 and Air Pit
  receive visible spawn protection, default four seconds from activation in
  live combat. Firing or throwing a grenade ends their shield. Other maps do
  not grant bot spawn protection; player protection is unchanged.
- Protected/buffed bots have a narrow body contour in the effect's color,
  hidden behind opaque cover. Simultaneous effects share a segmented contour.
- Zombie voice budgeting and optional Zombie Crowd behavior. Old-corpse and
  resource-pool cleanup reduce accumulation during long matches.

### HUD, teams and audio

- Match scoreboard with kills/deaths/assists/headshots/points and team totals.
  Damage-share assists add points without reducing the killer's reward.
- Left-side killfeed with names, distances, team colors, weapon/cause icons and
  own environmental deaths. Score-panel and killfeed offsets are configurable.
- Hitmarkers, a kill skull above the crosshair and optional combined burst
  damage numbers, with separate body/armor damage.
- Health, armor/helmet durability, injury and weight panels; weapon/fire-mode,
  chamber/ammunition, spare-magazine and grenade information. Armored rigs count.
- Friendly diamonds, team-colored tracers even for tracerless ammunition,
  team laser colors and separate point/beam brightness controls.
- Friendly fire defaults off for PFT Zombies, on for Team DM and PFT Tactical.
  Solo modes do not use the team tracer/laser options.
- Multikill, spree, First Blood, single-hit Headshot, Payback, Shutdown, Airborne,
  No Scope, last-survivor/clutch and match-progress announcements.
- Independent announcer/hitmarker/pickup volume. Announcer volume bypasses the
  Interface slider by default; overall game volume still applies.

### Maps, replay and quality of life

- Polygon Customs areas with in-area starts and approaching-boundary grids;
  unsafe Labs gate and Factory courtyard bot starts are excluded.
- Labyrinth access without EnableLabyrinth, disabled entrance traps and drained
  toxic pool, optional center-alarm muting and a 10-second Way Up countdown.
  Not every environmental hazard is removed.
- Round-state lighting on every supported Arena map, including the ports,
  without editing the maps.
- Killcam support in every mode, with respawn delayed until camera cleanup;
  separate PFT third-person spectating with the observed bot's HP.
- Death-related concussion effects are cleared from spectator/replay views;
  Escape remains available to leave the match.
- Faster raid setup; suppressed extraction-list clutter and quest/achievement/XP
  toasts. Misleading lost-item exit warnings are hidden and the XP result page
  is skipped while the required native result flow remains intact.
- Area/pickup drafting tools for development, including an explicitly enabled
  ordinary-PMC surveying mode; drafts do not automatically become live layouts.

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
    maschine-Redeploy.Core.dll
    maschine-ArenaMode/
      maschine-ArenaMode.Client.dll
      maschine-ArenaMode.BigBrainCompat.dll
      announcements/                  announcer OGGs
      sounds/                         hitmarker and pickup WAVs
      docs/                           documentation and manifest
        licenses/                     UI, audio and map-source notices
  SPT_Runtime/user/mods/ArenaMode/
    maschine-ArenaMode.Server.dll
    config/
      kits.json
      ladder.json
```

`maschine-Redeploy.Core.dll` is a required shared library, not the full Redeploy
mod. Use the version supplied for the Arena build, keeping only one copy in the
plugin tree. This build requires Core **1.0.8 or newer**. If Core is missing,
unreadable or too old, Arena disables itself before settings, hooks or components
start and reports an error in the client log and SPT dependency list. Install the
supplied Core and restart the game. This minimum check permits newer versions;
it does not certify future API changes.
Redeploy's standalone client and server are not required: Core owns local
respawning, while Arena's own server protects and restores the raid profile.

The package always includes ArenaMode's own BigBrain integration library. It has
no separate plugin GUID; Arena starts and stops it. Without BigBrain
it stays inactive; **1.5.0** is the tested minimum. Newer versions are attempted
with a warning and runtime API checks, not certified as compatible. BigBrain
itself and other mods are installed separately. Preserve documentation and notices.
Update both Arena client DLLs together and restart the game. Mixing the new
client with the old companion plugin disables the new integration with an error.
When upgrading from a flat installation, remove the old Arena client/companion
DLLs directly under `BepInEx/plugins` to avoid duplicate plugin copies; keep
existing assets, drafts and configuration.
Rename the old `BepInEx/plugins/ArenaMode` asset folder to `maschine-ArenaMode`
before extracting. If both folders exist, preserve and compare custom files
such as `Pickups.json`, `AreaDrafts` and `PickupDrafts` before merging them.

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
| **Left/right mouse button** while team spectating | Cycle living targets: left = next, right = previous; Tactical also allows observing opponents |
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
| **PitFireTeam** client and server | **0.10.0 or 0.10.3+**; 1–4 companions for Tactical and Zombies. Not required for independent Team DM. |
| **SAIN** | **4.5.1+** adapters for personalities, pursuit and movement. Native AI remains available without it. |
| **BigBrain** | **1.5.0+** (tested on 1.5.0; newer versions warn), with the bundled ArenaMode companion: bot pickup objectives, post-death pursuit and Molotov fire avoidance. |
| **APBS** | **2.4.1+** equipment/progression adapter. Not required for kit or bot generation. |
| **ArenaMapPort** | Optional Bay5/Equator/Air Pit client, server and map assets. |
| **Killcam** | **1.0.0** is the current integration target; compatible API and enabled DeathReplay required. The former KillcamReplay plugin is not accepted. |
| **DynamicMaps** | Corpse-marker cleanup adapter for **1.2.1+**; not required for Arena's static Customs preview. |

Newer versions of SAIN, PitFireTeam, APBS, DynamicMaps and BigBrain are attempted
with a warning; required API and protection checks still apply. PFT 0.10.1 and
0.10.2 remain excluded. Passing these checks does not certify a future release.

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
`BepInEx/plugins/maschine-ArenaMode/` assets and its server mod folder. Back up customized
files first. Keep Redeploy.Core if another installed mod uses it. Do not remove
the BepInEx or SPT user directories themselves.

## Building from source

Windows, the **.NET 10 SDK**, an installed **SPT 4.1** game and NuGet restore are
required. The client targets netstandard2.1; the server targets net10.0. Game
assemblies must come from your own installation.

The source also requires a sibling [Redeploy](https://github.com/maschine34675/Redeploy)
checkout containing
`Redeploy.Core/`, its `CoreVersion.props`, and `drivers/CoreVersionGate.cs`.
Use the exact Redeploy commit recorded in the release's `PACKAGE-MANIFEST.json`;
this release uses Core 1.0.8. Do not substitute an arbitrary Core DLL for the
source dependency.

**Ordinary builds automatically deploy into the configured SPT installation.**
Build/deploy tools require PowerShell 7 (`pwsh`). Both Arena DLLs, audio and
notices use `BepInEx/plugins/maschine-ArenaMode/`, matching the release ZIP.
The build migrates the old `ArenaMode` asset folder and flat Arena DLL/PDB copies,
preserves custom files, and keeps recoverable replaced copies outside plugin
discovery in `BepInEx/backups/maschine-ArenaMode-layout/`. Conflicting custom
files, locked files or junctions stop migration before changes. An interrupted
multi-file deployment can be rerun; it is not a transaction across all files.
Shared Core remains flat and server files stay in `SPT_Runtime/user/mods/ArenaMode/`.
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
The client build/publish output keeps the DLL at its normal output location and
stages its assets/notices in the adjacent `maschine-ArenaMode/` folder. The same
payload writer supplies staged assets, the installation and release archives;
only release `INSTALL.txt` and `PACKAGE-MANIFEST.json` are package-specific.

To build an installable release ZIP without deploying, commit the source first
and run `pwsh -File tools/Package-TestRelease.ps1 -Release -SptPath C:\SPT41`.
This uses isolated source snapshots, builds both halves, runs the unit suite and
verifies the archive. Omit `-Release` for a distinctly named local test package.
Neither form uploads anything. Published DLL scan links are in the release notes.
When coordinating with a Redeploy release, pass its verified Core DLL through
`-CoreAssemblyPath` and its independently checked hash through
`-CoreAssemblySha256`. The packager checks the assembly identity, version and
exact source commit so both downloads can contain the same shared DLL bytes.

## Licenses and credits

Original ArenaMode code and documentation are under the [MIT License](LICENSE).
**This does not license every bundled asset under MIT.** The
[license and source overview](THIRD-PARTY-NOTICES.md) covers Drexira's adapted
DragonDen code, game-icons.net artwork, the Customs map, audio and Redeploy.Core.
The Customs artwork is **CC BY-NC-SA 4.0**; announcer recordings have separate
terms and are not an unrestricted reusable voice pack.

ArenaMode is an unofficial mod, not affiliated with or endorsed by Battlestate
Games. EFT game content and trademarks remain the property of their owners.
