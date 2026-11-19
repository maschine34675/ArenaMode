using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

// /status carries the server's belief about the Tactical round, so a native run can curl it
// next to the HUD round (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md).
public sealed partial class ArenaAreaServerTests
{
    [Fact]
    public async Task StatusCarriesTheTacticalRoundAndSeqOfTheLiveRaid()
    {
        var router = RouterWithSnapshot();
        Sessions.Arm(Session, ArenaMapCatalog.FactoryDayId, "tactical", armToken: Token);
        using (var armed = JsonDocument.Parse(await InvokeStatus(router)))
        {
            Assert.Equal(1, armed.RootElement.GetProperty("tacticalRound").GetInt32());
            Assert.Equal(0, armed.RootElement.GetProperty("tacticalSeq").GetInt32());
        }

        Start(ArenaMapCatalog.FactoryDayId);
        Assert.Equal(ArenaSessionService.TacticalRoundReportResult.Accepted,
            Sessions.TryReportTacticalRound(Session, Token, 3, 4, out _, out _, out _, out _));
        using (var live = JsonDocument.Parse(await InvokeStatus(router)))
        {
            Assert.Equal(3, live.RootElement.GetProperty("tacticalRound").GetInt32());
            Assert.Equal(4, live.RootElement.GetProperty("tacticalSeq").GetInt32());
        }

        Sessions.MarkIdle(Session);
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(1, idle.RootElement.GetProperty("tacticalRound").GetInt32());
        Assert.Equal(0, idle.RootElement.GetProperty("tacticalSeq").GetInt32());
    }
}
