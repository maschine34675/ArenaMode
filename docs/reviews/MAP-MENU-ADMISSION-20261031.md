# Arena map screen: one admission verdict per display pass

Date: 2026-10-31. ArenaMode branch `fix/map-screen-admission` from `master` `5203083c`
(commits `af444148` adapter guards, `0e85df4d` display pass); Redeploy branch
`fix/absent-adapter-unpatch` from `main` `70bceb1` (commit `51ac95c`, Redeploy.Core
**1.0.19**). Neither branch is merged or pushed. On 31.10. evening the orchestrator built
`60795208` and `51ac95c` and deployed ArenaMode `0.4.1+60795208` with Core `1.0.19+51ac95c`
to all Syncthing hosts for the native measurement; the review follow-up (below) came after
that build and changes comments, documents and harnesses only. This
supersedes nothing: the [26.10 snapshot](../reports/2026-10/MAP-MENU-PERFORMANCE-20261026.md)
still shares one verdict across the rows of a refresh; this contract makes the refresh
itself evaluate admission once and stops the optional-mod adapters from making each
evaluation expensive.

## Problem (measured on GPU-2, 2026-10-31)

Build 0.4.1+`a6ed8d93` plus the DeveloperMode timing probe (`5203083c`), recorded in
`artifacts/iceberg-20261031/gpu/menu-perf/findings.md` (gitignored):

- Arena SelectLocation ran at 48-49 FPS (mean 20.5 ms, p95 45 ms, maximum 74-128 ms) with
  one long frame every 0.2 s, the `ArenaMapMenuUpdater` tick, for every map and mode; the
  PMC SelectLocation screen ran at 60 FPS.
- The probe (`[dev] Arena map menu refresh: …`, five-second windows) put the cost in the
  two eligibility sections (21-29 ms per tick) and in `compat`: 96-122 calls per window,
  four per tick, 12 ms average. Inside one compatibility check: whitelist 0.0 ms,
  Redeploy.Core `BodyReplacementCompatibility` 9.8 ms, `FastGrenadeThrowCompat` (mod not
  installed) 2.2 ms, PiP preload and Tarkova Cola 0.0 ms. Every UI section stayed under
  0.3 ms, the PitFireTeam roster at 0.0 ms.
- Cause 1, four reads per tick: `ScreenState.Refresh` read `CurrentModeEligibility` and
  `CurrentEligibility`, and each also ran the side screen's provider
  (`ArenaSideSelectionPatch.EvaluateEligibility`), which reads `CompatibilityRejection`
  again.
- Cause 2, every read was expensive: an adapter whose optional mod is NOT installed called
  `Clear()`/`ClearBinding()` → `Harmony.UnpatchSelf()` on every check. In the installed
  HarmonyX 2.9 that is `UnpatchID(Id)`, which walks every patched method of the process
  (about 1,270 there) with a `GetPatchInfo` allocation each: about 2 ms and 10 MB per
  walk. The source has six such adapters on that host: Quick Cancel, PiP Disabler,
  Quest Tracker, Audio Accessibility Indicators and Healing Autocancel in Core, and Fast
  Grenade Throw in ArenaMode, which matches 9.8 + 2.2 ms. Quest Tracker and Audio
  Indicators arrived with Redeploy `d9a1108` (Core 1.0.17) on 2026-10-29, after the
  26.10 menu fix, which is why the screen regressed.

## Contract

### One admission verdict per display pass, fresh on every native entry point

- `ArenaMenuSelection.BeginDisplayPass()` opens a pass and returns an `IDisposable`; it is
  opened with `using`, so it ends in a finally. While it is open, `CompatibilityRejection`,
  `CurrentEligibility` and `CurrentModeEligibility` evaluate on first use and answer every
  later read of the pass from that verdict. Passes nest by depth; the verdicts are dropped
  when the outermost one ends. Only the thread that opened the pass reads or fills them,
  and a pass begun on another thread while one is open is a no-op.
- The strict and the roster-only verdicts are kept per selected mode: `Refresh` repairs a
  saved PitFireTeam mode between its two reads (`Modes.Synchronize`) and the strict PFT
  policy depends on the mode, so a repair inside the pass is seen by the next read. The
  compatibility verdict does not depend on the mode and is kept for the whole pass.
- Installing new providers (`ConfigureEligibility`, `RegisterCompatibilityAdmission`, a new
  native controller in `Begin`) and `EndFlow` drop any verdict, even inside a pass.
- Outside a pass nothing is memoised. A native map click (`OnLocationSelected` prefix), the
  `SelectedLocation` setter, the area, team-size and participant callbacks, `ResumeAfterMenu`,
  the claim (`ArenaClaim`), the final request validation and roster freeze
  (`TryValidateAndFreezeRequest`) and the raid start (`ArenaLocalRaidStartedPatch`) all
  evaluate fresh. That is the 26.10 rule of `ArenaMapAvailabilitySnapshot`: "one synchronous
  display pass, not an admission cache".
