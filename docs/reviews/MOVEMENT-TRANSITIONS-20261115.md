# Jump and slide transitions

Source contract, 2026-11-15. Original implementation against native EFT and Arena's
existing movement code. No code or assets from SAM, Aero's Movement or Air Strafing
were imported. Source checks are separate from Unity/Mono acceptance.

## Scope

Only the exact living local body of the admitted, started Arena raid. The Fast
Movement master remains authoritative; normal raids, AI, foreign/stale bodies,
inventory, respawn and spectator input are excluded. Existing Slide and Airborne
Hip Fire toggles control their features. No new menu text or settings.

| Transition | Contract |
|---|---|
| Fresh jump tap | Clear only an expired native vault timer whose vault input is no longer held. Held/pending/entered vault keeps priority. |
| Walking off an edge | One grace request within 150 ms of actual controller contact, at first-jump strength. A ray reporting nearby ground is insufficient. |
| Extra jump | Grace does not spend or refill the separate Double Jump credit. Ordinary takeoff disarms grace. Jump Boots is applied once per impulse. |
| Air crouch → slide | A fresh crouch within 350 ms before physical landing can start a slide once a supported native landing/ground state owns the body. Explicit uncrouch cancels it. |
| Sprint → slide | Also admit the exact `SprintToIdleTransitionState` instance whose `PreviousState` is the freshly sampled native `SprintPlayerState`. Bind before either component Tick or motion validation can discard the exit; an arbitrary Transition state is insufficient. |
| Slide movement | Replace horizontal root motion after the existing speed scale; preserve native Y and one controller move. Measure accepted movement and permanently reduce carry at obstructions. |
| Slide → jump | Check native headroom and synchronize the native standing pose immediately, including its smoothed animation level. Native jump entry owns lift and the one jump debit. Seed only horizontal jump memory, dividing out the existing motion scale. |
| Jump during sprint-exit slide | Reserve one fresh press for at most 1 second only in that exact bound sprint exit. Wait for native Idle/Move/Sprint, then attempt native standing and dispatch through the existing jump owner once. |
| Jump during landing slide | The exact `JumpLandingState` retains its separate 500 ms maximum. Both queues capture the deadline at the first press; repeated input never extends it. No forced animator transition. |
| Slide → normal movement | Blend the remaining accepted slide velocity into native motion over 200 ms. |
| Sprint-jump hip fire | Restore first-person firearm authority only in the actual native sprint-jump leaf/blend and ready idle/firing operations. |
| Airborne aiming | Refuse ADS through both idle input and the native aim setter, including active automatic fire; clear already-held ADS at airborne state entry. |

## Native weapon cause

`SprintPlayerState.Exit` already clears sprint on the body and firearm. The
remaining lowered weapon comes from `Player.VisualPass`: the sprint-jump clip's
`Weapon_3rd` curve blends the first-person weapon toward the body's third-person
weapon root. The installed `Jump_*_Sprint` clips hold that curve at 1.

Arena substitutes only that single visual curve read, returning 0 while the
scoped ready firearm owns the genuine airborne sprint jump. Idle/moving jump
clips, third-person presentation, reload/draw/holster, launcher, malfunction,
blindfire and mantle use their native presentation. No animation parameter or
transform is stored or restored; movement and native recoil remain authoritative.

The airborne ADS guard retains the full native `IsAiming` setter and its side
effects, coercing a denied true request to false. Ground Sprint-to-ADS uses an
exact-controller synchronous bypass, restored even on exceptions.

## Ownership and review traps

- Native MOVE → Jump requires animation `Level > 0.6`, not merely logical
  `PoseLevel > 0.6`. The existing 250 ms entry reservation expires before gradual
  standing reaches that level. Only an admitted slide jump uses native immediate
  standing; its equal-logical-pose early return is covered by an explicit native
  headroom check and synchronization of the native smoothed-pose property.
  Physical maximum pose remains authoritative. This uses native force-pose cost
  semantics: no gradual standing-pose stamina or PushUp skill accumulation;
  native jump stamina is still charged once by Jump entry.
- `ArenaDoubleJump` remains the sole jump-command owner. Native translation runs;
  no global input filter swallows the jump or changes the configured binding.
