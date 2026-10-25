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
- Arena runs only with **whitelisted mods**. With other mods installed, **START ARENA
  SESSION** restarts the game into a filtered Arena session with a private copy of the
  server; **RETURN TO NORMAL SESSION** brings the full setup back (see *Whitelist*).

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
  buy menu. With PitFireTeam, choose own gear or a supplied PMC kit and bring
  1–4 companions against five opponents. Companions keep their own gear.
  Spectate after death and return next round with the selected entry loadout.
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
- With ArenaMapPort: Bay5, Air Pit and Bowl in all modes; Equator in Gun Game, Deathmatch,
  Team DM and solo Zombies. Equator does not support Tactical or PFT Zombies.
- Select participant totals in Deathmatch/Gun Game and team sizes in Team DM,
  within the chosen map/area's limits.
- Air Pit Team DM requires ArenaMapPort 0.2.2 or newer for its team-start boxes.
- Bowl (ArenaMapPort 0.3.0 was tested) defaults to eight participants in Deathmatch, Gun
  Game and Team DM. Independent Team DM uses its east/west team boxes for 2v2 to 10v10;
  combat waits for the spawn gates to open, and the gates stay open for respawns.

#### Mutators

- Faster movement, double jump, bunnyhop, air control, sliding, airborne hip
  fire and vaulting. Less inertia/weight slowdown and shorter landing recovery.
  A master switch disables the movement group.
- **Slow walking with CMS/Surv12 or while splinting a leg**, up to 1 m/s with
  Fast Movement enabled; sprint/jump/prone/vault remain blocked.
- Faster drawing, holstering, reloading, magazine packing and medical use.
  Supported weapons select full-auto when drawn; manual changes remain possible.
- Backpack items and magazines, automatic medical quickslots, instant search.
- Arena HUD and announcer switches; separate announcer/hitmarker volume.
  Kill skull, optional combined burst damage, detailed health/ammo/grenade
  panels and a scoreboard with damage-share assists.
- Team tracer/laser colors and friendly fire. Friendly fire defaults off for
  PFT Zombies, on for Team DM and PFT Tactical.
- Akimbo pistols (WIP), additional Molotov (WIP), bot movement options.
- Killcam replays with Killcam installed and DeathReplay enabled.
  PFT team spectating has a target-HP display and works without the replay mod.
- Pickups on every supported Arena map in Deathmatch and Team DM: health,
  shield, double damage, jump boots, unlimited ammo and an M60. Checked positions
  rotate during play, within the selected Customs area where applicable.
- Visible Team DM bot spawn protection in the fixed team boxes on Equator,
  Bay5, Air Pit and Bowl, default four seconds; firing, throwing a grenade or dealing
  damage ends it. Other maps retain player protection without bot spawn shields.
- Narrow colored body contours identify bot spawn protection, Shield and Double
  Damage. Opaque cover hides the contour; simultaneous effects share its colors.

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
- Both the client and server are required. The two Arena client DLLs, audio,
  assets and notices belong in `BepInEx/plugins/maschine-ArenaMode/`.
  The server belongs in `SPT_Runtime/user/mods/ArenaMode/`.
  The Arena session adds the preloader gate
  `BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll`, the helper
  `maschine-ArenaMode.Session.exe` and the default whitelist `arena-whitelist.json`
  beside the client DLL; the list is overwritten by updates, your own entries belong in
  `BepInEx/config/com.maschine.ArenaMode.whitelist.json`.
  When updating, preserve custom files and remove duplicate old Arena DLLs.
- Use the supplied `maschine-Redeploy.Core.dll` (1.0.10); keep only one copy,
  directly in `BepInEx/plugins/`.
  Arena requires at least this version and disables itself before initialization
  if Core is missing, unreadable or older. Restart after correcting the files.
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

### Whitelist

Many third-party mods break Arena raids, and Arena no longer adapts to each of them
one by one. Arena therefore runs only with whitelisted mods. The shipped default list
(`arena-whitelist.json`, next to the client DLL) allows the mods that were installed on
the author's hosts when it was written, each with its reviewed version: the bot stack
(SAIN, APBS, ABPS, BigBrain, Waypoints), the author's own mods, PitFireTeam, DynamicMaps,
UIFixes, WTT-CommonLib and WTT-ContentBackport (with its preloader patcher), SVM, give-ui,
Continuous Healing, Continuous Load Ammo, Recoil Rework Legacy, Freecam, DoorDash,
Anvil-WebOverlay, ArmorClassIcon, BetterAmmoLoadingList, BrighterInteriors,
DevBalaclavaFix, HealingAutoCancel and SmajlecLights. It denies ORBIT, Questing Bots,
Adaptive Bot Culling, ISB Special Forces and Aishi. SPT's own plugins and ArenaMode are
always allowed; a version that differs from the reviewed one only produces a warning.
Allowing a mod is **not** a compatibility certification: it runs unadapted, at your own
risk.

- **Other mods installed:** ARENA is disabled with the reason (`Blocked for Arena: ...`
  for denied mods, `Not on the whitelist: ...` for unlisted ones; preloader patchers count
  like plugins) and offers **START ARENA SESSION**. Two clicks, because the game restarts
  (about two minutes): a preloader gate loads only the whitelisted plugins, the helper
  `maschine-ArenaMode.Session.exe` starts a private copy of the SPT server on port 6970
  with only the whitelisted server mods and a **disposable copy of your profile**, and only
  Arena raids are offered. Profile changes made in the session are discarded.
  **RETURN TO NORMAL SESSION** restarts with your normal server and the full mod set.
  Closing the Arena game by hand (EXIT, a crash) ends the private server as well.
