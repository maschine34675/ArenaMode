# Changelog

Notable changes since **v0.1.0**, reconstructed from the Git history and published
release notes. Dates below are GitHub publication dates. Unreleased covers changes
merged through [`36402fe`](https://github.com/maschine34675/ArenaMode/commit/36402fe)
on 2026-10-23; it is not part of the 0.1.3 download.

Released entries describe the behavior **at that release**. Several compatibility
adapters were subsequently removed or moved to Redeploy.Core, as noted under
Unreleased. See [Dependencies and compatibility](DEPENDENCIES.md) for the current
state. Allowing a mod through the whitelist is not a compatibility guarantee.

## [Unreleased]

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

### Compatibility changes

- Moved general body-replacement fixes for **Recoil Rework Legacy, Continuous Load
  Ammo and Continuous Healing** into **Redeploy.Core 1.0.9**, sharing them with
  standalone Redeploy. Arena now requires at least that Core version and no longer
  installs duplicate hooks for those mods.
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

Details: [Arena sessions](ARENA-SESSION-20261021.md),
[Bowl team boxes](BOWL-TEAM-BOXES-20261023.md),
[shared body-replacement fixes](SHARED-BODY-COMPAT-20261023.md).

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

[Release notes and verification limits](RELEASE-0.1.3.md).

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

[Release notes and verification limits](RELEASE-0.1.2.md).

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

[Release notes and verification limits](RELEASE-0.1.1.md).

## [0.1.0] - 2026-10-19

Baseline for this changelog: the currently published `v0.1.0` tag at
[`9708949`](https://github.com/maschine34675/ArenaMode/commit/9708949).
The release archive was refreshed on 2026-10-19 without changing its version;
earlier same-version archives are not reconstructed here. Features already in
this baseline are documented in its
[feature inventory](https://github.com/maschine34675/ArenaMode/blob/v0.1.0/docs/FEATURES.md).

[Unreleased]: https://github.com/maschine34675/ArenaMode/compare/v0.1.3...36402fe
[0.1.3]: https://github.com/maschine34675/ArenaMode/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/maschine34675/ArenaMode/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/maschine34675/ArenaMode/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/maschine34675/ArenaMode/releases/tag/v0.1.0
