# Announcement mode integration checks

Run `pwsh -NoProfile -File tools/AnnouncementIntegrationChecks/Verify.ps1` from
the repository root. This net10 console harness links the actual mode
snapshot adapters, independent-team score adapter, director, progress policy,
DM/TDM rules and both Domination policies. Only native
world/time boundaries and the containing modes' private state are doubled.

It verifies exact-raid admission, current countdown cancellation, finite round
state, real time/kill limits, raw team kills rather than points, delayed solo
Tactical readiness, first-to-one/deciding PFT rounds and equipped Gun Game tiers.
Domination uses objective lead in the selected faction and never announces held
points as remaining kills. Its terminal state comes from its actual policy.
Nine temporary compiled source faults must fail their named assertions; a compile
error is not a successful negative control. Source hashes and logs are written
under ignored `artifacts/announcement-integration/` and originals remain unchanged.

This does not execute full native modes or prove in-game speech. Run the separate
AnnouncerChecks, AnnouncementProgressChecks and AnnouncerCombatChecks suites and
the staged native client build before handing a combined build to GPU testing.
