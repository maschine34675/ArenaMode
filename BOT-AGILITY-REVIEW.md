# Arena bot agility and weight inertia — 2026-09-28

User approved faster starts/direction changes and reduced weight influence after
accepting the previous bot tempo update. `ArenaBotAgility` adds both under the
existing `BotMovementEnabled` master; no additional configuration surface.

## Actual weight contract

Native EFT and installed SAIN 4.5.1 already give bots overweight thresholds of
9000–10000 kg and force `SprintOverweight=0`. Raising those limits again would
usually do nothing. SAIN still initializes real `BaseInertiaLimits`, so gear
affects acceleration, walking inertia, reversals and sprint turn slowdown.

The adapter reduces those **remaining locomotion inertia inputs**, not inventory
weight, armor stats, skill levels or general stamina consumption. It never calls
`OnWeightUpdated`/`OnWeightLimitsUpdated`: those also touch weapons, fall/noise
state and, crucially, a shared `EFTHardSettings.MovementAccelerationRange` curve.

## Native consumers and tuning

Only ten scalar reads in four native methods are wrapped. Original method bodies,
input clocks, `deltaTime`, state changes, path steering and collision still run.
Getter-only Harmony patches would be vulnerable to Mono inlining; these patches
modify the calling methods instead.

| Consumer | Adjusted input |
|---|---|
| `MovePlayerState.UpdateMovementDirection(float)` | Two WalkInertia reads, side/diagonal inertia, reversal inertia, second-stage speed smoothing |
| `MovePlayerState.ProcessDirection(float,float)` | Direction acceleration divisor |
| `MovementContext.PreSprintAcceleration(float)` | Pre-sprint acceleration scalar |
| `MovementContext.SprintAcceleration(float)` | Sprint acceleration scalar and weight inertia in the native turn-cap formula |

Weight penalties move halfway toward their native zero-weight baseline, only
when that improves the input. Installed SideTime `(2,1)` and DiagonalTime
`(1.4,1)` are counterintuitive: weight already lowers them, so this adapter does
not raise them toward a slower baseline. WalkInertia's baseline is `0.005`;
pre-sprint's is `3`. Sprint acceleration uses native **InverseLerp(1,0,0)=1**,
not the end of that range. These baselines are read from current global settings,
never hard-coded or written. Direct normalized inertia operands are halved.

Agility is a separate adjustment: positive acceleration inputs are multiplied
by 1.5; the weight-adjusted walking inertia is multiplied by 0.6. This addresses
both walking smoothing stages, the direction-blend floor, and sprint startup.
It does not increase aim/rotation speed. The walking input also participates in
the native `_lowerRange` acceleration calculation; it is not only a time value.
The sprint target formula remains native, but lighter inertia reduces its
weight-related turn slowdown. Existing displacement factors, door exclusions,
corner easing, speed stops and the 0.3m displacement ceiling remain unchanged.

Zero, negative and nonfinite inputs are retained; an overflowing result is also
discarded. In particular, an extremely heavy bot with native SprintAcceleration
already zero receives no relief for that one scalar. Pre-sprint, walking and
turning may still improve. Therefore this is not a promise to halve every
possible weight penalty or to remove all movement restrictions.

## Scope and cleanup

Admission reuses `ArenaBotMovement.TryGetLiveBot`: exact active game/world, live
mode phase, registered living AI body, BotOwner and controller. It uses actual
Player.IsAI because SAIN overrides MovementContext.IsAI. Human/local players,
ordinary raids, obsolete states/bodies, loading/countdown/results, abort/area
setup, paused time and shutdown are excluded. Grounded Idle/Run/Sprint only;
prone, vault/fall/jump, mounted/platform/stationary weapons and native/SAIN door
handling remain native. Both teams and PFT bodies qualify on the same terms.

Every patched invocation establishes a thread-local value-type scope. Even
ineligible nested calls establish a barrier, preventing borrowed admission.
Helpers check receiver, current body/context/state/Physical and live admission
again. Finalizers restore the predecessor after success or exceptions, and an
Entered flag distinguishes a prefix skipped by an earlier foreign Harmony hook.
Scopes allocate no per-call objects and change no stored native tuning values.
Switching off or closing the live phase therefore stops substitutions on the
next read without a reweight event or persistent restoration job.

SprintBrakeInertia is deliberately excluded: its only consumer stores an animator
parameter on sprint entry, requiring different lifetime/restore handling. This
work does not change it. Profile protection, item weights, player movement,
stamina configuration, SAIN presets and map navigation are untouched.

All four patches install as one transaction. Missing or ambiguous scalar sites
roll back this adapter alone. Exceptions retain native input and warn once. An
activation message is emitted once per world when a value actually changes.

## Verification and native acceptance

`tools/BotAgilityChecks/Verify.ps1` links production agility and shared bot
admission with boundary doubles. It checks composition, baseline direction,
zero/overflow behavior, normal raids, all live modes, body/state/controller
identity, nested barriers, exceptions, live toggles, partial patch failure and
the scalar stack contract. Compiled fault controls must fail named assertions.
The installed-IL checker verifies all ten sites, method shapes, native Vector2
Evaluate/InverseLerp semantics and existing bot overweight sentinels. No native
Unity/Harmony execution or animation simulation is claimed by those tests.

Independent read-only reviewers checked motion contracts and weight/lifetime
semantics. Exact final counts, build and delivery are recorded in `HANDOFF.md`.

After EFT restart, compare light and heavy bots starting from rest, strafing,
reversing and beginning a sprint; also check tight doors/corners and PFT allies
after player death. Toggle BotMovementEnabled to compare (it controls all bot
movement/tempo additions). No extra sliding, navigation loops or player changes
should appear. Native visual quality, action coverage and performance remain
user-run acceptance; a successful build/offline harness does not prove them.
