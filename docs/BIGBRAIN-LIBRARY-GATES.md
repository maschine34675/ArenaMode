# Gates: Arena-owned optional BigBrain library

OWNS: ArenaMode.Client/Plugin.cs, ArenaMode.Client/Compat/ArenaBigBrainLibrary.cs, ArenaMode.BigBrainCompat/**, tools/BigBrainCompatibilityChecks/**, tools/Assert-BigBrainLibrary.ps1, tools/Package-TestRelease.ps1, tools/PickupObjectiveChecks/**, tools/MolotovChecks/**, tools/TeamArenaExpansionChecks/**, docs/**, README.md, AGENTS.md

Scope: Explain Core/server responsibilities from source; replace the extra plugin
with an Arena-owned optional library, retaining absence safety and bot ownership.
No publication or live deployment is required for this code/package change.

- [x] G1: Core/server dependency answer follows actual requests and driver callbacks.
  EVIDENCE: Independent redeploy_guid_audit traced standalone ServerModCheck/ping and inventory/postraid patches; Core has only local respawn/session callbacks. Arena PostRaidPmcSkipPatch and ArenaProtectedRaidEndService own protected profile restoration. See BIGBRAIN-LIBRARY-20261018.md.

- [ ] G2: Isolated production bootstrap checks cover optional absence, registration failures, repeated init and teardown.
  CHECK: dotnet run --project tools/BigBrainCompatibilityChecks -c Release
  EXPECT: BIGBRAIN_COMPATIBILITY_CHECKS_PASSED
  EVIDENCE: pending

- [ ] G3: Real binaries build without installing files, and compiled metadata proves no companion plugin GUID or hard optional dependency in the client.
  CHECK: pwsh -NoProfile -File tools/BigBrainCompatibilityChecks/Verify-Library.ps1
  EXPECT: BIGBRAIN_LIBRARY_BINARY_CHECKS_PASSED
  EVIDENCE: pending

- [x] G4: Independent adversarial review accepts lifecycle, failure cleanup, migration and package boundaries.
  EVIDENCE: bigbrain_library_review inspected production diff plus installed BepInEx/BigBrain decompilation, then re-reviewed real disk loader fixtures and package metadata guard. No blockers. Top-level-only metadata scan was extended to nested types after review. Native Unity/Mono acceptance remains explicitly outside the offline claim.

- [ ] G5: Frozen release package passes its build, unit suite, layout and source guards with the library included.
  CHECK: pwsh -NoProfile -File tools/Package-TestRelease.ps1 -SptPath D:/SPT41
  EXPECT: PACKAGE_READY
  EVIDENCE: pending

Offline checks cannot certify native Mono or live AI behavior. Report that limit
separately; do not treat source/build evidence as a completed gameplay test.
