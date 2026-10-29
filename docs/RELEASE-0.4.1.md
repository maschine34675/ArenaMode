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
- **Wiped or moved profile with Tarkova Cola history:** SPT no longer refuses to
  start (on every machine sharing the profile) after a profile wipe, a deleted
  profile file or a move or copy of the installation when the Cola reward history
  is complete; rewards stay off for that profile and nothing is changed or
  deleted. An unfinished Cola reward or an unreadable Cola file still stops
  startup, now with the real cause and the remedy that fits it.
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

## Native release check

On a GPU test host (SPT 4.1.6) with these exact binaries loaded: a profile that had
been wiped in the SPT launcher after Arena had paid Tarkova Cola progress no longer
stopped SPT; one Warning named it, and the profile and all 185 of its reward files
stayed byte-identical. A direct start of an Arena session with that profile ran
without rewards as intended, and RETURN TO SPT restarted the normal game. With
Redeploy 1.1.1 and Recoil Rework Legacy 2.0.0, Continue as Scav kept the Scav alive
through extraction. Arena matches, the several-installations case and an unusable
reward folder were not run natively.

## Package checks

Built from `2a4438c973e611a3b4458c3d7919c9728d15bf7c`. ZIP SHA-256: `ec29f941e5d29a82a35f1a5317a6c68931a8534e251d26886787d5cb312e591f` (3,859,686 bytes). The package manifest records Core 1.0.18 from Redeploy `232af19`; the bundled Core is byte-identical to the one in Redeploy 1.1.1. Package contents, paths, hashes, defaults, notice links and audio provenance passed the package verifier.

Fresh checks: 3,517 passed, with four explicit environment-dependent skips. Credential scanning found no credentials in the release source or payload.

VirusTotal results below apply to these exact binaries (29 October 2026). The five DLLs had no malicious or suspicious labels. The unsigned helper `maschine-ArenaMode.Session.exe` has **4 malicious labels** (VirIT `Trojan.Win32.MSIL_Heur.A`, Elastic `malicious (moderate confidence)`, APEX `Malicious`, McAfeeD `ti!51BD61743D01`), 67 undetected and 4 unsupported engines. A separate Microsoft Defender scan found no threats. These findings are unresolved; they are not established false positives. No antivirus protection needs to be disabled.

| File | SHA-256 / scan |
|---|---|
| `maschine-ArenaMode.Gate.dll` | [d368a6d09451…](https://www.virustotal.com/gui/file/d368a6d094519ddfd220450857d0305cd35b5355a1c8c1381a2a715702dc3a4d) |
| `maschine-ArenaMode.BigBrainCompat.dll` | [163e287df9bf…](https://www.virustotal.com/gui/file/163e287df9bf66b7dabe47da22ab8a54a93d3e9c80ce90928eb81f5e4a3ed1b0) |
| `maschine-ArenaMode.Client.dll` | [69083e467bda…](https://www.virustotal.com/gui/file/69083e467bda31ac2606e4a8ed7193b7b16e8ab71113056df07c4092240ae7bc) |
| `maschine-ArenaMode.Session.exe` | [51bd61743d01…](https://www.virustotal.com/gui/file/51bd61743d013218b860f33e84b14daa231797106812d791d41f0a7be31cdd1c) |
| `maschine-Redeploy.Core.dll` | [98620898b6a7…](https://www.virustotal.com/gui/file/98620898b6a7cdbdc4802681f3ad39dbecb4eacec515fb2013d379d872a8faaf) |
| `maschine-ArenaMode.Server.dll` | [0eb1d3ae6441…](https://www.virustotal.com/gui/file/0eb1d3ae64414d220aae65b9a0d725b9bc6b61bdcf390814e400caf8021445a7) |