- **Only whitelisted mods installed:** Arena plays directly, as before, on your normal
  server, without a restart.
- The switch needs a local SPT server in `SPT_Runtime` beside the game and this release's
  Arena server mod. The session writes `%LOCALAPPDATA%\maschine-ArenaMode\session`
  (helper log, private server copy); remove it when uninstalling. The first session start
  copies the server binaries, which an antivirus scan can slow down.
- A policy file edited while the session runs is noticed at the next character screen:
  a session that no longer matches your policy refuses Arena and offers the way back.

Your own additions and exclusions belong in
`BepInEx/config/com.maschine.ArenaMode.whitelist.json`, which updates never touch. An
entry names a plugin or server mod `guid` or a preloader `patcher` file name; your
`deny` always wins (a denied mod also excludes the mods that need it), and your `allow`
lifts a default deny:

```json
{
  "schema": 1,
  "allow": [ { "guid": "com.example.extra", "name": "Extra Mod", "kind": "mod" } ],
  "deny":  [ "com.example.looting", "Example.Patcher.dll" ]
}
```

To let every installed mod load and name only the ones that must stay out (a blacklist;
nothing has been reviewed for the mods it lets through), add `"unlisted": "allow"`.
`{ "schema": 1, "unlisted": "allow" }` alone is the testing override: everything loads
except the default deny list, and the menu, the Arena session notice and the log say so
(`policy override`). A file that does not parse is ignored as a whole with a warning. Fika
is never filtered; with Fika, Arena stays unavailable. Full rules and the GPU acceptance
runs: README and `docs/ARENA-SESSION-20261021.md` in the repository.

### Compatibility

Optional integration targets:

- **PitFireTeam 0.10.0 or 0.10.3+**, client and server: 1–4 companions in Tactical
  or Zombies. PFT 0.10.1 and 0.10.2 remain excluded.
- **SAIN 4.5.1+:** personality, pursuit and movement adapters.
- **BigBrain 1.5.0+:** Arena's bundled integration library handles bot actions; it has no separate plugin GUID and stays inactive without BigBrain. Tested on 1.5.0; newer versions log a warning and retain runtime checks.
- **APBS 2.4.1+:** equipment/progression adapter.
- **ArenaMapPort:** Bay5, Equator, Air Pit and Bowl, with client/server and map assets.
- **Killcam 1.0.0:** compatible API and enabled DeathReplay required. The former
  KillcamReplay plugin is not accepted. Team spectating does not require Killcam.
- **DynamicMaps 1.2.1+:** corpse-marker cleanup integration.
- **Continuous Healing 1.6.3+:** player-only healing continuation/cancellation
  does not affect Arena bots or retired player bodies. It remains enabled for
  the current living player.
- **Continuous Load Ammo 1.2.0+:** loading follows Arena body changes without old
  cleanup or delayed completion affecting the replacement player.
- **SVM 2.2.3+:** SaveLoot/Ignore settings do not bypass Arena's protected raid end;
  ordinary raids retain those rules. Other SVM settings remain effective.
- **Recoil Rework Legacy 2.0.0+:** recoil/camera components and weapon state follow
  Arena's replacement player bodies. Original recoil settings and ordinary raids
  are preserved. Supplied kits, repeated respawns, PFT refills and firing
  afterward passed native tests with the former Arena-owned adapter; the shared Core
  implementation that replaces it was validated natively without PFT, and the full
  camera/Akimbo matrix remains untested.

The Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing body-replacement fixes
are provided by the bundled Redeploy.Core 1.0.10 (they also apply to standalone Redeploy);
Arena keeps only its medical fallback for Arena bots.

Targeted Factory tests cover SVM's SaveLoot/Ignore raid ends and CLA loading and
respawns. Specific timing/UI and replay cases remain open; these checks are not
exhaustive compatibility certification.

Adapters for other foreign mods (MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead,
Smoothie, Late To The Party, Classic Movement, StatTrack, Deminvincibility, Skills
Extended, Pause Continued, EFT Effects Manager / Broken Limbs) were removed on
2026-10-22: Arena runs only with whitelisted mods, and a mod you allow yourself runs
unadapted.

Newer versions of integrations marked with `+` are attempted with a warning while keeping
the required API and protection checks. These targets are not a blanket guarantee for future versions or every mod
combination. Optional mods keep their own dependencies. RaidInfoPanels and
CombatSlide functionality is already included for Arena. Fika activation is
explicitly blocked.

ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces and Aishi are blocked by
the default deny list of the Arena whitelist, not by code specific to them; ordinary
PMC/Scav raids remain available. ISB's separate progression is not protected by Arena's
profile snapshot. An `allow` entry in your own whitelist file lifts a default deny, at
your own risk.

### Profile rewards (development)

Development builds after 0.2.0 also include optional profile rewards, enabled by
default for newly registered profiles (existing OFF choices stay OFF):

- Per eligible kill: 1,000 RUB + 100 XP; per DM/TDM/Tactical round win:
  10,000 RUB + 1,000 XP; maximum 200,000 RUB + 20,000 XP per match.
- Gun Game and Zombies currently award kills only; no-contest results do not pay.
- Paid at the next normal server start: RUB by mail, XP directly to the PMC.
  The mail lists XP already credited; collecting it does not grant XP twice.
- The Arena map menu controls rewards in either session. Private sessions return
  signed results, without copying their profile back.

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
