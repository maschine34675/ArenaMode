using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

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

        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", clone);

        Assert.True(clone.Enabled);
        Assert.False(clone.ForceOnlineRaidInPVE);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
        Assert.Equal(original.AccessKeys, clone.AccessKeys);
        Assert.Equal(original.AccessKeysPvE, clone.AccessKeysPvE);
        Assert.Equal(original.DisabledForScav, clone.DisabledForScav);
        Assert.Equal(original.IconY, clone.IconY); // Position projection is menu-only.
        AssertOnlyWayUpTimersChanged(original.Exits!, clone.Exits!);
        Assert.Equal(original.RequiredPlayerLevelMin, clone.RequiredPlayerLevelMin);
        Assert.Equal(original.RequiredPlayerLevelMax, clone.RequiredPlayerLevelMax);
        Assert.True(ArenaContext.Sessions!.TryGetStashedCloneMode(clone, out var mode));
        Assert.Equal("teamdeathmatch", mode);

        // Idempotent Arena access must not duplicate optional upstream exits.
        var once = JsonSerializer.Serialize(clone);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", clone);
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
        var originalExits = JsonSerializer.Serialize(location.Exits);
        ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "teamdeathmatch");
        Assert.False(location.Enabled);
        Assert.True(location.ForceOnlineRaidInPVE);
        Assert.Equal(325, location.IconY);
        Assert.Equal(["679b9819a2f2dd4da9023512"], location.AccessKeys);
        Assert.Equal(originalExits, JsonSerializer.Serialize(location.Exits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryArmedOtherSessionDisabledMapAndRaidExitAreUnchanged(bool coInstalled)
    {
        var ordinary = Location(coInstalled);
        var baseline = JsonSerializer.Serialize(ordinary);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        ArenaContext.Sessions!.Arm(Session, "labyrinth", "teamdeathmatch");
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        Start();
        ArenaGenerateLocationAndLootPatch.Postfix(OtherSession, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));

        ArenaContext.Config.Maps["labyrinth"].Enabled = false;
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        ArenaContext.Config.Maps["labyrinth"].Enabled = true;

        ArenaContext.Sessions.BeginPostRaidFinalization(Session);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", ordinary);
        Assert.Equal(baseline, JsonSerializer.Serialize(ordinary));
        Assert.False(ArenaContext.Sessions.TryGetStashedCloneMode(ordinary, out _));
    }

    [Theory]
    [InlineData("endless")]
    [InlineData("rounds")]
    [InlineData("gungame")]
    [InlineData("tactical")]
    [InlineData("teamdeathmatch")]
    public void WayUpIsImmediatelyAvailableWithTenSecondCountdownInEveryArenaMode(string mode)
    {
        var original = Location(coInstalled: true);
        var clone = JsonSerializer.Deserialize<LocationBase>(JsonSerializer.Serialize(original))!;
        Start(mode);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, "labyrinth", clone);
        AssertOnlyWayUpTimersChanged(original.Exits!, clone.Exits!);
    }

    [Fact]
    public void WayUpTimerReplacementDoesNotMutateSharedExitRecords()
    {
        var source = Location(coInstalled: true);
        var before = JsonSerializer.Serialize(source);
        // Even an upstream consumer retaining aliases into the native raid
        // clone must not receive our per-Arena timer writes.
        var clone = source with { };
        var originalRecords = source.Exits!.ToArray();
        ArenaLocationMutator.Mutate(clone, new ArenaMapConfig(), new ArenaModeConfig(), "teamdeathmatch");

        Assert.Equal(before, JsonSerializer.Serialize(source));
        Assert.NotSame(source.Exits, clone.Exits);
        Assert.NotSame(originalRecords[0], clone.Exits!.First());
        Assert.Same(originalRecords[1], clone.Exits!.Skip(1).First());
        Assert.Same(originalRecords[2], clone.Exits!.Last());
        AssertOnlyWayUpTimersChanged(originalRecords, clone.Exits!);
    }

    [Theory]
    [InlineData("The Way Up (scav)")]
    [InlineData("labyrinth_secret_tagilla_key")]
    [InlineData("labir_exit_custom")]
    [InlineData("LABIR_EXIT")]
    public void OtherExitNamesRemainUntouchedWithoutCreatingWayUp(string name)
    {
        var location = Location();
        var exit = TimedExit(name, 27);
        location.Exits = [exit];
        var before = JsonSerializer.Serialize(location.Exits);
        ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "teamdeathmatch");
        Assert.Equal(before, JsonSerializer.Serialize(location.Exits));
        Assert.Same(exit, Assert.Single(location.Exits!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingExitsRemainMissing(bool nullList)
    {
        var location = Location();
        location.Exits = nullList ? null! : [];
        ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "teamdeathmatch");
        if (nullList) Assert.Null(location.Exits);
        else Assert.Empty(location.Exits!);
    }

    private static void Start(string mode = "teamdeathmatch")
    {
        var token = ArenaContext.Sessions!.Arm(Session, "labyrinth", mode);
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
            ? [TimedExit("labir_exit", 57), TimedExit("labyrinth_secret_tagilla_key", 31), TimedExit("The Way Up (scav)", 30)]
            : [TimedExit("labir_exit", 900), TimedExit("labyrinth_secret_tagilla_key", 31)],
        Waves = [],
        BossLocationSpawn = [],
    };

    private static Exit TimedExit(string name, double availability) => new()
    {
        Name = name,
        MinTime = availability,
        MaxTime = availability + 5,
        MinTimePVE = availability + 7,
        MaxTimePVE = availability + 12,
        ExfiltrationTime = 5,
        ExfiltrationTimePVE = 8,
        ExfiltrationType = ExfiltrationType.SharedTimer,
        PassageRequirement = RequirementState.Timer,
        Chance = 100,
        ChancePVE = 87,
        EntryPoints = "keep-native-entry-points",
    };

    private static void AssertOnlyWayUpTimersChanged(IEnumerable<Exit> originals, IEnumerable<Exit> actual)
    {
        var expected = originals.ToArray();
        var result = actual.ToArray();
        Assert.Equal(expected.Length, result.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i].Name != "labir_exit")
            {
                Assert.Equal(JsonSerializer.Serialize(expected[i]), JsonSerializer.Serialize(result[i]));
                continue;
            }
            Assert.Equal(new double?[] { 0, 0, 0, 0, 10, 10 },
                [result[i].MinTime, result[i].MaxTime, result[i].MinTimePVE, result[i].MaxTimePVE,
                 result[i].ExfiltrationTime, result[i].ExfiltrationTimePVE]);
            // Every field outside the requested timers stays unchanged,
            // including mode, requirements, names, chance and entry points.
            Assert.Equal(WithoutTimers(expected[i]), WithoutTimers(result[i]));
        }
    }

    private static string WithoutTimers(Exit exit)
    {
        var value = JsonSerializer.SerializeToNode(exit)!.AsObject();
        foreach (var key in new[] { "MinTime", "MaxTime", "MinTimePVE", "MaxTimePVE", "ExfiltrationTime", "ExfiltrationTimePVE" })
            value.Remove(key);
        return value.ToJsonString();
    }
}
