# Zombie kit warming checks

Run `pwsh -NoProfile -File tools/ZombieKitWarmupChecks/Verify.ps1` from the repo.
The standalone project never builds or deploys a mod. It links the production
controller, both preloader partials, scope policy and preload plan; only the
Unity/EFT host, kit catalogue suppliers and unrelated controller services are
doubled. Native pool tasks are explicitly completed or failed by each test.

Coverage: both Zombie equipment choices at all three loading scopes, reservation
of the actual opening kit, no optional background work even before a match exists
or after a rewarm request, non-Zombie loading/background behavior, PFT and ordinary
raid exclusion, in-flight adoption, required opening/cold-item loads, same-kit
refill, failed-load retry and a mismatched arm epoch. Ten compiled source faults
must fail named behavioral assertions; compilation failures do not count.

This verifies scheduling and required preload ownership, not Unity pool creation
cost, frame rate, actual weapon draw or native intermission body replacement.
The existing Zombie solo/team and TDM warmup checks remain separate regressions.
