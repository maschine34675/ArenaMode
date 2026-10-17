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
