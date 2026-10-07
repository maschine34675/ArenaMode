# Zombie Mode (v0 prototype)

## Purpose and v0 scope

Zombies is Arena's fifth mode: solo, endless waves of EFT's own infected
against the local player, instant respawn, Arena kits, no PitFireTeam, no
loot. It does not invent a zombie system — it reactivates one EFT already
ships (the Halloween-2024 infection event) for exactly one armed raid, drives
its spawn count and pacing itself instead of the native crowd spawner, and
keeps everything else about Arena (the sacred profile, bit-identical normal
raids, Arena kits) unchanged. See `ArenaModeCatalog.ZombieId` ("ZOMBIES" on
the map screen) and `ArenaPopulationPolicy.Zombie`.

v0 is deliberately narrow. In scope: the four GENERIC infected roles
(`infectedAssault`/`infectedPmc`/`infectedCivil`/`infectedLaborant`) at
`normal`/`hard` difficulty only, one endless wave director, a HUD status
line, Arena's assist/scoreboard/corpse-cleanup plumbing extended just enough
to not misbehave under it, and the server-side clone edits the native event
needs. Out of scope is listed in its own section below — read it before
assuming a native Halloween behaviour (crowd spawns, the boss zombie,
announcer lines) is reachable here.

## The native system this mode reactivates

