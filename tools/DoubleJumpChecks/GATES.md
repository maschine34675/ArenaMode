# Double jump verification

## Ledge-jump revision — 2026-09-12

Scope: deliberate air jump after walking/falling off an edge; one allowance per
body/context airborne episode, exact Arena/native ownership and genuine landing.

- [x] G1: Production-linked behavior checks and compiled negative controls pass.
  CHECK: pwsh -NoProfile -File tools/DoubleJumpChecks/Verify.ps1
  EXPECT: ARENA_DOUBLE_JUMP_CHECKS_PASSED
  EVIDENCE: exit=0; 278 assertions; all 22 compiled source faults rejected by
  their named behavioral assertions; production helper rebuilt and rerun after
  faults. Evidence: `artifacts/double-jump-checks/0a7474de55034562a9a4aaa313679ea1/`.
  Production source SHA256:
  `21F4F97E6EE79A765C19CDA8742E40B05FF4A2C20DC60E6A6E818A345327452D`.
- [x] G2: Independent current-source and native-contract review completed.
  EVIDENCE: `deathmatch_server` reviewed the same source SHA256, native
  Jump/FallDown/TryJump/CheckFlying/debit contracts, and tests. Ground-toggle,
  shutdown retention, native signal cleanup/handoff and retained lift-basis
  findings were corrected and re-reviewed; no remaining established defect.
- [x] G3: Root-owned isolated full-client build and final repository acceptance.
  EVIDENCE: Parent reran the 278/22 helper successfully and built clean source
  c151aa683a7bcb83eec8d0183f36fc19ab8ce18a with 0 errors and 4 existing warnings.
  Manifest: artifacts/double-jump-build/146ebfa928204a548a5c314c528e431c/manifest.json.
  Client SHA256: 3FFF60D7EA67B7CF40046421362F93BED3A87C984449F0C62B518026344F3FF6.
  Installed r3 DLLs were unchanged; HANDOFF.md and the separate r1 test job
  record staged-only delivery, preserved concurrent test evidence and GPU limits.
- [ ] G4: Native GPU acceptance of early-edge transitions, contact and animation.
  EVIDENCE: pending. Contract doubles do not execute EFT's animator graph,
  native Harmony detours or physical controller contacts.

## Historical version 1 preparation — 2026-09-08

The following gates record the original grounded-takeoff-only version. They are
historical evidence, not build or runtime acceptance of the 2026-09-12 revision.

Scope: Implement one optional additional jump after a normal local Arena takeoff,
verify native integration and safeguards offline, and stage without changing the
DLLs currently undergoing GPU acceptance. Live double-jump acceptance is a separate
handoff, not a result this non-GPU host can claim.

- [x] G1: Production-linked double jump checks reject normal-raid leaks, extra air jumps, stale bodies and invalid movement states.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/DoubleJumpChecks/Verify.ps1
  EXPECT: ARENA_DOUBLE_JUMP_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\Administrator\Documents\Codex\ArenaDoubleJump; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=cf1709d254af7ced8eec8fb8d971496892b52ed19f35cecf7259453a5d3c106c; output-bytes=1261

- [x] G2: The complete client compiles against installed game APIs into isolated staging and installed client/server/Redeploy bytes remain unchanged.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/DoubleJumpChecks/Build-Staged.ps1
  EXPECT: ARENA_DOUBLE_JUMP_STAGED_BUILD_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\Administrator\Documents\Codex\ArenaDoubleJump; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=061fcc4841865de12922ea7cd906c7bd2ac3c33aaca653f8ad8ac366e2f4220d; output-bytes=3343

- [x] G3: Independent adversarial review finds no unresolved consequential defect in input, collision, lifecycle and ordinary-raid isolation.
  EVIDENCE: 2026-09-08 double_jump_hooks independent source review found the reused-credit/stamina-callback lifetime gap; exact request credit and post-debit ownership revalidation fixed it. Re-review found no remaining P0-P2 in movement hooks or isolated build script; native GPU behavior remains pending.

- [x] G4: A committed review and handoff identify the prepared feature, unchanged live test baseline, actual offline results and outstanding GPU tests.
  EVIDENCE: 2026-09-08 verified the feature commit contains DOUBLE-JUMP-REVIEW.md, HANDOFF.md, implementation and checks, with the Codex co-author trailer. Both documents explicitly distinguish the isolated prepared client from the unchanged installed GPU test baseline and list pending live acceptance; no deployment or push performed.
