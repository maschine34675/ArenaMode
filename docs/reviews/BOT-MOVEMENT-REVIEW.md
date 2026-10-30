# Arena bot locomotion speed

2026-09-15. The existing movement bonus was local-player-only: current defaults
multiply walk/strafe displacement by 1.25 * 1.1 = 1.375 and sprint by 1.3 * 1.1 =
1.43. Local infinite stamina, weight limits, max skills and inertia changes also
exclude bots. Bots already receive Arena awareness/search/personality/pursuit
changes; their behavior is not vanilla, but they had no Arena displacement bonus.

## Controls and scope

F12 section **Bot movement** adds independent live controls:

- `BotMovementEnabled`: true by default; independent of the local movement switch.
- `BotWalkSpeedMult`: 1.25, range 1 to 1.5.
- `BotSprintSpeedMult`: 1.30, range 1 to 1.5.

Multipliers apply equally to hostile bots and friendly bots, including PFT
followers. They scale the bot's current motion after existing health, stance,
weight and AI speed choices. They do not grant local-player stamina/skills,
force sprint, speed up aiming/firing/reloading, or alter AI decisions. A stopped
bot stays stopped; this feature cannot fix a stalled search decision or route.

Only exact currently registered, living Active BotOwner/Player/MovementContext/
controller identities in the current live Arena match qualify. Ordinary raids,
local/other human players, menu models, replaced bodies, inactive bots, pending
area/team-start failures and non-live match phases receive no bonus. The four
existing mode live-phase properties are reused without adding timing rules.

## Native movement boundary

`ArenaBotMovement` prefixes `MovementContext.DirectApplyMotion(Vector3, float)`.
Native normalized target speed is clamped and the animator's root-motion blends
saturate; increasing target speed alone previously gave the local player no
visible sprint increase. This adapter instead scales X/Z before native
`CharacterController.Move`, without bypassing that call or changing Y/gravity.

The SimpleCharacterController applies its own speed limit after collision. The
adapter temporarily scales a positive limit by the effective movement factor.
It never replaces zero/negative limits with an unlimited sentinel. The finalizer
restores the captured controller, including after a native exception or body
replacement, but does not overwrite a different value another writer installed.
Same-context nested calls cannot apply the bonus twice. No per-bot/global speed
state persists after this bracket.

The alternate native BotMoverImpostor accounts for no more than 0.3 m per update.
The total motion, including unchanged Y, therefore limits only the added bonus to
that size. An already larger native step stays exactly native. Low FPS or steep
vertical motion can reduce/remove the bonus; the feature does not relax native
navigation checks or enlarge that step ceiling.

Run and Sprint on the ground qualify. Prone, mounted/stationary weapons, platform
motion, jump/fall/climb/vault and interaction states do not. Native door flags
and the native NearDoor mover state suppress the bonus. On a currently owned
native path, the bonus fades from full at 2 m to zero within 0.75 m of the current
corner. These are bonus limits, not new movement/turn/stop commands.

## SAIN compatibility

SAIN overrides `MovementContext.IsAI` to false, so admission uses actual
`Player.IsAI` and owner/body identity. SAIN also stops/pauses the native mover and
drives Player.Move itself; a native path, moving flag or unpaused mover is not a
general requirement. Native-corner data is used only when that native path is
active, never as a replacement for SAIN's steering.

SAIN has a separate door handler, which may move backwards while temporarily
ignoring a door collider. `ArenaBotMovementSain` reads the exact installed SAIN
4.5.1 component/owner/player/door identity and Interacting property. It changes
no SAIN settings, method results, collider state or stored object. Missing SAIN
permits the native path; an unsupported SAIN version/contract or unproved door
state conservatively leaves bots at their original speed and warns once.
Bots with no SAIN component can still use the native path.

Corner slowing already chosen by SAIN remains in the input motion; the boost
does not know every SAIN route corner. Its conservative per-step bound does not
replace a real crowded-doorway/close-corner test. Some foot sliding is expected
because the leg animation is deliberately not accelerated with the displacement.
The adapter allocates a temporary motion lease for a boosted call and uses
reflection for the optional SAIN state reads. Their cost under a crowded live
raid is unmeasured; offline checks are not a performance result.

## Validation and acceptance

`tools/BotMovementChecks` links the real adapter, uses explicit native boundary
doubles for simulation and checks the installed method/property contracts. It
exercises actual displacement/cap behavior, excluded identities/states/settings,
door handling, native-vs-SAIN movement ownership, corner/step limits and cleanup
through failures. Compiled faults must be rejected behaviorally. Relevant
existing local movement, TDM search and team pursuit checks run independently.
The SAIN contract check reads installed metadata; no third-party code is bundled.
An independent source-linked helper test also exercises the real reflection
binding and door/owner admission against an explicitly synthetic SAIN assembly.
The 2026-09-15 checks pass 126 motion assertions, 43 SAIN assertions and four
compiled fault controls. Six existing movement/search/pursuit harnesses pass
832 assertions. Independent native and adversarial reviews found no remaining
concrete blocker; native runtime and performance acceptance remain outstanding.

Final counts, native build and deployment identity belong in the dated HANDOFF
entry. Source-linked/metadata checks and a clean native build do not prove live
Harmony execution, navigation quality or visual speed. User-run GPU checks:

1. Compare a moving bot in a clear corridor with both factors at 1 and defaults;
   toggle the bot switch during the raid. Player movement must not change.
2. Observe native/SAIN sprint, strafing, braking, narrow corners and doors in TDM,
   plus both teams in PFT 5v5 before and after player death and round rebind.
3. Check no extra fall/climb/prone/platform movement, no standing-bot drift and
   no new obstruction loops. At low FPS, a smaller bonus is intentional.
4. Run a normal PMC raid: no bot boost or Arena movement diagnostic should occur.
   The first eligible motion observation logs the requested factors once per raid;
   it is activation evidence, not a measured traversal-speed result.

Only the client needs updating/restarting. Server, Core, profiles, map contours,
spawn points and third-party configurations are unchanged.

## 2026-09-28: separate SAIN tempo adapter and bounded diagnostics

`ArenaBotTempo` adds the approved search/reload-tail/combat-strafe behavior under
the same master switch; see `BOT-TEMPO-REVIEW.md` for exact ownership, thresholds
and exclusions. This does not relax the motion factor, corner or step limits.
The shared `IsLiveBot` entry point reuses the existing exact admission above.
Motion now summarizes boosted/corner-limited/step-limited calls at most once per
60 gameplay seconds/world. Counts overlap and do not measure actual travel speed.
Additional source-linked tests check admission reuse and diagnostic cadence.

## 2026-09-28: starts, direction blending and remaining weight inertia

After user acceptance of the preceding tempo change, `ArenaBotAgility` adds
per-consumer acceleration/direction tuning and reduced weight-related locomotion
inertia. See `BOT-AGILITY-REVIEW.md` for baseline math, native zero-value limits,
scoped operand substitution and acceptance. Native displacement/collision limits
are not relaxed. Bot overweight thresholds were already 9000–10000 kg; this work
does not raise them or modify inventory, stored Physical fields or shared curves.
