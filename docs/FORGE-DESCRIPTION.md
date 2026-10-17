## {.tabset}

### Overview

ArenaMode adds arena matches against AI to SPT 4.1. Select **ARENA** next to PMC
and Scav, then choose a mode, map, equipment and mutators in the raid setup menu.

- Five modes: Gun Game, Deathmatch, Team DM, Tactical and Zombies.
- Respawns or round/wave-based survival, depending on the mode.
- Arena kits, a weapon ladder and mode-specific own-gear options.
- Scoreboard, assists, killfeed, hitmarkers, health/armor and weapon panels.
- Profile restoration on exit: Arena loot, XP and quest changes are not kept.
- Single-player only. **Fika is not supported.**

Tactical, Zombies, Akimbo and Molotov are marked **WIP**. Back up your profile
before playing, particularly when combining mods.

### Modes

- **Gun Game:** each kill advances your weapon. Respawn at the current stage;
  the ladder repeats after the final weapon.
- **Deathmatch:** free-for-all or all bots against you. Choose rounds, a
  per-round kill/time limit, or continuous play without limits.
- **Team DM:** USEC against BEAR, with respawns and team kill/time limits.
  Does not require PitFireTeam.
- **Tactical (WIP):** round-based elimination. Solo defaults to 1v5 with a kit
  buy menu. With PitFireTeam, bring your gear and 1–4 companions against five
  opponents. Spectate after death and return next round.
- **Zombies (WIP):** increasing waves, with ammunition replenished between
  waves. Solo death ends the match. A PitFireTeam squad can keep fighting after
  your death; fallen members return after the wave is cleared. A wipe ends the
  match. Entry gear is restored between waves, including the surviving player.

Zombie defaults: **32 alive, 50 in the first wave, +20 per wave, own gear**.
Population and starting equipment can be changed in the menu.

### Maps and mutators

#### Maps

- Factory, day and night; Labs; Labyrinth.
- Customs: Factory Center, Factory Side and Customs Yard. Only these bounded
  areas are available, in all five modes.
- With ArenaMapPort: Bay5 and Air Pit in all modes; Equator in Gun Game, Deathmatch, Team DM
  and solo Zombies. Equator does not support Tactical or PFT Zombies.
- Select participant totals in Deathmatch/Gun Game and team sizes in Team DM,
  within the chosen map/area's limits.

#### Mutators

- Faster movement, double jump, bunnyhop, air control, sliding, airborne hip
  fire and vaulting. Less inertia/weight slowdown and shorter landing recovery.
  A master switch disables the movement group.
- **Slow walking with CMS/Surv12**, up to 1 m/s with Fast Movement enabled;
  sprint/jump/prone/vault remain blocked during surgery.
- Faster drawing, holstering, reloading, magazine packing and medical use.
  Supported weapons select full-auto when drawn; manual changes remain possible.
- Backpack items and magazines, automatic medical quickslots, instant search.
- Arena HUD and announcer switches; separate announcer/hitmarker volume.
  Kill skull, optional combined burst damage, detailed health/ammo/grenade
  panels and a scoreboard with damage-share assists.
- Team tracer/laser colors and friendly fire. Friendly fire defaults off for
  PFT Zombies, on for Team DM and PFT Tactical.
- Akimbo pistols (WIP), additional Molotov (WIP), bot movement options.
- Killcam replays with KillcamReplay installed and DeathReplay enabled.
  PFT team spectating has a target-HP display and works without the replay mod.
- Pickups on every supported Arena map in Deathmatch and Team DM: health,
  shield, double damage, jump boots, unlimited ammo and an M60. Checked positions
  rotate during play, within the selected Customs area where applicable.
- Visible Team DM bot spawn protection, default four seconds; firing, throwing
  a grenade or dealing damage ends it.

Molotov and deliberate bot pickup steering require BigBrain 1.5.0 or newer plus
ArenaMode's companion DLL. Molotov is unavailable in Zombies.

