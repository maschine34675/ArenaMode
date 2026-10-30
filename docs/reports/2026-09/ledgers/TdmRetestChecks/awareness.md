# TDM search awareness

OWNS: ArenaMode.Client/Bots/TeamDeathmatchRelations.cs, ArenaMode.Client/Bots/TdmSearchAwareness.cs, tools/TdmAwarenessChecks/**, TDM-AWARENESS-REVIEW.md

- [x] G1: Production-linked awareness checks admit bounded search cues only for exact current live opponents, preserve visual/native combat state, and reject stale/foreign/ordinary-raid cases with negative controls.
  CHECK: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/TdmAwarenessChecks/Verify.ps1
  EXPECT: TDM_AWARENESS_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=5f1e4eff4e51/32 entries; EXPECT=matched; output-sha256=24f3caacc400a7ab96bbdc60516efb462e8194e02c6bfcbdd24f3f165daa7e3d; output-bytes=2455

- [x] G2: Installed native and optional SAIN contracts and diagnostic bounds have been independently reviewed without copying external implementation or changing AI path ownership.
  EVIDENCE: 2026-09-08 tdm_navigation_diagnosis independently inspected installed SAIN hearing/forgetting and the exact native ownership contracts. Permanent-wrapper forgetting and post-stop writes were corrected; final review reports no remaining P0-P2. Root separately checked ownership/callback boundaries and production-linked fixture/negative-control semantics. TDM-AWARENESS-REVIEW.md records native DLL hashes, exact contracts, weak ordinal diagnostics, unchanged position refresh and live GPU limits. No external implementation or new path/movement owner was introduced.
