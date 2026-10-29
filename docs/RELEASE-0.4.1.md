# ArenaMode 0.4.1

29 October 2026. SPT 4.1.x. Single-player only. Hotfix for
[0.4.0](https://github.com/maschine34675/ArenaMode/blob/v0.4.1/docs/RELEASE-0.4.0.md).

## Fixed

- **Wiped profiles, pre-enrollment backups, moved installations:** START ARENA SESSION no longer closes
  EFT and leaves the normal SPT server paused when a profile's reward data no
  longer matches its reward folder. The session then runs without rewards. At
  the next normal start the old reward folder is renamed to `.archived-...`
  (never deleted) and the profile is enrolled again if nothing in it is
  unsettled; your reward choice is kept.
- **Unusable reward folder:** SPT starts normally when its folder or a parent
  folder is a junction, symbolic link or mount point, or when the reward folder
  cannot be used. Rewards are skipped for that start, with one Warning.
- **Several SPT installations:** a finished Arena session of one installation
  no longer blocks Arena sessions of another with a false "still running"
  message. A refused enter reopens the normal game and says why.
- **Shared Core 1.0.18:** Recoil Rework Legacy and Fontaine FOV Fix are handed
  over to Redeploy's Continue as Scav body like an ordinary respawn. Arena
  respawns are unchanged. The whitelist records
  Redeploy 1.1.1.

See the [versioned changelog](https://github.com/maschine34675/ArenaMode/blob/v0.4.1/CHANGELOG.md#041---2026-10-29) for the full list.

## Profile rewards

Optional **PROFILE REWARDS** are on by default for every profile that is not yet
registered. At the next normal server start ArenaMode.Server adds a small data
block to the profile file. It credits RUB mail and PMC XP only after Arena
matches. Switch rewards off with the rewards card in the Arena map menu.

## Installation

Close game and server first, back up your profile, then extract
**ArenaMode-0.4.1.zip** into the SPT root, merging `BepInEx/` and `SPT_Runtime/`.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.18**. The same
Core is bundled in Redeploy 1.1.1; the standalone Redeploy client/server are
optional. Never replace a newer Core with an older copy. BigBrain, its
prerequisites and ArenaMapPort are separate downloads. Existing F12 settings,
whitelist choices, kits and ladder are preserved.

## Validation and remaining limits

The fixes are covered by unit tests, offline harnesses and planted-bug checks.
The native release check of the published binaries is recorded on the
[GitHub release page](https://github.com/maschine34675/ArenaMode/releases/tag/v0.4.1).
Gameplay is unchanged from 0.4.0, so its
[native sweep](https://github.com/maschine34675/ArenaMode/blob/v0.4.1/docs/RELEASE-SWEEP-20261029.md)
still describes the modes and maps; it used earlier binaries.

If another SPT installation was deleted while its Arena session was still open,
delete its private session folder named in the message by hand. All limits of
[0.4.0](https://github.com/maschine34675/ArenaMode/blob/v0.4.1/docs/RELEASE-0.4.0.md#validation-and-remaining-limits)
still apply: Domination, Tactical, Zombies, Akimbo and Molotov remain WIP,
Domination has no profile rewards, and Fika is unsupported.
