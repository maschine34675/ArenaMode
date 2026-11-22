# ArenaMode 0.7.0

22 November 2026. SPT 4.1.x. Single-player only. Successor of
[0.6.0](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/RELEASE-0.6.0.md).
Bundles shared **Redeploy.Core 1.0.28**, identical to the Core in Redeploy 1.1.4.

This release adds the map Block, replaces the pressure system with one difficulty step, gives
Tactical lighter opening rounds and Zombies special waves, and makes the session and reward
startup fail soft. It is made for ArenaMapPort 0.7.0.

## New

- **Block** through ArenaMapPort 0.7.0 (`arena_yard`, Arena's own name for the map): Gun Game,
  Deathmatch, Team DM, Domination, Tactical and Zombies, solo and with a PitFireTeam squad
  (18 bot spawn points), with eight participants by default. Team DM uses the map's original
  pockets as team boxes (USEC south, BEAR north) for 2v2 to 10v10, and Domination has two
  capture points, A and B. The saved map list gains Block once, through optional-map
  revision 7: only a list that equals the exact previous default is extended, an edited list
  stays as it is. The map has nothing to do with the Customs Yard area.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/BLOCK-INTEGRATION-20261121.md).
- **One difficulty step:** **Maximum** (default) or **Reduced**, chosen on the Arena map screen
  and as setting Difficulty > Step. Bots fight at full strength from the first spawn in
  Deathmatch, Team DM, Domination and Tactical; only Gun Game still builds up, over the map's
  ramp time, and Zombies follow the step through their composition. Reduced lowers everything
  one step (PMCs at hard and Scavs at normal, bot equipment at about 60 % of the top level,
  two instead of four extra PMCs, half of the extra bot speed, position reports 1.5 times less
  often). The step is fixed when a raid starts. Scale Difficulty, Pressure Ramp Minutes,
  Pressure Streak Percent, Pressure PMC Bonus and Max Bot Difficulty are removed; a saved
  Scale Difficulty of off, or a Max Bot Difficulty below impossible, selects Reduced once.
  Client and server must be updated together.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/DIFFICULTY-MODEL-20261118.md).
- **Tactical opening rounds (solo):** the enemy squad's gear is lighter in rounds 1 and 2
  (bot equipment level about 25 and 45 at Maximum, 15 and 27 at Reduced on the default map)
  and full from round 3, so the first rounds are not decided by what a starting budget cannot
  afford. The buy screen says what applies ("Enemy squad this round: light gear", "...medium
  gear", and once "Enemy squad from now on: full gear"). Protected PitFireTeam Tactical and
  every other mode are unchanged.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md).
- **Zombies:** at Maximum the horde keeps the configured number alive from wave one, the share
  of slow walkers fades and the shooter share grows with the waves, and every fifth wave is a
  special wave: waves 5, 15, 25, ... are ALL RUNNERS and waves 10, 20, ... are ALL SHOOTERS,
  announced on screen. Reduced has no special waves and at most 24 infected alive at once. The
  menu row ALIVE AT ONCE shows that limit. Zombies can also recover individual confirmed stuck
  enemies without awarding kills, and pursuit and melee share a budget of two native planning
  jobs per frame.
  [Escalation](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/ZOMBIE-ESCALATION-20261119.md),
  [flow](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/ZOMBIE-FLOW-20261115.md).
- **Movement:** improved jump input around ledges and slide transitions, landing slides that
  keep collision-reduced momentum, slide jumps that keep their direction, and sprint jumps that
  restore the normal hip-fire weapon position (aiming down sights stays disabled in the air).
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/MOVEMENT-TRANSITIONS-20261115.md).
- **Translocator (WIP, optional):** a device you select in the Combat mutator list: fire to
  throw or recall one disc, aim to teleport to a clear ground or airborne destination.
  Disabled by default, Arena only. Its throws ignore invisible spawn helpers and broad
  movement volumes, which fixed false blocks on Factory.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/TRANSLOCATOR-20261115.md).
- **Chop Shop recommends six participants again** (eight since 0.6.0). A count you already
  saved for the map stays; a fresh configuration starts at six.
