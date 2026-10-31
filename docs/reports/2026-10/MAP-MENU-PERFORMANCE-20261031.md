# Arena map-menu performance, second round — 2026-10-31

The user reported a strong frame-rate drop on the Arena map selection screen (the
SelectLocation screen with the ARENA ZONE panel). This records the cause, how it was
measured on SPT-GPU-2 (Ryzen 7 9800X3D, RTX 4070 Ti, 1920×1080, 60 Hz, SPT 4.1.6), the
fix and the measurement after it. Contract: [MAP-MENU-ADMISSION-20261031.md](../../reviews/MAP-MENU-ADMISSION-20261031.md);
the first round of 26.10. is [MAP-MENU-PERFORMANCE-20261026.md](MAP-MENU-PERFORMANCE-20261026.md).
Raw evidence (probe lines, profiler export, status readbacks) is under the ignored
`artifacts/iceberg-20261031/gpu/menu-perf/`.

## Before (ArenaMode `a6ed8d93`, Redeploy.Core 1.0.18)

Frame times from `uibridge_status perfSeconds=10` after at least twelve seconds of
settling, no input during the window:

| Screen | FPS | Mean | P95 | Max |
|---|---:|---:|---:|---:|
| Main menu | 60.0 | 16.7 ms | 16.7 ms | 16.7 ms |
| PMC SelectLocation (no Arena) | 59.9 | 16.7 ms | 16.7 ms | 34.5 ms |
| Arena SelectLocation, no map | 49.1 | 20.4 ms | 45.0 ms | 76.5 ms |
| Arena, Iceberg + Team DM | 48.8 | 20.5 ms | 45.2 ms | 74.1 ms |
| Arena, Iceberg + Domination | 48.4 | 20.7 ms | 45.1 ms | 81.4 ms |
| Arena, Bowl + Domination | 48.7 | 20.5 ms | 45.6 ms | 76.3 ms |
| Arena, Bowl + Deathmatch | 47.7 | 21.0 ms | 46.8 ms | 128.1 ms |

One long frame every 0.2 s, independent of map and mode; the 26.10. measurement after
that round's fix had P95 27 ms.

A ModProfiler headless capture (which instruments 2,022 methods and inflates exactly this
cost) attributed every recorded spike (110–210 ms) to `ArenaMapMenuUpdater.Update`,
41 ms per frame on average, 217 MB/s of allocations and 217 GC collections in 90 s; no
other ArenaMode method exceeded 0.06 ms per frame. ModProfiler cannot see inside the
tick, so a DeveloperMode timing probe was added to the refresh (`5203083c`,
`[dev] Arena map menu refresh: …` every five seconds). It showed per 0.2 s tick:

| Section | Average | Maximum |
|---|---:|---:|
| modeEligibility | 21–26 ms | 25–66 ms |
| eligibility | 23–29 ms | 36–71 ms |
| modes, rows, prepare, areas, info, preview, ported labels | 0.0–0.3 ms | ≤ 0.8 ms |
| optional-mod admission (`compat`) | 4 calls per tick, 12 ms each | 25–76 ms |
| of which Redeploy.Core `BodyReplacementCompatibility` | 9.8 ms | 67 ms |
| of which `FastGrenadeThrowCompat` (mod not installed) | 2.2 ms | 6.6 ms |
| PitFireTeam roster decision (`pft`) | 0.0 ms | 3.3 ms |

Cause: `ArenaMenuSelection.CompatibilityRejection` ran four times per tick (mode and
strict eligibility, each once directly and once inside `EvaluateEligibility`), and inside
it every adapter of an optional mod that is not installed called `Clear()`/`ClearBinding()`
→ `Harmony.UnpatchSelf()`, which walks all ~1,270 patched methods of the process with
`GetPatchInfo` allocations: `QuickCancelCompat`, `PipDisablerCompat`, `QuestTrackerCompat`,
`AudioIndicatorsCompat`, `HealingAutoCancelCompat` (Redeploy.Core; the Quest Tracker and
Audio Indicators adapters arrived with Core 1.0.17 on 29.10., after the first round) and
`FastGrenadeThrowCompat` (ArenaMode). The user's own client log of 17:53 shows the same
mod set; that machine is slower, so its stalls were longer. An independent read-only code
analysis (three readers, one judge) reached the same ranking before the probe confirmed it.

## Fix

- Redeploy.Core 1.0.19 (`fix/absent-adapter-unpatch`): every adapter unpatches only when
  its Harmony instance actually holds patches (`_patched`/`_hooked` flags set where the
  adapter patches, cleared after `UnpatchSelf`); a Roslyn source-contract test pins the
  pattern for all 16 adapter files. ArenaMode built against 1.0.19 refuses an older Core,
  so both ship together.
- ArenaMode (`fix/map-screen-admission`): `FastGrenadeThrowCompat` and `PipPreloadCompat`
  get the same guard; the map screen evaluates the admission once per display pass
  (`ArenaMenuSelection.BeginDisplayPass()` around `ScreenState.Refresh`), and the memo
  ends with the pass, so every native click, claim, roster freeze and raid start still
  evaluates fresh. `DominationMenuPerformanceChecks` pins exactly one admission check per
  idle refresh, a fresh evaluation on a click after a pass and the end of the pass, with
  seven rejected mutants; `FastGrenadeThrowChecks` counts HarmonyX `UnpatchID` walks (0 for
  an absent or refused plugin). Three independent reviews found no blocker.

## After (ArenaMode `fix/map-screen-admission`, Redeploy.Core 1.0.19, same host and method)

| Screen | FPS | Mean | P95 | Max |
|---|---:|---:|---:|---:|
| Arena SelectLocation, no map | 60.0 | 16.67 ms | 16.67 ms | 16.71 ms |
| Arena, Iceberg + Domination | 60.0 | 16.67 ms | 16.67 ms | 16.68 ms |

Probe, steady state: 25–27 ticks per 5 s, maximum tick 0.5–0.6 ms, `compat` exactly one
evaluation per tick at 0.1 ms, the other three reads answered by the pass, `pft` 0.0 ms.
The window with two clicks (ICEBERG tile, DOMINATION row) had 14 admission evaluations
at 0.1–0.8 ms each and one 6.4 ms tick (the area view rebuild). Startup logged no
ArenaMode, Redeploy or Core warning; Redeploy.Client 1.1.1 accepted Core 1.0.19; all 16
plugins identical across VM build, host disk and loaded process. A Domination raid on
Iceberg started through the menu afterwards (fresh admission at READY) with 0 warnings
and 0 errors and was left normally.

## Not claimed

- No measurement on the user's own machine; no long session; no measurement of the Show
  path (about nine admission reads per screen open, now each about 0.1 ms).
- The probe stays in the code and costs nothing while DeveloperMode is off.
