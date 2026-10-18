# Gates: Arena-owned optional BigBrain library

OWNS: ArenaMode.Client/Plugin.cs, ArenaMode.Client/Compat/ArenaBigBrainLibrary.cs, ArenaMode.BigBrainCompat/**, tools/BigBrainCompatibilityChecks/**, tools/Assert-BigBrainLibrary.ps1, tools/Package-TestRelease.ps1, tools/PickupObjectiveChecks/**, tools/MolotovChecks/**, tools/TeamArenaExpansionChecks/**, docs/**, README.md, AGENTS.md

Scope: Explain Core/server responsibilities from source; replace the extra plugin
with an Arena-owned optional library, retaining absence safety and bot ownership.
No publication or live deployment is required for this code/package change.

- [x] G1: Core/server dependency answer follows actual requests and driver callbacks.
  EVIDENCE: Independent redeploy_guid_audit traced standalone ServerModCheck/ping and inventory/postraid patches; Core has only local respawn/session callbacks. Arena PostRaidPmcSkipPatch and ArenaProtectedRaidEndService own protected profile restoration. See BIGBRAIN-LIBRARY-20261018.md.

- [x] G2: Isolated production bootstrap checks cover optional absence, registration failures, repeated init and teardown.
  CHECK: dotnet run --project tools/BigBrainCompatibilityChecks -c Release
  EXPECT: BIGBRAIN_COMPATIBILITY_CHECKS_PASSED
  EVIDENCE: automatic-evidence=v1; definition-sha256=2042ae5dc39618c9f9ee589b9cf9c83263e0c18f74f548b5947f162fb47e762e; exit=0; EXPECT=matched; output-sha256=bb0907f5ad41970bcf33be5a6d3b5dc8296adf88e6311fe3682b5fbdb31fa862; output-bytes=44; shell=C:\Program Files\PowerShell\7\pwsh.exe; cwd=D:\SPT41\Development\ArenaMode; path=fa64bc5e3f9a/33 entries

- [x] G3: Real binaries build without installing files, and compiled metadata proves no companion plugin GUID or hard optional dependency in the client.
  CHECK: pwsh -NoProfile -File tools/BigBrainCompatibilityChecks/Verify-Library.ps1
  EXPECT: BIGBRAIN_LIBRARY_BINARY_CHECKS_PASSED
  EVIDENCE: automatic-evidence=v1; definition-sha256=e655c12a3ee152e0f8969b264f9428921cc8d48e6e8edc745e8f6a4103540ec7; exit=0; EXPECT=matched; output-sha256=579c8a3188e137a9366f60af29d517c83307080c4425a79bbabef9106b15de88; output-bytes=7633; shell=C:\Program Files\PowerShell\7\pwsh.exe; cwd=D:\SPT41\Development\ArenaMode; path=fa64bc5e3f9a/33 entries

- [x] G4: Independent adversarial review accepts lifecycle, failure cleanup, migration and package boundaries.
  EVIDENCE: bigbrain_library_review inspected production diff plus installed BepInEx/BigBrain decompilation, then re-reviewed real disk loader fixtures and package metadata guard. No blockers. Top-level-only metadata scan was extended to nested types after review. Native Unity/Mono acceptance remains explicitly outside the offline claim.

- [x] G5: Frozen release package passes its build, unit suite, layout and source guards with the library included.
  CHECK: pwsh -NoProfile -File tools/Package-TestRelease.ps1 -SptPath D:/SPT41 -ArenaRevision 5bed649a3356a5e69a50fe9c9899c63ea1a5dbe6
  EXPECT: PACKAGE_READY
  EVIDENCE: automatic-evidence=v1; definition-sha256=9bdb0a17bd2dd4170e5d1c3e2bf8d047a59783acc5feccf1b0be58a1d6a6a279; exit=0; EXPECT=matched; output-sha256=283b23298d14398d3608af48622cd088d0910d73c08a5c1a989c2c54dabe8a7f; output-bytes=25912; shell=C:\Program Files\PowerShell\7\pwsh.exe; cwd=D:\SPT41\Development\ArenaMode; path=fa64bc5e3f9a/33 entries

Offline checks cannot certify native Mono or live AI behavior. Report that limit
separately; do not treat source/build evidence as a completed gameplay test.
