# Announcer combat checks

Run `pwsh -NoProfile -File tools/AnnouncerCombatChecks/Verify.ps1` from the
repository root. The console harness compiles full production ArenaMatch,
ArenaCombatAwards and ArenaScoreboard; native mode callbacks are doubled.

Checks cover coincident multi-kill/spree selection with unchanged score formulas,
First Blood and Payback precedence, current physical-life Shutdown admission and
living Clutch versus stale survival, resurrection and post-death spectator wins.
The original win event must still be delivered. A source check binds the real
Tactical caller's final validated survivor observation before its outcome.

The historical spree-first source and nine temporary compiled source faults
must fail at their intended runtime assertions. Build errors are rejected.
Source hashes and logs remain in ignored `artifacts/announcer-combat-checks/`;
no production file, installed DLL or game process is changed. These checks are
not native raid or audible playback proof.
