# GPU team-start follow-up

OWNS: ArenaMode.Client/Areas/ArenaAreaTeamRuntime.cs, ArenaMode.Client/Plugin.cs, tools/AreaTeamRuntimeChecks/**, tools/TeamStartAcceptanceChecks/**, TEAM-START-ACCEPTANCE-REVIEW.md, HANDOFF.md

Scope: Integrate double jump into master first, correct the native GPU report's actionable team-start and role-label failures, investigate render/menu errors, and deliver a reviewed client without changing server/profile behavior. Native GPU retesting is an explicit handoff; no GPU exists on this host.

- [x] G1: Production team runtime requires one local PMC plus N-1 allies and N opponents for either faction and rejects genuinely insufficient dispatch pools.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/AreaTeamRuntimeChecks/Verify.ps1
  EXPECT: ARENA_TEAM_RUNTIME_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=2d7f1afb4e32b6ba912deba6f1f01baf409130427a755a4306e4ab2675f8f6e8; output-bytes=2554

- [x] G2: Production connectivity checks verify alternate contained bidirectional routes, bounded work, and fresh validation without allowing unsafe actual bot paths.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/AreaConnectivityChecks/Verify.ps1
  EXPECT: ARENA_AREA_CONNECTIVITY_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=ebd09270eb3e4841952c07cdcabc4fb200147c5a1884700fc4f46147c320e771; output-bytes=1820

- [x] G3: Production role-label checks correctly classify a local PMC with native assault defaults and preserve AI/Scav roles.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/RoleLabelChecks/Verify.ps1
  EXPECT: ARENA_ROLE_LABEL_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=64bf7c4da73b3aa2cbb3a19ec6f0620ea143ecf0117b59394af836f2ed1e0fa4; output-bytes=993

- [x] G4: Integrated double-jump regressions pass after the merge.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/DoubleJumpChecks/Verify.ps1
  EXPECT: ARENA_DOUBLE_JUMP_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=22347cd87b097586b8dc05b00266c6d76dfdae246f2700c204ade7f40b68b54c; output-bytes=1237

- [x] G5: The complete merged client compiles in isolated staging without changing installed client/server/Redeploy artifacts.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/DoubleJumpChecks/Build-Staged.ps1
  EXPECT: ARENA_DOUBLE_JUMP_STAGED_BUILD_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=7061681428f8e67113e53be4a77236ad6158de558eba12e12a91fb3aee7f6d08; output-bytes=2911

- [x] G6: Adversarial reviews have no unresolved consequential defects; the report records the source-supported render/menu findings without speculative global patches.
  EVIDENCE: 2026-09-08 independent tdm_role_label_fix reviewed root roster counts and the new navigation adapter, found stale preparation ancestry and confirmed its final parent-first verification plus targeted fixtures; final review no P0-P2. Root independently reviewed human role classification and the native GPU/PFT ordering guards, their tests and plugin lifecycle; no unresolved P0-P2. All three review documents and TEAM-START-ACCEPTANCE-REVIEW.md distinguish measured offline results from required GPU acceptance and preserve profile/end isolation.

- [ ] G7: Small reviewed commits are integrated into master; local client delivery matches the staged artifact, server/Redeploy remain unchanged, and handoff identifies pending GPU checks.
  EVIDENCE: pending

- [x] G8: Production render/menu guards repair only the exact active Arena replacement-camera and inactive optional PFT warmup cases, with deliberate fault controls.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/GpuAcceptanceCompatChecks/Verify.ps1
  EXPECT: GPU_ACCEPTANCE_COMPAT_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=4e30b6517b056c16a7d7d58a1977cf392e026132522f6c8a4321797218c71640; output-bytes=826

- [x] G9: Existing native-menu, team lifecycle, protected server state and polygon regression checks pass with no installed-artifact change.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/CustomsContourChecks/Verify.ps1 -Stage Integration
  EXPECT: CUSTOMS_CONTOUR_INTEGRATION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=5f89d2f77f783c14d98154be4802cdf14f51d44b534273fe1260d533a45220c9; output-bytes=5527
