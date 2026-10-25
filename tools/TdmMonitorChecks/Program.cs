using ArenaMode.Compat;
using ArenaMode.Modes;
using ArenaMode.Effects;
using BepInEx.Bootstrap;
using EFT;

int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
}
ArenaMapPortScoreMonitors.Session Claim() => ArenaMapPortScoreMonitors.TryClaimContract(typeof(ArenaMapPort.Client.ScoreMonitors), "check");
ArenaMapPortScoreMonitors.Frame Frame(int score = 9) => new(0xFF6B6B, 0x7FA7FF, 5, score, 4, 2, 3, 1, 65);

// Optional boundary: no plugin, old type, no usable map, a foreign owner,
// unsupported version, malformed signature, throwing property and claim.
FakeHub.Reset(); Chainloader.PluginInfos.Clear();
Check(ArenaMapPortScoreMonitors.TryClaim("owner") == null, "plugin absent is normal");
FakeHub.Reset(); Chainloader.PluginInfos["com.maschine.ArenaMapPort"].Instance = new object();
Check(ArenaMapPortScoreMonitors.TryClaim("owner") == null, "old plugin without contract is normal");
Check(ArenaMapPortScoreMonitors.TryClaimContract(null, "owner") == null, "null type");
Check(ArenaMapPortScoreMonitors.TryClaimContract(typeof(ArenaMapPort.Client.ScoreMonitors), " ") == null, "empty owner");
FakeHub.Reset(); FakeHub.Available = false;
Check(Claim() == null && FakeHub.Claims == 0, "unavailable map is never claimed");
foreach (int version in new[] { 0, 2, int.MaxValue })
{
    FakeHub.Reset(); FakeHub.Version = version;
    Check(Claim() == null && FakeHub.Claims == 0, "unknown contract version " + version);
}
FakeHub.Reset(); FakeHub.RefuseClaim = true;
Check(Claim() == null && FakeHub.Releases == 0, "refused claim owns nothing");
FakeHub.Reset(); FakeHub.Claim("another owner");
Check(Claim() == null && FakeHub.Handle != 0 && FakeHub.Releases == 0, "foreign owner untouched");
FakeHub.Reset();
Check(ArenaMapPortScoreMonitors.TryClaimContract(typeof(WrongSignature), "owner") == null && FakeHub.Claims == 0,
    "float clock signature must match exactly");
Check(ArenaMapPortScoreMonitors.TryClaimContract(typeof(ConvertibleClaimSignature), "owner") == null && FakeHub.Claims == 0,
    "contravariant object claim is not the exact string contract");
FakeHub.Reset(); FakeHub.ThrowGetter = true;
Check(Claim() == null && FakeHub.Claims == 0, "throwing property is contained");
FakeHub.Reset(); FakeHub.ThrowClaim = true;
Check(Claim() == null, "throwing claim is contained");

FakeHub.Reset(); var session = Claim();
Check(session != null && session.Alive && FakeHub.Claims == 1, "positive claim control");
Check(session.Push(Frame()) && FakeHub.Sides == (0xFF6B6B, 0x7FA7FF, 5)
    && FakeHub.Score == (9, 4, 2, 3) && FakeHub.Clock == (1, 65f), "all groups reach contract");
Check(session.Push(Frame()) && FakeHub.SidesCalls == 1 && FakeHub.ScoreCalls == 1 && FakeHub.ClockCalls == 2,
    "unchanged frame only validates handle through idempotent clock");
Check(session.Push(Frame(10)) && FakeHub.SidesCalls == 1 && FakeHub.ScoreCalls == 2, "changed score updates its group");
session.Release(); session.Release();
Check(!session.Alive && FakeHub.Releases == 1 && FakeHub.Handle == 0, "release exactly once");
int clocks = FakeHub.ClockCalls;
Check(!session.Push(Frame()) && FakeHub.ClockCalls == clocks, "released session never pushes");

foreach (var kind in new[] { "clock", "sides", "score" })
foreach (bool throws in new[] { false, true })
{
    FakeHub.Reset(); session = Claim();
    if (throws) FakeHub.ThrowSetter = kind; else FakeHub.FailSetter = kind;
    Check(!session.Push(Frame()), kind + " failure contained (throws=" + throws + ")");
    Check(!session.Alive && FakeHub.Handle == 0 && FakeHub.Releases == 1, kind + " failure releases retained handle");
    session.Release(); Check(!session.Push(Frame()) && FakeHub.Releases == 1, kind + " failure stays closed");
}
FakeHub.Reset(); session = Claim(); session.Push(Frame());
int stale = FakeHub.Handle; FakeHub.Release(stale); FakeHub.Claim("replacement"); int replacement = FakeHub.Handle;
Check(!session.Push(Frame()) && FakeHub.Handle == replacement, "identical stale frame detects loss and cannot release replacement");
FakeHub.Reset(); session = Claim(); FakeHub.ThrowRelease = true;
session.Release(); session.Release();
Check(!session.Alive && FakeHub.Releases == 1, "throwing release is contained and attempted once");

