using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaTransitTests
{
    private static LocationBase Location(string id) => new()
    {
        Id = id, Waves = [], BossLocationSpawn = [],
        Transits = [new() { Id = 12, IsActive = true, Name = "Woods", Location = "Woods", Time = 20 },
            new() { Id = 13, IsActive = false, Name = "Customs", Location = "bigmap", Time = 20 }],
        Exits = [new() { Name = "Gate 3", ExfiltrationTime = 7 }],
    };

    [Theory]
    [InlineData("factory4_day", "gungame")]
    [InlineData("factory4_night", "deathmatch")]
    [InlineData("factory4_day", "teamdeathmatch")]
    [InlineData("factory4_day", "tactical")]
    [InlineData("factory4_day", "zombie")]
    public void InactiveTransitRecordsKeepIdsAndMetadataWithoutMutatingAliases(string id, string mode)
    {
        var location = Location(id);
        var original = location.Transits!;
        var originalJson = JsonSerializer.Serialize(original);
        var exits = location.Exits;
        ArenaLocationMutator.Mutate(location, new(), new(), mode);
        Assert.NotSame(original, location.Transits);
        Assert.Equal(originalJson, JsonSerializer.Serialize(original));
        Assert.Equal(JsonSerializer.Serialize(original.Select(t => t with { IsActive = false })),
            JsonSerializer.Serialize(location.Transits));
        Assert.Same(exits, location.Exits);
        var once = JsonSerializer.Serialize(location.Transits);
        ArenaLocationMutator.Mutate(location, new(), new(), mode);
        Assert.Equal(once, JsonSerializer.Serialize(location.Transits));
        location.Transits = null;
        ArenaLocationMutator.Mutate(location, new(), new(), mode);
        Assert.Null(location.Transits);
    }

    [Fact]
    public void NativeLocationPatchDisablesOnlyTheConfirmedArenaSession()
    {
        var previousSessions = ArenaContext.Sessions;
        var previousConfig = ArenaContext.Config;
        var session = new MongoId("0123456789abcdef01234567");
        try
        {
            ArenaContext.Sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
            ArenaContext.Config = new() { Maps = new() { ["factory4_day"] = new() { Enabled = true } } };
            var location = Location("factory4_day");
            var before = JsonSerializer.Serialize(location);
            void Unchanged(MongoId owner)
            {
                ArenaGenerateLocationAndLootPatch.Postfix(owner, "factory4_day", location);
                Assert.Equal(before, JsonSerializer.Serialize(location));
            }
            Unchanged(session); // Ordinary PMC/Scav.
            var token = ArenaContext.Sessions.Arm(session, "factory4_day", "gungame");
            Unchanged(session); // Menu selection alone is insufficient.
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
                ArenaContext.Sessions.ProcessLocalRaidStart(session, "factory4_day", Protocol.BuildNativeStartIdentity(token)));
            Unchanged(new MongoId("89abcdef0123456701234567"));
            ArenaContext.Config.Maps["factory4_day"].Enabled = false;
            Unchanged(session);
            ArenaContext.Config.Maps["factory4_day"].Enabled = true;
            ArenaGenerateLocationAndLootPatch.Postfix(session, "factory4_day", location);
            Assert.All(location.Transits!, transit => Assert.False(transit.IsActive));
            location = Location("factory4_day");
            ArenaContext.Sessions.BeginPostRaidFinalization(session);
            Unchanged(session);
        }
        finally { ArenaContext.Sessions = previousSessions; ArenaContext.Config = previousConfig; }
    }
}
