# Local Arena air control

2026-09-12. Adds gentle WASD steering during a jump or ledge fall. F12 Movement
contains `AirControlEnabled` (default true) and `AirControlDegreesPerSecond`
(default 60, range 0–180). Settings apply live; disabled or zero restores native
direction. Existing MovementEnabled also applies. No change to jump input,
double-jump availability, mantle limits, weapon state, gravity or stamina.

## Native seam and behavior

Installed SPT 4.1 Assembly-CSharp SHA256:
`EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
The complete decompilation and an independent Cecil check confirm:

- `JumpPlayerState.ProcessJumpMovement(float)` calculates native horizontal
  magnitude and lift, then calls `MovementState.LimitMotion(ref Vector3,float)`
  once before `MovementContext.ApplyMotion`. FallDown inherits this path.
- The native input changes speed relative to the original takeoff direction,
  but does not turn that direction. The new helper rotates only the outgoing
  XZ displacement, retaining its current native magnitude and exact Y value.
- A minimal transpiler replaces only that unique call with a stack-compatible
  wrapper, retaining instruction labels and exception blocks. Missing/ambiguous
  binding disables this optional feature. The wrapper always calls the native
  limiter once, including outside Arena. Clipped movement is never restored;
  native limiter exceptions remain visible. Existing Arena motion scaling and
  the native physical controller still run afterward.
- Player.Move is the nontrivial native input entry point. A prefix records its
  state and a postfix admits input only if the original ran and the same state
  is still current. Input must belong to the current rendered frame and exact
  current Jump/Fall state. Native ignored axes, blind fire, inventory and Arena
  or native menu ownership deny a new turn. The helper never reads raw keys.

Private heading accumulates at a bounded angle per elapsed second; otherwise
each frame would merely offset the takeoff direction. Releasing WASD retains
the achieved heading. Zero horizontal displacement receives no new impulse.
Native forward/backward speed modulation still applies, so this is not Quake
strafe acceleration. Long steps cap turning time at 0.1 s; invalid or >0.25 s
steps leave native displacement intact. Turning can be slower under frame stalls.
At >0.25 s the native-direction fallback can briefly interrupt the stored heading;
the next valid step resumes it. No claim of seamless steering across such hitches.

## Scope and lifecycle

Uses the existing actual ArenaMovementScope: exact armed/active game and world,
Started local game, live current local body, no bot, ordinary raid, preview,
spectator, stand-down or respawn. Only exact native JumpPlayerState/FallDown
types are admitted; pushing/bumped phases, grounded samples, prone, stationary
weapon, mounting, state/control overrides, sliding, vault animation and pending
mantle requests are excluded. No global native configuration is changed.

The per-context weak table also verifies body/game/world identities; its game
and world references are weak so a pooled context cannot retain an old raid
through this helper. Heading survives Jump/Fall and double-jump transitions.
Reset requires actual controller contact and descending/landed phase, rather
than only the native proximity ray. Shutdown disables surviving detours before
unpatch and clears state. Optional-hook failures fail open and warn once.

## Verification and limits

`tools/ArenaAirControlChecks` links the production helper and movement scope;
Unity, Harmony registration and the limiter are I/O doubles. The checks cover
bounded accumulation at multiple frame rates, exact vertical preservation,
planar magnitude, collision ordering, input freshness, scope and lifecycle.
Compiled faulty copies must fail behavior assertions, rather than only fail
compilation. Installed contracts separately check native signatures, collision
stack/order and inherited FallDown integration. Neither runs Unity animation,
actual collisions, native input update order or third-party Harmony composition.
Final root run: **155 assertions / 21 rejected compiled faults / fresh 155**,
recorded in `artifacts/air-control-checks/c9427c6abea84a42819930b99fd33946/`.
Installed contract check passed. Independent production and harness review found
no remaining established P0–P2 after the scope, input and oracle corrections.

Independent adversarial review additionally checked native local Auto body
update and Update input delivery. No C# ordering blocker was found, but the live
Unity Animator and other mods still require GPU acceptance. Test clips should
compare the same running jump with the setting off/on, then release input,
reverse direction gently, fall from a ledge, double jump and approach a wall.
Check respawn, inventory/pause, conservative mantle and ordinary PMC behavior.
Do not infer acceptance from the startup marker alone.

Ready: `Arena air control: native jump/fall steering ready (planar speed preserved).`
Failure: `Arena air control unavailable: ...`.
