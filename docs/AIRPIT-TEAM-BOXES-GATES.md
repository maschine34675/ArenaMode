# AirPit team boxes — acceptance, 2026-10-17

OWNS: ArenaMode.Shared/ArenaTeamBoxCatalog.cs, ArenaMode.Tests/**, tools/**, AGENTS.md, docs/**

Scope: Original AirPit USEC north / BEAR south boxes for independent TDM,
deployment and native GPU1 acceptance. Jumping over turnstiles remains permitted.
Root owns implementation; read-only advisers independently inspect geometry and
integration. No adviser may edit, build, deploy or operate the GPU host.

- [x] G1: Exact authored formation and strict protected-clone admission pass automated tests.
  CHECK: pwsh -NoProfile -File tools/AirPitTeamBoxChecks/Verify.ps1
  EXPECT: AIRPIT_TEAM_BOX_CHECKS_PASSED
  (Historical: the folder merged into tools/BowlTeamBoxChecks on 2026-10-23; run that Verify.ps1,
  which now includes MapModeAdmissionChecks and the installed-snapshot comparison.)
  CWD: ..
  EVIDENCE: automatic-evidence=v1; definition-sha256=ab7d4488fdafe3f4abf4318f2dd360aef0bb59c3edd73a5e4cd5dc3ca4fa99be; exit=0; EXPECT=matched; output-sha256=16895b6ab555e26b5212b5508052af419e577cf9766ddcd9aef70960280de3b9; output-bytes=8347; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode-airpit-boxes; path=5efda2e351da/33 entries
- [x] G2: Independent geometry and adversarial implementation review have no unresolved defect.
  EVIDENCE: Two independent read-only reviews verified exact v9 transforms/U8 coverage and found no static implementation defect. Runtime 318 +12 rejected mutants; gates 197 +21 rejected mutants. Native proof recorded under G4.
- [x] G3: Final clean build preserves concurrent changes and installs only owned artifacts.
  EVIDENCE: Clean source 132549d includes cd2ede5; manifest artifacts/area-map-delivery/7a5bdfe518554030928f37ee18aacacc/manifest.json has no source changes. All 186 installed files unchanged while staging. Canonical deploy plus server DLL synchronized; restarted GPU1 instance 755a2aff loads client MVID 70db91b5-3816-43fe-a76d-0bb6a5d92b14. All 19 maschine plugins and Core match development/host/loaded. Exact hashes in AIRPIT-TEAM-BOXES-REVIEW.md.
- [x] G4: GPU1 demonstrates original team starts, preparation gates, both-team departures and respawns.
  EVIDENCE: Native 3v3 and 10v10 passed in successive worlds: 10/10 safe positions per side, one hold/release each, all twenty complete outside paths before Live, player north-03/north-10 and respawn north-08/north-04, actual walking out of both boxes, correct bot replacements. Full log and walk results under artifacts/airpit-native-20261017. One 253.6 m south-box detour timed out after exiting; next walk arrived. No gate/respawn/teardown failure; caveat retained in review.
- [x] G5: Protected raid teardown restores the actual entry profile; test settings and unrelated assets preserved.
  EVIDENCE: Both whole profiles equal their actual server entry backups except Hideout timestamp, 424/424 items each. preservation-verified.json confirms 51 other plugin DLLs, five map files, 33 PFT files, server/MapPort configs unchanged. Own mode/participant preferences restored; concurrently changing synchronized Zombie preferences preserved rather than overwritten. MainMenu, godmode off, free control lease; review and handoff updated.

Native observation, exact deployment/loaded-code comparison and independent
source review remain manual. The staged build manifest binds source and binaries.
