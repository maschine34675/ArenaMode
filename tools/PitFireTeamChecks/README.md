# PitFireTeam compatibility offline checks

This executable compiles ArenaMode's production `PitFireTeamSelectionPolicy` and
`PitFireTeamRaidStartPolicy` directly. It fault-injects absent, solo, authenticated, stale,
duplicate, mixed, oversized and unsupported rosters, plus ready and unavailable server
persistence contracts. It also checks the source-level boundary that keeps PitFireTeam
optional, lets its accept-screen roster callback run, revalidates the live roster before
arming the server, and restricts the first compatibility release to one Tactical round
without a player-body swap.

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