EFT 0.16.9.5 ships a complete "Halloween 2024" infected-bot event, off by
default on SPT and effectively unused by any other Arena mode. Every fact
below is decompile-verified against `D:\SPT41\Development\_decompiled`; file
paths are relative to that root unless marked `EFT\`.

- **Infected roles and brain.** Roles 60-64 are infected
  (`WildSpawnTypeExtension.IsInfected`, `WildSpawnTypeExtension.cs:540-547`:
  true for `infectedCivil`/`infectedLaborant`/`infectedAssault`/`infectedPmc`
  and the boss role `infectedTagilla`). `StandartBotBrain`'s role switch
  (`StandartBotBrain.cs:185-189`) routes all four generic roles to
  `InfectedLayersStrategy`.
- **Impossible throws; difficulty picks melee vs. shooting.**
  `InfectedLayersStrategy`'s constructor (`InfectedLayersStrategy.cs:77-99`)
  calls `GetZombieModeByDifficulty` (149-158), a switch with NO default case
  for three values and a `throw new ArgumentOutOfRangeException()` for
  everything else: `easy` → `Slow` (melee), `normal` → `Fast` (melee),
  `hard` → `Shooting` (draws the profile's PM pistol), `impossible` → throws
  during bot construction. This is exactly why `ZombieRoleMix` only ever
  produces `normal`/`hard` and why `BurstSpawnService.SpawnInfected` rejects
  anything else at the door (`BurstSpawnService.cs:913-918`) rather than let
  a bad draw reach this constructor.
- **The construction-order bug.** `BotsController`'s constructor passes its
  OWN `_botSpawner` field into `new BotsEventsController(...)` at
  `EFT\BotsController.cs:211` — six lines before that same field is actually
  assigned, at `EFT\BotsController.cs:236`. `BotsEventsController`'s own
  constructor (`BotsEventsController.cs:28-37`) immediately builds
  `BotHalloweenWithZombies = new BotHalloweenWithZombies(this, botsList,
  spawner, events?.Halloween2024)` at line 35 — UNCONDITIONALLY, every raid,
  whether or not the Halloween flag is ever turned on — handing it that same
  still-null spawner. `BotHalloweenWithZombies._spawner` is a public
  `[NonSerialized]` field (`BotHalloweenWithZombies.cs:67`); its own
  `Activate()` (142-158), gated on the flag and invoked from
  `BotsController.Init()` at `EFT\BotsController.cs:271` inside the same
  loading call, calls `AddBackupProfiles()` (188-211), whose body
  dereferences `_spawner.AddToTargetBackup(...)` for 4 roles × 3
  difficulties × 10 — the exact null reference the flag's first-ever flip-on
  would hit if nothing repaired the field first. `ZombieNativeEventBridge`'s
  hook 1 (below) is that repair.
- **The 498-profile preload.** `LocalGame.vmethod_1` (`EFT\LocalGame.cs:
  161-200`), the loading-time method that also starts `BotsController.Init`,
  unconditionally calls the STATIC `BotHalloweenWithZombies.
  GetProfilesOnStart()` at lines 166-169 whenever
  `Location.Events.Halloween2024.InfectionPercentage > 0` — independent of
  the `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` flag entirely, and strictly BEFORE
  `BotsController.Init()` (called at line 184, which is where the
  construction-order bug above actually lives). The native body
  (`BotHalloweenWithZombies.cs:392-407`) is a fixed list of 10
  `CountTypeBotWave` entries across all four roles and three difficulties
  (`easy`/`normal`/`hard`, including an `easy` share nothing in v0's role mix
  ever draws) totalling **498** profiles, which `BotProfileClient.
  TryLoadBotsProfilesOnStart` then requests from the server, each row capped
  at `Core.LOCAL_BOTS_COUNT` (100). This is the dump
  `ZombieNativeEventBridge`'s hook 3 replaces with Arena's own small opening
  wave.
- **The inert 8-attacker throttle.** `BotHalloweenDistributions`
  (`BotHalloweenDistributions.cs`) defines `MAX_BOT_PER_ENEMY = 8` (line 31)
  and, every 10 seconds (`AIPeriodAction`, line 39), sorts each enemy's
  attacking infected by distance and calls `InfectedLayersStrategy.
  SetCanAttack(false)` for every one past the 8 closest
  (`UpdateCanAttack`, lines 47-76). `SetCanAttack(false)` activates
  `WAIT_LOGIC` (layer priority 5, `InfectedWaitLayer`) — but
  `InfectedWaitLayer.ShallUseNow()` (`InfectedWaitLayer.cs:50-53`)
  unconditionally `return false;`, so the AI core's own layer-selection
  logic can never actually hand control to that layer. The throttle is real
  source, wired correctly, and permanently inert: an infected past the 8th
  nearest is marked "cannot attack" and attacks anyway, because the one
  mechanism meant to hold it back can never be selected. v0 does not rely on
  or work around this; `Settings.ZombieMaxAlive` already bounds total
  concurrent infected independently of per-target attacker counts.
- **Pursuit and the call relay.** `InfectedAttackLayer`'s own sighting path
  reaches `BotHalloweenWithZombies.OnSeeEnemy`/`OnGetHit` →
  `StartPursuit` (`BotHalloweenWithZombies.cs:348-385`), which opens one
  `BotHalloweenEventPursuit` per distinct target and enrolls every
  currently-infected bot. `BotHalloweenEventPursuit.CheckOnVisual` (93-116)
  grows its own call radius by `ZombieCallDeltaRadius` every
  `ZombieCallPeriodSec` up to `ZombieCallRadiusLimit` and, for every
  enrolled zombie within that radius, fires `HalloweenGameEvent.Call`, which
  `InfectedLayersStrategy.Call` (160-168) turns into
  `_owner.BotsGroup.AddPointToSearch(positionToCheck, 1000f, _owner)` — the
  exact call `ZombieSpawnService.OnZombieArrived`/`NudgeIdleZombies` also
  make directly on arrival/idle, so a freshly spawned zombie is aware of the
  player whether or not this native relay ever successfully arms this raid.
- **The crowd spawner is neutralized, not merely configured low.**
  `BotHalloweenCrowdSpawn.CalcBotsCanSpawnedPerCrowd` (182-193): with
  `InfectionPercentage >= 100` (which `ZombieLocationSettings` always writes)
  it returns `MaxCrowdAttackSpawnLimit` directly (line 190); the server
  clone fixes that to 0, so `BeginCrowdSpawn` (176-180) sets
  `spawnsRemains = 0` and the crowd spawner's own spawn loop never runs.
  `CrowdsLimit = 0` and an hour-long `CrowdCooldownPerPlayerSec` are
  belt-and-suspenders on top, in case a future build changes what a zero
  `MaxCrowdAttackSpawnLimit` does.
- **`TeleportBotNearToTarget` degenerates to self-relative jitter.**
  `InfectedLayersStrategy`'s constructor calls `GetNearestPursuitCenter`
  then `TeleportBotNearToTarget(nearestPursuitCenter)` unconditionally
  (`InfectedLayersStrategy.cs:95-96`). `GetNearestPursuitCenter`
  (`BotHalloweenWithZombies.cs:213-247`) falls back to
  `owner.Position` — the bot's OWN, just-assigned spawn position — whenever
  no `BotHalloweenEventPursuit` with `TargetAllowToSpawnCrowd` exists yet,
  which is true for every opening-wave spawn and for the whole raid whenever
  the native event never armed. `TeleportBotNearToTarget` (115-147) then
  computes `(owner.Position - point).normalized`, which is the zero vector
  when `point == owner.Position`, and relocates the bot to a semi-random
  cover point near that same degenerate direction — not toward the player at
  all. `ZombieNativeEventBridge`'s hook 5 (below) suppresses this call
  outright for a live Zombie raid so it can never defeat
  `BurstSpawnService.SpawnInfected`'s own 25-70 m spawn ring.
- **Body model: `UseSimpleAnimator`, not the role.** `LocalPlayer.cs:52`
  (and `EFT\Interactive\Corpse.cs:120` for the corpse) select
  `InGameBundles.ZOMBIE_BUNDLE_NAME` purely from
  `profile.Info.Settings.UseSimpleAnimator`, a plain bool on the profile —
  nothing in this mode sets it directly; it is carried by the infected
  profile data the server returns for `infected*` bot types
  (`SPT_Data\database\bots\types\infected*.json`), the same way every other
  field on a generated infected profile is.
- **The flag itself.** `BotInternalSettingsController.Core.
  ACTIVE_HALLOWEEN_ZOMBIES_EVENT` is a public instance field on a public
  static singleton (`BotGlobalsCoreSettings.cs:308`, default `false` — no
  initializer on a `bool`), read by `BotsEventsController.Activate()`/
  `ManualUpdate()`/`SpawnAction()` (`BotsEventsController.cs:39-57,59-69,
  71-77`) to decide whether `BotHalloweenWithZombies` ever runs at all. It
  is absent from SPT's shipped `core.json`, so it is `false` on every
  ordinary install.

## Architecture

**Server.** `ArenaLocationMutator.Mutate` calls `ZombieLocationSettings.
Apply(location)` on the already-cloned `LocationBase` (never the shared
template) only for `ArenaPopulationPolicy.Zombie`
(`ArenaLocationMutator.cs:174-179`), on top of the same quiet-clone branch
every `OwnsEntirePopulation` mode already takes (waves emptied, bosses
silenced, ABPS window closed). `ZombieLocationSettings` is one pure, static
type (`ArenaMode.Server/Services/ZombieLocationSettings.cs`) that writes
`Location.Events.Halloween2024` and raises `BotMax`/`BotMaxPvE`/
`MaxBotPerZone` to a fixed `MinimumBotCap = 60`; every constant it writes
carries the decompiled fact that makes it matter in its own doc comment
(`InfectionPercentage`/`MinInfectionPercentage` = 100 so `LocalGame.
vmethod_1`'s preload gate fires; `MaxCrowdAttackSpawnLimit`/`CrowdsLimit` = 0
to neutralize the crowd spawner; `MinSpawnDistToPlayer` = 25 kept only as a
fallback for the one case hook 5 below cannot cover; `TargetPointSearchRadiusLimit`
= 400 so `InfectedTargetLayer.ShallUseNow` stays usable on every map in
scope; `ZombieCallRadiusLimit`/`Delta`/`Period` = 400/50/1 so a sighting
reaches the whole map within 8 seconds; `DefaultInfectedLookCoeff` floors a
missing-or-zeroed map value to native 1.0 without overwriting an
intentionally tuned one; `DefaultSpawnParamRole`/`Difficulty`/`Weight` give
`BotHalloweenDistributions` a sane non-empty default it can never actually
use, since the crowd path is already dead). `Apply` never shares this block
with any other mode's clone.

**Client**, all in `ArenaMode.Client/Modes/` and `ArenaMode.Client/Bots/`:

- **`ZombieMode`** (`Modes/ZombieMode.cs`) is the `IArenaMode` shell:
  freezes `ZombieWavePolicy.Config` once at `OnMatchStart` (never re-reads
  `Settings` afterward — the same frozen-at-start convention as
  `TeamDeathmatchMode`/`RoundsMode`), drives `ZombieWavePolicy.Evaluate`
  every 0.1 s with the real clock and `ZombieSpawnService`'s real
  alive/pending counts, turns a "spawn one now" into a `ZombieRoleMix.Next`
  draw plus a `ZombieSpawnService.TryRequest` dispatch, and nudges idle
  zombies toward the player every 5 seconds independently of whether the
  native pursuit relay ever armed.
- **`ZombieWavePolicy`** (`Modes/ZombieWavePolicy.cs`) is a pure state
  machine with no EFT/Unity types (linked directly into `ArenaMode.Tests`):
  wave `n` has `WaveStartSize + WaveGrowth*(n-1)` infected; one spawn request
  goes out every `SpawnIntervalSeconds` while `alive+pending` stays under
  `min(MaxAlive, remainingToSpawn + alive)`; a wave ends only once everything
  in it has been requested AND nothing from it is alive. Two safety valves
  were added in review (see "Fixes from review" below): a
  `StuckWaveTimeoutSeconds` watchdog for a wave whose tail never clears, and
  `ReportFailedDispatch` to undo an optimistic spawn-slot commit that the
  real dispatch never actually fulfilled.
- **`ZombieRoleMix`** (`Modes/ZombieRoleMix.cs`) is the pure, deterministic
  role/difficulty draw: `infectedAssault` 50% / `infectedPmc` 25% /
  `infectedCivil` 15% / `infectedLaborant` 10%, `normal` unless an
  independent `ZombieShooterPercent` chance rolls `hard`.
- **`ZombieProfilePlan`** (`Modes/ZombieProfilePlan.cs`) is the pure
  apportionment layer: `OpeningProfiles` turns the same weight table into
  the native preload's opening-wave list (replacing the 498-profile dump),
  and `BackupTargets` is what gets registered with the native backup loader
  instead of the skipped `AddBackupProfiles` order, floored to
  `MinimumBackupCount = 4` because `BotProfileBackuploader`'s own top-up only
  fires once its deficit exceeds 3 (`BotProfileBackuploader.cs:144`,
  `num2 - 3 > 0`).
- **`ZombieSpawnService`** (`Bots/ZombieSpawnService.cs`) is the seam
  between the pure policy and the real native spawn path: `TryRequest` calls
  `BurstSpawnService.SpawnInfected`, `CountAliveInfected`/`NudgeIdleZombies`
  read `BotsController.Bots.BotOwners` directly, `OnZombieArrived` gives a
  freshly spawned bot the same group awareness `ArenaHostility.
  ReportPlayerTo` gives any other Arena bot plus the native pursuit's own
  `AddPointToSearch` call, and `RetireAllAliveInfected` administratively
  kills whatever the stuck-wave watchdog needs cleared, through
  `ArenaAdministrativeDeathGate` so it is never scored as a player kill.
  `BurstSpawnService.SpawnInfected` itself (`Bots/BurstSpawnService.cs:
  903-944`) rejects anything but the four generic infected roles and
  `normal`/`hard` difficulty at the door, then reuses `SpawnPair`'s existing
  25-70 m point ring and `SpawnBotsInZoneOnPositions` dispatch exactly like
  the ambient burst pump and `SpawnSquad` do.
- **`ZombieNativeEventBridge`** (`Bots/ZombieNativeEventBridge.cs`) is five
  independently-`TryEnable`d Harmony hooks, each failing closed internally
  (an exception inside a hook body leaves the native event off, never a
  half-armed or changed non-Zombie raid):

  | # | Target | Gate | What it does |
  |---|---|---|---|
  | 1 | `BotsEventsController.Activate()` prefix | `IsArmedZombieLoading` (+ unconditional restore check, see below) | Repairs `BotHalloweenWithZombies._spawner`/`._crowdSpawn._spawner` from the controller's own (by-now-assigned) `BotSpawner`, saves the flag's prior value, then — only if the repair succeeded — flips `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` on. |
  | 2 | `BotsEventsController.Dispose()` postfix | none (acts only if hook 1 saved a value) | Restores the flag at raid teardown (`BotsController.StopGettingInfo` → `EventsController.Dispose()`, `EFT\BotsController.cs:643`). |
  | 3 | static `BotHalloweenWithZombies.GetProfilesOnStart()` prefix | `IsArmedZombieLoading` | Returns `ZombieProfilePlan.OpeningProfiles` instead of the native 498-profile list; also the one place that snapshots `ZombieWaveStartSize`/`ZombieShooterPercent` for the whole raid, since it fires before hook 1 in `LocalGame.vmethod_1`. |
  | 4 | instance `BotHalloweenWithZombies.AddBackupProfiles()` prefix | `IsArmedZombieLoading` | Skips the native 120-profile standing order; `ZombieSpawnService.RegisterBackupTargets` registers Arena's own, much smaller targets at match start instead. |
  | 5 | instance `InfectedLayersStrategy.TeleportBotNearToTarget()` prefix | `ArenaSession.IsActive` + live mode == Zombie | Skips the native ctor-time teleport outright for any bot spawned during a live Zombie raid, since it decompile-verified collapses to self-relative jitter with no pursuit established yet. |

  Hooks 1/3/4 gate on `ArenaPopulationPolicy.IsArmedZombieLoading`
  (`ArenaMode.Shared/ArenaPopulationPolicy.cs:119-124`: armed + exact map
  match + mode == `"zombie"`), the SAME loading-time exception class
  `ArenaProfilePreloadPatch` already documents in `AGENTS.md`: all three
  fire inside `LocalGame.vmethod_1`, before `BotsController.Init()` has even
  run, which is before `ArenaSession.IsActive` can have latched — so they
  cannot gate on `IsActive` the way every live-raid patch does, and instead
  gate on the same armed+map check the kit/profile preload uses. Hook 5 is
  different in kind (gates on `IsActive`, not the loading-time check)
  because it only ever matters once bots are actually spawned into a
  running raid, never during loading.

  **Flag restore paths.** The flag has three independent restore paths, not
  one: hook 2 (`Dispose()`, the normal end-of-raid path); `ArenaSession.
  OnRaidEnded`'s own safety-net call to `ZombieNativeEventBridge.
  RestoreFlag()` (idempotent — whichever of the two runs first clears the
  saved value so the second is a no-op); and, since the P0 fix below, hook
  1's own Prefix, which ALSO restores a still-saved flag from a PREVIOUS
  raid before doing anything else, the moment it observes one on ANY later
  raid's `Activate()` call — because `BotsController.Init()` calls
  `Activate()` unconditionally, every raid, Arena or not, Zombie or not.

## Settings and defaults

All in the `"Zombie"` BepInEx config section
(`ArenaMode.Client/Config/Settings.cs`), frozen at match start unless noted:

| Setting | Default | Range | Notes |
|---|---|---|---|
| `MaxAlive` | 20 | 4-56 | Ceiling kept at 56, not 60: the server clone's own `BotMax`/`MaxBotPerZone` floor (`ZombieLocationSettings.MinimumBotCap`) is a fixed 60, and the client cap this feeds (`ArenaBotCapPatch`: `MaxAlive + 2`, up to 58) must stay under it. |
| `StuckWaveTimeoutSeconds` | 100 | 0-600 | Watchdog ceiling; 0 disables it (wave then waits for every infected to die naturally, with no recovery if one is unreachable). |
| `WaveStartSize` | 8 | 1-100 | Wave 1's size. |
| `WaveGrowth` | 4 | 0-50 | Extra infected per wave over the last. |
| `IntermissionSeconds` | 10 | 0-60 | Pause between a cleared wave and the next. |
| `SpawnIntervalSeconds` | 1.0 | 0.2-10 | Seconds between individual spawn requests while filling toward `MaxAlive`. |
| `ShooterPercent` | 0 | 0-100 | Chance each spawn is `hard`/Shooting instead of `normal`/Fast melee. |
| `ReplaySeconds` | (shared default) | — | KillcamReplay duration, same per-mode binding every other mode uses. |
| `ColoredTracers` | false | — | Independent of every other mode's tracer setting and of `Effects/TeamLasers`, which Zombie does not use at all. |

## What is deliberately NOT in v0

- **No bot movement/agility boost.** Infected use whatever
  `InfectedLayersStrategy.GetZombieModeByDifficulty` and the profile's own
  movement speed give them; `BOT-MOVEMENT-REVIEW.md`'s independent
  walk/sprint controls are not wired to Zombie.
- **No announcer lines.** Wave-start uses `ArenaMatch.ReportTextCallout`
  (the same plain callout other modes use for a brief HUD line), not the
  `AnnouncerExpansion`/awards pipeline.
- **No pickups.** `ArenaMode.Client/Pickups`' allowlist is
  `mode is RoundsMode || mode is TeamDeathmatchMode`; Zombie is excluded by
  construction, matching `PICKUPS-REVIEW.md`'s documented restriction.
- **No lasers.** `ArenaTeamLaserColors` explicitly excludes Zombie from its
  solo-mode cosmetic recoloring list (see its own in-code comment) — an
  unrecognized mode id falling through to "no lasers" is the intended
  behaviour here, not an oversight.
- **No PitFireTeam.** `ArenaModeCatalog.CanSelect` still only allows
  `TacticalId` when `tacticalOnly` is set; a selected full PFT roster
  continues to force protected Tactical and excludes every other mode,
  Zombie included.
- **No corpse-lifetime tuning beyond the one exemption change — true for v0
  only; superseded by v3 (2026-10-07).** v0's only corpse-cleanup change was
  dropping the "near the player" distance exemption to 0 for Zombie (see the
  fix list below). Since v3, Zombie also sweeps at its own faster cadence and
  drains releases between sweeps — see "v3 (2026-10-07): faster corpse
  release in Zombie mode" below for what that changes and, more to the point
  of this bullet, what it still does not: every `MayRelease` hold (ragdoll-
  settled, open-loot-window, held-interactable, own-corpse handling, damage
  sources, pending aggressor, map marker) is exactly as untouched by v3 as it
  was by v0.
- **No `infectedTagilla` (the boss zombie).** `WildSpawnTypeExtension.
  IsInfected` is also true for this fifth, pure-boss role, but it needs
  `BotBossSpawn`'s `SetBoss` initialization the way `SpawnManagedBot`'s
  dedicated boss branches provide — `BurstSpawnService.SpawnInfected`
  explicitly rejects it at the door (`BurstSpawnService.cs:905-906`), and
  `RoleClassifier.Label`'s `"ZOMBIE"` case still matches it (for a
  consistent kill-feed label if it ever appears some other way) without
  treating it as reachable from this mode's own spawn path.

## Test matrix and the fault controls actually demonstrated

Offline (`dotnet test ArenaMode.Tests`, no game required): **828 passed, 2
skipped (pre-existing, environment-gated, unrelated to Zombie), 0 failed**
— confirmed by re-running the suite for this document, with the
`artifacts/zombie-nodeploy/must-not-exist` no-deploy sentinel directory
absent both before and after. `ZombieWavePolicyTests.cs`,
`ZombieRoleMixTests.cs`, `ZombieProfilePlanTests.cs` and `ZombieServerTests.cs`
are pure/production-linked and carry no EFT/Unity dependency, matching this
repo's existing `ClientRules`-linked pattern (see `ArenaMode.Tests.csproj`).

Fault controls actually demonstrated (not merely asserted — a test proven to
fail against the bug it claims to catch, per `AGENTS.md`'s own convention):

- **`AnAliveButPermanentlyUnreachableInfectedWouldFreezeTheWaveWithNoWatchdog`**
  reproduces the wave-softlock bug directly: with the watchdog disabled
  (`StuckWaveTimeoutSeconds = 0`), one alive-but-stuck infected freezes
  `Phase` at `Wave` forever, exactly as the original P0 finding described.
- **`TheStuckWaveWatchdogForceClearsAndSignalsAdministrativeRetirement`**
  confirms the fix: once armed and past the configured ceiling, `Evaluate`
  force-clears the wave and returns `ForceRetireStuckInfected = true` exactly
  once.
- **`APermanentlyFailingDispatchDoesNotCountAsSpawnedOrSilentlyEmptyTheWave`**
  reproduces the optimistic-slot-commit bug: without `ReportFailedDispatch`,
  a persistently failing dispatch still drives `SpawnedThisWave` to the
  wave's full size and the wave "completes" with zero real zombies; the test
  fails at iteration 5 without the fix and passes with it.
- **`OnlyConfirmedDispatchesCountTowardTheWaveWhenSomeFail`** — a mixed
  fail/succeed sequence confirming only confirmed dispatches ever reduce
  `remaining`.
- **`ZombieServerTests.ZombieLeavesHostilityVanillaEvenUnderFfa`** proves the
  generic FFA hostility-rewrite branch would otherwise mutate an
  `infectedAssault` `AdditionalHostilitySettings` entry, by reference-equality
  and JSON-snapshot assertion against the clone.
- **`ZombieEnablesTheNativeInfectionEventOnTheClone`**,
  **`ZombieKeepsAMapsExistingCrowdAttackSpawnParamsAndLookCoeff`** and
  **`ZombieRaisesTheCloneBotCaps`** assert concrete field values on the
  server clone (not merely "does not throw"), including that an existing
  map's own `InfectedLookCoeff`/`CrowdAttackSpawnParams` survive `Apply`
  unchanged.
- **`WaveSizeGrowsLinearlyFromTheStartSize`**,
  **`SpawningStopsAtTheConfiguredMaxAliveEvenWithManyLeftToSpawn`**,
  **`TheWaveEndsOnlyOnceEverythingIsSpawnedAndNothingIsAlive`**,
  **`IntermissionLastsExactlyItsConfiguredDurationThenAdvancesTheWave`** and
  **`ZeroLengthIntermissionAdvancesAndSpawnsInTheSameCall`** cover the wave
  arithmetic's edge cases, including the bounded (depth-2) self-recursion a
  zero-length intermission or wave would otherwise require.
- `ZombieRoleMixTests` covers determinism-by-seed, divergence-by-seed, the
  null-`Random` guard, 0%/100% shooter edge cases, and distribution within
  tolerance over 20,000 draws.
- `ZombieProfilePlanTests` covers exact apportionment (counts sum back to
  the requested total across many sizes including 0, 1, 97, 100) and the
  `MinimumBackupCount` floor.

**Known test-coverage gap, by design, not an oversight:** `ZombieWavePolicy`
is intentionally oblivious to real dispatch outcomes (it only knows
"accepted the optimistic slot" vs. "a failure was reported back"), so no
test at that layer alone exercises `ZombieMode.Tick`'s actual role/difficulty
mapping and `ZombieSpawnService.TryRequest` call together with a permanent
dispatch failure; that integration is covered at the `ZombieWavePolicy`
contract boundary (the two tests above) rather than end-to-end through
`ZombieMode`, which has no EFT-free seam to test through.

## Fixes from review (summary; see commit messages for full reasoning)

The adversarial review that followed this implementation found two P0s, four
P1s and several smaller issues; every P0/P1 and every small-and-safe P2 was
fixed, one P2 (reusing `BurstSpawnService`'s shared per-bot log lines for a
pure verbosity concern) was explicitly skipped as disproportionate without a
GPU-measured baseline first, and the P3 notes were left untouched as
instructed:

- **P0 — native flag leak across raids.** Fixed by making hook 1's own
  Prefix a third, unconditional restore path (see "Flag restore paths"
  above).
- **P0 — wave softlock on a stuck zombie.** Fixed by the
  `StuckWaveTimeoutSeconds` watchdog and `RetireAllAliveInfected`.
- **P1 — native ctor-time teleport defeating the spawn ring.** Fixed by
  hook 5; this also resolved the related P1 (the `ZombieLocationSettings`
  doc comment that previously assumed the native teleport was a genuine
  homing move).
- **P1 — scoreboard entries never trimmed.** Fixed: Zombie's
  `SynchronizeScoreboard` branch now syncs against an empty roster (still no
  per-bot allocation) instead of returning unconditionally, so
  `TrimOrdinaryHistory` can reclaim the permanent one-life entries every
  killed infected leaves behind.
- **P1 — optimistic wave-slot commit silently emptying waves.** Fixed by
  `ZombieWavePolicy.ReportFailedDispatch`.
- **P1 — corpse cleanup's 15 m exemption defeated by horde melee.** Fixed:
  `CorpseCleanupService.ActiveMinDistanceMeters()` returns 0 instead of 15
  while Zombie is the active mode; every other mode is untouched.
- **P1 (corrected from P2) — pending-request ledger FIFO mismatch.** Fixed:
  `ZombieSpawnService`'s ledger is now keyed by a per-request id instead of
  position.
- **P2s fixed:** `ZombieMaxAlive`'s range ceiling lowered from 60 to 56 (see
  Settings table); the native opening-wave snapshot
  (`ZombieNativeEventBridge.TryGetOpeningSnapshot`) is now the one
  `ZombieMode.OnMatchStart` reads, instead of a second, later `Settings`
  read that a mid-loading F12 edit could desync from; `ZombieMode.Tick`'s
  wave evaluation (and the `BotOwners` scan/HUD string rebuild it drives) is
  throttled to 0.1 s instead of every rendered frame.
- **P2 skipped:** reducing `BurstSpawnService`'s shared per-bot Info
  logging — also used by the ambient burst pump and Team Deathmatch
  placement — for a pure log-verbosity concern, without first measuring
  actual line volume on the GPU host, as the finding itself recommends.

## Considered and dismissed

None. Every finding the review raised was either confirmed and fixed,
confirmed and explicitly skipped with a stated reason (above), or left as a
lower-severity note for later (the P3 items: the stale "four modes" assertion
in `tools/NativeModeSelectionChecks/Program.cs`, `CountAliveInfected`'s
zero-on-failure convention, and this document's own existence as the
requested v0 summary). No finding was refuted as a false positive.

## Known limitations and GPU playtest checklist

Everything client-side in this branch is Harmony patches against Unity/EFT
types that cannot load outside the game — per `AGENTS.md`, "for anything
client-side, verification is still a raid." This document and the offline
test suite are NOT a claim that any of the following has been seen live;
they are decompile cross-checking and clean builds only. The first in-game
test (prefer SPT-GPU-2) must confirm, in order:

1. **Spawning works and bodies look right.** Zombies spawn from wave 1
   onward, use the zombie body/animator (not a PMC model), and land roughly
   in the 25-70 m ring around the player rather than clustering at a map's
   bot-zone spawn points.
   *Confirmed 2026-10-06 (raid 1, `1e76582`): 8 infected spawned within 7 s,
   zombie bodies (`UsedSimplifiedSkeleton=true`), 25-70 m ring, BotZone.*
2. **No `InfectedLayersStrategy` exception.** No `ArgumentOutOfRangeException`
   from `GetZombieModeByDifficulty` (would mean an `impossible`-difficulty
   infected reached the native constructor) and no NRE from
   `AddBackupProfiles`/the crowd spawner (would mean hook 1's repair or hook
   4's skip did not actually install this build).
   *Confirmed 2026-10-06 (raids 1 and 2): no Error lines in either raid.*
3. **No teleport oddity.** A freshly spawned zombie should hold its spawn
   point, not visibly snap or relocate right after arriving — confirms hook
   5 is actually suppressing `TeleportBotNearToTarget`.
   *2026-10-06: the first-raid note in the section below lists this item as
   passed; the results document records no separate observation of it.*
4. **Native melee/shooting behaviour lands.** `normal` infected close and
   melee; any `hard` infected (if `ShooterPercent > 0`) draw and fire a PM
   pistol at range, per the native brain's own difficulty table. The first
   GPU raid (2026-10-06) passed every item on this list EXCEPT this one —
   zombies closed and swung but never landed a hit, root-caused to SAIN's
   own global `RunToEnemyUpdate` prefix; see "Native melee is suppressed by
   SAIN's global prefix" below for the fix and what the NEXT raid must show
   (an actual health drop, a melee kill in the killfeed, the
   `ZombieMeleePatch`/`ZombieMeleeDiagnostics` log lines).
   *Melee half confirmed 2026-10-06 (raid 2, `0c6fab3`): first native swing
   14:12:25 at 0.7 m via the HarmonyX reverse patch, first landed hit
   14:12:25.8, player health dropped, killfeed "ZOMBIE -> USEC . 1 m .
   Infectious strike", 2618 swings / 178 hits over the raid. The ranged half
   (`ShooterPercent > 0`) is still untested.*
5. **The wave HUD counts down and the wave ends.** `HudStatusLine` shows
   `WAVE n | m LEFT` decrementing as zombies die, reaches `WAVE n+1 IN Xs`
   during intermission, and actually advances to wave `n+1`'s first spawn.
   *Partly confirmed 2026-10-06: the HUD showed "WAVE 1 | 8 LEFT | 00:13" in
   raid 1 and counted down per kill in raid 2 ("WAVE 2 | 10 LEFT" after two
   kills), and later waves followed the 10 s pause; a wave ending through kills
   was NOT seen (the watchdog ended every wave).*
6. **Player death/respawn does not stall the wave.** Dying and instantly
   respawning (Zombie's `HoldRespawn = false`) must not pause or reset wave
   progression — infected should keep dying/spawning through the respawn.
   *Confirmed 2026-10-06 (raid 2): death #1 at 14:12:48, respawn pipeline
   0.20 s, 4 s spawn protection, the wave kept running (HUD "1 DEATHS ... WAVE
   1 | 8 LEFT").*
7. **The flag restores after the raid.** Leave the Zombie raid, then start
   an ORDINARY (non-Zombie) raid on the same game session without
   restarting: the BepInEx log must show no `"Arena zombie native bridge"`
   lines for that second raid, and no infected should appear in it.
   *Confirmed 2026-10-06: the flag read `false` after raids 1 and 2; the
   control raid at 14:16:40 (ordinary PMC, Factory, straight after raid 2)
   logged no Zombie/bridge lines and no errors, its bots ran SAIN's Combat
   Layer.*
8. **Performance notes to record.** Frame time/ms over the raid's length,
   peak concurrent bot count (compare against the configured `MaxAlive`),
   and corpse count over time (confirms `ActiveMinDistanceMeters()`'s zero
   exemption is actually letting the overflow pass trim zombie corpses
   instead of leaving them all exempt near the player).
   *Partly confirmed 2026-10-06 (raid 2, 12 zombies): 120 fps (8.3 ms mean),
   RAM 11.2 GB, managed 2.3 GB. Not yet measured: MaxAlive 40-56, corpse count
   over time, any raid longer than 3.9 min.*
   *MaxAlive 56 was measured later the same evening (17.2 ms at 56 alive, 8.4 ms at
   20): see "Performance at 56 concurrent infected (measured 2026-10-06)" at the
   end of this document. The corpse count over time is still not measured.*

Also worth exercising once wave 1 is confirmed: triggering the stuck-wave
watchdog live (strand one infected somewhere unreachable and wait past
`StuckWaveTimeoutSeconds`) and a `StuckWaveTimeoutSeconds = 0` control raid
to confirm the watchdog can indeed be disabled on request.
*2026-10-06: the watchdog fired under real conditions (raid 1 waves 1-4 after
100 s; raid 2 at 14:14:03 and 14:16:05) but only because the tester did not
kill the wave, not on a stranded infected; neither the stranded-zombie case
nor the `StuckWaveTimeoutSeconds = 0` control has been run.*

Open items after the two GPU-2 raids of 2026-10-06 (details in
`GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`):

- **Watchdog semantics.** 100 s without the wave ending is too short for real
  players: a running wave is cleared in the middle of the fight. Proposal: count
  the timer only while no infected has died for 100 s (progress instead of
  duration), or raise the default to 240 s. Then re-run a raid in which a wave
  ends through kills.
- A second `Activate()` per raid with no known caller (see "Double-Activate").
- Performance and corpses at `MaxAlive` 40-56.
- Ranged infected (`ShooterPercent > 0`), Labs/Labyrinth, the boss zombie and
  PitFireTeam remain out of scope for now.

## Native melee is suppressed by SAIN's global prefix

**What the first GPU raid found (SPT-GPU-2, 2026-10-06 12:52-13:11, build
`1e76582`).** Every item on the checklist above except #4's melee half
passed: 8 infected spawned within 7s with zombie bodies
(`UsedSimplifiedSkeleton=true`), knives in hand
(`Player.HandsController is Player+KnifeController`,
`BotWeaponManager.IsMelee=true`, `Melee.MeleeWeaponEquipped=true`,
`Melee.KnifeController` set), they closed to melee range within ~40s, the
HUD counted waves down, the stuck-wave watchdog retired them after 100s and
the next wave followed, and no Error log line appeared anywhere. But **no
zombie ever damaged the player in 19 minutes** — health stayed full the
whole raid.

**Evidence (live inspection of a zombie standing 0.79m from the player,
agent `LastReason = "atk2"` — `InfectedAttackLayer`'s `oneMeleeAttack`
decision):**

- `BotDoorOpener`: `_lastFrameDoorInteractionStatus=CanRun`,
  `NearDoor=false`, `Interacting=false`, `_currentDoorLink=null`;
  `Mover.CurrentState=Default` — so
  `OneMeleeAttackNode.UpdateNodeByBrain`'s own `DoorOpen() == CanRun` gate
  passes every time.
- `GoalEnemy`: `Distance=0.79`, `IsVisible=true`,
  `PersonalLastSeenTime` within 1s of `Time.time`, `CanShoot=false`.
  `Physical.CanMeleeHit=true`, `StateIsSuitableForHandInput=true`,
  `KnifeController.CurrentOperation=Idling`.
- `BotMeleeWeaponData._nextTryHitTime == 0.0` the whole time,
  `LastTimeEnemyHit == -100000`, `ShallEndRun=false`: the native
  `RunToEnemyUpdate` (../_decompiled/BotMeleeWeaponData.cs:132-208) never
  executed its body — lines 165-169 would have set `_nextTryHitTime` on the
  very first call.
- `AICoreController._errorsPrinted == 0`: no exception was swallowed by the
  agent loop (AICoreController.cs:49-63) — this is not a crash, it is a
  skip.
- **Root cause.** SAIN (installed; its exclusion list — fetched from the
  server into the static `BotSpawnController.StrictExclusionList` — names
  `infectedAssault`/`infectedCivil`/`infectedLaborant`/`infectedPmc`/
  `infectedTagilla` by role, SAINServerMod/Generators/DefaultBotTypes.cs:
  102-109) patches `BotMeleeWeaponData.RunToEnemyUpdate` with a PREFIX
  (SAIN-master/SAIN/Patches/FixPatches.cs, `RunToEnemyUpdatePatch`):
  <code>if (SAINEnableClass.GetSAIN(profileId, out bot) &amp;&amp;
  bot.SAINLayersActive) { ...SAIN's own version...; return false; } return
  false;</code> — the trailing, UNCONDITIONAL `return false;` is reached for
  every bot `GetSAIN` returns false for, which is every excluded bot (SAIN
  never adds it to its own `_enabledBots` set —
  SAIN/Plugin/SAINEnableClass.cs:23-68,257-272), which is every infected bot
  in this mode. Harmony's own call to the ORIGINAL method is what that
  `return false;` skips — so the native body never runs, and neither does
  SAIN's replacement branch (gated on `SAINLayersActive`, which an excluded
  bot never has).
- **Native chain that must run for a swing**, confirmed end to end against
  the decompile: `OneMeleeAttackNode.UpdateNodeByBrain`
  (OneMeleeAttackNode.cs:10-16: `if (DoorOpen()==CanRun)
  _owner.WeaponManager.Melee.RunToEnemyUpdate();`) → `RunToEnemyUpdate`
  (faces the body part, sprints, every `TRY_HIT_PERIOD_FALSE=0.1s` calls
  `TryHit` — BotMeleeWeaponData.cs:276-290: `MeleeWeaponEquipped &&
  enemy.IsVisible && Time.time-PersonalLastSeenTime<0.2 &&
  KnifeController!=null` → `KnifeController.MakeKnifeKick()` — then path
  handling via `CanRunToEnemyToHit`/`GoToByWay`/`StopMove`).
  `MakeKnifeKick` on the simplified skeleton sets the animator triggers and
  the knife-collider hit window comes from the zombie animation events
  (Player.cs:18590-18610,17776-17777).

**The fix (`ZombieMeleePatch`, `ArenaMode.Client/Bots/ZombieMeleePatch.cs`).**
Does not patch SAIN (never touch a competing mod — AGENTS.md licence
rules) and does not try to out-race SAIN's own prefix by patch-registration
ORDER (rejected by design: fragile across Harmony/BepInEx load order). A
new prefix on `OneMeleeAttackNode.UpdateNodeByBrain` itself — one call site
entirely outside `BotMeleeWeaponData`, so SAIN's patch on the melee method
is never even reached — reproduces that node's own two-line body, but
drives the TRUE native `RunToEnemyUpdate` body through a HarmonyX reverse
patch (`Harmony.CreateReversePatcher`, confirmed present via Mono.Cecil
metadata inspection of the installed `0Harmony.dll`, version `2.9.0.0`,
which copies the original method's IL into a brand-new, patch-chain-free
`MethodInfo` — SAIN's prefix is never consulted, because nothing calls
through the original PATCHED method at all). A hand-written, line-cited
fallback reimplementation
(`ZombieMeleePatch.FallbackRunToEnemyUpdate`) covers the case where the
reverse patch cannot be prepared at runtime; either path is logged once,
and if even the fallback is unreachable the prefix stays inert (returns
true — today's vanilla/SAIN-suppressed behaviour) rather than half-driving
the node. The gating decision (exact Zombie raid + infected role + the bot
can actually melee right now) is `ZombieMeleeGate.ShouldDrive`, a pure
function with no EFT/Unity types, tested the same way as
`ZombieWavePolicy`/`ZombieRoleMix`/`ZombieProfilePlan`.

Cheap diagnostics (`ZombieMeleeDiagnostics`) log the first 3 native swings
and first 3 landed hits per raid at Info from
`ZombieSpawnService.OnZombieArrived`'s own per-bot subscription, and
`ZombieMode.OnMatchEnd` reports the raid's totals in one line.

**What the NEXT GPU raid must show** to confirm the fix: the player's
health actually drops while a zombie is in melee range; a zombie death from
melee appears in the killfeed/score as an ordinary `ZOMBIE` kill; no new
Error/Exception log lines; and the Info line from `LogPathChoiceOnce`
('...driving infected RunToEnemyUpdate through a HarmonyX reverse patch...'
or '...through the hand-written fallback...') confirms which path actually
ran, plus the per-raid swing/hit counts from `ZombieMeleeDiagnostics` and
`ZombieMode`'s own end-of-raid summary line.

## Double-Activate

The same GPU raid's log showed `ZombieNativeEventBridge` log
"Activate() fired again before the previous raid's flag was restored —
leaving the saved original value alone" 77ms after its own first "armed"
line, in the SAME raid (`BotsEventsController.Activate` apparently ran
twice during one raid's loading). Decompile tracing of every link in the
native chain that reaches it —
`TarkovApplication.LocalGameMatching` → `LocalGameCreate` →
`LocalGame.Run` → `BaseLocalGame.vmethod_1` → `BotsController.Init` →
`BotsEventsController.Activate` — found exactly ONE call site at every
single link inside `Assembly-CSharp`. That rules out a native, vanilla
double-call for an ordinary raid start; the repeat happens OUTSIDE that
chain, most plausibly an SPT-side or another installed mod's own Harmony
patch re-running part of raid loading. The exact caller is not pinned down
in this pass; a stack trace captured the next time the Info line below
fires would settle it, but that is left for whoever next needs the answer
rather than chased further here.

What the evidence DOES establish is that this exact repeat is harmless: the
77ms gap is far too short for a whole new raid (new scene, new `GameWorld`)
to have loaded, so it is the SAME raid's own `GameWorld` reactivating — and
that is exactly the identity check `ActivatePatch` now uses (see
`ZombieNativeEventBridge._savedFlagGameWorld`'s own doc comment) to log Info
instead of Warning for a same-`GameWorld` repeat, while a genuinely
different, earlier raid's stuck flag still logs the original Warning. The
restore semantics themselves are completely unchanged: `_savedFlag` is
still saved exactly once per unrestored window, and `RestoreFlag` still
clears it (and the new `_savedFlagGameWorld`) together, from whichever of
`Dispose()`/`ArenaSession.OnRaidEnded`/the next raid's own `Activate()`
reaches it first.

## v1 (2026-10-06 evening): progress-based watchdog, continuous waves, refill, own gear

Driven by the user's own post-playtest feedback on v0 ("funktioniert gut") — four
requests, implemented as four stages on `feature/zombie-prototype` plus one
same-evening review-fix pass. Commits, oldest first:
`452d87a` `7588923` `7bdd868` `66fb9aa` `a54201a` `40503d1` `3a16528` `a6879df`
`1203438` `3c0a528` `c088b5d`.

### 1. Watchdog: progress, not duration

**Problem (found in the first GPU raid, see "Native melee..." above).**
`StuckAliveSince` was a pure wall-clock timer from the moment the tail state
("everything of the wave spawned, at least one still alive") began, never reset
by a kill in between — so a wave the player was steadily clearing one infected at
a time still got force-cleared once total tail duration crossed
`StuckWaveTimeoutSeconds`, exactly as raids 1 and 2 showed.

**New semantics.** "Stuck" now means "fully spawned AND no infected has died for
`StuckWaveTimeoutSeconds`" — matching the setting's own name. The clock restarts
on every infected death, not merely once at tail entry. The setting itself is
unchanged: name, 100 s default, 0-600 s range; only its description in
`Settings.cs` was reworded to document the progress-based semantics.

**How a death is detected (and why not through the kill/scoring pipeline).**
`ZombieSpawnService.ConfirmedArrivals` (`Bots/ZombieSpawnService.cs`) is a new
monotonic lifetime counter, incremented in `OnZombieArrived` immediately after
the bot-alive check — deliberately before the player-alive early return, so it
can never undercount relative to `CountAliveInfected`'s own definition during
Zombie's instant-respawn window. `ZombieWavePolicy.Evaluate` (`Modes/
ZombieWavePolicy.cs`) gained a sixth parameter, `int arrivedTotal = 0` (defaulted
so every pre-existing non-watchdog call/test is unaffected), nets it against the
previous call's baseline (`State.LastArrivedTotal`) to get
`arrivedSinceLastCall`, and only then asks whether `alive` fell short of what new
arrivals alone would explain — a raw "did alive drop" check is not enough by
itself, because a request dispatched before the tail began can resolve into a
confirmed arrival during the tail (alive rising for a reason unrelated to
dying), and a death and a late arrival can land in the same call and net to zero
visible change. Every self-recursive call inside `Evaluate` forwards the SAME
`arrivedTotal` it received rather than letting it default, so a synthetic
re-entrant call can never corrupt the real baseline. `ZombieMode.Tick` reads
`ZombieSpawnService.ConfirmedArrivals` each wave-evaluation tick and passes it
through.

Two designs were weighed for the death signal itself (see `ZombieWavePolicy`'s
own doc comment, "How a death is detected, and why not the obvious way"): wiring
it through the existing kill-capture/scoring pipeline (`ZombieMode.OnKill`), or
deriving it purely from alive/arrived counts inside the pure policy layer.
Chosen: the latter — `ArenaKill` is only raised when EFT names a credited
aggressor, and an infected that dies with none identified (environmental damage,
a fall) is routed to `ArenaUncreditedDeathPatch`, which explicitly skips any
non-local-player victim, so that class of death would silently fail to reset the
watchdog if wired through `OnKill`. Staying inside the pure layer also keeps the
watchdog independent of any future change to scoring/kill-feed filtering.

**Fault control.** Reverted the tail-branch re-arm condition from
`if (NaN || anyDeathSinceLastCall)` back to the old `if (NaN)`-only check;
`TheStuckWaveWatchdogDoesNotFireWhileInfectedKeepDying`,
`TheStuckWaveWatchdogStillFiresWhenDeathsActuallyStop` and
`AMaskedDeathCoincidingWithALateArrivalStillResetsTheStuckTimer` all failed
(red) with the exact expected symptom — the watchdog firing early because a
death never restarted the clock; restored, full suite green again.

**Review fix, same evening (`c088b5d`).** The bigger-wave defaults below made a
watchdog-cleared tail able to hold the full `MaxAlive = 20` at once (vs. at most
`WaveStartSize = 8` under the old defaults), so one firing of
`RetireAllAliveInfected` could burst up to 20 per-bot `LogWarning` lines in a
single frame. Changed to one `LogInfo` summary line
("administratively retired N infected…") after the loop; the per-bot
`LogWarning` stays, but only on the genuinely exceptional failure-to-retire
path. Log-shape only, no behaviour change.

**Saturated waves arm the watchdog too (2026-10-06, same day).** The
progress fix above still only ARMED once `remainingToSpawn == 0` — the wave
fully spawned. A GPU raid the same day (18:04-18:10, wave 1 of 30 after
`7588923` raised `WaveStartSize` to 30 with `MaxAlive` unchanged at 20) hit
exactly the gap that leaves open: 20 infected alive, 10 still to spawn, a
player who could not land a kill, no infected death for over four minutes —
and the watchdog never fired at all, because the wave was never "fully
spawned". In v0 the same kind of stall resolved after 100s, only because
`WaveStartSize` (8) sat below `MaxAlive`, so every wave reached the
fully-spawned tail while still comfortably under the cap; the bigger-wave
change below is what exposed this. The real failure condition was never
"fully spawned", it is SATURATED: no spawn is possible right now, either
because everything has already been requested, or because `alive + pending`
already fills the policy's own target
(`min(MaxAlive, remainingToSpawn + alive)`, the same formula the ordinary
continuous-refill spawn decision already used) — both are the same failure
from the watchdog's point of view, since only an infected death can free the
room for the next spawn. `ZombieWavePolicy.Evaluate` now arms/re-arms on
saturation, using the exact same progress-not-duration clock as before. A
firing while `remainingToSpawn > 0` clears only the alive infected and stays
in `Phase.Wave` — the wave is not over, only unstuck — so the remainder
resumes spawning in the very same call, exactly as an ordinary death would
have let it; only a firing at the fully-spawned tail still ends the wave as
before. A wave still filling up (`alive + pending` below target) is never
saturated and must never arm this.

**Fault control.** Three new tests, each with an injected fault that turns
it red before the corresponding fix and green after it is restored:
(a) `TheStuckWaveWatchdogArmsWhenTheWaveIsSaturatedEvenWithSpawnsStillLeft`
reproduces the GPU raid directly (30-wave, `MaxAlive` 20, 20 alive/10 left,
zero deaths) — reverting the arming condition from `saturated` back to the
old `remainingToSpawn == 0` alone made it fail (the 90s ceiling crossed with
`ForceRetireStuckInfected` never turning true). (b)
`TheStuckWaveWatchdogKeepsResettingWhileSaturatedAndInfectedKeepDying` reuses
the masked-death pattern (one infected dies the same call a different one
arrives, so `alive` never visibly dips) but for the mid-wave/capped case —
reverting the re-arm condition from `NaN || anyDeathSinceLastCall` back to
`NaN`-only made it fail inside the loop, because nothing else resets the
clock while the wave stays pinned at the cap. (c)
`AWaveStillFillingUpDoesNotArmTheWatchdogEvenForALongTime` sits at 5/30
alive (`MaxAlive` 20) for 1000s with no deaths and confirms the watchdog
never arms and the wave keeps trying to spawn — the boundary the `saturated`
condition must respect.

### 2. Continuous, much bigger waves

**New defaults (`Settings.cs`, section `"Zombie"`):**

| Setting | Old | New | Range |
|---|---|---|---|
| `WaveStartSize` | 8 | **30** | 1-200 (was 1-100) |
| `WaveGrowth` | 4 | **10** | 0-100 (was 0-50) |
| `SpawnIntervalSeconds` | 1.0 | **0.75** | 0.2-10 (unchanged) |
| `MaxAlive` | 20 | 20 (unchanged, as decided) | 4-56 (unchanged) |

`ZombieWavePolicy.Evaluate`'s continuous-refill math already implemented "many
more zombies, not necessarily at once, but without pause, bounded by `MaxAlive`"
with no formula change needed: `target = min(MaxAlive, remainingToSpawn +
alive)`; one spawn request goes out every `SpawnIntervalSeconds` while
`alive + pending < target`. Raising the defaults alone was enough to deliver
item 2 of the request.

**The one real throttle risk a bigger wave 1 exposes.**
`ZombieProfilePlan.BackupTargets` was registered with the native backup loader
(`BotProfileBackuploader`) at a flat `MinimumBackupCount = 4` regardless of wave
size. With `infectedAssault` weighted 50% of the role mix, a 30-strong wave 1
draws roughly 15 of that role alone against a cache of 4; `BotProfileBackuploader`
only tops up once its deficit exceeds 3, one outstanding fetch at a time
(decompile-verified), so the cache would empty almost immediately into
`BotProfileClient.CreateProfile`'s synchronous 3-profile-at-a-time miss path —
visibly stalling spawns even though `ZombieWavePolicy` itself never pauses.
`ZombieProfilePlan.BackupCountForWaveStartSize(int waveStartSize)` scales the
per-(role, difficulty) backup target by the heaviest role weight's share of
`waveStartSize`, floored at `MinimumBackupCount` and ceilinged at
`MaxBackupCountPerPair` (a new `const int = 100`, i.e. `Core.LOCAL_BOTS_COUNT`,
the same native per-request cap the opening preload already respects by
construction). `ZombieMode.OnMatchStart` now calls
`ZombieProfilePlan.BackupTargets(_shooterPercent,
ZombieProfilePlan.BackupCountForWaveStartSize(_config.WaveStartSize))` instead
of the old implicit-default call.

**Opening preload.** `ZombieNativeEventBridge`'s `GetProfilesOnStart` hook
already covers at least `MaxAlive` for wave 1 by construction — it sizes to the
full `WaveStartSize`, and the new `30 > 20` holds with room to spare (the old
`8 < 20` did not need to, and now does not need to either).

**Tests added:** `WaveSize`'s arithmetic at the new defaults; a 30-zombie wave
at `MaxAlive = 20` ramping to exactly 20 alive and then refilling one per
`SpawnIntervalSeconds` as each kill opens a slot, continuously, until all 30
have gone out; `BackupCountForWaveStartSize`'s scaling, floor, ceiling and
monotonicity.

### 3. Refill between waves (ammo, meds, grenades)

Implemented as two layers, per `AGENTS.md`'s pattern elsewhere in this mode:

- **`ZombieRefillPolicy`** (`Modes/ZombieRefillPolicy.cs`) — pure, EFT/Unity-free
  WHEN layer, linked into `ArenaMode.Tests`. Fires exactly once per
  `Wave`→`Intermission` transition (edge-detected from the `Phase` the caller
  already reads every `WaveEvaluateIntervalSeconds`), only while the caller
  reports the player eligible (alive, not mid-death/respawn). A transiently
  ineligible player is not a permanent skip — Zombie's instant-respawn pipeline
  can still be finishing exactly when the wave's last infected dies (~0.2 s on
  the GPU host, per the GPU results document) — so the same intermission keeps
  asking on later calls, bounded by a retry budget (`RefillMaxAttempts = 20` in
  `ZombieMode`, a private const, at the existing 0.1 s evaluate cadence ≈ 2 s of
  real-attempt headroom) that only real attempts (a reported "did not start")
  consume; an ineligible tick costs nothing. `ReportStarted` resolves the
  intermission for good, mirroring `ZombieWavePolicy`'s own optimistic-commit
  convention (dispatched == resolved).
- **`ZombieLoadoutRefill`** (`Modes/ZombieLoadoutRefill.cs`) — the thin EFT-facing
  adapter. Writes **no new inventory-mutation code**: it re-applies the
  player's loadout through the SAME live-body respawn path Gun Game/Tactical
  already use between rounds.
  - **Random-kit mode:** `TryRefill` re-applies the player's CURRENT
    `KitDefinition` through `Respawn.LiveKitRespawn.AtRoundBreak` — the exact
    path `TacticalMode.StartRound` already uses between rounds. Zombie sets no
    `KitCatalog.KitProvider`, so `AtRoundBreak`'s internal `GetSelectedKit()`
    would otherwise hand back a fresh random draw; `ZombieMode` tracks "the kit
    currently applied" via the existing `ArenaEvents.OnKitApplied` event
    (subscribed in `OnMatchStart`, unsubscribed in `OnMatchEnd`) and
    `ZombieLoadoutRefill` installs that kit as a one-shot `KitProvider` for the
    exact width of `AtRoundBreak`'s single synchronous kit-capture call, then
    restores whatever was there before. A later ordinary mid-wave death respawn
    is unaffected and still draws a fresh random kit exactly as v0 does.
  - **Own-gear mode (item 4):** `TryRefillOwnGear` is `TryRefill`'s sibling —
    restores `ZombieOwnGearLoadout`'s captured entry loadout through a new
    `LiveKitRespawn.AtRoundBreakOwnGear` (which needed `AtRoundBreak`'s shared
    `SwapRoutine` machinery to grow an optional `equipmentOverride` delegate,
    since the kit-only `AtRoundBreak` hard-refuses when
    `KitCatalog.GetSelectedKit()` returns null and had no parameter for
    substituting a `TeamArenaLoadoutSnapshot`-style restore).
  - Both log exactly one Info line per successful refill, only from the
    `onComplete` callback once the whole swap is confirmed (not merely the one
    `EquipmentStrategy` step inside it):
    `"Zombie refill after wave {n}: kit '{name}' re-applied."` (random-kit) or
    `"Zombie refill after wave {n}: own-gear entry loadout re-applied (epoch {N})."`
    (own-gear). `TryRefill`/`TryRefillOwnGear` never throw into `Tick` — caught,
    logged once via `LogWarning`, reported as "did not start" so the policy's
    retry budget handles it.
  - `ZombieMode.Tick` computes `playerEligible` from `HealthController.IsAlive`
    plus `!ArenaRespawnDriver.{IsRespawnInProgress, StandDown}` and branches the
    refill call on the frozen `_ownGear` field; `ZombieRefillPolicy` itself (the
    WHEN) needed no change between the two modes.

**Accepted superset, documented, not fixed.** Re-applying a kit (or the entry
loadout) through `LiveKitRespawn.AtRoundBreak`/`AtRoundBreakOwnGear` also
restores armor/weapon **durability** and performs `AtRoundBreak`'s own short
`NearPositionSpawnSelector` relocation — both are accepted supersets of "just
refill ammo/consumables" inherited from reusing this already-tested path as-is,
rather than writing a bespoke in-place inventory edit. `ZombieLoadoutRefill`'s
own class doc comment argues this explicitly.

**Doc-comment correction (`3c0a528`, review).** The class doc comment had
claimed a failed restore is "a fatal, raid-ending abort" for both callers of the
underlying `TeamArenaLoadoutSnapshot` primitive. True for
`ArenaRespawnDriver.RespawnOwnGearPlayer` (a death respawn —
`AbortOwnGearRespawn` sets `StandDown` and schedules an abort), but **not** for
`ZombieLoadoutRefill.TryRefillOwnGear`: the `InvalidOperationException` it
throws from inside the `EquipmentStrategy` closure on a failed `TryRestore` is
caught by `RespawnEngine.RunRespawn`'s own failure callback and handled by
`LiveKitRespawn.SwapRoutine`'s ordinary non-fatal recovery (`RecoverOldBody` +
reporting "did not start") — that one resupply is skipped and the raid
continues on the player's current body. The comment now states the asymmetry
explicitly: a poisoned snapshot keeps silently failing every later
intermission's refill through the non-fatal path until the player's next real
death hits the same poisoned snapshot through the OTHER caller, which then IS
fatal.

**Fault controls (`7bdd868`):** dropping `playerEligible` from
`ShouldAttempt`'s expression failed `RetriesOnLaterTicksWhilePlayerIsIneligible
ThenFiresOnceEligible` and `TheRefillPolicyNeverFiresWhileThePlayerIsIneligible`
exactly as expected; making `ReportStarted` a no-op failed
`FiresExactlyOnceOnEnteringIntermissionWhilePlayerIsEligible` and
`TheRefillPolicyDoesNotRefireAfterAlreadyStartedThisIntermission` exactly as
expected. Both restored, full suite green again.

### 4. Own gear: bring your own protected equipment

As an alternative to random Arena kits, the player can keep their own
equipment, protected exactly the way Tactical's protected PitFireTeam path
protects a PMC's entry loadout — reusing that mechanism rather than writing new
profile-protection code.

- **Setting.** `Settings.ZombieOwnGear` (bool, default `false`, section
  `"Zombie"`) — a new F12 toggle.
- **Gate.** `ZombieOwnGearGate.IsSelectedFor(mode, ownGearSelection)`
  (`Modes/ZombieOwnGearGate.cs`) is the one pure place comparing the frozen F12
  value against `ArenaPopulationPolicy.Zombie` (`OrdinalIgnoreCase`, matching
  `KitPreloadPlan`'s own mode-comparison convention) — no EFT/Unity types, so it
  links directly into `ArenaMode.Tests` like `ZombieWavePolicy`/`ZombieRoleMix`/
  `ZombieMeleeGate`. Used identically for the loading-screen check
  (`ZombieOwnGearArmed`, against `ArmedMode`) and the live-raid check
  (`ZombieOwnGearActive`, against `ActiveMode`), so the two can never disagree.
- **Frozen at arm.** `ArenaSession.ZombieOwnGearSelection` freezes
  `Settings.ZombieOwnGear.Value` the exact moment `GunGameLadderSelection`
  already freezes its own F12 setting (`MarkArmed`) — a mid-loading F12 edit
  must not desync the loading-screen preload from what the live raid actually
  does. Cleared alongside `GunGameLadderSelection` on every
  arming-cleared/raid-rejected/raid-ended path.
- **Entry-loadout snapshot.** `ZombieOwnGearLoadout` (`Kits/
  ZombieOwnGearLoadout.cs`) wraps the EXISTING
  `TeamArenaLoadoutSnapshot` capture/arm/prepare/commit primitive Tactical's
  protected-team path already uses and tests — one snapshot per raid, one
  strictly increasing epoch per restore. Unlike Tactical's multi-frame
  round-transition staging (needed because `PitFireTeam`'s round-transition can
  hold the respawn pipeline open across several frames while four teammates
  reassemble), every Zombie restore arms, prepares AND commits in one
  synchronous call, because Zombie has no such hold (`HoldRespawn = false`, no
  round-transition state machine) — the dead/old body's `Profile` is still
  exactly the one `TryCaptureEntryLoadout` saw at the point `RespawnEngine`
  invokes `EquipmentStrategy` (confirmed against the decompiled
  `RespawnEngine.RunRespawn`'s own call ordering). A failed prepare or commit
  permanently poisons the snapshot's restore gate by design (same contract
  Tactical already lives with); both later callers treat that as fatal for a
  death respawn (see the doc-comment correction above for the refill's own,
  non-fatal, exception).
- **Raid start.** `ArenaSession.OnRaidStarted`'s kit-swap block gets an
  own-gear branch alongside the existing PitFireTeam one: captures the entry
  loadout (`ZombieOwnGearLoadout.TryCaptureEntryLoadout`) instead of calling
  `LiveKitRespawn.AtRaidStart`, and schedules a failed-start abort if the
  capture itself fails, mirroring how a rejected raid-start token is already
  handled at this same call site — never silently falling through to a random
  kit.
- **Loading-screen preload skipped.** `KitBundlePreloader.BeginRaidStart` gets
  an early return shaped like its existing PitFireTeam/
  `CompatibilityTeamArenaActive` check, gated on `ZombieOwnGearArmed` (the
  loading-time exception class `AGENTS.md` already documents for this file) —
  a whole-catalogue preload plan would otherwise still warm pools for a raid
  that hands out no kit at all. (Deliberately NOT routed through
  `KitPreloadPlan.OpeningKit`'s existing `protectedTeamRaid` parameter alone,
  because that would still let a whole-catalogue-scope plan warm the rest of
  the catalogue — the same reason the production PitFireTeam check already
  returns early instead of threading through that parameter.)
- **Secure container.** `ArenaSecureContainer.LeavesContentsAtHome` gets a third
  condition alongside `EmptySecureContainer` and `!HasFrozenTeamRoster`: own
  gear has no medical ladder to outclass and emptying the container would
  destroy the player's own real supplies, the same way it already would for a
  frozen PitFireTeam roster.
- **Death respawn.** `ArenaRespawnDriver.RespawnRoutine` gets a new branch,
  checked FIRST — before the frozen-PitFireTeam branch and unconditionally
  ahead of the ordinary `KitCatalog.GetSelectedKit()` call — so a random-kit
  respawn can never reach a player who asked to keep their own gear. The new
  `RespawnOwnGearPlayer` coroutine mirrors `RespawnProtectedTeamPlayer`'s shape
  (same spawn protection, same failure handling via a new
  `AbortOwnGearRespawn`).
- **Wave-intermission refill.** Covered in section 3 above
  (`TryRefillOwnGear`/`AtRoundBreakOwnGear`).
- **Menu exposure (superseded 2026-10-06, item 1 of the follow-up — "own gear
  to the left").** The UI described below shipped in v1 and has since moved;
  kept here for history, see the paragraph after it for the current location.
  ~~Mirrors Tactical's equivalent the way the task asked: Tactical has no
  literal toggle for its own-gear path (it is automatic, driven by a complete
  PitFireTeam roster), so this reuses the SAME mutator-panel mechanism that
  already mode-gates other per-mode options (Pickups to Deathmatch/Team DM,
  Lasers to Team DM/Tactical 5v5) rather than inventing new UI: a new
  `ArenaMutator.OwnGear` row ("OWN GEAR" / "Bring your own protected
  equipment.", unavailable outside Zombie: "Available in Zombies.").~~ No
  `ArenaMutatorsView` code change was needed at the time — it already builds
  one row per `Enum.GetValues(typeof(ArenaMutator))` entry and lays out two
  columns of `(count + 1) / 2` rows; 15→16 mutators kept `rowsPerColumn` at 8
  either way (verified by arithmetic, not confirmed in-game).
  ~~`ArenaModeSelectionView.Refresh`'s EQUIPMENT fact text gets the same
  special case its `TacticalOnly` branch already has ("PMC entry gear"): the
  ZOMBIES row shows "Own gear" instead of the catalog's default "Arena kits"
  once `Settings.ZombieOwnGear` is on.~~

  **Current location (2026-10-06 follow-up, item 1 — "own gear to the
  left").** The task asked for this to move into the LEFT mode panel as an
  equipment choice that will later exist for most modes, not stay a Zombie-
  only mutator-panel row. `ArenaMutator.OwnGear` is REMOVED (back to 15
  mutators, `ArenaMutatorsView` untouched again). The row is now
  `ArenaEquipmentChoiceView`/`ArenaEquipmentChoiceState`
  (`ArenaMode.Client/Session/`), driven by a pure per-mode availability table,
  `Modes.ArenaEquipmentChoice` (`ArenaMode.Client/Modes/ArenaEquipmentChoice.cs`
  — dependency-free like `ZombieOwnGearGate`, links into `ArenaMode.Tests`):
  `IsOfferedFor(mode)` is true only for Zombie today, so Deathmatch/Gun Game
  later is one more comparison, not new UI. It sits in the EQUIPMENT fact's
  own panel slot (top=434) and replaces that fact — never alongside it — for
  any mode the table offers a choice to; every other mode (including
  Tactical, whose own protected-team own-gear semantics stay completely
  untouched) keeps the plain fact, the same HIDE-when-inapplicable convention
  the TDM/Deathmatch rules rows already use (not the mutators panel's
  "grey out + N/A" convention). The write path moved from
  `ArenaMutatorSelection.TryToggle` to `Settings.TrySelectEquipmentChoice`
  (mode-dispatched like `TrySelectKillcam`/`ReplayEntry`), but still hits the
  exact same `Settings.ZombieOwnGear` ConfigEntry, the exact same
  `ArenaSession.IsActive||ArmedForNextRaid` lock, and the exact same
  `File.Save()`-when-`SaveOnConfigSet`-is-false fallback the mutator-panel
  toggle had — `ArenaSession.ZombieOwnGearSelection`'s frozen-at-arm capture in
  `MarkArmed` and its six reset sites are unchanged, because they read that
  same ConfigEntry, not the new UI. `ArenaModeSelectionView.Refresh`'s
  EQUIPMENT fact text now reads `Modes.ArenaEquipmentChoice.IsOfferedFor` plus
  `Settings.GetEquipmentChoiceOwnGear(mode)` instead of a Zombie-only literal,
  so it stays correct for a future mode automatically. Pure state tests:
  `ArenaEquipmentChoiceTests.cs` (fault-controlled: `IsOfferedFor` forced to
  `true` for every mode, and the state's `!_tacticalOnly` guard dropped — both
  confirmed red, both reverted and confirmed green). `tools/TdmMatchMenuChecks`
  gained inline production-linked coverage of the real row (hidden for every
  mode but Zombie, real control persists `Settings.ZombieOwnGear`, frozen
  during arm, EQUIPMENT fact text stays in sync even while hidden).

**Fault control (`a54201a`):** temporarily changed `ZombieOwnGearGate`'s
`StringComparison.OrdinalIgnoreCase` to `StringComparison.Ordinal`;
`TheComparisonIsCaseAndWhitespaceInsensitive` failed exactly as expected
(an uppercase `"ZOMBIE"` mode id stopped matching); reverted, full suite green.

### Review fix: the stale cached local-player reference (`1203438`)

**Bug (`confirmed` by the orchestrator's own review pass after stages A-C).**
`ZombieMode._local` was refreshed only by the real death-respawn event
(`OnLocalRespawn`, raised from `ArenaRespawnDriver`'s three death call sites) —
but the raid-start kit swap AND the wave-intermission refill's own live-body
swap (`LiveKitRespawn.AtRaidStart`/`AtRoundBreak`/`AtRoundBreakOwnGear`, all
through the shared `SwapRoutine`) replace the live body too and destroy the old
one (`ArenaRetiredBodyEffects.TryDestroy`) WITHOUT ever raising that event.
`EFT.Player : MonoBehaviour`, so Unity's overridden `==`/`!=` makes
`_local != null` read false forever for the destroyed body on the very next
tick — silently, no log line, since `ZombieRefillPolicy.Evaluate` simply stops
returning `ShouldAttempt = true`. In random-kit mode this broke the refill from
before wave 1 even started (`OnMatchStart` captures the pre-swap body strictly
before `ArenaSession`'s own `AtRaidStart` call destroys it). In own-gear mode
wave 1's refill fired correctly, but that very refill's own
`AtRoundBreakOwnGear` call then invalidated `_local` for every later
intermission. Net effect either way: at most one resupply for the whole raid
instead of one per cleared wave — exactly the "zwischen den Wellen" behaviour
the user asked for, silently only half-delivered.

**Fix.** Mirrors `TeamDeathmatchMode.Tick`'s already-established pattern for
this exact class of staleness: poll `_world.MainPlayer` and heal `_local` (via
the existing `OnLocalRespawn`) when it has changed, guarded on
`!ArenaRespawnDriver.IsRespawnInProgress` so the heal only fires once a
replacement body is fully wired, never mid-swap (that flag spans the whole
`LiveKitRespawn.SwapRoutine` pipeline too, via the shared
`BeginPipelineUse`/`EndPipelineUse`). Placed right before computing
`playerEligible`, inside the existing 0.1 s wave-evaluate throttle.

No new unit test: the bug is Unity-specific (`MonoBehaviour`'s overridden
null-equality on a destroyed object), which a desktop-CLR xunit test cannot
reproduce — the same "client-side, verification is still a raid" limitation
this document's own "Known test-coverage gap" note and `AGENTS.md` already
document for `ZombieMode.Tick`'s EFT-facing glue. **The next GPU raid must show
a refill firing after EVERY cleared wave, not just the first.**

### Test totals after v1

Offline (`dotnet test ArenaMode.Tests`, no-deploy sentinel confirmed absent
before and after): **852 passed, 2 skipped (pre-existing, environment-gated,
unrelated to Zombie), 0 failed, 854 total** — re-confirmed for this document.
After the saturated-wave watchdog fix (`39e193d`/`acd57ff`, three new
`ZombieWavePolicyTests`): 857 total, 855 passed, 2 skipped.
Clean Release builds of `ArenaMode.Client` and `ArenaMode.Server` with the same
no-deploy properties: 0 errors. `ZombieWavePolicyTests.cs`,
`ZombieRefillPolicyTests.cs`, `ZombieOwnGearGateTests.cs` and
`ZombieProfilePlanTests.cs` carry the new coverage; all remain pure/
production-linked with no EFT/Unity dependency.

### GPU playtest checklist for v1 (played 2026-10-06 evening)

v1 was played on SPT-GPU-2 on 2026-10-06 evening in three raids: A (`cf100ab`,
random kit, still the old wave values 8/4/1), B (`cf100ab`, OwnGear, 30/+10/0.75)
and C (`acd57ff`, OwnGear, after the saturated-wave watchdog fix). Details:
`GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`, section "v1 (06.10.2026 abends)".
Per `AGENTS.md`, "for anything client-side, verification is still a raid", so
the status of each item below is what a raid showed, not what the offline tests
say. The checklist, in order, with its status:

1. **Watchdog stays quiet while the player is actively killing.** Fight a wave
   down through kills at a pace slower than `StuckWaveTimeoutSeconds` between
   individual kills; the watchdog must NOT force-clear it. Then stop killing
   entirely for `StuckWaveTimeoutSeconds` with infected still alive and confirm
   it still fires (the watchdog is not disabled, only re-armed by progress).
   *Confirmed 2026-10-06 (raid A): four kills over more than 100 s drew no
   warning; it fired 18:03:06, 100 s after the last kill.*
2. **A wave actually ends through kills**, not the watchdog — the first time
   this mode has shown that live (both GPU-2 raids on 2026-10-06 had every wave
   end via the watchdog instead).
   *Not confirmed 2026-10-06: every wave that ended in raids A and C ended
   through the watchdog, raid B had no kills.*
3. **A 30-zombie wave 1 keeps feeding at `MaxAlive = 20`** with no visible
   stall: alive count ramps to 20 quickly and a kill is followed by a fresh
   spawn within about `SpawnIntervalSeconds`, not a multi-second
   `BotProfileClient` synchronous-miss pause. No `ArgumentOutOfRangeException`
   or profile-starvation warning.
   *Partly confirmed 2026-10-06 (raids B, C): 20 alive in about 15 s, no Error
   lines; a kill followed by a fresh spawn was not seen (raid B had no kills,
   raid C's last 10 spawned only after the watchdog retired the 20).*
4. **The intermission refill restores magazines/meds/grenades after wave 1**,
   both modes: (a) random-kit — consume some ammo/meds/grenades during wave 1,
   survive to the intermission, confirm the log line `"Zombie refill after
   wave 1: kit '...' re-applied."` and that the inventory is topped up (and
   that durability/position also changed, per the accepted superset); (b)
   repeat for wave 2's intermission and confirm the SAME refill fires again
   (this is exactly what `1203438` fixes — the prior build would have silently
   stopped refilling after wave 1).
   *Partly confirmed 2026-10-06: (a) after wave 1 in random-kit mode (raid A:
   magazine 17/30 -> 30/30 +1, health and hydration full, kills kept, no death
   counted, but the player was moved about 81 m) and in own-gear mode (raid C,
   epoch 1); (b) a refill after wave 2 was NOT seen.*