Labyrinth entrance traps/toxic pool and its center alarm are handled separately
for Arena. Way Up uses a 10-second countdown. Round-state lighting is available
on every supported Arena map. Quest/achievement/XP notifications and the
extraction list are suppressed; the misleading item-loss warning is hidden and
the post-raid XP page skipped.

### Setup and controls

- Requires **SPT 4.1.x**; native testing has used 4.1.6.
- Close the game and server. Extract the release into the SPT folder containing
  `EscapeFromTarkov.exe`, keeping its directory structure.
- Both the client and server are required. Keep the `ArenaMode` asset directory
  beside the client DLL, including its audio and license files.
- Use the supplied matching `maschine-Redeploy.Core.dll`; keep only one copy.
  The full Redeploy mod is not required.
- Install optional mods separately, with their own prerequisites.
- Start server and game, select ARENA, then configure and enter the match.
  Invite 1–4 PFT companions first if using a squad mode.

Default controls:

- **F9:** scoreboard and live Arena settings.
- **Backspace:** give up the current life; ends solo Zombies.
- **Escape:** raid menu / leave match.
- **Left/right mouse** while team spectating: cycle living bots; Tactical also
  allows observing opponents.
- **F12:** advanced settings, if a BepInEx configuration UI is installed.

Most switches are in the map-screen Mutators panel. Server configuration is
also available at the SPT server's HTTPS address plus `/arenamode`. Preserve
custom settings when updating; restart the game/server after replacing DLLs.

### Compatibility

Optional integration targets:

- **PitFireTeam 0.10.0 or 0.10.3**, client and server: 1–4 companions in Tactical
  or Zombies. Other PFT versions are not accepted.
- **SAIN 4.5.1:** personality, pursuit and movement adapters.
- **BigBrain 1.5.0+:** bundled ArenaMode bot action companion; inactive without BigBrain. Tested on 1.5.0; newer versions log a warning and retain runtime checks.
- **APBS 2.4.1:** equipment/progression adapter.
- **ArenaMapPort:** Bay5, Equator and Air Pit, with client/server and map assets.
- **KillcamReplay:** compatible API required; 0.9.13 adds the shorter Arena live
  tail. Team spectating itself does not require it.
- **DynamicMaps 1.2.1:** corpse-marker cleanup integration.

These targets are not a blanket guarantee for future versions or every mod
combination. Optional mods keep their own dependencies. RaidInfoPanels and
CombatSlide functionality is already included for Arena. Fika activation is
explicitly blocked.

### Notes and credits

- High bot counts depend on hardware, map and installed AI mods. Zombie defaults
  are not a performance guarantee.
- Some kits cannot fit large spare magazines/medical items; M60 spare boxes and
  the Enforcer Grizzly are known examples.
- Report problems with the mode, map/area, versions and relevant client/server
  logs. Remove private information before sharing; do not post full profiles.
- Original code/documentation: MIT. Media licenses are separate; keep the
  supplied notices when redistributing.
- Adapted HUD behavior: Drexira's DragonDen.Hitmarker and DragonDen.Killfeed,
  MIT. Shared respawn library: Redeploy.Core by maschine, MIT.
- Icons: [Lorc and Skoll / game-icons.net](https://game-icons.net/), CC BY 3.0.
- Customs preview: [Shebuka / Tarkov.dev](https://github.com/the-hideout/tarkov-dev-svg-maps),
  ground layer via acidphantasm's DynamicMaps, adapted to grayscale by maschine;
  CC BY-NC-SA 4.0.
- Audio: [IENBA](https://freesound.org/people/IENBA/sounds/762911/) and
  [User391915396](https://freesound.org/people/User391915396/sounds/570335/), CC0.
  Announcer recordings were supplied from an ElevenLabs Creator workflow for
  ArenaMode; they are not a CC/MIT voice pack for separate reuse.
- Weapon-category pictures were generated with OpenAI; the remaining original
  symbols and Molotov presentation are ArenaMode work. Full sources and terms
  are listed in `THIRD-PARTY-NOTICES.md` and the asset notices.

Unofficial mod; not affiliated with or endorsed by Battlestate Games.

{.endtabset}
