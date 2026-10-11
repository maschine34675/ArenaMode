# Labyrinth long-raid review, 2026-09-16

Supersedes `LONG-RAID-REVIEW-2026-09-05.md`. That report examined a 33-minute
Gun Game raid and had no frame-cost measurement at all; this one has nineteen
ModProfiler exports, so for the first time the per-frame cost of every installed
mod is measured rather than argued. Its two headline findings — the corpse
stripping diagnostics and the growing respawn pipeline — are both re-checked
below: the first is fixed, the second is not, and is now quantified.

Two defects were found and fixed as a result of this raid. Both are recorded
here with the evidence that identified them; neither was visible in the previous
report.

## The raid

`D:/SPT41/BepInEx/LogOutput.log`, Labyrinth Deathmatch FFA, 17:49:54 to
18:17:38 local time.

- **83 kills / 26 deaths**, score 18,405, best streak 11, 27.4 min; the match
  summary is the last Arena line of the session.
- 657 corpses trimmed, ten retained visibly.
- 26 respawns, all completed.
- Burst spawn held 8–11 alive against a target of 10 with a PMC floor of 10 for
  the whole raid; 274 requests, one dropped (`no usable spawn point for
  pmcUSEC`). No sustained empty-opponent condition.
- The load boost held the scheduler's force mode for **367.5 s across 320
  cycles**, 22.4 % of the raid. That is bot resupply, not one long hold.
- No Arena error and no no-contest.

`Arena raid-end remains pending` appears again at 18:17:38 and is again not a
failed restore; see the previous report for the same sequence.

## Frame cost, measured

### How the numbers were obtained

Nineteen CSV exports in `BepInEx/plugins/maschine-ModProfiler/`, 17:50:41 to
18:17:32. Three properties of that data decide how it may be read, and getting
any of them wrong inverts the conclusion:

- **The counters are cumulative and only advance while the profiler window is
  open.** Wall-clock time is therefore not a denominator. Consecutive snapshots
  must be differenced, and only the frames actually measured count.
- **The frame count is `Anvil-WebOverlay | WebOverlayPlugin.Update`: 17,481
  frames.** It matches `ArenaMode | ArenaController.Update` exactly, which is
  the independent confirmation that both run once per rendered frame. Per-mod
  call *sums* are not frame counts — `OnGUI` runs once per event and
  `BrighterInteriors`' `DrawStencilShadow` once per light, both twice per frame,
  which is how a first pass here produced numbers that were uniformly half the
  truth.
- **The instrumentation inflates every row.** `PitAlex-PitFireTeam` averages
  0.61 µs over 204.9 calls per frame, so on this machine the per-call overhead
  is *below* 0.61 µs, not the ~1 µs the profiler's own README estimates. Any
  row near that value is measurement, not work. Costs below are therefore given
  as lower bounds where the call volume is large.

### What it costs

Average over the 17,481 measured frames, all mods together: **2.598 ms/frame.**

| Mod | measured ms/frame | calls/frame | attributable (lower bound) |
|---|---:|---:|---:|
| SAIN | 1.648 | 287.0 | ≥ 1.47 |
| maschine-ArenaMode | 0.396 | 204.8 | ≥ 0.27 |
| DrakiaXYZ-BigBrain | 0.231 | 22.3 | ≥ 0.22 |
| PitAlex-PitFireTeam | 0.124 | 204.9 | at or below the floor — noise |

SAIN is the dominant cost by a factor of roughly five over ArenaMode, and a
single postfix accounts for most of it: `WorldTickPatch.Patch` on
`GameWorld.DoWorldTick` costs **0.753 ms/frame** at 376 µs per call, 29 % of all
measured mod time. Its vision jobs add 0.414 ms/frame. BigBrain's
`BotAgentUpdatePatch` runs 11.1 times per frame, which is the live bot count,
for 0.151 ms/frame.

ArenaMode's own largest item is `ArenaController.Update` at 0.108 ms/frame —
inclusive of the roughly thirty Tick services it drives. Everything else of ours
that clears the measurement floor is small: `MotionPatch.Prefix` on
`DirectApplyMotion` 0.041 ms/frame (12.4 calls/frame, one per live body),
`LaserBeam.LateUpdate` 0.031 ms/frame (2.2 calls/frame). The hot-path fast-outs
committed earlier today show up as intended: `FirearmController.ManualUpdate`
now averages 0.60 µs and `Player.BodyUpdate` 0.21 µs per call, both under the
floor, so neither is attributable cost any more. That is consistent with the
change but is not a before/after measurement — no instrumented baseline exists
from before it.

### Whether it degrades

It does, modestly and measurably. Per measured interval, ms per rendered frame:

| | first intervals (17:50–17:53) | last intervals (18:06–18:17) |
|---|---:|---:|
| SAIN | 1.17–1.43 | 1.74–2.17 |
| maschine-ArenaMode | 0.24–0.30 | 0.47–0.59 |
| DrakiaXYZ-BigBrain | 0.18–0.28 | 0.21–0.23 |
| all mods | 1.82–2.13 | 2.81–3.25 |