// Pure projection: values deliberately asymmetric so swaps cannot pass by coincidence.
foreach (bool localUsec in new[] { false, true })
foreach (TdmMatchPhase phase in Enum.GetValues<TdmMatchPhase>())
foreach (int limit in new[] { 0, 600, 7200 })
{
    var f = TeamDeathmatchMonitors.FrameFor(localUsec, 7, phase, limit, 100, 103, 37, 19, 11, 3, 6);
    Check(f.ColorA == ArenaTeamPalette.BearRgb && f.ColorB == ArenaTeamPalette.UsecRgb, "faction colors fixed");
    Check(f.ScoreA == (localUsec ? 11 : 19) && f.ScoreB == (localUsec ? 19 : 11), "faction scores " + localUsec);
    Check(f.AliveA == (localUsec ? 6 : 3) && f.AliveB == (localUsec ? 3 : 6) && f.TeamSize == 7, "faction alive " + localUsec);
    var expected = phase switch
    {
        TdmMatchPhase.Assembling => (limit > 0 ? 1 : 0, (float)limit),
        TdmMatchPhase.Countdown => (2, 103f),
        TdmMatchPhase.Live => (limit > 0 ? 2 : 3, limit > 0 ? 100f + limit : 100f),
        _ => (1, limit > 0 ? limit - 37f : 37f)
    };
    Check((f.ClockMode, f.ClockSeconds) == expected, "clock " + phase + " limit " + limit);
}
var unknown = TeamDeathmatchMonitors.FrameFor(true, 5, TdmMatchPhase.Live, 600, 100, 0, 3, 0, 0, -1, -1);
Check(unknown.AliveA == -1 && unknown.AliveB == -1, "unknown counts preserved");
var expired = TeamDeathmatchMonitors.FrameFor(true, 5, TdmMatchPhase.Finished, 600, 100, 0, 605, 0, 0, 0, 0);
Check(expired.ClockSeconds == 0, "expired clock clamps to zero");
try
{
    TeamDeathmatchMonitors.FrameFor(true, 5, (TdmMatchPhase)99, 600, 0, 0, 0, 0, 0, 0, 0);
    throw new Exception("invalid phase accepted");
}
catch (ArgumentOutOfRangeException) { Check(true, "invalid phase rejected"); }

// Link and exercise the real runtime partial, with only game objects doubled.
foreach (bool localUsec in new[] { false, true })
{
    FakeHub.Reset(); var mode = new TeamDeathmatchMode(localUsec);
    mode.Board.FriendlyKills = 3; mode.Board.OpponentKills = 2;
    var friendly = mode.AddBot(localUsec, "friendly");
    mode.AddBot(!localUsec, "enemy");
    mode.AddBot(!localUsec, "preactive", EBotState.PreActive);
    var retired = mode.AddBot(!localUsec, "dead"); retired.GetPlayer.HealthController.IsAlive = false;
    var pooled = mode.AddBot(!localUsec, "pooled"); mode.World.Players["pooled"] = new Player { ProfileId = "pooled" };
    mode.Claim();
    Check(mode.HasSession && FakeHub.Clock == (1, 600f), "runtime assembly claim");
    Check(FakeHub.Owners[0].Length <= 40, "owner uniqueness survives contract truncation");
    Check(FakeHub.Score == (localUsec ? 2 : 3, localUsec ? 3 : 2, localUsec ? 1 : 2, localUsec ? 2 : 1),
        "alive contains only current active bodies and local slot");
    int reads = mode.Board.TotalReads; mode.Push(); mode.Push();
    Check(mode.Board.TotalReads == reads, "unchanged scoreboard avoids repeated scans");
    mode.Board.OpponentKills++; mode.Board.Version++; mode.Push();
    Check(FakeHub.Score.a == 3 && FakeHub.Score.b == 3 && mode.Board.TotalReads == reads + 2,
        "versioned opponent forfeit goes to correct faction");
    mode.Start(100); mode.Push(); Check(FakeHub.Clock == (2, 103f), "runtime countdown absolute deadline");
    mode.Advance(102, false); mode.Push(); Check(FakeHub.Clock == (1, 600f), "lost readiness restores assembly clock");
    mode.Start(105); mode.Push(); Check(FakeHub.Clock == (2, 108f), "restarted countdown has fresh deadline");
    mode.Advance(111); mode.Push(); Check(FakeHub.Clock == (2, 711f), "delayed gate release never backdates clock");
    mode.Local.HealthController.IsAlive = false; mode.Push();
    Check((localUsec ? FakeHub.Score.aliveB : FakeHub.Score.aliveA) == 1, "dead local stops contributing immediately");
    friendly.GetPlayer.AIData.BotOwner = null; mode.Push();
    Check((localUsec ? FakeHub.Score.aliveB : FakeHub.Score.aliveA) == 0, "unbound bot physical owner is excluded");
    mode.Board.FriendlyKills = 10; mode.Board.OpponentKills = 4; mode.Board.Version++;
    mode.Advance(125); mode.Push();
    Check(mode.HasResult && FakeHub.Clock == (1, 586f), "terminal clock freezes at result");
    var finalScore = FakeHub.Score;
    mode.Board.FriendlyKills = 55; mode.Board.OpponentKills = 99; mode.Board.Version++;
    mode.Advance(140); mode.Push();
    Check(FakeHub.Score == finalScore && FakeHub.Clock == (1, 586f) && mode.HasSession,
        "result survives full 15 second hold with frozen scores/time");
    mode.Release(); Check(!mode.HasSession && FakeHub.Handle == 0, "normal runtime release");
}

