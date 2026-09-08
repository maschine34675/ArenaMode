# Bounded team-area connectivity acceptance

OWNS: ArenaMode.Client/Areas/ArenaAreaRuntime.cs, ArenaMode.Client/Areas/ArenaAreaConnectivity*.cs, tools/AreaConnectivityChecks/**, AREA-CONNECTIVITY-REVIEW.md

Owner: navigation-connectivity leaf. Allowed edits: ArenaAreaRuntime.cs,
ArenaAreaConnectivity*.cs, this check folder, AREA-CONNECTIVITY-REVIEW.md only.
Root owns team capacity/integration, full build, adversarial review, commit and
deployment. No live GPU acceptance is claimed by this leaf.

## Gates written before implementation

- [x] G1: Existing native marker identities, masks, geometry, sample tolerances,
   twelve-second preparation timeout, and actual accepted-path guards. Only the
   new team-start preset uses waypoint connectivity; legacy behavior stays direct.
  EVIDENCE: Independent source review by tdm_role_label_fix found no remaining P0-P2; source pool, masks and geometry were not edited; actual movement guards remain unchanged. Root staged client build passed with four existing warnings.

- [x] G2: Production-linked graph checks prove bounded query budgets, independent
   forward/reverse edges, disconnected/one-way rejection, waypoint recovery,
   fresh chain validation, stale-edge rejection and reset ownership. Named
   deliberate source faults must fail their targeted checks.
  CHECK: pwsh -NoProfile -File tools/AreaConnectivityChecks/Verify.ps1
  EXPECT: ARENA_AREA_CONNECTIVITY_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=3e35ca58b66da67db96a653f09a2e3f29d12e59d73db15a634cab108304e90ab; output-bytes=1820

- [x] G3: Integration checks verify all placement/release paths use the same fresh
   connectivity helper and actual movement still checks supplied corners.
  CHECK: pwsh -NoProfile -File tools/AreaConnectivityChecks/Verify.ps1 -IntegrationOnly
  EXPECT: AREA_CONNECTIVITY_INTEGRATION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=14c5171df018df8fb8ab3a840e76b045a2299a83c0d3f4ba84bd23b3454e3fe1; output-bytes=71

- [x] G4: Native-source rationale, measured check results and GPU-only limitations are documented and independently reviewed.
  EVIDENCE: AREA-CONNECTIVITY-REVIEW.md records exact native APIs, bidirectional proof, discovery versus finalization budgets, placement cost bounds and remaining GPU work. Reviewer requested and accepted stale-preparation, near-point, ID and replacement fixtures; final review has no remaining P0-P2. Root owns full regressions, integration commit and delivery.

## Verification

Run `pwsh tools/AreaConnectivityChecks/Verify.ps1` against production-linked
sources, inspect every negative control and clean-check the leaf diff. This
project is a pure executable and has no mod deployment target.
