# Changelog

Notable changes since **v0.1.0**, reconstructed from the Git history and published
release notes. Dates below are GitHub publication dates.

Released entries describe the behavior **at that release**. Several compatibility
adapters were subsequently removed or moved to Redeploy.Core, as noted under
0.2.0. See [Dependencies and compatibility](docs/DEPENDENCIES.md) for the current
state. Allowing a mod through the whitelist is not a compatibility guarantee.

## Unreleased

- Add the PMC VOICELINES mutator for restrained native action callouts. Team DM
  rotates faction voices between matches, including English BEAR voices, without
  changing saved profiles. Respawns retain the match voice and the announcer
  takes priority. [Details](docs/PMC-VOICELINES-20261026.md).

- Compose Arena's backpack reload and item-access extension with Use Items Anywhere
  2.1.6. Its configured slots take precedence, and Arena's magazine HUD uses the
  same candidate query as reloading.
- Update shared Redeploy.Core to 1.0.15. This includes the 1.0.14 ImpactCue 2.0.3
  telemetry and Enemy Markers 1.4.0 camera/cleanup fixes, tested in Arena and
  standalone Redeploy. Add the reviewed clients and ImpactCue's Web Config
  server to the development whitelist.
- Integrate Fast Grenade Throw 2.0.0 with Arena grenade priority and input locks.
  Native tests covered priority Molotov, underhand RGD and blocked inventory input.
- Retire Quick Cancel 2.0.0's old-body pending controllers and PiP Disabler 2.1.0's
  owned optic caches through shared Core. Isolate Arena's cold-kit optic preloads.
  Actual cancellation/firearm recovery and two scoped Arena deaths passed;
  the same final Core also passed two Factory respawns with Arena absent,
  including a scoped death and cancellation after respawn.
- Review HUD Breakout 1.2.0 without adding a body adapter. Its health/ammo/grenade
  values followed two Arena respawns; grenade reach and spectator semantics
  remain those of the foreign HUD.
- Add Fast Grenade Throw 2.0.0, Quick Cancel 2.0.0, HUD Breakout 1.2.0 and
  PiP Disabler 2.1.0 to the development default whitelist after the bounded
  Arena and standalone checks. No foreign packages are bundled.
- Repair Amand's Graphics 1.8.0 camera handover in Core, preserving ON/OFF and
  scene defaults. Keep it unlisted: Labyrinth's initial native activation fails
  before Core replacement. The successful Factory tests do not resolve that
  separate map blocker. See [remaining-mod evidence](docs/REMAINING-MOD-COMPAT-20261026.md).
- Select USEC or BEAR for Team DM in the map menu, including the corresponding
  team starts and respawns. The real PMC faction and equipment stay unchanged.
- Use consistent USEC blue / BEAR red for Team DM lighting, tracers, lasers and
  team HUD accents. Connect ArenaMapPort score monitors to faction scores,
  living players and the match clock.
- Disable native extractions in every Arena mode/map, preventing accidental
  raid endings in team boxes. Ordinary raids and leaving Arena through Esc
  retain their existing behavior.

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
[Bowl team boxes](docs/BOWL-TEAM-BOXES-20261023.md),
[shared body-replacement fixes](docs/SHARED-BODY-COMPAT-20261023.md).

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