- **Domination objective markers on DynamicMaps 1.3.0** (1.2.1 stays supported): changing
  DynamicMaps' marker pack keeps the objective diamonds.
  [Details](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/DOMINATION-MAP-MARKERS-20261101.md).

## Fixed

- **Pack A Punch** (and any mod that changes what is written to your profile file): entering
  Arena, returning from it and the end of an Arena raid no longer fail with
  "save-not-confirmed". Arena checks the save against what the game's own write produced;
  without such a mod nothing changes. Arena's startup profile writes also no longer make Pack
  A Punch set its data aside as a `.stale` file at the next start. If your Pack A Punch data
  went missing after Arena 0.6.0, see the recovery steps in
  [PACK-A-PUNCH-COMPAT.md](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/PACK-A-PUNCH-COMPAT.md).
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/PROFILE-WRITE-PIPELINE-20261120.md).
- **SPT starts when Arena reward data has a problem.** Rewards pause for that start with a
  Warning in the server log that says why and what to do, nothing is paid, and the next start
  tries again from the records; a Tarkova Cola reward file problem pauses rewards the same way.
  After Arena, if SPT cannot settle the session yet, the helper opens the game against the
  running SPT and one dialog says that rewards are paused; starting Arena again while that
  session is unsettled is refused with its own message.
- **A moved or copied installation:** SPT starts after the folder was moved to another folder
  or drive as long as every earlier Arena session was finished, and the first Arena start no
  longer stops on the old private session folder. If a session was not finished, the message
  names the folder that can finish it.
- **A foreign file in a profile's reward history** (a sync tool's conflict copy, a backup) no
  longer stops that profile's rewards silently: the server log names the file in one Warning
  per start, the reward card says what needs attention, finished matches stay queued and are
  paid at the next start, and such a file can no longer shift the numbering of a new reward.
- **The session helper's dialogs** say in plain words what happened, whether your profile was
  changed (only where that is certain), what to do next and where the logs are, and end with
  a short code to quote in a report.
- **Zombie waves stalled in the later waves of a long raid:** the game stored reloaded bot
  profiles twice and the second copy could not be used, so spawn requests came back empty.
  The correction applies to every Arena mode; only Zombies were measured.
- **Zombies, audio:** the game could put an infected's sound container back into its pool
  twice when that infected died, after which two infected shared one container and, rarely,
  the game's audio failed every frame until one of them died. In an Arena raid a container
  that is already in the pool is no longer put in a second time; normal raids are unchanged.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/AUDIO-CONTAINER-GUARD-20261120.md).
