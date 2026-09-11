# Killfeed identity regression checks

Run `pwsh -NoProfile -File tools/KillfeedTextChecks/Verify.ps1` from the repo.
The harness links the actual formatter, captured kill model and role enum.
It verifies complete attacker/victim names in the headline for local kills,
local deaths and other kills, with metadata below and literal handling of
markup-like input. A compiled source fault removes the local attacker again;
the corresponding identity assertion must reject it.

These checks do not instantiate Unity or TMP and do not prove line wrapping,
preferred-height measurement or rendering. The real client must still be built
against the installed game. GPU acceptance must cover long two-name entries,
all three kill directions, wrapped metadata, six live entries, expiry and reuse
of a tall row for a short entry. The dark background must contain all text and
the following entry must start below it. Do not change an active GPU job's DLL
or hash binding to run these checks.
