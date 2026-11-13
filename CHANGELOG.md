# Changelog

Notable changes since **v0.1.0**, reconstructed from the Git history and published
release notes. Dates below are GitHub publication dates.

Released entries describe the behavior **at that release**. Several compatibility
adapters were subsequently removed or moved to Redeploy.Core, as noted under
0.2.0. See [Dependencies and compatibility](docs/DEPENDENCIES.md) for the current
state. Allowing a mod through the whitelist is not a compatibility guarantee.

## Unreleased

- Admit Bot Callsigns Reloaded 2.0.6, Player Encumbrance Bar 1.3.0,
  Auto Shoulder Swap 1.1.1 and Dynamic External Resolution Patch 1.1.2.
  Arena guards Shoulder Swap's unsafe non-firearm callbacks and displays
  Arena's effective carry limits in Encumbrance Bar. Shared Core 1.0.23
  corrects the encumbrance and resolution mods' body references after respawn.
  Scoped Factory checks passed through actual Arena respawn and standalone
  Redeploy's PMC-to-own-Scav continuation. No third-party binaries are bundled.
  [Scope](docs/reviews/FOUR-MOD-COMPAT-20261113.md),
  [native checks](docs/reports/2026-11/GPU-FOUR-MOD-COMPAT-20261113.md)
- Arena now refuses to start, with the reason in the menu and at raid start, when its map-exit
  blocking failed to install. ArenaMapPort's newer maps put an exit right at the team start positions,
  so an Arena raid without that blocking could end about 8 s after spawning. Ordinary raids are unchanged.
  [Contract](docs/reviews/EXTRACTION-HARDENING-20261113.md)
- With SAIN's bot extraction switched on in its preset (off by default), Arena bots no longer leave
  a match through a map exit; ordinary raids keep SAIN's setting.
  [Contract](docs/reviews/EXTRACTION-HARDENING-20261113.md)
- Added optional **Chop Shop** support through ArenaMapPort in Gun Game, Deathmatch, Team Deathmatch,
  Domination and solo Zombies, **without Tactical or PitFireTeam Zombies** (seven bot spawn points,
  none beyond the 25 m floor from the yard centre; a very small walkable area). Default is six
  participants. Team DM and Domination use the map's original pockets, USEC east / BEAR west, for
  2v2-10v10, and Domination has its one capture point. The map needs an ArenaMapPort build that
  ships it (not yet released); the saved map list gains it once through optional-map revision 6.
  It ran on a test machine on 2026-11-12 (menu, Deathmatch, Team Deathmatch 4v4 and 10v10, Domination,
  solo Zombies); its gates get a 15 s watchdog because the match flow polls the opening once a second.
  Known limits: Domination places 5 of 6 pickups, and the yard centre is a dead patch for spawns
  (the map has only seven bot spawn points).
  [Contract](docs/reviews/CHOP-SHOP-INTEGRATION-20261112.md),
  [native report](docs/reports/2026-11/GPU-CHOP-SHOP-20261112.md)
- Zombies: when an infected spawn request fails (no usable spawn point), the wave now waits two seconds
  before the next attempt instead of retrying every pacing interval. The slot is still retried. In a spot
  without a usable spawn point this took the dropped requests and warnings from 133 in 30 s to 18 in 36 s.
  [Contract](docs/reviews/CHOP-SHOP-INTEGRATION-20261112.md)
- Build your own kits: COPY TO MY KITS on any kit in the KITS window makes an own kit, and the
  editor changes it slot by slot (weapon, ammunition, armour, rig, helmet, headset, backpack) from
  lists of what each slot takes, every item arriving as the game's own default build and a weapon
  loaded with its rounds. Every change is checked (slot rules, conflicts, required parts, supplies
  fitting) and saved at once. An own kit costs the handbook sum of its items; it is handed out in
  every mode when it costs no more than the dearest stock kit, and solo Tactical sells it at its
  price either way. Own kits come exactly as built (no attachment draw, no extra grenades) and are
  kept in `BepInEx/config/com.maschine.ArenaMode.custom-kits.json`, the same file in a normal and an
  Arena session. The config page's "Reload from disk" no longer leaves every stock kit at price 0.
  [Contract](docs/reviews/CUSTOM-KITS-20261110.md)
- Pick your kit: **KITS** on the Arena map screen opens the catalogue as cards, cheapest first, with
  the game's own picture of each weapon, the armour and helmet class, the value and the full contents
  of the card the pointer rests on (crossing other cards on the way to COPY TO MY KITS keeps the
  kit). Pick one kit for every life or keep the random draw, and mark favourites. A picked kit
  keeps its per-raid attachment draw. The choice is the F12 pair `Kits/RandomKit` and
  `Kits/SelectedKit` as before; a raid now keeps the choice it started with (an F12 edit made in a
  raid counts from the next one). New F12 key `Kits/FavoriteKits`.
  Solo Tactical's buy phase shows the same cards with prices, the purse and the countdown: a kit you
  cannot afford stays visible, NO PURCHASE takes a purchase back, READY starts the round, CLOSE lets
  you walk. The plain buy window remains as the fallback, and during a buy phase the panel hotkey
  reopens the buy screen instead of also opening the scoreboard.
  [Contract](docs/reviews/KIT-PICKER-20261110.md),
  [native runs](docs/reports/2026-11/GPU-KIT-PICKER-20261110.md)
