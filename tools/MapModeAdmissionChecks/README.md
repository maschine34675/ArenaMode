# Map/mode admission checks

Run `pwsh -NoProfile -File tools/MapModeAdmissionChecks/Verify.ps1 -SptRoot D:\SPT41`.
This compiles the real shared catalog/participant policy and client spawn planner
with minimal native vector/marker boundaries. It requires the installed Customs,
Equator and Bay5 JSON records; it fails rather than silently skipping absent data.
No game process, deployment, settings or profiles are changed.

Checks cover supported mode pairs, the concrete Equator five-opponent limit,
bounded TDM-only ownership, exact frozen totals/team sizes, retired/wrong-map
contours and unidentified legacy populations. Installed opening markers are
planned by the production selector; the Equator center is a real counterexample
to equating safe initial starts with Tactical multi-round capacity. Six compiled
faults must each fail at their intended assertion, after the positive baseline.

This is database/rule evidence, not NavMesh, physics or live round acceptance.
