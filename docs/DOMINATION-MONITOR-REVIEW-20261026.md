# Domination monitor projection review — 2026-10-26

Scope: `TeamDeathmatchMode.Monitors.cs` and this production-linked harness only.

The optional MapPort monitor claim now accepts the active mode's rules. Domination reads its constant-time objective score counters on every presentation tick, independently of kill-scoreboard version changes. Ordinary TDM keeps the existing versioned scoreboard scan. Both modes use their actual phase, time limit, start time and frozen final scores.

The BEAR-left / USEC-right projection, team colors, current physical-body counts, unique per-match owner, exact mode/match/world ownership and one-time release behavior are unchanged. Terminal results remain visible during the existing result hold. Expected MapPort absence/refusal still affects presentation only.

The harness links the actual monitor partial, MapPort reflection adapter, frame mapper, Domination partial/accessors and both match policies. Only game objects, native world services and the external MapPort endpoint are doubles. The Domination world runtime is not exercised here; native map, gate and renderer acceptance belongs to the parent's GPU checks.

Verified with `pwsh -NoProfile -File tools/TdmMonitorChecks/Verify.ps1`:

- 275 assertions pass, including existing ordinary TDM controls.
- 15 source mutants fail at their intended assertions; production source files are hash-checked unchanged by the runner.
- New Domination coverage uses both player factions and one/two/three-point layouts, with real capture/income from the linked policy. Kill totals deliberately throw on reads, remain version-stable while objectives earn income, and later change to unrelated values. Monitor scores still match the objective factions.
- Assembly, readiness loss, delayed gate release, local death, actual score-limit finish, the full 15-second result hold and native stop are covered.
- The existing native-stop mutant's obsolete expression-body anchor was updated to the current block-body implementation; it still removes monitor release and is rejected by the existing lifecycle assertion.

Review: no gameplay/scoring rules, capture timings, MapPort API signatures, gate ownership or profile persistence changed. The parent separately owns making `OnMatchStart` claim monitors for Domination. No product build, deployment, GPU action or commit was performed in this leaf.