- Leaving an Arena match whose reward capture holds no combat round (solo Tactical's first buy
  phase, or a match whose first round ended before it went live) no longer logs a reward
  Warning on both sides (`reward-capture-identity` in the game, `reward-final-report-missing` on
  the server) or shows "No reward for the last match." in the menu: the server now accepts such a
  final report without a round as an ordinary no-pay end. Nothing was paid before and nothing is
  paid now; the profile restoration and its proof are unchanged.
- Fixed profile rewards stopping for good after one Arena raid whose end the server could not
  confirm before it stopped (`return-profile-save-not-confirmed`): at every later normal start the
  reward pass for that profile stopped at `reward-checkpoint-profile-mismatch` (a Debug line), so
  queued rewards were never paid and new ones only queued up. The server now closes such a
  restoration without payment, as it already did for other unproven ones, and the profile's other
  rewards pay again at the next normal start. [Report](docs/reports/2026-11/REWARD-UNPROVABLE-CHECKPOINT-20261110.md)
- Allow Caliber Under Ammo Name 1.2.0, Compatibility Highlighter 1.3.2 and both
  halves of Trader Modding and Improved Weapon Building 2.1.2 in Arena sessions.
  [Compatibility checks](docs/reports/2026-11/GPU-INVENTORY-UI-MODS-20261110.md)
- The private Arena home menu hides the orange beta notice. During a confirmed
  Arena raid the HUD hides the lower-left version line, including when playing
  Arena directly from a normal SPT process.
- The private Arena session shows a sunlit indoor training course from the
  desktop profile picker through EFT startup and profile loading. In the home
  menu the foreground moves subtly with the mouse. [Details](docs/reviews/SESSION-MENU-ART-20261109.md)

- Bring your own gear in Deathmatch, Team DM and Domination: the EQUIPMENT row of the map screen
  offers "Arena kits" or "Own gear" there as it did for Zombies and PitFireTeam Tactical, one saved
  preference per mode (new F12 keys `Deathmatch/OwnGear`, `Team Deathmatch/OwnGear`,
  `Domination/OwnGear`, all off by default). With own gear your real, protected equipment replaces
  the kit: it is restored with full ammunition on every respawn and at Deathmatch's round break, the
  secure container stays as it is, nothing is lost or gained, and items picked up in a life are not
  kept across a restore. Gun Game keeps its ladder (the ladder is the loadout) and solo Tactical its
  buy economy (the gear is earned in the match).
  [Contract](docs/reviews/OWN-GEAR-ALL-MODES-20261109.md). Run natively on GPU-2 on 2026-11-09: Deathmatch
  (raid start, death respawn, round break) and Team DM (three respawns) each restored the entry loadout
  with full ammunition ([report](docs/reports/2026-11/GPU-OWN-GEAR-20261109.md)). The corpse cleanup leaves
  the retained slots and the pockets on the player's own corpse since that run: a round-start clear had
  stripped the bayonet before a held Tactical respawn read it, which the restore rightly refused.
- The Tactical and Zombies descriptions in the map screen name the PitFireTeam option again
  ("PFT optional", as the row labels abbreviate it): since the compact copy of 2026-10-26 a solo player
  had no visible hint that both modes can be played as a squad.
