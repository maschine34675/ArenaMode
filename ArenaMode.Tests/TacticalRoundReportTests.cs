using System.Collections;
using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Bots;
using NativeCloner = SPTarkov.Server.Core.Utils.Cloners.FastCloner;

namespace ArenaMode.Tests;

/// <summary>
/// The server half of the Tactical opening rounds: the report, its binding to the exact live raid,
/// the order (seq), the reset where a raid starts, and the band the patch writes per round.
/// The map is 1..66 over 15 minutes, so the default band centres are 25 / 45 / 66 (Maximum)
/// and 15 / 27 / 40 (Reduced).
/// </summary>
public sealed partial class BotTierScalePatchTests
{
    private const int OpeningEnd = 66;

    private static ArenaSessionService.TacticalRoundReportResult Report(
        ArenaSessionService sessions, MongoId id, int round, int seq, out int storedRound, out int storedSeq,
        out bool applies, string? token = null)
        => sessions.TryReportTacticalRound(id, token ?? sessions.GetArmToken(id), round, seq,
            out storedRound, out storedSeq, out applies, out _);

    private static ArenaSessionService.TacticalRoundReportResult Report(
        ArenaSessionService sessions, MongoId id, int round, int seq, string? token = null)
        => Report(sessions, id, round, seq, out _, out _, out _, token);

    private static object RawEntry(ArenaSessionService sessions, MongoId id)
    {
        var store = (IDictionary)typeof(ArenaSessionService).GetField(
            "_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sessions)!;
        return store[id.ToString()]!;
    }

    private static (int Round, int Seq) RawTactical(ArenaSessionService sessions, MongoId id)
    {
        var entry = RawEntry(sessions, id);
        return ((int)entry.GetType().GetField("TacticalRound")!.GetValue(entry)!,
            (int)entry.GetType().GetField("TacticalReportSeq")!.GetValue(entry)!);
    }

    private static void SetRawTactical(ArenaSessionService sessions, MongoId id, int round, int seq)
    {
        var entry = RawEntry(sessions, id);
        entry.GetType().GetField("TacticalRound")!.SetValue(entry, round);
        entry.GetType().GetField("TacticalReportSeq")!.SetValue(entry, seq);
    }

    private static (int Min, int Max, int Level) Band(MongoId id)
    {
        var details = Details(BandMap, 47, 33, 61);
        ArenaBotTierScalePatch.Prefix(id, details);
        return (details.LocationSpecificPmcLevelOverride!.Min, details.LocationSpecificPmcLevelOverride.Max, details.PlayerLevel!.Value);
    }