FakeHub.Reset(); var endless = new TeamDeathmatchMode(true, 0, 0);
endless.Claim(); Check(FakeHub.Clock == (0, 0f), "unlimited assembly hides clock");
endless.Start(100); endless.Advance(103); endless.Push();
Check(FakeHub.Clock == (3, 103f), "unlimited match counts from actual release"); endless.Release();

foreach (var condition in new[] { "ended", "native", "fail", "world", "replacement" })
{
    FakeHub.Reset(); var mode = new TeamDeathmatchMode(true); mode.Claim();
    mode.FinishOrStop(condition); mode.Push(); mode.Push(); mode.Release();
    Check(!mode.HasSession && FakeHub.Handle == 0 && FakeHub.Releases == 1, "exact lifecycle loss releases once: " + condition);
}
FakeHub.Reset(); var brokenScore = new TeamDeathmatchMode(true); brokenScore.Claim();
brokenScore.Board.ThrowRead = true; brokenScore.Board.Version++; brokenScore.Push();
Check(!brokenScore.HasSession && FakeHub.Releases == 1 && FakeHub.Handle == 0, "score mapping failure releases claim");
Check(brokenScore.Rules.Phase == TdmMatchPhase.Assembling, "presentation failure cannot fail match rules");
FakeHub.Reset(); var brokenBody = new TeamDeathmatchMode(true); brokenBody.Claim(); brokenBody.Local.ThrowHealth = true; brokenBody.Push();
Check(!brokenBody.HasSession && FakeHub.Releases == 1 && FakeHub.Handle == 0, "physical body read failure releases claim");
FakeHub.Reset(); var brokenRaid = new TeamDeathmatchMode(true) { ThrowRaidRead = true }; brokenRaid.Claim();
Check(!brokenRaid.HasSession && FakeHub.Claims == 0 && brokenRaid.Rules.Phase == TdmMatchPhase.Assembling,
    "claim preparation failure cannot propagate into match startup");
FakeHub.Reset(); var nativeStop = new TeamDeathmatchMode(true); nativeStop.Claim();
nativeStop.OnNativePresentationStop(); nativeStop.OnNativePresentationStop();
Check(!nativeStop.HasSession && FakeHub.Releases == 1 && FakeHub.Handle == 0,
    "native service early return releases once without a mode tick");
nativeStop.Release(); Check(FakeHub.Releases == 1, "match end after native stop does not release twice");
FakeHub.Reset(); var first = new TeamDeathmatchMode(true); first.Claim(); string firstOwner = FakeHub.Owner;
var second = new TeamDeathmatchMode(false); second.Claim();
Check(!second.HasSession && FakeHub.Owner == firstOwner, "replacement cannot share previous match handle");
first.Release(); second.Claim();
Check(second.HasSession && FakeHub.Owner != firstOwner, "new match uses unique owner");
int secondHandle = FakeHub.Handle; first.Release();
Check(FakeHub.Handle == secondHandle, "old mode cleanup cannot release new match"); second.Release();

Console.WriteLine($"TDMMONITORCHECKS_PASSED ({checks} assertions)");
