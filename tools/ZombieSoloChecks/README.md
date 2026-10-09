# Solo Zombie survival death

Run `pwsh -NoProfile -File tools/ZombieSoloChecks/Verify.ps1` from the repository.
The harness compiles the actual Zombie mode, wave/refill policies, match,
scoreboard, result adapter, replay wait policy and native exit coordinator.
Only Unity/EFT I/O and optional compatibility dependencies are doubled.

It covers both equipment choices and native death/kill callback orders, exactly
one final casualty, frozen score/assists/spawn admission, missing or mismatched
native owners, a precommit Stop failure, active native replacement, pause,
optional replay cleanup and timeout, the no-aggressor health backstop, stale
matches, and the living intermission refill control. Deliberate source copies
must compile and fail a runtime assertion; production files are never mutated.

Separate `ZombieTeamChecks/Verify.ps1` retains the protected four-companion
restore regression. These checks do not prove native GPU gameplay or profile
preservation; those require a restarted client and protected raid exit.
