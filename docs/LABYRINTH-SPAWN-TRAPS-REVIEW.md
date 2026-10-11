# Labyrinth entrance traps: Arena-only native disable states

`F12 / Arena / DisableLabyrinthSpawnTraps` defaults to true and is sampled
once per confirmed Arena raid. A change applies to the next raid; disabling
the option must not re-arm a trap around a living player in the current raid.
The feature covers the ten entrance traps (four gun, two flame, two liquid/grate
and two steam zones), plus the toxic pool's shallow and deep damage zones.
It drains that pool through its native animator. It does not remove random spike
traps, trapdoors, unrelated water puzzles, map-wide fall protection, quest objects,
or the center alarm's independently configured sound suppression.

## Installed scene contract

`globalgamemanagers` maps `level552` to
`Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity`.
SHA256: `d39b3c4606d51fe9699319ef1da4f671f129843c1bcd7bc8f23efd68436bbfeb`.
Read-only inspection with UnityPy 1.25.x is reproducible using
`tools/LabyrinthTrapChecks/Inspect-InstalledScene.py` and the installed
`EscapeFromTarkov_Data` directory. No game assets/audio are copied or edited.

| Trap | Native off control | Controlled damage/shot entries |
|---|---|---|
| Gun | `disable_traps_01` | `guntrap_trigger_1628276106`, `2131128863`, `1446364582`, `2505672369` (same prefix) |
| Flame | `disable_traps_02` | `flame_enter_zone_493925125`, `flame_enter_zone_1349880479` |
| Liquid | `disable_traps_04` | `Luquid_enter_zone_3486811782`, `Luquid_enter_zone_2773347541` |
| Steam | `set_valve_01` | `steam_enter_zone_115583983`, `steam_enter_zone_3704346085` |
| Toxic pool | `WaterRemove_2612876931` | `toxicZoneEnter1_2612876931`, `toxicZoneEnter2_2612876931` |

All six `HandlerTriggerState` instances have `_targetState=false` and exactly
those controlled IDs. The two pool controls share the input event but each owns
one zone (`DamageZone1` or `DamageZone2`); both exact name/ID pairs must be unique.
The eight continuous `HandlerDamage` instances also have
verified matching exit IDs and `INTERACTIVE_*_soft`/`*_deadly` configuration
identities; pool configurations are `INTERACTIVE_Labyrinth_water_01_soft` and
`INTERACTIVE_Labyrinth_water_01_deadly`. Four gun and two liquid `HandlerEffect` instances apply effects
independently from the damage handler; they must be gated as well.

Four verified one-action animators reproduce the switch's visible state:

- `Fire_zone_Animaton`: `Fire_zone_switch_01` sets `isGoing=false`.
- `Valve_Animation_Sync`: `set_valve_01` sets `HasValve=true`.
- `INTERACTIVE_Lifting_grate_01`: `LiftGate_843566315` sets `IsClosed=true`.
- `INTERACTIVE_Labyrinth_water_02`: `WaterRemove_2612876931` sets `IsRemoved=true`.

Each animator target belongs to that handler's own GameObject, and no extra
integer/float/trigger animation action is allowed by the contract. There are
five continuous sound handlers: one on each flame zone, two on `Gas_sound_01`,
and one on `INTERACTIVE_Labyrinth_water_02`. All autoplay, with empty play ID
and the matching flame/steam/pool stop ID. Their
native end fade is about 0.1 seconds. The liquid grate has an activation
one-shot, which this direct-state application does not emit.

## Runtime behavior and safety

Every effect requires active Arena, the exact started GameWorld/AbstractGame
pair, map `labyrinth`, a loaded exact scene path, and the corresponding native
component/name/trigger/configuration identity. Unknown content fails open to
vanilla. No shared configuration, templates, global health flags or profile
state is modified.

Before the first setup tick, narrowly targeted prefixes already block native
`HandlerDamage.OnTriggerStartDamage`, `HandlerShoot.OnShootEvent`,
`HandlerEffect.HandleEvent`, and the five sounds' parameterless `PlaySound`.
This prevents early entry events and later direct callbacks from restarting a
known hazard, including its independent health effect. Existing health effects
from other causes are never cleared; exit/finish/stop callbacks remain native.

