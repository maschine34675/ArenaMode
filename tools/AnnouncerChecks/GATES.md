# Gates: Announcer events and independent volume

Scope: Add the requested WARLORD combat/team cues, preserve reset-safe playback, and make announcer volume independent of Interface volume without changing normal raids.

- [x] G1: First Blood, strict single-hit Headshot, existing Unstoppable, Last Man Standing, real match results and same-body Payback have deterministic rules; round/respawn/administrative-death boundaries cannot produce false awards.
  CHECK: dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --no-restore
  EXPECT: Fehler:     0
  EVIDENCE: PowerShell 7, cwd D:\SPT41\Development\ArenaMode; exit 0, expected marker matched, 135/135 tests. New rules cover bot-first, same-body revenge, stale aggressor on Fall/health effects, initially wounded bodies, armor/other-shooter/pellet contacts, direct lethal cause and once-per-round Last Man. Native synchronous death order and exact frozen rosters independently reviewed.

- [x] G2: Production-linked playback checks prove ordered bounded cues, reset-safe PCM, lifecycle cancellation and independent announcer volume without changing shared mixer/source state.
  CHECK: dotnet run --project tools/AnnouncerChecks -c Release
  EXPECT: ARENA_ANNOUNCER_CHECKS_PASSED
  EVIDENCE: PowerShell 7, cwd D:\SPT41\Development\ArenaMode; exit 0, expected marker matched, 142 production-linked assertions. Seventeen source-copy mutants are rejected at their intended assertions and both production source hashes stay unchanged. Independent routing reviewer confirmed exact native Master/host contracts, no shared mixer/source/listener writes, unchanged PCM recovery and no remaining P0-P2 findings after fail-soft teardown review. Actual audibility remains G5.

- [x] G3: WARLORD assets are converted, licensed and deployed with source/hash records; decompiled hook/routing evidence and independent adversarial review have no unresolved correctness findings.
  EVIDENCE: Seven new WARLORD OGGs pass ffprobe and decoded measurement (peaks <= -1.5 dBTP); source/output hashes and encoder in WARLORD-ADDITIONS.json, unchanged CC BY-SA 4.0 text retained. Two independent event/lifecycle reviews confirmed both identified fixes and no remaining P0-P2 findings. Client builds with four existing CS8632 warnings, no errors. Audio routing review continues under G2/G4.

- [x] G4: Release build and affected regression checks pass, installed binaries/audio match tested output, and the events and volume changes receive separate commits.
  EVIDENCE: Release solution build exit 0 with five existing warnings (four CS8632, one MUD0002), zero errors. Unit suite 135/135; Team Arena 767, PFT lifecycle 367, loadout 142, lighting 201, Solo Tactical 32 and tracer 129 assertions passed. Final explicit client Release build also redeploys Redeploy.Core Release. ARENA_DEPLOYMENT_VERIFIED, ARENA_AUDIO_DEPLOYMENT_VERIFIED: all 20 audio/assets/documents equal source and build, seven additions match their pinned manifest. Client SHA256 E1B61FCD17722F0FE1C17DBD5999D39D80DE2FA9BF5773EFA7211C6AD6A01415; server 0EA57DCC8D6497F63D88A03DF8F071627EE4CF6C267A700DF758C23854D6EE87; companion 619F96BCB34D44DD062000E4CD32BB947C4803F6E520889CEDF21775FF8429D1. Events committed as 2c6489a; independent volume committed separately with this final evidence update. No GPU result claimed.

- [ ] G5: A GPU client verifies audible independent volume, correct event timing through respawns and 5v5 rounds, and unchanged ordinary-raid audio.
  EVIDENCE: pending user game restart and live test; this server has no GPU.

ABANDON: G5 No GPU on this server; handoff to the user's gaming client. Offline tests do not prove audibility.
