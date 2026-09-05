# Gates: corpse cleanup event pairing and AI update review

Root owns repository writes. Read-only delegates inspect upstream SAIN 4.5.1 and PitFireTeam beta-0.10.1 against installed versions and Arena contracts; no third-party update is installed or bundled. Cleanup remains restricted to already eligible Arena corpses, preserving views, pools, teammate and inventory protection.

- [x] G1: Native removal, activity and view callbacks establish correct successful and failed operation event ordering before implementing the correction.
  EVIDENCE: Root and independent native reviewer verified ItemManipulator.RemoveWithoutRestrictions, Slot.RemoveItemInternal, RemoveResult, ItemController.ProcessActivity/SelectEvents/HasForeignEvents, SlotItemAddress.ApplyContainedItem and PlayerBody slot pooling. Native ChangeItemsOperation/DestroyResult confirms post-mutation Begin/Succeed. Same-item activity is excluded; Succeed in finally completes committed removal after a Begin callback error. No broad activity clearing, rollback, replacement inventory operation or corpse destruction. CORPSE-CLEANUP-REVIEW.md records limits for native callbacks throwing after physical detachment but before returning the result.

- [x] G2: Production-linked regression checks detect the original unpaired completion, preserve view removal and protected/excluded gear, and verify failure paths without retaining active activities.
  CHECK: powershell -NoProfile -ExecutionPolicy Bypass -File tools/CorpseCleanupChecks/Verify-Regression.ps1
  EXPECT: ARENA_CORPSE_CLEANUP_REGRESSION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=4c3d8970d08801673c578ef17556bd8a0e4accbb311530fac71838df6d0b99ad; output-bytes=225

- [x] G3: Independent adversarial review, relevant regressions, Release build and installed DLL hash verification pass; the focused fix and evidence are committed.
  EVIDENCE: Independent native/production/test review found no unresolved P0-P2 issues and independently passed all 44 cleanup assertions. Root regression runner rejected all four source-copy mutants and rebuilt production successfully. Existing server tests 135/135, PFT compatibility 231 assertions and team expansion 767 assertions passed. Client Release (including Redeploy.Core Release) built with zero errors and four existing CS8632 warnings. Verify-NativeMenuDeployment.ps1 passed; client built/installed SHA256 EE31CC054C1F3525C8BECB0D4C0BFAC7B19E0741F96B649A1B67C8F8D2E95D51, Redeploy.Core built/installed 3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C. git diff --check passed. The focused fix, tests and native review are committed together; no GPU runtime proof or repair of arbitrary callback failures is claimed.

- [x] G4: Tagged SAIN and PFT source/release changes are compared with installed binaries and Arena's exact compatibility contracts; actionable implications and uncertainty are documented separately without installing updates.
  EVIDENCE: AI-UPDATE-REVIEW-2026-09-05.md records exact tags, primary source links, installed/candidate client/server hashes, root-verified artifact identity and two independent read-only audits/reviews. Existing team expansion harness with only TEMP path overrides passes 767 assertions against candidate SAIN 4.5.1, root inspected the diff and reran the candidate and ordinary installed-SAIN checks. PFT 0.10.1 key member contracts remain intact but introduces uncovered proficiency transactions and read-side clothing writes; no version lock was relaxed. A bounded PFT 0.10.0/SAIN 4.5.1 aiming-reference check found no concrete removed-API dependency, without claiming coverage of every cross-mod hook. Installed SAIN 4.5.0 and PFT client/server 0.10.0 hashes are unchanged. The assessment is a separate documentation commit; cleanup fix is 684c7be. No updates installed, no third-party code bundled, no live runtime claim.

- [ ] G5: A restarted GPU raid verifies corpse view cleanup and absence of the original per-item errors across many kills and raid exit.
  EVIDENCE: unavailable on this server

ABANDON: G5 This server has no GPU. Local contract tests cannot validate Unity pool behavior in a live raid; verification remains on the user's gaming client.