5. **Own gear end to end.** Arm Zombie with `OwnGear` on (F12 or the new OWN
   GEAR mutator row); confirm the map screen's EQUIPMENT fact shows "Own gear";
   start a raid and confirm the BepInEx log shows the raid-start kit swap was
   skipped and the secure container was NOT emptied; die once and confirm the
   respawn restores the real loadout (log: "Arena Zombie own-gear respawn
   completed (epoch 1)…"); survive to an intermission and confirm the log line
   `"Zombie refill after wave N: own-gear entry loadout re-applied (epoch
   N)."` with ammo/meds/grenades topped up; end the raid and confirm the
   profile afterward is identical to before arming (the sacred-profile
   guarantee, same as every other mode).
   *Partly confirmed 2026-10-06 (raids B, C): preload, kit swap and container
   emptying skipped, four own-gear respawns in 0.07-0.19 s with identical
   equipment, refill "re-applied (epoch 1)", profile identical before/after
   raid B; only a bayonet/empty-Gamma loadout was tested (so no ammo/meds/
   grenades top-up), the map screen's "Own gear" fact was not recorded.*
6. **Regression check.** A normal (OwnGear off) Zombie raid and an ordinary
   non-Zombie raid are both completely unaffected by all of v1.
   *Partly confirmed 2026-10-06: raid A is an OwnGear-off Zombie raid and the
   flag read `false` after every raid; no ordinary non-Zombie control raid was
   run after v1.*

Also still open from the v0 checklist and not yet retried: performance/corpses
at `MaxAlive` 40-56, a deliberately stranded infected for the watchdog itself,
`ShooterPercent > 0` (ranged infected), the second `Activate()`'s caller, Labs/
Labyrinth, the boss zombie, PitFireTeam.

### 2026-10-06 follow-up, item 1 ("own gear to the left") — Stage A

After playing v1, the user asked for the OWN GEAR switch to move out of the
right-hand mutators panel into the LEFT mode panel as a mode-independent
equipment choice (later offered to most modes; Tactical's own automatic
protected-team own-gear path is untouched). See the "Menu exposure" bullet
above (now superseded) for the full before/after and the new abstraction
(`Modes.ArenaEquipmentChoice`, `Session.ArenaEquipmentChoiceState/View`).
Announcer-per-mode (item 2 of the same follow-up) and the 56-zombie
performance measurement (item 3) are separate work, not part of this stage.

Offline (`dotnet test ArenaMode.Tests`, no-deploy sentinel confirmed absent
before and after): **864 passed, 2 skipped (pre-existing, environment-gated,
unrelated), 0 failed, 866 total** (+9 over v1's 857, all in the new
`ArenaEquipmentChoiceTests.cs`). Clean Release builds of `ArenaMode.Client`
and `ArenaMode.Server` with the same no-deploy properties: 0 errors (new
`CS8618` nullability warning on `ArenaEquipmentChoiceState`'s constructor-set
`_mode` field, pre-existing warning class, not a new error).

Fault control (pure state tests, inject/red/restore/green):

- `ArenaEquipmentChoice.IsOfferedFor` forced to return `true` unconditionally:
  `OnlyZombieOffersAChoiceToday`, `AMissingModeIsNeverOffered` and
  `ChoiceIsOnlyVisibleForZombie` failed exactly as expected (3 red); reverted,
  full suite green (866 total).
- `ArenaEquipmentChoiceState.Visible`'s `!_tacticalOnly` guard dropped:
  `ProtectedTeamNeverShowsTheRowEvenIfATableEntryExistedForIt` failed exactly
  as expected (1 red); reverted, full suite green.

`tools/TdmMatchMenuChecks` (the harness that exercises the real
`ArenaModeSelectionView`/`ArenaMutatorsView` against production Settings)
needed both: (a) new inline coverage for the real equipment-choice row
(hidden for every mode but Zombie — with gungame/tactical additionally
proving the plain EQUIPMENT fact reappears, since deathmatch/teamdeathmatch
already hide every fact for their own unrelated rules-sub-view reason — real
control persists `Settings.ZombieOwnGear`, frozen during arm, EQUIPMENT fact
text tracks the choice even while the fact itself is replaced by the row);
and (b) **a pre-existing, unrelated fix**: three literals (`Buttons().Length
== 30`, panel `sizeDelta.y == 488/554`, a rule-rect bound, and a
`Run-Checks.ps1` mutation anchor `20f, 307f, Width, 124f`) were already stale
before this task — `604a76d` ("Add zombie mode to the catalog...") grew the
mode panel by one row (+42: 488→530, 554→596, 307f→349f) without updating
this harness, so it was failing on its own before any of this stage's edits.
Fixed as part of touching this file anyway; both `dotnet run` (963
assertions) and `Run-Checks.ps1` (963 assertions + 14/14 negative controls)
are green. `tools/DeathmatchMenuChecks/Verify.ps1` chains six tools plus two
`Run-Checks.ps1` harnesses; five of the six run standalone-green
(DeathmatchMenuChecks, NativeModeSelectionChecks, NativeMenuChecks,
TeamGiveUpChecks, TdmMatchMenuChecks) but `tools/AreaTeamMenuChecks` fails on
its own, untouched by this stage's diff, on the exact same pre-existing
panel-height-literal class of bug (a different hardcoded bound, `>=668` /
`<=916`, sized for the pre-Zombie panel). Spun off as a separate task rather
than fixed here, since it is unrelated to item 1 and was already broken
before this stage started.

`ArenaMutator` is back to 15 values (`OwnGear` removed); no
`ArenaMutatorsView` code changed (it already builds its rows generically from
`Enum.GetValues`). `ArenaSession.ZombieOwnGearSelection`'s frozen-at-arm
capture/6-reset-site contract is untouched — it reads `Settings.ZombieOwnGear`
directly, not the new UI, and the new write path
(`Settings.TrySelectEquipmentChoice`) writes that exact same `ConfigEntry`.

### 2026-10-06 follow-up, item 2 ("announcer mutator") — Stage B

After item 1 moved own gear out of the mutators panel, the user asked for a
new mutator toggling the Arena announcer (stingers, round/match cues — never
the HUD's own "WAVE n" text callouts, a completely separate event chain, see
`ArenaAnnouncerGate`'s doc comment) PER MODE, the same way Killcam is per
mode: default OFF for Zombie, default ON for every other mode, frozen at arm
like the other per-mode mutators.

`ArenaMutator.Announcer` is back to 16 values. Followed the Killcam
PER-MODE-DISPATCH shape (`Settings.AnnouncerEntry(mode)`, mirroring
`ReplayEntry`/`TracerEntry`), not Killcam's SPECIAL-CASE shape: unlike Killcam
(a float "seconds" entry) and the retired OwnGear/equipment-choice row
(offered for only one mode), every mode maps to a real bool entry here, so
`ArenaMutatorSelection`'s existing generic `Entry()`/`Enabled()`/`TryToggle()`
path — already used for Tracers/Lasers/Akimbo — needed no special case at
all, only one more switch arm in `Entry()`.

Two independent switches, per the task's own design hint ("keep volume
global and make the enable per mode; the global switch, if any, remains a
master gate"): the pre-existing single global `Settings.AnnouncerEnabled`
("Audio"/"Announcer") is UNCHANGED and stays the master kill switch for
every mode; `AnnouncerVolume`/`AnnouncerUseUiVolume` stay global too, exactly
as asked. Five new per-mode `ConfigEntry<bool>` ("Announcer" key under each
mode's own section — Deathmatch/Gun Game/Tactical/Team Deathmatch/Zombie —
so none collides with the global "Audio"/"Announcer" key), bound through a
new `Settings.BindAnnouncer(config, section, defaultValue)` helper mirroring
`BindReplay`, with every default pulled from the single pure table
`Modes.ArenaAnnouncerGate.DefaultEnabled(mode)` so the bound defaults can
never drift from what `ArenaAnnouncerGateTests` actually exercises (Zombie
false, everyone else true — one negated comparison, not five positive ones).
`Settings.AnnouncerEntry(mode)` dispatches to the right one of the five.
`Settings.IsAnnouncerEnabledFor(mode)` is the ONE place that ANDs the master
and the per-mode value together (`ArenaAnnouncerGate.IsEnabled`), because
Stage A's own research for this follow-up had flagged exactly this risk:
"all these call sites must agree on which mode they are gating for the
CURRENT match" — `AnnouncerService` has four-plus read sites (the attach-time
log line, `SubmitCallout`, both checks inside `Tick`, `Receive`) that used to
read the bare global switch; all four now call the one gate function instead
of repeating the `&&`, so they cannot disagree with each other even if a
future edit touches only one of them.

**(Superseded the same day — see "ANN-1 fix" below.)** No `ArenaSession`
frozen capture (unlike `ZombieOwnGearSelection`/`GunGameLadderSelection`'s
1-set/6-reset-site pattern) — per Stage A's own handoff note, this is
deliberately the SIMPLER shape: `AnnouncerService` already keeps its own
`_match` field, and `Attach()` already runs strictly AFTER `ArenaSession.
ActiveMode` latches (from `ArenaMatchService`'s raid-start handler), so a live
read of `_match.Mode.Id` can never observe a mode different from the one the
running raid actually armed — the mode itself is already immutable for a
match's whole life (`ArenaMatch.Mode` has no setter). The WRITE side still
freezes exactly like every other per-mode mutator: `ArenaMutatorSelection.
TryToggle` refuses every write while `ArenaSession.IsActive||ArmedForNextRaid`,
the same lock Killcam and the equipment choice use — no new lock plumbing
needed. **This was wrong**: it shows the mode id cannot drift, never that the
per-mode `ConfigEntry`'s VALUE cannot change mid-match via a live F12 edit,
which bypasses `TryToggle`'s lock entirely. `AnnouncerService` now reads a real
frozen `ArenaSession.AnnouncerEnabledSelection` instead.

The attach-time log line (`"Arena announcer attached: ..."`, the exact
literal `tools/AnnouncerChecks` pins) now reports the COMBINED per-mode gate
under the same `enabled=` field name the existing check asserts, plus a new
`mode=` field; `AnnouncerVolume` stays untouched there.

Pure tests: `ArenaAnnouncerGateTests.cs` (6 tests: the per-mode default
table, a defensive null/empty-mode case, and all four truth-table rows of
the master+per-mode AND via `[Theory]`). `tools/TdmMatchMenuChecks` gained:
the panel button count pin (15→16, "fifteen"→"sixteen"), direct assertions on
the five fresh per-mode defaults and the `AnnouncerEntry` dispatch (including
an unmapped-mode case normalizing to Deathmatch), the combined-gate master
override check, and `ArenaMutator.Announcer` added to the existing
`preferencePairs` round-trip array — which means it is now ALSO automatically
covered by every generic loop that already walks that array (F12 refresh,
save-without-auto-save, live active/armed lock, stale-popup-cannot-reopen,
description/layout non-overlap over `Enum.GetValues`). `tools/AnnouncerChecks`
gained a new integration check (`VerifyPerModeGate`, linking the real
`AnnouncerService.cs`): a Zombie-mode match stays silent on a callout even
with the master switch on, a non-Zombie match still plays with that exact
same master switch, and the master switch still silences a mode whose own
per-mode default is on — proving the WIRING at the real read sites, not just
the pure table.

**Fault control:** none needed beyond the `[Theory]` truth table itself,
which already exercises every one of `ArenaAnnouncerGate.IsEnabled`'s four
input combinations directly (an AND swapped for an OR, or either operand
dropped, fails at least one of the four rows). A deliberate
`StringComparison.Ordinal` swap in `DefaultEnabled` (mirroring
`ZombieOwnGearGate`'s own fault control) was tried and reverted: `"ZOMBIE"`
(uppercase) stopped comparing equal, so `ZombieDefaultsOffEveryOtherModeDefaultsOn`
failed exactly as expected; reverted, full suite green.

Three csproj files link the real `ArenaMode.Client/Config/Settings.cs` (or,
for `ArenaMutatorSelection.cs`, the real file directly) as production-linked
test sources and therefore needed `ArenaAnnouncerGate.cs` added to their own
`<Compile Include>` list once `Settings.cs` started calling it as real code
(not just inside a doc-comment `<see cref>`, which never breaks a build):
`tools/TdmMatchMenuChecks` and `tools/AreaConfigChecks`. Found by actually
building each tool that links `Settings.cs`, not by inspection — `tools/
AreaConfigChecks` built clean before this stage (confirmed: Stage A's own
`Modes.ArenaEquipmentChoice` reference lives only in a doc comment, so it
never needed this) and failed with six `CS0234`s immediately after this
stage's `Settings.cs` edit, until fixed; re-verified green afterward (178
assertions, real BepInEx bind/save/reload). Two OTHER tools whose csproj
files happen to contain the substring "Settings.cs" (`tools/
ParticipantMenuChecks` → `ArenaParticipantSettings.cs`, `tools/
RaidInfoPanelChecks` → `InfoPanelSettings.cs`) do not link the real
`Config/Settings.cs` at all and needed no change; confirmed by building both
(`RaidInfoPanelChecks` green at 326 assertions, `ParticipantMenuChecks`
unaffected by this stage's diff in either direction).

**Pre-existing, unrelated build breaks found while checking the above (not
caused by this stage, not fixed here):** `tools/ParticipantMenuChecks` fails
on a missing `Hud` name in `ArenaAreaSelectionView.cs` (CS0103); `tools/
RoleLabelChecks` fails on a missing `WildSpawnTypeExtension` name in
`RoleClassifier.cs` (CS0103). Both confirmed via `git diff --stat HEAD` on
the implicated files returning empty — this stage's diff touches neither.
Same bug CLASS Stage A already found and spun off separately
(`tools/AreaTeamMenuChecks`, task_38d68c2f): an offline tool's own csproj
drifted out of sync with a production file it links, independent of
anything either follow-up item changed.

Offline (`dotnet test ArenaMode.Tests`, no-deploy sentinel confirmed absent
before and after): **870 passed, 2 skipped (pre-existing, environment-gated,
unrelated), 0 failed, 872 total** (+6 over Stage A's 866, all in the new
`ArenaAnnouncerGateTests.cs`). Clean Release builds of `ArenaMode.Client` and
`ArenaMode.Server` with the same no-deploy properties: 0 errors, 0 new
warnings. `tools/TdmMatchMenuChecks` (`dotnet run`): 1064 assertions, up from
963; its `Run-Checks.ps1` mutation harness: 1064 assertions + 14/14 negative
controls, still all green (none of this stage's edits touch any of the 14
mutated fragments). `tools/AnnouncerChecks` (`dotnet run`, links the real
`AnnouncerService.cs`): 313 assertions. `tools/AreaConfigChecks` (`dotnet
run`, real BepInEx bind/save/reload against `D:\SPT41`'s installed
assembly): 178 assertions.

### 2026-10-06 follow-up, item 2 — ANN-1 fix (frozen at arm)

Review of Stage B found a real P1 the same day it landed: the paragraph above
("No `ArenaSession` frozen capture ... deliberately the SIMPLER shape") was
wrong, and the code it described the bug it argued around. `_match.Mode.Id`
cannot drift mid-match, exactly as argued — but that argument never addressed
the per-mode `ConfigEntry`'s VALUE changing mid-match via a live BepInEx F12
edit, which bypasses `ArenaMutatorSelection.TryToggle`'s armed/active lock
entirely (F12 writes the `ConfigEntry.Value` directly). `Settings.
IsAnnouncerEnabledFor(mode)` was being called fresh at every one of
`AnnouncerService`'s four-plus live-match read sites for the whole life of a
match, so an F12 edit made mid-raid — to either the per-mode entry or, as a
day-one `BindAnnouncer` description claimed without it being true, "frozen...
while the raid is active or armed" — reached a running match immediately.
Every sibling per-mode setting in this codebase (Killcam's seconds,
`GunGameLadderSelection`, `ZombieOwnGearSelection`) was deliberately engineered
specifically to close this exact class of bug; Announcer's per-mode half
shipped without it.

Fix: `ArenaSession.AnnouncerEnabledSelection` (new `bool` property, the same
1-set/6-reset-site shape as `ZombieOwnGearSelection`) is captured once in
`MarkArmed` from a new raw-value getter, `Settings.GetAnnouncerPerModeEnabled
(mode)` (the per-mode entry alone, no master switch — mirrors `GetReplaySeconds`
feeding `ArenaReplaySelection.Capture`), and reset to `false` at the same six
sites `ZombieOwnGearSelection` resets at (`ClearArmed`; the ordinary-raid,
Fika-rejected, armed-map-mismatch and server-rejected branches of
`OnRaidStarted`; `OnRaidEnded`). `AnnouncerService` gained one private helper,
`IsEnabledForLiveMatch()`, ANDing the live master switch
(`Settings.AnnouncerEnabled.Value`, per the follow-up's own design: "the
global switch, if any, remains a master gate" — deliberately NOT frozen, so
F12 can still silence/unsilence any mode's announcer mid-raid as a real kill
switch) with the frozen `AnnouncerEnabledSelection`, through the same
`ArenaAnnouncerGate.IsEnabled` pure function as before. All four read sites
(attach-time log line, `SubmitCallout`, `Tick`, `Receive`) now call that one
helper instead of `Settings.IsAnnouncerEnabledFor` directly; that method
itself is unchanged and stays available for menu-time display (the same role
`GetReplaySeconds`/`ZombieOwnGear.Value` already play), with its doc comment
corrected to say so. `ArenaAnnouncerGate.cs`'s own doc comment — the one that
argued no capture was needed — is corrected to explain the real design instead
of the disproven one.

Fault control (inject/red/restore/green, via `tools/AnnouncerChecks`'
`-p:AnnouncerServiceSource=<mutant>` override — the same mechanism `Verify-
Regression.ps1` already uses for every other `AnnouncerService.cs` mutation):
`IsEnabledForLiveMatch()`'s body swapped back to the pre-fix
`Settings.IsAnnouncerEnabledFor(_match?.Mode?.Id)` (a live, `Mode.Id`-keyed
read, identical in shape to the bug this fix closes). Two new assertions in
`VerifyPerModeGate` — a Zombie match armed with the per-mode switch ON must
play despite Zombie's live default being off, and a Team Deathmatch match
armed OFF must stay silent despite its live default being on — fail exactly
as expected against that mutant (red: `ARENA_ANNOUNCER_CHECKS_FAILED: ANN-1: a
Zombie match armed with the per-mode switch on must play, even though
Zombie's own live default is off.`); reverted to the real fixed source, full
suite green again (green: 315 assertions, up from 313).

`tools/AnnouncerChecks/StandIns.cs` gained a minimal `ArenaMode.Session.
ArenaSession` stand-in (`AnnouncerEnabledSelection`, settable directly — this
harness does not and cannot link the real `ArenaSession.cs`, which is
Unity/EFT-only; see AGENTS.md's "client-side verification is still a raid")
and a minimal `ArenaMode.Modes.ArenaAnnouncerGate` stand-in (`IsEnabled` only
— the real file's `DefaultEnabled` half pulls in `ArenaPopulationPolicy`,
which this harness has no other use for). `AnnouncerModeChecks.cs`'s
`VerifyPerModeGate` now sets `ArenaSession.AnnouncerEnabledSelection` directly
before each `Attach`, simulating the one capture `MarkArmed` would have made,
instead of deriving the expected mute/play outcome from the attached match's
own `Mode.Id` the way the old fake `Settings.IsAnnouncerEnabledFor` did —
which is also how the two new assertions above prove the fix is real: they
deliberately arm a mode AGAINST its own live default and show the frozen
value, not the mode id, wins.

Offline (`dotnet test ArenaMode.Tests`, no-deploy sentinel confirmed absent
before and after): unchanged at 870 passed, 2 skipped, 0 failed, 872 total —
`ArenaSession.cs`'s own capture/reset wiring cannot be unit-tested outside the
game (same reason `ZombieOwnGearSelection`'s capture never got its own xunit
test either; `ArenaAnnouncerGateTests.cs`'s pure truth table is unchanged and
still green). Clean Release builds of `ArenaMode.Client` and `ArenaMode.Server`
with the same no-deploy properties: 0 errors, 0 new warnings. `tools/
AnnouncerChecks` (`dotnet run`, links the real `AnnouncerService.cs`): 315
assertions, up from 313. `tools/AnnouncerChecks/Verify-Regression.ps1` (the
full historical mutation suite plus the two new assertions above): all
negative cases still fail at their expected boundary, production sources
confirmed byte-unchanged by the script's own hash checks, final positive run
green — `ARENA_ANNOUNCER_REGRESSION_VERIFIED`. `tools/TdmMatchMenuChecks`
(`dotnet run`, links the real `Settings.cs`): unchanged at 1064 assertions —
this fix added no new menu-facing surface for it to cover.

## v2 (2026-10-06 night): equipment choice in the left panel, per-mode announcer

Consolidated reference for the night's two user-requested changes (the same
evening's own-gear/wave work above is v1; this is the user's follow-up after
playing v1). The full narrative — design reasoning, every call site touched,
every fault control's exact mechanism — is already written above as
`### 2026-10-06 follow-up, item 1 ("own gear to the left") — Stage A`,
`### 2026-10-06 follow-up, item 2 ("announcer mutator") — Stage B` and
`### 2026-10-06 follow-up, item 2 — ANN-1 fix (frozen at arm)`, all still
nested under the `## v1` heading because they were written before this
section existed; this section does not restate that narrative, only
collects it into the table/checklist shape the task asked for. Commits,
oldest first: `f79efc4` `3080683` `11c97e0` `ab2ed91` (item 1) `2f1df78`
`f82c187` `a32dda3` `2124f17` (item 2) `091c5f9` (ANN-1 fix).

### Equipment-choice availability table

`Modes.ArenaEquipmentChoice.IsOfferedFor(mode)` is the single source of
truth; both the left-panel row and the EQUIPMENT fact text read only this
table, never a mode-id literal of their own.

| Mode | Offered today? | What shows in the left panel |
|---|---|---|
| Zombie | **yes** | `ArenaEquipmentChoiceView` row ("Arena kits" / "Own gear") replaces the EQUIPMENT fact, drives `Settings.ZombieOwnGear` |
| Deathmatch | no | plain EQUIPMENT fact ("Arena kits"); table entry is the only change needed later |
| Gun Game | no | plain EQUIPMENT fact |
| Team Deathmatch | no | plain EQUIPMENT fact |
| Tactical | no (has its own mechanism) | plain EQUIPMENT fact; the automatic protected-team own-gear path is untouched and gets no row here |

### Frozen plumbing

- **Equipment choice.** Unchanged from v1: `ArenaSession.ZombieOwnGearSelection`
  freezes `Settings.ZombieOwnGear.Value` at `MarkArmed`, reset at the same six
  sites as before. Only the write path moved, from
  `ArenaMutatorSelection.TryToggle` to the mode-dispatched
  `Settings.TrySelectEquipmentChoice`, onto the exact same `ConfigEntry`, the
  exact same `ArenaSession.IsActive||ArmedForNextRaid` lock and the exact same
  `File.Save()` fallback.
- **Announcer.** Shipped WITHOUT a frozen capture on day one (the live
  `_match.Mode.Id` read was argued safe because the mode id cannot drift —
  true, but irrelevant to the per-mode `ConfigEntry`'s *value* changing via a
  live F12 edit, which bypasses `TryToggle`'s lock entirely). Caught in
  same-day review as ANN-1 and fixed: `ArenaSession.AnnouncerEnabledSelection`
  now freezes `Settings.GetAnnouncerPerModeEnabled(mode)` at `MarkArmed`,
  reset at the same six sites `ZombieOwnGearSelection` resets at.
  `AnnouncerService.IsEnabledForLiveMatch()` ANDs that frozen value with the
  still-LIVE `Settings.AnnouncerEnabled` master switch (deliberately not
  frozen — it is the kill switch the task asked to keep global and immediate)
  through the same pure `ArenaAnnouncerGate.IsEnabled`. All four of
  `AnnouncerService`'s read sites (the attach-time log line, `SubmitCallout`,
  both `Tick` checks, `Receive`) call this one helper now, so they cannot
  disagree with each other.

### Announcer per-mode default table

`Modes.ArenaAnnouncerGate.DefaultEnabled(mode)` is one negative comparison
(Zombie → false, everything else → true), so a sixth mode added later
defaults to announcer ON without anyone having to remember to list it.

| Mode | Default | Backing `ConfigEntry` |
|---|---|---|
| Zombie | **OFF** | `Zombie / Announcer` (`ZombieAnnouncerEnabled`) |
| Deathmatch | ON | `Deathmatch / Announcer` (`DeathmatchAnnouncerEnabled`) |
| Gun Game | ON | `Gun Game / Announcer` (`GunGameAnnouncerEnabled`) |
| Tactical | ON | `Tactical / Announcer` (`TacticalAnnouncerEnabled`) |
| Team Deathmatch | ON | `Team Deathmatch / Announcer` (`TeamDeathmatchAnnouncerEnabled`) |

The pre-existing global `Settings.AnnouncerEnabled` ("Audio" / "Announcer")
is unchanged and stays the master gate over all five rows above; volume
(`AnnouncerVolume`/`AnnouncerUseUiVolume`) stays global too. HUD text
callouts (e.g. "WAVE n") are a separate event chain and are untouched by any
of this.

### Tests and fault controls

Offline totals progressed 857 (end of v1) → 866 (Stage A, +9
`ArenaEquipmentChoiceTests`) → 872 (Stage B, +6 `ArenaAnnouncerGateTests`) →
872 unchanged (ANN-1 fix — `ArenaSession`'s own capture/reset wiring is
Unity-only and cannot be unit-tested, same as `ZombieOwnGearSelection`'s
capture never having its own xunit test). Final: 870 passed, 2 pre-existing
skips, 0 failed, 872 total; no-deploy sentinel confirmed absent before and
after every run; `ArenaMode.Client`/`ArenaMode.Server` Release builds 0
errors throughout.

Fault controls actually run (inject/red/restore/green), summarized from the
full detail above:

- `ArenaEquipmentChoice.IsOfferedFor` forced to `true` for every mode → 3
  tests red → reverted, green.
- `ArenaEquipmentChoiceState.Visible`'s `!_tacticalOnly` guard dropped → 1
  test red → reverted, green.
- `ArenaAnnouncerGate.DefaultEnabled`'s `OrdinalIgnoreCase` swapped to
  `Ordinal` → `ZombieDefaultsOffEveryOtherModeDefaultsOn` red → reverted,
  green.
- `ArenaAnnouncerGateTests`' `[Theory]` truth table exercises all four
  `IsEnabled(master, perMode)` rows directly.
- ANN-1: `AnnouncerService.IsEnabledForLiveMatch()` reverted to the pre-fix
  live `Settings.IsAnnouncerEnabledFor(_match?.Mode?.Id)` read via
  `tools/AnnouncerChecks`' `-p:AnnouncerServiceSource` override → the two new
  `VerifyPerModeGate` assertions (Zombie armed ON must play despite its own
  live default being off; Team Deathmatch armed OFF must stay silent despite
  its own live default being on) failed exactly as expected → reverted to
  the real fixed source, green (315 assertions, up from 313).

Offline tool totals at the end of this work: `tools/TdmMatchMenuChecks`
1064 assertions (`dotnet run` and its `Run-Checks.ps1` mutation harness, 14/14
negative controls); `tools/AnnouncerChecks` 315 assertions plus
`Verify-Regression.ps1`'s full historical mutation suite all still failing at
their expected boundary; `tools/AreaConfigChecks` 178 assertions. Two
pre-existing, unrelated offline-tool build breaks found while verifying the
above (`tools/ParticipantMenuChecks`, `tools/RoleLabelChecks`) were spun off
rather than fixed here; two more of the same bug class
(`tools/AreaTeamMenuChecks`, and `tools/ParticipantMenuChecks`'s own stale
ceiling) were fixed by a different, parallel session on this shared branch
the same night (`e73351b`, `c4ba796`, `2739263`, `0e05208`) — confirmed via
`git diff --stat` that none of those four commits touch any file this v2
work changed.

### GPU playtest checklist for v2 (not yet played)

Per `AGENTS.md`, "for anything client-side, verification is still a raid" —
every item below is still open; nothing in this list has been confirmed in
a running game yet. The 56-concurrent-zombie performance measurement raid is
tracked separately (see "State" above / the handoff) and is not part of
this checklist.

1. **Left-panel equipment-choice row** is visible in the Zombie mode panel,
   shows "Arena kits"/"Own gear", and actually toggles
   `Settings.ZombieOwnGear` (persisted, reflected in the EQUIPMENT fact text,
   frozen once armed — an F12 edit mid-arm must not move the live raid).
2. The same row is **N/A/hidden for every other mode** (Deathmatch, Gun
   Game, Team Deathmatch) and Tactical in particular shows its ordinary
   plain EQUIPMENT fact with its automatic protected-team behaviour
   completely unaffected.
3. **Mutators panel shows 16 rows**, `ANNOUNCER` among them, laid out in the
   same two-column shape as before (no visible OWN GEAR row left there).
4. **A Zombie raid stays silent** on the announcer (no stingers/match cues)
   under the per-mode default, with the global Audio/Announcer master switch
   left on; toggling the master switch off and back on around that same raid
   still works as an immediate kill switch.
5. **A Deathmatch (or other non-Zombie) raid is audible** on the announcer
   under its per-mode default, with the same master switch on.
6. **Killcam is unchanged**: its per-mode seconds setting, menu row and
   recording behaviour show no regression from either change above.
7. **Regression**: an ordinary non-Arena raid and a Zombie raid with
   `OwnGear` off behave exactly as before this night's work.

## Performance at 56 concurrent infected (measured 2026-10-06)

The first measurement of the mode at its ceiling: SPT-GPU-2 (Ryzen 7 9800X3D, RTX
4070 Ti, 1920x1080), build `acd57ff` (the v1 client, before v2), 20:07-20:17. The
host ran the user's own settings, not the defaults: MaxAlive 56, WaveStartSize 56
(80 for the measurement), +10 per wave, spawn interval 0.2 s, shooter share 20 %,
OwnGear on (user loadout, MP7A1), godmode on so the player is a standing target.
ModProfiler's overlay was open from 20:09:19 with a 30 s AutoExport. Every number
below is from `GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`, "Performance-Messung mit 56
gleichzeitigen Zombies", which holds the full tables.

Headline: 17.2 ms (58.2 fps, p95 18.1 ms) with 56 alive and about 80 corpses, the
profiler open; the same run settled at 8.4 ms (119.7 fps) with 20 alive and the
profiler closed, equal to the v1 raid. That is roughly **0.25 ms per additional
infected at the player** (0.21-0.27 across the three samples taken with the horde
at the player, against the 8.4 ms reference; 0.15 in the first sample, 12 s after
the start, while the horde was still 13-18 m away). The 20-infected reference appears
to sit at the 120 fps limit (p95 8.3 ms against a mean of 8.4 ms), so the slope is
more likely a lower bound than an upper one. No error lines in the raid.

### The measured budget

ModProfiler 30 s delta window 20:12:19-20:12:49, 56 alive, about 80 corpses from two
watchdog clear-outs, ms per frame:

| Row | ms/frame | Share of 17.2 ms | Whose | Largest items |
|---|---:|---:|---|---|
| EFT (vanilla, instrumented) | 4.60 | 27 % | EFT | `DoWorldTick` 1.70; `Player.ComplexUpdate` 1.30 at 116.6 calls† (56 alive); `BotOwner.UpdateManual` 0.88; `AICoreController.Update` 0.71 |
| DrakiaXYZ-BigBrain | 0.85 | 5 % | third party | `BotAgentUpdatePatch` 0.70 (56 agents); `BotBaseBrainUpdatePatch` 0.15 |
| SAIN | 0.67 | 4 % | third party | `WorldTickPatch` 0.35; `DirectionDataJob` 0.15; small per-bot hooks, although SAIN controls no infected |
| maschine-ArenaMode | 0.48 | 2.8 % | **ours** | `ArenaController.Update` 0.225 (all Arena services); `MeleeAttackNodePatch` ~0.12; `BurstSpawnService.SpawnPair` 3 ms per spawn, only while spawning |
| PitAlex-PitFireTeam | 0.43 | 2.5 % | third party | three per-bot prefixes: `HostileNonCombatActiveLayerFilter` 0.26, `FollowerWeaponSelector` 0.065, `LootPatrol` 0.03 |
| DrakiaXYZ-Waypoints | 0.13 | 0.8 % | third party | `FindPath` prefix |
| **Instrumented total** | **~7.3** | **42 %** | | the listed rows add to 7.16; the remaining rows are each below 0.06 |
| **Not instrumented** | **~10** | **58 %** | engine | rendering and animation of 56 bodies plus corpses, physics (CharacterController), audio, Unity internals |

† Corrected 2026-10-07: an earlier version of this row read 116.6 calls/frame
against 56 live infected as "56 alive + ~60 dead bodies still updated". A dead
body leaves `GameWorld.AllAlivePlayersList` one frame after death
(`Player.OnDead` → `OnDeadCoroutine` → `GameWorld.UnregisterPlayer`), well
before any hiding or trimming runs, so it is never ticked again regardless of
how long release waits. The 116.6 figure is 56 LIVE bodies getting
`ComplexUpdate` called about twice each per frame (once via `PlayerTick`'s
own-queue call, once via `DoOtherWorldTick`'s other-queue call), not a
per-corpse cost. See "v3 (2026-10-07): faster corpse release in Zombie mode"
below for the full decompile evidence and correction.

How to read it. The profiler's counters only advance while its window is open, and
the delta columns are the last 30 s, not a raid average (see
`LONG-RAID-REVIEW-2026-09-16.md`, "How the numbers were obtained"). Its
instrumentation costs about 0.17 µs per measured call, which is roughly 0.02 ms of
the 1.30 ms `ComplexUpdate` row. Rows are inclusive, so the total is an upper bound:
`ComplexUpdate` is a nested row in ModProfiler (the README says it must not be added
to a total by hand), and BigBrain's `BotAgentUpdatePatch` replaces
`AICoreAgent<BotLogicDecision>.Update`, which `AICoreController.Update` calls once
per agent, so its 0.70 ms very probably sits inside the vanilla row's 0.71 ms (the
two are equal at 56 calls each). This was not re-derived from the CSV export. The
consequence is that the ~10 ms remainder is a lower bound, and that more than half
of the frame is outside what the profile can see.

### What is ArenaMode's, and what is not

- **Ours, directly: 0.48 ms, 2.8 % of the frame**, about 9 µs per infected per
  frame. Nothing in it is a candidate worth touching on its own.
- **Ours by policy:** how many infected exist (`MaxAlive`, spawn pace, shooter
  share), how long their bodies stay in the scene (corpse cleanup), and when the
  collector runs (`GcThresholdMonitor`). The cost of those decisions lands in
  EFT's rows and in the uninstrumented remainder, not in ours.
- **Not ours, in third-party mods: 2.1 ms** (BigBrain 0.85, SAIN 0.67, PitFireTeam
  0.43, Waypoints 0.13). They hook every bot, so they run for every infected whether
  or not they control it. On a bare install this share is absent.
- **Not ours, EFT's own per-bot work: about 1.6 ms** (`BotOwner.UpdateManual` and
  `AICoreController.Update`, ~28 µs per infected) plus the world tick.

### Optimisation candidates, ordered

Ordered by how directly the numbers support the change and how much of it is ours to
change. None of them is implemented; each needs a measured raid, as the corpse and
pool work before it did.

**(a) Corpse handling in Zombie mode. The only item with a measured number and an
Arena-owned lever. Implemented 2026-10-07, pending its own GPU measurement — see
"v3 (2026-10-07): faster corpse release in Zombie mode" below.**
- Evidence, corrected 2026-10-07: the original entry here read `Player.ComplexUpdate`
  running 116.6 times per frame for 56 live bodies as "about 60 dead ones were still
  being updated" and estimated ~0.65 ms/frame for them (1.30 ms / 116.6 = 11.1 µs per
  call). That is wrong — a dead body leaves `GameWorld.AllAlivePlayersList` one frame
  after death (`Player.OnDead` → `OnDeadCoroutine` → `GameWorld.UnregisterPlayer`),
  well before hiding or trimming ever runs, so `ComplexUpdate` never runs on it again;
  the 116.6 figure is 56 LIVE bodies called about twice each per frame (`PlayerTick`'s
  own-queue call plus `DoOtherWorldTick`'s other-queue call). What a hidden-but-
  unreleased body DOES still cost is the bounded-scene-growth case
  CORPSE-CLEANUP-REVIEW.md's 2026-09-17 measuring raid already made: it stays a
  complete, active player GameObject hierarchy (ragdoll, disabled-but-still-
  registered renderers) in `GameWorld.LootList` and every scene search anybody runs,
  not a per-frame `ComplexUpdate` call. See "v3 (2026-10-07)" below, "Why (decompile
  evidence)", for the full correction.
- Why they linger: the stuck-wave watchdog retires up to 56 bodies at once
  (`ZombieSpawnService.RetireAllAliveInfected` kills them through
  `ArenaAdministrativeDeathGate`). `CorpseCleanupService` hides everything beyond
  `MaxCorpses` (default 10) at its next 5 s sweep, but a hidden body is still a
  complete, active player object (CORPSE-CLEANUP-REVIEW.md, "Why hiding was not
  enough"), and the release is capped at `MaxReleasesPerSweep = 8`. Releasing 56
  bodies therefore takes seven sweeps, about 35 s. The 15 m "near the player"
  exemption is not the cause: it is already 0 for Zombie.
- Option: raise the per-sweep release cap for Zombie only (8 to a few times that, or
  scaled to the backlog), and/or release infected bodies at the first sweep after
  they are hidden instead of queueing them. Zombie mode has no loot, so a body has
  nothing to preserve and nothing is lost by releasing it earlier. A watchdog
  retirement could in principle remove the body outright instead of killing it; that
  is a different native path and is not evaluated here. What actually shipped is
  neither a wider per-sweep cap nor an immediate release: a faster 1 s cadence, a
  SMALLER two-per-frame cap than the ordinary eight, and draining that cap between
  sweeps as well as at one — see "v3 (2026-10-07)" below for why.
- Payoff, corrected 2026-10-07: not a per-frame ms figure — the "roughly 1-2 ms during
  a clear-out" this bullet used to claim rested on the wrong Evidence above and is
  withdrawn with it. The actual payoff is the same bounded-scene-growth benefit
  CORPSE-CLEANUP-REVIEW.md's 2026-09-17 raid measured, now applied to a watchdog
  clear-out's 56-body backlog: it drains in the few seconds v3's 1 s cadence and
  two-per-frame drain take to empty it (not the ~35 s the ordinary 5 s/8-per-sweep
  budget would have taken — see "v3 (2026-10-07)" above and the GPU checklist below),
  nothing extra at steady state. At steady state kills arrive at the player's kill
  rate, well below the 1.6 bodies per second the cap sustains, so the queue stays
  short;
  the pile in this measurement exists because the tester did not shoot.
- Risk: real. The cap exists because a release walks every bot group and tears
  down a player hierarchy, and a burst of 50 is the frame spike it was written
  against (`MaxReleasesPerSweep` doc, CORPSE-CLEANUP-REVIEW.md, "Bounded work per
  sweep"). The release order is load-bearing (AI knowledge cleared before the
  dead-body record, DynamicMaps markers, pending aggressors, damage-source quiet
  period, open loot window); a larger cap changes the burst size, not the order, but
  the cost of one release has never been measured. It needs its own review and a
  measured raid before it ships.

**(b) Third-party per-bot hooks. Not ours: document only.** PitFireTeam's three
prefixes cost 0.43 ms (`HostileNonCombatActiveLayerFilter` alone 0.26 ms, ~4.6 µs per
infected per frame); SAIN's `WorldTickPatch` costs 0.35 ms and its per-bot hooks the
rest of 0.67 ms although it controls no infected; BigBrain's agent loop is the AI
work itself (0.70 ms, ~12.5 µs per agent, equal to the vanilla
`AICoreController.Update` row it very probably runs inside), and Waypoints'
`FindPath` prefix 0.13 ms. Payoff to us: none we can take; a user with a smaller
mod stack gets about 2 ms back. Do not patch around another mod's prefix to claim
it: SAIN's `RunToEnemyUpdate` prefix is the reason the native melee needed a reverse
patch in the first place (see "Native melee is suppressed by SAIN's global
prefix").

**(c) An AI update LOD for infected far from the player.**
- Idea: beyond about 40 m, skip every other `BotOwner.UpdateManual` / agent frame.
  EFT's own `BotStandBy` is the precedent (distance-based, gated by a bot's
  `Mind.CAN_STAND_BY`, and `UpdateManual` skips its whole body while it is paused),
  but a paused infected stops walking, so it is not usable as it stands for a horde
  that has to keep approaching.
- Payoff: an upper bound of about 1 ms. `UpdateManual` plus `AICoreController.Update`
  is 1.59 ms at 56; halving it for every infected is ~0.8 ms, plus the nested hooks
  (PitFireTeam, our melee patch) that would be skipped with it. The expected payoff
  is well below that: the first sample, 12 s after the start, already had the horde at
  13-18 m, so infected beyond 40 m exist only in the first seconds after each spawn,
  and at steady state almost none are far away.
- Risk: medium. It interacts with BigBrain's `AICoreAgent.Update` prefix (ours would
  have to order against a prefix that replaces the method) and with timers the native
  code assumes tick every frame (`_nextGetGoalTime`, the 0.1 s melee cadence,
  `Memory.ManualUpdate(Time.deltaTime)`, pursuit timers). It needs a measured
  prototype before anything else, and it must never touch an infected inside melee or
  Shooting range.

**(d) Audio: throttle `BotTalk.Say` to the nearest few infected.**
- Evidence: `InfectedAttackLayer.ManualUpdate` and `InfectedPatrolLayer.ManualUpdate`
  each call `BotTalk.Say(..., sayImmediately: true)` after `DropNextSayPeriod()` every
  `Random(1, 3)` s, so 56 infected issue about 28 phrases per second on average. The
  call is not instrumented; its cost sits in the uninstrumented remainder.
- Option: an Arena-gated prefix (Zombie mode, infected roles) that lets only the
  nearest N bots speak.
- Payoff: unknown, since nothing here measures audio; the audible gain (56 voices,
  each speaking every one to three seconds, is mostly noise to the player) may matter
  more than the milliseconds. Risk: low for
  correctness, medium for feel; read `BotTalk.Say` first for state it updates.

**(e) Spawn bursts.** `BurstSpawnService.SpawnPair` costs about 3 ms per spawn, in
the point selection. At a 0.2 s interval that is one 3 ms spike on a 12-14 ms frame
every ~12 frames for the ~11 s a 56-strong wave takes to fill, i.e. frames of roughly
15-17 ms at a wave start; averaged over frames it is ~0.25 ms. The first sample's
p95 (18.7 ms against a 13.8 ms mean, a 4.9 ms spread against 0.9-3.2 ms in the later
samples) fits it. Acceptable as it is. Spreading the point selection over frames
would remove the spikes without changing the average, but `BurstSpawnService` is
shared with the ambient burst pump and Team Deathmatch placement, so any change must
be Zombie-gated.

**(f) GC.** The heap reached 4.0 GB at raid minute 2.8 (the profiler's allocation
rate was 6.45 MB/s in the spawn phase; the managed figures in the frame table, 1.46
GB at 20:07:41 to 4.0 GB, imply a mean above 16 MB/s, and the two were not reconciled
to one quantity). `GcThresholdMonitor` armed at 4 GB and switched at the next death,
minute 4.2 at 4.69 GB, after which the heap fell to 2.97 GB. It works as designed
(GC-THRESHOLD-REVIEW.md); at 56 infected the 4 GB default is simply reached in under
three minutes, against roughly nine at the ~7 MB/s a Labyrinth Deathmatch allocates.
A lower default for Zombie (3 GB, say) would lower the peak by the difference; the
overshoot while waiting for a death (0.69 GB here) stays. Risk: the incremental
collector spends up to its 3 ms slice inside frames, and what that costs at this heap
size is not measured: the 17.2 ms sample was taken after the switch with the overlay
open and 80 corpses, so it cannot separate the collector, the overlay and the corpse
backlog. Measure before changing a default.

**(g) Rendering: not measurable with the current tooling.** There is no CPU/GPU
split, and ModProfiler times managed methods only, so Unity's skinning, Animator and
PhysX work is invisible to it; extending its curated `EFT (vanilla)` list (the set
behind `Profiling/VanillaEngineMethods`) to EFT's own body and animation entry points
would narrow the ~10 ms but not close it. If the user
wants more than 56, the next step is a Unity profiler or frame-debugger capture on
the GPU host, not guesses.

### The 4..56 range of MaxAlive, and what raising it would need

`Zombie / MaxAlive` accepts 4 to 56. The ceiling is bound by two things: the server
clone's `ZombieLocationSettings.MinimumBotCap` is a fixed 60 (`BotMax`, `BotMaxPvE`,
`MaxBotPerZone`, raised with `Math.Max`; below it BotSpawner's point selection
throttles spawn positions and silently drops spawns), and the client cap
`ArenaBotCapPatch` sets is `MaxAlive + 2`, so 56 gives 58, which must stay under 60.
The server never learns `MaxAlive`, which is why the floor is a constant. Raising
the ceiling would take: the range in `Settings.ZombieMaxAlive`; a larger
`MinimumBotCap` (or `MaxAlive` carried in the raid-start payload so the server can
derive it); both DLLs shipped together, because a client with a higher ceiling on an
old server drops spawns silently; and a check of the opening-wave profile preload
(`ZombieProfilePlan.MaxBackupCountPerPair`, 100). The cost side is the limit in
practice: extrapolating 0.25 ms per infected linearly, 80 would be about 23 ms
(~43 fps) on this host, and that is before (a) and (c). Raise the ceiling only after
those and a CPU/GPU capture.