The setup pass first validates the full contract from the existing
`LocationScene.GetAll<ITriggerEntity>()` registry, then:

1. Calls the six disable handlers directly to set twelve IDs in the current
   local emitter's ignore set. No global switch, placement, item-consumption,
   quest or bot event is emitted.
2. Calls `OnTriggerFinishDamage(null)` on the eight known damage handlers to
   stop chains that began before the Arena handshake.
3. Executes only the four verified native Boolean animator actions.
4. Reads each sound handler's exact `_playCoroutine` field (declaring type and
   `Coroutine` field type are validated first), stops only that start/loop
   handle, then calls native `StopSound()`.

The setup retries at most five times, one second apart, if the registry is
not ready or a native action fails. No per-frame scene enumeration continues
after success/final failure. Direct actions are explicit/idempotent, not
toggle events. Failure is logged; exact hazard guards remain in place while
unknown scene content stays vanilla. Shutdown removes the owned hooks.

## Audio review finding and correction

Native `HandlerPlaySoundSequenced.StopSound()` does not cancel a pending
start coroutine. It also schedules a `BetterSource.VolumeFadeOut` callback.
Forcing `OnLoopSourceFadeOut()` manually can re-enter it via
`BetterSource.Stop()` and clear the source twice. Independent adversarial
review caught this P1 before completion.

The final code does **not** manually release sources or call
`StopAllCoroutines()`. Stopping only the verified start handle preserves
existing end timers, including those from a switch activated before setup or
an earlier retry. Native fade/end callbacks release their sources normally.
The loop has a short native fade; a start/end one-shot may finish naturally.
With no end clip, an already silent one-shot source may stay owned until native
`OnDestroy`, as vanilla does. This is not a claim of instant pool reclamation.

Native evidence is under `../_decompiled/`: `HandlerTriggerState.cs`,
`LocalTriggersEmitter.cs`, `HandlerDamage.cs`, `HandlerShoot.cs`,
`HandlerEffect.cs`, `HandlerAnimator.cs`, `HandlerPlaySoundSequenced.cs`,
`BetterSource.cs` and `MonoBehaviourExtensions.cs`. The inspected scene has no
`FlameDamageTrigger`; no general fire-volume or global damage patch is added.

## Verification and handoff

Production-linked tests exercise exact hooks, normal/stale/foreign contexts,
early damage and health effects, all twelve ignored entries, animations, delayed
source cleanup, an already stopping sound, partial failure/retry, scene drift,
bounded scanning, option latching and shutdown. Seven source-copy negative
controls remove raid ownership, skip visuals, force recursive release, cancel
end timers, omit pool damage ownership, skip the drain, or reject the valid
shared pool control ID. Each must fail its intended assertion while production
source hashes remain unchanged.

The toxic-pool follow-up corrects an omission in the original scope: the two
`Luquid` zones belong to the lifting grate and cannot disable the separate toxic
pool. The installed pool has no `HandlerEffect`; no general health-effect
removal is needed. Its `IsRemoved` condition enters the native `REMOVED` state.
The removal clip's events only stop sound, and direct off-state application
does not emit the global water switch event or either drain one-shot.

Independent re-verification passed the installed-scene inspection and 384
production-linked assertions, with all seven deliberate regressions rejected.
Adversarial review checked duplicate pool control IDs, exact scene/raid gating,
pending damage cancellation, native drain ownership and sound cleanup; no
unresolved findings remain. Visible drainage and live contact remain GPU tests.

The long raids are documented separately in
`LONG-RAID-REVIEW-2026-09-16.md`, which supersedes the 2026-09-05 report; both
predate this change. This host has no GPU.
After restarting the client, test all four entrance types without relying on
respawn protection, then repeat after respawn/round changes. A successful
setup logs `Arena Labyrinth spawn traps disabled: 12 hazards ...` and
`toxic pool drain requested`. The animation is requested through the native
off-state; visible completion is not inferred from this log alone.
Normal Labyrinth raids and the disabled-option next-raid path must retain
vanilla traps. Local evidence and the explicit GPU handoff are recorded in
`tools/LabyrinthTrapChecks/GATES.md`.
