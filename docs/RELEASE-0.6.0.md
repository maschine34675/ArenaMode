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

Pending: filled in after packaging and the native run.

## Package checks

Pending: filled in after packaging and the native run.