- Contact witnesses are published only by a successful native motion transaction,
  including intermediate SimpleCharacterController contact. Delayed observation
  must retain the original contact time.
- All pending intent binds player, controller, context, world, game and input owner.
  Invalidation clears owned requests even when new input is currently blocked.
- A native landing sprint can reset pose to standing. That automatic reset must
  not masquerade as an explicit uncrouch and erase fresh crouch intent.
- Native Sprint entry can also restart requested XZ motion at zero. Bind the
  successful physical-contact velocity separately for the existing landing
  deadline. Later requested moves may only reduce this reserve by their actual
  collision loss; zero requested motion cannot replace it with a fabricated
  pre-collision speed. Consumption, cancellation and owner reset clear it.
- Native motion can enter the sprint exit before the component Tick. Observe the
  exact predecessor/instance binding at both boundaries, including when crouch
  already started the slide while the state was still Sprint. An existing exit
  binding cannot be replaced or have its timestamp refreshed by another instance.
- Buffered slide jumps bind the slide and input epochs, expire on clock rollback
  or deadline, and cancel when the supported owned slide ends. Clear the ticket
  before callbacks; recheck native headroom, pose, ground, stamina and vault guards
  before dispatch. Repair an unheld stale vault timer only after standing succeeds.
- A new slide launch clears the previous air-control heading. The final grounded
  bit may be false after an intermediate landing contact; retaining that heading
  would redirect the slide jump without input.
- Native exceptions propagate; no fake grounded bit, forced animator state,
  collision bypass, repeated stamina debit or extra airborne displacement.

## Verification

Focused production-linked checks: `JumpReliabilityChecks`, `DoubleJumpChecks`,
`SlideTransitionsChecks`, `ArenaAirFireChecks`, `ArenaAirControlChecks` and
`BunnyhopChecks`. Native IL contracts cover the existing controller and firearm
targets. The shared runner selects affected checks and guards the installed files.

Native acceptance is recorded separately in the
[dated GPU report](../reports/2026-11/GPU-MOVEMENT-TRANSITIONS-20261115.md), bound
to each loaded candidate. Earlier failed candidates are diagnosis evidence,
not acceptance of the final source. The following table distinguishes the main
cases from boundaries that need their own evidence.

| Feature | Mandatory main case | Separate boundary evidence still required for full acceptance |
|---|---|---|
| Fresh jump | An eligible fresh tap reaches native jump entry; if claiming stale-timer recovery, observe the unheld expired timer before the tap. | Held/pending/entered vault remains authoritative; no second command owner or duplicate debit. |
| Coyote | Walk off a real, recorded ledge without a preceding jump or autojump; prove physical contact, then ungrounded input within 150 ms and an upward impulse with Double Jump disabled. **No native ledge evidence is currently recorded here.** | Same ledge after expiry produces no grace impulse; repeat cannot consume grace twice; ordinary takeoff disarms it and separate Double Jump credit remains unchanged. NavMesh routing or camera movement alone is insufficient. |
| Ground slide → jump | On a supported flat route, record actual slide ownership, fresh Jump, successful native stand, Jump entry and upward motion. | Exact Sprint → SprintToIdle exit survives motion-before-Tick ordering; early Jump waits through the exit, dispatches once within the 1-second bound and retains accepted horizontal carry. |
| Air crouch → landing slide → jump | Record real descent/contact, queued crouch, active landing slide and a fresh Jump that reaches native ground locomotion and takeoff. | The landing queue remains bounded to 500 ms; repeated input cannot extend either queue; explicit uncrouch, vault, lost support and head obstruction cancel or refuse it. |
| Airborne firearm | Visible sprint-jump weapon posture and actual shots; ADS refused while idle and firing in air. | Ordinary jump presentation, normal grounded ADS, ground Sprint-to-ADS, and native reload/draw/holster/malfunction presentation remain intact. |
| Lifecycle and isolation | Verify loaded identity and cleanup of owned requests after the tested raid. | Inventory, respawn/body or input-owner replacement, feature/master toggles, ordinary raid and AI isolation each need scoped evidence; offline checks do not establish native acceptance. |

Collision/headroom and accepted-motion carry need their own bounded scenarios;
success on open flat ground does not cover them. The pre/post whole-profile
comparison and PitFireTeam hashes remain mandatory in the root-owned native report.
