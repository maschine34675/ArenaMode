using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

internal static partial class Program
{
    private const string UnsafeFactoryId = "c671337f-e58f-4de6-a387-307d877954a3";
    private const string NeighborFactoryId = "c671337f-e58f-4de6-a387-307d877954a4";

    /// <summary>
    /// Every Factory player marker Arena refuses, with the coordinate it was
    /// excluded for. The installed fixture below compares these against the live
    /// database, so a BSG marker move is a failed check rather than a spawn that
    /// silently stops being excluded.
    /// </summary>
    private static readonly (string Id, float X, float Y, float Z)[] UnsafeFactoryPoints =
    {
        (UnsafeFactoryId, 14.018f, 1.18999994f, 58.119f),
        ("c2d01ce0-e0e4-41af-8311-44dc5cf38a93", -6.47099972f, -2.598f, 15.79f),
        ("97998a75-baa4-4b03-ac1f-f86dfbf48d79", -6.689f, -2.598f, 17.303f),
        ("ea010f6f-4860-4339-b576-09439f8f4c3a", -5.58f, -2.598f, 14.757f),
        ("8128ea83-0d4f-4e64-b109-5acc9e477b33", -4.302f, -2.598f, 14.318f),
        ("71c2ba1e-e226-47be-814f-d52ddd931603", -2.81400013f, -2.598f, 18.476f)
    };

    private static void CheckFactory()
    {
        foreach (string map in new[] { "factory4_day", "factory4_night" })
        {
            CheckFactoryBotBoundary(map, armed: true);
            CheckFactoryBotBoundary(map, armed: false);
            CheckFactoryPlanner(map);
            CheckFactoryTeamView(map, armed: true);
            CheckFactoryTeamView(map, armed: false);
        }
        CheckFactoryPolicy();
        CheckFactoryTeamViewIsolation();
        CheckFactoryWorldIsolation();
        CheckInstalledFactoryFixture();
        SetSession("laboratory", false, true);
    }

    private static TestSpawnPoint FactoryPoint(string id, float x = 30f,
        ESpawnCategoryMask category = ESpawnCategoryMask.Bot)
        => new() { Id = id, BotZoneName = "BotZone", Position = new Vector3(x, 0, 0),
            Categories = category, NextBornTime = 23.5f, CorePointId = 17 };

    private static void CheckFactoryBotBoundary(string map, bool armed)
    {
        SetSession(map, armed, !armed);
        string context = $"{map} {(armed ? "armed" : "active")}";
        var bad = FactoryPoint(UnsafeFactoryId, category: ESpawnCategoryMask.Player);
        var safe = FactoryPoint(NeighborFactoryId, 40);
        var points = new List<ISpawnPoint> { safe, bad };
        var before = Snapshot(points);
        Assert(RunAiPostfix(points) == null, context + ": exact unsafe Factory point rejects complete AI batch");
        Assert(ArenaBotSpawnFilter.IsExcludedPoint(bad) && !ArenaBotSpawnFilter.IsExcludedPoint(safe),
            context + ": point predicate distinguishes exact ID from neighboring marker");
        AssertUnchanged(points, before, context + ": AI rejection preserves shared player point and list");

        var zones = SceneZones.Where(_ => true);
        Assert(ReferenceEquals(RunZonePostfix(zones), zones), context + ": no Factory zone exclusions preserve enumerable identity");
        var prefix = RunAiPrefix(Gate1, points);
        Assert(prefix.RunOriginal && ReferenceEquals(prefix.Result, points), context + ": Factory does not inherit Labs gate-zone exclusions");
        var safeList = new List<ISpawnPoint> { safe };
        Assert(ReferenceEquals(RunAiPostfix(safeList), safeList), context + ": safe batch keeps native list identity");

        var spawner = new BotSpawner();
        var data = new BotCreationData();
        int profiles = data.ProfileCount;
        if (RunDispatchPrefix(spawner, data, points, Floor2))
            spawner.SpawnBotsInZoneOnPositions(points, Floor2, data);
        Assert(spawner.DispatchCalls == 0 && spawner.InSpawnProcess == 0,
            context + ": mixed Factory dispatch is denied before native accounting");
        Assert(data.ProfileCount == profiles && data.Positions.Count == 0 && !data.Stopped,
            context + ": denied Factory batch does not edit creation data");
        AssertUnchanged(points, before, context + ": dispatch rejection leaves point IDs categories cooldowns and order unchanged");
        Assert(bad.Categories == ESpawnCategoryMask.Player && !bad.SpawnBlocked,
            context + ": Player-tagged unsafe marker remains unchanged outside bot dispatch");
        if (RunDispatchPrefix(spawner, data, safeList, Floor2))
            spawner.SpawnBotsInZoneOnPositions(safeList, Floor2, data);
        Assert(spawner.DispatchCalls == 1 && spawner.InSpawnProcess == 1 && data.Positions.Count == 1,
            context + ": same creation data remains usable for a safe fallback batch");
    }