So total measured mod cost grows by about **0.7–1.1 ms/frame over 27 minutes**,
SAIN contributing most of it in absolute terms and ArenaMode roughly doubling in
relative terms. BigBrain and PitFireTeam are flat. `ArenaController.Update`
drifts from about 85 µs to about 160 µs per call across the same span.

**This is not a large enough effect to explain a severe frame-rate collapse.**
The longest single measured window, 18:00:42 to 18:03:35, covers 10,211 frames
in 173 s of wall clock, which puts the average at **no less than 59 fps** at
minute 13 of the raid — a lower bound, since the window may have been closed for
part of it. A ~1 ms/frame growth matters at a 120 fps target and is nearly
invisible at 60. The intervals with ArenaMode above 1 ms/frame (17:51, 17:53,
18:04) are respawn and burst-spawn coroutines, which are one-shot costs at a
death, not a standing per-frame load.

The honest summary: mod cost grows, the growth is real and reproducible, and it
is an order of magnitude too small to be the whole story of a subjective
late-raid slowdown. What the profiler cannot see is explicitly listed in its
README and applies here — indirect engine cost from more bots, more corpses and
more models, plus GC. That remains unmeasured.

## Defect 1: a slide controller per death (fixed)

`ArenaSlideController.Update` was called **26.7 times per frame** by the end of
the raid, rising monotonically from 1: 1.0, 2.0, 3.0, 4.4, 6.6, 9.4, 12.1, 15.5,
23.2, 26.7 across the snapshots. The raid had 26 deaths. The match is exact and
the growth is unbounded.

`MovementRuntime` attaches the controller to the local player's GameObject and
nothing ever removed it. Its comment claimed the component "dies with each
respawn", which is wrong in Arena specifically: Arena never destroys corpses, so
the previous body's GameObject survives and Unity keeps dispatching `Update` to
the controller on it for the rest of the raid. `Update` early-outs only on a
null player or context, and a corpse's Player is neither.

At 0.74 µs per call this was about 0.02 ms/frame at 26 instances — small, but
unbounded, and it carried a latent write: a body that died inside the 0.3 s
slide window could still reach `SetPoseLevel` on a corpse.

Fixed in `66cfc44`. The component disables itself once its own body is no longer
alive, which bounds the cost by the live body instead of the death count. Since
a disabled component is not null, `MovementRuntime` had to be changed to adopt
it rather than skip it, so a body that is somehow live again still slides.
Covered by `tools/ArenaWeightChecks`, verified against the previous attach logic
as a negative control.

## Defect 2: the Nightforce NXS 2.5-10x24 (fixed)

87 native `NullReferenceException`s in `ScopeZoomHandler.method_2`, all inside
1.3 seconds at 17:51:45.936–17:51:47.240 — one per `LateUpdate` for as long as
the player aimed through the optic the attachment draw had bolted onto an SR-25.
They are the single largest error class in the session's native
`errors.log` as well.

The mechanism is a BSG prefab/template mismatch, verified in `../_decompiled/`:

- `OpticComponentUpdater.LateUpdate` decides whether to drive the zoom by asking
  the **prefab** — `ScopeSmoothCameraData.IsAdjustableOptic` is hard-coded
  `true` — and then calls `ScopeZoomHandler.UpdateScope`.
- `ScopeZoomHandler.Init` only fills `_adjustableOpticData` when the **item
  template** says `IsAdjustableOptic`.
- `UpdateScope` calls `method_2`, which dereferences that field with no guard.

This scope ships the variable-zoom model with a template that says `false`, so
the field stays null and every aiming frame throws. Its data is unfinished to
match: the name promises 2.5-10x but `Zooms` holds a single 1x, `MinMaxFov` is
all zeroes, **no trader assort contains it**, and the handbook price is 609 where
real riflescopes start near 22,000. Fifty-eight hosts accept it in `mod_scope`,
including the SR-25 upper receiver, so this was reachable on far more than one
kit.

Neither existing rule could catch it: the display name is ordinary, so the
`!!!DO_NOT_USE!!!` marker rule is blind to it, and it has a handbook entry, so
the cartridge rule would not have applied either. Fixed in `35ddc59`: its id
joins a second seed list, `ArenaTemplateEligibility.KnownBrokenTemplates`, and
**both** seed lists are now refused whatever the configuration holds — the
server's start-up migration adds missing config *keys*, not missing list
*entries*, so an installed `excludedTemplates` would otherwise have kept handing
it out.

A scan of the whole item database found three further optics whose name promises
variable zoom while the template says not adjustable: the Armasight Zeus-Pro
2-8x50, FLIR RS-32 2.25-9x and SIG ECHO1 1-2x30. All three are **thermal**
optics with complete data — their maximum zoom matches their name, and traders
sell them — so they take a different prefab path and are deliberately *not*
blacklisted. The Nightforce is alone in this database in combining an
unfinished template with a variable-zoom model.

## Corpse stripping: the previous report's main finding is fixed

