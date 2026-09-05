# Gates: Labyrinth spawn traps and long raid review

Scope: Disable the ten verified entrance traps only in exact active Arena raids, including their damage, firing, animation and continuous audio. Independently inspect the user's long raid without guessing at FPS or memory effects. Root owns all writes; delegated native research and log review are read-only.

- [x] G1: Installed scene and native methods confirm the complete four-switch, ten-hazard, six-effect, three-animation, four-loop contract without touching unrelated Labyrinth puzzles or normal raids.
  EVIDENCE: Read-only Inspect-InstalledScene.py with Python 3.12/UnityPy 1.25.3, PowerShell, cwd D:\SPT41\Development\ArenaMode, exit 0 and ARENA_LABYRINTH_TRAP_SCENE_VERIFIED. level552 SHA256 d39b3c4606d51fe9699319ef1da4f671f129843c1bcd7bc8f23efd68436bbfeb. Verified all ten identities/configs/enter-exit IDs, six effect handlers, exact false controls, all three animator Boolean actions and target ownership, four autoplay loops with 0.1s end fade. Native contracts and safety reasoning are in LABYRINTH-SPAWN-TRAPS-REVIEW.md.

- [x] G2: Production-linked checks cover exact raid/scene/handler ownership, early and repeated trap events, pending damage/audio, once-per-raid settings, drift and teardown.
  CHECK: powershell -NoProfile -ExecutionPolicy Bypass -File tools/LabyrinthTrapChecks/Verify-Regression.ps1
  EXPECT: ARENA_LABYRINTH_TRAP_REGRESSION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=1fa0479ddf938368389c9157237f2b89f6de84a6e627eaab60e0a266cf8f99f9; output-bytes=228

- [x] G3: Release deployment and affected regression checks pass; independent adversarial review has no unresolved findings; completed changes and documentation are committed.
  EVIDENCE: Final client Release build (including Redeploy.Core Release) passed with zero errors and four existing CS8632 warnings. Verify-NativeMenuDeployment.ps1 passed. Built/installed client SHA256 6F1A3311DF3F155B1E59E4DA12DD04CE7C004B23FADDD01A1E6861C310E55A35; Redeploy.Core 3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C. The new UnityEngine.AnimationModule compile reference has Private=false; its DLL is absent from client Release output and installed plugins. Production-linked trap checks passed 297 assertions plus four required failing source-copy controls; existing server tests 135/135, announcer checks 142, alarm checks 91 passed. Independent native adversarial review found a P1 recursive audio-release bug, corrected and regression-tested before the final review reported no unresolved P0-P2 findings; the final packaging-only reference change was reviewed separately. Implementation, checks and this evidence are committed together; long-raid report is separate commit 1fee69f. No live GPU test is claimed.

- [x] G4: Long raid findings cite measured counters and source/log evidence, distinguish successful completion/profile restore from cleanup noise and uncertain third-party faults.
  EVIDENCE: Independent read-only review plus root log/native-source verification; committed report 1fee69f. 158 kills/47 deaths, six ladders, 53/53 callout play requests/0 skipped, 649 corpses, protected profile restoration confirmed server-side. 5640 corpse activity errors correlate with StripGear's Succeed-only notification and native ProcessActivity Begin requirement; no inventory-loss claim. First/last ten pipeline averages 0.393/0.962s are qualified as a timing trend, not FPS or leak proof. Native teardown and third-party stacks are separately described with uncertainty and exact log/source references. No cleanup/lifecycle code was changed.

- [ ] G5: After client restart, a GPU raid verifies all four entrance trap types are visibly disabled and harmless without respawn protection, through deaths/rounds, while normal raids retain their traps.
  EVIDENCE: unavailable on this server

ABANDON: G5 The server has no GPU. Newly disabled traps require a live test on the user's gaming client; the previous long raid predates this change.
