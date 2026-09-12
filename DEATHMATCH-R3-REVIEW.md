# Deathmatch GPU r3 triage — 2026-09-12

The immutable report is `GPU-DEATHMATCH-MENU-TEST-RESULTS-r3.md` (ee0a421).
It ran the prior client 4F33B337..., not the later four refinements.
Two menu cycles and the observed C1/C2 round rules passed. C2 exited with
three exceptions; profile comparisons found no persistent gameplay delta,
with the explicitly reported Hideout heartbeat timestamp exception.
No new GPU test was run on the development server.

## R3-01: duplicate terminal result

The original C1 terminal screenshot shows the independent HUD canvas banner
above the F9 result text. `ArenaAssemblyBanner` now hides only terminal result
notices while the Arena panel is visible in BattleUI. Preparation/assembly
notices remain unchanged. F9 already displays the heading, detail and exit timer;
closing it immediately restores the banner. Panel transitions refresh the notice
without waiting for the HUD tick, and disable/destroy releases panel visibility.
Optional HUD refresh errors cannot prevent input/cursor cleanup. No scoring,
round timer, protection, spawn or native exit changes.

The production-linked ArenaAssemblyChecks passed 44 assertions and rejected
five compiled faults, including restored duplicate results and lost preparation
notices. The previously stale external doubles for RoundsMode/ScorePanel were
updated so this helper again compiles current production code. Independent
read-only review checked the actual panel/banner integration. The helper does
not execute ArenaPanel's IMGUI, Unity draw ordering or live lifecycle events.
Those still require GPU acceptance.

## Additional close-click observation

The reported single-ammunition decrease is not independently reproduced.
The native input path has separate collection/delivery and an immediate final
owner release in `ArenaInputBlocker`; it deserves a focused reproduction.
The installed ClientApplication normally delivers during Update, so a delayed
FixedUpdate delivery is not an established explanation of this particular run.
Do not claim that Event.Use alone clears native commands, or that the banner
change fixes firing. Retest explicit F9 Close clicks, hotkey and Escape while
recording ammo before/after; keep this observation separate from exit failures.
