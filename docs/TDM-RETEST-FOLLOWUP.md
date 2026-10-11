# GPU retest follow-up, 2026-09-08

Baseline report: `55164e4`, testing client source `e2859e8` (SHA256
`DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`).
Raw evidence is ignored under `artifacts/gpu-acceptance/2026-09-08_125900-retest/`.
The report establishes full initial rosters for both factions at every new
size, corrected faction labels, sampled rendering/menu/double-jump behavior,
and eleven successful protected Arena profile restorations. It does not establish
consistent bot combat or acceptable preparation latency.

## Source-supported corrections

The quiet runs contain no area path rejection or awareness exception diagnostics.
Do not blame the new connection graph on that evidence. The optional SAIN bridge
could supply a position without a first seen/heard contact, leaving search
eligibility dependent on unrelated contact, squad timing or personality choices.
See `TDM-AWARENESS-REVIEW.md` for the exact installed contract, guarded search cue
and diagnostics. This addresses a concrete missing transition; only a new live
test can establish whether it explains each previously quiet bot.

Initial preparation is the measured spawn bottleneck. BEAR 5v5 reached its full
roster after 423.098 seconds and discarded five completed cold preparations before
dispatch. BEAR 4v4 took 228.347 seconds and discarded another preparation. USEC
5v5 took 114.328 seconds without an expired request. Area preparation took around
one second; native arrivals followed each logged dispatch plan in tens of
milliseconds. See `TDM-PREPARATION-REVIEW.md` for separate request phases, retained
ownership, timeout limits and exact tests. This does not promise to eliminate
the native cost of preparing unfamiliar bot equipment.

Optional full-kit catalog warmup now waits for the current TDM initial roster,
active native kit-pool loading and player replacement. It previously started eight seconds after raid activation,
alongside bot pools and sometimes the same cold player kit. Deferring that work
removes this particular overlap; the logs do not quantify its share of loading
time. Required per-life `KitBundlePreloader.Preload(kit)` is untouched. Other
modes retain their schedule, fixed kits still skip catalog-wide warmup, and
protected PFT never starts it. An epoch-scoped counter tracks active native pool
tasks, including the opening live swap before the respawn pipeline flag is set.
Faults, cancellation and late completion after reset release only their own
epoch; disposing the polling coroutine does not claim its task has completed.
This is not a global pool-priority change: native
low-priority pool creation does not provide the awaited completion contract this
preloader requires.

## Findings that do not justify a patch

The first 21.52-second USEC 5v5 player respawn followed a fixed-kit switch from
Conscript to cold Vanguard. Native asset loading and pool creation took 20.00
seconds; the remaining replacement pipeline took 1.52 seconds. Later same-kit
respawns completed in 1.55/1.56 seconds. There is no retry/timeout failure here.
The log's 22 "bundles" counts item templates, not distinct physical bundles.

Two Unity archive-close errors occurred at 16:36:59.411/.419 during a map-menu
transition, before Arena arm at 16:38:45.859. Arena's own character preview had
loaded about twenty seconds earlier and releases its retained resources when
leaving the side screen, so pre-arm timing does not exclude Arena ownership.
The affected CAB archives have not been attributed. Do not suppress the errors,
delay foreign unloads or claim a proven preview bug. A repeatable transition or
owned-preview retention/release diagnostics is needed to establish causality.

## Native retest focus

Use the delivered client identified in HANDOFF.md and restart the game. Observe
the new contour's USEC and BEAR 2v2, plus cold BEAR 5v5; retain both log streams.
Record actual roster time and visible movement/combat independently. Let quiet
bots run long enough to capture the bounded awareness snapshots; do not fire to
"prove" awakening. Successful calls or READY text alone are not combat proof.
Check refills, faction friendliness and actual boundary containment. Recheck a
normal PMC raid and protected profile restoration after each Arena match. The
previous report remains the baseline; no new native GPU result is claimed here.

In a cold 5v5, also record the first combat/casualty relative to full-roster READY.
The new cue may advance early fights. Existing TDM already allowed combat before
the first simultaneous full roster, so early deaths can delay that latch and
release of the initial spawn restrictions. Required kit loading and bot refills
never wait for optional catalog warmup; there is no circular dependency. If this
test repeatedly fails to reach READY because of casualties, initial assembly
needs explicit gameplay design rather than selectively hiding one hearing cue.

## Completed offline verification and delivery

All seven executable gates and three manual review/delivery gates are met:
10/10, zero unmet or abandoned. The parent independently reran both child suites.
Policy checks: 14972 assertions/18 source faults; runtime: 268/17; complete
extracted async Burst methods: 21/8; SAIN awareness: 565/20; catalog scheduling
and native pool lifetime: 37/10; actual optic guards: 67/11. These are 84 targeted
source-fault controls, each required to fail its intended assertion. Existing
integration also passed all 444 server tests without skips, native menu/team/
give-up/polygon regressions, and 1605 supplies checks. The supplies harness's
previously missing renderer-free optic boundary was updated; actual optic
ownership is verified independently, not by that no-renderer double.

Independent review found and corrected the opening-kit loading gap, permanent
SAIN-wrapper contact latch and callback/native-stop ownership gap. No remaining
P0-P2 findings in the revised changes. See `tools/TdmRetestChecks/` for the parent
gate fingerprints and `tools/TeamDeathmatchRuntimeChecks/PREPARATION-GATES.md`
for leaf self-verification.

Commits `8fb502c`, `8ee1b05` and `4a979cf` are integrated into `master`. A fresh
client build from clean `4a979cf` passed with zero errors/four existing nullable
warnings. Only the client was installed locally, SHA256
`04D47F6DD8B7C9C252C564604819405F89E0ED9F3BD621999032571B8A349C9D`.
The staged artifact and installed client match; the previous client backup is
verified and server/Redeploy hashes are unchanged. Delivery manifest:
`artifacts/tdm-retest/delivery-5f45d5094fe54298a490ea46e1f81d9c/delivery.json`.
No push, GPU mirror confirmation or native success is claimed. Restart only the
game after checking the mirrored hash. HANDOFF.md contains the current test entry.
