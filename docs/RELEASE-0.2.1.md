# ArenaMode 0.2.1

Fix release for 0.2.0, for SPT 4.1.x. Native checks used SPT 4.1.6 on SPT-GPU-2.

## Fix

- **Arena session with content mods left out.** The Arena session runs a private copy of the
  SPT server with only the whitelisted server mods and a disposable copy of your profile. When
  it left out a mod that adds items, clothing or traders, and your profile held such records
  (a modded weapon, a suit, a purchase from a modded trader, a mail attachment), SPT's shipped
  settings marked the profile copy invalid: SPT ships `removeModItemsFromProfile` and
  `removeInvalidTradersFromProfile` switched off in `SPT_Data/configs/core.json`, and then it
  refuses to load such a profile. The session's game failed to load and closed itself after
  about a minute, without a reason on screen. Installations that had switched both settings on
  were not affected.

  The private server's copy of `core.json` now switches SPT's own clean-up on. SPT then deletes
  those records from the disposable copy only: a modded weapon you carry is missing in the Arena
  session and is back in the normal session. Your own `core.json`, your real profile and the
  normal server are not changed. The helper log
  (`%LOCALAPPDATA%\maschine-ArenaMode\session\helper.log`) names the change in one line.

Nothing else changed: this release is 0.2.0 with this fix and the new version number.

## Compatibility and test boundaries

- Reproduced and fixed on SPT-GPU-2 on 2026-10-25 with WTT-ContentBackport left out of the
  session, both settings at SPT's shipped values and a profile holding 11 of its items, among
  them the equipped primary weapon: before the fix the session's game closed itself after the
  server had marked the profile invalid; with the fix the server deleted the 11 items from the
  copy, the session reached its main menu with Arena available, and the return to the normal
  session left the real profile with all its items. That run used the development line with
  the same helper change (the host carried it); the 0.2.1 package itself was built and checked
  offline.
- Not covered by SPT's clean-up, and therefore not by this fix: quests, hideout areas and other
  profile records of a left-out mod that SPT's profile validator does not look at.
- Everything 0.2.0 states about its tests and limits still holds:
  [0.2.0 release notes](RELEASE-0.2.0.md).

## Updating

- Close the game and server, then merge the ZIP's **BepInEx** and **SPT_Runtime** folders into
  the SPT root as for 0.2.0. The session helper
  `BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe` carries the fix; update
  all Arena files together so the versions match. Preserve customized kits, ladder,
  configuration and your own whitelist file.
- Keep one copy of the bundled **Redeploy.Core 1.0.10** (unchanged from 0.2.0).
- Close any running Arena session first: while one runs, the helper's file is in use.

The GitHub release includes the exact source revision, ZIP hash, build/test results and
VirusTotal links for the packaged files.
