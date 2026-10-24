# PitFireTeam compatibility offline checks

This executable compiles ArenaMode's production `PitFireTeamSelectionPolicy` and
`PitFireTeamRaidStartPolicy` directly. It fault-injects absent, solo, authenticated, stale,
duplicate, mixed, oversized and unsupported rosters, plus ready and unavailable server
persistence contracts. It also checks the source-level boundary that keeps PitFireTeam
optional, lets its accept-screen roster callback run, revalidates the live roster before
arming the server, and guards the Tactical best-of-N lifecycle: exact old-body teardown,
fresh follower/opponent adoption, one-shot prepared PMC respawn, spectator/pursuit wiring,
and an overall match result without a raid-start body swap.
The supported protected modes are Tactical and Zombies, each with exactly four
authenticated stored companions. Zombie admission runs its own negative roster,
Fika/co-op, special-companion and missing-persistence cases; it never inherits a
relaxed generic team predicate. Runtime Zombie wave/rebuild checks live separately.

When the optional client plugin is installed, the check also reads the installed
`pitFireTeam.dll` and `Assembly-CSharp.dll` as metadata (it does not execute either
assembly). This verifies the installed identity against the 0.10.0/0.10.3/1.0.0
reviewed baselines and forward admission, the selected-roster type, the
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

The production policies also run canonical `deathmatch` and
`teamdeathmatch` through solo, Tactical-only, pending selection and server gates;
legacy `endless`/`rounds` cases remain. Authenticated teams are allowed before
mode selection, but final team arming must remain restricted to the two supported
protected modes. Zombie cannot bypass exact roster identities or server persistence
readiness. Mode comparison remains case-insensitive, but unknown aliases are denied.

## Protected Zombie extension (2026-10-08)

**309 assertions** pass, including installed optional PFT metadata checks. The
mode-freeze source gate now preserves both accepted protected modes. Respawn
authorization still requires the exact coordinator-owned lease, and the solo
Zombie own-gear path explicitly excludes a frozen PFT roster.

The initial-body source check accounts for the intervening solo own-gear branch
instead of requiring the old adjacent team/kit branch layout. Direct negative
controls reject an inverted team condition, a broken `else` chain and a missing
solo kit swap. The solo kit-selection ordering marker names the executable
declaration, so an explanatory comment cannot accidentally satisfy it.