- Spare magazines that hold more than one stack of their round are handed out full: the PKM/PKP
  and M60 boxes came with 40 of 100 rounds, the RPD box with 60 of 100, and 50-round and 75-round
  drums short the same way, in full kits and in Gun Game weapon swaps alike. Loose reserve rounds
  never form a stack larger than the round allows (only the MSGL's 40 mm grenades were affected).
- Fixed a multi-round match paying nothing ("no contest or incomplete combat evidence") when
  EFT credited a kill to a body of an earlier round: a tripwire or grenade of a life that
  ended before the round. On Labyrinth in Deathmatch this was close to certain in every match
  with more than one round, because the map re-arms its tripwires for every Shadow of
  Tagilla life and credits a trap to its planter; a bot's grenade that goes off after a round
  change did the same on any map. Such a kill is now captured as a death without a killer: it
  pays nobody, the match stays valid, and the game's own scoring of it is unchanged.
  Single-round matches were never affected. Reported with
  the analysis and a tested fix in
  [maschine34675/ArenaMode#2](https://github.com/maschine34675/ArenaMode/issues/2). Run natively
  on GPU-2 on 2026-11-09: a three-round Labyrinth Deathmatch met the case in round 2 and completed.
- Add Amand's Graphics 1.8.0 to the Arena whitelist. Its Labyrinth lighting
  initializes before the starting kit is handed out, and its ON/OFF setting
  survives respawns. [Native test](docs/reports/2026-11/GPU-AMANDS-20261108.md)
- A wiped profile, a restored pre-enrollment backup or a moved installation whose Tarkova Cola reward history is complete is now archived and enrolled again at the next normal server start, like any other such profile; the reward choice is kept (this needs a runtime-managed enrollment with a saved reward choice, as the automatic archive always did; otherwise the Warning now says so). Before, any Cola history kept rewards off for good with a Warning at every start, and only the documented manual rename helped. An unfinished or unreadable Cola reward history still keeps the quarantine, and an unfinished Cola reward still stops startup with the real cause.
- Team DM and Domination tables have fixed seats, as in live Arena: a replacement bot takes over the seat, the name and the running stats of the body it replaces (shown under that name in the scoreboard, killfeed and death notices; the bot profile is not written), and a vacant seat stays on the table as OUT until the replacement arrives. Before, every replacement started a new row under a new name. [Details](docs/reviews/FORK-QUICK-WINS-20261108.md)
- Hold the inventory key (Tab by default; the bound keyboard key is read, a mouse-bound key keeps its stock behaviour) in any Arena raid to see the scoreboard tables while you keep moving and shooting; a tap still opens the inventory, under the default key setting on the release of the tap rather than on the press (at most a quarter second later). The F9 panel with its live settings is unchanged and wins over the held table.
- Generated Team DM and Domination bots carry no backpack any more (live Arena issues none); the armband pass removes it with its contents. Their reload magazines always came from the rig and pockets, so they fight as before; only loot, meds, grenades and loose ammo that had landed in the backpack are gone. No switch.
- A refused native bot arrival in Team DM and Domination now names the gate it failed in the retire Warning (stale request, faction, duplicate profile, capacity, group, registration) instead of one generic text.
- Domination now earns profile rewards for eligible kills (1,000 RUB + 100 XP each under the default policy, capped per match), like Gun Game and Zombies; there is no match-win bonus, because point ownership has no server-verifiable proof. The rewards card no longer says "No profile rewards in Domination". Existing reward choices are kept. Run natively on GPU-2 on 2026-11-08 (one kill paid at the next normal server start).
- Tactical (solo and PitFireTeam) gets an optional **capture point** (default on, F12 `Tactical/Objective`): on Factory, Bay 5, Air Pit and Bowl one verified point of the Domination layout appears 60 s into the round's combat (`ObjectiveAfterSeconds`, 0-300); the side that holds it alone for 10 s (`ObjectiveHoldSeconds`, 3-60) wins the round, both sides on it freeze the hold, an empty point resets it, elimination still ends a round first and the round timer is unchanged. With BigBrain installed the enemy squad spreads out across the middle of the arena until the point appears instead of rushing you (without BigBrain the squad plays as before); PitFireTeam companions are never driven, and in the team arena the squad leaves the point once the PMC is down. A ring, a beam and a `POINT` label mark the point, the Tactical status line adds `POINT in 23s` / `POINT you 40%` / `POINT contested`; maps without a layout and areas play by elimination (`no capture point here`), and the menu copy promises the point only where there is one. With the point on, profile rewards pay kills only (no server round-win reward; the in-raid roubles bonuses are unchanged), like Domination; the server admits that rule for Tactical. The appear and hold times are read at each round start, so a live F12 change applies from the next round; a Warning names an appear plus hold time beyond the round timer. Adapted from the fork survey's TeamFight objective. [Details](docs/reviews/TACTICAL-OBJECTIVE-20261108.md)
- Tactical shuts the doors a round left open at the next round start (`Tactical/CloseDoorsAtRoundStart`, default on); doors the map itself starts open, locked, broken and blocked doors and doors with map-script triggers are left alone.

## [0.5.0] - 2026-11-07

Iceberg and the crowd reactions need **ArenaMapPort 0.5.0** (published 2026-11-07); with
0.4.0 the map is not offered and only the crowd murmur follows the match. The package
bundles shared **Core 1.0.20**, identical to the Core in Redeploy 1.1.2.

### Maps

- Added optional **Iceberg** support through ArenaMapPort in Gun Game, Deathmatch,
  Team DM and solo and PitFireTeam Zombies, with the gold map label, lighting and pickup
  placement and eight participants by default. Tactical (solo and PitFireTeam) is not
  offered on Iceberg: its centre leaves too few distant bot spawn points for five
  opponents. Existing custom map allowlists are preserved; the exact previous default
  list gains Iceberg once. [Details](docs/reviews/ICEBERG-INTEGRATION-20261031.md)
- Team DM on Iceberg uses the original USEC west / BEAR east pockets for 2v2–10v10;
  combat waits for the spawn gates to open, and the gates stay open for respawns.
  [Details](docs/reviews/ICEBERG-INTEGRATION-20261031.md#team-boxes-commit-b)
- Domination on Iceberg with three original points (A/B/C) in the same pockets.
  [Details](docs/reviews/ICEBERG-INTEGRATION-20261031.md#domination-commit-c)
- Documentation for the Iceberg integration, its migration and its native-test
  boundary. [Details](docs/reviews/ICEBERG-INTEGRATION-20261031.md#not-claimed)

### Domination

- Added live Domination objectives on optional **DynamicMaps 1.2.1 or newer**: the current
  round's points appear on the map and the minimap as diamonds with their letter (A/B/C on
  Factory and Iceberg, A/B on Air Pit and Bowl, the original X on Equator and Bay5), light
  grey while neutral, blue for USEC, red for BEAR, yellow while contested, placed from the
  round's verified floors (Factory's random layout included) and only while a Domination
  match is live; they leave at the score or time limit, on a map change and at every stop.
  The diamonds and labels are fifty percent larger than first drawn, after the first GPU run
  found the native dot unreadable. Without DynamicMaps nothing happens, an incompatible
  version disables only this overlay, and no match rule, profile or DynamicMaps setting
  changes. Run natively on Factory (GPU-1) and Iceberg (GPU-2) on 2026-11-01.
  [Details](docs/reviews/DOMINATION-MAP-MARKERS-20261101.md)
- Domination leaves WIP: the mode row reads DOMINATION, and the default score limit is
  300 points instead of 200 (an existing F12 value is kept; the row's choices are
  unchanged). The default time limit stays ten minutes.

### Crowd

- Added an optional link to the spectator crowd of ArenaMapPort maps (F12 Audio,
  **Crowd Reactions (ArenaMapPort Maps)**, default on, frozen when the raid is armed; not in
  Zombies). The crowd murmur gets louder in the final round, at match point and in the last
  minute or the last five kills; this works with ArenaMapPort 0.4.0. With **ArenaMapPort
  0.5.0** the crowd also cheers your kills, awards and leads, jeers your deaths, groans at a
  lost lead and calls round starts and results, and every reaction makes the whole crowd roar
  or boo from all stands; a later ArenaMapPort can also let it complain when a match goes a
  minute without a kill. Because of that roar, a kill and a death in the same moment (within
  a second) give one reaction, the stronger. No game rules, scores or profiles change. Run in
  the game on 2026-11-02 and 2026-11-07 and heard by the author.
  [Details](docs/reviews/CROWD-REACTIONS-20261102.md)

### Rewards and sessions

- Fixed profile rewards being switched off at every server start, without naming a
  cause, when another server mod's startup step also used `OnLoadOrder.Watermark + 1`
  (BetterFlea 2.4.1 and 2.5.0 do, for a step that only snapshots the item table).
  Since 0.3.0 Arena's reward step refused the whole start for any other step in that
  priority range, before or after it; no profile was enrolled, nothing was recorded or
  paid, the normal session said "temporarily unavailable" and the Arena session's card
  said "Activation pending — return to the normal session, then re-enter Arena"
  forever. The reward step now runs at the earliest slot (`Watermark`, 0) and refuses
  only a step that really runs before it in SPT's order, the Warning names each such
  step with its type, assembly and priority, every refusal that recurs while the
  installation stays as it is says "Rewards are off: …" in both sessions, and the
  Arena session's card shows the reason instead of the round trip. Reported in
  [maschine34675/ArenaMode#1](https://github.com/maschine34675/ArenaMode/issues/1).
  [Details](docs/reviews/REWARD-STARTUP-ORDER-20261105.md)
- Failed session switches now report whether disk readback differs from RAM,
  RAM changed during readback, or both, plus a profile alias, up to three safe
  section labels and the final resume permission. One warning per failed attempt;
  no profile contents or extra success logging. Save protection is unchanged.

### Compatibility

- Fixed the Arena map screen stuttering: it dropped to about 48 FPS with one long frame
  every 0.2 s, because each refresh checked optional-mod compatibility four times and
  every check of a mod that is not installed rescanned all patched game methods. The
  screen now checks once per refresh, the adapters of absent mods no longer rescan, and
  clicks, the claim and the raid start still check fresh. Measured on the GPU test host
  on 2026-10-31: the screen went from 48 to about 60 FPS. The adapter fix lives in the
  shared Core (1.0.19, shipped here inside **Core 1.0.20**); this ArenaMode refuses an
  older Core. [Details](docs/reviews/MAP-MENU-ADMISSION-20261031.md)
- **Shared Core 1.0.20** (identical to Redeploy 1.1.2): Raid Review 1.10.0 records every
  physical life separately, HollywoodFX 3.0.0 refreshes its caches after a replacement and
  Hands Are Not Busy 1.7.1 keeps its hands reset off retired bodies; the handovers apply to
  Arena respawns as well as to Redeploy's. All three were validated offline only, not in the
  game. A client built from this source refuses Core 1.0.19 or older.
- **PitFireTeam 1.1.1** is a reviewed baseline (no advisory warning; 1.0.0, 0.10.3 and 0.10.0
  stay reviewed). Every member Arena reflects on is unchanged, but 1.1.1 fetches the saved
  follower settings asynchronously before it creates the companions, which broke Arena's
  follower respawn of rounds 2 and later in Tactical and Zombies; Arena now serves that
  preflight from the first round's result. The kill-report guard also covers 1.1.1's new
  SendKillReport overload, and the protected-stop fallback mirrors its two new resets. A
  companion wounded in a normal raid is refused by 1.1.1 at the Arena start; heal it in
  PitFireTeam's menu first. Builds below 1.1.0 are bound exactly as before. See
  [the review](docs/reviews/PITFIRETEAM-111-20261107.md).
- The whitelist records ArenaMapPort 0.5.0, Redeploy 1.1.2 and PitFireTeam 1.1.1; a
  differing recognized version stays advisory, user policy is preserved.

## [0.4.1] - 2026-10-29

Hotfix for [v0.4.0](https://github.com/maschine34675/ArenaMode/releases/tag/v0.4.0).

### Profile rewards and server startup

- A wiped or hand-edited profile, a backup restored from before rewards were
  enrolled, or a moved or copied installation no longer breaks Arena entry.
  In 0.4.0, START ARENA SESSION closed EFT and left the normal SPT server paused
  when the profile's reward data no longer matched its reward folder. Entry now
  checks reward authority before the normal server pauses. Such a session runs
  without rewards, and a Warning names the profile and the remedy.
- At the next normal start, the old reward folder of such a profile is renamed
  to `<profileId>.archived-<UTC yyyyMMddTHHmmssZ>-<8 hex>` (never deleted) and the profile is enrolled
  again, provided nothing in its records is unsettled. The reward choice is kept;
  an opt-out stays off. For a moved install the old ledger block is saved in the
  archive and then removed from the profile; payments already made stay in its
  mail. Records that are not settled keep the profile quarantined, reported as a
  Warning and in the menu.
- A wiped profile, a deleted profile file or a moved or copied installation whose
  Tarkova Cola reward history is complete no longer stops SPT at startup on every
  machine that shares the profile. In 0.4.0 the server died with "needs
  companion-file recovery" because the wiped profile no longer held the ledger it
  had to compare against (a moved installation, because the reward folder is
  bound to the old installation root). Startup now continues, the profile stays
  withheld from rewards and no reward or Tarkova Cola file is changed. For a
  wiped profile or a moved installation one Warning says why; a deleted profile
  file is logged at Debug level only. A Cola plan that was prepared but never paid
  and a stray temporary file in the plan folder do not stop startup. If a Cola
  reward was still being completed, or a file cannot be read, startup still
  stops, now with the real cause and the remedy that fits: a profile backup taken
  after that reward for a reward still being completed, the original
  installation for a moved one, a copy of the reward records for damaged ones.
- SPT no longer refuses to start when the SPT folder or a parent folder is a
  junction, symbolic link or mount point, or when the reward folder cannot be
  used. ArenaMode then skips rewards for that start, writes no reward data and
  logs one Warning. A held lock, a running private session or an unresolved
  Arena session record still stops SPT.
- A file error that can clear on its own (another program holding the profile,
  access denied) while the entry reads the reward data declines the entry before
  the normal server pauses; the helper reopens the normal game, and the next
  attempt can succeed. A missing profile file starts the session without rewards.
- If the entry proof cannot be written after the normal server paused, the
  server saves, shuts down and tells the helper, which asks you to start SPT
  again, instead of waiting on a server that stays paused.
- Returning from a session no longer stops normal startup over a damaged or
  oversized `enrollment.json`.
- The menu says "Rewards are off" with a pointer to the server log for a reward
  folder that cannot be used. With rewards switched off, you see the ordinary
  "rewards are off" text while old records stay unsettled.

### Sessions with several SPT installations

- A finished Arena session of one SPT installation no longer blocks Arena
  sessions of another installation of the same Windows user with a false
  "still running" message. Only a server actually running from the other
  installation's session, or a session there that is not closed, blocks; an
  unclosed session is refused before the normal server is touched, with a
  message naming the other installation.
- When an enter fails before the private session opens, the normal game reopens
  and the helper says why Arena did not start.
- Limit: if the other installation was deleted while its session was still open,
  the message names that installation's private session folder, which you delete
  by hand. Arena results that were never returned from it are lost with it.

### Compatibility

- Bundle shared Redeploy.Core **1.0.18**, identical to Redeploy **1.1.1**'s
  Core. It hands Recoil Rework Legacy and Fontaine FOV Fix over to Redeploy's
  Continue as Scav body like an ordinary respawn; Arena respawns behave as with
  1.0.17.
- The whitelist records Redeploy **1.1.1**; Redeploy 1.1.0 still loads with a
  version advisory.

### Documentation

- README, INSTALL.txt and the release notes state that profile rewards are on by
  default and that the server adds a small data block to each profile file at
  the next normal start; switch rewards off in the Arena map menu.
- Statements that need ArenaMapPort 0.4.0 say that it is not yet published. The
  Domination tooltip names its BigBrain requirement.

## [0.4.0] - 2026-10-29

### Domination and teams

- Add Domination (WIP), with continuous USEC/BEAR respawns, bot capture/defence
  assignments and configurable point/time limits. BigBrain is required.
- Factory day/night chooses three separated control points each match. Equator
  and Bay5 use one original point each; Air Pit and Bowl use two. Bot approaches
  must be capture-valid and reachable. Domination does not award profile rewards.
- Integrate objective status into the main score panel, improve ownership rings
  and keep pickups clear of objectives. ArenaMapPort monitors display scores
  (ArenaMapPort 0.4.0 or newer, not yet published; 0.3.0 works without monitors).
- Choose USEC or BEAR for Team DM/Domination; team colors and killfeed follow
  the selected side through death/respawn without changing the saved PMC faction.
- Add optional native PMC action voicelines and rotating TDM faction voices.
  Disable accidental native extractions during Arena matches.

### Sessions and profile handling

- Start directly from Session.exe, select/remember a profile or create a desktop
  shortcut, without opening normal EFT first.
- Harden restart acknowledgement, process isolation and return to normal SPT
  after safely refused entry. Failed shutdown stages remain visible in logs.
- Fix round-end stalls from concurrent hideout updates during restoration;
  verify the complete saved profile before releasing the protected end.
- Automatically discard uncertain old rewards and recover normal startup after
  stopped sessions lose disposable data, preserving normal-profile progress.
- Remove the false missing-snapshot warning when rewards are switched off.

### Menus and logging

- Shorten mode/mutator copy, keep one session-return control and put details in
  tooltips. Preserve a short character description and stable mode-text alignment.
- Default new installations to Team DM. Show Day/Night for Factory and Equator;
  keep the session indicator and HUD within their Arena contexts.
- Avoid repeated map-row compatibility scans that stalled the map menu.
- Move routine helper link cleanup to opt-in debug output; preserve lifecycle,
  retry and failure messages.

### Compatibility

- Bundle shared Redeploy.Core **1.0.17**, identical to Redeploy **1.1.0**'s Core.
  Includes ImpactCue/Enemy Markers, Quick Cancel and PiP body/camera fixes.
- Compose Use Items Anywhere access/reload settings and Fast Grenade Throw
  priority/input locks. Review HUD Breakout without adding an unnecessary adapter.
- Add the reviewed mods to the default whitelist; record ArenaMapPort **0.4.0**
  and Redeploy **1.1.0**. Recognized version differences remain advisory in both
  direct Arena play and new private sessions, including UIFixes.
- Keep identity, deny/dependency and protection/API checks. Amand's Graphics
  remains unlisted because its separate Labyrinth setup problem is unresolved.

See [release notes](docs/RELEASE-0.4.0.md), [dependencies](docs/DEPENDENCIES.md)
and the [native test report](docs/reports/2026-10/RELEASE-SWEEP-20261029.md) for scope and limits.

## [0.3.0] - 2026-10-25

### Profile rewards and private sessions

- Add optional normal-profile rewards, enabled by default for eligible profiles;
  existing OFF choices stay OFF. The Arena map screen shows the switch, rates and
  payment status. Eligible kills award 1,000 RUB + 100 PMC XP; DM/TDM/Tactical round
  wins award 10,000 RUB + 1,000 XP, capped at 200,000 RUB + 20,000 XP per match.
  Gun Game/Zombies currently award kills only; no-contest results do not pay.
- Validate results after saved profile restoration. Returning from a private
  session starts normal SPT and settles rewards before opening EFT: RUB arrives
  as mail and XP is already credited. Direct Arena play pays at the next normal
  server start. Permanent payment records prevent duplicate rewards.

- Stop the supported local normal server when entering a private Arena session,
  after native save/read-back and before copying the profile. Rewards can still
  be switched inside Arena. Returning starts normal SPT and recovers the final
  choice and signed results before loading profiles. Concurrent normal starts
  cannot open the same profiles while the private session owns them.
- Protect Tarkova Cola's separate progress during Arena. With both parts of
  Tarkova Cola 2.2.1+ installed and rewards enabled, eligible Zombie results can
  transfer research/augment/synergy progress and newly earned achievement rewards.
  No bottle/grenade refunds or equipped-perk settings are transferred; other
  modes keep temporary perks without permanent Cola progress.

### Compatibility and setup

- Bundle and require shared Redeploy.Core **1.0.13** in this release. Its new
  body-replacement fixes cover Fontaine FOV Fix, Manimal Ammo Loading Animations
  and Magazine Check Interrupt; the full standalone Redeploy mod is not required.
- Preserve Accurate Circular Radar and Simple Crosshair through player/camera
  replacement using shared Core adapters. Review versions 1.3.4 and 1.4.0,
  together with Better BepInEx 1.0.0, for the default whitelist. The new F12 menu
  still requires the stock ConfigurationManager.
- Add tested Fontaine FOV Fix 4.1.0, Manimal 2.0.0, Increase Look Direction 1.3.0
  and Magazine Check Interrupt 1.1.1 to the default list. Auto Shoulder Swap 1.1.1
  remains unlisted after a native knife/lean error; ordinary raids are unchanged.
- Document seven further mod reviews without adding them to the whitelist:
  Use Items Anywhere, ImpactCue, Enemy Markers, Amand's Graphics, HUD Breakout,
  Manimal's Quick Cancel and Picture in Picture Disabler. Use Items Anywhere's
  competing magazine-query patches already conflict during normal plugin loading;
  a later Arena admission check does not resolve that overlap.
- Adapt PitFireTeam's profile-protection contracts to 1.0.0, including both
  companion databases and the new recruitment, karma, insurance and cargo paths.
  Older supported versions keep their existing guards. Record 1.0.0 as reviewed
  in the shipped policy and adapter after the bounded native checks, removing
  its stale version warning and exclusion from version-filtered sessions.
- Offer an Arena session when an installed mod differs from its reviewed version.
  This option omits that mod on both sides, together with its hard dependents,
  for this session only. Direct Arena entry still treats version differences as
  advisory; the normal installation and user policy are not changed.
- Show a shared client/server version note once, even when the two components
  use different display names. Preserve genuinely different version warnings.
- Show the server's actual Arena-start refusal reason and remove the duplicate
  message caused by EFT also displaying inner exceptions.

### Gameplay and loading

- Disable native transit points in confirmed Arena raids. Ordinary raids and
  normal extracts retain their behavior.
- Preload the first three selected and validated Gun Game weapons during loading.
- Halve loose-ammunition reserves in Arena kits and weapon-only swaps, rounding
  the total. Loaded rounds and spare detachable magazines remain unchanged.

[Release notes and verification limits](docs/RELEASE-0.3.0.md).

## [0.2.1] - 2026-10-25

### Fixes

- **Arena session with content mods left out.** When the session left out a mod that adds
  items, clothing or traders and the profile held such records, SPT's shipped settings
  (`removeModItemsFromProfile` and `removeInvalidTradersFromProfile` off) marked the private
  profile copy invalid: the session's game failed to load and closed itself after about a
  minute, without a reason on screen. The private server's copy of `core.json` now switches
  SPT's own clean-up on, so those records are deleted from the disposable copy only (a modded
  weapon you carry is missing in the session and back afterwards). Your `core.json` and your
  real profile are not changed.

[Release notes and verification limits](docs/RELEASE-0.2.1.md).

## [0.2.0] - 2026-10-24

### Arena sessions and mod policy

- Added **START ARENA SESSION** to restart into an isolated Arena session when
  the normal session contains mods that are not allowed for Arena. The session
  uses a filtered client, a private SPT server and a disposable copy of the
  selected profile. Returning to the normal session restores the regular setup.
  Installations containing only allowed mods can still enter Arena directly.
- Added a shared whitelist for client plugins, server mods and preloader patchers,
  with user allow/deny overrides and an optional blacklist mode. Unknown mods are
  excluded by default; explicitly denied dependencies also exclude their dependents.
- Moved the exclusions for **ORBIT, Questing Bots, Adaptive Bot Culling,
  ISB Special Forces and ISB Aishi** into the default policy. They remain excluded
  by default, without preventing normal PMC/Scav play.
- Refresh the policy after file edits and check it again when starting a session.
  Refusal messages name each mod only once. A missing or unusable default policy,
  mismatched client/server policy, or excluded plugins/patchers already loaded
  prevent Arena entry. Invalid user overrides fall back to the default policy
  with a warning.
- Require the session helper to acknowledge a switch before closing the game;
  a missing or failed helper leaves the current game running. Confine the private
  server's profile backups and logs to its own tree, prevent competing helper
  instances, and stop that server when its Arena game exits or crashes.

### Maps

- Added optional **Bowl** support through ArenaMapPort in all five modes, including
  PitFireTeam Tactical and Zombies, with a map preview, lighting and pickup placement.
- Bowl defaults to **eight participants** in Deathmatch, Gun Game and Team DM.
  Independent Team DM uses its original east/west team boxes for **2v2–10v10**.
  Combat waits for the spawn gates to open and all exit routes to pass safety
  checks; gates stay open for respawns. Other modes retain generic map spawns.
  Existing custom map allowlists are preserved.
- Team DM now consumes ArenaMapPort's gate-completion signal on all four ported
  maps. Bowl no longer waits the old fixed 13.5 seconds when its faster animation
  has finished; complete exit paths and safe spawn positions remain mandatory.
  Older MapPort builds keep the previous fallback timing.

### Compatibility changes

- Moved general body-replacement fixes for **Recoil Rework Legacy, Continuous Load
  Ammo and Continuous Healing** into **Redeploy.Core** (1.0.9), sharing them with
  standalone Redeploy. Arena bundles and requires at least **Core 1.0.10**, the
  library of Redeploy 1.0.1, and no longer installs duplicate hooks for those mods.
- Added Recoil Rework Legacy 2.0.0 and Continuous Load Ammo 1.2.0 to the default
  whitelist after native body-replacement checks. Continuous Healing was already
  listed. These checks do not cover every configuration or asynchronous operation;
  Recoil's server component was not part of those tests.
- Removed Arena-owned adapters for **MoreBotsAPI, AI Limit, Looting Bots,
  Remove The Dead, Smoothie, Late To The Party, Classic Movement, StatTrack,
  Deminvincibility, Skills Extended, Pause Continued, and EFT Effects Manager /
  Broken Limbs Don't Heal**. Session filtering replaces those workarounds;
  admitting one of these mods does **not** restore its former adapter behavior.

### Development

- Consolidated verification into one entry point, repaired outdated harnesses,
  and retired obsolete checks and one-off probes. Fixed test builds accidentally
  deploying files and added detection of unintended installation changes during
  verification.

Details: [Arena sessions](docs/ARENA-SESSION-20261021.md),
[Bowl team boxes](docs/reviews/BOWL-TEAM-BOXES-20261023.md),
[shared body-replacement fixes](docs/reviews/SHARED-BODY-COMPAT-20261023.md).

[Release notes and verification limits](docs/RELEASE-0.2.0.md).

## [0.1.3] - 2026-10-20

### Compatibility

- Added **Recoil Rework Legacy** component/cache handoff across respawns, preventing
  its old singleton from destroying the replacement player.
- Isolated **Skills Extended** Signals reward receipts and initial-player medical
  and weapon-mastery XP callbacks from protected Arena matches.
- Prevented new **Pause Continued** pause activations in Arena and displayed an
  explanation, without changing normal raids or saved settings.
- Transferred **EFT Effects Manager / Broken Limbs Don't Heal** effect ownership
  across replacement bodies while preserving separately owned temporary skill effects.
- Blocked Arena with **ISB Special Forces** or **ISB Aishi** client/server components
  because their separate progression stores were not protected. Normal PMC/Scav
  raids remained available.

### Documentation

- Expanded the mod compatibility review and translated the dependency overview
  into English.

[Release notes and verification limits](docs/RELEASE-0.1.3.md).

## [0.1.2] - 2026-10-20

### Interface and fixes

- Added a visible character-selection notice explaining why Arena is unavailable,
  including missing/outdated Redeploy.Core, incompatible mods and failed admission
  checks. Kept it above the native character description.
- Fixed protected early exits during the initial raid introduction, including
  exits before the first Zombie wave.

### Compatibility

- Blocked Arena entry with **Adaptive Bot Culling** while leaving normal raids available.
- **Continuous Healing:** guarded callbacks from retired player bodies and preserved
  healing on the current body.
- **Late To The Party:** protected uncollected Arena weapon-pickup sources from
  simulated looting; ordinary loot and collected weapons retained normal behavior.
- **Classic Movement:** preserved Arena movement-state ownership. Nostalgia Mode
  remained unsupported.
- **StatTrack:** excluded protected Arena activity from persistent statistics.
- **Continuous Load Ammo:** rebound magazine operations after player replacement
  and prevented retired operations from interfering with the new body.
- **SVM 2.2.3+:** preserved protected Arena endings alongside raid-end and
  loot-retention settings.
- **Deminvincibility:** preserved Arena death/respawn despite immortality,
  Second Chance and minimum-health rules, while retaining its other multipliers.

[Release notes and verification limits](docs/RELEASE-0.1.2.md).

## [0.1.1] - 2026-10-19

### Gameplay and fixes

- Allowed slow walking while splinting a leg, using the same Fast Movement
  requirement and **1 m/s** limit as surgical-kit walking.
- Changed the four-kill HUD callout to **Ultra Kill** to match its recording,
  while retaining existing audio filenames for custom announcer packs.
- Fixed pickup labels failing to initialize when their marker started hidden.
- Made failed skill/mastery restoration retryable and kept its original baseline
  protected from subsequent boosts.
- Synchronized the BigBrain companion's version with the client during builds.

### Compatibility

- **MoreBotsAPI:** protected Arena spawn identities, team relationships and persistent
  revenge state from cross-mode interference.
- **AI Limit:** paused its limiting component only in the protected Arena world.
- **Looting Bots:** paused autonomous Arena looting while preserving authenticated
  PitFireTeam commands.
- **Remove The Dead:** paused corpse removal while Arena owned the bodies and
  pickup sources.
- **Smoothie:** protected moving pickup markers and suppressed its optional GC
  pause in Arena while retaining other optimizations.
- Blocked Arena with active **Questing Bots** or loaded **ORBIT**, without blocking
  ordinary PMC/Scav raids. Questing Bots could remain installed when fully disabled
  in its server configuration.

[Release notes and verification limits](docs/RELEASE-0.1.1.md).

## [0.1.0] - 2026-10-19

Baseline for this changelog: the currently published `v0.1.0` tag at
[`9708949`](https://github.com/maschine34675/ArenaMode/commit/9708949).
The release archive was refreshed on 2026-10-19 without changing its version;
earlier same-version archives are not reconstructed here. Features already in
this baseline are documented in its
[feature inventory](https://github.com/maschine34675/ArenaMode/blob/v0.1.0/docs/FEATURES.md).

[0.2.1]: https://github.com/maschine34675/ArenaMode/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/maschine34675/ArenaMode/compare/v0.1.3...v0.2.0
[0.1.3]: https://github.com/maschine34675/ArenaMode/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/maschine34675/ArenaMode/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/maschine34675/ArenaMode/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/maschine34675/ArenaMode/releases/tag/v0.1.0
[0.3.0]: https://github.com/maschine34675/ArenaMode/compare/v0.2.1...v0.3.0
