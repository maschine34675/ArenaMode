# Killfeed identity, distance and team-color checks

Run `pwsh -NoProfile -File tools/KillfeedTextChecks/Verify.ps1` from the repo.
The harness links the actual formatter, captured kill model, role enum and
match-owned tracer team registry. Only the registry's unrelated patch-diagnostic
reset is inert in this harness.
It verifies complete attacker/victim names in the headline for local kills,
local deaths and other kills, with metadata below and literal handling of
markup-like input. Measured distance is included for every kill direction;
unknown, zero, rounded, negative and non-finite distances are distinguished.
Compiled source faults remove the local attacker again, restore the old
local-only distance rule, or disregard unknown distance. The respective
behavioral assertion must reject each fault. Two further compiled faults paint
opponents friendly or unknown identities hostile and must also be rejected.

Both names use player-relative team colors, not actor roles or the direction of
the kill. The real registry is exercised for solo Arena, admitted TDM actors,
protected PFT rosters, historical IDs after a valid round rebind, invalid or
missing rosters, unknown identities and match reset. The formatter does not
write scoring metadata. Customized color strings affect new entries; an already
formatted entry keeps its original colors.

The PFT fixtures explicitly freeze four companions through the variable-squad
registry API. The deliberately short replacement roster still expects four,
so this negative case cannot accidentally become an accepted one-person squad.
The 2026-10-16 API repair passed all 120 assertions and six compiled faults;
it changes the fixture calls only, not gameplay or assertions.

KillfeedView reads FriendlyTracerColor/OpponentTracerColor for each Push and
normalizes the shared HDR palette with the existing friendly-marker UI policy,
then converts RGB to an opaque HTML color. This Unity adapter and actual F12
setting changes are not executed by this harness. The synchronous OnKill event
runs before mode.OnKill; presentation queries the registry while that match is
still bound. No delayed body lookups or event-time reclassification are added.

These checks do not instantiate Unity or TMP and do not execute the Harmony
capture hook or prove line wrapping, preferred-height measurement or rendering.
The hook reads the original aggressor and victim positions at death, never a
profile-ID lookup that could select a respawned body. The real client must still be built
against the installed game. GPU acceptance must cover long two-name entries,
all three kill directions, team colors/settings, wrapped metadata, six live entries, expiry and reuse
of a tall row for a short entry. The dark background must contain all text and
the following entry must start below it. Do not change an active GPU job's DLL
or hash binding to run these checks.
