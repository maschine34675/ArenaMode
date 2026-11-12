# ChopShopChecks

`Verify.ps1` runs the Chop Shop integration checks against the installed ArenaMapPort data
(`user/mods/ArenaMapPort/db/arena_autoservice/base.json`, 4 Player and 7 Bot rows, exactly
the seven the Tactical refusal and the default of six participants were argued from), the
filtered Chop Shop xunit classes and the PFT compiled faults; it fails loudly when the
installed MapPort build ships no `arena_autoservice`. The location id is MapPort's, the
player-facing name is Chop Shop. Contract: `docs/reviews/CHOP-SHOP-INTEGRATION-20261112.md`.

There is no `scenarios/` folder yet: the native ArenaMode acceptance cycle on GPU-2
(held gates, formation, Domination, the Tactical refusal) is still pending, and its gpu-run
scenarios are added when it runs. Until then every "native" statement in the contract is
limited to the ordinary PMC raid measurement of 12.11.2026.
