using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaLabyrinthAccessTests : IDisposable
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId OtherSession = new("89abcdef0123456701234567");
    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ArenaModeConfig _oldConfig = ArenaContext.Config;

    public ArenaLabyrinthAccessTests()
    {
        ArenaContext.Config = new ArenaModeConfig
        {
            Maps = new Dictionary<string, ArenaMapConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["labyrinth"] = new() { Enabled = true },
            },
        };
        ArenaContext.Sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
    }

    public void Dispose()
    {
        ArenaContext.Sessions = _oldSessions;
        ArenaContext.Config = _oldConfig;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RaidCloneSupportsStandaloneAndCoInstalledMetadataWithoutTouchingSource(bool coInstalled, bool removedKeys)
    {
        var original = Location(coInstalled, removedKeys);
        var sourceJson = JsonSerializer.Serialize(original);
        var clone = JsonSerializer.Deserialize<LocationBase>(sourceJson)!;
        Start();

        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", clone);

        Assert.True(clone.Enabled);
        Assert.False(clone.ForceOnlineRaidInPVE);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
        Assert.Equal(original.AccessKeys, clone.AccessKeys);
        Assert.Equal(original.AccessKeysPvE, clone.AccessKeysPvE);
        Assert.Equal(original.DisabledForScav, clone.DisabledForScav);
        Assert.Equal(original.IconY, clone.IconY); // Position projection is menu-only.
        Assert.Equal(JsonSerializer.Serialize(original.Exits), JsonSerializer.Serialize(clone.Exits));
        Assert.Equal(original.RequiredPlayerLevelMin, clone.RequiredPlayerLevelMin);
        Assert.Equal(original.RequiredPlayerLevelMax, clone.RequiredPlayerLevelMax);
        Assert.True(ArenaContext.Sessions!.TryGetStashedCloneMode(clone, out var mode));
        Assert.Equal("teamdeathmatch", mode);

        // Idempotent Arena access must not duplicate optional upstream exits.
        var once = JsonSerializer.Serialize(clone);
        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", clone);
        Assert.Equal(once, JsonSerializer.Serialize(clone));

        // Later mutations to this real per-raid copy cannot reach database aliases.
        clone.AccessKeys = ["changed-on-raid-copy"];
        clone.Exits!.First().Name = "changed-on-raid-copy";
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData("factory4_day")]
    [InlineData("laboratory")]
    [InlineData("Woods")]
    public void OtherArenaMapsKeepNativeAccessMetadata(string map)
    {
        var location = Location();
        location.Id = map;
        ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "teamdeathmatch");
        Assert.False(location.Enabled);
        Assert.True(location.ForceOnlineRaidInPVE);
        Assert.Equal(325, location.IconY);
        Assert.Equal(["679b9819a2f2dd4da9023512"], location.AccessKeys);
    }

    [Fact]
    public void OrdinaryArmedOtherSessionDisabledMapAndRaidExitAreUnchanged()
    {
        var ordinary = Location();
        var baseline = JsonSerializer.Serialize(ordinary);
        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        ArenaContext.Sessions!.Arm(Session, "labyrinth", "teamdeathmatch");
        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        Start();
        GenerateLocationAndLootPatch.Postfix(OtherSession, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));

        ArenaContext.Config.Maps["labyrinth"].Enabled = false;
        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        ArenaContext.Config.Maps["labyrinth"].Enabled = true;

        ArenaContext.Sessions.BeginPostRaidFinalization(Session);
        GenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        Assert.False(ArenaContext.Sessions.TryGetStashedCloneMode(ordinary, out _));
    }

    private static void Start()
    {
        var token = ArenaContext.Sessions!.Arm(Session, "labyrinth", "teamdeathmatch");
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            ArenaContext.Sessions.ProcessLocalRaidStart(Session, "labyrinth", Protocol.BuildNativeStartIdentity(token)));
    }

    private static LocationBase Location(bool coInstalled = false, bool removedKeys = false) => new()
    {
        Id = "Labyrinth",
        Enabled = coInstalled,
        ForceOnlineRaidInPVE = !coInstalled,
        IconY = coInstalled ? 350 : 325,
        AccessKeys = removedKeys ? [] : ["679b9819a2f2dd4da9023512"],
        AccessKeysPvE = removedKeys ? [] : ["679b9819a2f2dd4da9023512"],
        RequiredPlayerLevelMin = 0,
        RequiredPlayerLevelMax = 100,
        DisabledForScav = !coInstalled,
        Exits = coInstalled
            ? [new Exit { Name = "labir_exit" }, new Exit { Name = "The Way Up (scav)" }]
            : [new Exit { Name = "labir_exit" }],
        Waves = [],
        BossLocationSpawn = [],
    };
}
