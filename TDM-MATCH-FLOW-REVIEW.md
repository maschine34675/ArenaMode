# Independent Team DM match flow

2026-09-08. Authorized after the latest diagnostic playtest did not reproduce the
previous sampled invalid path. This feature does not attempt a navigation fix.

## Rules and presentation

Independent TDM now freezes two client settings when its match is created:
50 team kills and 600 seconds by default. Either value can be zero to disable
that limit; both zero retain unlimited play. The native map screen shows kill
presets Off/25/50/100 and time presets Off/5/10/15/20 minutes inside the existing
mode panel. F12 permits 0–1000 kills and 0–7200 seconds, displays custom values
honestly, and changes during a raid affect the next match. There is no server
schema/settings route change. PFT and other modes do not show these controls.

Assembly requires every bot slot Active, the current local player alive, no
player replacement in progress, and the initial kit swap settled. A three-second
countdown then precedes combat scoring and match time. Loss of readiness,
unreadable roster, local death or body handover invalidates that countdown;
recovery requires all three seconds again. The existing damage/armor/contusion
prefixes protect owned TDM bodies during assembly/countdown and after results.
Movement is still permitted; this is not a new global bot pause or positioning
lock. Direct native Kill and non-damage health mutations are not globally patched.

The first enabled limit reached ends scoring. Time is measured from actual
countdown release using the simulation clock, excluding assembly and pauses.
At a time limit team kills decide win/loss/draw. A score-bearing event checks the
deadline before mutation; the winning kill checks the limit again after scoring.
Give-up awards only its existing opposing team credit, never a synthetic personal
kill or award. Local death is counted once before a final local-victim result,
regardless of native death/kill event order. Post-result events cannot mutate the
scoreboard or publish respawn updates.

The common banner displays assembly, countdown and result. The existing scoreboard
opens at result once native body replacement is no longer running, and stays
closable with Escape/F9/Close. Its footer offers Return to menu instead of Give up;
the give-up hotkey requests this same exit after a result. Result display lasts
15 unscaled seconds before automatic protected exit. Victory/defeat reuse the
licensed bundled match clips. No draw clip is bundled; draws have neutral DRAW
text/toast rather than an incorrect win/loss announcement. HUD-off still permits
the existing scoreboard and result toast; no second UI renderer is introduced.

## Native lifecycle safety

The terminal result immediately stops population requests. Late arrivals pass
the existing rejection/administrative retirement path, never refill or restart
the match. A final death remains intercepted: terminal HoldRespawn prevents a
new body, including the previously open race after asynchronous kit preloading.

An already-entered Redeploy pipeline cannot be canceled through its public API:
it destroys the old owner before awaiting player creation. It may finish one
physical replacement after the result; gameplay/scoring do not resume. We wait
on `RespawnEngineState.IsPipelineRunning`, not the held death driver's flag.
If that pipeline fails, the TDM result retains interception and bypasses the
generic area-abort/death-screen fallback. Only the result exit coordinator may
stop its exact captured game/world, with the immutable raid profile ID, a matching
native owner/current local/world body, and no active native pipeline. A stopped
simulation defers native Stop because its fade callback needs deltaTime.

Immediately before intentional Stop, StandDown releases death/black-screen
interception. A precommit failure restores interception and may retry; after
EFT commits Stopping no second Stop is issued. The existing protected server end
and profile snapshot/restore remain unchanged. Owner-unavailable failures stay
visible and log on problem changes rather than attempting forced profile/owner
repair. A native factory failure that never restores its owner may still require
manual recovery; this change does not pretend that a timed-out native Task was
canceled or that its owner is safe.

Native anchors independently reviewed: `EFT/BaseLocalGame.cs` Stop and GameEnd;
`PreloaderUI.cs` Co_ShowBlackImage; `Redeploy.Core/Engine/RespawnEngine.cs` owner
destroy, asynchronous creation, failure/rewire and pipeline flag; Arena's current
death driver and all existing combat mutation prefixes. Third-party sources/DLLs
were not copied or bundled.

## Verification and delivery

See `tools/TdmMatchChecks/GATES.md` for current measured outcomes. The new verifier
links actual policy and exit coordinator, and extracts the actual driver preload
hold, terminal failure branch and death-interception methods on each run. Native
I/O doubles are independent, not proof of Unity runtime behavior. Both existing
runtime and give-up suites link the new partial mode/policy; the latter also
executes the actual ArenaMatch. Menu tests link Settings and both native view/state
implementations. Deliberate source faults must fail the intended assertion, not
just compilation. The shared Tactical banner retains its previous regression set.

Independent review corrected countdown invalidation, terminal pipeline failure
fallback, and paused native exit. Final counts/build hashes are recorded in the
handoff after verification and delivery. No native GPU raid or visual test is
claimed by the offline checks.

## GPU acceptance

Restart the game after matching the delivered client hash. The server/Redeploy
are unchanged. Inspect the largest Customs area/team-size menu and switch between
Team DM, Tactical/PFT, other modes and maps: no overlap, stale controls or lost
custom values. Compare native 1080p and the actual test resolution.

Test a short time-limited match (F12 TimeLimitSeconds=30, KillLimit=0 before loading
is available for quick checks), then a small kill target, and both Off. Verify
no damage/score during assembly/countdown, timer starts at release, refills while
Live, immutable final score, announcer, closable final scoreboard and native exit.
Include win/loss/draw, give-up at the final opposing point, last hit killing the
local player, match time expiring during preload/respawn, and an ordinary Escape
exit. Never force a body factory failure in the main profile.

After each exit confirm protected profile restore/persistence in both logs; a
normal PMC raid and PFT Tactical remain separate regression checks. These are
acceptance instructions, not reported GPU passes.
