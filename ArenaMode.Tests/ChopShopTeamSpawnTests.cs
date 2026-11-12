using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class ChopShopTeamSpawnTests
{
    // Frozen Chop Shop TeamFight evidence (autoservice.modes.json, autoservice-native-v2):
    // USEC = the ten "Usec house" markers inside SPAWN_BOUND_Fighter (east), BEAR = the ten
    // inside SPAWN_BOUND_Fighter (1) (west), in sidecar order. The names say nothing about
    // the faction; only the containing bound does.
    private static readonly (float X, float Y, float Z, float Yaw)[] OriginalPoints =
    [
        (-337.789f, -3.8f, 485.627f, 239.9997f),
        (-338.448f, -3.8f, 490.47f, 269.9997f),
        (-337.87f, -3.8f, 495.48f, 314.9997f),
        (-336.619f, -3.8f, 497.847f, 239.9997f),
        (-337.278f, -3.8f, 502.69f, 269.9997f),
        (-336.7f, -3.8f, 507.7f, 269.9998f),
        (-338.781f, -3.783f, 476.541f, 281.4502f),
        (-338.85f, -3.783f, 482.13f, 255.353f),
        (-340.59f, -3.797f, 506.675f, 271.065f),
        (-334.816f, -3.84f, 482.536f, 255.0871f),
        (-409.9721f, -3.74f, 491.243f, 59.9998f),
        (-409.313f, -3.74f, 487.0409f, 89.9998f),
        (-409.451f, -3.74f, 483.84f, 134.9998f),
        (-409.256f, -3.74f, 499.843f, 89.9997f),
        (-409.402f, -3.74f, 477.391f, 89.9998f),
        (-407.045f, -3.74f, 482.428f, 134.9998f),
        (-406.688f, -3.74f, 501.696f, 92.8713f),
        (-405.933f, -3.72f, 476.098f, 71.763f),
        (-409.503f, -3.72f, 495.572f, 77.189f),
        (-405.941f, -3.72f, 486.419f, 159.278f),
    ];

    // The installed arena_autoservice-bot-core-03 row of ArenaMapPort 0.5.0+b7876d3.
    internal static SpawnPointParam Donor() => JsonSerializer.Deserialize<SpawnPointParam>("""
        {"Id":"arena_autoservice-bot-core-03","Position":{"x":-367.44,"y":-3.79,"z":492.11},"Rotation":0,
        "BotZoneName":"BotZone_Main","CorePointId":0,"DelayToCanSpawnSec":4,
        "Categories":["Bot","BotPmc"],"Sides":["Savage"],"Infiltration":"",
        "ColliderParams":{"_parent":"SpawnSphereParams","_props":{"Center":{"x":0,"y":0,"z":0},"Radius":25}}}
        """)!;

    private static LocationBase Map() => new()
    {
        Id = ArenaMapCatalog.AutoServiceId,
        SpawnPointParams = [Donor(), Donor() with { Id = "unrelated-native-marker" }],
        Waves = [], BossLocationSpawn = [], BotLocationModifier = new(),
    };

    [Fact]
    public void CatalogRetainsAllTwentyOriginalPositionsFactionsAndYaws()
    {
        var formation = ArenaTeamBoxCatalog.AutoService;
        Assert.Same(formation, ArenaTeamBoxCatalog.Find("ARENA_AUTOSERVICE"));
        Assert.Same(formation, ArenaTeamBoxCatalog.Find(ArenaMapCatalog.AutoServiceId));
        Assert.Equal("arena_autoservice", formation.MapId);
        Assert.Equal("Chop Shop", formation.DisplayName);
        Assert.Equal("arena-autoservice-tdm-v1-", formation.IdPrefix);
        Assert.Equal("ArenaAutoService", formation.Infiltration);
        Assert.Equal("USEC east / BEAR west", formation.TeamDescription);
        Assert.Equal("BotZone_Main", formation.Zone);
        Assert.Equal(0, formation.CorePointId);
        Assert.Equal(10, formation.CapacityPerTeam);
        Assert.Equal(20, formation.MaximumParticipants);
        Assert.Equal(20, formation.Points.Count);
        Assert.Equal(20, formation.Points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < OriginalPoints.Length; i++)
        {
            bool usec = i < 10;
            string id = $"arena-autoservice-tdm-v1-{(usec ? "east" : "west")}-{i % 10 + 1:00}";
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
        // The pockets are flat: every floor lies between -3.84 and -3.72.
        Assert.InRange(formation.Points.Min(p => p.Y), -3.84f, -3.84f);
        Assert.InRange(formation.Points.Max(p => p.Y), -3.72f, -3.72f);
        Assert.Empty(formation.BoothExits);
        // Usec house (4) = MapPort player-usec; Usec house (6) = MapPort player-bear.
        Assert.Same(formation.Points[4], formation.BoothAnchor(true));
        Assert.Same(formation.Points[11], formation.BoothAnchor(false));
        Assert.Equal((-337.278f, -3.8f, 502.69f), (formation.BoothAnchor(true).X, formation.BoothAnchor(true).Y, formation.BoothAnchor(true).Z));
        Assert.Equal((-409.313f, -3.74f, 487.0409f), (formation.BoothAnchor(false).X, formation.BoothAnchor(false).Y, formation.BoothAnchor(false).Z));
        Assert.True(formation.BoothAnchor(true).Usec);
        Assert.False(formation.BoothAnchor(false).Usec);
        Assert.Equal("arena_autoservice-bot-core-03", formation.Donor.Id);
        Assert.Equal((-367.44f, -3.79f, 492.11f), (formation.Donor.X, formation.Donor.Y, formation.Donor.Z));
        Assert.Equal((-367.44f, -3.79f, 492.11f), (formation.Destination.X, formation.Destination.Y, formation.Destination.Z));
        Assert.Equal(string.Empty, formation.Destination.Id);
        foreach (bool usec in new[] { true, false })
            Assert.False(formation.InsideBooth(formation.Destination.X, formation.Destination.Y, formation.Destination.Z, usec));
        // Every MapPort build that ships Chop Shop exposes GatesOpen; the longest drive (the
        // big gate) is 8.0 s at drive scale 1.0, which leaves the ten-second watchdog 2 s.
        Assert.Equal(0f, formation.MinimumGateOpeningSeconds);
        Assert.Equal(10f, formation.GateOpeningTimeoutSeconds);
        Assert.Null(formation.Find("arena-autoservice-tdm-v1-east-11"));
        Assert.Null(formation.Find("arena-autoservice-tdm-v1-west-00"));
        Assert.False(formation.Owns(ArenaEquatorTeamSpawns.Points[0].Id));
        Assert.False(formation.Owns(ArenaTeamBoxCatalog.Iceberg.Points[0].Id));
        Assert.True(ArenaTeamBoxCatalog.Owns("arena-autoservice-tdm-v1-west-01"));
        Assert.False(ArenaTeamBoxCatalog.Owns("arena_autoservice-bot-core-03"));
        Assert.True(ArenaTeamBoxCatalog.Applies("arena_autoservice", "teamdeathmatch"));
        Assert.True(ArenaTeamBoxCatalog.Applies("ARENA_AUTOSERVICE", "domination"));
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie", "", null! })
            Assert.False(ArenaTeamBoxCatalog.Applies("arena_autoservice", mode));
        Assert.Null(ArenaTeamBoxCatalog.Find("arena_autoservice_extra"));
    }

    // The smallest distance of any marker to a face of its own box, pinned per pocket: the
    // floor of the box (-4.8) lies 0.96 m below the lowest east marker (-3.84) and 1.06 m
    // below the west floor (-3.74). Horizontally the tightest marker is Usec house (19), 1.817 m
    // from the east x max face. A box that shrinks past a marker or grows past a face shows here.
    [Fact]
    public void SmallestMarkerMarginToItsOwnBoxIsPinned()
    {
        var formation = ArenaTeamBoxCatalog.AutoService;
        (float, float, float, float, float, float) east = (-343.587f, -332.999f, -4.8f, -1f, 473.821f, 511.764f);
        (float, float, float, float, float, float) west = (-413.635f, -402.769f, -4.8f, -1f, 473.498f, 511.441f);
        foreach (var (usec, box, expectedAll, expectedHorizontal) in new[]
                 { (true, east, .96f, 1.817f), (false, west, 1.06f, 2.60f) })
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

    // World-aligned interiors (both authored bounds have yaw exactly 270). The east x min face
    // is the big gate's pocket-side face plus the .35 m capsule radius; the west gate rule is
    // looser than the inset, so the inset stays. Height is cut to y -4.8 .. -1.0 (measured on
    // GPU-2, 2026-11-12: the west roof is walkable at 1.7 .. 1.9, the east islands sit at -1.8
    // .. 1.0, the highest connected pocket surface is a step at y -2.5).
    [Theory]
    [InlineData(true, -343.587f, -332.999f, -4.8f, -1f, 473.821f, 511.764f)]
    [InlineData(false, -413.635f, -402.769f, -4.8f, -1f, 473.498f, 511.441f)]
    public void WorldAlignedInteriorRejectsCornersJustOutside(bool usec, float minX, float maxX,
        float minY, float maxY, float minZ, float maxZ)
    {
        var formation = ArenaTeamBoxCatalog.AutoService;
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
            // The big gate leaf (x -344.202 .. -343.937), the authored bound edge (x -344.2145)
            // and the equator shutter leaf (x -344.650 .. -344.558) are outside the closed box.
            Assert.False(formation.InsideBooth(-343.937f, -3.8f, 490f, true));
            Assert.False(formation.InsideBooth(-344.2145f, -3.8f, 490f, true));
            Assert.False(formation.InsideBooth(-344.6f, -3.8f, 506.8f, true));
            // The pocket's connected raised step (-343.5, -2.5, 489.5) is inside; the
            // separate islands at y -0.8, -0.2 and 1.0 are above the cut.
            Assert.True(formation.InsideBooth(-343.5f, -2.5f, 489.5f, true));
            Assert.False(formation.InsideBooth(-338.7f, -.8f, 511.6f, true));
            Assert.False(formation.InsideBooth(-342.6f, -.2f, 497f, true));
            Assert.False(formation.InsideBooth(-341.6f, 1f, 503.5f, true));
        }
        else
        {
            // The techlight leaves (x -402.320 .. -402.200) are outside; the roof (y 1.7 .. 1.9)
            // and the authored bound's top (2.117) are above the cut.
            Assert.False(formation.InsideBooth(-402.32f, -3.74f, 501.22f, false));
            Assert.False(formation.InsideBooth(-402.2f, -3.74f, 477.51f, false));
            Assert.False(formation.InsideBooth(-408.5f, 1.8f, 490f, false));
            Assert.False(formation.InsideBooth(-408.5f, 2.117f, 490f, false));
            Assert.False(formation.InsideBooth(-410f, -7.7f, 490f, false));
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
        ArenaLocationMutator.ValidateParticipantPopulation(clone, "arena_autoservice", "teamdeathmatch", total);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(original));
        Assert.Equal(22, clone.SpawnPointParams!.Count());
        foreach (var point in native)
            Assert.Same(point, clone.SpawnPointParams!.Single(p => p.Id == point.Id));
        foreach (var spec in ArenaTeamBoxCatalog.AutoService.Points)
        {
            var point = clone.SpawnPointParams!.Single(p => p.Id == spec.Id);
            Assert.NotSame(native[0], point);
            Assert.NotSame(native[0].ColliderParams, point.ColliderParams);
            Assert.Equal(spec.X, point.Position!.Value.X); Assert.Equal(spec.Y, point.Position.Value.Y);
            Assert.Equal(spec.Z, point.Position.Value.Z);
            // Independent of NativeYaw: every authored Chop Shop yaw lies in 0..359.99,
            // where the native Euler equivalent is the authored value itself.
            Assert.InRange(spec.Yaw, 0f, 359.99f);
            Assert.Equal(spec.Yaw, point.Rotation);
            Assert.Equal(new[] { "Player", "Bot", "BotPmc" }, point.Categories);
            Assert.Equal(new[] { spec.Usec ? "Usec" : "Bear" }, point.Sides);
            Assert.Equal("ArenaAutoService", point.Infiltration); Assert.Equal("BotZone_Main", point.BotZoneName);
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

    // Domination is a Team Deathmatch variant in the generic catalog: Applies already
    // names it, so the box catalog would hand it the same twenty records. The resolver's
    // explicit Domination list does not admit Chop Shop yet (that comes with its single
    // control point), so the server mutator still refuses the mode before it touches
    // the map. The admission step replaces this test by the Iceberg shape: twenty
    // private records and no mutation of the source.
    [Fact]
    public void DominationIsStillRefusedBeforeMutationUntilItsAdmissionStep()
    {
        Assert.True(ArenaTeamBoxCatalog.Applies("arena_autoservice", "domination"));
        var map = Map();
        string before = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "domination", participants: 8));
        Assert.Equal(before, JsonSerializer.Serialize(map));
        Assert.DoesNotContain(map.SpawnPointParams!, p => ArenaTeamBoxCatalog.Owns(p.Id));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("position-x")] [InlineData("position-y")] [InlineData("position-z")]
    [InlineData("side")] [InlineData("category")] [InlineData("infiltration")]
    [InlineData("zone")] [InlineData("core")] [InlineData("collider")]
    [InlineData("rotation")] [InlineData("delay")] [InlineData("foreign-equator")]
    [InlineData("foreign-bowl")] [InlineData("foreign-iceberg")]
    public void FinalResponseRejectsChangedMissingAndForeignMarkersWithoutRepair(string fault)
    {
        var map = Map();
        ArenaLocationMutator.Mutate(map, new(), new(), "teamdeathmatch", participants: 8);
        var points = map.SpawnPointParams!.ToList();
        var point = points.Single(p => p.Id == "arena-autoservice-tdm-v1-east-01");
        switch (fault)
        {
            case "missing": points.Remove(point); break;
            case "unknown": points.Add(point with { Id = "arena-autoservice-tdm-v1-east-11" }); break;
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
        }
        map.SpawnPointParams = points;
        string changed = JsonSerializer.Serialize(map);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateParticipantPopulation(
            map, "arena_autoservice", "teamdeathmatch", 8));
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
    [InlineData("arena_bowl")] [InlineData("arena_iceberg")]
    public void ForeignPrivateMarkerCannotEnterTheChopShopFormation(string foreignMap)
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
            case "id": donor.Id = "arena_autoservice-bot-core-02"; break;
            case "position-x": donor.Position = new Vector3(-367.441f, -3.79f, 492.11f); break;
            case "position-y": donor.Position = new Vector3(-367.44f, -3.791f, 492.11f); break;
            case "position-z": donor.Position = new Vector3(-367.44f, -3.79f, 492.111f); break;
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
        map.SpawnPointParams = map.SpawnPointParams!.Where(p => p.Id != "arena_autoservice-bot-core-03").ToList();
        string before = JsonSerializer.Serialize(map.SpawnPointParams);
        string mapBefore = JsonSerializer.Serialize(map);
        void Mutate() => ArenaLocationMutator.Mutate(map, new(), new(), mode,
            participants: mode is "deathmatch" or "gungame" ? 24 : null);
        if (mode == "tactical")
        {
            // The shared resolver refuses Tactical on Chop Shop; the server mutator throws
            // before it changes any raid data, so the native markers stay as they are.
            var refused = Assert.Throws<InvalidOperationException>(Mutate);
            Assert.Contains("Tactical", refused.Message);
            Assert.Equal(mapBefore, JsonSerializer.Serialize(map));
        }
        else Mutate();
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
        Assert.False(ArenaParticipantPolicy.TryResolve("arena_autoservice", "teamdeathmatch", false, false,
            total, out var selected, out var reason));
        Assert.Null(selected); Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            map, new(), new(), "teamdeathmatch", participants: total));
        Assert.Equal(before, JsonSerializer.Serialize(map));
    }
}
