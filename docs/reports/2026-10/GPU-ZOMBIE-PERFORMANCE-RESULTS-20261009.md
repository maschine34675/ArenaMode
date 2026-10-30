# Bounded Zombie performance changes — GPU2, 2026-10-09

## Delivered scope

- `bc21205`: omit unused optional Zombie kit-catalogue prewarming at both entry
  points; preserve the chosen opening kit and required refill loading.
- `415c5aa`: discard confirmed friendly generic-infected footsteps before native
  delayed hearing work, and omit their peaceful social-partner search.
- Combat listeners, no-delay/muted/unknown hearing, humans, PFT companions,
  infected Tagilla and other modes retain native behavior. Existing global pair
  management, vision, movement, animation, combat and population limits remain.
- No new options or global AI configuration. No supported-cap increase.

Read `ZOMBIE-PERFORMANCE-REVIEW.md` and `ZOMBIE-KIT-WARMUP-REVIEW.md` for exact
identity, relationship, preload and native-call boundaries.

## Offline evidence

- Unit suite: 909 passed, two existing skipped tests.
- Production-linked Zombie performance harness: 192 assertions; 17 compiled
  faulty variants rejected. Parent reran both; final fault evidence
  `artifacts/zombie-performance-checks/23ca390220af4e52ab960c3ed79cc70a`.
- Production-linked warmup harness: 90 assertions; ten compiled faulty variants
  rejected in the independent parent rerun at
  `artifacts/zombie-kit-warmup-checks/4e4c7eefd6d64bb991efa923f705c955`.
- Existing TDM warmup 37, Zombie solo 85 and Zombie PFT 59 assertions passed.
  TDM's host fixture gained the new linked policy and seven missing no-op tick
  boundary stubs; no scheduling expectations were relaxed.
- Installed Assembly-CSharp IL contract passed, SHA256
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Independent adversarial review found and resolved avoidable registry lookups
  on the native synchronous combat hearing path. Tests now demand zero lookups
  for combat/no-delay/muted paths and exactly two in the positive delayed case.
  Final registration/source review has no open P1/P2 findings.
- Staged client build: zero errors, four pre-existing CS8632 warnings. Stage
  `artifacts/double-jump-build/75e85ca5fe5549fb8070c0fbb3da9c4f`.
  The ordinary unit-test command rebuilt/deployed the unchanged-source server
  locally; the exact original server bytes were restored immediately from GPU2
  before the staged client build. GPU2's running server was not restarted or
  replaced. Subsequent test runs should redirect SPTUserPath when building tests.

## Exact artifacts

Client ProductVersion `0.1.0+415c5aa6aadbd339b3dda646f4e44b76ef533332`, SHA256
`425A517EC295F90CE8BBCE5CD36D40942D9D0FA7A655AF959848D2E04CE15240`.
Deployed to local/GPU2; new process `ba0e4a1a` loaded all 21 maschine plugins
identically to local/host disk. No Core, other mod, asset or server deployment.

Unchanged server: ProductVersion `0.1.0+9c67ed74ea032c239656599536ee645b53e9fc7a`,
SHA256 `C0871C34B4F1AFAAE990777E39827EDCC1BAF7A6C74401A1A0A47AD73DE49307`.
Core SHA256 `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD`.
UiBridge 0.7.4 SHA256
`306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E`.

## Native comparison protocol

SPT-GPU-2, profile SPT-GPU-2, Factory day, solo own gear, 56 first-wave/maximum
living zombies, 0% shooters, spawn interval 0.2 s, wave-stall watchdog disabled.
Godmode keeps the stationary human alive; no kills or corpses in measurement.
Reference position (72.1, 0.2, -73.9), yaw 356.4, pitch 0. No companions invited.
ModProfiler headless, existing vanilla-engine instrumentation, 30 s exports,
no profiler overlay. Test settings are temporary and restored after testing.

Baseline process `9fb2c889`, client `9c67ed7`, loaded/local/host 21/21 matched.
Full raw evidence is ignored under `artifacts/zombie-perf-20261009/`.

| Capture | Frames / seconds | Mean frame time | Notes |
|---|---:|---:|---|
| Baseline 13:45:23–13:45:53 | 2457 / 30.009 | 12.21 ms | Last three optional kit loads; load boost ends 13:45:27 |
| Baseline 13:45:53–13:46:23 | 2518 / 30.012 | 11.92 ms | Cleaner steady-state; 56 living bots |
| Final 13:59:44–14:00:14 | 2594 / 30.01 | 11.57 ms | Horde still approaching; fewer melee updates |
| Final 14:00:14–14:00:44 | 2680 / 30.01 | 11.20 ms | 56 living bots; convergence continues |

Baseline confirms 20 optional kit loads / 387 new item templates despite own
gear. Both measured windows have 56 BotOwner updates/frame. Melee-node calls
rise from 47.23 to 51.04/frame as the horde gathers. Use export interval deltas,
not cumulative FPS/times, and do not sum inclusive/nested mod timers. Final
prefixes add their own profiler instrumentation; this is a bounded comparison,
not a randomized benchmark or proof of a safe higher population.