## v3 (2026-10-07): faster corpse release in Zombie mode

Implements optimisation candidate (a) above. Zombie mode only
(`CorpseCleanupService.IsZombieRaid()`, the same ordinal check
`ActiveMinDistanceMeters` already used, now shared); every other mode is
untouched.

### What changed

- **Cadence: 1 s instead of 5** (`ZombieCadenceSeconds`, read through a new
  `ActiveCadenceSeconds()` ternary parallel to `ActiveMinDistanceMeters()`).
  The sweep still does everything it always did — reassert, trim, release —
  just five times as often in Zombie mode.
- **Release cap: 2 per frame instead of 8 per sweep**
  (`ZombieMaxReleasesPerFrame`), for the sweep's OWN release pass too, not
  only the new drain below. `ReleaseTrimmed` takes this as a parameter
  (`maxReleases`) now instead of reading `MaxReleasesPerSweep` directly;
  every non-Zombie call site still passes `MaxReleasesPerSweep` (8)
  unchanged.
- **Between-sweep drain** (`CorpseCleanupService.DrainBetweenSweeps`,
  called from `Tick`'s per-frame early return whenever `IsZombieRaid()` is
  true). Releases up to `ZombieMaxReleasesPerFrame` already-trimmed bodies
  on every frame that is not itself a sweep, through the exact same
  `ReleaseTrimmed`/`MayRelease` call the sweep uses — no `ReassertTrimmed`,
  no trim pass, and the sweep counter (`_sweep`) is never incremented by it.
  That last point is what keeps the reassert invariant intact: `MayRelease`'s
  `state.Sweep < _sweep` gate is exactly as strict for a drain frame as for
  the sweep itself, so a body hidden in the CURRENT sweep still waits for the
  NEXT sweep's reassert pass before the drain can touch it — proven across
  20 drain frames within one cadence window in the regression suite.
