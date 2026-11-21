using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class BlockTeamSpawnTests
{
    // Frozen Block TeamFight evidence (yard.modes.json, yard-native-v2; arena_yard, Arena's name
    // Block, unrelated to the Customs yard area): USEC = the ten "Usec house" markers of the
    // south pocket (SPAWN_BOUND_FIGHTER), BEAR = the ten "Bear house" markers of the north
    // pocket (SPAWN_BOUND_FIGHTER (1)), each in ascending marker order (5..14). The names say
    // nothing about the faction; only the containing bound does. One height is not the marker's:
    // Usec house (8) (the fourth row) is authored at 7.2693 but stands on the entrance floor piece,
    // whose collision floor was measured at 7.483 on GPU-2 on 2026-11-21; the catalogue carries
    // that floor, because FloorAndClear (0.15 m) would refuse the authored height.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (-14.2502f, 7.2673f, 4.794f, 345.319f),
        (-16.3742f, 7.2653f, 9.801f, 28.2439f),
        (-22.5742f, 7.5003f, 9.834f, 24.8881f),
        (-19.4762f, 7.483f, 10.224f, 358.2441f),
        (-14.8642f, 7.2673f, 13.299f, 283.2444f),
        (-12.6752f, 7.2673f, 9.876f, 283.2444f),
        (-8.3742f, 7.2673f, 4.824f, 283.2444f),
        (-11.7552f, 7.2673f, 4.846f, 341.152f),
        (-22.8812f, 6.8833f, 12.477f, 14.603f),
        (-19.9562f, 6.8833f, 13.494f, 350.1978f),
        (-5.6512f, 6.1873f, 144.096f, 255f),
        (-7.2152f, 6.1923f, 146.586f, 204.3006f),
        (-10.8252f, 6.1773f, 145.085f, 164.9995f),
        (-12.6562f, 6.1773f, 148.057f, 172.1205f),
        (-5.2152f, 6.1773f, 150.206f, 223.2587f),
        (-8.2632f, 6.1773f, 149.683f, 178.2587f),
        (-8.3852f, 6.1773f, 153.506f, 181.1123f),
        (-2.0452f, 6.1773f, 152.166f, 227.7909f),
        (-1.7652f, 6.1773f, 148.416f, 269.6875f),
        (-9.7552f, 6.1773f, 142.836f, 179.6875f),
    ];

    // The installed arena_yard-bot-core-04 row of ArenaMapPort (the donor of Block).
    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_yard-bot-core-04","Position":{"x":-12.88,"y":6.17,"z":87.03},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.YardId,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.Yard;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_YARD"));
        Assert.Same(formation, ArenaTeamBoxCatalog.Find(ArenaMapCatalog.YardId));
        Assert.Equal("arena_yard", formation.MapId);
        Assert.Equal("Block", formation.DisplayName);
        Assert.Equal("arena-yard-tdm-v1-", formation.IdPrefix);
        Assert.Equal("ArenaYard", formation.Infiltration);
        Assert.Equal("USEC south / BEAR north", formation.TeamDescription);
        Assert.Equal("BotZone_Main", formation.Zone);
        Assert.Equal(0, formation.CorePointId);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-yard-tdm-v1-{(usec ? "south" : "north")}-{i % 10 + 1:00}";
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
        // The south pocket is a platform with a ramp (6.8833 .. 7.5003), the north one a flat room
        // (6.1773 .. 6.1923): the whole catalogue lies between 6.1773 and 7.5003.
        Assert.InRange(formation.Points.Min(p => p.Y), 6.1773f, 6.1773f);
        Assert.InRange(formation.Points.Max(p => p.Y), 7.5003f, 7.5003f);
        Assert.Equal(new[] { "Block South Gate", "Block North Gate" }, formation.BoothExits);
        // Usec house (9) = MapPort player-usec (south-05, index 4); Bear house (7) = MapPort
        // player-bear (north-03, index 12). MapPort's two "All" rows are Usec house (10) and
        // Bear house (6), which are not anchors.
        Assert.Same(formation.Points[4], formation.BoothAnchor(true));
        Assert.Same(formation.Points[12], formation.BoothAnchor(false));
        Assert.Equal("arena-yard-tdm-v1-south-05", formation.BoothAnchor(true).Id);
        Assert.Equal("arena-yard-tdm-v1-north-03", formation.BoothAnchor(false).Id);
        Assert.Equal((-14.8642f, 7.2673f, 13.299f), (formation.BoothAnchor(true).X, formation.BoothAnchor(true).Y, formation.BoothAnchor(true).Z));
        Assert.Equal((-10.8252f, 6.1773f, 145.085f), (formation.BoothAnchor(false).X, formation.BoothAnchor(false).Y, formation.BoothAnchor(false).Z));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Equal("arena_yard-bot-core-04", formation.Donor.Id);
        Assert.Equal((-12.88f, 6.17f, 87.03f), (formation.Donor.X, formation.Donor.Y, formation.Donor.Z));
        Assert.Equal((-12.88f, 6.17f, 87.03f), (formation.Destination.X, formation.Destination.Y, formation.Destination.Z));
        Assert.Equal(string.Empty, formation.Destination.Id);
        foreach (bool usec in new[] { true, false })
            Assert.False(formation.InsideBooth(formation.Destination.X, formation.Destination.Y, formation.Destination.Z, usec));
        // Every MapPort build that ships Block exposes GatesOpen, so there is no legacy minimum.
        // The watchdog is fifteen seconds like Chop Shop's: the south gondola is authored at 8.0 s
        // (4.0 s at the default drive scale), and the match flow polls the opening once a second,
        // so the ordinary ten would leave two polls after the drive.
        Assert.Equal(0f, formation.MinimumGateOpeningSeconds);
        Assert.Equal(15f, formation.GateOpeningTimeoutSeconds);
        Assert.Null(formation.Find("arena-yard-tdm-v1-south-11"));
        Assert.Null(formation.Find("arena-yard-tdm-v1-north-00"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(formation.Owns(ArenaTeamBoxCatalog.Iceberg.Points[0].Id));
        Assert.False(formation.Owns(ArenaTeamBoxCatalog.AutoService.Points[0].Id));
        Assert.False(ArenaTeamBoxCatalog.AutoService.Owns("arena-yard-tdm-v1-south-01"));
        Assert.False(ArenaTeamBoxCatalog.Iceberg.Owns("arena-yard-tdm-v1-south-01"));
        Assert.True(ArenaTeamBoxCatalog.Owns("arena-yard-tdm-v1-north-01"));
        Assert.False(ArenaTeamBoxCatalog.Owns("arena_yard-bot-core-04"));
        Assert.True(ArenaTeamBoxCatalog.Applies("arena_yard", "teamdeathmatch"));
        Assert.True(ArenaTeamBoxCatalog.Applies("ARENA_YARD", "domination"));
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie", "", null! })
            Assert.False(ArenaTeamBoxCatalog.Applies("arena_yard", mode));
        Assert.Null(ArenaTeamBoxCatalog.Find("arena_yard_extra"));
        Assert.Null(ArenaTeamBoxCatalog.Find("yard"));
    }

    // The smallest distance of any marker to a face of its own box, pinned per pocket, computed
    // from the literals: south, the lowest marker (6.8833, the platform's lower step) is 0.5558 m
    // above the floor of the band (6.3275) and the tightest horizontal distance is 2.1948 m (Usec
    // house (13) from the x min face); north, the room floor (6.1773) is 0.8498 m above the band
    // floor (5.3275) and the tightest horizontal distance is 1.369 m (Bear house (14) from the z
    // min face). A box that shrinks past a marker or grows past a face shows here.
    [Fact]
    public void SmallestMarkerMarginToItsOwnBoxIsPinned()
    {
        var formation = ArenaTeamBoxCatalog.Yard;
        (float, float, float, float, float, float) south = (-25.076f, -5.347f, 6.3275f, 11.9159f, 1.073f, 16.374f);
        (float, float, float, float, float, float) north = (-14.738f, -.098f, 5.3275f, 8.6f, 141.467f, 155.341f);
        foreach (var (usec, box, expectedAll, expectedHorizontal) in new[]
                 { (true, south, .5558f, 2.1948f), (false, north, .8498f, 1.369f) })
        {
            float all = float.MaxValue, horizontal = float.MaxValue;
            foreach (var p in formation.Points.Where(p => p.Usec == usec))
            {
                float[] h = [p.X - box.Item1, box.Item2 - p.X, p.Z - box.Item5, box.Item6 - p.Z];
                float[] v = [p.Y - box.Item3, box.Item4 - p.Y];
                horizontal = Math.Min(horizontal, h.Min());
                all = Math.Min(all, Math.Min(h.Min(), v.Min()));
            }
            Assert.Equal(expectedAll, all, 3);
            Assert.Equal(expectedHorizontal, horizontal, 3);
        }
    }

    // World-aligned interiors (both authored bounds have yaw 0). Every horizontal face is the
    // authored AABB inset by the .35 m capsule radius; no face is moved to a gate. The south
    // box keeps the authored height band (6.3275 .. 11.9159); the north box is cut at 8.6 because
    // a separate deck at 9.16 .. 9.72 lies inside the authored band (5.3275 .. 10.9159).
    [Theory]
    [InlineData(true, -25.076f, -5.347f, 6.3275f, 11.9159f, 1.073f, 16.374f)]
    [InlineData(false, -14.738f, -.098f, 5.3275f, 8.6f, 141.467f, 155.341f)]
    public void WorldAlignedInteriorRejectsCornersJustOutside(bool usec, float minX, float maxX,
        float minY, float maxY, float minZ, float maxZ)
    {
        var formation = ArenaTeamBoxCatalog.Yard;
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
            // The gate strip: the inset face is z 16.374, so a point at z 16.4 (the gondola's
            // closing cube spans z 16.199 .. 17.065) is outside and z 16.3 (the room's own wall
            // edge, where a held path's corner may stand) is inside; the yard beyond 17.065 is out.
            Assert.False(formation.InsideBooth(-20f, 7.3f, 16.4f, true));
            Assert.True(formation.InsideBooth(-20f, 7.3f, 16.3f, true));
            Assert.False(formation.InsideBooth(-20f, 7.3f, 17.1f, true));
            // The platform and its ramp (6.5 .. 8.65) are inside; the walled-off ground-level band
            // of the outer ring (6.15 .. 6.20) lies below the band and the roof island (12.12)
            // above it.
            Assert.True(formation.InsideBooth(-20f, 7.3f, 8f, true));
            Assert.True(formation.InsideBooth(-20f, 8.65f, 8f, true));
            Assert.False(formation.InsideBooth(-20f, 6.2f, 8f, true));
            Assert.False(formation.InsideBooth(-20f, 12.12f, 8f, true));
            // Known and accepted: the small island at y 7.4 .. 7.76 (x -12.25 .. -10.75, z 13.25 ..
            // 14.25) lies inside the band; it is not connected to the platform floor on the built
            // mesh, so nothing can use it.
            Assert.True(formation.InsideBooth(-11.5f, 7.6f, 13.75f, true));
            // The north pocket's start points are not in the south box.
            Assert.False(formation.InsideBooth(-10.8252f, 6.1773f, 145.085f, true));
        }
        else
        {
            // The top is cut at 8.6: the deck at 9.16 .. 9.72 inside the north footprint
            // (x -13.75 .. -10.25, z 150.25 .. 154.75) is outside, the room floor and a point
            // 2.05 m above the highest connected surface are inside.
            Assert.False(formation.InsideBooth(-12f, 9.2f, 152f, false));
            Assert.True(formation.InsideBooth(-12f, 8.5f, 152f, false));
            Assert.False(formation.InsideBooth(-12f, 9.72f, 152f, false));
            Assert.True(formation.InsideBooth(-12f, 6.2f, 152f, false));
            Assert.True(formation.InsideBooth(-12f, 5.4f, 152f, false));
            Assert.False(formation.InsideBooth(-12f, 5.3f, 152f, false));
            // The sealed corridor to the gate leaves (x -11.38 .. -7.88, z 128.6 .. 142) is
            // outside while it is still sealed, in both boxes: the box stays the authored room.
            Assert.False(formation.InsideBooth(-9.6f, 6.2f, 135f, false));
            Assert.False(formation.InsideBooth(-9.6f, 6.2f, 135f, true));
            Assert.False(formation.InsideBooth(-9.6f, 6.2f, 141.4f, false));
            Assert.True(formation.InsideBooth(-9.6f, 6.2f, 142f, false));
            // Known and accepted: the island at y 7.04 .. 7.5 (x -6.25 .. -3.25, z 153.75 .. 154.25)
            // and the unreachable ground in the south-east corner of the footprint lie inside the
            // box; no held path or formation point can use them.
            Assert.True(formation.InsideBooth(-4.75f, 7.3f, 154f, false));
            Assert.True(formation.InsideBooth(-3f, 6.2f, 143f, false));
            // The south pocket's start points are not in the north box.
            Assert.False(formation.InsideBooth(-14.8642f, 7.2673f, 13.299f, false));
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_yard", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams!.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.Yard.Points)
        {
            var point = clone.SpawnPointParams!.Single(p => p.Id == spec.Id);
            Assert.NotSame(native[0], point);
            Assert.NotSame(native[0].ColliderParams, point.ColliderParams);
            Assert.Equal(spec.X, point.Position!.Value.X); Assert.Equal(spec.Y, point.Position.Value.Y);
            Assert.Equal(spec.Z, point.Position.Value.Z);
            // Independent of NativeYaw: every authored Block yaw lies in 0..359.99,
            // where the native Euler equivalent is the authored value itself.
            Assert.InRange(spec.Yaw, 0f, 359.99f);
            Assert.Equal(spec.Yaw, point.Rotation);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaYard", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_yard", "domination", 8);
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        Assert.Equal(ArenaTeamBoxCatalog.Yard.Points.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal),
            clone.SpawnPointParams!.Where(p => ArenaTeamBoxCatalog.Yard.Owns(p.Id)).Select(p => p.Id!)
                .OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("position-x")] [InlineData("position-y")] [InlineData("position-z")]
    [InlineData("side")] [InlineData("category")] [InlineData("infiltration")]
    [InlineData("zone")] [InlineData("core")] [InlineData("collider")]
    [InlineData("rotation")] [InlineData("delay")] [InlineData("foreign-equator")]
    [InlineData("foreign-bowl")] [InlineData("foreign-iceberg")] [InlineData("foreign-chopshop")]
    public void FinalResponseRejectsChangedMissingAndForeignMarkersWithoutRepair(string fault)
    {
        var map = Map();
        ArenaLocationMutator.Mutate(map, new(), new(), "teamdeathmatch", participants: 8);
        var points = map.SpawnPointParams!.ToList();
        var point = points.Single(p => p.Id == "arena-yard-tdm-v1-south-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-yard-tdm-v1-south-11" }); break;
            case "duplicate": points.Add(point with { }); break;
            case "position-x": point.Position = new Vector3(point.Position!.Value.X + .01f, point.Position.Value.Y, point.Position.Value.Z); break;
            case "position-y": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y + .01f, point.Position.Value.Z); break;
            case "position-z": point.Position = new Vector3(point.Position!.Value.X, point.Position.Value.Y, point.Position.Value.Z + .01f); break;
            case "side": point.Sides = ["Bear"]; break;
            case "category": point.Categories = ["Bot", "BotPmc"]; break;
            case "infiltration": point.Infiltration = "ArenaBowl"; break;
            case "zone": point.BotZoneName = "other"; break;
            case "core": point.CorePointId = 10; break;
            case "collider": point.ColliderParams = Donor().ColliderParams; break;
            case "rotation": point.Rotation = 0; break;
            case "delay": point.DelayToCanSpawnSec = 1; break;
            case "foreign-equator": points.Add(point with { Id = ArenaEquatorTeamSpawns.Points[0].Id }); break;
            case "foreign-bowl": points.Add(point with { Id = ArenaTeamBoxCatalog.Bowl.Points[0].Id }); break;
            case "foreign-iceberg": points.Add(point with { Id = ArenaTeamBoxCatalog.Iceberg.Points[0].Id }); break;
            case "foreign-chopshop": points.Add(point with { Id = ArenaTeamBoxCatalog.AutoService.Points[0].Id }); break;
        }
        map.SpawnPointParams = points;
        string changed = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateParticipantPopulation(
            map, "arena_yard", "teamdeathmatch", 8));
        Assert.Equal(changed, JsonSerializer.Serialize(map));
        if (fault != "missing")
        {
            Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
                map, new(), new(), "teamdeathmatch", participants: 8));
            Assert.Equal(changed, JsonSerializer.Serialize(map));
        }
    }

    [Theory]
    [InlineData("arena_equator")] [InlineData("arena_bay5")] [InlineData("arena_airpit")]
    [InlineData("arena_bowl")] [InlineData("arena_iceberg")] [InlineData("arena_autoservice")]
    public void ForeignPrivateMarkerCannotEnterTheBlockFormation(string foreignMap)
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
            case "id": donor.Id = "arena_yard-bot-core-03"; break;
            case "position-x": donor.Position = new Vector3(-12.881f, 6.17f, 87.03f); break;
            case "position-y": donor.Position = new Vector3(-12.88f, 6.171f, 87.03f); break;
            case "position-z": donor.Position = new Vector3(-12.88f, 6.17f, 87.031f); break;
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
    public void OtherModesKeepNativeMarkersAndDoNotRequireTeamBoxDonor(string mode)
    {
        var map = Map();
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_yard-bot-core-04").ToList();
        string before = JsonSerializer.Serialize(map.SpawnPointParams);
        ArenaLocationMutator.Mutate(map, new(), new(), mode,
            participants: mode is "deathmatch" or "gungame" ? 24 : null);
        Assert.Equal(before, JsonSerializer.Serialize(map.SpawnPointParams));
        Assert.DoesNotContain(map.SpawnPointParams!, p => ArenaTeamBoxCatalog.Owns(p.Id));
    }

    [Fact]
    public void MissingModeRejectsWithoutTouchingNativeMap()
    {
        var map = Map(); string before = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(map, new(), new(), null));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }

    [Theory]
    [InlineData(null)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    [InlineData(19)] [InlineData(21)] [InlineData(22)] [InlineData(24)]
    public void InvalidOrOmittedParticipantTotalFailsBeforeAddingAnyMarkers(int? total)
    {
        var map = Map(); string before = JsonSerializer.Serialize(map);
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_yard", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
