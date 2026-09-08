# Gates: independent TDM match flow

Scope: deliver frozen match limits, shared scoring start, terminal result and safe lifecycle without changing other modes. Native GPU playtesting remains explicitly outside offline completion evidence.

- [x] G1: countdown, score/time limits, draws and terminal immutability obey the actual production policy
  CHECK: pwsh -NoProfile -File tools/TdmMatchChecks/Run-Checks.ps1
  EXPECT: TDM_MATCH_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=68c184028c0ce1636926456607359f4608552cd6b6e8d45e6ad40a851189cc68; output-bytes=775

- [x] G2: native-menu rules persist valid choices and stay hidden outside independent TDM
  CHECK: pwsh -NoProfile -File tools/TdmMatchMenuChecks/Run-Checks.ps1
  EXPECT: TDM_MATCH_MENU_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=90b5ece07a97137bfa279a44a0fe0b44058a9ada443af766b951277abac99bf8; output-bytes=541

- [x] G3: the real client compiles against installed EFT and Redeploy without deployment side effects
  CHECK: pwsh -NoProfile -File tools/DoubleJumpChecks/Build-Staged.ps1
  EXPECT: ARENA_DOUBLE_JUMP_STAGED_BUILD_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=854e65857b65f3cb7ea4c2b89df73e0227eb6f3df5059f9b5153f74ff7ba2ddd; output-bytes=2911

- [x] G4: shared banner, area menu, warmup, other modes and server/profile regression suites pass on integrated sources
  CHECK: pwsh -NoProfile -File tools/TdmMatchChecks/Run-Regressions.ps1
  EXPECT: TDM_MATCH_REGRESSIONS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=093ee60b0e3f5c3f74f322bad7281f39381656714c62750a384f2674b91b792f; output-bytes=3810

- [x] G5: independent adversarial review finds no unresolved high/medium correctness issues, including in-flight respawns and native teardown
  EVIDENCE: Independent double_jump_hooks final rereview found no remaining P0-P2 after countdown invalidation, terminal factory failure fallback, paused-stop and give-up deadline fixes. Root verified native owner/Stop source and added actual extracted driver guard tests. Native GPU behavior is not claimed.

- [ ] G6: committed source and locally installed client match, unrelated installed server and Redeploy are unchanged, and GPU acceptance steps are documented
  EVIDENCE: pending
