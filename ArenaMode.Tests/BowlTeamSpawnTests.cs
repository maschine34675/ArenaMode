using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class BowlTeamSpawnTests
{
    // Frozen Bowl TeamFight evidence from bowl-native-v2 / bowl.modes.json.
    // Source north/south pockets map to east USEC / west BEAR.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (50.16f, 0.047f, -3.25f, 216.8446f),
        (53.346f, 0.047f, -3.043f, 225.5344f),
        (52.986f, 0.047f, -6.003f, 230.1743f),
        (55.966f, 0.047f, -5.853f, 237.9887f),
        (55.976f, 0.047f, -8.703f, 238.7445f),
        (46.224f, 0.047f, -5.707f, 258.2478f),
        (49.41f, 0.047f, -5.5f, 247.4753f),
        (49.05f, 0.047f, -8.46f, 230.1743f),
        (52.03f, 0.047f, -8.31f, 209.5248f),
        (52.04f, 0.047f, -11.16f, 198.9134f),
        (-52.144f, 0.047f, -68.883f, 51.3698f),
        (-55.33f, 0.047f, -69.09f, 51.3698f),
        (-54.97f, 0.047f, -66.13f, 51.3698f),
        (-57.95f, 0.047f, -66.28f, 51.3698f),
        (-57.96f, 0.047f, -63.43f, 51.3698f),
        (-48.764f, 0.047f, -66.183f, 51.3698f),
        (-51.95f, 0.047f, -66.39f, 51.3698f),
        (-51.59f, 0.047f, -63.43f, 51.3698f),
        (-54.57f, 0.047f, -63.58f, 51.3698f),
        (-54.58f, 0.047f, -60.73f, 51.3698f),
    ];

    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_bowl-bot-core-02","Position":{"x":2.5,"y":0.11,"z":-21.1},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.BowlId,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.Bowl;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_BOWL"));
        Assert.Equal("arena_bowl", formation.MapId);
        Assert.Equal("arena-bowl-tdm-v1-", formation.IdPrefix);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-bowl-tdm-v1-{(usec ? "east" : "west")}-{i % 10 + 1:00}";
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
        Assert.Equal(formation.Points[2], formation.BoothAnchor(true));
        Assert.Equal(formation.Points[16], formation.BoothAnchor(false));
        Assert.False(formation.InsideBooth(-15.51f, .1f, -37.64f, false));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Null(formation.Find("arena-bowl-tdm-v1-east-11"));
        Assert.Null(ArenaTeamBoxCatalog.Find("factory4_day"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(ArenaTeamBoxCatalog.Applies("arena_bowl", "tactical"));
    }

    [Theory]
    [InlineData(true, 51.2529f, -7.0645f, 11.1677f, 12.82095f)]
    [InlineData(false, -52.8998f, -64.8147f, 9.82035f, 10.53575f)]
    public void RotatedInteriorRejectsAabbCornersAndOtherFloors(bool usec, float x, float z, float halfX, float halfZ)
    {
        var formation = ArenaTeamBoxCatalog.Bowl;
        double yaw = 318.9815 * Math.PI / 180;
        bool Inside(float localX, float y, float localZ)
            => formation.InsideBooth(x + (float)(Math.Cos(yaw) * localX + Math.Sin(yaw) * localZ),
                y, z + (float)(-Math.Sin(yaw) * localX + Math.Cos(yaw) * localZ), usec);
        foreach (float a in new[] { -halfX + .001f, 0, halfX - .001f })
        foreach (float b in new[] { -halfZ + .001f, 0, halfZ - .001f })
            Assert.True(Inside(a, .047f, b));
        foreach (float a in new[] { -halfX - .01f, halfX + .01f }) Assert.False(Inside(a, .047f, 0));
        foreach (float b in new[] { -halfZ - .01f, halfZ + .01f }) Assert.False(Inside(0, .047f, b));
        Assert.False(formation.InsideBooth(x + halfX, .047f, z + halfZ, usec));
        foreach (float y in new[] { -.151f, 4.135f, float.NaN, float.PositiveInfinity }) Assert.False(Inside(0, y, 0));
        Assert.False(formation.InsideBooth(float.NaN, .047f, z, usec));
        Assert.False(formation.InsideBooth(x, .047f, float.PositiveInfinity, usec));
        Assert.Equal(13.5f, formation.MinimumGateOpeningSeconds);
        Assert.Equal(20f, formation.GateOpeningTimeoutSeconds);
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_bowl", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams!.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.Bowl.Points)
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
            Assert.Equal("ArenaBowl", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
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
        var point = points.Single(p => p.Id == "arena-bowl-tdm-v1-east-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-bowl-tdm-v1-east-11" }); break;
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
            map, "arena_bowl", "teamdeathmatch", 8));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
        if (fault != "missing")
        {
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), "teamdeathmatch", participants: 8));
            Assert.Equal(changed, JsonSerializer.Serialize(map));
        }
    }

    [Theory]
    [InlineData("arena_bowl", "arena_equator")] [InlineData("arena_bowl", "arena_bay5")]
    [InlineData("arena_equator", "arena_bowl")] [InlineData("arena_bay5", "arena_bowl")]
    [InlineData("arena_equator", "arena_bay5")] [InlineData("arena_bay5", "arena_equator")]
    [InlineData("arena_bowl", "arena_airpit")] [InlineData("arena_airpit", "arena_bowl")]
    public void ForeignMapPrivateMarkerCannotEnterAnyProtectedFormation(string ownMap, string foreignMap)
    {
        var own = ArenaTeamBoxCatalog.Find(ownMap)!;
        var foreign = ArenaTeamBoxCatalog.Find(foreignMap)!;
        var donor = ownMap == "arena_equator" ? EquatorTeamSpawnTests.Donor()
            : ownMap == "arena_bay5" ? Bay5TeamSpawnTests.Donor()
            : ownMap == "arena_airpit" ? AirPitTeamSpawnTests.Donor() : Donor();
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
            case "position-x": donor.Position = new Vector3(2.501f, .11f, -21.1f); break;
            case "position-y": donor.Position = new Vector3(2.5f, .111f, -21.1f); break;
            case "position-z": donor.Position = new Vector3(2.5f, .11f, -21.101f); break;
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
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_bowl-bot-core-02").ToList();
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
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_bowl", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
