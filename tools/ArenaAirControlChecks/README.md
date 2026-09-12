# Air control checks

Run `pwsh -NoProfile -File tools/ArenaAirControlChecks/Verify-Contracts.ps1`
against the actual SPT installation, then `pwsh -NoProfile -File
tools/ArenaAirControlChecks/Verify.ps1`. Neither builds/deploys the mod or changes
the installation. Production ArenaAirControl and ArenaMovementScope are linked;
Unity transforms/collision and Harmony registration are I/O doubles only.

The final 2026-09-12 root run passed **155 assertions and 21 compiled faults**,
then the unmodified 155 again. Recorded output:
`artifacts/air-control-checks/c9427c6abea84a42819930b99fd33946/`.
Faults must fail at their intended runtime assertion, not compilation. Positive
coverage includes frame-rate independence, left/right and shortest-angle turns,
bit-exact Y, native speed, collision clipping and exception propagation, stale
input/state, menu persistence, real ground contact, game/world/body replacement,
optional-hook rollback and failed shutdown. No invented steering calculation
is substituted for the production helper.

Independent review strengthened the tests: both directions across ±180°, exact
Y instead of approximate comparison, attained heading during menu ownership,
and fresh input attempted after a rejected scope transition. The latter caught
an initially surviving WrongRaid fault: without fresh input, Observe's correct
sample invalidation concealed the scope defect. The final verifier rejects it.

The installed Cecil check separately validates the unique native collision call,
its argument stack and placement before ApplyMotion, native LimitMotionXZ
delegation, Player.Move and CheckFlying signatures, fields and FallDown inheritance.
The arranged Move/CheckFlying/ProcessJumpMovement spy sequence is a modeled input
order, not execution of native Animator code or rewritten IL. Actual Harmony
composition, Unity update timing and collision geometry need GPU testing.
See AIR-CONTROL-REVIEW.md and the current HANDOFF for delivered build and test job.