    [Theory]
    [InlineData("maximum", 25, 45, 66)]
    [InlineData("reduced", 15, 27, 40)]
    public void TheBandFollowsTheReportedRoundAndEndsAtTheFullLevel(string step, int r1, int r2, int full)
    {
        var (sessions, id) = StartBand("tactical", step, end: OpeningEnd);
        Assert.Equal(r1, Band(id).Level);                      // no report yet: round 1
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 1, 1));
        Assert.Equal(r1, Band(id).Level);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 2, 2));
        Assert.Equal(r2, Band(id).Level);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 3, 3));
        Assert.Equal(full, Band(id).Level);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 4, 4));
        Assert.Equal(full, Band(id).Level);
    }

    [Fact]
    public void TheWrittenOverrideIsTheCentrePlusMinusFiveAtEveryRound()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Assert.Equal((20, 30, 25), Band(id));
        Report(sessions, id, 2, 1);
        Assert.Equal((40, 50, 45), Band(id));
        Report(sessions, id, 3, 2);
        Assert.Equal((61, 71, 66), Band(id));
    }

    [Fact]
    public void ARoundOneReportWithAHigherSeqLowersTheBandAgain()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 3, 5, out _, out _, out _));
        Assert.Equal(66, Band(id).Level);
        var result = Report(sessions, id, 1, 6, out var round, out var seq, out var applies);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, result);
        Assert.Equal((1, 6, true), (round, seq, applies));
        Assert.Equal(25, Band(id).Level);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void ASeqThatIsNotAboveTheStoredOneIsADuplicateAndChangesNothing(int seq)
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 2, 2));
        var result = Report(sessions, id, 3, seq, out var round, out var storedSeq, out var applies);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Duplicate, result);
        Assert.Equal((2, 2, true), (round, storedSeq, applies));
        Assert.Equal((2, 2), RawTactical(sessions, id));
        Assert.Equal(45, Band(id).Level);
    }

    [Fact]
    public void RefusedReportsEchoTheStoredValuesAndChangeNothing()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Report(sessions, id, 2, 4);
        string token = sessions.GetArmToken(id)!;

        void Refused(Func<ArenaSessionService.TacticalRoundReportResult> call)
        {
            Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused, call());
            Assert.Equal((2, 4), RawTactical(sessions, id));
            Assert.Equal(45, Band(id).Level);
        }

        Refused(() => Report(sessions, id, 3, 9, "not-the-token"));
        Refused(() => sessions.TryReportTacticalRound(id, null, 3, 9, out _, out _, out _, out _));
        Refused(() => sessions.TryReportTacticalRound(id, "", 3, 9, out _, out _, out _, out _));
        Refused(() => Report(sessions, id, 3, 9, token.ToUpperInvariant()));
        Refused(() => Report(sessions, id, 0, 9));
        Refused(() => Report(sessions, id, 1000, 9));
        Refused(() => Report(sessions, id, -1, 9));
        Refused(() => Report(sessions, id, 3, 0));
        Refused(() => Report(sessions, id, 3, -5));
        // Another session id has no entry at all.
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused,
            Report(sessions, new MongoId(OrdinarySession), 3, 9, token));
        Assert.Equal((2, 4), RawTactical(sessions, id));
        // The last accepted round is the largest legal one.
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 999, 10));
    }

    [Theory]
    [InlineData("deathmatch")]
    [InlineData("gungame")]
    [InlineData("teamdeathmatch")]
    [InlineData("domination")]
    [InlineData("zombie")]
    public void OnlyTacticalRaidsTakeAReport(string mode)
    {
        var (sessions, id) = StartBand(mode, "maximum");
        var result = Report(sessions, id, 2, 1, out var round, out var seq, out var applies);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused, result);
        Assert.False(applies);
        Assert.Equal((1, 0), (round, seq));
        Assert.Equal((1, 0), RawTactical(sessions, id));
    }

    [Fact]
    public void AProtectedTeamRaidRefusesTheReportAndKeepsRoundOne()
    {
        var (sessions, id) = StartBand("tactical", "maximum", protectedTeam: true);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused, Report(sessions, id, 3, 1));
        Assert.Equal((1, 0), RawTactical(sessions, id));
        // Even a stored round of a (hypothetical) earlier state is never read for a protected raid.
        SetRawTactical(sessions, id, 5, 5);
        Assert.True(sessions.TryGetBotTierContext(id, BandMap, out _, out _, out var identity, out _, out _));
        Assert.Equal(1, sessions.GetTacticalRound(identity));
    }

    [Fact]
    public void AnArmedRaidThatIsNotLiveRefusesTheReport()
    {
        ArenaContext.Config.Maps[BandMap].ScaleBotTier = true;
        var id = new MongoId(ArenaSession);
        var sessions = new ArenaSessionService(TestLogger.Create<ArenaSessionService>());
        string token = sessions.Arm(id, BandMap, "tactical", difficulty: "maximum");
        ArenaContext.Sessions = sessions;
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused,
            sessions.TryReportTacticalRound(id, token, 2, 1, out _, out _, out _, out var refusal));
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Equal((1, 0), RawTactical(sessions, id));
    }

    [Fact]
    public void AStaleRaidRefusesTheReport()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        SetElapsed(sessions, id, TimeSpan.FromMinutes(100)); // past RaidTimeMinutes (60) + 15
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Refused, Report(sessions, id, 2, 1));
        Assert.Equal((1, 0), RawTactical(sessions, id));
    }

    [Fact]
    public void AMapThatOptedOutOfTierScalingIsAcceptedAsNotApplyingAndWritesNoBand()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        ArenaContext.Config.Maps[BandMap].ScaleBotTier = false;
        var result = Report(sessions, id, 2, 1, out var round, out _, out var applies);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, result);
        Assert.False(applies);
        Assert.Equal(2, round);
        var details = Details(BandMap, 47, 33, 61);
        ArenaBotTierScalePatch.Prefix(id, details);
        Assert.Equal(47, details.PlayerLevel);
        Assert.Equal(33, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Theory]
    [InlineData(20, 20)]   // no opening: every round is the full level
    [InlineData(30, 30)]   // round 1 is only 5 below full: not a tier
    [InlineData(45, 25)]   // round 1 lighter, round 2 is the full level
    public void ACustomMapsOpeningFollowsTheMinimumGap(int end, int round1)
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: end);
        Assert.Equal(round1, Band(id).Level);
        Report(sessions, id, 2, 1);
        Assert.Equal(end, Band(id).Level);
        Report(sessions, id, 3, 2);
        Assert.Equal(end, Band(id).Level);
    }

    [Fact]
    public void ARoundReportDoesNotMoveABandThatIsAlreadyWrittenAndOwned()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        var wave = Details(BandMap, 47, 33, 61);
        wave.IsPmc = true; wave.Role = "pmcUSEC"; wave.Side = "Usec"; wave.BotCountToGenerate = 15;
        ArenaBotTierScalePatch.Prefix(id, wave);
        Assert.Equal((20, 30), (wave.LocationSpecificPmcLevelOverride!.Min, wave.LocationSpecificPmcLevelOverride.Max));

        // The report lands AFTER this request's band was written and BEFORE the bot system asks for it.
        var clone = new NativeCloner().Clone(wave)!;
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 3, 1));
        var scope = ArenaBotTierOwnership.Begin(id, wave);
        try
        {
            ArenaBotTierOwnership.BindClone(id, clone);
            Assert.True(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out var range));
            Assert.Equal((20, 30), (range!.Min, range.Max));
            // A different range than the one written does not match: the marker still compares exactly.
            clone.LocationSpecificPmcLevelOverride = new(61, 71);
            Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        }
        finally { ArenaBotTierOwnership.ReleaseClone(clone); ArenaBotTierOwnership.End(scope); }

        // The next request is generated at the newer round.
        var next = Details(BandMap, 47, 33, 61);
        next.IsPmc = true; next.Role = "pmcUSEC"; next.Side = "Usec"; next.BotCountToGenerate = 15;
        ArenaBotTierScalePatch.Prefix(id, next);
        Assert.Equal((61, 71), (next.LocationSpecificPmcLevelOverride!.Min, next.LocationSpecificPmcLevelOverride.Max));
    }

    [Fact]
    public void TheRoundIsReadForTheExactEntryOnly()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Assert.Equal(1, sessions.GetTacticalRound(null));
        Report(sessions, id, 3, 1);
        Assert.True(sessions.TryGetBotTierContext(id, BandMap, out _, out _, out var identity, out _, out _));
        Assert.Equal(3, sessions.GetTacticalRound(identity));

        // The same arm string armed again is a different entry: the old identity reads round 1, the new one its own.
        string token = sessions.GetArmToken(id)!;
        sessions.Arm(id, BandMap, "tactical", armToken: token, difficulty: "maximum");
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(id, BandMap, Protocol.BuildNativeStartIdentity(token)));
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 2, 1));
        Assert.True(sessions.TryGetBotTierContext(id, BandMap, out _, out _, out var fresh, out _, out _));
        Assert.Equal(2, sessions.GetTacticalRound(fresh));
        Assert.Equal(1, sessions.GetTacticalRound(identity));

        SetElapsed(sessions, id, TimeSpan.FromMinutes(100)); // past RaidTimeMinutes (60) + 15
        Assert.Equal(1, sessions.GetTacticalRound(fresh));
    }

    [Fact]
    public void AReportIsReadBackThroughStatusStateOnlyWhileTheRaidIsLive()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Assert.Equal((1, 0), sessions.GetTacticalReportState(id));
        Report(sessions, id, 2, 7);
        Assert.Equal((2, 7), sessions.GetTacticalReportState(id));
        SetElapsed(sessions, id, TimeSpan.FromMinutes(100)); // past RaidTimeMinutes (60) + 15
        Assert.Equal((1, 0), sessions.GetTacticalReportState(id));
    }

    // ---- reset exactly where a raid starts, not where a running raid resumes ----

    [Fact]
    public void ARaidThatStartsBeginsAtRoundOneWhateverTheEntryHeld()
    {
        ArenaContext.Config.Maps[BandMap].ScaleBotTier = true;
        var id = new MongoId(ArenaSession);
        var sessions = new ArenaSessionService(TestLogger.Create<ArenaSessionService>());
        string token = sessions.Arm(id, BandMap, "tactical", difficulty: "maximum");
        Assert.Equal((1, 0), RawTactical(sessions, id));
        // A fresh entry would hide a missing reset, so plant leftovers on the ARMED entry first.
        SetRawTactical(sessions, id, 7, 9);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(id, BandMap, Protocol.BuildNativeStartIdentity(token)));
        Assert.Equal((1, 0), RawTactical(sessions, id));
    }

    [Fact]
    public void AReplayedNativeStartOfTheRunningRaidKeepsTheReports()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        string identity = Protocol.BuildNativeStartIdentity(sessions.GetArmToken(id)!);
        Assert.True(sessions.BindNativeEndIdentity(id, identity, "factory.Pmc tactical-test", out _));
        Report(sessions, id, 2, 3);
        Assert.Equal(ArenaLocalRaidStartDisposition.DuplicateArenaStart,
            sessions.ProcessLocalRaidStart(id, BandMap, identity));
        Assert.Equal((2, 3), RawTactical(sessions, id));
    }

    [Fact]
    public void AnAbortedNativeEndReturnsTheSameRaidToInRaidWithItsReports()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        string identity = Protocol.BuildNativeStartIdentity(sessions.GetArmToken(id)!);
        Assert.True(sessions.BindNativeEndIdentity(id, identity, "factory.Pmc tactical-test", out var nativeEnd));
        Report(sessions, id, 2, 3);
        Assert.True(sessions.BeginNativeEndFinalization(id, nativeEnd));
        Assert.True(sessions.AbortNativeEndFinalization(id, nativeEnd));
        Assert.Equal(ArenaSessionState.InRaid, sessions.GetStatus(id).state);
        Assert.Equal((2, 3), RawTactical(sessions, id));
        Assert.Equal(45, Band(id).Level);
        // ...and the raid goes on reporting from where it was: seq 3 is a duplicate, seq 4 applies.
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Duplicate, Report(sessions, id, 3, 3));
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted, Report(sessions, id, 3, 4));
    }

    [Fact]
    public void ANewArmAfterARaidStartsFromTheDefaults()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        Report(sessions, id, 3, 5);
        sessions.MarkIdle(id);
        string token = sessions.Arm(id, BandMap, "tactical", difficulty: "maximum");
        Assert.Equal((1, 0), RawTactical(sessions, id));
        sessions.ProcessLocalRaidStart(id, BandMap, Protocol.BuildNativeStartIdentity(token));
        Assert.Equal((1, 0), RawTactical(sessions, id));
        Assert.Equal(25, Band(id).Level);
    }

    // ---- the wire ----

    [Fact]
    public void TheRequestAndTheRouteHaveTheContractedNames()
    {
        Assert.Equal("/arenamode/tactical-round", Protocol.TacticalRoundRoute);
        var request = JsonSerializer.Deserialize<ArenaTacticalRoundRequest>("{\"armToken\":\"abc\",\"round\":3,\"seq\":7}")!;
        Assert.Equal(("abc", 3, 7), (request.ArmToken, request.Round, request.Seq));
        var empty = JsonSerializer.Deserialize<ArenaTacticalRoundRequest>("{}")!;
        Assert.Equal((null, 0, 0), (empty.ArmToken, empty.Round, empty.Seq));
    }

    private static JsonDocument Handle(ArenaTacticalRoundRequest request, MongoId id)
        => JsonDocument.Parse(ArenaModeRouter.HandleTacticalRound(request, id));

    [Fact]
    public void TheHandlerAnswersAcceptedAppliesAndTheStoredRoundAndSeq()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        string token = sessions.GetArmToken(id)!;

        using (var first = Handle(new() { ArmToken = token, Round = 2, Seq = 1 }, id))
        {
            var root = first.RootElement;
            Assert.True(root.GetProperty("accepted").GetBoolean());
            Assert.True(root.GetProperty("applies").GetBoolean());
            Assert.Equal((2, 1), (root.GetProperty("round").GetInt32(), root.GetProperty("seq").GetInt32()));
            Assert.False(root.GetProperty("duplicate").GetBoolean());
        }

        using (var duplicate = Handle(new() { ArmToken = token, Round = 3, Seq = 1 }, id))
        {
            var root = duplicate.RootElement;
            Assert.True(root.GetProperty("accepted").GetBoolean());
            Assert.True(root.GetProperty("duplicate").GetBoolean());
            Assert.Equal((2, 1), (root.GetProperty("round").GetInt32(), root.GetProperty("seq").GetInt32()));
        }

        using (var wrongToken = Handle(new() { ArmToken = "nope", Round = 3, Seq = 5 }, id))
        {
            Assert.False(wrongToken.RootElement.GetProperty("accepted").GetBoolean());
            Assert.False(wrongToken.RootElement.GetProperty("applies").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(wrongToken.RootElement.GetProperty("reason").GetString()));
        }

        // An ordinary (non-Arena) session has no entry: refused, nothing stored anywhere.
        using (var ordinary = Handle(new() { ArmToken = token, Round = 2, Seq = 1 }, new MongoId(OrdinarySession)))
            Assert.False(ordinary.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal((2, 1), RawTactical(sessions, id));
    }

    [Fact]
    public void ATacticalOptOutMapIsAcceptedWithAppliesFalse()
    {
        var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
        ArenaContext.Config.Maps[BandMap].ScaleBotTier = false;
        using var doc = Handle(new() { ArmToken = sessions.GetArmToken(id), Round = 1, Seq = 1 }, id);
        Assert.True(doc.RootElement.GetProperty("accepted").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("applies").GetBoolean());
    }

    [Fact]
    public void TheDebugLineOfTheBandNamesTheRound()
    {
        // The dedupe is on the level, so the line fires once per stage change; the round rides on it.
        var logger = CapturingLogger.Create(out var debug);
        var previous = ArenaContext.Logger;
        ArenaContext.Logger = logger;
        try
        {
            var (sessions, id) = StartBand("tactical", "maximum", end: OpeningEnd);
            Band(id);
            Report(sessions, id, 2, 1);
            Band(id);
            Band(id);
        }
        finally { ArenaContext.Logger = previous; }
        Assert.Equal(2, debug.Count(line => line.Contains("Bot gear band")));
        Assert.Contains(debug, line => line.Contains("level 20-30") && line.Contains("round=1."));
        Assert.Contains(debug, line => line.Contains("level 40-50") && line.Contains("round=2."));
    }

    public class CapturingLogger : DispatchProxy
    {
        private List<string> _debug = new();

        public static SPTarkov.Common.Models.Logging.ISptLogger<ArenaMode.Server.PatchManager> Create(out List<string> debug)
        {
            var proxy = DispatchProxy.Create<SPTarkov.Common.Models.Logging.ISptLogger<ArenaMode.Server.PatchManager>, CapturingLogger>();
            debug = ((CapturingLogger)(object)proxy)._debug;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "Debug" && args is { Length: > 0 } && args[0] is string text)
                _debug.Add(text);
            return targetMethod?.ReturnType == typeof(void) ? null
                : targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }
}
