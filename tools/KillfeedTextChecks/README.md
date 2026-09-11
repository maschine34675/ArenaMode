# Killfeed identity regression checks

Run `pwsh -NoProfile -File tools/KillfeedTextChecks/Verify.ps1` from the repo.
The harness links the actual formatter, captured kill model and role enum.
It verifies complete attacker/victim names in the headline for local kills,
local deaths and other kills, with metadata below and literal handling of
markup-like input. Measured distance is included for every kill direction;
unknown, zero, rounded, negative and non-finite distances are distinguished.
Compiled source faults remove the local attacker again, restore the old
local-only distance rule, or disregard unknown distance. The respective
behavioral assertion must reject each fault.

These checks do not instantiate Unity or TMP and do not execute the Harmony
capture hook or prove line wrapping, preferred-height measurement or rendering.
The hook reads the original aggressor and victim positions at death, never a
profile-ID lookup that could select a respawned body. The real client must still be built
against the installed game. GPU acceptance must cover long two-name entries,
all three kill directions, wrapped metadata, six live entries, expiry and reuse
of a tall row for a short entry. The dark background must contain all text and
the following entry must start below it. Do not change an active GPU job's DLL
or hash binding to run these checks.
