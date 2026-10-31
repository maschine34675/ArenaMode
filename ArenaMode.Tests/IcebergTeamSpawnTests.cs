using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class IcebergTeamSpawnTests
{
    // Frozen Iceberg CheckPoint pocket evidence (iceberg.modes.json, iceberg-native-v2):
    // USEC = Usec house (5..14) in SPAWN_BOUND_DEFENCE (west), BEAR = Bear house (5..14)
    // in SPAWN_BOUND_FIGHTER (east). Bear keeps its three authored heights.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (-39.3812f, 3.2493f, -87.6749f, 75.183f),
        (-40.7162f, 3.2473f, -79.931f, 88.2443f),
        (-41.3192f, 3.2473f, -89.603f, 45.3416f),
        (-39.8452f, 3.2473f, -91.1639f, 52.3511f),
        (-42.9652f, 3.2443f, -92.504f, 85.0953f),
        (-39.7352f, 3.2473f, -94.964f, 358.2447f),
        (-39.9732f, 3.2473f, -85.4959f, 81.4048f),
        (-40.0992f, 3.2473f, -82.904f, 89.3089f),
        (-37.0262f, 3.2473f, -93.7889f, 354.4831f),
        (-44.5252f, 3.2503f, -90.424f, 99.4831f),
        (97.9361f, 0.7345f, -83.7253f, 251.5087f),
        (97.3612f, 0.7345f, -86.7347f, 267.5384f),
        (90.7486f, 1.8862f, -94.3529f, 303.1341f),
        (95.9648f, 0.7345f, -94.074f, 265.3582f),
        (89.2948f, 1.8862f, -86.464f, 271.4964f),
        (94.6365f, 0.7345f, -84.9785f, 266.5088f),
        (90.7653f, 1.8862f, -91.9553f, 258.906f),
        (97.7513f, 0.7345f, -96.1495f, 276.4523f),
        (96.6548f, 0.7345f, -91.174f, 258.9876f),
        (95.2948f, 0.8753f, -89.564f, 273.3514f),
    ];

    // The installed arena_iceberg-bot-core-01 row of ArenaMapPort 0.4.0+32fda33.
    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_iceberg-bot-core-01","Position":{"x":-10.88,"y":3.25,"z":-80.66},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.IcebergId,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.Iceberg;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_ICEBERG"));
        Assert.Same(formation, ArenaTeamBoxCatalog.Find(ArenaMapCatalog.IcebergId));
        Assert.Equal("arena_iceberg", formation.MapId);
        Assert.Equal("Iceberg", formation.DisplayName);
        Assert.Equal("arena-iceberg-tdm-v1-", formation.IdPrefix);
        Assert.Equal("ArenaIceberg", formation.Infiltration);
        Assert.Equal("USEC west / BEAR east", formation.TeamDescription);
        Assert.Equal("BotZone_Main", formation.Zone);
        Assert.Equal(0, formation.CorePointId);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-iceberg-tdm-v1-{(usec ? "west" : "east")}-{i % 10 + 1:00}";
            var point = formation.Points[i];
            var expected = OriginalPoints[i];
            Assert.Equal(id, point.Id);
            Assert.Equal(usec, point.Usec);
            Assert.Equal(expected.X, point.X); Assert.Equal(expected.Y, point.Y);
            Assert.Equal(expected.Z, point.Z); Assert.Equal(expected.Yaw, point.Yaw);
            Assert.Same(point, formation.Find(id));
            Assert.True(formation.Owns(id));
            Assert.True(formation.InsideBooth(point.X, point.Y, point.Z, usec), id + " inside its own box");
            Assert.False(formation.InsideBooth(point.X, point.Y, point.Z, !usec), id + " outside the other box");
        }
        // Bear's terrace and floor heights stay distinct; nothing was flattened.
        Assert.Equal(3, formation.Points.Where(p => !p.Usec).Select(p => p.Y).Distinct().Count());
        Assert.Empty(formation.BoothExits);
        Assert.Same(formation.Points[2], formation.BoothAnchor(true));
        Assert.Same(formation.Points[19], formation.BoothAnchor(false));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Equal("arena_iceberg-bot-core-01", formation.Donor.Id);
        Assert.Equal((-10.88f, 3.25f, -80.66f), (formation.Donor.X, formation.Donor.Y, formation.Donor.Z));
        Assert.Equal((-10.88f, 3.25f, -80.66f), (formation.Destination.X, formation.Destination.Y, formation.Destination.Z));
        Assert.Equal(string.Empty, formation.Destination.Id);
        foreach (bool usec in new[] { true, false })
            Assert.False(formation.InsideBooth(formation.Destination.X, formation.Destination.Y, formation.Destination.Z, usec));
        // Every MapPort build that ships Iceberg exposes GatesOpen; its longest drive is
        // 5 s at drive scale 1.0, inside the ordinary ten-second watchdog.
        Assert.Equal(0f, formation.MinimumGateOpeningSeconds);
        Assert.Equal(10f, formation.GateOpeningTimeoutSeconds);
        Assert.Null(formation.Find("arena-iceberg-tdm-v1-west-11"));
        Assert.Null(formation.Find("arena-iceberg-tdm-v1-east-00"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(formation.Owns(ArenaTeamBoxCatalog.Bowl.Points[0].Id));
        Assert.True(ArenaTeamBoxCatalog.Owns("arena-iceberg-tdm-v1-east-01"));
        Assert.False(ArenaTeamBoxCatalog.Owns("arena_iceberg-bot-core-01"));
        Assert.True(ArenaTeamBoxCatalog.Applies("arena_iceberg", "teamdeathmatch"));
        Assert.True(ArenaTeamBoxCatalog.Applies("ARENA_ICEBERG", "domination"));
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie", "", null! })
            Assert.False(ArenaTeamBoxCatalog.Applies("arena_iceberg", mode));
        Assert.Null(ArenaTeamBoxCatalog.Find("arena_iceberg_extra"));
    }

    // World-aligned interiors (authored yaws 179.9999 / 270.2255 are within 0.23
    // degrees of the axes). Each box is clipped 0.35 m short of its gate plane.
    [Theory]
    [InlineData(true, -48.31f, -36.392f, 2.328f, 10.271f, -96.926f, -76.014f)]
    [InlineData(false, 82.732f, 114.444f, -.4f, 5f, -98.465f, -78.223f)]
    public void WorldAlignedInteriorRejectsCornersJustOutside(bool usec, float minX, float maxX,
        float minY, float maxY, float minZ, float maxZ)
    {
        var formation = ArenaTeamBoxCatalog.Iceberg;
        float midX = (minX + maxX) / 2, midY = (minY + maxY) / 2, midZ = (minZ + maxZ) / 2;
        foreach (float x in new[] { minX + .001f, midX, maxX - .001f })
        foreach (float y in new[] { minY + .001f, midY, maxY - .001f })
        foreach (float z in new[] { minZ + .001f, midZ, maxZ - .001f })
            Assert.True(formation.InsideBooth(x, y, z, usec));
        foreach (float x in new[] { minX - .01f, maxX + .01f }) Assert.False(formation.InsideBooth(x, midY, midZ, usec));
        foreach (float y in new[] { minY - .01f, maxY + .01f }) Assert.False(formation.InsideBooth(midX, y, midZ, usec));
        foreach (float z in new[] { minZ - .01f, maxZ + .01f }) Assert.False(formation.InsideBooth(midX, midY, z, usec));
        foreach (float x in new[] { minX - .01f, maxX + .01f })
        foreach (float z in new[] { minZ - .01f, maxZ + .01f })
            Assert.False(formation.InsideBooth(x, midY, z, usec));
        foreach (float y in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.False(formation.InsideBooth(midX, y, midZ, usec));
        Assert.False(formation.InsideBooth(float.NaN, midY, midZ, usec));
        Assert.False(formation.InsideBooth(midX, midY, float.PositiveInfinity, usec));
        if (usec)
        {
            // The strip between the GATES_B leaves (x -36.042) and the authored
            // SPAWN_BOUND edge (x -32.911) is outside the closed gates.
            Assert.False(formation.InsideBooth(-36.042f, 3.25f, -85.925f, true));
            Assert.False(formation.InsideBooth(-34f, 3.25f, -88f, true));
            // The pocket continues north past the authored box to the CheckPoint gate.
            Assert.True(formation.InsideBooth(-40.7162f, 3.2473f, -77f, true));
            Assert.False(formation.InsideBooth(-38.908f, 6.439f, -75.664f, true));
        }
        else
        {
            // GATES_A leaves (x 82.382) and the authored 16.3 m height are excluded.
            Assert.False(formation.InsideBooth(82.382f, 2.176f, -93.689f, false));
            Assert.False(formation.InsideBooth(81.5f, .7345f, -89f, false));
            Assert.False(formation.InsideBooth(97f, -8f, -88f, false));
            Assert.False(formation.InsideBooth(97f, 8f, -88f, false));
        }
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_iceberg", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams!.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.Iceberg.Points)
        {
            var point = clone.SpawnPointParams!.Single(p => p.Id == spec.Id);
            Assert.NotSame(native[0], point);
            Assert.NotSame(native[0].ColliderParams, point.ColliderParams);
            Assert.Equal(spec.X, point.Position!.Value.X); Assert.Equal(spec.Y, point.Position.Value.Y);
            Assert.Equal(spec.Z, point.Position.Value.Z);
            Assert.Equal(spec.NativeYaw, point.Rotation);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaIceberg", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
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

    [Fact]
    public void DominationUsesTheSameTwentyPrivateRecords()
    {
        var map = Map();
        string sourceBefore = JsonSerializer.Serialize(map);
        var clone = JsonSerializer.Deserialize<LocationBase>(sourceBefore)!;
        ArenaLocationMutator.Mutate(clone, new(), new(), "domination", participants: 8);
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_iceberg", "domination", 8);
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        Assert.Equal(ArenaTeamBoxCatalog.Iceberg.Points.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal),
            clone.SpawnPointParams!.Where(p => ArenaTeamBoxCatalog.Iceberg.Owns(p.Id)).Select(p => p.Id!)
                .OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("position-x")] [InlineData("position-y")] [InlineData("position-z")]
    [InlineData("side")] [InlineData("category")] [InlineData("infiltration")]
    [InlineData("zone")] [InlineData("core")] [InlineData("collider")]
    [InlineData("rotation")] [InlineData("delay")] [InlineData("foreign-equator")]
    [InlineData("foreign-bowl")]
    public void FinalResponseRejectsChangedMissingAndForeignMarkersWithoutRepair(string fault)
    {
        var map = Map();
        ArenaLocationMutator.Mutate(map, new(), new(), "teamdeathmatch", participants: 8);
        var points = map.SpawnPointParams!.ToList();
        var point = points.Single(p => p.Id == "arena-iceberg-tdm-v1-east-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-iceberg-tdm-v1-east-11" }); break;
            case "duplicate": points.Add(point with { }); break;
            case "position-x": point.Position = new Vector3(point.Position!.Value.X + .01f, point.Position.Value.Y, point.Position.Value.Z); break;
            case "position-y": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y + .01f, point.Position.Value.Z); break;
            case "position-z": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y, point.Position.Value.Z + .01f); break;
            case "side": point.Sides = ["Usec"]; break;
            case "category": point.Categories = ["Bot", "BotPmc"]; break;
            case "infiltration": point.Infiltration = "ArenaBowl"; break;
            case "zone": point.BotZoneName = "other"; break;
            case "core": point.CorePointId = 10; break;
            case "collider": point.ColliderParams = Donor().ColliderParams; break;
            case "rotation": point.Rotation = 0; break;
            case "delay": point.DelayToCanSpawnSec = 1; break;
            case "foreign-equator": points.Add(point with { Id = ArenaEquatorTeamSpawns.Points[0].Id }); break;
            case "foreign-bowl": points.Add(point with { Id = ArenaTeamBoxCatalog.Bowl.Points[0].Id }); break;
        }
        map.SpawnPointParams = points;
        string changed = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateParticipantPopulation(
            map, "arena_iceberg", "teamdeathmatch", 8));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
        if (fault != "missing")
        {
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), "teamdeathmatch", participants: 8));
            Assert.Equal(changed, JsonSerializer.Serialize(map));
        }
    }

    [Theory]
    [InlineData("arena_equator")] [InlineData("arena_bay5")] [InlineData("arena_airpit")] [InlineData("arena_bowl")]
    public void ForeignPrivateMarkerCannotEnterTheIcebergFormation(string foreignMap)
    {
        var foreign = ArenaTeamBoxCatalog.Find(foreignMap)!;
        var map = Map();
        map.SpawnPointParams = [Donor(), Donor() with { Id = foreign.Points[0].Id }];
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
            case "id": donor.Id = "arena_iceberg-bot-core-03"; break;
            case "position-x": donor.Position = new Vector3(-10.881f, 3.25f, -80.66f); break;
            case "position-y": donor.Position = new Vector3(-10.88f, 3.251f, -80.66f); break;
            case "position-z": donor.Position = new Vector3(-10.88f, 3.25f, -80.661f); break;
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
    [InlineData("deathmatch")] [InlineData("gungame")] [InlineData("zombie")]
    public void OtherModesKeepNativeMarkersAndDoNotRequireTeamBoxDonor(string mode)
    {
        var map = Map();
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_iceberg-bot-core-01").ToList();
        string before = JsonSerializer.Serialize(map.SpawnPointParams);
        ArenaLocationMutator.Mutate(map, new(), new(), mode,
            participants: mode is "deathmatch" or "gungame" ? 24 : null);
        Assert.Equal(before, JsonSerializer.Serialize(map.SpawnPointParams));
        Assert.DoesNotContain(map.SpawnPointParams!, p => ArenaTeamBoxCatalog.Owns(p.Id));
    }

    [Theory]
    [InlineData(null)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    [InlineData(19)] [InlineData(21)] [InlineData(22)] [InlineData(24)]
    public void InvalidOrOmittedParticipantTotalFailsBeforeAddingAnyMarkers(int? total)
    {
        var map = Map(); string before = JsonSerializer.Serialize(map);
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_iceberg", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