- **Reporting.** A drain frame never logs its own line (that would turn one
  line per sweep into one per frame); what it releases is accumulated
  (`_drainReleasedSinceReport`/`_drainOwnReleasedSinceReport`) and folded
  into the NEXT working sweep's existing Info line, then zeroed.
  `CorpseCleanupService.Reset()` clears both, so nothing carries over into a
  fresh raid. A drain frame that throws is caught and reported once per raid
  — the same policy every other fault in this file already uses — and
  releases nothing for that frame rather than retrying.

### Why (decompile evidence)

**Correction, 2026-10-07.** This section originally argued that a hidden
corpse keeps costing a `Player.ComplexUpdate` call every frame until it is
released, and read the 2026-10-06 measurement's 116.6 `ComplexUpdate` calls
per frame (56 live infected) as "56 LIVE + ~60 DEAD ones still being called
every frame". That argument is wrong, and the decompile says so directly —
kept below for the record, with the correction next to it, rather than
deleted.

A dead body leaves the per-frame player tick at DEATH, not at release.
`Player.OnDead` starts `OnDeadCoroutine` (`EFT/Player.cs:30218`), which, one
frame later, calls `GameWorld.UnregisterPlayer(this)`
(`EFT/Player.cs:30247`) unconditionally — no check on whether
`CorpseCleanupService` has hidden or trimmed the body yet, because nothing in
this mod runs before the engine's own death handling does.
`GameWorld.UnregisterPlayer` (`EFT/GameWorld.cs:2060-2076`) removes the
`Player` from `RegisteredPlayers`, `allAlivePlayersByID`,
`AllAlivePlayerBridges` and — the one that matters here —
`AllAlivePlayersList`. `GameWorld.PlayerTick`/`AfterPlayerTick`/
`DoOtherWorldTick` (`EFT/GameWorld.cs:1811-1823`, `1826-1832`, `1782-1795`)
all go through `SafePlayerOperation` (`EFT/GameWorld.cs:1848-1861`), which
iterates exactly `AllAlivePlayersList` — so from one frame after death
onward, none of them ever calls `Player.ComplexUpdate`
(`EFT/Player.cs:29011`, reached from `UpdateTick`/`FixedUpdateTick` at
`EFT/Player.cs:28999-29013`) on that body again. `Player.Dispose`'s own call
to `UnregisterPlayer` (`EFT/Player.cs:30933`) is therefore a no-op repeat by
the time it runs — the `RegisteredPlayers.Contains` guard at
`EFT/GameWorld.cs:2062` returns immediately — not the FIRST removal the
original argument assumed it was.
`CorpseCleanupService.TrimCorpse`'s hiding (renderers/colliders off, the
Player component disabled, gear stripped) runs well after `OnDeadCoroutine`
already unregistered the body, so it was never in a position to keep it
ticking in the first place: the body is hidden from the PLAYER's eyes, and
the tick it is not part of any more had nothing to do with hiding at all.
The 116.6 figure is therefore 56 LIVE bodies, each getting `ComplexUpdate`
called about TWICE per frame — once from `PlayerTick`'s call for the body's
own `UpdateQueue`, once from `DoOtherWorldTick`'s call for the other queue —
not once per live body plus once per corpse.

