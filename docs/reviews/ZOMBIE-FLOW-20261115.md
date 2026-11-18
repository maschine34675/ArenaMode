# Zombie recovery, planning budget and wave variety

2026-11-15. Original Arena implementations inspired by the reviewed techniques in
Zombie Control and SPZombies; no third-party code, assets or dependencies added.
This extends the historical [Zombie contract](ZOMBIE-MODE-REVIEW.md).

## Individual recovery

Only confirmed Arena arrivals in the exact active Zombie match can be replaced.
The body must remain within 0.75 m for 20 seconds, outside every live survivor's
8 m proximity and physically occluded at head and torso height. Recent damage,
shooting, melee hits, visible targets and native standby reject observations.
Native door work also excludes the physical-stall proof below.

A fresh pursuit request precedes either two failed route passes to all stable
survivors or 15 seconds of renewed movement input without physical progress on
a complete route. Each candidate gets at most 30 seconds; one native route probe
is allowed per 0.25 seconds. Moving/replaced survivors, paused/rebinding/terminal
matches and changed body/world identities discard observations.

Administrative death uses the existing unscored gate. The captured health
controller must actually report death, including after a throwing death callback.
Exactly one request slot is then owed again; pending reservations remain intact,
and this death contributes neither kill/assist/reward credit nor watchdog progress.
Each wave permits at most `min(8, max(1, waveSize / 5))` replacements. Dead entries
are pruned even when recovery is disabled or this allowance is exhausted.

The existing saturated-wave watchdog remains the final fallback. Setting its
timeout to zero disables both recovery stages. Its default stays 100 seconds.

## Native planning budget

Owned infected share two complete planning jobs per frame between native
`MoveToEnemyZigZag.NotMovingCheck` and close-melee replanning. A due approach check
is conservatively counted even if its native early return avoids computation.
An admitted job retains native zigzag searches and all native fallbacks.
This bounds jobs, **not individual CalculatePath calls or milliseconds**.
Recovery probes have the separate bound described above.

The bounded FIFO permits at most one job per body per frame and drops stale
body/target tickets and heads absent for two frames. Exact match/world reset
clears all state. Deferral leaves retry clocks and existing paths untouched.
Melee attempts, posture, sprint and steering run before its budget boundary.
The guard changes Arena's original reverse copy and its fallback, without
unpatching another mod or intercepting global NavMesh operations. Exceptions in
budget admission retain native movement and produce a bounded warning.

Developer diagnostics report admitted jobs, deferrals, queue size and wait.
These counters do not establish an FPS gain or justify increasing population caps.

## Wave composition

See also [DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md): it plans a fade of the
slow share and a Reduced population limit on top of this composition. As of 2026-11-18 that part
is not implemented; the text below is what the code does.

The configured shooter chance remains unchanged, including exactly 0% and 100%.
Among the remaining melee infected, the slow share is 75%, 50% and 25% in waves
1–3, then repeats 40%, 20%, 30%. The remainder uses the native fast variant.
Native difficulties map `easy` to slow, `normal` to fast and `hard` to shooting.
Role weights, wave quotas, population caps and equipment are unchanged. No
additional health or damage scaling is introduced; boss and impossible variants
remain excluded.

The opening profile plan uses wave one's composition. Backup preparation covers
all possible later role/difficulty pairs while excluding melee at 100% shooters
and shooters at 0%. Settings remain frozen for the raid. No additional menu rows
or gameplay settings are introduced.

## Verification boundary

`ZombieRecoveryChecks`, `ZombiePathBudgetChecks` and `ZombieCompositionChecks`
link production code and reject compiled faults. The path checks also pin the
installed native call graph and execute the label-sensitive transpiler fixture.
Existing pending-spawn, solo and protected-team checks retain their separate
contracts. Source checks do not establish native geometry or gameplay acceptance;
the dated native report records the actual candidate and exercised scenarios.
