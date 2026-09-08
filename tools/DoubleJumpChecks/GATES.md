# Double jump preparation

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
