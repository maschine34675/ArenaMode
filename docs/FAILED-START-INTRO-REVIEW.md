# Failed-start intro/exit ordering — 2026-10-17

## Native failure and scope

The first Equator gate run (process `b6f5925a`, client `983b774`) rejected its
formation at 12:36:01.933. Native Stop accepted at 12:36:02.027, but the game
remained Stopping with no results screen. That incident required a client-only
restart; the retained server snapshot recovered before the next arm.

Native source explains the otherwise silent hang: BaseLocalGame.Stop installs
its sole GameEnd callback through PreloaderUI.StartBlackScreenShow. The initial
RaidBattleUIScreenController.ShowAction calls ShowRaidStartInfo, which queues
FadeBlackScreen(0.5f, -0.3f). After that scaled delay, the intro unconditionally
stops/replaces the same black-screen coroutine, discarding GameEnd. Deferring
Stop by one frame does not order these two delayed callbacks.

ArenaFailedStartIntro transparently wraps the existing WaitSeconds call only
while PreloaderUI.ShowRaidStartInfo owns the synchronous scope. Prefix/finalizer
restore nested scope even on exceptions. Capture may precede OnGameStarted.
At callback execution, suppression requires the same preloader, arm epoch,
LocalGame, GameWorld, immutable profile and exact started Arena identity, an
owned pending/committed failed-start abort, and native Stopping/Stopped status.
Only that obsolete cosmetic intro callback is skipped. Native Stop, scaled
timing, GameEnd, results and server profile restoration stay native.

Ordinary raids, normal Arena starts, other fades, pending aborts before Stop,
foreign/stale worlds and retired aborts invoke the original action unchanged.
There is no global fade suppression, extra timeout or direct GameEnd invocation.
The transpiler requires exactly one native WaitSeconds call and rejects drift.

Before initiating Stop, the existing abort queue additionally waits for a
nonpaused game, completed Core respawn pipeline and matching owner/local/world
player binding. This preserves native Stop's required owner dereferences.

## Verification

Offline baseline: 112 assertions and 23 compiled deliberate regressions rejected
by FailedStartAbortChecks/Verify.ps1. Independent source review found no blocking
runtime defect. Actual native acceptance is recorded separately below/in handoff.

The linked production harness includes a positive unpatched cancellation
control, intro scheduled both before and after Stop, pre-confirm server rejection,
scope finalization after an exception, stale identities, normal fade pass-through,
single Stop and transpiler shape/label checks. Boundary doubles do not certify
native Unity or Harmony behavior. Staged compilation and GPU verification are
recorded separately in the handoff/native report; no test injector is shipped.

The existing MapPort developer hold can reproduce the failed start without
changing game code: Spawns/SpawnGateHold=true with carving enabled on Equator
TDM. Arena refuses to acquire someone else's hold, so its connected-formation
check rejects the early start. Restore the developer setting after testing.

## Native acceptance and delivery

Client commit **63466e4** was staged without deployment, then installed locally
and synchronized to SPT-GPU-2. SHA256:
`2E2CA630881FE2E7F880E2895E9295F59C20647E848071114D9426D48B7F401F`.
Build manifest: `artifacts/double-jump-build/bf983657055d4e2cadd6cd1da67bf685/manifest.json`.
Four existing nullable-annotation warnings, zero build errors. Server remains
983b774 / SHA256 `EFBB3C2A99AF02B5F9A9FA59D31CFBBC74A92BCE40247781E17F7C425018ADBB`.
Adjacent SpawnGateChecks passed 100 assertions and 19 deliberate regressions.

GPU process **b4b60417**, started 14:32:53 local (UTC+02), UiBridge0.9.4,
EFT0.16.9.5, 44 plugins loaded; all20 maschine assemblies matched local/host/loaded.
Evidence log: `LogOutput-SPT-GPU-2-2026-10-17_14-32-45.log`.

- Rejected Equator3v3: original MapPort SpawnGateHold=false changed temporarily
  to true (carving already true; HoldSeconds remained30). At14:34:50.088 USEC
  capacity0/10 caused the expected rejection. Stop accepted14:34:50.186;
  the exact obsolete intro callback was skipped14:34:50.566. Server restored
  the snapshot14:34:51.238. ExitStatus was reached; NEXT through KillList and
  SessionStatistics returned to MainMenu. No restart/recovery workaround needed.
  The generic raid recipe timed out waiting for a running raid, as expected for
  this deliberately rejected start; this is not used as the acceptance oracle.
- Normal Equator3v3 immediately afterward, hold restoredfalse: both factions
  had10/10 safe positions at14:36:28.799–800. Assembling→Countdown14:36:45.806;
  all20 paths revalidated and Live14:36:50.810. Ordinary Arena Disconnect used
  native Stop(Left)14:36:58.554; snapshot restored14:36:59.676, then results and
  MainMenu again. No startup-fade suppression occurred on this successful raid.

Each final profile compared with its exact native pre-raid backup
(`12-34-34`, `12-36-20`) retains314/314 items and differs only in the usual
Hideout update timestamp. Inventory, health, quests, skills and other profile
content match. Relative to the earlier pre-process baseline, Health.UpdateTime
also changed during normal login; the raid-level comparisons exclude that.
No raw profile write was used.

Final audit covers137 local/host DLLs: only the client DLL at the two
destinations changed. All12 PFT files, semantic plugin settings and server
configuration are unchanged. No MapPort code/bundles or server DLL replacement.
EFT quit gracefully; server stopped, matching the initial stopped state.
The two BepInEx errors are the intentionally induced initialization rejection;
no additional BepInEx/server errors. EFT's own stream contains31 map/native
diagnostics (spawn-marker reconciliation, weather/season components,
JetMaterial and spatial-audio initialization); this test does not certify or
resolve those separate map issues. No ordinary PMC/Scav raid was tested here.

Detailed ignored evidence: `artifacts/early-start-abort-20261017/` (fresh baseline,
delivered manifest, lifecycle/errors exports, per-raid profile comparisons and
final DLL/follower/config audit). No publication, push or trial-ZIP refresh.

## Other original team boxes

Bay5 is the next straightforward candidate. Its three original gate paths are
already registered by MapPort's generic SpawnGates API; twenty source spawn
markers exist, and earlier depot/warehouse connectivity tests passed. ArenaMode
still needs private faction formations, capacity and closed/open path validation,
and the TDM countdown/respawn integration now specific to Equator. The complete
formation has not been natively validated; no current map blocker is established.

AirPit needs map-side preparation first. Its ten original north starts at z75–80
lie beyond the current z70 NavMesh bake boundary. Extend and validate ground,
colliders and connectivity before admitting those points. The north passport
turnstiles have walkable gaps and do not make a sealed box: a reliable temporary
boundary is required. Separate start gates from permanently removed geometry
before using Hold: the current OpenedBarriers registry also contains shop shells,
portal fences and blockout walls, which a generic hold would re-enable.

References: MapPort docs/progress/spawn-gates.md; tools/maps/bay5.json;
docs/progress/bay5-arenamode-integration.md; tools/maps/airpit.json;
ArenaMapPort.Shared/ArenaMapDescriptor.cs (AirPit OpenedBarriers rationale);
docs/progress/airpit-release.md (original marker groups and north-gap tests).
No MapPort source, bundles or map data are changed by this task. Original team
boxes currently apply to independent TDM; Tactical/PFT needs its own round-flow
integration and is not implicitly certified by this work.
