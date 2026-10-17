using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class Bay5TeamSpawnTests
{
    // Frozen native level88 TeamFight evidence, independent of the production
    // table. Depot points deliberately keep both authored floor elevations.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (143.009003f, -2.892f, 51.568001f, -90.000355f),
        (139.727005f, -2.892f, 55.222000f, -90.000355f),
        (143.031006f, -2.892f, 54.198002f, -90.000355f),
        (143.125000f, -2.892f, 49.327000f, -90.000355f),
        (142.923004f, -2.892f, 46.603001f, -90.000355f),
        (142.856003f, -2.892f, 43.527000f, -90.000355f),
        (139.746994f, -2.892f, 57.638000f, -90.000355f),
        (139.667999f, -2.892f, 52.910999f, -90.000355f),
        (139.684998f, -2.892f, 50.188000f, -90.000355f),
        (139.826004f, -2.892f, 47.021999f, -90.000355f),
        (53.588001f, -2.920f, -11.539000f, -.000273f),
        (51.981998f, -2.920f, -11.319000f, -.000273f),
        (55.909000f, -2.920f, -11.646000f, -.000273f),
        (49.633999f, -2.920f, -10.908000f, 35.362692f),
        (48.335999f, -2.920f, -8.990000f, 89.999631f),
        (61.165001f, -3.816f, -12.189000f, 57.056374f),
        (49.111000f, -3.815f, -5.086000f, 3.326737f),
        (59.973000f, -3.816f, -8.822000f, -.000273f),
        (55.268002f, -3.815f, -6.678000f, 15.816699f),
        (52.182999f, -3.815f, -6.296000f, -.000377f),
    ];

    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_bay5-bot-west-centre","Position":{"x":63.497,"y":-3.8574,"z":42.2217},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.Bay5Id,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.Bay5;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_BAY5"));
        Assert.Equal("arena_bay5", formation.MapId);
        Assert.Equal("arena-bay5-tdm-v1-", formation.IdPrefix);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-bay5-tdm-v1-{(usec ? "warehouse" : "depot")}-{i % 10 + 1:00}";
            var point = formation.Points[i];
            var expected = OriginalPoints[i];
            Assert.Equal(id, point.Id);
            Assert.Equal(usec, point.Usec);
            Assert.Equal(expected.X, point.X); Assert.Equal(expected.Y, point.Y);
            Assert.Equal(expected.Z, point.Z); Assert.Equal(expected.Yaw, point.Yaw);
            Assert.Same(point, formation.Find(id));
            Assert.True(formation.InsideBooth(point.X, point.Y, point.Z, usec));
            Assert.False(formation.InsideBooth(point.X, point.Y, point.Z, !usec));
        }
        Assert.Equal(5, formation.Points.Count(p => !p.Usec && p.Y == -2.920f));
        Assert.Equal(5, formation.Points.Count(p => !p.Usec && p.Y < -3.8f));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Null(formation.Find("arena-bay5-tdm-v1-warehouse-11"));
        Assert.Null(ArenaTeamBoxCatalog.Find("arena_airpit"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(ArenaTeamBoxCatalog.Applies("arena_bay5", "tactical"));
    }

    [Theory]
    [InlineData(true, 129.01f, 149.59f, -3.24f, 1.04f, 39.78f, 69.46f)]
    [InlineData(false, 45.87f, 66.45f, -4.08f, .20f, -14.46f, 1.53f)]
    public void NativeBoothBoundsAreInclusiveButNeverExpandToAnotherFloorOrOutsideCorner(
        bool usec, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var formation = ArenaTeamBoxCatalog.Bay5;
        foreach (float x in new[] { minX, maxX })
        foreach (float y in new[] { minY, maxY })
        foreach (float z in new[] { minZ, maxZ })
            Assert.True(formation.InsideBooth(x, y, z, usec));
        float midX = (minX + maxX) / 2, midY = (minY + maxY) / 2, midZ = (minZ + maxZ) / 2;
        foreach (float x in new[] { minX - .001f, maxX + .001f, float.NaN, float.PositiveInfinity })
            Assert.False(formation.InsideBooth(x, midY, midZ, usec));
        foreach (float y in new[] { minY - .001f, maxY + .001f, float.NaN, float.NegativeInfinity })
            Assert.False(formation.InsideBooth(midX, y, midZ, usec));
        foreach (float z in new[] { minZ - .001f, maxZ + .001f, float.NaN, float.PositiveInfinity })
            Assert.False(formation.InsideBooth(midX, midY, z, usec));
    }

    [Theory]
    [InlineData(4)] [InlineData(8)] [InlineData(20)]
    public void ProtectedCloneAddsIndependentRecordsOnceAndPreservesEveryNativeRecord(int total)
    {
        var original = Map();
        string sourceBefore = JsonSerializer.Serialize(original);
        var clone = JsonSerializer.Deserialize<LocationBase>(sourceBefore)!;
        // Even a shallow-cloned collection must not leak per-raid markers or
        // nested collider edits into the ordinary map's source records.
        clone.SpawnPointParams = original.SpawnPointParams;
        var native = original.SpawnPointParams!.ToArray();
        ArenaLocationMutator.Mutate(clone, new(), new(), "teamdeathmatch", participants: total);
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_bay5", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.Bay5.Points)
        {
            var point = clone.SpawnPointParams.Single(p => p.Id == spec.Id);
            Assert.NotSame(native[0], point);
            Assert.NotSame(native[0].ColliderParams, point.ColliderParams);
            Assert.Equal(spec.X, point.Position!.Value.X); Assert.Equal(spec.Y, point.Position.Value.Y);
            Assert.Equal(spec.Z, point.Position.Value.Z);
            // Native FixParams compares scalar Euler yaw, so -90 must be 270.
            Assert.Equal(spec.Usec ? 269.999645f : spec.Yaw, point.Rotation);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaBay5", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
            Assert.Equal(0, point.CorePointId); Assert.Equal(4, point.DelayToCanSpawnSec);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(point));
            var collider = json.RootElement.GetProperty("ColliderParams");
            Assert.Equal("SpawnSphereParams", collider.GetProperty("_parent").GetString());
            Assert.Equal(.8f, collider.GetProperty("_props").GetProperty("Radius").GetSingle());
            var center = collider.GetProperty("_props").GetProperty("Center");
            foreach (string axis in new[] { "x", "y", "z" }) Assert.Equal(0f, center.GetProperty(axis).GetSingle());
        }
        string once = JsonSerializer.Serialize(clone);
        ArenaLocationMutator.Mutate(clone, new(), new(), "teamdeathmatch", participants: total);
        Assert.Equal(once, JsonSerializer.Serialize(clone));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData(-90.000355f, 269.999645f)]
    [InlineData(-.000273f, -.000273f)]
    [InlineData(-.000002f, -.000002f)]
    [InlineData(359.999f, -.0010070801f)]
    [InlineData(180f, 180f)]
    [InlineData(0f, 0f)]
    public void BackendYawMatchesNativeEulerRangeWithoutChangingAuthoredYaw(float source, float expected)
    {
        var point = new ArenaTeamBoxCatalog.Point("test", true, 0, 0, 0, source);
        Assert.Equal(expected, point.NativeYaw);
        Assert.Equal(source, point.Yaw);
        Assert.All(ArenaTeamBoxCatalog.Equator.Points, p => Assert.Equal(p.Yaw, p.NativeYaw));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("position-x")] [InlineData("position-y")] [InlineData("position-z")]
    [InlineData("side")] [InlineData("category")] [InlineData("infiltration")]
    [InlineData("zone")] [InlineData("core")] [InlineData("collider")]
    [InlineData("rotation")] [InlineData("delay")] [InlineData("foreign-equator")]
    public void FinalResponseRejectsChangedMissingAndForeignMarkersWithoutRepair(string fault)
    {
        var map = Map();
        ArenaLocationMutator.Mutate(map, new(), new(), "teamdeathmatch", participants: 8);
        var points = map.SpawnPointParams!.ToList();
        var point = points.Single(p => p.Id == "arena-bay5-tdm-v1-warehouse-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-bay5-tdm-v1-warehouse-11" }); break;
            case "duplicate": points.Add(point with { }); break;
            case "position-x": point.Position = new Vector3(point.Position!.Value.X + .01f, point.Position.Value.Y, point.Position.Value.Z); break;
            case "position-y": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y + .01f, point.Position.Value.Z); break;
            case "position-z": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y, point.Position.Value.Z + .01f); break;
            case "side": point.Sides = ["Bear"]; break;
            case "category": point.Categories = ["Bot", "BotPmc"]; break;
            case "infiltration": point.Infiltration = "ArenaEquator"; break;
            case "zone": point.BotZoneName = "other"; break;
            case "core": point.CorePointId = 10; break;
            case "collider": point.ColliderParams = Donor().ColliderParams; break;
            case "rotation": point.Rotation = 0; break;
            case "delay": point.DelayToCanSpawnSec = 1; break;
            case "foreign-equator": points.Add(point with { Id = ArenaEquatorTeamSpawns.Points[0].Id }); break;
        }
        map.SpawnPointParams = points;
        string changed = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateParticipantPopulation(
            map, "arena_bay5", "teamdeathmatch", 8));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
        if (fault != "missing")
        {
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), "teamdeathmatch", participants: 8));
            Assert.Equal(changed, JsonSerializer.Serialize(map));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ForeignMapPrivateMarkerCannotEnterEitherProtectedFormation(bool equator)
    {
        var map = Map();
        if (equator)
        {
            map.Id = ArenaMapCatalog.EquatorId;
            map.SpawnPointParams = [EquatorTeamSpawnTests.Donor(),
                Donor() with { Id = "arena-bay5-tdm-v1-warehouse-01" }];
        }
        else
            map.SpawnPointParams = [Donor(),
                EquatorTeamSpawnTests.Donor() with { Id = ArenaEquatorTeamSpawns.Points[0].Id }];
        string before = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: 8));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("id")]
    [InlineData("position-x")] [InlineData("position-y")] [InlineData("position-z")]
    [InlineData("zone")] [InlineData("core")] [InlineData("category")]
    [InlineData("additional-category")] [InlineData("delay")] [InlineData("collider")]
    public void ChangedNativeDonorFailsBeforeMutatingAnyRaidData(string fault)
    {
        var map = Map(); var points = map.SpawnPointParams!.ToList(); var donor = points[0];
        switch (fault)
        {
            case "missing": points.Remove(donor); break;
            case "duplicate": points.Add(donor with { }); break;
            case "id": donor.Id = "foreign-west-centre"; break;
            case "position-x": donor.Position = new Vector3(63.498f, -3.8574f, 42.2217f); break;
            case "position-y": donor.Position = new Vector3(63.497f, -3.857f, 42.2217f); break;
            case "position-z": donor.Position = new Vector3(63.497f, -3.8574f, 42.222f); break;
            case "zone": donor.BotZoneName = "other"; break;
            case "core": donor.CorePointId = 1; break;
            case "category": donor.Categories = ["Bot"]; break;
            case "additional-category": donor.Categories = ["Bot", "BotPmc", "Player"]; break;
            case "delay": donor.DelayToCanSpawnSec = 3; break;
            case "collider": donor.ColliderParams = null; break;
        }
        map.SpawnPointParams = points; string before = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: 8));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("deathmatch")] [InlineData("gungame")]
    [InlineData("tactical")] [InlineData("zombie")]
    public void OtherModesKeepNativeMarkersAndDoNotRequireTeamBoxDonor(string? mode)
    {
        var map = Map();
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_bay5-bot-west-centre").ToList();
        string before = JsonSerializer.Serialize(map.SpawnPointParams);
        ArenaLocationMutator.Mutate(map, new(), new(), mode,
            participants: mode is "deathmatch" or "gungame" ? 24 : null);
        Assert.Equal(before, JsonSerializer.Serialize(map.SpawnPointParams));
        Assert.DoesNotContain(map.SpawnPointParams!, p => ArenaTeamBoxCatalog.Owns(p.Id));
    }

    [Fact]
    public void MissingModeRejectsWithoutTouchingNativeMap()
    {
        var map=Map(); string before=JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(map,new(),new(),null));
        Assert.Equal(before,JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData(null)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    [InlineData(19)] [InlineData(21)] [InlineData(22)] [InlineData(24)]
    public void InvalidOrOmittedParticipantTotalFailsBeforeAddingAnyMarkers(int? total)
    {
        var map = Map(); string before = JsonSerializer.Serialize(map);
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_bay5", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
