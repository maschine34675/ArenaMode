# Gates: Labyrinth Arena alarm suppression

Scope: Suppress only the confirmed Labyrinth center siren during the exact active Arena raid; preserve triggers, other audio and ordinary raids. Root is sole writer; independent native research and review are read-only.

- [x] G1: Installed scene data identifies the alarm clip, four entry triggers and their shared seven-source player; native audio and trigger methods agree with the selected hook.
  EVIDENCE: Inspect-InstalledScene.py with UnityPy 1.25.3, Python 3.12, PowerShell, cwd D:\SPT41\Development\ArenaMode; exit 0 and ARENA_LABYRINTH_SCENE_CONTRACT_VERIFIED. Scene index 552, player 8958, alarm_oneshot 4.0 seconds, four handlers reference the same seven-source player. All seven source ancestries verified. level552 SHA256 d39b3c4606d51fe9699319ef1da4f671f129843c1bcd7bc8f23efd68436bbfeb. Native source and independent read-only review confirm the audio-only InitializeSource hook and ITriggerEntity registry contract; full evidence in LABYRINTH-ALARM-REVIEW.md.

- [x] G2: Production-linked checks reject ordinary/stale raids, other maps, foreign sources and changed scene identities; suppress repeated siren output and catch already scheduled playback without editing shared audio or trigger state.
  CHECK: powershell -NoProfile -ExecutionPolicy Bypass -File tools/LabyrinthAlarmChecks/Verify-Regression.ps1
  EXPECT: ARENA_LABYRINTH_ALARM_REGRESSION_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=864caa03d59c/32 entries; EXPECT=matched; output-sha256=012c1b057d589722470996c1afe9b7c6d96b243a8de736d0dc680386a2fa6b0d; output-bytes=193

- [x] G3: Release build/deployment and affected regression checks pass; independent adversarial review has no unresolved correctness finding and the scoped change is committed.
  EVIDENCE: Release client build exit 0; initial compilation has four pre-existing CS8632 warnings and zero errors, final incremental build zero warnings/errors. Unit suite 135/135, announcer 142 assertions, alarm 91 assertions; all exit 0. Three isolated source-copy mutants (raid ownership, source membership, clip ownership) fail their intended assertions; production source SHA unchanged and restored harness passes. Independent adversarial reviewer found no unresolved P0/P1/P2. ARENA_DEPLOYMENT_VERIFIED; built/deployed client SHA256 702C7AB0C0DD084C7E3465E2AD2CCAB1C0C7F75C0738ECC7529A1054D77F6267, Redeploy.Core Release 3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C. Server unchanged and hash-matched. Change, tests and documentation committed together on fix/labyrinth-arena-alarm. GPU behavior is explicitly G4, not inferred from these checks.

- [ ] G4: A GPU raid confirms the center stays quiet across rounds/respawns while announcements, combat audio and ordinary Labyrinth raids remain audible.
  EVIDENCE: unavailable on this server

ABANDON: G4 This server has no GPU. The user must restart the client and test the identified alarm in a live raid.