No gameplay NRE occurs in either baseline interval. Three known
`BotMemory.GoalTargetBeingHitAction` NREs occur 247–314 ms after native Stop,
after capture ended, followed by 56 teardown Animator warnings. This existing
disposed-melee callback issue is separate from these changes.

Final was slightly faster in this run (86.44/89.32 versus 81.88/83.90 FPS), but
that is **not a demonstrated optimization percentage**. Final melee calls were
27.54/46.70 per frame versus baseline 47.23/51.04, and native inclusive time in
the cleaner second interval was essentially flat (3.305 -> 3.324 ms/frame).
The final player spawned elsewhere and was teleported to the reference point
before measurement; the point snapped up 0.1 m. Native target paths therefore
differed. The final crowd ended with all 56 in ZombieFast, four moving, farthest
3.3 m. No kill, death or corpse cleanup happened during these windows.

The new prefixes together measured about 0.016 inclusive ms/frame (including
profiler instrumentation). 1786 baseline methods versus 1784 final is explained
by two new prefixes and four absent lazy optic-preload guard hooks; the latter
were idle in the baseline's cleaner window. Both captures have intact timers,
valid deltas, no resets/rescans/GC collections or instrumentation patch failures.
These results show no obvious performance regression, but do not certify higher
population capacity or attribute observed heap/FPS differences to one change.

## Native behavior and removed work

- Own-gear run: **zero optional kit loads**, versus baseline's twenty / 387 new
  templates. Both runs reach 56 live native infected.
- First complete diagnostic minute: 544 delayed-step candidates, **461 friendly
  steps skipped**, **3097 social searches skipped**. Second minute, once fighting:
  zero delayed candidates/steps skipped and **3018 social searches skipped**.
  Zero delayed candidates in combat is expected: the native synchronous path is
  deliberately retained, not a failed hook.
- Native approach, swing and landed-hit logs remain present. After measurement,
  godmode was disabled; native melee killed the human at 14:01:08.735, followed
  by the protected solo end without respawn.
- Separate same-build kit smoke: requested `WholeCatalogue`, own gear off,
  one-zombie waves. Exactly Enforcer's 37 templates preloaded in 0.7 s before
  entry, adopted as one completed batch; MP-153 equipped and fired with native
  ammunition decrement. No other catalogue entry warmed. The same-kit refill
  executed, but the wave-clear acceptance is limited by the existing issue below.
- No optimization fallback warning or Arena gameplay damage NRE in final runs.
  Existing teardown Animator warnings remain. The kit body replacements trigger
  the installed RespawnApiProbe's intentionally throwing subscriber and its
  existing culling-sampler cleanup warning; later subscribers and swaps finish.
- PFT and ordinary-raid exclusions were verified offline/source-wise; a native
  PFT survival or ordinary PMC raid was not rerun for this change.

### Existing tiny-wave solo readiness issue

The one-zombie smoke exposed a separate bug: solo can enter intermission while
its last requested spawn is still pending. `ZombieWavePolicy.Evaluate` tests
remaining-to-spawn and alive but not pending at its clear condition; the runtime
currently protects that gap only for PFT. Mode/policy/spawn-service source blobs
are identical to baseline `4158a64`. The warmup change can alter timings, but
does not introduce this incorrect condition.

Observed: match start 14:02:36.839, kit ready 37.101, early refill 37.551, first
bot arrival 37.613, second arrival 40.617 after the three-second intermission.
This proves startkit/use/refill execution, **not** a correctly cleared-wave
transition. Required preload/refill rules pass the production-linked checks;
normal cleared-wave GPU acceptance remains a follow-up. No expansion into the
wave director was made in this bounded optimization task. The 56-zombie
comparison remained in its first wave and did not encounter this condition.

## Preservation and final state

All temporary values restored through their ledgers, no conflicts/missing keys.
After graceful EFT shutdown, the Arena and ModProfiler configs were checked for
value equality and restored byte-for-byte to their original files. Server stays
running (PID 15184); game is closed. Final client local/host SHA256 match the
artifact above; server bytes match the original on both hosts. Core unchanged.
Full profile comparison changed only `Health.UpdateTime` and
`Hideout.sptUpdateLastRunTimestamp`; equipment, progression and other fields
unchanged. All twelve PFT files are byte-identical. No profile file was restored
or manually edited. No push. Untracked Molotov assets were preserved untouched.

## Separate future investigation

Baseline PFT `HostileNonCombatActiveLayerFilterPatch` costs about 0.223–0.253
inclusive ms/frame at 56 calls/frame despite no followers. A narrow infected
fast path is a possible later investigation; no PFT patch is changed here.
BigBrain's strategy-update prefix and Arena's melee prefix execute native AI
inside their measured intervals, so those timings are not pure mod overhead.
SAIN world/direction jobs also remain active despite per-zombie exclusion.
