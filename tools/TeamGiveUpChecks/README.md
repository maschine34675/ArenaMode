# Team DM voluntary deaths and exact visual owners

Run `dotnet run --project tools/TeamGiveUpChecks -c Release` and
`pwsh -NoProfile -File tools/TeamGiveUpChecks/Verify-Regression.ps1` from the
repository. These executables do not reference deployment projects or write to
SPT configuration, user profiles or installed DLLs.

The harness directly links production `ArenaGiveUp`, `TeamDeathmatchMode`,
`ArenaMatch`, `ArenaScoreboard`, combat awards, population, relations, and the
administrative-death gate. It shares the native/Unity model from
`TeamDeathmatchRuntimeChecks` using a compile symbol to replace its stub match
with the real match. Only keyboard/UI input, native body/health/bot APIs and the
respawn driver's notification are modeled. The existing runtime checks continue
to compile without this symbol.

Coverage includes both PMC factions; exactly one opposing team kill and the
normal PMC base points for a successful Give up; no invented personal killer,
killfeed event, headshot/streak bonus, First Blood consumption or revenge award;
unchanged local-death accounting; duplicate and synchronous reentrant requests;
suppressed Kill; exceptions before and after actual death; invalid match, world,
mode, body, profile, faction and health bindings; environmental deaths; typing,
hotkey, respawn and intermission guards; ordinary-mode isolation; and team total
persistence through actual scoreboard history compaction.
Stopped or failed Team DM matches retain the existing Give up escape action;
scoring eligibility is deliberately separate from permission to end that life.

The read-only visual-owner API is tested with current local bodies, both admitted
bot teams, inactive wrappers, unknown bots, stale profile aliases, role drift,
death, replacement bodies and ended/replaced matches. It never registers an
unknown actor just to choose a color.

The mutation runner changes scratch copies under this project's ignored `obj`
directory and requires each to fail its intended behavioral assertion. Compiler
errors do not qualify. It checks original source hashes and finishes by rerunning
the unchanged production baseline.

Native evidence: decompiled `EFT.HealthSystem/ActiveHealthController.cs:3549`
sets `IsAlive = false` before notifying death subscribers. Arena's respawn driver
raises its usual local-death notification and yields for kit preloading before
swapping the body. A death subscriber may therefore throw after death has
succeeded; the Give up completion checks actual health in `finally` and still
requires the original current match/body. No Unity/GPU playtest is implied by
these offline checks.

Review passes: (1) implemented the whole action/scoreboard path; (2) verified
native death notification and respawn ordering, including subscriber exceptions;
(3) corrected the independent review finding that ineligible scoring must not
block the existing escape action, and added reentrancy/failed-match regressions;
(4) checked exact visual body ownership, unchanged other-mode behavior, bounded
state, scoreboard compaction and scratch-only mutant execution. Final verification
runs production checks, behavioral mutants and the unchanged runtime harness.

## Bounded-area consumer contract

The 2026-09-07 standalone run passed 385 assertions. Added 2v2 cases verify that
giving up grants the opponent exactly one kill, pending area/respawn work cannot
duplicate that credit, and an accepted replacement permits a new life. The
shared `AreaStandIns.cs` supplies the readiness/position verdict only; actual
scene validation, physical respawn and protected native exit are outside this
harness. All previous give-up, typing and lifecycle assertions are retained.
