# Bot spawn-shield scope checks

Run `pwsh -NoProfile -File tools/SpawnShieldScopeChecks/Verify.ps1` from the repository.

The harness compiles the real Team Deathmatch mode, match flow, population,
protection leases and team-box catalog. It reuses the existing TDM engine doubles;
it neither deploys a mod nor starts EFT. Independent expected map/mode lists
prevent an accidentally widened catalog from widening the test oracle too.

Coverage includes all three ported box maps and both human factions, ordinary
maps including day/night Factory, other modes, unarmed/PFT/stale raid rejection,
current-match loss, setting changes across replacement bots, disabled/invalid
durations and the existing 30-second cap. Local-player shields are not owned by
these code paths; the model checks that live local damage remains outside bot
spawn admission.

Five compiled fault controls remove map filtering, remove all box-map protection,
admit PFT, reread the duration for refills and remove current-match identity.
Each must compile and fail the named behavioral assertion. The verifier stores
its transcripts below ignored `obj/scope-controls/`, checks source integrity,
then reruns the production baseline. It does not certify native rendering,
Unity object lifetime, scene geometry or actual damage/attack hook delivery;
those remain native test gates.
