# Timed Arena Bunnyhop

2026-10-05. User-approved implementation; native evidence is recorded separately.

## Contract

`Movement / BunnyhopEnabled` defaults off and shares its value with the visible
`BUNNYHOP` mutator. No automatic hold-to-hop and no additional tuning sliders.
The existing Jump binding is used (including a release binding); press/release
again near the landing. A descending jump with a walkable native ground hit
within 0.45 m and within the next 150 ms may reserve that input. It takes
precedence over DoubleJump only there; other airborne inputs still DoubleJump.
A late ground input may join the chain within 120 ms of actual contact.

Initial flight tracking starts only on a native Idle/Move/Sprint-to-Jump entry
with context groundedness. SimpleCC's last depenetration bit can be false at
that entry even on a flat floor (observed on GPU-2); seeding alone grants no
extra jump or carry. A chained hop additionally requires witnessed actual
controller contact after leaving the ground. SimpleCC resets that bit on each
substep: an exact-controller SingleMove postfix retains a physical contact pulse
only within a successful native DirectApplyMotion transaction. The next Tick
can consume it for at most 100 ms. Once contact is latched, fresh native grounding
and an upward ground hit are required throughout the pending landing/dispatch;
the transient depenetration bit is not required again. Losing that support
immediately cancels the request. Proximity alone cannot create a landing. The
physical timestamp also defines the buffer window, independent of observer delay.
DoubleJump shares this committed contact witness to renew its single credit;
upward/pushing states, stale/foreign samples and an unfinished move are excluded.
The
native landing animation finishes normally: there is no teleport, forced
animator state, fake grounded flag or extra stamina debit. Wait at most 500 ms
after contact for Idle/Move/Sprint, then dispatch one `Player.Jump()` and allow
250 ms for native entry. Player's vault guard, native collision, injury,
stamina and surgical restrictions remain authoritative. Holding Space retains
the existing vault behavior. Native Jump Enter applies the normal jump height
and Jump Boots; Bunnyhop never changes vertical motion.

Each successful hop carries applied horizontal speed plus 6%, at most 125%
of the chain's initial measured speed and at most 12 m/s. AirControl still
owns steering; Bunnyhop does not add Quake-style strafe acceleration. The
pre-collision displacement is divided by the SAME scale that DirectApplyMotion
later applies. A single existing controller-speed bracket accommodates the
carried motion; no nested second bracket or global speed mutation is added.

Actual DirectApplyMotion position delta measures collision-reduced speed.
Severe obstruction discards the chain; smaller losses reduce carry permanently.
Platform and asynchronous Impostor motion are excluded. Invalid/long frame
deltas (>100 ms) fail open and discard momentum. The chain ends on a missed
input, drop >0.35 m below takeoff, vault/slide, medical/input restriction,
death, body/world/game replacement, setting change or raid end. Per-frame
owner cleanup also retires a corpse that no longer receives movement ticks.

## Native evidence

`JumpLandingState` inherits a no-op Jump. The installed standard
`Player_anim_controller` (object -5637414435831338160) has no landing-to-jump
edge and no AnyState edges. Land_Move normally exits toward MOVE at 0.85 of
~0.4 s clips; idle landing is 0.5 s with exits at 0.5/0.85. Sprint jump lands
directly into Sprint/Move. Bundle SHA256:
`9A6B93737D806D6A563BD1AFCBDFB77538DFCCE917C9D1DB0E2BE2E1F39CDD9F`.

`MovementContext.CheckFlying` invokes Player.LandingAdjustments with apex-to-
ground distance. EFT already applies a Fall speed limit above 0.3 m, so merely
seeing that limit must NOT cancel ordinary hops or Jump Boots. Only a fall
below the tracked takeoff floor cancels; native fall damage and Fall limit
writes themselves are not changed. OnJumpEnd retains all its native behavior.

`TryJump` emits noise before writing IsJumping. A synchronous cancellation
during that callback must release the later bool too, while never clearing a
different/native-entered state. Pending request cleanup is identity-bound and
the native motion finalizer returns its original exception unchanged.

## Independent review and verification

Read-only native-contract review verified the actual installed controller and
native stamina/dispatch paths. Adversarial review found and confirmed fixes for
ordinary-fall cancellation, synchronous signal cleanup and stale-body retention.
The UI keeps the accepted thumbnail and READY geometry: six paired rows plus
a compact Bunnyhop footer. Final gameplay/visual observations and build hashes
are tracked in the GPU report/handoff rather than inferred from mocks.

`tools/BunnyhopChecks/Verify.ps1` compiles production Bunnyhop, DoubleJump,
MovementScope and JumpBoots against narrow native doubles. It exercises speed
caps/scaling, contact, timing, interruption and real DoubleJump admission;
compiled negative controls prove decisive assertions can fail.
`Verify-Integration.ps1` also runs existing DoubleJump, AirControl (including
installed IL contracts), handling and mutator-menu checks.

Required native cases: sprint and walking chains, missed/early timing,
DoubleJump away from ground, vault priority, walls/slopes/steps, Jump Boots,
master/mutator off, ordinary PMC control, respawn and clean protected exit.
Offline checks cannot certify animation timing, terrain or subjective feel.
