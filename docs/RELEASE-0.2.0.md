# ArenaMode 0.2.0

Changes since 0.1.3, for SPT 4.1.x. Native checks used SPT 4.1.6 on SPT-GPU-2.

## Changes

- **Arena runs only with whitelisted mods.** A shipped default list (`arena-whitelist.json`,
  next to the client DLL) allows the mods that were installed on the development hosts when
  it was written, each with its reviewed version, and denies ORBIT, Questing Bots, Adaptive
  Bot Culling, ISB Special Forces and ISB Aishi. Your own `allow`/`deny` entries, and an
  optional blacklist mode, go into `BepInEx/config/com.maschine.ArenaMode.whitelist.json`,
  which updates never touch. Client plugins, server mods and preloader patchers are judged
  alike; a denied mod also excludes the mods that need it.
- **START ARENA SESSION.** With other mods installed, ARENA is disabled with the reason and
  offers a two-click restart into an Arena session: a preloader gate loads only the
  whitelisted plugins, the helper `maschine-ArenaMode.Session.exe` starts a private copy of
  the SPT server on port 6970 with only the whitelisted server mods and a disposable copy
  of your profile, and only Arena raids are offered. **RETURN TO NORMAL SESSION** restarts
  with the normal server and the full mod set. The game closes only after the helper has
  taken the switch over; a session whose policy changed since the restart refuses Arena at
  the next character screen and offers the way back; closing the Arena game by hand (EXIT,
  a crash, a kill) stops the private server. The helper logs to
  `%LOCALAPPDATA%\maschine-ArenaMode\session\helper.log`. A normal session whose mods are
  all allowed plays Arena directly, as before.
- **Bowl** (ArenaMapPort 0.3.0): all five modes, including PitFireTeam Tactical and
  Zombies, with a map preview, lighting and pickup placement; eight participants by default
  in Deathmatch, Gun Game and Team DM; independent Team DM in Bowl's east/west team boxes
  from 2v2 to 10v10, with combat waiting for the spawn gates and complete exit routes.
  Team DM consumes ArenaMapPort's gate-completion signal on all four ported maps.
- **Redeploy.Core 1.0.10.** The general Recoil Rework Legacy, Continuous Load Ammo and
  Continuous Healing body-replacement fixes moved into Core (1.0.9) and are shared with
  standalone Redeploy; this release bundles Core 1.0.10, the library of Redeploy 1.0.1, and
  requires at least that version. Arena keeps only its medical fallback for Arena bots.
  Recoil Rework Legacy 2.0.0 and Continuous Load Ammo 1.2.0 are on the default list.
- **Removed adapters:** MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead, Smoothie,
  Late To The Party, Classic Movement, StatTrack, Deminvincibility, Skills Extended, Pause
  Continued and EFT Effects Manager / Broken Limbs Don't Heal. The whitelist replaces those
  workarounds: these mods are not on the default list, and allowing one of them does
  **not** restore its former adapter behavior.
- Verification was consolidated into one entry point; outdated harnesses were repaired and
  obsolete checks retired.

## Compatibility and test boundaries

Allowing a mod through the whitelist is not a compatibility certification: a mod you allow
runs unadapted, at your own risk, and the default list reflects one installation. Fika is
never filtered; Arena stays unavailable with Fika. The Arena session needs a local SPT server
in `SPT_Runtime` beside the game; with the server elsewhere, a remote backend or an older
Arena server the session is not offered and the refusal asks you to remove the other mods.

Native tests on SPT-GPU-2 (details and limits in the linked documents):

- Arena session, five runs between 2026-10-21 and 2026-10-23: the switch in both
  directions, the default list, the testing override, a policy file changed while the
  session ran (a denied plugin, a denied preloader patcher) refusing at the next screen,
  the Arena game closed by hand (EXIT and kill) ending the private server, the return
  waiting for the helper. Not run natively, offline checks only: the helper's
  acknowledgement timeout and a late helper, a server mod or policy hash that disagrees with
  the current policy, a policy that cannot be loaded, a backup configuration outside the
  server tree, two Windows sessions of one account.
- Bowl: Team DM 4v4 runs, a death and respawn, Deathmatch overviews and the gate-completion
  signal on 2026-10-23 and 2026-10-24.
- Shared body replacement: Arena with Core 1.0.9 on 2026-10-23 (Recoil Rework Legacy,
  Continuous Load Ammo, Continuous Healing). Core 1.0.10 is the library published with
  Redeploy 1.0.1 on 2026-10-24; the Arena native runs above used 1.0.9. The GitHub release
  records the smoke test of this build with Core 1.0.10.
- Not covered: every mod combination and optional-mod version, Tactical PFT beyond the
  earlier playtests, preloader patchers registered late by other patchers.

Evidence and limits:

- [Arena session: specification, acceptance runs and change log](ARENA-SESSION-20261021.md)
- [Arena session: answer to the external review of 2026-10-22](ARENA-SESSION-REVIEW-RESPONSE-20261022.md)
  and [of 2026-10-23](ARENA-SESSION-REVIEW-RESPONSE-20261023.md)
- [Bowl team boxes](BOWL-TEAM-BOXES-20261023.md), [GPU results](GPU-BOWL-TEAM-BOXES-20261023.md),
  [spawn-gate completion and DynamicMaps](GPU-SPAWN-GATE-COMPLETION-20261024.md)
- [Shared body-replacement adapters in Redeploy.Core](SHARED-BODY-COMPAT-20261023.md)
- [Dependencies and compatibility](DEPENDENCIES.md)

## Updating

- Close the game and server; back up profiles and customized configuration.
- Merge the ZIP's **BepInEx** and **SPT_Runtime** folders into the SPT root. New in this
  release: `BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll`,
  `BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe` and
  `BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json` (overwritten by every update;
  keep your own entries in `BepInEx/config/com.maschine.ArenaMode.whitelist.json`). Update
  both Arena client DLLs and the server together and remove duplicate old Arena DLLs.
  Preserve customized kits, ladder and configuration files.
- Keep one copy of the bundled **Redeploy.Core 1.0.10**, byte-identical to the library of
  the published Redeploy 1.0.1. Standalone Redeploy is not required. Arena 0.1.3 together
  with Redeploy 1.0.1 and Recoil Rework Legacy, Continuous Load Ammo or Continuous Healing
  made Core refuse body replacement; this release resolves that combination.
- The Arena session writes `%LOCALAPPDATA%\maschine-ArenaMode\session`: the helper log, the
  private server copy with the whitelisted server mods and the disposable profile. The
  first session start copies the server binaries, which an antivirus scan can slow down;
  later starts reuse them. Remove the folder when uninstalling.
- The bundled BigBrain integration is a library and remains inactive without BigBrain.
  Optional third-party mods and development/test tools are not bundled.

The GitHub release includes the exact source revision, ZIP hash, build/test results and
VirusTotal links for the packaged DLLs. Older releases remain available.
