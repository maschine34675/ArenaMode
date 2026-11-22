using System.Text.Json;
using System.Reflection;
using System.Collections;
using ArenaMode.Server.Compat;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Common;
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

    [Fact]
    public void ServerItemRefundBarrierSerializesOfferingBeforeMailAndPreservesOtherProfiles()
    {
        const string profile = "111111111111111111111111", other = "222222222222222222222222";
        var type = typeof(ArenaTarkovaColaState);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        object sync = type.GetField("Sync", flags)!.GetValue(null)!;
        var scopes = (IDictionary)type.GetField("Scopes", flags)!.GetValue(null)!;
        var scopeType = type.GetNestedType("Scope", BindingFlags.NonPublic)!;
        object scope = Activator.CreateInstance(scopeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { "arm", "zombies", "{}", "{}", RewardColaCatalog.LatestVersion }, null)!;
        var refund = type.GetMethod("Refund", flags)!;
        var exit = type.GetMethod("ExitOperation", flags)!;
        lock (sync)
        {
            Assert.False(scopes.Contains(profile));
            scopes.Add(profile, scope);
            try
            {
                (bool Original, bool Result) Call(string id)
                {
                    object?[] args = { new MongoId(id), true, false };
                    try
                    {
                        bool original = (bool)refund.Invoke(null, args)!;
                        Assert.True((bool)args[2]!); // lock held through the actual native method
                        Assert.True(Monitor.IsEntered(sync));
                        return (original, (bool)args[1]!);
                    }
                    finally { exit.Invoke(null, new[] { args[2] }); }
                }
                Assert.Equal((false, false), Call(profile)); // no cooldown, profile write or item mail
                Assert.Equal((true, true), Call(other)); // unrelated ordinary profile untouched
                scopes.Remove(profile);
                Assert.Equal((true, true), Call(profile)); // normal queued offering allowed after lease
            }
            finally { scopes.Remove(profile); }
        }
    }
}
