# Tactical purchase, readiness and countdown checks

Run `pwsh -NoProfile -File tools/TacticalPolishChecks/Verify.ps1` from the repo.
The script creates only ignored per-run artifacts; it never builds or deploys
the game mod. It compiles complete, unmodified production `TacticalBuyPanel`
and `SoloTacticalRoundStartPolicy` / `SoloTacticalSpawnPolicy`, plus the exact
production Tactical methods listed in `Verify.ps1` and four real presentation /
phase properties. The real `ArenaMatch.ReportAnnouncerCue`, `BeginAwardRound`
and `RaiseSafe` methods also run, with the complete real `ArenaCombatAwards`,
`ArenaKill` and `ScoreRules`. Extraction requires exactly one method, copies its complete
body including callback closures, and does not rewrite decision logic.

`ModeBoundary` supplies fields, invokes those private methods through a small
fixture adapter, and doubles unrelated team/end-result services. `Doubles`
models only native I/O, dispatch observations, output and configuration. Tests
execute dispatch, asynchronous/stale arrivals, PreActive-to-Active progression,
failed plans, complete/partial/no-ready squads, eliminated-before-ready bodies,
retries, late teardown, starter/purchase/survivor/death paths and native Escape
with living/dead/foreign owners, native menu handover and pointer-drain requests.
The full production input blocker is separately covered by `ArenaInputChecks`;
this suite observes that the real panel requests that existing drain.

Three negative probes compile before running and must fail the named assertion:
the actual old `StartRound` from `8e7f9ea` announces at dispatch; the actual old
provider lambda from that commit draws arbitrary base gear; the actual old buy
panel has no native Escape bridge. The latter is linked with no-op API shims
only so tests can observe that absent bridge, not to simulate a new behavior.
An additional compiled fault removes the real ArenaMatch cue's award-reset
guard and must fail the early-kill-then-readiness check: First Blood cannot be
reissued and the multi-kill chain cannot reset when the delayed sound occurs.
Reports in the ignored artifact folder retain every probe's result. The final
source binding verifies every participating source and harness file remained
byte-identical from the beginning to the end of the run.

Scope limitations: native Unity rendering, actual detour order, bot activation,
input-tree scheduling, body swaps and profile restoration cannot run in this
harness. PFT `TickTeamStaging` and actual mode startup/teardown are source guards,
not simulated team-raid acceptance. Award captures at the native kill boundary
remain outside this harness; the actual round reset and award policy run here.
Staged native compilation, independent review and later user
raid tests remain required.
