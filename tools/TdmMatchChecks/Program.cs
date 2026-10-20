using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
TeamDeathmatchMatchPolicy Start(int kills, int seconds)
{
    var p = new TeamDeathmatchMatchPolicy(kills, seconds);
    p.Update(100, true, 999, 999);
    Check(p.Phase == TdmMatchPhase.Countdown && p.Elapsed(1000) == 0, "assembly must not charge time or scores");
    p.Update(102.99f, true, 999, 999);
    Check(p.Phase == TdmMatchPhase.Countdown, "full three-second countdown required");
    p.Update(103, true, 999, 999);
    Check(p.Phase == TdmMatchPhase.Live && p.StartedAt == 103, "countdown starts fresh live clock");
    return p;
}
foreach (var bad in new[] { (-1, 0), (1001, 0), (0, -1), (0, 7201) })
{
    bool rejected = false;
    try { _ = new TeamDeathmatchMatchPolicy(bad.Item1, bad.Item2); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "invalid limits must be rejected");
}
var waiting = new TeamDeathmatchMatchPolicy(1, 1);
waiting.Update(1000, false, 999, 999);
Check(waiting.Phase == TdmMatchPhase.Assembling && waiting.Elapsed(10000) == 0, "unready roster never starts match");
waiting.Update(1001, true, 0, 0);
waiting.Update(1002, false, 0, 0);
Check(waiting.Phase == TdmMatchPhase.Assembling, "readiness loss resets countdown");
waiting.Update(1010, true, 0, 0);
waiting.Update(1012, true, 0, 0);
Check(waiting.Phase == TdmMatchPhase.Countdown, "replacement readiness requires complete new countdown");
waiting.Update(1030, true, 0, 0);
Check(waiting.StartedAt == 1030 && waiting.Elapsed(1030) == 0, "slow countdown frame never backdates play time");
foreach (int limit in new[] { 1, 25, 50, 100, 1000 })
foreach (bool won in new[] { false, true })
{
    var p = Start(limit, 600);
    p.Update(104, false, limit - 1, limit - 1);
    Check(p.Phase == TdmMatchPhase.Live, "kill limit boundary remains live until reached");
    p.Update(105, false, won ? limit : limit - 1, won ? limit - 1 : limit);
    Check(p.Result == (won ? TdmMatchResult.Won : TdmMatchResult.Lost), "kill target selects actual leading faction");
    Check(p.Elapsed(9000) == 2, "terminal elapsed is frozen");
    var score = (p.FriendlyKills, p.OpponentKills, p.Result, p.FinishedAt, p.Reason);
    p.Update(9000, true, 100000, 200000);
    Check(score == (p.FriendlyKills, p.OpponentKills, p.Result, p.FinishedAt, p.Reason), "terminal result and scores immutable");
}
foreach (int seconds in new[] { 1, 5, 300, 600, 7200 })
foreach (var score in new[] { (0, 0), (1, 0), (0, 1), (53, 53) })
{
    var p = Start(0, seconds);
    p.Update(103 + seconds - .1f, false, score.Item1, score.Item2);
    Check(p.Phase == TdmMatchPhase.Live, "time limit boundary remains live until reached");
    p.Update(103 + seconds, false, score.Item1, score.Item2);
    var expected = score.Item1 > score.Item2 ? TdmMatchResult.Won : score.Item1 < score.Item2 ? TdmMatchResult.Lost : TdmMatchResult.Draw;
    Check(p.Result == expected && p.Elapsed(9000) == seconds, "time limit compares team kills and preserves draws");
}
var unlimited = Start(0, 0);
unlimited.Update(1000000, false, 100000, 200000);
Check(unlimited.Phase == TdmMatchPhase.Live, "unlimited disables both end conditions");
var killOnly = Start(25, 0); killOnly.Update(1000000, false, 24, 24);
Check(killOnly.Phase == TdmMatchPhase.Live, "time disabled independently of kill target");
killOnly.Update(1000001, false, 25, 24);
Check(killOnly.Result == TdmMatchResult.Won, "kill-only match still ends");
var malformedTime = Start(1, 1);
foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    malformedTime.Update(bad, true, 10, 20);
Check(malformedTime.Phase == TdmMatchPhase.Live, "invalid clock cannot manufacture terminal result");

