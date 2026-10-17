using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class AirPitTeamSpawnTests
{
    // Frozen AirPit TeamFight scene evidence. North markers 6..10,16..20;
    // south 1..5,11..15. Preserve authored heights and signed headings.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (-12.590000153f, .101999998f, 79.209999084f, -179.851960318f),
        (-15.108654022f, .101999998f, 76.089179993f, -179.851960318f),
        (-17.191347122f, .101999998f, 80.067764282f, -179.851960318f),
        (-22.415035248f, .101999998f, 79.616569519f, -179.851960318f),
        (-19.129999161f, .101999998f, 76.260002136f, -179.851960318f),
        (.326310158f, .101999998f, 78.296867371f, -179.851960318f),
        (-2.813618660f, .101999998f, 75.182609558f, -179.851960318f),
        (-4.896311760f, .101999998f, 79.161193848f, -179.851960318f),
        (-10.244000435f, .101999998f, 76.738998413f, -179.851960318f),
        (-6.834963799f, .101999998f, 75.353431702f, -179.851960318f),
        (-12.569999695f, .079999998f, -74.480003357f, -.000001708f),
        (-15.219999313f, .120999992f, -74.400001526f, -.000001708f),
        (-18.139999390f, .092999995f, -74.290000916f, 35.438785069f),
        (-9.939999580f, .122999966f, -74.400001526f, -.000001708f),
        (-7.269999504f, .136999965f, -74.580001831f, -.000001708f),
        (-11.340000153f, .079999998f, -71.889999390f, -.000001708f),
        (-13.989999771f, .120999992f, -71.809997559f, -.000001708f),
        (-16.909999847f, .092999995f, -71.699996948f, -.000001708f),
        (-8.710000038f, .122999966f, -71.809997559f, -.000001708f),
        (-6.039999962f, .136999965f, -71.989997864f, -36.495658363f),
    ];

    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_airpit-bot-center-east","Position":{"x":-3.21,"y":0.1,"z":-12.09},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.AirPitId,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.AirPit;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_AIRPIT"));
        Assert.Equal("arena_airpit", formation.MapId);
        Assert.Equal("arena-airpit-tdm-v1-", formation.IdPrefix);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-airpit-tdm-v1-{(usec ? "north" : "south")}-{i % 10 + 1:00}";
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
        Assert.Empty(formation.BoothExits);
        Assert.Equal(formation.Points[1], formation.BoothAnchor(true));
        Assert.Equal(formation.Points[17], formation.BoothAnchor(false));
        Assert.False(formation.InsideBooth(-15.51f, .1f, -37.64f, false));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Null(formation.Find("arena-airpit-tdm-v1-north-11"));
        Assert.Null(ArenaTeamBoxCatalog.Find("factory4_day"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(ArenaTeamBoxCatalog.Applies("arena_airpit", "tactical"));
    }

    [Theory]
    [InlineData(true, -36.45f, 13.50f, -.62f, 4.97f, 69.16f, 85.15f)]
    [InlineData(false, -22.81f, -.50f, -.62f, 4.97f, -80.41f, -64.42f)]
    public void NativeBoothBoundsAreInclusiveButNeverExpandToAnotherFloorOrOutsideCorner(
        bool usec, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var formation = ArenaTeamBoxCatalog.AirPit;
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_airpit", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams!.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.AirPit.Points)
        {
            var point = clone.SpawnPointParams!.Single(p => p.Id == spec.Id);
            Assert.NotSame(native[0], point);
            Assert.NotSame(native[0].ColliderParams, point.ColliderParams);
            Assert.Equal(spec.X, point.Position!.Value.X); Assert.Equal(spec.Y, point.Position.Value.Y);
            Assert.Equal(spec.Z, point.Position.Value.Z);
            // Native FixParams compares scalar Euler yaw. Retain its tiny-zero tolerance.
            float expectedYaw = spec.Yaw < -.005729578f ? spec.Yaw + 360f : spec.Yaw;
            Assert.Equal(expectedYaw, point.Rotation);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaAirPit", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
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
        var point = points.Single(p => p.Id == "arena-airpit-tdm-v1-north-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-airpit-tdm-v1-north-11" }); break;
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
            map, "arena_airpit", "teamdeathmatch", 8));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
        if (fault != "missing")
        {
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), "teamdeathmatch", participants: 8));
            Assert.Equal(changed, JsonSerializer.Serialize(map));
        }
    }

    [Theory]
    [InlineData("arena_airpit", "arena_equator")] [InlineData("arena_airpit", "arena_bay5")]
    [InlineData("arena_equator", "arena_airpit")] [InlineData("arena_bay5", "arena_airpit")]
    [InlineData("arena_equator", "arena_bay5")] [InlineData("arena_bay5", "arena_equator")]
    public void ForeignMapPrivateMarkerCannotEnterAnyProtectedFormation(string ownMap, string foreignMap)
    {
        var own = ArenaTeamBoxCatalog.Find(ownMap)!;
        var foreign = ArenaTeamBoxCatalog.Find(foreignMap)!;
        var donor = ownMap == "arena_equator" ? EquatorTeamSpawnTests.Donor()
            : ownMap == "arena_bay5" ? Bay5TeamSpawnTests.Donor() : Donor();
        var map = Map(); map.Id = own.MapId;
        map.SpawnPointParams = [donor, Donor() with { Id = foreign.Points[0].Id }];
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
            case "id": donor.Id = "foreign-center-east"; break;
            case "position-x": donor.Position = new Vector3(-3.211f, 0.1f, -12.09f); break;
            case "position-y": donor.Position = new Vector3(-3.21f, 0.101f, -12.09f); break;
            case "position-z": donor.Position = new Vector3(-3.21f, 0.1f, -12.091f); break;
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
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_airpit-bot-center-east").ToList();
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
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_airpit", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
