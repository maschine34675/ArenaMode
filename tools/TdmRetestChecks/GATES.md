# GPU retest follow-up integration

- [x] G1: Optional catalog warming waits for current TDM initial readiness, active native kit-pool loading and player replacement, with existing other-mode scheduling preserved.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/TdmWarmupChecks/Verify.ps1
  EXPECT: TDM_WARMUP_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=4b2d893e6279fd6ed17ebbebb17f1b075b863832375635cb58922ce88a08cbd4; output-bytes=572

- [x] G2: Existing protected server, menu, TDM and area integration regressions pass without live artifact changes.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/CustomsContourChecks/Verify.ps1 -Stage Integration
  EXPECT: CUSTOMS_CONTOUR_INTEGRATION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=58070b8d54ed2d6d841cea9f10e74e2f58f73fd992cc86fba9126ae4b5a72bab; output-bytes=5528

- [x] G3: Full integrated client compiles in isolated staging with installed client/server/Redeploy unchanged.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/DoubleJumpChecks/Build-Staged.ps1
  EXPECT: ARENA_DOUBLE_JUMP_STAGED_BUILD_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=0188d97c7f818ef40afe39615f89a5d1dc59676c304897d5d76dd2cb7215deb9; output-bytes=2911

- [x] G4: Both children are independently reverified and adversarial review leaves no consequential unresolved defects.
  EVIDENCE: 2026-09-08 parent re-executed all three child runnable gates with exit 0 and matched EXPECT (4/4 child gates met including independent native contract review). Policy 14972 assertions/18 faults, runtime 268/17, actual extracted async Burst 21/8, awareness 565/20. Root reviewed phase ownership and callbacks; independent tdm_navigation_diagnosis reviewed hearing, native contracts, counter/scheduler and renderer-free supplies stand-in. Opening-pool ownership, forgotten-contact reacquisition and callback/native-stop findings are corrected; no remaining P0-P2. Early pre-ready combat is documented as a live fairness/performance check without a loading dependency cycle.

- [x] G5: Small coherent commits are integrated into master, client delivery hashes match clean staged source, and handoff separates findings from pending live GPU acceptance.
  EVIDENCE: 2026-09-08 code commits 8fb502c, 8ee1b05 and 4a979cf fast-forwarded into master. Fresh build from clean 4a979cfff1e21de90867786edaf5871298a5b80d produced staged manifest artifacts/double-jump-build/907b9b5c983344b78b993bb40a13a63e/manifest.json (SourceChanges empty, zero errors/four existing warnings). Atomic local client replacement and backup verified: client SHA256 04D47F6DD8B7C9C252C564604819405F89E0ED9F3BD621999032571B8A349C9D; server D7879AEF... and Redeploy 7D950A47... unchanged. Delivery/previous client in artifacts/tdm-retest/delivery-5f45d5094fe54298a490ea46e1f81d9c/. HANDOFF.md and TDM-RETEST-FOLLOWUP.md preserve exact evidence, unresolved archive errors/cold-kit interpretation, client-only restart and native 2v2/5v5 retests. GPU mirror/live-tested both explicitly false; no push.

- [x] G6: Existing production-linked optic preload guards and source-fault controls pass with the new native-load observer.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/OpticPreloadChecks/Verify.ps1
  EXPECT: ARENA_OPTIC_PRELOAD_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=11b94ebc5a99ee21b371305347378d576a3dde868c250fc8757a414247178464; output-bytes=1633
