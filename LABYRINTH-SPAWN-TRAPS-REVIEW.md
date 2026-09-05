# Labyrinth entrance traps: Arena-only native disable states

`F12 / Arena / DisableLabyrinthSpawnTraps` defaults to true and is sampled
once per confirmed Arena raid. A change applies to the next raid; disabling
the option must not re-arm a trap around a living player in the current raid.
The feature covers the ten entrance traps: four gun traps, two flame traps,
two liquid/grate traps and two steam traps. It does not remove random spike
traps, trapdoors, the separate water puzzle, map-wide fall protection, quest
objects, or the center alarm's independently configured sound suppression.

## Installed scene contract

`globalgamemanagers` maps `level552` to
`Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity`.
SHA256: `d39b3c4606d51fe9699319ef1da4f671f129843c1bcd7bc8f23efd68436bbfeb`.
Read-only inspection with UnityPy 1.25.3 is reproducible using
`tools/LabyrinthTrapChecks/Inspect-InstalledScene.py` and the installed
`EscapeFromTarkov_Data` directory. No game assets/audio are copied or edited.

| Trap | Native off control | Controlled damage/shot entries |
|---|---|---|
| Gun | `disable_traps_01` | `guntrap_trigger_1628276106`, `2131128863`, `1446364582`, `2505672369` (same prefix) |
| Flame | `disable_traps_02` | `flame_enter_zone_493925125`, `flame_enter_zone_1349880479` |
| Liquid | `disable_traps_04` | `Luquid_enter_zone_3486811782`, `Luquid_enter_zone_2773347541` |
| Steam | `set_valve_01` | `steam_enter_zone_115583983`, `steam_enter_zone_3704346085` |

All four `HandlerTriggerState` instances have `_targetState=false` and exactly
those controlled IDs. The six continuous `HandlerDamage` instances also have
verified matching exit IDs and `INTERACTIVE_*_soft`/`*_deadly` configuration
identities. Four gun and two liquid `HandlerEffect` instances apply effects
independently from the damage handler; they must be gated as well.

Three verified one-action animators reproduce the switch's visible state:

- `Fire_zone_Animaton`: `Fire_zone_switch_01` sets `isGoing=false`.
- `Valve_Animation_Sync`: `set_valve_01` sets `HasValve=true`.
- `INTERACTIVE_Lifting_grate_01`: `LiftGate_843566315` sets `IsClosed=true`.

Each animator target belongs to that handler's own GameObject, and no extra
integer/float/trigger animation action is allowed by the contract. There are
four continuous sound handlers: one on each flame zone, two on `Gas_sound_01`.
All autoplay, with empty play ID and a matching flame/steam stop ID. Their
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
`HandlerEffect.HandleEvent`, and the four sounds' parameterless `PlaySound`.
This prevents early entry events and later direct callbacks from restarting a
known hazard, including its independent health effect. Existing health effects
from other causes are never cleared; exit/finish/stop callbacks remain native.

The setup pass first validates the full contract from the existing
`LocationScene.GetAll<ITriggerEntity>()` registry, then:

1. Calls the four disable handlers directly to set ten IDs in the current
   local emitter's ignore set. No global switch, placement, item-consumption,
   quest or bot event is emitted.
2. Calls `OnTriggerFinishDamage(null)` on the six known damage handlers to
   stop chains that began before the Arena handshake.
3. Executes only the three verified native Boolean animator actions.
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
early damage and health effects, all ten ignored entries, animations, delayed
source cleanup, an already stopping sound, partial failure/retry, scene drift,
bounded scanning, option latching and shutdown. Four source-copy negative
controls remove raid ownership, skip visuals, force recursive release, or
cancel end timers; each must fail its intended assertion while production
source hashes remain unchanged.

The previous long raid is documented separately in
`LONG-RAID-REVIEW-2026-09-05.md`; it predates this change. This host has no GPU.
After restarting the client, test all four entrance types without relying on
respawn protection, then repeat after respawn/round changes. A successful
setup logs `Arena Labyrinth spawn traps disabled: 10 hazards ...`.
Normal Labyrinth raids and the disabled-option next-raid path must retain
vanilla traps. Local evidence and the explicit GPU handoff are recorded in
`tools/LabyrinthTrapChecks/GATES.md`.