(LocalGame, GameWorld, TeamDeathmatchMode) NewExit()
{
    var game = new LocalGame(); game.Owner = new Owner { Player = game.LocalPlayer };
    var world = new GameWorld { MainPlayer = game.LocalPlayer };
    game.GameWorld = world;
    TdmMatchExit.Reset();
    var mode = new TeamDeathmatchMode { ResultOwnsCurrentRaid = true };
    Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    ArenaMatchService.Current = new ArenaMatch { Mode = mode };
    ArenaSession.Exact = ArenaSession.IsActive = true; ArenaRespawnDriver.StandDown = false;
    RespawnEngineState.IsPipelineRunning = false; LocalGameAccessor.Throw = false;
    UnityEngine.Time.timeScale = 1f;
    return (game, world, mode);
}
Action<LocalGame, GameWorld, TeamDeathmatchMode>[] exclusions = [
    (g,w,m) => RespawnEngineState.IsPipelineRunning = true,
    (g,w,m) => g.Status = GameStatus.Stopping,
    (g,w,m) => Singleton<AbstractGame>.Instance = new LocalGame(),
    (g,w,m) => Singleton<GameWorld>.Instance = new GameWorld(),
    (g,w,m) => ArenaSession.Exact = false,
    (g,w,m) => ArenaMatchService.Current.Mode = new OtherMode(),
    (g,w,m) => m.ResultOwnsCurrentRaid = false,
    (g,w,m) => g.ProfileId = "other",
    (g,w,m) => g.Owner = null,
    (g,w,m) => g.Owner.Player = new Player(),
    (g,w,m) => g.LocalPlayer = null,
    (g,w,m) => w.MainPlayer = new Player(),
    (g,w,m) => g.LocalPlayer.ProfileId = "other",
    (g,w,m) => LocalGameAccessor.Throw = true,
    (g,w,m) => UnityEngine.Time.timeScale = 0f,
    (g,w,m) => g.GameWorld = new GameWorld(),
    (g,w,m) => ArenaSession.IsActive = false
];
for (int i = 0; i < exclusions.Length; i++)
{
    var (g,w,m) = NewExit(); exclusions[i](g,w,m);
    TdmMatchExit.TryStop(g,w,"local");
    Check(g.StopCalls == 0 && !ArenaRespawnDriver.StandDown, "unsafe exit excluded " + i);
}
foreach (bool alive in new[] { true, false })
{
    var (g,w,_) = NewExit(); g.LocalPlayer.HealthController.IsAlive = alive;
    TdmMatchExit.TryStop(g,w,"local"); TdmMatchExit.TryStop(g,w,"local");
    Check(g.StopCalls == 1 && g.Status == GameStatus.Stopping && ArenaRespawnDriver.StandDown,
        "valid terminal exit exactly once and guards released");
    Check(g.LastStatus == (alive ? ExitStatus.Survived : ExitStatus.Killed), "native exit status matches physical life");
}
{
    var (g,w,_) = NewExit(); g.BeforeStop = () => throw new Exception("precommit");
    TdmMatchExit.TryStop(g,w,"local");
    Check(g.Status == GameStatus.Started && !ArenaRespawnDriver.StandDown, "precommit error restores death interception");
    g.Status = GameStatus.Stopping;
    Check(!TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "precommit error clears terminal lease");
    g.Status = GameStatus.Started;
    g.BeforeStop = null; TdmMatchExit.TryStop(g,w,"local");
    Check(g.StopCalls == 2 && g.Status == GameStatus.Stopping, "precommit failure can safely retry");
}
{
    var (g,w,_) = NewExit(); g.AfterCommit = () => throw new Exception("postcommit");
    TdmMatchExit.TryStop(g,w,"local"); TdmMatchExit.TryStop(g,w,"local");
    Check(g.StopCalls == 1 && ArenaRespawnDriver.StandDown, "postcommit failure never repeats native stop");
    Check(TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "postcommit error retains terminal lease");
}
{
    var (g,w,_) = NewExit(); g.IgnoreStop = true; TdmMatchExit.TryStop(g,w,"local");
    Check(g.Status == GameStatus.Started && !ArenaRespawnDriver.StandDown, "ignored native stop retains terminal death interception");
    g.Status = GameStatus.Stopping;
    Check(!TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "no-op stop clears terminal lease");
}
foreach (bool throwAfterEnd in new[] { false, true })
{
    var (g,w,m) = NewExit();
    bool ownedInsideStop = false;
    g.AfterCommit = () =>
    {
        m.ResultOwnsCurrentRaid = false;
        ownedInsideStop = TdmMatchExit.OwnsTerminalTeardown(g,w,"local");
        TdmMatchExit.Reset();
        if (throwAfterEnd) throw new Exception("after synchronous GameEnd");
    };
    TdmMatchExit.TryStop(g,w,"local");
    Check(ownedInsideStop, "lease exists inside committed Stop without current result");
    Check(!TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "synchronous GameEnd cannot resurrect terminal lease");
}
foreach (var status in new[] { GameStatus.Started, GameStatus.Starting, GameStatus.SoftStopping, GameStatus.Stopping, GameStatus.Stopped })
{
    var (g,w,_) = NewExit();
    bool ownedBeforeCommit = false;
    g.BeforeStop = () =>
    {
        ownedBeforeCommit = TdmMatchExit.OwnsTerminalTeardown(g,w,"local");
        g.Status = status;
        throw new Exception("native status probe");
    };
    TdmMatchExit.TryStop(g,w,"local");
    Check(!ownedBeforeCommit, "pending terminal lease never suppresses intro");
    bool committed = status == GameStatus.Stopping || status == GameStatus.Stopped;
    Check(ArenaRespawnDriver.StandDown == committed, "only native teardown releases terminal death hold " + status);
    Check(TdmMatchExit.OwnsTerminalTeardown(g,w,"local") == committed, "only native stopping statuses authorize intro guard " + status);
    if (!committed)
    {
        g.Status = GameStatus.Stopping;
        Check(!TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "non-teardown status discards lease " + status);
    }
}
foreach (string mismatch in new[] { "none", "game", "world", "owned-world", "exact", "ordinary",
    "epoch", "profile", "captured-profile", "captured-game", "captured-world", "reset" })
{
    var (g,w,m) = NewExit(); TdmMatchExit.TryStop(g,w,"local");
    m.ResultOwnsCurrentRaid = false;
    var capturedGame = g; var capturedWorld = w; string capturedProfile = "local";
    switch (mismatch)
    {
        case "game": Singleton<AbstractGame>.Instance = new LocalGame(); break;
        case "world": Singleton<GameWorld>.Instance = new GameWorld(); break;
        case "owned-world": g.GameWorld = new GameWorld(); break;
        case "exact": ArenaSession.Exact = false; break;
        case "ordinary": ArenaSession.IsActive = false; break;
        case "epoch": ArenaSession.ArmEpoch++; break;
        case "profile": g.ProfileId = "successor"; break;
        case "captured-profile": capturedProfile = "successor"; break;
        case "captured-game": capturedGame = new LocalGame(); break;
        case "captured-world":
            capturedWorld = new GameWorld();
            Singleton<GameWorld>.Instance = capturedWorld; g.GameWorld = capturedWorld;
            break;
        case "reset": TdmMatchExit.Reset(); break;
    }
    Check(TdmMatchExit.OwnsTerminalTeardown(capturedGame,capturedWorld,capturedProfile) == (mismatch == "none"),
        "terminal lease exact scope " + mismatch);
}
{
    var (g,w,_) = NewExit(); ArenaRespawnDriver.StandDown = true; g.Status = GameStatus.Stopping;
    Check(!TdmMatchExit.OwnsTerminalTeardown(g,w,"local"), "StandDown alone never authorizes intro guard");
}
{
    var (g,w,m) = NewExit();
    var probe = new RespawnGuardProbe { IsTerminalTdmHold = true, IsRespawnInProgress = true };
    var preload = probe.AfterPreload(g, g.LocalPlayer, 1);
    Check(preload.MoveNext() && !probe.EnteredEngine, "terminal preload continuation waits without entering engine");
    Check(preload.MoveNext() && !probe.EnteredEngine, "terminal hold persists across arbitrary preload frames");
    probe.StandDown = true;
    Check(!preload.MoveNext() && !probe.EnteredEngine && probe.LeaseEnds == 1, "intentional terminal stop releases preload without new body");
    probe = new RespawnGuardProbe(); preload = probe.AfterPreload(g,g.LocalPlayer,1);
    Check(!preload.MoveNext() && probe.EnteredEngine, "nonterminal respawn passes entry positive control");
    probe = new RespawnGuardProbe { IsTerminalTdmHold = true };
    var failure = probe.AfterEngineFailure(new Exception("factory timeout"),1);
    Check(!failure.MoveNext() && probe.LeaseEnds == 1 && !probe.StandDown && !probe.FallbackCalled
        && m.ExitProblem.Contains("factory timeout"), "terminal factory failure cannot bypass safe-owner exit");
    probe = new RespawnGuardProbe(); failure = probe.AfterEngineFailure(new Exception("ordinary failure"),1);
    Check(!failure.MoveNext() && probe.FallbackCalled && probe.StandDown, "nonterminal failure retains existing fallback");
    foreach (bool progress in new[] { true, false })
    {
        probe = new RespawnGuardProbe { IsTerminalTdmHold = true, IsRespawnInProgress = progress };
        Check(probe.ShouldBlockDeathScreen(g) && probe.ShouldBlockKilledStop(g) && probe.ShouldSuppressKilledSessionResult(),
            "terminal death interception survives driver lease completion");
        RespawnEngineState.IsPipelineRunning = false;
        Check(!probe.ShouldHoldBlackScreen(), "result display must not retain black screen for held death");
        probe.StandDown = true;
        Check(!probe.ShouldBlockDeathScreen(g) && !probe.ShouldBlockKilledStop(g) && !probe.ShouldSuppressKilledSessionResult(),
            "intentional native stop releases all terminal death guards");
    }
    ArenaSession.IsActive = false;
    probe = new RespawnGuardProbe { IsTerminalTdmHold = true, IsRespawnInProgress = true };
    Check(!probe.ShouldBlockDeathScreen(g) && !probe.ShouldHoldBlackScreen() && !probe.ShouldBlockKilledStop(g)
        && !probe.ShouldSuppressKilledSessionResult(), "ordinary raid is untouched by terminal guard methods");
    ArenaSession.IsActive = true;
}
foreach (bool required in new[] { true, false })
{
    var (g, _, _) = NewExit();
    ArenaMode.Bots.ArenaTeamBoxRuntime.IsRequired = required;
    ArenaMatchService.AbortCalls = 0;
    var probe = new RespawnGuardProbe();
    var failure = probe.AfterBoxFailure(new Exception("box became occupied during body creation"));
    Check(!failure.MoveNext() && probe.FallbackCalled == !required && ArenaMatchService.AbortCalls == (required ? 1 : 0),
        "Equator body failure uses protected abort instead of generic death screen");
    foreach (int invalid in new[] { 0, 1, 2 })
    {
        ArenaMatchService.AbortCalls = 0;
        probe = new RespawnGuardProbe();
        var body = invalid == 0 ? null : new Player { IsYourPlayer = invalid != 1 };
        var continuation = probe.BeforeRespawnEvents(g, body);
        Check(!continuation.MoveNext() && !probe.EnteredEngine && ArenaMatchService.AbortCalls == (required ? 1 : 0),
            "Equator missing exact replacement aborts without publishing respawn");
    }
    foreach (bool valid in new[] { true, false })
    {
        ArenaMode.Bots.ArenaTeamBoxRuntime.ArrivalValid = valid;
        ArenaMode.Bots.ArenaTeamBoxRuntime.ArrivalChecks = 0;
        probe = new RespawnGuardProbe();
        var continuation = probe.BeforeRespawnEvents(g, g.LocalPlayer);
        Check(!continuation.MoveNext() && probe.EnteredEngine == (!required || valid)
            && ArenaMode.Bots.ArenaTeamBoxRuntime.ArrivalChecks == 1,
            "Equator arrival is revalidated before kit and respawn subscribers");
    }
}
for (int invalid = -1; invalid < 7; invalid++)
{
    var (g, w, _) = NewExit();
    g.GameWorld = w;
    Comfort.Common.Singleton<AbstractGame>.Instance = g;
    Comfort.Common.Singleton<GameWorld>.Instance = w;
    int epoch = RespawnIdentityProbe.RaidEpoch.Capture();
    switch (invalid)
    {
        case 0: Comfort.Common.Singleton<AbstractGame>.Instance = new LocalGame(); break;
        case 1: Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld(); break;
        case 2: ArenaSession.Exact = false; break;
        case 3: ArenaSession.IsActive = false; break;
        case 4: g.Status = GameStatus.Stopping; break;
        case 5: RespawnIdentityProbe.RaidEpoch.Advance(); break;
        case 6: g.GameWorld = null; break;
    }
    Check(RespawnIdentityProbe.IsSameActiveRaid(g, epoch) == (invalid == -1),
        "detached respawn continuation requires the exact current game and world " + invalid);
    ArenaSession.IsActive = true;
}
Console.WriteLine($"TDM_MATCH_POLICY_EXIT_PASSED ({checks} assertions)");
