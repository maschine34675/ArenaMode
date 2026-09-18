# Arena GC threshold (2026-09-18)

Implementation review. GPU acceptance pending — this server has no GPU (see
`WINDOWS-MCP` notes elsewhere); the design and the offline checks below are
what can be verified here.

## The problem

EFT disables Unity's GC for the whole raid. `BaseLocalGame.PrepareSession`
(`../_decompiled/EFT/BaseLocalGame.cs:670-684`) does a forced, blocking
collection and then sets `InGameMemoryManagement.GCEnabled = false`
(`../_decompiled/InGameMemoryManagement.cs`), which only takes effect on a
machine with at least `Settings.GigabytesRequiredToDisableGCDuringRaid` GB of
RAM (SPT serves 25). From then on nothing collects again until raid end
(`TarkovApplication` calls `InGameMemoryManagement.Collect(2,
GCCollectionMode.Forced, isBlocking: true, compacting: true, force: true)` —
a forced, blocking, COMPACTING sweep) or the menu
(`EFT.UI/MenuScreen.cs:192,327` set `GCEnabled = true`). The build runs
Unity's incremental collector when it IS enabled (`boot.config
gc-max-time-slice=3`, i.e. roughly 3 ms of collector work budgeted per
frame), which is the mechanism this feature switches back on — never the
forced sweep above.

Left alone for a long Arena raid the managed heap simply grows. Measured on
the 32 GB GPU host, Labyrinth deathmatch, with the collector off: ~7 MB/s
(7.6 GB after 16 minutes, ~26 GB extrapolated for an hour) — matching the "11
GB with no collection" figure already on record for this project (see
AGENTS.md, corpse cleanup section) from an unrelated raid.

## The design

`Config.Settings.GcHeapThresholdGB` (Performance section, default 4, 0 =
never/pure vanilla). While an Arena session is active AND the collector
currently reads Disabled (so a machine EFT itself kept enabled is untouched),
`ArenaMode.Client/Performance/GcThresholdMonitor` polls `GC.GetTotalMemory
(false)` every 5 seconds — never `true`, which would force a collection just
to ask. Once the heap crosses the threshold it ARMS rather than switching
immediately: an abrupt collector-mode flip mid-firefight is exactly what this
feature exists to avoid. The switch itself happens at the first of:

- the local player's own death (`ArenaEvents.OnPlayerDied` — the screen is
  about to change regardless, so this is already the least visible moment
  Arena has), or
- a new body at a round break (`ArenaEvents.OnKitApplied`, raised by
  `LiveKitRespawn` once the round-break swap from `RoundsMode`/`TacticalMode`
  stands protected at the scoreboard pause; it also fires after a death
  respawn, which only repeats the death's moment, and at the raid-start swap,
  where nothing can be armed yet), or
- a bounded 120 s wait, so a player who never dies and whose raid never
  breaks between rounds still gets it eventually.

A threshold turned down to 0 through the hot-reloadable config page stops new
arming, but a raid that already armed keeps its bounded wait and its reassert:
the poll has no early return for 0, `ShouldArm` refuses a zero threshold on
its own.

The switch is `InGameMemoryManagement.GCEnabled = true` — EFT's own setter.
Verified in the decompile (`InGameMemoryManagement.cs:66-88`): its only guard
blocks *disabling* below the RAM floor; enabling is never refused, and the
whole setter is one `GarbageCollector.GCMode` assignment plus a log line
("GC mode switched to Enabled" — EFT's own, into `application.log`), never a
collection. This feature calls that setter, or reads
`GarbageCollector.GCMode`/`GC.GetTotalMemory(false)`/`GC.CollectionCount(0)`,
and nothing else on the runtime's memory — `tools/GcThresholdChecks`' source
guard fails the build if `GC.Collect(` or `GetTotalMemory(true` appears
anywhere in `GcThresholdPolicy.cs` or `GcThresholdMonitor.cs`.

Once switched, the raid latch never re-arms (`GcThresholdPolicy.ShouldArm`
refuses once `Armed` or `Switched`), so a heap that keeps growing afterward —
it will, the collector is now doing bounded incremental work, not none —
cannot retrigger anything. If something else disables the collector again in
the same raid (nothing in this codebase does; the guard is defensive against
another mod or an EFT path this review did not find), the monitor re-enables
it, logged, at most once every 30 s — deliberately, because silently
accepting the disable would let one unrelated decision undo this feature for
the rest of a long raid.

### Nothing leaks into the next raid

Two independent reasons, argued in code comments at both ends:

1. `GcThresholdMonitor.Reset()` runs from `ArenaRespawnDriver.ResetSession()`
   — the same call every other per-raid Arena service resets from
   (`CorpseCleanupService.Reset()` and neighbours) — on *every* raid
   transition, arena or not, including the "missed GameEnd" recovery path.
   Our own armed/switched bookkeeping cannot survive a raid boundary.
2. Even if it did: `BaseLocalGame.PrepareSession` runs
   `InGameMemoryManagement.GCEnabled = false` unconditionally at the start of
   every raid. That setter reads the CURRENT `GarbageCollector.GCMode` to
   decide whether there is anything to do (`if (value != GCEnabled)`) and, on
   a machine with enough RAM, always ends up Disabled regardless of what this
   feature set it to in a previous raid. The menu/raid-end paths
   (`MenuScreen.cs`, `TarkovApplication`'s forced Collect) are untouched by
   this feature entirely — it never patches them, only reads
   `GarbageCollector.GCMode`/calls the same public setter EFT itself calls.

### Logging

One Info line when armed (heap GB, threshold, raid minute), one when
switched (heap GB, threshold, raid minute, which moment triggered it), and
one follow-up line 45 s later — inside the requested 30-60 s window — with
the heap delta and `GC.CollectionCount(0)` delta since the switch, so a GPU
raid can prove the incremental collector actually ran. The hot path
(`GcThresholdMonitor.Tick`, called every frame) does two `Time.time`
comparisons and nothing else outside the 5 s poll cadence or a pending
follow-up — no allocation, no heap read, no logging.

## Measured (orchestrator, GPU host, 2026-09-18)

Labyrinth deathmatch, collector switched back on right after
`PrepareSession` for the whole raid (not the threshold-gated design above —
see "Open question" below):

| | GC off (vanilla) | GC on (incremental) |
|---|---|---|
| Heap | grows ~7 MB/s, unbounded | held to 1.1-1.9 GB, ~1 collection/minute |
| Frame p50/p95/p99 | 27.6 / 36.5 / 44.4 ms | 28.0 / 37.0 / 47.6 ms |
| Frames over 50 ms | 1.25 % | 1.28 % |
| Respawn pipeline | unchanged | unchanged |

Intervals containing a collection had ~6 more frames over 50 ms than
intervals without one. EFT's own forced `Collect` at raid end: ~0.46 s at
0.8 GB live, ~0.9 s at 1.7 GB live, plus ~1.9 s more for the "aggressive"
second pass when `Settings.AggressiveGC` is on — this feature never calls
that path.

## Answering the question this was built to answer

**Will switching the collector back on make the first collection in an
advanced round noticeably freeze the game?** Short answer: it should not,
and nothing in this design *can* produce the kind of freeze EFT's own
raid-end collection does, because that requires calling
`InGameMemoryManagement.Collect`/`GC.Collect` directly, which this feature
never does. The switch is a single enum write; everything that happens next
is Unity's own incremental collector, which by Unity's own design spends a
bounded slice of time per frame (`gc-max-time-slice=3`, roughly 3 ms) rather
than pausing to finish a collection in one frame. The measured numbers above
back that up: continuous incremental collection over a whole raid barely
moved the frame-time percentiles (p95 36.5→37.0 ms) or the over-50ms-frame
rate (1.25%→1.28%).

**Open question, not yet measured**: the table above ran the collector
continuously from the start of the raid, so the heap it was ever asked to
collect never grew past ~1.9 GB. The threshold design's actual first
collection happens after the heap has been allowed to grow *unchecked* to
the configured threshold (4 GB by default, more if raised) — there is more
garbage to find on that first pass than on the steady ~1-per-minute passes
the table measured. Unity's incremental collector is designed to spread even
a large first pass over several frames rather than doing it in one (the same
per-frame slice budget applies regardless of how much garbage there is to
find), so the expectation is a short CLUSTER of small hitches in the seconds
after arming rather than one long freeze — but that is an inference from
Unity's documented design, not yet a measurement of this exact transition.
The follow-up log line this feature adds (heap + collection-count delta,
45 s after the switch) exists specifically to let a real GPU raid confirm or
correct that inference: force a low threshold, watch frame times in the
30-60 s window after the "GC threshold switch" log line, and update this
document with the result.

## Verification

`tools/GcThresholdChecks` — offline, no game required, built in the style of
the project's other `tools/*Checks` harnesses. `GcThresholdPolicy` is plain
C# with no Unity/EFT dependency, so it compiles directly into the checks
project (no stubs); `GcThresholdMonitor` references UnityEngine/EFT types
that only exist inside the game, so its half of the source guard is a text
scan rather than a compiled check.

```
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/GcThresholdChecks/Verify.ps1
```

Expect `ARENA_GC_THRESHOLD_CHECKS_PASSED`. It runs the baseline (33
assertions: threshold/arena/collector gating, arm timing, moment/timeout
switching, once-per-raid latching, raid-boundary reset, reassert with its
rate limit, and the source guard against `GC.Collect`/`GetTotalMemory(true)`
in both production files), then 15 source mutants — including "a zero
threshold still arms", "arms outside an active Arena session", "arms while
the collector is already enabled", "switches twice", "the bounded wait never
fires", "reset leaves the previous raid's state", "reassert has no rate
limit", and the four guard mutants that inject a banned call into each
production file — each confirmed to fail its intended assertion, then
reruns the baseline once more to prove production source was never modified.

**PowerShell note**: run this with `pwsh` (PowerShell 7), not the Windows
PowerShell 5.1 `powershell.exe` shipped in the box. Under 5.1 with
`$ErrorActionPreference = 'Stop'` (which the shared
`tools/Invoke-ArenaSourceChecks.ps1` runner relies on), a native command's
stderr — which is exactly what an intentionally-thrown `FAIL: ...` exception
from a mutant run writes — is converted into a terminating PowerShell error
before the runner's own exit-code/output-content check ever sees it, so the
whole run aborts on the FIRST fault instead of evaluating it. `pwsh` does
not have this problem. `tools/CorpseCleanupChecks/Verify-Regression.ps1`
works around the same issue locally by temporarily setting
`$ErrorActionPreference = 'Continue'` around its own `dotnet run` calls;
this harness instead just documents the requirement to run it under `pwsh`,
since that is the simpler fix and does not touch shared infrastructure other
checks depend on.

`dotnet build ArenaMode.Client -c Release -p:SPTModType=Library` — 0 errors,
the same 4 pre-existing `CS8632` warnings in `ArenaMode.Shared/
ClientSettingsModels.cs` as before this change (unrelated to this feature).

No GPU raid has run against this change. The follow-up log line's window
(30-60 s after a threshold switch) is where that raid should look first.
