# ArenaMode 0.6.0

14 November 2026. SPT 4.1.x. Single-player only. Successor of
[0.5.0](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/RELEASE-0.5.0.md).
Bundles shared **Redeploy.Core 1.0.24**, identical to the Core in Redeploy 1.1.3.

## New

- **Chop Shop** through ArenaMapPort 0.6.0 (`arena_autoservice`): Gun Game, Deathmatch,
  Team DM, Domination and solo Tactical and Zombies, with eight participants by default.
  Team DM uses the map's original pockets as team boxes (USEC east, BEAR west) for 2v2
  to 10v10, and Domination has its one capture point, X. Zombies with a PitFireTeam squad
  (a very small walkable area) and Tactical with a PitFireTeam squad (too few distant spawn
  points when the surviving leader stands in the yard centre) are not offered there. The
  saved map list gains Chop Shop once, and a participant count already saved for the map is
  not changed.
- **Tactical on Equator and Iceberg** (solo and with a PitFireTeam squad) and on Chop Shop
  (solo). The three maps were refused before because they had too few distant spawn points
  for five opponents; ArenaMapPort 0.6.0 ships more (Chop Shop 16, Equator 16, Iceberg 17),
  and the refusals were decided again against that data. Squad Zombies stay refused on
  Equator and Chop Shop. ArenaMapPort 0.6.0 also moves the two exits of each ported map
  into the team start areas; extraction stays disabled in Arena.
- **Domination on The Lab** (three points, A/B/C on floor 1), **The Labyrinth** (one
  point, X) and in the three **Customs areas** (Industrial Center, Industrial Side and
  Customs Yard; three points each, team size 2 to 5 per side chosen like Team DM's). Not
  with PitFireTeam, and not on whole Customs. The points are proved again against the
  loaded scene at match start; if no candidate of a point passes, the match stops instead
  of playing a smaller layout. Tactical has no capture point on these maps.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reviews/DOMINATION-VANILLA-MAPS-20261113.md).
- **Domination polish and rewards:** on the vanilla maps the point's letter is readable
  from afar (a bold white letter with a dark edge on a chip in the owner's colour, at a
  constant apparent size); on the ported maps, which have the ring and the map markers,
  the world letter is no longer drawn. Bots now have Team DM's spawn protection in
  Domination on the ported maps. Domination pays profile rewards for eligible kills (see
  *Profile rewards*).
- **KITS window and own kits:** **KITS** on the Arena map screen shows the kit catalogue as
  cards, cheapest first, with the game's picture of each weapon, the armour and helmet class,
  the value and the full contents of the card under the pointer. Pick one kit for every life
  or keep the random draw, and mark favourites; a raid keeps the choice it started with. Its
  top-right button reads **DONE**, and the kit window's buttons no longer flash white when
  they are built. **COPY TO MY KITS** makes an own kit, and an editor changes it slot by
  slot with every change checked. An own kit costs the handbook sum of its items, is handed
  out in every mode when it costs no more than the dearest stock kit, and solo Tactical sells
  it at its price. Own kits are kept in
  `BepInEx/config/com.maschine.ArenaMode.custom-kits.json`. Solo Tactical's buy phase shows
  the same cards with prices, the purse and the countdown.
  [Kit screen](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reviews/KIT-PICKER-20261110.md),
  [own kits](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reviews/CUSTOM-KITS-20261110.md).
- **Own gear in Deathmatch, Team DM and Domination:** the EQUIPMENT row offers "Arena kits"
  or "Own gear" there, as it already did for Zombies and PitFireTeam Tactical (one saved
  preference per mode, off by default). Your protected equipment replaces the kit and is
  restored with full ammunition on every respawn. Gun Game and solo Tactical keep their
  ladder and buy economy.
  [Contract](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reviews/OWN-GEAR-ALL-MODES-20261109.md).