What a hidden-but-unreleased body DOES still cost, measured and documented
well before this section existed, is exactly what
CORPSE-CLEANUP-REVIEW.md's "Why hiding was not enough" and this class's own
opening paragraphs (`CorpseCleanupService.cs`) already argue from the
2026-09-17 raid: it remains a complete, active player GameObject hierarchy —
ragdoll rigidbodies, colliders disabled, renderers forced off but still
registered with the culling system, which keeps turning them back on (hence
`ReassertTrimmed`) — sitting in `GameWorld.LootList` and
`GameWorld._allPlayersEverExisted`, and every scene search anybody runs pays
for it: 415 active LocalPlayer objects after 23 minutes,
`FindObjectsOfType<LocalPlayer>` 95 -> 303 ms, Arena's respawn pipeline
0.66 -> 1.66 s. At the ordinary 5 s/8-per-sweep budget, draining a watchdog
clear-out of up to 56 bodies (`ZombieSpawnService.RetireAllAliveInfected`)
took about 35 seconds, during which every one of those bodies sat in the
scene as that kind of dead weight — bounded scene growth, not a per-frame
tick — which is the honest reason to release a Zombie clear-out's bodies in
seconds rather than ~35: it shrinks how long that backlog is in the scene at
all, the same case the 2026-09-17 raid already made, now applied to a
watchdog's burst instead of to the raid as a whole.

