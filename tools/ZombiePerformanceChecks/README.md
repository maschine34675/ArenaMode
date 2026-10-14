# Zombie population benchmark

External, single-operator native-game benchmark. No game DLL is patched or built.
Requires the sibling UiBridge repository's configured `tools/gpu-run` MCP proxy
and a dedicated, idle **SPT-GPU-2** test profile. Run from the ArenaMode root;
`UIBRIDGE_GPU_RUN` can point at another checkout of the same official tool.
Credentials stay in the existing private `.claude.json`/token files.

1. Read ArenaMode `AGENTS.md` and UiBridge's overview/raid/shooting playbooks.
2. Record loaded binaries, hardware, graphics, original configuration and profile
   in an **ignored** artifact folder. Check that no other user owns the host.
3. Use official `gpu-run config-pin` ledgers and the same explicit
   `--host host2 --lane <your-lane>` for settings/start/leave/restore. Set
   `$env:ZOMBIE_BENCH_LANE = '<your-lane>'` to the same label for this harness
   (default remains `zombiebench-20261013` for older invocations).
   Each MaxAlive needs a fresh raid. Keep kit, graphics, shooter percentage and
   spawn interval fixed. For a sustained population, use a wave larger than the cap.
4. Immediately after official `gpu-run raid`, run `prepare.mjs output.json` to
   enable godmode and infinite ammunition with weapon condition preserved.
   A loaded magazine weapon is required; the test profile may be unarmed.
5. `node tools/ZombiePerformanceChecks/run.mjs output.jsonl 240 route.json`
   controls an **already started** raid: 30 seconds of warmup, walking, native
   head-directed gunfire and relocations every 55 seconds. The optional route
   is an array of observed `uibridge_places` spawn records, checked against this
   map. Reuse exactly the same route for comparisons. No synthetic kills/spawns.
6. `node tools/ZombiePerformanceChecks/analyze.mjs output.jsonl 24 summary.json`
   reconstructs the per-frame ring and reports combat quantiles and evidence.
   For early evidence files without a recorded duration, a final positional
   argument supplies the independently recorded requested seconds. A conflicting
   override is rejected. Never infer the requested duration from a truncated file.
7. Leave via official `gpu-run leave`, verify the protected profile, restore
   configuration ledgers in reverse order and confirm all test hooks are off.
   **Do not leave the live raid running during lengthy analysis.**

The runner deliberately leaves godmode/ammo enabled until protected exit so the
player cannot die during the handoff to the leave recipe. They end with the raid.
The combat loop reapplies godmode about every two seconds: UiBridge 0.9.1 binds
its kill guard to the health controller, which a new wave kit can replace.
On failure, finish that cleanup instead of merely stopping the shell process.
Do not replace the saved profile to conceal failed protection.

## Measurement

UiBridge's status P95 covers only the last 300 frames. This harness instead reads
its existing raw `float[300]` ring through the read-only inspector. Two pages are
bracketed by ring cursors and Unity frame counts; potentially overwritten slots
are discarded and recovered in later polls. Unproven wraps form separate segments.
Frame-count brackets come from `status.bridge.frame`, the same native Unity
counter without a temporary weak inspector wrapper. The ring itself remains
owned by the live plugin. Record the runner hash: earlier trials used inspector
Time handles, so their measurement overhead is not identical.

Analyze the interior of the combat phase, excluding setup, warmup and final drain.
Never average window percentiles. Missing frames count as infinitely slow for the
conservative P95 criterion; a whole-run pass additionally requires complete phase
coverage, the requested workload duration (250 ms timestamp tolerance), matching
process/map, one segment and no capture errors. Check actual
near-cap population, enough successful shots/kills, movement and repeatability
separately. `qualifiedPass` alone does not prove a sufficient gameplay workload.

Automated analyzer checks:

```powershell
node --test tools/ZombiePerformanceChecks/ring-analysis.test.mjs tools/ZombiePerformanceChecks/run-integrity.test.mjs
```

Raw JSONL/profile/log evidence can contain private identifiers. Keep it ignored;
commit only reviewed aggregate results and hashes. `compare-profile.mjs` reports
changed paths without printing profile values.