    private static void CheckFactoryPlanner(string map)
    {
        SetSession(map, false, true);
        var bad = FactoryPoint(UnsafeFactoryId);
        var samePositionSafe = FactoryPoint(NeighborFactoryId);
        var others = new[] { FactoryPoint("safe40", 40), FactoryPoint("safe50", 50),
            FactoryPoint("safe80", 80), FactoryPoint("safe90", 90) };
        var all = new ISpawnPoint[] { bad, samePositionSafe }.Concat(others).ToArray();
        var zone = new BotZone("BotZone") { SpawnPoints = all };
        var before = Snapshot(all);
        var origin = new Vector3(0, 0, 0);
        int cursor = -1;
        var selected = BurstSpawnPointSelector.PickPoints(zone, origin, 5, ref cursor, out int preferred, out int distant);
        Assert(selected.Count == 5 && selected.All(point => !ReferenceEquals(point, bad)) && selected.Contains(samePositionSafe),
            map + ": planner skips unsafe Factory ID before physical-position dedupe and fills five safe points");
        Assert(preferred == 3 && distant == 2 && BurstSpawnPointSelector.CountUsable(zone, origin) == 5,
            map + ": capacity and selection share safe preferred-plus-distant counts");
        AssertUnchanged(all.ToList(), before, map + ": burst planning does not mutate source markers");

        // A unique bad position must not make a four-safe zone look complete.
        var shortZone = new BotZone("short") { SpawnPoints = new ISpawnPoint[] { FactoryPoint(UnsafeFactoryId, 60) }.Concat(others).ToArray() };
        Assert(BurstSpawnPointSelector.CountUsable(shortZone, origin) == 4,
            map + ": unsafe distinct position never inflates squad capacity");
        int zoneCursor = -1;
        Assert(ReferenceEquals(BurstSpawnPointSelector.PickZone(new[] { shortZone, zone }, origin, 5, ref zoneCursor), zone),
            map + ": planner selects a fully safe five-bot zone over unsafe apparent capacity");
        cursor = -1;
        var withoutLocal = BurstSpawnPointSelector.PickPoints(zone, null, 5, ref cursor, out preferred, out distant);
        Assert(withoutLocal.Count == 5 && !withoutLocal.Contains(bad) && preferred == 5 && distant == 0,
            map + ": no-local-position planner still excludes exact unsafe ID");
    }

    private static SpawnPointsCollection SceneView(params ISpawnPoint[] points)
        => new(points.ToDictionary(point => point, point => new SpawnPointMarker { SpawnPoint = point }),
            new SpawnPointsCollection.Logger());

