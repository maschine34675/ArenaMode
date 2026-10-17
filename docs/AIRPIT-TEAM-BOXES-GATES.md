# AirPit team boxes — acceptance, 2026-10-17

OWNS: ArenaMode.Shared/ArenaTeamBoxCatalog.cs, ArenaMode.Tests/**, tools/**, AGENTS.md, docs/**

Scope: Original AirPit USEC north / BEAR south boxes for independent TDM,
deployment and native GPU1 acceptance. Jumping over turnstiles remains permitted.
Root owns implementation; read-only advisers independently inspect geometry and
integration. No adviser may edit, build, deploy or operate the GPU host.

- [x] G1: Exact authored formation and strict protected-clone admission pass automated tests.
  CHECK: pwsh -NoProfile -File tools/AirPitTeamBoxChecks/Verify.ps1
  EXPECT: AIRPIT_TEAM_BOX_CHECKS_PASSED
  CWD: ..
  EVIDENCE: automatic-evidence=v1; definition-sha256=ab7d4488fdafe3f4abf4318f2dd360aef0bb59c3edd73a5e4cd5dc3ca4fa99be; exit=0; EXPECT=matched; output-sha256=9558d0631b0acd58104dc8fa49ad212a12d85431182ef30a55a99cad0b9b99f5; output-bytes=934; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode-airpit-boxes; path=5efda2e351da/33 entries
- [x] G2: Independent geometry and adversarial implementation review have no unresolved defect.
  EVIDENCE: Two independent read-only reviews verified exact v9 transforms/U8 coverage and found no static implementation defect. Runtime318 +12 rejected mutants; gates197 +21 rejected mutants. Native proof remains G4.
- [ ] G3: Final clean build preserves concurrent changes and installs only owned artifacts.
  EVIDENCE: pending; staged manifest, baseline hashes, canonical path and GPU1 loaded-code verification.
- [ ] G4: GPU1 demonstrates original team starts, preparation gates, both-team departures and respawns.
  EVIDENCE: pending; native 3v3 and 10v10, all twenty paths after opening, real walk and replacement evidence.
- [ ] G5: Protected raid teardown restores the actual entry profile; test settings and unrelated assets preserved.
  EVIDENCE: pending; actual entry-backup comparisons, config and artifact hashes, final commits and delivery report.

Native observation, exact deployment/loaded-code comparison and independent
source review remain manual. The staged build manifest binds source and binaries.