The 2026-09-05 raid produced **5,640** `No parent inventory ...
RemoveItemEventArgs` diagnostics covering its 649 trimmed corpses. This raid
trimmed 657 corpses and produced **14**. The Begin/Succeed activity pairing done
in `CORPSE-CLEANUP-REVIEW.md` holds at a comparable corpse count, and the
diagnostic is no longer a per-corpse event. The residue was not attributed to a
specific call site here.

## Respawn pipeline growth: still unexplained, now quantified

The previous report flagged an upward trend without a slope. Over this raid's 26
respawns the native pipeline stage is strictly monotonic:

```
0.56 0.44 0.46 0.56 0.50 0.49 0.49 0.54 0.56 0.68 0.72 0.74 0.75 0.77 0.77
0.88 0.80 0.82 0.90 0.87 0.89 0.91 0.93 0.96 0.99 1.02
```

Mean of the first five 0.504 s, of the last five 0.962 s; least-squares slope
**0.0231 s per respawn**. Kit time stays 0.00 s and bundles 0.02–0.03 s
throughout, so the template cache is working and the growth is entirely in the
native pipeline stage.

It reached 0.96 s after 26 respawns here, where the 2026-09-05 raid needed 47 to
reach the same figure. The signal tracks respawn count far better than elapsed
time, which points at something accumulating per replaced body rather than per
minute — the same shape as the slide-controller leak, though that one is now
excluded as the cause since it cost microseconds. **This is the most concrete
open performance lead in the project and it should be measured directly, by
timing the stages inside the pipeline, rather than guessed at again.**

## Native defects observed, not ours

**Killa's agro strategy throws every ten seconds.** Eighteen
`NullReferenceException`s in `BossKillaAgroLayersStrategy.UpdateCheckEnemyInfo`,
at 17:52:00.603 and then every ~10 s until 17:54:51.553, surfaced through
BigBrain's agent-update patch and logged by PitFireTeam's exception handler. The
ten-second period is native: the strategy constructs
`new AIPeriodAction(10f, UpdateCheckEnemyInfo)`.

The bug is entirely BSG's, verified in `../_decompiled/`:

```csharp
// BossKillaAgroLayersStrategy.UpdateCheckEnemyInfo
foreach (KeyValuePair<IPlayer, EnemyInfo> enemyInfo in _owner.EnemiesController.EnemyInfos)
{
    IPlayer key = enemyInfo.Key;
    if (!((_corePos - key.Position).sqrMagnitude > 4f))
    {
        Player.FirearmController firearmController = Singleton<GameWorld>.Instance
            .GetAlivePlayerByProfileID(key.ProfileId).HandsController as Player.FirearmController;
        ...
```

`GameWorld.GetAlivePlayerByProfileID` returns `null` for a profile that is not
in `allAlivePlayersByID`, and `.HandsController` is dereferenced with no check.
`EnemyInfos` retains entries for enemies that have since died, so the throw is
guaranteed as soon as a *dead* enemy's last position lies within 2 m of Killa's
core position. Arena FFA makes that routine: bodies stay where they fell.

What this does and does not explain: the exception aborts `ManualUpdate` before
`base.ManualUpdate()`, so Killa loses one brain update per ten seconds. That is
one frame in roughly six hundred and is **not** enough to freeze a bot. It is
better read as evidence that corpses were lying within 2 m of Killa's core
position for three minutes — consistent with the user's report that Killa fell
into a pit and could not get out, but not proof of it. The reported bot
teleports have no Arena log evidence either way; `BotMover.Teleport` is patched
by both Arena and SAIN's own unstuck routine, and neither logged an Arena-side
relocation this raid. Labyrinth runs with `disableBosses: false`, so Killa is
expected there.

Also present and not investigated: 394 native `JetMaterial == null`, 83
`Unknown custom item type`, 45 `Animator.GotoState on Synchronize layer`
warnings, and one `DynamicMaps` end-of-raid error.

## What this report does not establish

- No frame-time or memory series exists, so nothing here proves or disproves a
  late-raid FPS collapse. The 59 fps figure is a lower bound from one window.
- The profiler cannot attribute indirect engine cost, GC, or transpiler work.
  ArenaMode spawns bots and keeps corpses; that load lands in the engine, where
  none of these numbers can see it.
- The hot-path fast-outs from earlier today have no instrumented baseline to be
  compared against. Their rows sit under the measurement floor, which is
  consistent with the intent and is not a measured improvement.
- Neither fix in this report has been playtested. Both need a raid: sliding
  after several deaths, and a kit draw that would previously have produced the
  Nightforce.

## Priorities

1. **Instrument the respawn pipeline stages.** It is monotonic, it has a slope,
   and it is ours. Measure it; do not theorise again.
2. **Playtest both fixes**, then re-export profiler CSVs from a comparable raid
   so the slide-controller row can be confirmed flat at one call per frame.
3. **Leave SAIN alone for now, but know the number.** It is five times our cost
   and its `DoWorldTick` postfix alone is 29 % of measured mod time. That is a
   configuration and expectation question, not a bug in Arena.
4. Nothing in this raid justifies weakening profile protection, corpse
   retention or the load boost.
