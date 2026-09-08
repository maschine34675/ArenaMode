# TDM preparation ownership and native submission checks

OWNS: ArenaMode.Client/Modes/TeamDeathmatchMode.cs, ArenaMode.Client/Modes/TeamDeathmatchPopulation.cs, ArenaMode.Client/Bots/BurstSpawnService.cs, tools/TeamDeathmatchRuntimeChecks/**, tools/TeamDeathmatchChecks/**, TDM-PREPARATION-REVIEW.md

- [x] G1: Production policy accepts valid slow preparation, preserves capacity and rejects stale native admission without replacing unresolved loads.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/TeamDeathmatchChecks/Verify-TeamDeathmatchChecks.ps1
  EXPECT: ARENA_TEAM_DEATHMATCH_POLICY_REGRESSION_PASSED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=ba395d1e8ff509adb42bc107f578d6a82035a82d10a36488f2201d5b710c09bc; output-bytes=775

- [x] G2: Production mode and extracted async burst methods preserve all phase, exact-raid and callback boundaries under targeted faults.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/TeamDeathmatchRuntimeChecks/Verify-Regression.ps1
  EXPECT: ARENA_TDM_RUNTIME_REGRESSION_PASSED
  CWD: ../..
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=726f9e4039fb2ca801d53fcc09f05f2be5dd7d98ed38b359cc10cbc66e90e23e; output-bytes=1307

Parent handoff: independent final review, full staged client build, deployment and
commits are outside this leaf. Repeated cold GPU rounds remain explicit runtime
acceptance; neither gate claims NavMesh, asset performance or live SAIN proof.
