# Optional Zombie Crowd regression checks

`dotnet run --project tools/ZombieCrowdChecks -c Release` links the actual Crowd
runtime, policy and bounded recall adapter. Native world, navigation and physics
are controlled doubles; no game DLL, build/deploy target or live profile is used.

`pwsh -File tools/ZombieCrowdChecks/check.ps1` rejects five behavioral mutations
(raw candidate, uncleared offsets, ignored OFF switch, occupied slot, skipped
physical clearance), then reruns the real source. Mutation files/logs are ignored
under `artifacts/zombie-crowd-checks`; production files are never rewritten.

Coverage includes exact sampled destinations, bounded fail-closed guards,
physical floor/capsule and saturated query buffers, all-occupied rings, excluded
roles/followers/stale bodies, shooter sight, live ON-to-OFF behavior, and both
native avoidance offsets. Doubles intentionally provide no renderer/animator
API: this runtime must not own pooled visual state again.

This is not a Unity geometry, Harmony ordering, spectator or performance proof.
Native menu, full-map and bounded-map smoke evidence belongs in the dated review.
