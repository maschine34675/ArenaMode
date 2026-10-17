using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class EquatorTeamSpawnTests
{
    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_equator-bot-atrium","Position":{"x":0,"y":0.2,"z":2},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":30}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaEquatorTeamSpawns.MapId,
        SpawnPointParams = [Donor()],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Theory]
    [InlineData(4)] [InlineData(20)]
    public void FormationAddsIndependentNativeRecordsOnceAndPreservesDonor(int total)
    {
        var map = Map(); var donor = map.SpawnPointParams!.Single();
        string before = JsonSerializer.Serialize(donor);
        ArenaLocationMutator.Mutate(map, new(), new(), ArenaEquatorTeamSpawns.ModeId, participants: total);
        ArenaLocationMutator.ValidateParticipantPopulation(map, map.Id!, ArenaEquatorTeamSpawns.ModeId, total);
        Assert.Equal(before, JsonSerializer.Serialize(donor));
        Assert.Equal(21, map.SpawnPointParams!.Count());
        foreach (var spec in ArenaEquatorTeamSpawns.Points)
        {
            var point = map.SpawnPointParams.Single(p => p.Id == spec.Id);
            Assert.NotSame(donor, point); Assert.NotSame(donor.ColliderParams, point.ColliderParams);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaEquator", point.Infiltration);
            Assert.Equal("BotZone_Main", point.BotZoneName);
        }
        string once = JsonSerializer.Serialize(map);
        ArenaLocationMutator.Mutate(map, new(), new(), ArenaEquatorTeamSpawns.ModeId, participants: total);
        Assert.Equal(once, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("position")] [InlineData("side")] [InlineData("category")]
    [InlineData("zone")] [InlineData("core")] [InlineData("collider")]
    [InlineData("rotation")] [InlineData("delay")]
    public void FinalResponseRejectsChangedOrMissingFormationWithoutRepair(string fault)
    {
        var map = Map();
        ArenaLocationMutator.Mutate(map, new(), new(), ArenaEquatorTeamSpawns.ModeId, participants: 4);
        var points = map.SpawnPointParams!.ToList();
        var point = points.Single(p => p.Id == ArenaEquatorTeamSpawns.Points[0].Id);
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = ArenaEquatorTeamSpawns.IdPrefix + "unknown" }); break;
            case "duplicate": points.Add(point with { }); break;
            case "position": point.Position = new Vector3(0, .2f, 2); break;
            case "side": point.Sides = ["All"]; break;
            case "category": point.Categories = ["Player"]; break;
            case "zone": point.BotZoneName = "other"; break;
            case "core": point.CorePointId = 10; break;
            case "collider": point.ColliderParams = Donor().ColliderParams; break;
            case "rotation": point.Rotation = 90; break;
            case "delay": point.DelayToCanSpawnSec = 1; break;
        }
        map.SpawnPointParams = points;
        string changed = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateParticipantPopulation(
            map, map.Id!, ArenaEquatorTeamSpawns.ModeId, 4));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("deathmatch")] [InlineData("gungame")] [InlineData("zombie")]
    public void OtherEquatorModesDoNotGainOrEditTeamMarkers(string mode)
    {
        var map = Map(); string before = JsonSerializer.Serialize(map.SpawnPointParams);
        ArenaLocationMutator.Mutate(map, new(), new(), mode,
            participants: mode == "zombie" ? null : 4);
        Assert.Equal(before, JsonSerializer.Serialize(map.SpawnPointParams));
    }

    [Fact]
    public void UnpreparedOrCollidingDonorFailsBeforeAnyMutation()
    {
        foreach (var fault in new[] { "missing", "changed", "duplicate" })
        {
            var map = Map(); var points = map.SpawnPointParams!.ToList();
            if (fault == "missing") points.Clear();
            else if (fault == "changed") points[0].BotZoneName = "other";
            else points.Add(points[0] with { });
            map.SpawnPointParams = points; string before = JsonSerializer.Serialize(map);
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), ArenaEquatorTeamSpawns.ModeId, participants: 4));
            Assert.Equal(before, JsonSerializer.Serialize(map));
        }
    }

    [Fact]
    public void PhysicalCapacityLimitsOnlyIndependentEquatorTeams()
    {
        Assert.Equal(4, ArenaParticipantPolicy.DefaultTotal("arena_equator", "teamdeathmatch"));
        Assert.Equal(ArenaEquatorTeamSpawns.MaximumParticipants,
            ArenaParticipantPolicy.MaximumFor("arena_equator", "teamdeathmatch"));
        foreach (int total in new[] { 4, 10, 20 })
            Assert.True(ArenaParticipantPolicy.TryResolve("arena_equator", "teamdeathmatch", false, false, total, out _, out _));
        foreach (int? total in new int?[] { null, 3, 21, 22, 24 })
            Assert.False(ArenaParticipantPolicy.TryResolve("arena_equator", "teamdeathmatch", false, false, total, out _, out _));
        Assert.True(ArenaParticipantPolicy.TryResolve("arena_equator", "deathmatch", false, false, 24, out _, out _));
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_bay5", "teamdeathmatch", false, false, 24, out _, out _));
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_airpit", "teamdeathmatch", false, false, 24, out _, out _));
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_equator", "teamdeathmatch", false, true, 4, out _, out _));
        Assert.Equal(20, ArenaEquatorTeamSpawns.Points.Select(p => p.Id).Distinct().Count());
        foreach (bool usec in new[] { true, false })
        {
            var points = ArenaEquatorTeamSpawns.Points.Where(p => p.Usec == usec).ToArray();
            Assert.Equal(10, points.Length);
            foreach (var point in points)
            {
                Assert.InRange(point.X, -8f, 8f); Assert.InRange(point.Y, 0, .2f);
                Assert.InRange(point.Z, usec ? 44f : -43f, usec ? 50f : -38f);
                foreach (var other in points.Where(p => p != point))
                    Assert.True(Math.Pow(point.X - other.X, 2) + Math.Pow(point.Z - other.Z, 2) > 2.25,
                        "Simultaneous formation points must exceed the runtime occupancy radius.");
            }
        }
    }
}