### What does not change

Every `MayRelease` hold applies to a drain frame exactly as it does to a
sweep's own release pass, because both call the identical
`ReleaseTrimmed`/`MayRelease` code — the regression suite exercises the
damage-source hold (reusing the static-tripwire case) and the
newest-own-corpse hold through the drain path specifically, and both still
hold exactly as before. The ragdoll-settled gate in `TrimCorpse` and
`MayRelease` (`corpse.Ragdoll._isPhysicsDone`, which
`CorpseRagdoll.WorkingCycle`/`Corpse.CheckCorpseIsStill` only set once every
rigidbody is asleep or 15 s have passed) is completely untouched by either
Zombie constant: a body still visibly FALLS before any of this touches it —
a faster cadence only shortens how long an already-settled, already-hidden
body waits before release, never how long one still falling is left alone.
Every non-Zombie mode takes one added ordinal string comparison in `Tick`'s
per-frame early return and otherwise behaves exactly as documented in
"Bounded work per sweep" and "Behaviour that does not change" in
CORPSE-CLEANUP-REVIEW.md: 5 s cadence, 8-per-sweep cap, no release between
sweeps, the 15 m exemption for every mode except Zombie (which was already
0, unchanged by this work).

### Tests

`tools/CorpseCleanupChecks/Verify-Regression.ps1`: 309 assertions (up from
180), 35 source mutants (up from 19), all through the production `Tick()`.
New: Zombie cadence vs the ordinary 5 s one; the sweep frame and successive
0.01 s-apart drain frames each releasing exactly `ZombieMaxReleasesPerFrame`
from a six-body backlog; confirmation that nothing drains between sweeps
outside Zombie even with a ten-body, sweep-cap-exceeding backlog (what an
unconditional drain would leak out); the reassert/sweep-count invariant
across 20 drain frames within one cadence window; the damage-source and
newest-own-corpse holds applying to drain frames; all four of the drain's
fail-closed guards (`MaxCorpses = 0`, `ReleaseTrimmedCorpses = false`, no
`GameWorld`, no `MainPlayer`) releasing nothing and logging nothing, then
resuming once lifted; and the reporting fold plus `Reset()`'s clearing of the
carried-over accumulator. New mutants: `zombie-cadence-ignored`,
`zombie-drain-cap-too-wide`, `zombie-drain-disabled`,
`zombie-drain-runs-outside-zombie`. Review pass (2026-10-07) added three
more, each caught by one of the drain-only tests above rather than by a
mutant that is actually rejected elsewhere in the file:
`zombie-drain-ignores-budget-guard` (the `MaxCorpses` guard inside
`DrainBetweenSweeps` itself, not the sweep's own copy), `zombie-drain-fold-skipped`
(the report-line fold), and `zombie-drain-reset-leaks-accumulator`
(`Reset()`'s own clearing of it). A second review pass (2026-10-07) found
`zombie-drain-ignores-budget-guard` only defeated the `MaxCorpses` half of
`DrainBetweenSweeps`' combined guard line, leaving its
`!Singleton<GameWorld>.Instantiated` half and the separate
`mainPlayer == null` guard right after it without a dedicated mutant of
their own, even though test (f)'s "no-world"/"no-player" cases already
assert against both; added `zombie-drain-ignores-world-guard` and
`zombie-drain-ignores-player-guard`, each rejected by test (f)'s own "a
guarded-off drain frame logs no warning" assertion for that guard (dropping
either leaves `world`/`mainPlayer` null going into `ReleaseTrimmed`, which
throws before releasing anything, so the *warning* assertion catches it,
not the *release-count* one). The reassert/sweep-gate and
damage-source/newest-own-corpse tests do not get a matching dedicated
mutant: they call the identical code the sweep's own release pass does, with
no branch of their own to mutate, and `DamageSourceQuietSeconds` (2 s) is
longer than `ZombieCadenceSeconds` (1 s), so a real sweep — which refreshes
every per-sweep cache — always runs before a quiet timer started inside a
drain window could finish; a regression in that shared code is still caught,
by the pre-existing mutants for those exact lines. The same argument also
covers the `_newestOwnCorpseLootCount` Count-check this same pass added to
`DrainBetweenSweeps` (replacing the unconditional per-frame cache
invalidation 8556419 had added, which forced a full `world.LootList` rescan
on every one of the drain's up to ~59 calls a second regardless of whether
anything had changed — a real cost for a check whose own fail-closed
guarantee, and the cadence/quiet-period relationship above, mean no test in
this suite can ever observe the difference either way): no dedicated
mutant, by the identical reasoning, and for the identical reason 8556419
itself needed none. See `Verify-Regression.ps1`'s own comment for the full
argument.
A third review pass (2026-10-07, F1-PERF) found that `ReleaseTrimmed`'s own
loop paid the full `MayRelease` gauntlet — including
`CorpseAggressors.IsPendingAggressor` (an `O(AllAlivePlayersList.Count)`
scan) and `DamageSourcesQuiet`'s `Describe`/`StillNamedInRegistry` reads —
for every Trimmed entry that had passed its sweep gate but was still inside
the 2 s `DamageSourceQuietSeconds` window, on every one of the drain's up to
~59 calls a second: exactly the ~46-56-body watchdog clear-out this whole
change targets. `QuietWindowKnownOpen` (`CorpseCleanupService.cs`) is a
cheap pre-check, sound by `DamageSourcesQuiet`'s own documented invariant,
that skips the gauntlet entirely once the quiet timer it would consult is
already known and has not elapsed — never for an own corpse while
`TrimState.LeftRegistry` is still undecided, so the first real reading that
decides it is never skipped. New test (h) in Program.cs's Zombie section
counts `CorpseAggressors.CallCount` (new, Stubs.cs) across a six-body
backlog to prove the skip actually happens while the window is open and
that the check still runs once bodies are actually due; new mutant
`zombie-drain-skips-no-pre-check`. The pre-existing `released-in-the-hiding-sweep`
mutant is now caught at the same assertion `releases-inside-the-hit-delay`
already was, rather than at its original earliest one — see
`Verify-Regression.ps1`'s own comment on that mutant for why the pre-check
makes its sweep-gate half permanently unobservable on its own (it always
needed the paired `DamageSourcesQuiet` mutation to be observable at all).
`tools/DynamicMapCorpseChecks/Verify.ps1`: 61 assertions, 8 compiled
controls, then the same 309/35 regression suite. `dotnet test
ArenaMode.Tests` (no-deploy sentinel): 870 passed, 2 skipped (pre-existing,
unrelated), 0 failed. `dotnet build ArenaMode.Client -c Release` (no-deploy
sentinel): 0 errors, 4 known CS8632 (pre-existing, unrelated —
`ArenaMode.Shared/ClientSettingsModels.cs`, untouched by this branch).

### GPU checklist (run 2026-10-07 — results in "GPU result" below)

- WITHDRAWN (2026-10-07): "`ComplexUpdate` calls per frame should approach
  `alive count + MaxCorpses`". A dead body is not in that count at all — it
  leaves the per-frame player tick at death (`Player.OnDead` ->
  `OnDeadCoroutine` -> `GameWorld.UnregisterPlayer`), not at release, so
  `ComplexUpdate` calls/frame track only LIVE infected and say nothing about
  the corpse backlog either way. See "Why (decompile evidence)" above for the
  correction. The signals that DO apply, all already in the sweep's own Info
  line (`CorpseCleanupService`) or reachable live: `N corpse(s) in the world`
  should shrink toward `MaxCorpses` (default 10) within a few seconds of a
  clear-out, not ~35; `player bodies X created / Y pooled` for the same
  trend; `uibridge_bots` with `aliveOnly=false` for a live body count; the
  wall-clock drain duration after a clear-out; and frame time, none of which
  a per-frame tick cost was ever the right lens for.
- After a stuck-wave watchdog clear-out, the backlog of hidden bodies should
  visibly drain within a few seconds rather than ~35 (the per-sweep-cap-times-
  cadence math predicts seconds, not tens of seconds).
- No new `ArenaDamageExceptionLog` reports and no new NullReferenceExceptions
  — the damage-source guard (`CorpseDamageSources`) is reached via the drain
  exactly as via a sweep, and should behave identically.
- Bodies still visibly fall before vanishing — the ragdoll-settled gate is
  unchanged; this is the user's own explicit constraint
  ("...dann würden die ja nicht mehr zu Boden fallen").
- No warning spam in the BepInEx log from `DrainBetweenSweeps` — it should
  log nothing at all in a healthy raid, and at most one warning for the whole
  raid if something is actually wrong.

### GPU result (2026-10-07, SPT-GPU-2, build `43cc7c3`)

Two Factory raids on SPT-GPU-2 (Ryzen 7 9800X3D / RTX 4070 Ti), client SHA256
`736A11CE89618A6A9EFE2435D43DBD48A5ED8FFA6ECEF3A02D024F448DC47E72`, server
unchanged (`0A826E8B…` from `1e76582`), `uibridge_status` 24/24 identical (VM
build = host disk = loaded). Host settings were the user's own F12 values
(mirrored by Syncthing): MaxAlive 56, WaveStartSize 56, +10 per wave, 0.2 s
spawn interval, OwnGear on (the test profile's own gear is a bayonet),
StuckWaveTimeoutSeconds 100, MaxCorpses 10, ReleaseTrimmedCorpses on. Godmode
through `uibridge_hook`; the player never fired. ModProfiler 1.4.0 headless
capture (`Capture/Headless`, exports every 30 s, session `20261007-201948`).

- **Drain after a watchdog clear-out — the case this change exists for.**
  Raid 1 (ShooterPercent 20): watchdog at 20:28:35.16 retired 36 infected,
  46 corpses in the world. Sweep lines: 20:28:36.28 `trimmed 10`, 37.28
  `trimmed 25`, 38.28 `trimmed 1, released 2`, 39.28 `released 10`, 40.28
  `released 24` → 46/46 released, `10 corpse(s) in the world`, `player bodies
  2 created / 0 pooled`: **5.1 s**. Raid 2 (ShooterPercent 0): watchdog at
  20:33:35.83 retired 56, 57 corpses (one own body from a death before godmode
  was switched on). 20:33:37.49 `trimmed 47` (every ragdoll had settled within
  1.65 s), 38.48 nothing due (no line), 39.48 `released 2`, 40.47 `released
  45` → 47/47 released, `10 corpse(s) in the world`, `player bodies 3 created
  / 0 pooled`: **4.6 s**. Before, on build `acd57ff` (host log
  `LogOutput-SPT-GPU-2-2026-10-06_20-05-51.log`): watchdog 20:09:28.4 with 56
  → `trimmed 46` at 20:09:31.9, then `released 8` every five seconds and
  `released 6` at 20:10:01.9 → 46/46 after **33.5 s**. What limited today's
  drain was the 2 s damage-source quiet window per body, not the two-per-frame
  cap: up to 24 releases landed inside one second.
- **Steady state** (raid 1, where shooter infected killed other infected by
  friendly fire at roughly one death per 5–40 s): sweep lines every second
  (20:24:00.9, :02.9, :10.9, :13.0, :17.0, :19.0, :21.0); a body trimmed in
  one sweep was released exactly two seconds later (`trimmed 1 … released 0`
  at 20:24:00.916, `released 1` at 20:24:02.929) and the count returned to
  `10 corpse(s) in the world` after every release.
- `uibridge_bots aliveOnly=false` right after the raid-2 clear-out listed only
  the next wave's spawns (27 registered): released bodies are gone from the
  bot registry as well.
- **Frame time.** 56 live infected at the player: 53.7 fps, 18.6 ms mean, p95
  21.9 ms (`uibridge_status`; profiler window 20:33:04–20:33:34: 1592 frames
  in 30 s = 53 fps). After the clear-out, with wave 2 spawning (27
  registered): 113.6 fps, 8.8 ms mean, p95 10.6 ms (window 20:33:34–20:34:04:
  2863 frames = 95 fps).
- `ComplexUpdate`, for the record behind the withdrawn bullet above:
  112.5–123.4 calls per frame with 56 live infected and 0–2 corpses,
  `BotOwner.UpdateManual` 56.0 — about two calls per LIVE body, exactly the
  corrected reading.
- **No `Error`-level line** in the whole game session; no `Arena corpse
  cleanup drain failed` warning; no new kind of `ArenaDamageExceptionLog`
  report. `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` read back `false` after both raids
  (`uibridge_inspect`).
- **Bodies still fall.** Nothing is trimmed before `_isPhysicsDone`; in raid 2
  the first sweep that found settled bodies ran 1.65 s after the kill, and
  the ten newest bodies stay visible regardless (MaxCorpses). A body beyond
  that budget now disappears about 2–3 s after death (settle plus at most one
  second of cadence) where it used to take 3–8 s; if a longer lying time is
  ever wanted, a minimum settled age in `TrimCorpse` is the one place to add
  it.
- **Pre-existing, NOT introduced by v3** — both already present in the
  2026-10-06 host log: `Arena damage exception #n: site=Player.ApplyDamageInfo
  NullReferenceException … victim=SPT-GPU-2 human alive=False … release=no`
  (2026-10-06: 5; 2026-10-07 raid 2: 6), an NRE inside
  `EFT.BotMemory.GoalTargetBeingHitAction` when an infected melee hit lands on
  the dying or dead player — unrelated to released bodies (reports #1 and #2
  carry `release=no`); and `Arena burst spawn: no profiles for infectedAssault
  — request dropped` (2026-10-06: 14; 2026-10-07 wave 2: 6), the
  `infectedAssault` profile pool running dry in wave 2 (66). Both stay open.
- Tooling notes: `uibridge_hook godmode` applies to the current player body
  only (raid 2 counted five deaths after respawns, the first one before it was
  switched on); `Capture/Headless` set through `uibridge_config_set` starts
  within seconds, but the stop is applied at the next 30 s export tick.