- **Gun Game weapon pool:** one broken weapon preset (for example from a weapon mod that
  changed the weapon's slots) no longer makes Arena fall back to the short built-in ladder;
  that preset is left out, the server log says which one and why, and the other weapons stay.
  Thanks to Sankese, whose ArenaMode-WeaponPoolFix add-on supplied the failure analysis.
- **Gun Game PMC gear:** pooled enemy PMC profiles that fell more than 5 levels behind the
  build-up are replaced by freshly generated ones. The Tactical start money setting describes
  the real kit prices (about 180k to 670k).
- **Translocator on Factory** no longer reports false blocks from invisible helper volumes.

## Compatibility

- **Shared Core 1.0.28** (identical to Redeploy 1.1.4), the bundled and the minimum Core:
  this ArenaMode disables its driver with a startup error when Core 1.0.27 or older is
  installed. Cores 1.0.25 to 1.0.27 were never released on their own; this is the first
  release that carries their changes, on top of the 1.0.24 of 0.6.0. Core 1.0.25 preserves
  VisualAssist 1.2.0's grenade preview through body replacement; 1.0.26 refreshes
  DynamicMaps 1.3.0's quest and key subscriptions after a replacement and retires Advanced
  Movement 1.0.1's slide, dive, gesture and audio state of the outgoing body; 1.0.27
  rebinds WTT Armory 2.0.5's AN-94 Hyperburst state to the replacement player; 1.0.28 adds
  the controller handoff of Fontaine FOV Fix 5.0.1 / SPT Realism Common Lib 0.1.0 and
  retires the outgoing body's downed, hands, reload and transient perk state for
  Tarkova-Cola 4.0.0. The public `RespawnApi` is unchanged (version 1). What was run in the
  game and what only offline is stated in Redeploy's compatibility records
  (`docs/compatibility`). Advanced Movement was allowed only in an isolated test policy and
  is not on the default whitelist.
- **Reviewed whitelist versions:** PitFireTeam 1.1.2 and TarkovaCola 3.0.1 (older supported
  versions and Cola reward receipts keep their behaviour), then Tarkova Cola 4.0.0, which
  adds a research and achievement catalog while older reward records are preserved and blocks
  Arena-generated Tombstone offering mails; VisualAssist 1.2.0 (Arena guards quick throws
  from stale prediction data); DynamicMaps 1.3.0 (the 1.2.1 minimum is unchanged);
  WTT Armory 2.0.5 (labelled 3.0.0 upstream), WTT Content Backport 2.0.7 and CommonLib
  3.0.8; Fontaine FOV Fix 5.0.1 and SPT Realism Common Lib 0.1.0; StashFilter 1.1.0,
  UIFixes 6.0.4, give-ui 5.0.1, Better BepInEx 1.0.4, HUD Breakout 1.4.1, WebOverlay 1.12.0
  and ArmorClassIcon 1.1.1. No third-party binaries are bundled.
  [Forge audit](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/WHITELIST-FORGE-AUDIT-20261121.md),
  [mod refresh](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-MOD-REFRESH-20261120.md),
  [Cola 4 review](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/TARKOVA-COLA-4-20261122.md).
- **ArenaMapPort 0.7.0** is the version this release is made for. It adds Block. With the
  older ArenaMapPort 0.6.0 Block is not offered and everything else works as with ArenaMode
  0.6.0; with 0.5.0 Chop Shop is not offered either, and Tactical on Equator and Iceberg has
  too few distant spawn points (Equator 10, Iceberg 9), as described in the notes of 0.6.0.
  ArenaMode does not detect the ArenaMapPort version: it offers each optional map when the
  map's own enabled live location with the exact id exists. Install 0.7.0.
- **PitFireTeam 1.1.2** is reviewed; 1.1.1 keeps working.
- The whitelist (69 mod entries, 5 deny entries) records ArenaMapPort 0.7.0, Redeploy 1.1.4,
  PitFireTeam 1.1.2, Tarkova Cola 4.0.0 and DynamicMaps 1.3.0; a differing recognized
  version stays advisory, and user policy is preserved.
- Fika is not supported.

See the [versioned changelog](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/CHANGELOG.md#070---2026-11-22) for the full list.

## Installation

Close game and server first, back up your profile, then extract
**ArenaMode-0.7.0.zip** into the SPT root, merging `BepInEx/` and `SPT_Runtime/`.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.28**. The same
Core is bundled in Redeploy 1.1.4; the standalone Redeploy client/server are
optional. Never replace a newer Core with an older copy, and note that this version
refuses Core 1.0.27 or older. BigBrain, its prerequisites and ArenaMapPort are separate
downloads; install **ArenaMapPort 0.7.0** for Block. Existing F12 settings, whitelist
choices, kits and ladder are preserved.

Upgrade notes. Update the client and the server together: the difficulty step and the Tactical
round report cross between them (an old server simply keeps the full gear level in Tactical).
In an edited server configuration `botTierEndLevel` of a map is now the bot equipment level from
the first bot in every mode but Gun Game (Maximum) and, from round 3, in solo Tactical;
`botTierStartLevel` and `botTierRampMinutes` act only in Gun Game (and the start level is the
floor of the Tactical opening rounds). Lower `botTierEndLevel` if you want softer bots than the
default 66. The removed difficulty settings migrate once, as described under New.

## Validation and remaining limits

Runs below are dated evidence for the builds their records name, not for the release binaries
(those follow below). Unless noted, they were native, on the GPU test host SPT-GPU-2, and the
records say what was not run.

- **Block** was run on 2026-11-21 with the installed ArenaMode build `0.6.0+fb2b3af6` and a
  pre-release ArenaMapPort build (`0.6.0+1d8faa8b`): Deathmatch, Team DM 4v4 and 10v10 with held
  gates, Domination, solo Zombies, solo Tactical and a plain PMC control, with no error line and
  no warning from ArenaMode or ArenaMapPort in each accepted run. The first Team DM start on an
  earlier build was refused because the start heights of seven south points were authored under
  the collision floor; it was fixed before the accepted runs. Not run: PitFireTeam on this map,
  Gun Game, the local player on a south formation point in Team DM or Domination (the test
  profile is BEAR), a match played to its result and reward, Deathmatch above eight
  participants, a player walking into a held gate, and anything heard (gate sounds, crowd,
  announcer). Records:
  [contract](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/BLOCK-INTEGRATION-20261121.md),
  [native report](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-BLOCK-20261121.md).
- **Difficulty step** was run on 2026-11-18 and 2026-11-19 in Tactical (Maximum and Reduced),
  Deathmatch (Maximum and Reduced, 16 participants), Team DM, Domination, Zombies (Maximum and
  Reduced), Gun Game passes, Tactical with one PitFireTeam companion and an ordinary PMC raid.
  Not run: Zombie waves beyond the first in that report (later covered by the Zombie runs
  below), Gun Game at Reduced, the Scav switch at half of the Gun Game ramp, a long Team DM or
  Domination raid with the full reserve per request, and the step's position-report factor was
  not timed. Record:
  [difficulty model](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-DIFFICULTY-MODEL-20261119.md).
- **Tactical opening rounds and Zombie special waves** were accepted natively on 2026-11-20:
  solo Tactical at Maximum and at Reduced over rounds 1 to 4 (bot levels inside the expected
  bands, the buy-screen lines, the purge of the profile pool), the menu row `24 (setting 32,
  Reduced)`, and special waves 5 and 10 with waves of six (none at Reduced, none at a shooter
  share of 100, only the Runners wave at 0). Not run: both failure shapes of the Tactical
  report (request lost, answer lost), protected PitFireTeam Tactical, a Scav squad, a custom
  map with other level numbers, wave 10 at the default size in that night's run, a played match
  with kills, the shooters' hit rate, and PitFireTeam with a companion in Zombies. The Gun Game
  weapon pool generated 155 weapons without a defective preset, so the guard's path for a left-out
  preset is pinned offline only. Record:
  [acceptance](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md).
- **Stalled Zombie waves and the sound-container guard** were run on 2026-11-20. With the
  profile correction the default-size waves 1 to 12 filled at 2.23 to 2.78 infected per second
  with no dropped request, where waves 5 to 7 had stalled at 0.61 to 0.72; the guard raid
  skipped 13 second returns, found no object in two pool slots in 1,153 polls and logged no
  `Can't handle not proposed occlusion test` line, where an unguarded raid of the same day logged
  5,259 in five bursts. Not shown: a played match with kills, the PMC modes with either change,
  memory of unused profiles, a normal raid for the guard. Records:
  [profile pool](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-ZOMBIE-PROFILE-POOL-20261120.md),
  [audio guard](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/AUDIO-CONTAINER-GUARD-20261120.md).
- **Zombie recovery, planning budget and wave variety** were run on 2026-11-15 on both test
  hosts. The record states that actual native retirement, replacement arrival and no-credit
  accounting for a persistent stuck body remain unverified, and that its planning figures do
  not establish an FPS improvement. Record:
  [Zombie flow](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-ZOMBIE-FLOW-20261115.md).
- **Movement transitions** were run on 2026-11-15 (slide to jump, landing slide, sprint-jump
  hip fire, held vault, death and respawn, master off). The record keeps the positive native
  ledge case of the 150 ms grace path open and claims no separate native acceptance of the
  other branches. Record:
  [movement](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-MOVEMENT-TRANSITIONS-20261115.md).
- **Translocator** was run on 2026-11-15 and 2026-11-16 on Air Pit and Factory. An independent
  low-ceiling-only refusal and the item-inspection animation were not exercised natively, and
  the Factory run covers the listed cases, not every map, dynamic door, live opposing body or
  movement combination. It remains WIP. Records:
  [first stage](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-TRANSLOCATOR-20261115.md),
  [collision correction](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-TRANSLOCATOR-COLLISION-20261116.md).
- **Pack A Punch** was run on 2026-11-20 with Pack A Punch 2.2.1 and Hideout Uncensored 1.3.0:
  the failure on release 0.6.0, a private session and a normal session with the fix, and on a
  copy of the installation the `.stale` case, where the control build left a `.stale` file and
  the fix kept the side files fresh. Not covered: a payout write at the return, fragments in the
  inventory or in mail and quest counters, and a private Arena server with Pack A Punch
  admitted. Record:
  [Pack A Punch](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/PAP-NATIVE-20261120.md).
- **Fail-soft startup and the moved installation** were run on 2026-11-19: a normal server with
  an unsettled session of another folder, a private Arena session round trip with the new
  helper, and a server start from a copy of the installation, before and after the fix. Not
  covered natively: a return whose normal server pauses its recovery, an entry refused because
  an earlier session is unsettled, a Tarkova Cola recovery failure, the hard stops, the helper
  side of a first session after a move, an unfinished foreign session, and no Arena raid was
  played in the fail-soft run. Records:
  [rewards paused](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/FAIL-SOFT-NATIVE-20261119.md),
  [moved installation](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/MOVED-INSTALL-NATIVE-20261119.md).
- **The foreign file in the reward history** was recovered on 2026-11-20 on a test profile
  (24 conflict copies moved out, one normal server start paid 433,000 RUB and 43,300 XP); the
  fix itself is covered by tests and was not deployed to the hosts when its record was written.
  Record:
  [reward history](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/REWARD-HISTORY-CONFLICT-20261120.md).
- **Mods.** PitFireTeam 1.1.2, TarkovaCola 3.0.1 and VisualAssist 1.2.0 were run on 2026-11-20
  (protected Tactical with two companions, Zombies with a companion and Cola, standalone and
  Arena quick throws); the record calls Cola's native research progress inconclusive. DynamicMaps
  1.3.0 with the Domination markers was run natively on Iceberg and Factory, with one Factory
  read pair recorded as non-evaluable. Tarkova Cola 4.0.0 and FOV Fix 5.0.1 were run on
  2026-11-22 for the listed body transitions, the isolation of ordinary and Arena offerings and
  v4 admission; the record says it is not a playthrough of all fifteen perks, all rewards, Scav
  continuation or every mod combination, and that its rollback case is checked offline only.
  WTT Armory 2.0.5 was run natively for the AN-94 body transition, standalone and in Arena;
  the review of the other reviewed mod versions (Forge audit, WTT Content Backport and
  CommonLib, the version refresh of seven further mods) is a source, release-metadata and
  installed-binary review, not an in-game test. Records:
  [mod refresh](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/GPU-MOD-REFRESH-20261120.md),
  [DynamicMaps](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reviews/DOMINATION-MAP-MARKERS-20261101.md),
  [Cola and FOV Fix](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/COLA-FOV-NATIVE-20261122.md),
  [WTT Armory](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/WTT-ARMORY-20261121.md),
  [WTT audit](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/reports/2026-11/WTT-WHITELIST-20261121.md).

Tactical, Zombies, Akimbo, Molotov and the Translocator remain WIP, and the limits of
[0.4.0](https://github.com/maschine34675/ArenaMode/blob/v0.7.0/docs/RELEASE-0.4.0.md#validation-and-remaining-limits)
still apply. Fika is not supported.

## Native release check

On the GPU test host SPT-GPU-2 (SPT 4.1.6) with these exact binaries loaded, together with
Redeploy 1.1.4 and the published ArenaMapPort 0.7.0: Deathmatch on Chop Shop with six
participants (five bots active, the map screen showing `Recommended: 6`); Team Deathmatch on
Block, 4v4 from the pockets, with all twenty box markers restored, the formation ready, the gates
opening 5.01 s after the release with every formation path revalidated, both factions alive and
an Arena respawn through Core 1.0.28 into the north pocket; and a Redeploy respawn near the corpse
in a normal Factory raid (`Respawn complete.` 5.1 s after the death). A third-party plugin that
another session had installed that day and that is on no whitelist kept the ARENA toggle at
`MOD CONFLICT`, so the two Arena raids ran in a private Arena session, entered and left through
the 0.7.0 session helper (`START ARENA · RESTART` and `RETURN TO SPT · RESTART`). No warning or
error from ArenaMode, ArenaMapPort, Redeploy or Core in the three game sessions of the run; Unity
logged two Animator notes, one collider note on Factory and one `MenuScreen` coroutine error
while the Factory raid was being left, which is in every earlier session of that day on the host
as well. After the run the configuration was set back (one menu value) and the test profile
differed from a copy taken before the installation only by the ordinary Factory raid and SPT's
own mod list. Not run: Gun Game, Domination, Tactical, Zombies and PitFireTeam with these
binaries, a match to its limit or reward, any Block mode but Team DM, the mods of the Core 1.0.25
to 1.0.28 integrations as such, and anything on the second test host. Record:
[GPU-RELEASE-070-20261122.md](reports/2026-11/GPU-RELEASE-070-20261122.md).

## Package checks

Built from `259d6dfb7a0164845ceaa6375433dbe244941b9e`. ZIP SHA-256:
`3a4e21c970b3fa9f0ac17bf8f33e40354c3f6ffb443ffa6845dfac9220c06b14` (7,466,372 bytes, 74
files). The package manifest records Core 1.0.28 from Redeploy `50c3df0` as a verified input;
the bundled Core is byte-identical to the one in Redeploy 1.1.4. Package contents, paths,
hashes, defaults, 31 notice links and 41 audio provenance records passed the package verifier,
and the PitFireTeam rule (no PitFireTeam binary, no byte-identical reference payload) passed
against 335 reference hashes.

Fresh checks in the isolated source snapshot: 4,566 passed with four environment-dependent
skips (two reward-recovery fixtures, the installed-APBS probe and the installed-PitFireTeam
transaction-target probe). The full offline verification register was not run again on the
release tree: the owner asked that the late Chop Shop change should not set off a complete
rerun. Its features were checked on their own branches before they were merged (Block: 109 of
109 selected steps over `96a51cea`; the other records are linked above). After the last merge,
the Chop Shop change and this release commit, the solution built with 0 errors, the 525 tests of
the participant classes and `ParticipantMenuChecks` (seven negative controls) passed, and the
suite above passed on the release commit itself. No credential scan tool was run; the commits
published with this release were searched for keys, tokens, private addresses and binaries, and
none was found.

VirusTotal results below apply to these exact binaries (22 November 2026). The five DLLs had no
malicious or suspicious labels (71 engines without detection each). The unsigned helper
`maschine-ArenaMode.Session.exe` has **3 malicious labels** (CrowdStrike
`win/malicious_confidence_60% (D)`, VirIT `Trojan.Win32.MSIL_Heur.A`, Elastic `malicious
(moderate confidence)`) and 68 engines without detection, as the helper of every earlier release
had generic labels of this kind. These findings are unresolved; they are not established false
positives. No antivirus protection needs to be disabled.

| File | SHA-256 / scan |
|---|---|
| `maschine-ArenaMode.Gate.dll` | [b76050715023…](https://www.virustotal.com/gui/file/b7605071502302ea07c34dc3453e91869caf15cc8704530d783f03130582dd1f) |
| `maschine-ArenaMode.BigBrainCompat.dll` | [76ac6d979341…](https://www.virustotal.com/gui/file/76ac6d9793417d152095cce92d892ed4b1956f022f4fb9c452c60592f1f53901) |
| `maschine-ArenaMode.Client.dll` | [ae25eeb5b8c6…](https://www.virustotal.com/gui/file/ae25eeb5b8c6cac6ed57bc98aa5863933e862f76d033393ee35531c5141c505b) |
| `maschine-ArenaMode.Session.exe` | [8c31e4b3888f…](https://www.virustotal.com/gui/file/8c31e4b3888fd42d132bed6b66ae60281dd2c28f6c712c6bb95c00c63a0dee66) |
| `maschine-Redeploy.Core.dll` | [238d202642b9…](https://www.virustotal.com/gui/file/238d202642b9c3ec1a9895a022b3f8f0e1c4123ccb9863f86cc74f3d1f7802e5) |
| `maschine-ArenaMode.Server.dll` | [710d5e5d189b…](https://www.virustotal.com/gui/file/710d5e5d189b0eda91b4a335b8deb6445a36b46b7481e289508fac13185a9600) |