- A pass may wrap synchronous display work, never a click, claim, roster freeze or raid
  start. The only writes inside a pass are the existing display repairs of `Refresh`: the
  saved-mode repair (`Modes.Synchronize`), the area repair of `TryPrepareMapArea` and the
  deselection of a map that became invalid. `Refresh` made them before the pass existed (the
  last two on the shared 26.10 snapshot, the mode repair on its own mode read); the pass adds
  no write.

**Where the pass is.** `ScreenState.Refresh` (`ArenaLocationSelectionPatch.cs`), and nothing
else. A refresh runs from the 0.2 s updater, at the end of the Show postfix, and after an
accepted area, team-size or participant click (the click itself is evaluated fresh before
the refresh). What runs inside is the display work the 26.10 snapshot already judged with
one verdict: row visibility, the selected map's area preparation through the snapshot, the
existing display repairs of a saved mode (`Modes.Synchronize`) and area
(`TryPrepareMapArea` → `Settings.TrySelectArea`), and the deselection of a map that became
unavailable (`screen.OnLocationSelected(null)`), whose native prefixes see a null map and
so cannot depend on the verdict.

**Show is deliberately not a pass** (argued at the Show postfix). The Show prefix resumes a
suspended flow (`ResumeAfterMenu`, a fresh admission by contract) and repairs the
remembered map through the selection path; vanilla `ShowInternal` assigns the remembered
selection through the setter prefix; `Modes.Show` commits a mode repair; `Areas.Show`
installs the click callbacks. Native row materialisation already shares one snapshot
(`state.Availability`). Show therefore pays its own reads once per screen open, outside any
pass: with a remembered map about eight strict reads (two in the prefix's map repair, about
five in the `OnLocationSelected` and `SelectedLocation` setter prefixes vanilla runs, one for
the row snapshot, more for a Labyrinth row) plus the postfix's mode read, each evaluating
the admission twice (directly and in the side screen's provider), then one evaluation in the
refresh pass. What that costs with Core 1.0.19 is to be measured natively. A pass there would
save one read per open while widening it over a mode commit.

### Absent adapters never unpatch per poll

- Redeploy.Core 1.0.19 (`51ac95c`): Quick Cancel, PiP Disabler, Quest Tracker, Audio
  Accessibility Indicators and Healing Autocancel keep a flag that is raised before their
  first `Patch` call and lowered only after `UnpatchSelf` returned; `Clear`/`ClearBinding`
  unpatch only while it is set. A bind that fails between two `Patch` calls still unpatches,
  a throwing unpatch is retried on the next call as before, and the installed-mod
  verification, fault and real unbinding paths are unchanged. The other eleven Core
  adapters already guarded the call; Enemy Markers and ImpactCue now also raise their flag
  at their preparation-time unregister hook.
- ArenaMode (`af444148`): `FastGrenadeThrowCompat` gets the same flag, raised before the first of its two
  `Patch` calls (not after both: a failing second call must still remove the first, or the
  next bind would add a second postfix and `VerifyHooks` would refuse the mod for the rest
  of the process). `PipPreloadCompat` already returned early for an absent PiP Disabler, but
  an installed one whose contract does not resolve unpatched twice per poll (rebind and
  catch) with nothing patched; it now unpatches only while it holds its prefixes.
  `ArenaTarkovaColaCompat` caches absence, failure and contract and was left unchanged.
- **Core 1.0.19 ships with this ArenaMode build.** `ArenaMode.Client` stamps
  `RedeployCoreBuiltAgainst` from `../Redeploy/Redeploy.Core/CoreVersion.props` at build
  time and refuses an older Core before runtime initialization, so a build of this branch
  fails closed against the installed and released Core 1.0.18. Merge, build and ship the two
  branches together; Redeploy.Client built from its branch requires 1.0.19 as well.

## DeveloperMode probe

`[dev] Arena map menu refresh: …` (Info, DeveloperMode only, every five seconds) keeps its
sections and timers. `compat` now counts real evaluations only, so it should read about
one call per tick (24-26 per window instead of 96-122). Two timers were added: `compat
reused` (reads a pass answered from its first verdict, three per idle tick: the two
providers and the strict read) and `eligibility reused` (zero while idle; rows and the
selected-map repair go through the snapshot). `CallTiming.TotalCalls` is a cumulative
counter that the five-second report does not reset, for harnesses.

## What the harnesses pin

- `tools/DominationMenuPerformanceChecks` (production-linked, eight rows): exactly one
  optional-mod admission per idle refresh, counted at the provider and by the probe's
  `compat` timer (four without the pass); each eligibility source at most once and no
  eligibility read answered by the pass while idle; 20 refreshes cost exactly 20 checks; the
  selected-map refresh shares display eligibility; a native click right after a pass
  evaluates admission fresh; a refusal arriving between two refreshes is shown by the next
  refresh and lifting it by the one after; final start, claim and click stay fresh after an
  external refusal (unchanged). Seven mutants must fail: the four of 26.10 (rows re-check,
  selected map re-checks, snapshot survives the refresh, final start trusts the display)
  and three new ones: a pass that never ends (`using` dropped), verdicts that survive the
  end of a pass (the forget dropped from `Dispose`) and a refresh without a pass. Since the
  review follow-up: a refresh that throws inside the pass (a drifted row whose GameObject is
  gone) still ends it, so the next native click evaluates fresh and the next refresh is one
  pass again; a nested pass keeps the outer verdict and only the outermost end drops it.
  Two more mutants must fail: a pass without a finally (`for` instead of `using`) and an
  inner pass whose end drops the outer verdicts.
- `tools/FastGrenadeThrowChecks`: a prefix on HarmonyX's own `UnpatchID(string)` counts
  patch-table walks with a positive control; absent-plugin and refused-contract polls cause
  none, and a bound adapter whose hooks were removed still unbinds itself once. Two new
  mutants (the unconditional unpatch of ArenaMode 0.4.1, `master` `5203083c`; the flag never
  raised) must fail. Since the review follow-up (below) it also makes the second `Patch`
  call throw (a one-shot prefix on Harmony's `Patch`): the next bind must be admitted, and
  the failed one unpatches exactly once. It links `PipPreloadCompat`: an installed PiP
  Disabler whose `ScopeLifecycle` does not resolve is refused on three polls without a walk.
  Two more mutants (flag raised only after the second `Patch`; PiP unpatching without its
  flag) must fail.
- `Redeploy.Tests/AdapterUnpatchGuardTests` (Redeploy): a Roslyn source contract over all
  sixteen Core adapters that own a Harmony instance, because they cannot load outside the
  game. It names the files and proves per file that removing the guard, the set or the
  clear fails it. Since the review follow-up the set must be an unconditional `F = true;`
  statement of a block that encloses the `Patch` statement (or the helper call that
  patches), after the method's last return before it; a theory with inline samples proves
  that a flag raised in the absent branch, before the absent branch's return, under a
  condition of its own or inside a lambda is rejected.

## Verification runs (2026-10-31, development machine, no deployment)

ArenaMode, `pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness
DominationMenuPerformanceChecks,NativeMenuChecks,OptionalModAdmissionChecks,NativeAreaHookChecks,AreaConfigChecks,CompactModeUxChecks,FastGrenadeThrowChecks,CompactSessionUxChecks,ArenaStartErrorChecks,ArenaLabyrinthAccessChecks`
on `0e85df4d` with the Redeploy checkout on `51ac95c`:
`ARENA_VERIFICATION_SUMMARY passed=11 failed=0 seconds=367`, live install hashes unchanged
after every step.

| Step | Result |
|---|---|
| ArenaMode.Tests (xunit, once) | 3,623 total, 3,620 passed, 3 known environment-gated skips |
| DominationMenuPerformanceChecks | 18 assertions, 7 of 7 mutants rejected |
| NativeMenuChecks | 956 assertions (program only, no Verify.ps1) |
| NativeAreaHookChecks | 207 assertions (program only) |
| OptionalModAdmissionChecks | 60 assertions, 11 negative controls |
| AreaConfigChecks | 321 assertions (real BepInEx bind/save/reload) |
| CompactModeUxChecks | 329 assertions, mutations passed |
| FastGrenadeThrowChecks | native compile; normal and newer 45, drift and older 10 checks; 6 of 6 mutants rejected |
| CompactSessionUxChecks | 25 assertions (program only) |
| ArenaStartErrorChecks | 91 assertions, 11 negative controls |
| ArenaLabyrinthAccessChecks | 50 assertions (program only; its `Verify-*.ps1` scripts were not run) |

The count line of the baseline: `MENU_PERFORMANCE_OBSERVED: rows=8, compatibility checks per
idle refresh=1 (compat timer 1, reads answered by the display pass 3; 4 per refresh without
the pass), strict=1, mode=1, eligibility reads answered by the pass=0`. The
refresh-without-display-pass mutant prints `compatibility checks per idle refresh=4`, the
pre-pass state (the harness at `5203083c` only bounded it at four). Mutant verdicts:

| Mutant | Rejected by |
|---|---|
| tile-rechecks-admission | one display pass evaluates each eligibility source once, not once per map |
| selected-map-rechecks-admission | selected-map repair and rendering share display eligibility |
| snapshot-survives-refresh | a refusal that arrives between two refreshes reaches the next display pass |
| final-start-trusts-old-display | final start rechecks compatibility immediately instead of trusting the display snapshot |
| display-pass-never-ends | a native click after a display pass evaluates admission fresh |
| verdict-survives-pass | a refusal that arrives between two refreshes reaches the next display pass |
| refresh-without-display-pass | one display pass evaluates optional-mod admission exactly once |

Redeploy (`51ac95c`), with the no-deploy switches of its AGENTS.md:
`Redeploy.Tests` 588 total, 586 passed, 1 skipped, 1 failed; the failure is
`CoreVersionTests.BuiltCoreCarriesItsOwnVersionLine` (expected 1.0.19.0, actual 1.0.18.0:
the Core in `Redeploy.Core/bin` predates the bump; before the bump 587 passed, 1 skipped).
`Redeploy.Server.Tests` 162 of 162 passed. `AdapterUnpatchGuardTests` failed against the
previous sources for exactly the five unguarded adapters (and, on the set rule only, Enemy
Markers and ImpactCue) before the fix. The per-adapter harnesses under Redeploy `tools/`
were not run; a static check found all their mutation anchors unchanged.

Compile only: two scratch projects outside both repositories compiled the Redeploy.Core
sources and the ArenaMode.Client sources (against that Core and copies of the game DLLs in
the checkouts' `bin` folders) with 0 errors. No repository project was built for the game.

## Review follow-up (2026-10-31 evening)

Three reviews found no blocker. Their minor findings, applied on both branches after the
orchestrator's build of `60795208`/`51ac95c` (comments, documents and harnesses only, no
runtime change):

- The pass rule above and in `ArenaMenuSelection.BeginDisplayPass` and docs/AREA-RULES.md
  said a pass never wraps a path that persists a user's selection; `Refresh` does wrap the
  existing display repairs. They now name those writes instead.
- The Show postfix comment counted three Show-time reads and called each cheap under Core
  1.0.19; it now gives the counts above and leaves the cost to the native measurement.
- `DominationMenuPerformanceChecks`: a throwing refresh and nested passes (above), two
  mutants. `FastGrenadeThrowChecks`: the second-`Patch` failure and the PiP refused-contract
  polling (above), two mutants. `AdapterUnpatchGuardTests` (Redeploy `3c41bd6`): the set must
  lie on the path to the `Patch` (above).
- docs/HANDOFF.md: the Core copy-back rule is limited to builds of `master` before the merge,
  and the installed state names the orchestrator's deployment.

Runs (development machine, no deployment), ArenaMode
`pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness
DominationMenuPerformanceChecks,FastGrenadeThrowChecks`:
`ARENA_VERIFICATION_SUMMARY passed=2 failed=0 seconds=77`, live install hashes unchanged.
DominationMenuPerformanceChecks 22 assertions, 9 of 9 mutants rejected
(`display-pass-without-finally` by "a native click after a refresh that threw evaluates
admission fresh", `inner-pass-end-drops-verdicts` by "a nested display pass keeps the outer
verdict and only the outermost end drops it"); its count line is unchanged:
`MENU_PERFORMANCE_OBSERVED: rows=8, compatibility checks per idle refresh=1 (compat timer 1,
reads answered by the display pass 3; 4 per refresh without the pass), strict=1, mode=1,
eligibility reads answered by the pass=0`. FastGrenadeThrowChecks: native compile; normal and
newer 53, drift and older 15 checks; 8 of 8 mutants rejected (`flag-raised-after-second-patch`
by "the bind after a failed second Patch is admitted (the half-applied set was unpatched)",
`pip-refused-contract-unpatches` by "refused PiP Disabler contract polls never walk Harmony's
patch table"). Redeploy `Redeploy.Tests` with the switches of its AGENTS.md: 593 total, 592
passed, 1 skipped (Redeploy.Client not built), 0 failed; `CoreVersionTests` are green against
the 1.0.19 build in `Redeploy.Core/bin/Debug` (`bin/Release` still holds 1.0.18).

## Not claimed

- No native measurement. Frame times on GPU-2 and the probe line with these branches are
  measured afterwards by the orchestrator; until then the 48 FPS figures above are the
  latest native state. The harness counts calls, not milliseconds.
- What one compatibility check costs with Core 1.0.19 is not measured. Installed optional
  mods still verify their hooks with `Harmony.GetPatchInfo` per hooked method on each check.
- This change built nothing for the game and deployed nothing. The deployment of
  ArenaMode `0.4.1+60795208` with Core `1.0.19+51ac95c` on 31.10. evening was the
  orchestrator's, for the native measurement; the follow-up commits are not in it.
- The Show-time reads were not reduced beyond what the adapter guards give.
- No GPU run or raid was part of this change. Three reviews found no blocker; their minor
  findings are the follow-up below.