    private static SpawnPointsCollection RunTeamScenePostfix(SpawnPointsCollection result)
    {
        var method = typeof(ArenaTeamSpawnPointsPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Production protected-team collection postfix missing.");
        var args = new object[] { result };
        method.Invoke(null, args);
        return (SpawnPointsCollection)args[0];
    }

    private static void CheckFactoryTeamView(string map, bool armed)
    {
        SetSession(map, armed, !armed);
        // Deliberately contradictory unused mode proves that the current phase's
        // mode is consulted, not a stale field left over from menu/previous raid.
        ArenaSession.ArmedMode = armed ? "tactical" : "gungame";
        ArenaSession.ActiveMode = armed ? "endless" : "tactical";
        PitFireTeamCompat.TeamArenaActive = true;
        string context = $"{map} {(armed ? "armed" : "active")} team";
        var bad = FactoryPoint(UnsafeFactoryId, category: ESpawnCategoryMask.Player);
        var neighbor = FactoryPoint(NeighborFactoryId, 40, ESpawnCategoryMask.Player);
        var safe = FactoryPoint("safe-bot", 50);
        var original = SceneView(bad, neighbor, safe);
        var dictionary = original._dictionary;
        var entries = dictionary.ToArray();
        var before = Snapshot(original);
        var result = RunTeamScenePostfix(original);
        Assert(!ReferenceEquals(result, original) && result.Count == 2 && !result.Contains(bad) && result.Contains(neighbor),
            context + ": protected team scene view excludes exact Player marker without removing its neighbor");
        Assert(!ReferenceEquals(result._dictionary, dictionary) && ReferenceEquals(result._logger, original._logger),
            context + ": returned collection owns a new dictionary and retains native logger");
        Assert(ReferenceEquals(original._dictionary, dictionary) && dictionary.Count == 3 && dictionary.ContainsKey(bad),
            context + ": original scene collection and unsafe entry remain intact");
        AssertUnchanged(original.ToList(), before, context + ": original point objects retain identity and state");
        Assert(entries.All(entry => ReferenceEquals(dictionary[entry.Key], entry.Value)
            && ReferenceEquals(entry.Value.SpawnPoint, entry.Key) && entry.Value.Active),
            context + ": scene marker references and active state remain unchanged");
        Assert(result.All(point => ReferenceEquals(result._dictionary[point], dictionary[point])),
            context + ": safe scene markers are reused without reconstruction");
        Assert(ReferenceEquals(RunTeamScenePostfix(result), result), context + ": already-safe view is returned unchanged");
        Assert(RunTeamScenePostfix(null) == null, context + ": absent native result remains absent");
    }

    private static void CheckFactoryTeamViewIsolation()
    {
        foreach (string invalid in new[] { "normal", "stale", "solo", "tdm", "gungame", "armedwrongmode", "bothflagswrongarmedmode", "labs", "labyrinth", "nullmap" })
        {
            SetSession("factory4_day", false, true);
            ArenaSession.ActiveMode = ArenaSession.ArmedMode = "tactical";
            PitFireTeamCompat.TeamArenaActive = true;
            switch (invalid)
            {
                case "normal": ArenaSession.ArmedMap = null; ArenaSession.IsActive = false; break;
                case "stale": ArenaSession.IsActive = false; break;
                case "solo": PitFireTeamCompat.TeamArenaActive = false; break;
                case "tdm": ArenaSession.ActiveMode = "teamdeathmatch"; break;
                case "gungame": ArenaSession.ActiveMode = "gungame"; break;
                case "armedwrongmode": ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMode = "endless"; break;
                case "bothflagswrongarmedmode": ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMode = "endless"; break;
                case "labs": ArenaSession.ArmedMap = "laboratory"; break;
                case "labyrinth": ArenaSession.ArmedMap = "labyrinth"; break;
                case "nullmap": ArenaSession.ArmedMap = null; break;
            }
            var bad = FactoryPoint(UnsafeFactoryId, category: ESpawnCategoryMask.Player);
            var collection = SceneView(bad, FactoryPoint(NeighborFactoryId));
            var before = Snapshot(collection);
            Assert(ReferenceEquals(RunTeamScenePostfix(collection), collection),
                "team scene view remains untouched: " + invalid);
            AssertUnchanged(collection.ToList(), before, "isolated scene points remain untouched: " + invalid);
        }

        foreach (var state in new[] { (Map: "factory4_day", Armed: false, Active: false),
            (Map: "factory4_night", Armed: false, Active: false), (Map: "laboratory", Armed: false, Active: true),
            (Map: "labyrinth", Armed: true, Active: false) })
        {
            SetSession(state.Map, state.Armed, state.Active);
            var badId = FactoryPoint(UnsafeFactoryId);
            var points = new List<ISpawnPoint> { badId };
            Assert(!ArenaBotSpawnFilter.IsExcludedPoint(badId) && ReferenceEquals(RunAiPostfix(points), points)
                && RunDispatchPrefix(points, Floor2), "Factory point ID remains unrestricted outside Factory Arena: " + state.Map);
            var zone = new BotZone("safe") { SpawnPoints = points.ToArray() };
            Assert(BurstSpawnPointSelector.CountUsable(zone, null) == 1,
                "Factory ID does not affect other-map or ordinary planner capacity: " + state.Map);
        }
    }

    private static void CheckFactoryPolicy()
    {
        foreach (string map in new[] { "factory4_day", "factory4_night", "FACTORY4_DAY", "FACTORY4_NIGHT" })
        {
            Assert(!ArenaBotSpawnPolicy.IsRestrictedMap(map) && ArenaBotSpawnPolicy.HasPointExclusions(map)
                && !ArenaBotSpawnPolicy.HasZoneExclusions(map), map + ": Factory point restriction does not opt server callers into Labs zone processing");
            Assert(ArenaBotSpawnPolicy.IsExcludedPoint(map, UnsafeFactoryId.ToUpperInvariant()), map + ": exact unsafe ID is case-insensitive");
            foreach (var unsafePoint in UnsafeFactoryPoints)
            {
                Assert(ArenaBotSpawnPolicy.IsExcludedPoint(map, unsafePoint.Id)
                    && ArenaBotSpawnPolicy.IsExcludedPoint(map, unsafePoint.Id.ToUpperInvariant()),
                    map + ": every reported unsafe marker is excluded in either casing: " + unsafePoint.Id);

                // The stuck-companion group is one place, not one marker: an
                // exclusion that covered only its nearest member would hand the
                // next round the marker 1.8 m away.
                foreach (string near in new[] { unsafePoint.Id + "0", unsafePoint.Id[..^1], " " + unsafePoint.Id })
                    Assert(!ArenaBotSpawnPolicy.IsExcludedPoint(map, near), map + ": near IDs are not excluded: " + near);
            }
            Assert(UnsafeFactoryPoints.Length == 6
                && UnsafeFactoryPoints.Select(point => point.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6,
                map + ": the excluded set is exactly the six distinct reported markers");
            foreach (string other in new[] { (string)null, "", NeighborFactoryId, UnsafeFactoryId + "0", UnsafeFactoryId[..^1] })
                Assert(!ArenaBotSpawnPolicy.IsExcludedPoint(map, other), map + ": null empty near and partial IDs are not excluded");
            string csv = " BotZoneGate1, BotZoneFloor2 ";
            Assert(ReferenceEquals(ArenaBotSpawnPolicy.FilterZones(map, csv), csv), map + ": shared zone CSV retains exact identity and formatting");
        }
        Assert(ArenaBotSpawnPolicy.HasZoneExclusions("LABORATORY") && !ArenaBotSpawnPolicy.HasPointExclusions("laboratory")
            && !ArenaBotSpawnPolicy.IsExcludedPoint("laboratory", UnsafeFactoryId), "Labs keeps zone-only exclusions");
        Assert(!ArenaBotSpawnPolicy.HasPointExclusions(null) && !ArenaBotSpawnPolicy.HasZoneExclusions(null)
            && !ArenaBotSpawnPolicy.IsExcludedPoint(null, UnsafeFactoryId), "null map has no exclusions");
    }

    private static void CheckFactoryWorldIsolation()
    {
        foreach (string invalid in new[] { "wrongmap", "noworld", "wrongworld", "wronggame", "nogame", "armedwrongmap" })
        {
            SetSession("factory4_day", false, true);
            ArenaSession.ActiveMode = ArenaSession.ArmedMode = "tactical";
            PitFireTeamCompat.TeamArenaActive = true;
            switch (invalid)
            {
                case "wrongmap": ArenaSession.World.LocationId = "factory4_night"; break;
                case "noworld": Comfort.Common.Singleton<GameWorld>.Instance = null; break;
                case "wrongworld": Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld { LocationId = "factory4_day" }; break;
                case "wronggame": Comfort.Common.Singleton<AbstractGame>.Instance = new AbstractGame(); break;
                case "nogame": Comfort.Common.Singleton<AbstractGame>.Instance = null; break;
                case "armedwrongmap": ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true; ArenaSession.World.LocationId = "laboratory"; break;
            }
            var bad = FactoryPoint(UnsafeFactoryId, category: ESpawnCategoryMask.Player);
            var points = new List<ISpawnPoint> { bad };
            var collection = SceneView(bad);
            Assert(!ArenaBotSpawnFilter.IsPointRestrictedRaid && !ArenaBotSpawnFilter.IsExcludedPoint(bad),
                "Factory stale or foreign world does not inherit point restrictions: " + invalid);
            Assert(ReferenceEquals(RunAiPostfix(points), points) && RunDispatchPrefix(points, Floor2),
                "Factory foreign-world bot boundaries pass through: " + invalid);
            Assert(ReferenceEquals(RunTeamScenePostfix(collection), collection),
                "Factory foreign-world team scene remains unchanged: " + invalid);
        }

        SetSession("factory4_day", true, true);
        ArenaSession.ArmedMode = "tactical";
        ArenaSession.ActiveMode = "gungame";
        PitFireTeamCompat.TeamArenaActive = true;
        var bothFlags = SceneView(FactoryPoint(UnsafeFactoryId), FactoryPoint(NeighborFactoryId));
        Assert(RunTeamScenePostfix(bothFlags).Count == 1,
            "new armed Tactical mode takes precedence over stale active mode");

        SetSession("factory4_day", true, false);
        ArenaSession.ArmedMode = "TACTICAL";
        ArenaSession.World.LocationId = "FACTORY4_DAY";
        PitFireTeamCompat.TeamArenaActive = true;
        var loading = SceneView(FactoryPoint(UnsafeFactoryId), FactoryPoint(NeighborFactoryId));
        Comfort.Common.Singleton<AbstractGame>.Instance = null;
        Assert(RunTeamScenePostfix(loading).Count == 1,
            "matching armed world filters before active game binding and tolerates ID casing");
    }

    private static void CheckInstalledFactoryFixture()
    {
        string database = Environment.GetEnvironmentVariable("SPT_DATABASE")
            ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        if (!Directory.Exists(database))
        {
            Console.WriteLine("FACTORY_INSTALLED_FIXTURE_SKIPPED (database unavailable; set SPT_DATABASE)");
            return;
        }
        foreach (string map in new[] { "factory4_day", "factory4_night" })
        {
            string path = Path.Combine(database, "locations", map, "base.json");
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var rawPoints = document.RootElement.GetProperty("SpawnPointParams").EnumerateArray().ToArray();
            foreach (var unsafePoint in UnsafeFactoryPoints)
            {
                var target = rawPoints.Single(point => point.GetProperty("Id").GetString() == unsafePoint.Id);
                var targetPosition = target.GetProperty("Position");
                Assert(target.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "Player" })
                    && string.IsNullOrEmpty(target.GetProperty("BotZoneName").GetString()),
                    map + ": installed unsafe ID is a Player-only marker without a bot-zone name: " + unsafePoint.Id);
                Assert(Math.Abs(targetPosition.GetProperty("x").GetSingle() - unsafePoint.X) < .0001f
                    && Math.Abs(targetPosition.GetProperty("y").GetSingle() - unsafePoint.Y) < .0001f
                    && Math.Abs(targetPosition.GetProperty("z").GetSingle() - unsafePoint.Z) < .0001f,
                    map + ": installed ID still sits at the reported coordinate: " + unsafePoint.Id);
            }
            var points = rawPoints.Select(point =>
            {
                var position = point.GetProperty("Position");
                var categories = point.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()).ToArray();
                return (ISpawnPoint)new TestSpawnPoint
                {
                    Id = point.GetProperty("Id").GetString(), BotZoneName = point.GetProperty("BotZoneName").GetString(),
                    Position = new Vector3(position.GetProperty("x").GetSingle(), position.GetProperty("y").GetSingle(), position.GetProperty("z").GetSingle()),
                    Categories = (categories.Contains("Player") ? ESpawnCategoryMask.Player : ESpawnCategoryMask.None)
                        | (categories.Contains("Bot") ? ESpawnCategoryMask.Bot : ESpawnCategoryMask.None),
                    CorePointId = 17, NextBornTime = 23.5f
                };
            }).ToArray();
            SetSession(map, true, false);
            ArenaSession.ArmedMode = "tactical";
            PitFireTeamCompat.TeamArenaActive = true;
            var original = SceneView(points);
            var originalDictionary = original._dictionary;
            var before = Snapshot(points);
            var view = RunTeamScenePostfix(original);
            var unsafeIds = new HashSet<string>(
                UnsafeFactoryPoints.Select(point => point.Id), StringComparer.OrdinalIgnoreCase);
            Assert(view.Count == rawPoints.Length - unsafeIds.Count
                && view.All(point => !unsafeIds.Contains(point.Id))
                && points.Where(point => !unsafeIds.Contains(point.Id)).All(view.Contains),
                map + ": installed scene view removes exactly the reported points and retains every other physical object");
            Assert(ReferenceEquals(original._dictionary, originalDictionary) && original.Count == rawPoints.Length
                && view.All(point => ReferenceEquals(view._dictionary[point], originalDictionary[point])),
                map + ": installed original collection and all retained scene markers preserve identity");
            AssertUnchanged(original.ToList(), before, map + ": installed fixture point state remains intact");
            Console.WriteLine($"FACTORY_INSTALLED_FIXTURE_PASSED ({map}: {rawPoints.Length} original, {view.Count} retained)");
        }
    }
}
