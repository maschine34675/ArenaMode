# ArenaMode — complete feature inventory

Source baseline: **67cad48, 2026-10-17**. This lists the current implemented
behavior, including smaller changes accumulated during development. It is not
a list of future plans or a claim that every combination has passed a recent
in-game test. Defaults below are code defaults; existing saved settings can
differ. Features apply to Arena unless an explicit exception is described.

For installation and controls, see the [README](../README.md). For exact optional
mod versions, see [Dependencies](DEPENDENCIES.md). Asset rights are separate from
features and are recorded in [Third-party notices](../THIRD-PARTY-NOTICES.md).

Contents: [Menus](#1-native-menus-and-match-setup) ·
[Modes](#2-game-modes-and-scoring-rules) ·
[Movement](#3-player-movement-and-handling) ·
[Equipment/combat](#4-equipment-inventory-medicine-and-combat) ·
[Pickups](#5-pickups-and-powerups) ·
[Teams](#6-team-identification-and-friendly-fire) ·
[HUD/scoring](#7-scoreboard-hud-and-feedback) ·
[Audio](#8-announcer-and-sound) ·
[Replay/exit](#9-death-replay-spectating-and-exit) ·
[Maps](#10-maps-areas-and-environment) ·
[Bots/difficulty](#11-bot-behavior-and-difficulty) ·
[Protection/performance](#12-profile-protection-stability-and-performance) ·
[Configuration/tools](#13-configuration-integrations-and-authoring-tools) ·
[All mutators](#14-complete-map-screen-mutator-list) ·
[Limits](#limits-and-items-not-to-advertise-as-complete).

## 1. Native menus and match setup

- An **ARENA** character choice beside PMC and Scav; both the button and the
  character model select it.
- An Arena character preview, with own gear or illustrative supplied equipment
  reflected in the subsequent confirmation/deployment flow. The mannequin is
  not a promise of the exact randomly drawn kit. The redundant map-screen avatar
  is hidden. Preview lifetime handling avoids the earlier model jitter and
  leftover equipment appearing in later inventory previews.
- Mode order: **Gun Game, Deathmatch, Team DM, Tactical (WIP), Zombies (WIP)**.
  Mode descriptions use a common objective/respawn/equipment scheme.
  **Capture the Flag** and **Domination** appear below as gray, disabled previews;
  they are not playable or saved as selected modes.
- Native map filtering, mode-specific equipment choices, participant/team-size
  controls, time choices and match rules in the map-selection screen.
- Own gear versus Arena kits is offered for solo Zombies and PFT Tactical.
  Tactical defaults to own gear; only the PMC receives a kit, while companions
  keep their equipment. PFT Zombies keeps own gear automatically.
  This is not a universal equipment toggle for DM,
  Gun Game or Team DM. Invited PFT companions restrict selection to their two
  supported modes; Tactical displays the actual party size, such as 3v5 PFT.
- Customs area selection includes a map preview with the actual polygon
  highlighted; thumbnails remain visible without the long vanilla description.
- Bay5, Equator and Air Pit have gold labels without the old `Arena:` prefix and their
  own positions on the map overview. Bay5 supports its native time choices;
  Equator's night selection is presented with its rainy weather.
- Mutators are embedded directly in the screen, with active/inactive colors
  and explanations when a feature cannot be used. They are locked once the
  raid is armed or active; unavailable settings do not silently enable a mod.
- Optional skip of the intermediate game-settings, insurance and confirmation
  screens, plus a shortened deployment countdown (1 second by default).
  The confirmation screen remains whenever PitFireTeam is installed, preserving
  its companion setup.
- Clear assembly/readiness and countdown notices. Tactical's combat-start
  announcement waits until opponents are ready, rather than firing on entry.

Sources: [native selection](../ArenaMode.Client/Session/ArenaSideSelectionView.cs),
[preview flow](../ArenaMode.Client/Session/Preview/ArenaPreviewFlow.cs),
[map layout](../ArenaMode.Client/Session/ArenaMapPanelLayout.cs),
[ported-map presentation](../ArenaMode.Client/Session/ArenaPortedMapPresentation.cs),
[mode catalog](../ArenaMode.Client/Modes/ArenaModeCatalog.cs).

## 2. Game modes and scoring rules

### Gun Game

- A kill advances the held weapon; compatible ammunition and spare magazines
  accompany the replacement. The equipment kit supplies the rest of the gear.
- A generated ladder draws weapons from increasing power bands rather than
  choosing an arbitrary unordered sequence. The shipped pool is broader than
  one match's ladder; the default generated ladder has 24 stages.
- Completing the ladder starts another cycle. Optional fresh draws on a cycle
  and an optional lost stage on death; the normal respawn retains the stage.
- Announcements for the final weapon and ladder completion.
- Select the total participant count, including the player; Arena replaces
  casualties while respecting that total.

### Deathmatch

- The earlier Endless and Rounds modes are combined into **Deathmatch**.
- Choose **free-for-all** or **all bots against the player**.
- Choose finite or unlimited rounds, with an optional per-round kill limit
  and/or time limit. Finite rounds need a finishing condition; without either
  limit, play is continuous rather than creating empty round breaks.
- FFA's kill limit can be reached by any participant; all-vs-player uses the
  player's kill count. Round and match results are distinct from score points.
- Arena-kit respawns, round intermissions and a configurable total population.
  Round changes can reissue equipment or only heal in place, depending on the
  advanced round-respawn preference.

### Team Deathmatch

- Independent **USEC vs BEAR**, without requiring PitFireTeam. Teammates are
  friendly to each other; the player's faction determines the team.
- Continuous replacement of dead participants into fixed faction slots.
- Visible protection for newly spawned bots on both teams **only in Equator,
  Bay5 and Air Pit Team DM**, where initial and replacement bots use fixed team
  boxes. Other maps do not grant bot spawn protection; player protection is
  unchanged. The configured duration defaults to **4 seconds** and starts in live
  combat when the bot is active. Firing, throwing a grenade or dealing outgoing
  damage ends the shield immediately.
- Team kill and time limits; defaults are **50 kills / 10 minutes**. The result
  can be a win, loss or draw. Readiness and countdown precede live combat.
- Giving up the current life awards a kill to the opposing team.
- Whole-map team sizes are selectable within map limits. Customs areas support
  **2v2 through 5v5**; Equator uses its north/south team boxes for both initial
  spawns and respawns.

### Tactical (WIP)

- Eliminate the opposing squad to win a round. Solo defaults to **1v5**;
  advanced settings can change the solo squad and timing.
- Solo kit buy phase, kill income, win/loss bonuses and retention of purchased
  kit selection while alive; that selected kit is refreshed for the next round.
  The buy menu reports the **front plate's** armor class.
  Without a first purchase, the cheapest buildable starter kit is free. After
  death without a purchase, the best affordable kit is selected automatically;
  if none is affordable, the cheapest buildable kit is free again.
- Optional PitFireTeam: **player plus 1–4 invited companions against five
  enemies**, using own gear (default) or one supplied PMC kit instead of the
  solo kit economy. The menu choice never replaces companions' equipment.
- The match continues after player death; watch the remaining combatants and
  return at the next round. Fallen companions are rebuilt for subsequent rounds.
- Multiple protected team rounds; default target **three round wins**.
- At the PFT round time limit, surviving team members decide the result. Two
  consecutive drawn rounds end the match as a draw. A surviving PFT PMC retains
  the current body and gear between Tactical rounds; the dead PMC receives the
  captured entry combat loadout on return. This differs from Zombie wave refill.
- PFT round changes wait for teardown, loadout restoration and complete new
  rosters. Failed assembly ends as **no contest** instead of starting an
  incomplete team. Solo can proceed with a partially assembled opposing squad
  after its assembly timeout.
- Escape remains available during countdown and spectating.

### Zombies (WIP)

- Arena-controlled waves of EFT infected, with separate limits for simultaneous
  enemies and total wave size. Defaults: **32 alive / 50 first wave / +20 per
  wave / own gear**. The menu exposes these population settings.
- A readiness-dependent countdown before the first wave and between waves;
  default intermission **10 seconds**.
- Individual spawn requests default to **0.2 seconds** apart; **20%** of infected
  are pistol-armed shooters by default.
- **Solo has one life.** Death ends the match after any selected replay; it does
  not respawn the player into the current wave.
- Optional PFT squad with **1–4 companions**. Surviving members can finish a
  wave after the player's death. Fallen members return for the next wave; a
  complete wipe ends the match.
- Ammunition is replenished between cleared waves. Protected PFT entry gear
  and health are restored for the surviving player as well as fallen members.
- Solo own-gear play also restores the entry combat loadout; kit play refills
  the current kit instead of drawing another. A refill can move the player
  slightly to a safe replacement position. Consumed protected-container supplies
  are not duplicated by this combat-loadout refill.
- Optional shooter proportion and stuck-wave recovery controls in advanced
  settings. Zombie Crowd is a separate opt-in harder-horde setting, including
  controlled recall of distant stragglers; it is not the normal Fast Bots boost.
- Stuck-wave recovery defaults to 100 seconds without an infected death while
  the wave is saturated. It clears remaining active infected administratively;
  pending spawns still run. This is not a 100-second limit on every wave.
- Repetitive infected voices are budgeted, and redundant PFT work for native
  infected is reduced without removing their combat AI.

Sources: [mode implementations](../ArenaMode.Client/Modes),
[Deathmatch rules](DEATHMATCH-MODE-REVIEW.md),
[TDM match flow](TDM-MATCH-FLOW-REVIEW.md),
[variable PFT squads](PFT-SQUAD-SIZE-REVIEW.md),
[Zombie countdown/refill](ZOMBIE-WAVE-REFILL-20261015.md).

Participant totals include the human: **2–24 in DM/Gun Game**, **4–24 in Team
DM** in even steps, or **4–20 on Equator Team DM**. Defaults are Equator 4,
Bay5 8, Labs 16, Labyrinth 8 and otherwise 10; Customs Team DM separately
defaults to 2v2. These are menu defaults, not hardware performance guarantees.

## 3. Player movement and handling

**FAST MOVEMENT** is the common master switch for Arena movement bonuses and
abilities. Individual preferences remain saved when it is off. Bot movement,
leg-injury penalty reduction, elite skills, medical-use speed and magazine-loading
speed have separate controls.

| Implemented change | Behavior / limit |
|---|---|
| Walking and sprinting | Defaults 1.25× walk/strafe and 1.3× sprint, with an additional 1.1× horizontal motion scale. |
| Inertia and strafing | Reduced inertia/direction-change delay, configurable strafe smoothing and quicker lean. |
| Weight | Carry-weight thresholds default to 2×, reducing overload penalties without deleting item weight. |
| Stamina | Optional unlimited movement and aiming stamina. |
| Bushes | Optional removal of bush movement slowdown. |
| Aiming | 1.5× ADS transition; default removes 70% of the native ADS movement penalty. ADS input can cancel a sprint directly. |
| Jump height | Configurable normal jump height, default 1.3×. |
| Double jump | One extra jump while airborne, including after walking off an edge; no requirement to have initiated a ground jump. |
| Bunnyhop | Correctly timed landing jumps carry momentum and build a bounded speed bonus, up to 25%; enabled by default. No hold-to-auto-hop. |
| Air control | Steer horizontal travel while airborne; the default turn-rate control is 80 degrees/second. |
| Airborne hip fire | Allows supported firearm hip fire in the admitted native jump state; reload, malfunction and ADS restrictions remain. |
| Air vault | Re-enter a valid native vault/climb from the air when the obstacle is within reach; not unrestricted wall climbing. |
| Vault speed/reach | Defaults 1.6× speed / 1.3× reach. Reach geometry changes take effect on the next player body. |
| Slide | Crouch during a sprint to slide, with native collision retained. |
| Falling/landing | Optional no fall damage; landing slowdown duration defaults to half its native duration. |
| Draw/holster/reload | Separate animation-speed controls, each defaulting to 1.5×; alternate UIFixes reload handling is accounted for. Underbarrel-launcher reloads and malfunction repair retain native timing. |
| **Walking during surgery** | **CMS and Surv12 can be used while walking at up to 1 m/s.** Follows FAST MOVEMENT; sprinting, jumping, going prone and vaulting remain blocked during surgery. |

Movement changes follow the current local player across respawns. Disabling the
master gates its bonuses without overwriting saved individual multipliers;
already initialized vault-reach geometry changes on the next body. Collision,
obstacle reach and weapon-operation rules still matter.

Sources: [movement settings](../ArenaMode.Client/Config/Settings.cs),
[surgical walking](SURGICAL-WALK-REVIEW.md),
[air movement](AIR-MOVEMENT-REVIEW.md), [bunnyhop](BUNNYHOP-REVIEW.md),
[reload handling](RELOAD-HANDLING-REVIEW.md),
[movement implementation](../ArenaMode.Client/Movement).

## 4. Equipment, inventory, medicine and combat

- **20 supplied kits**, plus generated weapon/ammunition/attachment variants.
  Optics and useful attachments are selected within compatibility rules; empty
  scope-ring arrangements used only as reflex mounts are filtered out.
- Random kits are the default, with immediate repeats avoided when another
  buildable kit exists; fixed selection is available. Per-raid variants prefer
  CQB optics and do not add thermal/night optics or scopes above 4×. A weapon
  without a valid optic path may still use iron sights.
- Invalid/unfinished game templates are excluded from generated equipment.
  Generated item trees and required bundles are checked before use.
- Own gear is a mode-dependent alternative. PFT Tactical preserves the selected
  entry loadout (own gear or a supplied PMC kit), and PFT Zombies uses own gear.
- Two **RGD-5** grenades in supplied kits, placed in accessible rig/pocket space
  where possible; no automatic Zarya flash grenade. Molotov is an additional
  dedicated item, not a renamed or converted RGD-5.
- **Automatic full-auto on drawing a supported weapon**; a manual fire-mode
  choice remains respected while that weapon is held. Semi-only weapons are
  not made automatic.
- **Backpack items:** use/bind carried medicine and reload from compatible
  magazines in the backpack through normal reload controls.
  Magazine selection searches the backpack directly, not nested containers;
  loose backpack rounds are not universally accessible to the reload key.
- Supplied magazines/chambers are inspected so their ammunition information
  displays immediately; this does not examine every subsequently looted item.
- **Automatic medical quickslots** with consistent purposes: healing on 4,
  heavy bleed on 5, light bleed on 6, fractures on 7, pain on 8 and surgery on 9.
  Rebinding after reissue respects user changes instead of taking over weapon
  or grenade keys.
- **2× medical use** by default and **2× magazine packing/unpacking**; these
  are distinct from the firearm reload-animation multiplier. Native treatment
  effects/resources and total healing remain in place.
- **3× bleeding HP loss** by default for both living players and bots; this does not make
  a health pickup cure bleeding or repair destroyed limbs.
- **50% less broken/blacked-leg walking penalty** by default, controlled
  separately from Fast Movement. The injury and native sprint restriction remain.
- Temporary elite skills and weapon mastery during Arena, restored afterwards
  instead of permanently adding skill progress.
- Optional empty/hidden secure-container contents for supplied-kit play. Own
  protected gear is exempt; originally protected items are restored on exit.
- Instant container search and automatic search of opened corpse inventories.
  Instant Search also suppresses the repeated Lucky Search jingle. The corpse-
  search integration steps aside when the standalone mod owns it.
- PMC victim names in the post-raid kill list without requiring dogtags.
- Respawn protection, default **4 seconds**, and stronger fatal-shot ragdoll
  impulses, default **5×**.

### Akimbo (WIP)

- A visual second copy of the eligible held pistol; selectable as a mutator.
- One projectile per trigger, alternating right and left. The bonus-side shot
  does not consume another cartridge, providing two shots per cartridge.
- Doubled firing cadence relative to one pistol, with side-specific shot
  presentation instead of making both copies appear to fire together.
- Supports eligible automatic pistols and handgun revolvers, not flare guns,
  launchers, shotgun revolvers or pistol-named SMGs. **Hip fire only: ADS is
  disabled while Akimbo is active.** Replicas hide during reload, sprint and
  other busy states; hand/finger posing remains WIP.
- Automatically enabled visual aiming lasers follow both pistol muzzles, including
  pistols and revolvers without an attachment rail. Green by default, or the
  friendly team color when team lasers apply. These Arena-owned visual aids do
  not add inventory attachments or native laser accuracy bonuses; they hide with
  the Akimbo presentation and stop at detected cover.
- Shares the real weapon's ammunition/reload state; it is not a second
  independently equipped inventory weapon. Off by default; unavailable in
  Zombies. Native weapon restrictions still apply.

### Molotov (WIP)

- A dedicated Arena item with a bottle model/icon, thrown presentation,
  temporary ground fire, synthesized sound and damage attribution.
- Adds one Molotov to the current equipment tree when available, without
  replacing the two regular grenades. The grenade hotkey initially prefers it;
  later manual grenade selection is respected. The mutator defaults to on.
- Requires free compatible rig/pocket space; no item eviction or backpack
  fallback. Spending it or toggling the mutator does not grant an endless refill;
  fresh kit/restored equipment receives its own allowance.
- Fire placement and damage use ground/obstruction checks. A bot escape action
  requires BigBrain plus ArenaMode's companion; without that capability the
  feature is unavailable. It is not offered in Zombies.
- Ground fire lasts six seconds, with 100 base HP damage per second. Overlapping
  fire does not stack on a victim; spawn protection and the applicable friendly-
  fire policy still apply.
- No ordinary loot/trader supply and no conversion of vanilla RGD-5s.

Sources: [kit supplies](../ArenaMode.Server/Services/ArenaKitSupplies.cs),
[weapon variants](../ArenaMode.Server/Services/ArenaWeaponVariants.cs),
[backpack reload](BACKPACK-RELOAD-REVIEW.md),
[default fire mode](DEFAULT-FIRE-MODE-REVIEW.md),
[skills](../ArenaMode.Client/Combat/ArenaSkills.cs),
[Akimbo](AKIMBO-REVIEW.md), [Molotov integration](MOLOTOV-NATIVE-INTEGRATION-20261015.md).

## 5. Pickups and powerups

Built-in layouts cover **every supported Arena map**, only in **Deathmatch and
independent Team DM**: Factory day/night, Labs, Labyrinth, the selected Customs
area, Equator, Bay5 and AirPit. Gun Game, Tactical/PFT and Zombies do not use them.

| Pickup | Effect |
|---|---|
| Health | Restore up to **150 HP**, within the existing body-part health limits; no treatment of wounds or destroyed limbs. |
| Shield | **220 temporary impact-shield points**, for up to **30 seconds**, against bullets/explosions/fragments; not a universal 220 HP increase or armor protection. |
| Double damage | **2× post-armor HP damage from ordinary firearm projectiles for 20 seconds**. |
| Jump boots | **Double jump height for 30 seconds**, with FAST MOVEMENT enabled; separate from granting the Double Jump ability. |
| Unlimited ammo | **10 seconds** without cartridge consumption in a compatible already-loaded magazine weapon. Does not fill an empty weapon; launchers/revolvers/break-actions excluded. |
| Weapon | **M60E6 FDE** with MRS, blue laser and 100 M993 rounds, based on the supplied preset; transferred into a free compatible inactive weapon slot, not automatically drawn. |

- Six pickups use eight distinct candidate spots on Factory. Other maps use
  up to sixteen positions derived from the current raid's native bot starts,
  excluding known unsafe zones and team boxes. Actual placement still requires
  floor, clearance, navigation and selected-area checks. Pickups vary between
  positions across rounds and also rotate after cooldown or 60 seconds left
  uncollected. A custom `Pickups.json` replaces the built-in layouts.
- Respawn cooldowns: Health **20 seconds**; Damage, Shield and Jump Boots
  **30 seconds**; Ammo and Weapon **45 seconds**.
- Colored models/markers, a matching vertical light beam up to **6 metres**
  (clipped below ceilings), collection sound and a HUD display of active effects
  and remaining duration.
- Walk-over collection for eligible pickups, including the weapon. Geometry,
  grounding, room and inventory checks can reject unsafe placement or transfer;
  a confirmed transfer refusal leaves the weapon available. An uncertain
  transfer is retired for the round to avoid duplicating the weapon.
- Weapon examination/bundle handling is included so unexamined M60 attachments
  do not by themselves make the pickup unusable.
- Recollection refreshes the effect/pool instead of stacking it. A character
  camping a respawn point must leave and re-enter to collect the next pickup.
  Effects end with that body; Team DM deaths do not reset pickup cooldowns.
- Bots may collect health, shield and damage powerups; deliberate pursuit of
  these objectives requires the BigBrain companion. Boots, unlimited ammo and
  the weapon are player pickups.
- Bots carrying Shield or Double Damage show a narrow blue or pink body contour,
  respectively, matching their pickup colors; spawn protection uses pale cyan.
  Opaque cover hides the occluded body contour. Simultaneous effects share one
  segmented contour, each with its own lifetime. The view remains independent of
  the HUD switch and disappears when its effect or body ends.

Sources: [pickup system](PICKUPS-REVIEW.md),
[all-map layouts](ALL-MAP-PICKUPS-20261017.md),
[current layouts](../ArenaMode.Client/Pickups/ArenaPickupLayout.cs),
[pickup implementations](../ArenaMode.Client/Pickups).

## 6. Team identification and friendly fire

- Native faction friendliness for Team DM and explicit PFT squad membership.
- Forced faction armbands in Team DM without requiring manual APBS changes.
- A raised friendly diamond above living allies, with screen-position handling
  for DLSS/render scaling. No corresponding enemy wall marker.
- Team-colored tracers for **all bullets**, including normally tracerless
  ammunition, in Team DM and protected PFT modes only.
- Team-colored active visible weapon lasers. This does not turn the laser on,
  recolor flashlights or expose an infrared laser to the naked eye.
- Separate visible laser point/beam brightness controls, both default **3×**.
  Existing stronger BrightLasers values are not multiplied a second time.
- Friendly/enemy colors are configurable; defaults are **cyan/red**. Killfeed
  team colors do not depend on the tracer or laser switches being enabled.
- Friendly Fire is selected per supported mode and fixed for the raid. Defaults
  are **on in Team DM/PFT Tactical**, **off in PFT Zombies**. Self-damage remains
  possible; this is not general invulnerability. On leaves native/PFT damage
  permission in place rather than forcing allied damage past another mod's rules.
- Solo modes retain native tracer behavior and do not offer team colors or
  friendly-fire controls as if an allied team existed.

Sources: [friendly fire](FRIENDLY-FIRE-REVIEW.md),
[armbands](../ArenaMode.Server/Services/TeamDeathmatchArmbandPolicy.cs),
[friendly markers](../ArenaMode.Client/Hud/ArenaFriendlyMarkers.cs),
[laser brightness](LASER-BRIGHTNESS-REVIEW.md), [beam brightness](LASER-BEAM-REVIEW.md),
[effects implementation](../ArenaMode.Client/Effects).

## 7. Scoreboard, HUD and feedback

- **F9 scoreboard** and live Arena settings: names, roles, alive/out state,
  kills, deaths, assists, headshots and points, plus local K/D, best streak and
  elapsed time. Local/PFT totals survive their replacement bodies; ordinary
  new bots begin a new personal run while team totals retain earlier lives.
  Non-team standings show the leading twelve runs and keep the player visible.
- The scoreboard opens for eligible round breaks/results, with Give up or
  Return to menu actions. Opening it during combat is not an invulnerability
  switch, and it does not stack over native inventory/raid menus.
- Score/status panel with match progress, local/rival information and mode
  state. Long names do not push points into a second line. X/Y offsets are
  configurable, with the default position below the quickslot area.
- Killfeed on the **left**, with an opaque-enough backing, configurable offsets,
  full killer/victim labels, distance and optional other participants' kills.
- Own uncredited deaths appear too. Icons distinguish weapon categories,
  headshots, melee, grenades, bleeding, barbed wire and fire; the obsolete `HS`
  text tag is removed.
  Other uncredited causes include falling, poison, exhaustion, dehydration,
  landmines and giving up, without inventing a killer.
- Eight generated weapon-category pictures rather than per-weapon inventory
  renders, with separately credited headshot/melee/grenade/barbed-wire symbols.
- Hitmarkers distinguish flesh, armor-only, headshot and kill contacts. The
  confirmed-kill skull **above the crosshair** is independent of subsequent
  nonlethal hits and the killfeed toggle.
- Floating damage numbers; optional combination of rapid hits on the same
  target within 0.25 seconds into one accumulating burst number. Body and armor
  damage remain separate; combining is off by default.
- Health/armor panel: current HP, armor and helmet durability, bleeding/fracture
  status and optional weight, with warnings as values fall. Armored rigs count
  as armor too.
- Weapon panel: held weapon/ammunition information, compatible spare magazines
  and loose rounds. Magazines in armored rigs are counted; immediately available
  supplies and backpack reserves are distinguished.
  The display includes caliber, fire mode, chamber/magazine capacity and
  underbarrel-launcher information when that mode is active.
- Grenade display with quantities/types, and a separate active-powerup panel.
- Configurable info-panel scale and opacity, plus info-panel, score-panel and
  killfeed offsets; native-style dark surfaces and consistent team/status colors.
- **ARENA HUD** toggles killfeed, hitmarkers/damage numbers and health/weapon
  panels together while preserving their individual preferences. Essential
  match notices, scoreboard/team markers and audio remain separately controlled.
- Quest-progress, achievement and XP-gain notifications are suppressed for
  Arena. The vanilla extraction list, including its automatic raid-start
  appearance, is suppressed as well; the numeric raid clock and real extraction
  countdown remain. The combat overlay hides behind native inventory/menus.

Sources: [HUD implementation](../ArenaMode.Client/Hud),
[HUD mutators](HUD-MUTATORS-REVIEW.md),
[info panels](RAID-INFO-PANELS-REVIEW.md),
[progress notifications](../ArenaMode.Client/Notifications/ArenaProgressNotifications.cs),
[UI artwork](WEAPON-RENDERS-20261016.md).

### Points and assists

- Kill base points depend on the victim role: **100** for ordinary/other targets,
  **150** for raiders/followers, **200** for PMCs, **300** for bosses.
- Headshot bonus **50**; each fifth consecutive kill adds a **100-point** streak
  bonus. Score points are separate from the raw kills used for win conditions.
- Assists award the contributor's share of the normal kill base value, based
  on actual damage contributed. The final killer keeps the existing full kill
  reward; headshot/streak bonuses are not copied into the assist.
- Assist points do not increase kill limits or Tactical buy money.
- No persistent cross-match/global leaderboard is implemented.

Sources: [score rules](../ArenaMode.Client/Combat/ScoreRules.cs),
[assist settlement](ASSIST-SCORING-REVIEW.md),
[scoreboard](../ArenaMode.Client/Modes/ArenaScoreboard.cs).

## 8. Announcer and sound

- Multikills: **Double Kill, Triple Kill, Ultra Kill, Monster Kill**.
- Streaks: **Killing Spree, Rampage, Dominating, Unstoppable, Godlike**.
- Special events include **First Blood, Headshot, Payback, Shutdown, Last Man
  Standing, Clutch, Airborne and No Scope**; cues depend on the mode/event rules.
- **COLLATERAL** is a text-only award for two distinct enemies killed by the same
  physical bullet, including its penetration continuation. Separate rapid shots,
  shotgun pellets, fragments, ricochets, teamkills and delayed bleeding do not
  qualify. It adds no score bonus and has no recording yet.
- Headshot award uses the single-headshot-kill condition; an ordinary headshot
  hitmarker is a separate event. Airborne requires an unaimed killing shot after
  at least **0.5 seconds in the air**. No Scope is for eligible bolt-action shots.
- A verified direct own-hand-grenade death gets **NICE THROW, GENIUS!** on
  screen, with the **Embarrassing** recording. The text remains visible with
  ANNOUNCER off; the recording follows the announcer switch.
- Round preparation/start, round win/loss and match win/loss/draw/no-contest
  cues; match point/final round where applicable.
- Lead changes, time remaining, kills remaining, final Gun Game weapon and
  completed ladder cues.
- Priority handling favors an earned multikill over a simultaneous ordinary
  spree cue. A bounded pending cue and expiry prevent a backlog of stale speech.
  Results wait for an active kill callout instead of cutting it off immediately.
- One main award label is chosen per kill: Collateral → First Blood → Payback → Shutdown →
  multikill → Airborne → No Scope → spree → Headshot. These labels do not grant
  separate new point bonuses. Shot awards use the state when fired, not impact;
  delayed bleeding does not inherit the preceding shot's Airborne/No Scope.
- Independent announcer and hitmarker volume, each default **0.7**. Announcer
  output bypasses the Interface-volume setting by default, but still respects
  the overall game/listener state. A legacy Interface route remains available.
- Per-mode announcer preferences: default on except Zombies. The switch covers
  announcer stingers/cues and kill-award text, not essential wave/status text.
  The detailed lead/time/limit director serves DM, TDM, Tactical and Gun Game;
  it is not a full parallel set of Zombie-wave announcements.
- Separate hit/headshot/kill sound slots and a pickup collection sound. The
  current three hitmarker slots deliberately contain the same selected effect.
  Hit sounds prioritize kill over headshot over hit, independently of visible
  HUD and announcer switches; a helmet headshot sound has looser conditions
  than the single-hit lethal HEADSHOT announcement.
- Loading/retry handling for announcements across matches, rather than requiring
  a full restart after a recoverable missing-clip load.

Sources: [cue catalog](../ArenaMode.Client/Audio/AnnouncerCueCatalog.cs),
[award conditions](../ArenaMode.Client/Combat/ArenaCombatAwards.cs),
[shot awards](../ArenaMode.Client/Combat/ArenaShotAwardHistory.cs),
[announcement policy](../ArenaMode.Client/Audio/ArenaAnnouncementPolicy.cs),
[audio provenance/playback notes](../ArenaMode.Client/Assets/ArenaMode/docs/licenses/audio/AUDIO-LICENSES.md).

## 9. Death, replay, spectating and exit

- Killcam selection for every mode when a compatible **Killcam** is
  installed and DeathReplay is enabled. Respawn/end-of-life handling waits for
  the selected replay instead of replacing the player underneath it.
- Mode-specific replay preferences; Tactical defaults to 3 seconds,
  other modes default off. The subsequent live tail is shortened by about half
  with the supported Killcam integration.
  If no playable recording exists, there is no mandatory extra replay delay.
- Built-in third-person team spectating after death in protected Tactical and
  PFT Zombies, independent of the optional replay plugin.
- Cycle living targets with the mouse buttons; Tactical may include surviving
  opponents. The overlay identifies the observed bot and shows its HP.
- Dead-player concussion and other health post-processing no longer obscure
  replay/spectator views. Camera/model cleanup prevents retired body visuals
  leaking into later weapon previews.
- Spectator collision protection keeps the camera out of obstacles. Tactical
  can observe either side; PFT Zombies follows the surviving companion set.
- Escape remains usable while observing, allowing the native raid menu and
  protected match exit. Give-up is counted as a real death according to mode.
- Arena leave bypasses the misleading item-loss confirmation. The post-raid
  lost-items warning is hidden and the XP-gain page is skipped; required native
  result completion and profile refresh still run.

Sources: [replay selection](KILLCAM-MODES-REVIEW.md),
[spectator implementation](../ArenaMode.Client/Spectator),
[post-raid screens](POSTRAID-PAGES-REVIEW.md),
[protected exit](../ArenaMode.Client/Session/ArenaRaidExit.cs).

## 10. Maps, areas and environment

- Factory **day/night**, Labs and Labyrinth support all five modes.
- Customs uses three refined polygon areas: **Factory Center, Factory Side,
  Customs Yard**. All five modes can select them; whole-map Customs and retired
  rectangular/test presets are not current menu choices.
- Polygons have **no upper or lower height cap**. Physical boundaries keep the
  player inside; spawn/path validation keeps Arena bots within the contour.
- The grid fades in near each boundary side: fully visible within 6 m, fading
  to invisible at 20 m. Invisible walls still block movement.
- Team starts, spacing, connected navigation and occupancy checks precede the
  match. Customs Yard has additional Arena-only start candidates; it does not
  overwrite normal map spawn records.
- Optional ArenaMapPort maps: **Bay5** and **Air Pit** support all modes; **Equator** supports
  Gun Game, DM, TDM and solo Zombies, but no Tactical or PFT Zombies, even with
  fewer companions. ArenaMode does not ship or install those maps itself.
- Labs excludes bot starts behind both locked extraction gates, preserving
  ordinary player starts.
- Factory day and night exclude the known unsafe courtyard/underground bot
  starts. Protected PFT leader starts are also filtered, since companions spawn
  around the leader. Walking into such a pocket later is not prevented by a
  spawn filter.
- Loose map loot can be removed for Arena, enabled by default. This is separate
  from supplied kits and corpse inventories, not a blanket removal of containers.
- Labyrinth can be selected for Arena without a separate EnableLabyrinth
  installation; keycard requirements are waived for supported Arena entry.
  This does not globally unlock ordinary raids or reproduce the other mod's
  Scav exits.
- Labyrinth entrance gun/flame/liquid/steam traps are disabled by default. The
  toxic pool is drained and its known contact-damage zones disabled. This does
  **not** remove every spike trap, trapdoor or environmental hazard on the map.
- Labyrinth's center alarm is muted independently, without disabling the boss
  events or other sounds. The option can be switched off.
- **Way Up** is available immediately but uses a **10-second extraction
  countdown**, avoiding an instant exit when crossing the doorway.
- Additive round-state lighting on **every supported Arena map**:
  countdown/start feedback, late-round pressure and Tactical result colors.
  It leaves map bundles, ordinary lamps, weather and AI vision unchanged.
  Blackout/Going Dark can suppress this layer by default to avoid competition.

Sources: [map/mode matrix](MAP-MODE-COMPATIBILITY.md),
[all-mode Customs handling](CUSTOMS-ALL-MODES-REVIEW.md),
[grid visibility](AREA-GRID-VISIBILITY-REVIEW.md),
[Factory exclusions](FACTORY-SPAWNS-REVIEW.md), [Labs exclusions](LABS-SPAWN-REVIEW.md),
[Labyrinth access](../ArenaMode.Client/Session/ArenaLabyrinthAccess.cs),
[trap handling](LABYRINTH-SPAWN-TRAPS-REVIEW.md),
[exit timing](../ArenaMode.Server/Services/ArenaLocationMutator.cs),
[lighting](MAP-LIGHTING-20261017.md).

## 11. Bot behavior and difficulty

- Independently configurable bot walk/sprint speeds, both default **1.5×**.
  Added changes reduce slow starts, direction-change delay and weight penalties;
  selected native movement slowdowns are shortened rather than disabling AI.
- Fast Bots is separate from player Fast Movement. It does not give infected
  the normal bot-speed package; Zombies has its own Crowd option instead.
- Arena pursuit/search behavior keeps combatants looking for opponents; team
  pursuit after player death reduces passive waiting. BigBrain provides the
  deliberate action owner where required; SAIN adapters preserve its lifecycle.
- Optional exclusion of passive **Rat, Timmy and Coward** SAIN personalities.
- Bots can be prevented from looting corpses and making gestures; weapon lights
  on corpses are turned off. Bot healing callbacks and stale action handling
  have targeted guards against the earlier dropped-Surv12/frozen-bot cases.
- DM/Gun Game refill a fixed selected population. Labyrinth can retain a boss
  slot within that total instead of adding uncontrolled native boss waves.
- AI pressure includes elapsed time and the player's streak; dying resets the
  streak contribution, not elapsed pressure. AI difficulty and equipment level
  are separate controls.
- For **timed independent Team DM**, time pressure and equipment progression
  scale to the selected live match duration, reaching their time-based maximum
  at 80% of the time limit. This is **not** implemented uniformly for every mode:
  DM, Gun Game and solo Tactical retain their normal elapsed-time ramps.
- Bot equipment can progress from low to high level through the raid (default
  level anchors 1→66 over 15 minutes), affecting newly generated bots rather than
  transforming existing gear. Protected PFT modes bypass that low-level ramp.
- The normal AI time ramp reaches its maximum after 12 minutes; streak pressure
  can contribute up to 35% at eight consecutive kills. PMCs progress from hard
  toward impossible, Scavs from normal toward hard. This affects new bots;
  Zombies uses its own role/difficulty selection.
- APBS 2.4.1 has an explicit adapter; unsupported versions retain their own
  equipment-level selection instead of being modified blindly.
- Fixed participant/faction counts are not increased beyond the selected total
  by the older pressure-floor settings. No single unified difficulty slider is
  implemented yet.

Sources: [bot movement](BOT-MOVEMENT-REVIEW.md),
[agility](../ArenaMode.Client/Movement/ArenaBotAgility.cs),
[pressure](../ArenaMode.Client/Bots/ArenaPressure.cs),
[duration policy](../ArenaMode.Shared/ArenaDifficultyClock.cs),
[population ownership](SOLO-POPULATION-REVIEW.md),
[APBS integration](APBS-COMPATIBILITY-REVIEW.md),
[bot implementation](../ArenaMode.Client/Bots).

## 12. Profile protection, stability and performance

- Profile snapshot/restore and guarded post-raid processing discard Arena loot,
  progression and quest changes and restore entry equipment. Ordinary raids
  retain their separate path; this is not a guarantee against every crash or
  conflicting mod, so profile backups remain advisable.
- PFT companion persistence is protected as well as the local PMC. Frozen squad
  membership is checked through rounds/waves; missing or inconsistent identities
  lead to a safe rejection/no-contest rather than a partially bound match.
- Fully consumed protected supplies are handled during round restoration;
  moving protected items out of their permitted ownership is not silently
  accepted as equivalent to consumption. Secure-container consumables are not
  recreated each round/wave; final raid-profile restoration is a separate step.
- Ordinary Arena respawns prefer distance from the death location and avoid
  recently used starts, with safe fallback where the map is too small. Bound
  areas and team-box spawns retain their own placement restrictions.
- Failed starts, delayed bot activations, body replacements and native exit
  teardown have explicit cleanup to avoid later accidental rounds or spawns.
- Initial kit assets can preload during the loading screen. Bot/kit resource
  loading has a bounded scheduler boost to reduce the former long assembly
  stalls; this is not a guarantee of a fixed load time.
- Bot profile construction is spread across frames with a default 3 ms budget
  for additional work. Each open batch still builds at least one profile per
  frame, so this is not a hard 3 ms limit. It reduces long single-frame parsing
  stalls at the cost of a potentially later completed batch.
- Dead-brain removal and bot FOV updates avoid redundant repeated work.
  Experimental offscreen Zombie animator/shadow switches remain off by default;
  the recorded A/B test did not show a measurable frame-time improvement.
- Old corpses can be hidden, stripped and subsequently released when no longer
  needed. PFT bodies and other still-referenced bodies have lifecycle exemptions;
  this is more than simply leaving every dead player invisible in the scene.
- DynamicMaps integration removes markers when those corpses are retired.
- Idle asset-pool growth is capped, and an optional managed-heap threshold can
  restore incremental garbage collection during long matches without a forced
  full collection.
- Zombie voice budgeting and native-infected PFT fast paths reduce redundant
  work; AI combat/navigation is retained. High populations remain dependent on
  hardware, map and other mods.
- Info logs focus on setup, protection and player-relevant limitations; gameplay
  (rounds, waves, results, scores) and routine internals use Debug, and detailed
  probes require DeveloperMode. Settings polling avoids the old
  repeated RequestHandler request/success spam. Real failures remain visible.

Sources: [protection services](../ArenaMode.Server/Services),
[protected consumption](TEAM-LOADOUT-CONSUMPTION-REVIEW.md),
[corpse cleanup](CORPSE-CLEANUP-REVIEW.md),
[DynamicMaps cleanup](DYNAMICMAPS-CORPSE-REVIEW.md),
[heap threshold](GC-THRESHOLD-REVIEW.md),
[pool prewarm](POOL-PREWARM-REVIEW.md), [logging policy](LOGGING.md).
See also [profile construction](PROFILE-BUILD-BUDGET-20261017.md) and
[Zombie body-culling checks](ZOMBIE-BODY-CULLING-20261017.md).

## 13. Configuration, integrations and authoring tools

- Map-screen controls for ordinary setup, F9 for scoreboard/live preferences,
  and F12 advanced configuration when the BepInEx settings UI is installed.
- Server web configuration at `/arenamode`, editable kit/ladder JSON and
  synchronized client preferences. Per-raid rules remain fixed for the active
  match; changes intended for the next raid do not rewrite its map mid-fight.
- Separate required client/server halves and the shared Redeploy.Core library;
  the complete standalone Redeploy mod is not required.
- Version-scoped optional integrations for SAIN, BigBrain, PitFireTeam, APBS,
  DynamicMaps and Killcam; optional ArenaMapPort locations. UIFixes reload,
  BrightLasers, EnableLabyrinth and overlapping search/notification features have
  specific coexistence handling, not unlimited compatibility guarantees.
- RaidInfoPanels and CombatSlide functionality is incorporated; Arena's local
  full-auto, search and notification behavior does not require their separate
  reference mods. **Fika remains explicitly unsupported.**
- Opt-in area-outline drafting: capture aimed/feet positions, undo, preview and
  export a polygon JSON. A separately enabled ordinary-PMC survey path permits
  walking outside an Arena boundary without turning that raid into Arena.
- Opt-in pickup drafting: capture positions/type/cooldown and export a new JSON.
  Draft files do not automatically install a new area or activate a layout.
- Development diagnostics and offline harnesses accompany the implementation;
  they are not player-package prerequisites or substitutes for native playtests.

Sources: [dependencies](DEPENDENCIES.md),
[server config page](../ArenaMode.Server/Web/Pages/ArenaConfig.razor),
[settings sync](../ArenaMode.Client/Config/SettingsSync.cs),
[polygon drafting](POLYGON-AREA-REVIEW.md),
[pickup capture](../ArenaMode.Client/Pickups/ArenaPickupSettings.cs).

## 14. Complete map-screen mutator list

There are 23 identifiers but **22 visible slots**: Fast Bots and Zombie Crowd
share one position and are shown in their respective modes.

| Mutator | Scope / prerequisite |
|---|---|
| Fast Movement | Master for player movement bonuses and abilities, including surgical walking. |
| Double Jump | Fast Movement must be on. |
| Bunnyhop | Fast Movement must be on. |
| Air Control | Fast Movement must be on. |
| Airborne Hip Fire | Fast Movement must be on. |
| Air Vault | Fast Movement must be on. |
| Slide | Fast Movement must be on. |
| Infinite Stamina | Fast Movement must be on. |
| No Fall Damage | Fast Movement must be on. |
| Backpack Items | Carried item use and backpack magazines. |
| Killcam | Compatible Killcam and DeathReplay enabled. |
| Pickups | Deathmatch/independent Team DM and a configured layout. |
| Colored Tracers | Team DM or a protected PFT squad. |
| Team Laser Colors | Team DM or a protected PFT squad. |
| Akimbo (WIP) | Eligible pistol; not active in Zombies. |
| Announcer | Per-mode choice, alongside the global audio master. |
| Friendly Fire | Team DM or a protected PFT squad. |
| Auto Quickslots | Medical quickslot binding. |
| Instant Search | Container search. |
| Arena HUD | Combat panels; also subject to the global HUD master. |
| Molotov (WIP) | BigBrain companion available; not Zombies. |
| Fast Bots | Non-Zombie modes. |
| Zombie Crowd | Zombies only; separate preference, default off. |

Source: [mutator selection policy](../ArenaMode.Client/Session/ArenaMutatorSelection.cs).

## Limits and items not to advertise as complete

- Tactical, Zombies, Akimbo and Molotov remain **WIP**. The catalogue above
  describes implemented behavior, not a new certification of every scenario.
- M60 spare boxes and some large kit medical supplies still have recorded
  placement limits. See the [known follow-ups](FUTURE-WORK.md).
- No network multiplayer/Fika support, Capture the Flag, Domination, King of the
  Hill or persistent online leaderboard is implemented. The disabled mode
  previews and reserved flag recordings do not make those modes playable.
- Difficulty-menu unification, more pickup layouts and general AI/navigation
  improvements remain follow-up work; native bots do not gain player vaulting
  or guaranteed pathfinding through every obstacle.
- This inventory does not publish the repository, grant new third-party asset
  rights or replace the outstanding release/package checks.
