# Gates: Announcer events and independent volume

Scope: Add the requested WARLORD combat/team cues, preserve reset-safe playback, and make announcer volume independent of Interface volume without changing normal raids.

- [x] G1: First Blood, strict single-hit Headshot, existing Unstoppable, Last Man Standing, real match results and same-body Payback have deterministic rules; round/respawn/administrative-death boundaries cannot produce false awards.
  CHECK: dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --no-restore
  EXPECT: Fehler:     0
  EVIDENCE: PowerShell 7, cwd D:\SPT41\Development\ArenaMode; exit 0, expected marker matched, 135/135 tests. New rules cover bot-first, same-body revenge, stale aggressor on Fall/health effects, initially wounded bodies, armor/other-shooter/pellet contacts, direct lethal cause and once-per-round Last Man. Native synchronous death order and exact frozen rosters independently reviewed.

- [ ] G2: Production-linked playback checks prove ordered bounded cues, reset-safe PCM, lifecycle cancellation and independent announcer volume without changing shared mixer/source state.
  CHECK: dotnet run --project tools/AnnouncerChecks -c Release
  EXPECT: ARENA_ANNOUNCER_CHECKS_PASSED
  EVIDENCE: pending

- [x] G3: WARLORD assets are converted, licensed and deployed with source/hash records; decompiled hook/routing evidence and independent adversarial review have no unresolved correctness findings.
  EVIDENCE: Seven new WARLORD OGGs pass ffprobe and decoded measurement (peaks <= -1.5 dBTP); source/output hashes and encoder in WARLORD-ADDITIONS.json, unchanged CC BY-SA 4.0 text retained. Two independent event/lifecycle reviews confirmed both identified fixes and no remaining P0-P2 findings. Client builds with four existing CS8632 warnings, no errors. Audio routing review continues under G2/G4.

- [ ] G4: Release build and affected regression checks pass, installed binaries/audio match tested output, and the events and volume changes receive separate commits.
  EVIDENCE: pending

- [ ] G5: A GPU client verifies audible independent volume, correct event timing through respawns and 5v5 rounds, and unchanged ordinary-raid audio.
  EVIDENCE: pending user game restart and live test; this server has no GPU.

ABANDON: G5 No GPU on this server; handoff to the user's gaming client. Offline tests do not prove audibility.
