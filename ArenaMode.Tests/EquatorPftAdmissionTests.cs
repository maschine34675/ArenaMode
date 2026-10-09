using System.Collections;
using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(ArenaAreaCatalog.FullMapId)]
    [InlineData("stale-area")]
    public void EquatorPftZombiesCannotPrepareOrReplaceAnExistingArm(string? area)
    {
        Assert.False(Sessions.BeginArmPreparation(Session, "ARENA_EQUATOR", "ZOMBIE", true, Token, area));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Sessions.Arm(Session, Map, "zombie", armToken: Token);
        var error = Assert.Throws<ArgumentException>(() =>
            Sessions.Arm(Session, "arena_equator", "zombie", true, areaId: area));
        Assert.Contains("PitFireTeam squad", error.Message);
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, "arena_equator", "zombie", true,
            Token, out _, area));
    }

    [Fact]
    public void EquatorPftZombieReplayAndNativeStartRejectAnOtherwiseMatchingLegacyArm()
    {
        // Simulate an old in-memory arm without calling the now-blocked API.
        // Matching token/map/team identity must not bypass current admission.
        Sessions.Arm(Session, "arena_bay5", "zombie", true, Token);
        var sessions = (IDictionary)typeof(ArenaSessionService).GetField("_sessions",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Sessions)!;
        var entry = sessions[Session.ToString()]!;
        entry.GetType().GetField("Map")!.SetValue(entry, "arena_equator");
        Assert.False(Sessions.TryReplayArmedRequest(Session, "arena_equator", "zombie", true,
            Token, out var replay));
        Assert.Null(replay);
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(ArenaLocalRaidStartDisposition.RejectedArenaStart,
            Sessions.ProcessLocalRaidStart(Session, "arena_equator", Protocol.BuildNativeStartIdentity(Token)));
        Assert.Equal(ArenaSessionState.Armed, Sessions.GetStatus(Session).state);
        Assert.Equal(Token, Sessions.GetArmToken(Session));
    }

    [Fact]
    public async Task EquatorPftZombieRouteRejectsBeforeStaleProfileRecovery()
    {
        var lookup = ArenaOptionalMapPolicy.FindLocation;
        try
        {
            ArenaOptionalMapPolicy.FindLocation = _ => new() { Id = ArenaMapCatalog.EquatorId, Enabled = true };
            var router = RouterWithSnapshot();
            var request = new ArenaRaidStartRequest { Map = ArenaMapCatalog.EquatorId, Mode = "zombie",
                PitFireTeamTeam = true, RequestId = Token };
            using var response = JsonDocument.Parse(await InvokeStart(router, request));
            Assert.False(response.RootElement.GetProperty("armed").GetBoolean());
            Assert.Contains("PitFireTeam squad", response.RootElement.GetProperty("reason").GetString());
            Assert.True(ArenaContext.Snapshots!.HasSnapshot(Session));
            Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        }
        finally { ArenaOptionalMapPolicy.FindLocation = lookup; }
    }

    [Fact]
    public void EquatorSoloZombiesStillPrepareReplayAndStartWhileUnarmedIsOrdinary()
    {
        Assert.Equal(ArenaLocalRaidStartDisposition.Ordinary,
            Sessions.ProcessLocalRaidStart(Other, ArenaMapCatalog.EquatorId, null));
        Assert.True(Sessions.BeginArmPreparation(Session, ArenaMapCatalog.EquatorId, "zombie", false, Token));
        Assert.True(Sessions.CompleteArmPreparation(Session));
        Assert.True(Sessions.TryReplayArmedRequest(Session, ArenaMapCatalog.EquatorId, "zombie", false,
            Token, out var replay));
        Assert.Equal(Token, replay);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            Sessions.ProcessLocalRaidStart(Session, ArenaMapCatalog.EquatorId,
                Protocol.BuildNativeStartIdentity(Token)));
    }
}
