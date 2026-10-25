using System.Text.Json;
using ArenaMode.Server.Routers;
using Xunit;

namespace ArenaMode.Tests;

public sealed class TarkovaColaWireTests
{
    [Fact]
    public void ManualClientArmPayloadMatchesCaseSensitiveNativeRequest()
    {
        // Native JsonUtil uses neither a camel-case policy nor case-insensitive
        // reads; the arm client builds this lower-camel JObject explicitly.
        const string json = """{"map":"factory4_day","mode":"zombie","requestId":"11111111111111111111111111111111","colaBaseline":"{\"chv\":2,\"xp\":{}}"}""";
        var request = JsonSerializer.Deserialize<ArenaRaidStartRequest>(json)!;
        Assert.Equal("zombie", request.Mode);
        Assert.Equal("{\"chv\":2,\"xp\":{}}", request.ColaBaseline);
        using var roundtrip = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Assert.Equal(request.ColaBaseline, roundtrip.RootElement.GetProperty("colaBaseline").GetString());
        Assert.False(roundtrip.RootElement.TryGetProperty("ColaBaseline", out _));
    }

    [Fact]
    public void OrdinaryLegacyArmWithoutColaKeepsNoBaseline()
        => Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("""{"map":"factory4_day","mode":"deathmatch"}""")!.ColaBaseline);
}
