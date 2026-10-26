# Domination policy checks

Run `pwsh -NoProfile -File tools/DominationPolicyChecks/Verify.ps1` from the repo.
This links both production objective policies plus the unchanged TDM policy;
it never builds or deploys mod projects.

The capture policy has no world, body, menu or match phase. The sibling match
policy owns readiness/countdown, the live interval, deadline and frozen result.
The native caller gathers exact living eligible occupants and supplies simulation
timestamps. Steps must be positive, finite and at most 30s; the native caller
should sample much more frequently to avoid stale occupancy. Repeated/backward
or invalid timestamps integrate nothing. A gap over 30s discards unobserved
capture/income and rebases the sample time, while still honoring the deadline.

Neutral capture takes five seconds by default, independent of occupant count.
Enemy-only presence takes five seconds to neutralize an owned point, then five
more to capture it. Both teams present freeze progress and income. Empty points
retain partial progress and ownership; empty owned points keep scoring. A hostile
occupant pauses old-owner income immediately. Friendly presence restores an
attacked owned point while scoring continues. Every owned/unopposed point pays
one point per second, with fractional contributions retained per team. A step
that completes a capture pays only for its remainder after capture.

The match defaults to 200 points or ten minutes and shares only phase/result
enums and the three-second readiness countdown with TDM. Either limit may be
zero to disable it. Income changes slope only when points finish capturing, so
the score-limit crossing is solved analytically from fractional team income.
Only that prefix changes points/scores. Equal simultaneous crossings draw; a
score crossing exactly at the deadline uses the score-limit reason. The player's
selected faction maps USEC/BEAR into friendly/opponent results. Terminal state
does not accept later samples or faction changes. A failed sample does not
consume the match clock. No native body or roster failure is handled here.

Tests cover both teams, delayed neutralization, contested/empty states, actual
post-capture income, float partition boundaries, invalid input atomicity,
immutable views, exact threshold/deadline clipping, readiness/pause/terminal
semantics and deterministic randomized faction/partition checks. Mutants
must fail the intended behavioral assertion. Native occupancy, AI movement,
visuals and raid/result isolation remain root integration/GPU acceptance.
