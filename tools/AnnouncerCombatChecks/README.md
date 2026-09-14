# Announcer combat checks

Run `pwsh -NoProfile -File tools/AnnouncerCombatChecks/Verify.ps1` from the
repository root. The console harness compiles full production ArenaMatch,
ArenaCombatAwards and ArenaScoreboard; native mode callbacks are doubled.

Checks cover coincident multi-kill/spree selection with unchanged score formulas,
First Blood and Payback precedence, current physical-life Shutdown admission and
living Clutch versus stale survival, resurrection and post-death spectator wins.
The original win event must still be delivered. A source check binds the real
Tactical caller's final validated survivor observation before its outcome.

Shot awards cover airborne hip fire, bolt-action hip fire and their overlap,
First Blood / Payback / Shutdown / multi precedence, shot awards before sprees
and Headshot, unchanged kill/headshot/streak points and closed/friendly admission.
These consume already verified shot-time scalar facts; capture identity, direct
impact and native hooks are checked separately. Run the combined
`tools/AnnouncerChecks/Verify-ShotAwards.ps1` for additional compiled shot-order
faults and provisional recording/queue regressions.

The historical spree-first source and nine temporary compiled source faults
must fail at their intended runtime assertions. Build errors are rejected.
Source hashes and logs remain in ignored `artifacts/announcer-combat-checks/`;
no production file, installed DLL or game process is changed. These checks are
not native raid or audible playback proof.
