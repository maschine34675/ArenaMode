# PitFireTeam compatibility offline checks

This executable compiles ArenaMode's production `PitFireTeamSelectionPolicy` and
`PitFireTeamRaidStartPolicy` directly. It fault-injects absent, solo, authenticated, stale,
duplicate, mixed, oversized and unsupported rosters, plus ready and unavailable server
persistence contracts. It also checks the source-level boundary that keeps PitFireTeam
optional, lets its accept-screen roster callback run, revalidates the live roster before
arming the server, and guards the Tactical best-of-N lifecycle: exact old-body teardown,
fresh follower/opponent adoption, one-shot prepared PMC respawn, spectator/pursuit wiring,
and an overall match result without a raid-start body swap.

When the optional client plugin is installed, the check also reads the installed
`pitFireTeam.dll` and `Assembly-CSharp.dll` as metadata (it does not execute either
assembly). This verifies the exact 0.10.0 identity, the selected-roster type, the
inherited `RaidPlayer` identity fields and PFT's own My Squad marker against the
runtime that will actually load.

Run from the repository root:

```powershell
dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
```

Success prints `PITFIRETEAM_COMPAT_CHECKS_PASSED`. These checks do not start EFT or execute
PitFireTeam/Unity code and cannot prove Unity screen layout, AI command handling, spawn
completion, hostility, audible output, or profile neutrality in a live raid. Those remain
manual checks.

## Current mode-freeze contract (2026-09-12)

The old two-argument MarkArmed source expectation stopped this harness after
98 assertions. Unified Deathmatch also freezes its selected rules, so the
contract now requires exactly one call passing location, requestedMode and
deathmatch. Whitespace does not matter; omitted rules, mutable mode, foreign
location and duplicate/mixed calls are rejected by direct predicate controls.
This is a lexical integration check, not execution of Unity's loading pipeline.

The unchanged production policies now also run canonical `deathmatch` and
`teamdeathmatch` through solo, Tactical-only, pending selection and server gates;
legacy `endless`/`rounds` cases remain. Authenticated teams are allowed before
mode selection, but final non-Tactical team arming must still be rejected.
Final root run: **248 assertions**, including installed optional PFT metadata
checks. No production or installed file changes are required for this correction.
