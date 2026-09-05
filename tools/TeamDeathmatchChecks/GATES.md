# Gates: standalone USEC vs BEAR team deathmatch

- [x] G1: Team mode is selectable in native/F12 UI, configuration is bounded, and the exact server raid clone suppresses ambient population while preserving normal modes.
  CHECK: pwsh -NoProfile -File tools/NativeModeSelectionChecks/Verify-TeamDeathmatchRegression.ps1
  EXPECT: ARENA_TEAM_DEATHMATCH_SETUP_REGRESSION_PASSED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=666e00fa291a604601e8b308e7ad4027abad462610189c59aa4cb94d1068152b; output-bytes=341
- [x] G2: Equal team capacities include the human, refill only dead bot slots, reject stale async work, and remain independent of PFT.
  CHECK: pwsh -NoProfile -File tools/TeamDeathmatchChecks/Verify-TeamDeathmatchChecks.ps1
  EXPECT: ARENA_TEAM_DEATHMATCH_POLICY_REGRESSION_PASSED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=d3dbf462d8c74894dcf800dd7bf1c8b977130b09d5a724049c684628305d58c4; output-bytes=590
- [x] G3: Same-faction friendship and attributed damage protection agree with opponent targeting, scoring, awards, scoreboard and tracers through local/bot respawns.
  CHECK: dotnet run --project tools/TeamDeathmatchRuntimeChecks -c Release
  EXPECT: ARENA_TDM_RUNTIME_CHECKS_PASSED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=1a2d55931e700b1cc34a52a02988779e60c4d8ea7cc546f9d771209a9445c3a8; output-bytes=96
- [x] G4: Native API verification, installed SAIN checks and independent adversarial review find no unresolved correctness defects; existing regressions pass.
  CHECK: pwsh -NoProfile -File tools/TeamDeathmatchChecks/Verify-ExistingModes.ps1
  EXPECT: ARENA_TDM_EXISTING_MODES_VERIFIED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=9af46cbe52bc38c1742e9b3bcf0decbd9e20bfeb9e787310f31983901cd14439; output-bytes=1280
- [ ] G5: Client/server Release artifacts are deployed and hash-matched, documentation and completed changes are committed with a clean working tree.
  EVIDENCE: pending
- [ ] G6: GPU tests verify USEC and BEAR player sides, full population/refill, friendly bots, opposing combat, respawns, team score/tracers and ordinary/PFT raid isolation.
  EVIDENCE: unavailable on this server

ABANDON: G6 The server has no GPU; native Unity/SAIN playtests require the user's gaming client after game/server restart.