- **Tactical capture point** (solo and PitFireTeam, default on, F12 `Tactical/Objective`):
  a point appears 60 s into the round's combat and the side that holds it alone for 10 s
  wins the round; elimination still ends a round first. Maps without a capture point layout
  and areas play by elimination. Tactical also shuts doors a round left open at the next
  round start (`Tactical/CloseDoorsAtRoundStart`, default on).
  [Details](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reviews/TACTICAL-OBJECTIVE-20261108.md).
- **Fixed seats and a held scoreboard:** Team DM and Domination tables keep a seat per
  participant (a replacement bot takes over the name and running stats of the body it
  replaces, a vacant seat reads OUT), generated bots carry no backpack, and holding the
  inventory key shows the scoreboard while you keep moving and shooting.
- **Private session:** the private Arena home menu shows a sunlit indoor training course
  during startup and profile loading, hides the orange beta notice, and the HUD hides the
  lower-left version line during a confirmed Arena raid. The direct-start launcher's compact
  header shows the ArenaMode emblem.

## Fixed

- **A multi-round match paid nothing** ("no contest or incomplete combat evidence") when EFT
  credited a kill to a body of an earlier round, a tripwire or grenade of a life that ended
  before the round. Such a kill is now captured as a death without a killer; the match
  stays valid. Reported in
  [maschine34675/ArenaMode#2](https://github.com/maschine34675/ArenaMode/issues/2).
- **Profile rewards stopped for good** after one Arena raid whose end the server could not
  confirm: queued rewards were never paid. The server now closes such a restoration without
  payment, and the profile's other rewards pay again at the next normal start.
- **Leaving a match whose reward capture holds no combat round** (solo Tactical's first buy
  phase) no longer logs a reward Warning on both sides or shows "No reward for the last
  match."; the server accepts it as an ordinary no-pay end.
- **Tarkova Cola reward history** of a wiped, restored or moved profile no longer keeps
  rewards off for good: a complete history is archived and enrolled again at the next normal
  server start, with the reward choice kept.
- **Solo Tactical squad** is requested only after you have been moved to the round's start
  point, so its spawn points keep 25 m from where you actually start (on small maps the
  squad could appear 10 to 15 m from your new start).
- **Arena refuses to start**, with the reason in the menu and at raid start, when its
  map-exit blocking failed to install (a map exit at the team start positions could end the
  raid about 8 s after spawning). With SAIN's bot extraction switched on, Arena bots no
  longer leave a match through a map exit.
- **Zombies** wait two seconds after a failed spawn request before the next attempt instead
  of retrying every pacing interval.
- **Spare magazines** that hold more than one stack of their round are handed out full: PKM,
  PKP and M60 boxes, the RPD box and 50-round and 75-round drums came short before. Loose
  reserve rounds no longer form a stack larger than the round allows.
- **Descriptions** of Tactical and Zombies name the PitFireTeam option again ("PFT
  optional"); a refused native bot arrival in Team DM and Domination names the gate it
  failed in.

## Compatibility

- **Shared Core 1.0.24** (identical to Redeploy 1.1.3). Cores 1.0.21 to 1.0.23 were never
  released on their own; this is the first release that carries their changes, on top of the
  1.0.20 of 0.5.0. Core 1.0.21 adds handovers for Climbable Ladders 1.0.4, P.L.A.T.E. 1.6.0,
  Loot In Vicinity 3.0.6 and Skills Extended 3.1.4; 1.0.22 for Amand's Sense Updated 3.1.0;
  1.0.23 for Dynamic External Resolution Patch 1.1.2 and Player Encumbrance Bar 1.3.0;
  1.0.24 for ORBIT 3.1.1, Questing Bots 1.2.0 and ISB SOF Notifier 2.0.4. The public
  `RespawnApi` is unchanged (version 1). What was run in the game and what only offline is
  stated in Redeploy's compatibility records (`docs/compatibility`): the 1.0.21 handovers
  are recorded there with offline checks and in-game verification pending, Amand's Sense
  ran in an Arena Deathmatch with a temporary allowance, the DERP and Encumbrance Bar
  handovers ran on Factory (see below), and the ORBIT, Questing Bots and ISB handovers ran
  standalone only, with Arena combinations excluded and not tested. This ArenaMode refuses
  Core 1.0.23 or older.
- **Newly admitted mods:** Bot Callsigns Reloaded 2.0.6, Player Encumbrance Bar 1.3.0
  (Arena displays its effective carry limits in the bar), Auto Shoulder Swap 1.1.1 (Arena
  guards its unsafe non-firearm callbacks), Dynamic External Resolution Patch 1.1.2, Caliber
  Under Ammo Name 1.2.0, Compatibility Highlighter 1.3.2, Trader Modding and Improved Weapon
  Building 2.1.2 (both halves) and Amand's Graphics 1.8.0. No third-party binaries are
  bundled. [Four-mod checks](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-FOUR-MOD-COMPAT-20261113.md),
  [inventory UI mods](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-INVENTORY-UI-MODS-20261110.md),
  [Amand's](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-AMANDS-20261108.md).
- **ArenaMapPort 0.6.0** is the version this release is made for. It adds Chop Shop and more
  bot spawn points on every ported map (Chop Shop 16, Equator 16, Bay5 16, Iceberg 17, Air
  Pit 20, Bowl 21). With the older ArenaMapPort 0.5.0 Chop Shop is not offered; Equator,
  Bay5, Air Pit, Bowl and Iceberg keep working in Gun Game, Deathmatch, Team DM, Domination
  and Zombies as with ArenaMode 0.5.0. Tactical on Equator and Iceberg is offered with 0.5.0
  too, but 0.5.0 has too few distant spawn points there (Equator 10, Iceberg 9): a solo squad
  can arrive short or late, and a PitFireTeam Tactical match can end as No Contest. ArenaMode
  does not detect the ArenaMapPort version, so install 0.6.0. Iceberg and the full crowd
  reactions need ArenaMapPort 0.5.0 or newer.
- **PitFireTeam 1.1.1** is unchanged.
- The whitelist records ArenaMapPort 0.6.0, Redeploy 1.1.3 and PitFireTeam 1.1.1; a
  differing recognized version stays advisory, and user policy is preserved.
- Fika is not supported.

See the [versioned changelog](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/CHANGELOG.md#060---2026-11-14) for the full list.

## Profile rewards

Optional **PROFILE REWARDS** are on by default for every profile that is not yet
registered. At the next normal server start ArenaMode.Server adds a small data
block to the profile file. It credits RUB mail and PMC XP only after Arena
matches. Switch rewards off with the rewards card in the Arena map menu. Domination now
pays for eligible kills like Gun Game and Zombies (1,000 RUB and 100 XP each under the
default policy, capped per match, no match-win bonus); with Tactical's capture point on,
Tactical pays kills only. Existing reward choices are kept.

## Installation

Close game and server first, back up your profile, then extract
**ArenaMode-0.6.0.zip** into the SPT root, merging `BepInEx/` and `SPT_Runtime/`.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.24**. The same
Core is bundled in Redeploy 1.1.3; the standalone Redeploy client/server are
optional. Never replace a newer Core with an older copy. BigBrain, its
prerequisites and ArenaMapPort are separate downloads; install **ArenaMapPort 0.6.0**
for Chop Shop and the Tactical spawn points. Existing F12 settings, whitelist choices,
kits and ladder are preserved.

## Validation and remaining limits

All runs below were native, on the GPU test host SPT-GPU-2 unless noted, and are dated
evidence for the builds they name, not for the release binaries (those follow below).
Chop Shop was run on 2026-11-12 (menu, Deathmatch, Team DM 4v4 and 10v10, Domination, solo
Zombies, a plain PMC raid) with an ArenaMapPort build that had seven bot spawn points
there, and again on 2026-11-13 with the 16 of ArenaMapPort 0.6.0's data (Domination, solo
Zombies from the yard centre, Team DM with the pocket exits). Solo Tactical was run on
Chop Shop, Equator and Iceberg (three rounds each) and PitFireTeam Tactical on Iceberg and
Equator on 2026-11-13, with a test timing that excludes a capture-point hold; no Tactical
match was played to its end. The dispatch order of the solo squad was measured on the same
day. Domination on The Lab, The Labyrinth and the three Customs areas, the kit window
without the button flash, the bot spawn protection and the point letter were run on
2026-11-13; no match on the new maps was played to its score or time limit. The kit screen
and own kits were run on 2026-11-10 and 2026-11-11 (one map, Factory, a normal session), own
gear on 2026-11-09 in Deathmatch and Team DM, the Tactical capture point on 2026-11-08 on
Factory, Domination rewards (one kill paid at the next normal start) and the Cola archive
on 2026-11-08, and the multi-round reward case on 2026-11-09 in a three-round Labyrinth
Deathmatch. Four of the newly admitted mods (Bot Callsigns Reloaded, Player Encumbrance Bar, Auto
Shoulder Swap, Dynamic External Resolution Patch) were run on Factory on SPT-GPU-1 on
2026-11-13.

Not run: Gun Game on Chop Shop, PitFireTeam on Chop Shop, a match played to its result and
reward on any of the new maps or modes, PitFireTeam Tactical to the end of a match, a lost
solo squad dispatch race, anything on the published ArenaMapPort 0.5.0 data (the
behaviour described under Compatibility follows from its spawn-point counts), Tactical's
card buy screen with an unaffordable kit, the kit screen in Domination or
Team DM raids with a fixed kit, own gear in Domination, and the kit handbook line in an Arena session. The
Core 1.0.21 handovers have no in-game record. Tactical, Zombies, Akimbo and Molotov
remain WIP, and the limits of
[0.4.0](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/RELEASE-0.4.0.md#validation-and-remaining-limits)
still apply. PitFireTeam 1.1.1 refuses to spawn a companion that was wounded in a normal
raid; Tactical and Zombies with companions then do not start until the companion is healed
in PFT's own menu. Records:
[Chop Shop](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-CHOP-SHOP-20261112.md),
[spawn rows](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-SPAWN-ROWS-20261113.md),
[squad order](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-SOLO-SQUAD-ORDER-20261113.md),
[pre-release polish](https://github.com/maschine34675/ArenaMode/blob/v0.6.0/docs/reports/2026-11/GPU-PRE-RELEASE-POLISH-20261113.md).

## Native release check

On the GPU test host SPT-GPU-2 (SPT 4.1.6) with these exact binaries loaded, together with
Redeploy 1.1.3, PitFireTeam 1.1.1 and the ArenaMapPort build `0.6.0+8755049c` that was installed
on the host that day (ArenaMapPort 0.6.0 itself was not published yet when the run took place):
Team DM on Chop Shop from the pockets with an Arena respawn through Core 1.0.24; Domination on
The Lab with its three points verified and both teams holding a point within half a minute;
solo Tactical on Equator over three rounds with five opponents each; a PitFireTeam Tactical
match on Equator with two companions, whose second round started with the companions respawned
through Arena's lease and five opponents planned again, without a no-contest end; the KITS
window with its DONE button; Domination in Customs Yard, 3v3, with the area, its three points
and the routes from both starts verified; and a Redeploy respawn near the corpse in a normal
Factory raid. No Arena, Core or Redeploy warning or error beyond one known note: the two
Tactical runs used a test timing that excludes a capture-point hold, and Arena says so in a
warning. The other warnings were third-party and engine notes (PitFireTeam 1.1.1's HarmonyX
note for the optional Questing Bots it does not find, one ArenaMapPort `[Physics] impact sound
skipped` warning, the start-up notes of third-party plugins, Unity's scene notes), and one
native `BotFirstAid` exception fell into the seconds in which a raid was being left. After the
Customs Yard raid the profile differed from a copy taken just before it in one Hideout
timestamp (a first comparison across the installation also showed SPT's own start-up changes
and was not counted); every PitFireTeam server file was byte-identical across the Arena raids,
and the two settings the menu had changed were put back. Not run: Gun Game, Zombies, Domination
on Chop Shop and The Labyrinth, Tactical on Iceberg and Chop Shop, a match to its limit or
reward, a capture-point hold, the spectator camera, the Session.exe path and a private Arena
session, the published ArenaMapPort 0.5.0 data, the mods of the Core 1.0.21 to 1.0.24
handovers, and anything on the second test host. Record:
[GPU-RELEASE-060-20261114.md](reports/2026-11/GPU-RELEASE-060-20261114.md).

## Package checks

Built from `39dfcf77345fb08cbe47fe656af397dba7cef531`. ZIP SHA-256:
`8d260af6cb78486877cc4ffb0b86eeb2e1c408f953f6a797422088f016b27857` (7,356,945 bytes, 74
files). The package manifest records Core 1.0.24 from Redeploy `67f8303` as a verified input;
the bundled Core is byte-identical to the one in Redeploy 1.1.3. Package contents, paths,
hashes, defaults, 31 notice links and 41 audio provenance records passed the package verifier,
and the PitFireTeam rule (no PitFireTeam binary, no byte-identical reference payload) passed
against the 1.1.1 reference tree.

Fresh checks in the isolated source snapshot: 3,916 passed with four environment-dependent
skips (two reward-recovery fixtures, the installed-APBS probe and the installed-PitFireTeam
transaction-target probe). Before packaging, every step of the offline verification register
(144 harness steps) and the unit test suite ran uncached on the release tree as it stood
before the last document corrections and the repair of one harness (ten files differ from the
release commit, no mod source among them). 141 steps and the suite pass (3,920 tests, 3,917
passed, the three known environment skips); one of them, `NativeStopChecks`, did not compile in
that run, was repaired in the release commit and passes with all five of its mutants
rejected; it, the three other steps the later changes touch and the suite ran again,
uncached, on the release commit. The other three steps cannot run on the build machine
because they compare with PitFireTeam
reference trees that are not on it (`PitFireTeam100Checks` needs the 1.0.0 release,
`PitFireTeamRoundLifecycleChecks` and the integration stage of `PftTacticalKitChecks` the
beta 0.10.0 source); the PitFireTeam 1.1.1 checks ran. No credential scan was run for this
release.

VirusTotal results below apply to these exact binaries (14 November 2026). The five DLLs had no
malicious or suspicious labels. The unsigned helper `maschine-ArenaMode.Session.exe` has **3
malicious labels** (CrowdStrike `win/malicious_confidence_60% (D)`, Elastic `malicious (high
confidence)`, McAfeeD `ti!ECC6EF535AFA`) and 66 engines without detection. These findings are
unresolved; they are not established false positives. No antivirus protection needs to be
disabled.

| File | SHA-256 / scan |
|---|---|
| `maschine-ArenaMode.Gate.dll` | [361fe10f5a5d…](https://www.virustotal.com/gui/file/361fe10f5a5dfd97fdfdaa5bbb181704c51befd77e8dd7376a02025cbc072b67) |
| `maschine-ArenaMode.BigBrainCompat.dll` | [f8a7ddce276d…](https://www.virustotal.com/gui/file/f8a7ddce276d9ad6c27c5e0eb98209f15ca9802b2fb74aa862c0d0df1e564762) |
| `maschine-ArenaMode.Client.dll` | [4b05b3e52f5f…](https://www.virustotal.com/gui/file/4b05b3e52f5f1373a8d449c708708fa203f668bd8ab338b86d6126e0a1dd4cf3) |
| `maschine-ArenaMode.Session.exe` | [ecc6ef535afa…](https://www.virustotal.com/gui/file/ecc6ef535afab89094d25ea1da6af5d12baac40a11adf7e13eb3e96c90814bff) |
| `maschine-Redeploy.Core.dll` | [aa46ec8495a7…](https://www.virustotal.com/gui/file/aa46ec8495a7ea01a671e4612cc84e08a26af4e4b9979860038d900e941b915b) |
| `maschine-ArenaMode.Server.dll` | [df910371f1c1…](https://www.virustotal.com/gui/file/df910371f1c10a7dbc5df373f2f562fd4b54a90afa4c158fed88d1de64002af9) |
