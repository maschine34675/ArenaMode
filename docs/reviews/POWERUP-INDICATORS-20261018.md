# Bot powerup indicators

Historical implementation note: the three-ring cages described below are
replaced by a shared, visible-body-only inner contour as of 2026-10-19. See
[current rendering, ownership and verification](BOT-EFFECT-OUTLINES-20261019.md).
Shield/Double Damage gameplay and colors remain; no native bot material or
animator is replaced. The old cage dimensions are not current configuration.

Implemented 2026-10-18. Deathmatch and independent Team DM bots with active
Shield or Double Damage use the same three-ring world-space cage as the existing
TDM spawn protection. Colors come from the same palette as their pickup markers:
blue `(0.25, 0.8, 1)` for Shield; pink `(1, 0.25, 0.7)` for Double Damage.

Each effect owns its own cage. Shield has a 0.60 m radius and Double Damage a
0.68 m radius, with proportionately different heights. Both remain visible when
simultaneous; neither suppresses or recolors the smaller 0.52 m pale spawn cage.
This is presentation only: the damage/shield rules, pickups, cooldowns, native
health coefficients and spawn-protection authority are unchanged.

The existing exact Player / captured ActiveHealthController life owns the
indicator. `ArenaPickupRuntime.IsLiveActor` continues to require the captured
match, game, world, round, active bot and both native player registrations.
There is no lookup by profile ID to recover an effect on another body.
Only bodies with active visual effects are retained for frame updates. Expiry,
depleted Shield, death, health-owner replacement, registration loss and runtime
reset dispose owned roots/materials and release those references. Shield depletion
hides its cage in the same damage adapter call. Ordinary frame follow/expiry runs
before the pickup contact-scan throttle; game-time durations still pause normally.

A disabled native body hides its cages without resetting gameplay timers. If the
same still-registered body is re-enabled, only its remaining effects reappear.
A discarded/recycled body is forgotten by the exact actor guard. Local-player
effects use the existing HUD; this adds no first-person cage. Arena HUD visibility
does not control bot cages, and world-space placement uses no screen projection.

Views are children of the owned GameWorld, not the pooled bot hierarchy. Only
private LineRenderer objects/materials are created; no collision, light, native
material, inventory or template changes. A missing shader or failed renderer
does not reject the collected buff. Construction is attempted once per active
effect appearance, with one process-level warning, rather than per-frame retries.

## Validation and native seams

`pwsh -NoProfile -File tools/ArenaPowerupChecks/Verify.ps1 -SptPath D:/SPT41`
links real policy/adapter/view code, runs 417 behavior assertions and 45 installed
IL/source contracts, and rejects 18 compiled fault controls. Negative controls
include stale reset visuals, wrong colors, local-body leakage, frozen positions,
disabled-body visibility, missing Double Damage, delayed depletion and per-frame
failed rendering retries. Existing damage invariants remain covered.

The renderer is an explicit Unity double in offline checks. Parent integration
must build the actual client and verify in a native raid: a bot collects each
pickup, colors are readable with the HUD off, simultaneous buffs remain distinct,
the moving cage follows the correct physical body, and expiry/depletion/death/exit
leave no cage. Native rendering/DLSS and pooled-object lifetime are not proven by
the offline assertions. The existing TDM default spawn constructor is preserved,
including its old color, radius, vertical center and independent disposal.
