using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using SPT.Reflection.Patching;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static readonly BotZone Gate1 = new("BotZoneGate1");
    private static readonly BotZone Gate2 = new("BotZoneGate2");
    private static readonly BotZone Floor2 = new("BotZoneFloor2");
    private static readonly BotZone Basement = new("BotZoneBasement");
    private static readonly BotZone Floor1 = new("BotZoneFloor1");
    private static readonly BotZone[] SceneZones = { Gate1, Floor2, Gate2, Basement, Floor1 };

    public static int MainEntry()
    {
        try
        {
            CheckHookTargets();
            CheckOrdinaryAndOtherMaps();
            CheckArenaState(armed: true, active: false);
            CheckArenaState(armed: false, active: true);
            CheckNullInputs();
            CheckSharedPlayerPoints();
            CheckDispatchAccounting();
            CheckPolicy();
            Console.WriteLine($"LABS_BOT_SPAWN_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LABS_BOT_SPAWN_CHECKS_FAILED after {_assertions} assertions: {exception}");
            return 1;
        }
    }

    private static int Main(string[] args) => MainEntry();

    private static void CheckHookTargets()
    {
        ModulePatch.EnabledTargets.Clear();
        ArenaBotSpawnPatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 3, "exactly three bot hooks registered");
        AssertTarget(0, typeof(BotSpawner), nameof(BotSpawner.SpawnZones),
            typeof(IEnumerable<BotZone>), typeof(bool));
        AssertTarget(1, typeof(SpawnSystem), nameof(SpawnSystem.SelectAISpawnPoints),
            typeof(List<ISpawnPoint>), typeof(BotCreationData), typeof(BotZone),
            typeof(int), typeof(float), typeof(Vector3?), typeof(ActionIfNotEnoughPoints));
        AssertTarget(2, typeof(BotSpawner), nameof(BotSpawner.SpawnBotsInZoneOnPositions),
            typeof(void), typeof(List<ISpawnPoint>), typeof(BotZone),
            typeof(BotCreationData), typeof(Action<BotOwner>));
    }

    private static void AssertTarget(int index, Type owner, string name, Type result, params Type[] arguments)
    {
        var method = (MethodInfo)ModulePatch.EnabledTargets[index];
        Assert(method.DeclaringType == owner && method.Name == name && method.ReturnType == result
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(arguments),
            $"hook {index} resolves exact native signature: {owner.Name}.{name}");
    }

    private static void CheckPolicy()
    {
        Assert(ArenaBotSpawnPolicy.IsRestrictedMap("laboratory")
            && ArenaBotSpawnPolicy.IsRestrictedMap("LABORATORY"), "Labs map recognized case-insensitively");
        Assert(!ArenaBotSpawnPolicy.IsRestrictedMap(null)
            && !ArenaBotSpawnPolicy.IsRestrictedMap("labyrinth"), "other maps not restricted");
        Assert(ArenaBotSpawnPolicy.IsExcludedZone("laboratory", "BotZoneGate1")
            && ArenaBotSpawnPolicy.IsExcludedZone("laboratory", "BotZoneGate2"), "both Labs gate zones excluded");
        Assert(ArenaBotSpawnPolicy.IsExcludedZone("LABORATORY", "botzonegate1"), "zone matching case-insensitive");
        Assert(!ArenaBotSpawnPolicy.IsExcludedZone("factory4_day", "BotZoneGate1")
            && !ArenaBotSpawnPolicy.IsExcludedZone("laboratory", "BotZoneGate10")
            && !ArenaBotSpawnPolicy.IsExcludedZone("laboratory", null), "policy exact, map-scoped, null-safe");
        Assert(ArenaBotSpawnPolicy.FilterZones("laboratory",
            "BotZoneGate1, BotZoneFloor2, BotZoneGate2, BotZoneBasement, BotZoneFloor1")
            == "BotZoneFloor2,BotZoneBasement,BotZoneFloor1", "persisted zone CSV removes gates, retains all three safe zones");
        var otherMapCsv = " BotZoneGate1, BotZoneFloor2 ";
        Assert(ReferenceEquals(ArenaBotSpawnPolicy.FilterZones("factory4_day", otherMapCsv), otherMapCsv),
            "unrelated map CSV passes through unchanged");
        Assert(ArenaBotSpawnPolicy.FilterZones("laboratory", "BotZoneGate1,BotZoneGate2") == string.Empty,
            "gate-only CSV has no allowed named zones");
    }

    private static void CheckOrdinaryAndOtherMaps()
    {
        foreach (var state in new[]
        {
            (Map: (string)null, Armed: false, Active: false, Label: "ordinary Labs without Arena state"),
            (Map: "laboratory", Armed: false, Active: false, Label: "stale ArmedMap with flags off"),
            (Map: "factory4_day", Armed: true, Active: false, Label: "armed Arena Factory"),
            (Map: "labyrinth", Armed: false, Active: true, Label: "active Arena Labyrinth")
        })
        {
            SetSession(state.Map, state.Armed, state.Active);
            var zones = SceneZones.Where(zone => true);
            var points = Points();
            var zoneResult = RunZonePostfix(zones);
            Assert(ReferenceEquals(zones, zoneResult), state.Label + ": zone enumerable identity retained");
            Assert(ReferenceEquals(points, RunAiPostfix(points)), state.Label + ": AI result list identity retained");
            var prefix = RunAiPrefix(Gate1, points);
            Assert(prefix.RunOriginal && ReferenceEquals(prefix.Result, points),
                state.Label + ": native gate selection untouched");
            Assert(RunDispatchPrefix(points, Gate2), state.Label + ": gate dispatch untouched");
        }
    }

    private static void CheckArenaState(bool armed, bool active)
    {
        SetSession("laboratory", armed, active);
        var context = active ? "active Labs" : "armed/loading Labs";
        var filtered = RunZonePostfix(SceneZones).ToArray();
        Assert(filtered.SequenceEqual(new[] { Floor2, Basement, Floor1 }), context + ": three safe zones retained in order");
        Assert(SceneZones.SequenceEqual(new[] { Gate1, Floor2, Gate2, Basement, Floor1 }), context + ": original scene zone array unchanged");

        foreach (var gate in new[] { Gate1, Gate2 })
        {
            var prefix = RunAiPrefix(gate, Points());
            Assert(!prefix.RunOriginal && prefix.Result == null,
                context + $": direct {gate.NameZone} AI selection stopped before native method");
        }

        var safe = new List<ISpawnPoint> { Point("BotZoneFloor2"), Point("BotZoneFloor1") };
        var safePrefix = RunAiPrefix(Floor2, safe);
        Assert(safePrefix.RunOriginal && ReferenceEquals(safePrefix.Result, safe), context + ": safe native selector allowed");
        Assert(ReferenceEquals(RunAiPostfix(safe), safe), context + ": safe AI batch retains identity");
        var mixed = Points();
        var before = Snapshot(mixed);
        Assert(RunAiPostfix(mixed) == null, context + ": mixed AI batch rejected in full");
        AssertUnchanged(mixed, before, context + ": rejected AI batch leaves count, order and point states unchanged");
        Assert(RunDispatchPrefix(safe, Floor2), context + ": safe dispatch allowed");
        Assert(!RunDispatchPrefix(safe, Gate1), context + ": gate zone rejects otherwise safe dispatch");
        Assert(!RunDispatchPrefix(mixed, Floor2), context + ": gate point rejects otherwise safe zone dispatch");
    }

    private static void CheckNullInputs()
    {
        SetSession("laboratory", armed: true, active: false);
        Assert(RunZonePostfix(null) == null && RunAiPostfix(null) == null, "null native results preserved");
        Assert(RunAiPrefix(null, null).RunOriginal, "unknown zone left to native validation");
        Assert(RunDispatchPrefix(null, Floor2), "safe zone with null points left to native validation");
        Assert(!RunDispatchPrefix(null, Gate2), "known gate zone denied even without point list");
    }

    private static void CheckSharedPlayerPoints()
    {
        SetSession("laboratory", armed: true, active: false);
        var playerGatePoint = Point("BotZoneGate2", ESpawnCategoryMask.Player);
        var playerSafePoint = Point("BotZoneFloor2", ESpawnCategoryMask.Player | ESpawnCategoryMask.Bot);
        var playerCollection = new List<ISpawnPoint> { playerGatePoint, playerSafePoint };
        var before = Snapshot(playerCollection);
        var botCandidateView = new List<ISpawnPoint>(playerCollection);
        Assert(RunAiPostfix(botCandidateView) == null,
            "bot boundary excludes gate point even with Player-only category");
        Assert(!RunDispatchPrefix(botCandidateView, Floor2), "direct bot dispatch cannot use Player-tagged gate point");
        AssertUnchanged(playerCollection, before, "shared player collection and point objects untouched by bot filtering");
        AssertUnchanged(botCandidateView, before, "caller bot candidate list not shortened or mutated");
        Assert(ReferenceEquals(playerCollection[0], playerGatePoint)
            && !playerGatePoint.SpawnBlocked && playerGatePoint.NextBornTime == 23.5f,
            "player gate marker remains available with original cooldown");
    }

    private static void CheckDispatchAccounting()
    {
        SetSession("laboratory", armed: false, active: true);
        ArenaBotSpawnPatches.ResetRaidState();
        var spawner = new BotSpawner();
        var data = new BotCreationData();
        var mixed = Points();
        var before = Snapshot(mixed);
        var originalProfiles = data.ProfileCount;
        if (RunDispatchPrefix(mixed, Floor2))
            spawner.SpawnBotsInZoneOnPositions(mixed, Floor2, data);
        Assert(spawner.DispatchCalls == 0 && spawner.InSpawnProcess == 0,
            "rejected direct batch never reaches accounting boundary");
        Assert(data.ProfileCount == originalProfiles && data.Positions.Count == 0 && !data.Stopped,
            "rejected batch neither edits nor stops shared creation/profile data");
        AssertUnchanged(mixed, before, "rejected dispatch leaves shared points intact");
        var safe = new List<ISpawnPoint> { Point("BotZoneFloor2") };
        if (RunDispatchPrefix(safe, Floor2))
            spawner.SpawnBotsInZoneOnPositions(safe, Floor2, data);
        Assert(spawner.DispatchCalls == 1 && spawner.InSpawnProcess == 1 && data.Positions.Count == 1,
            "subsequent safe batch can dispatch using same creation data");

        var native = new SpawnSystem { Selection = mixed };
        var gate = RunAiPrefix(Gate1, null);
        var selection = gate.RunOriginal
            ? native.SelectAISpawnPoints(data, Gate1, originalProfiles, 10f) : gate.Result;
        selection = RunAiPostfix(selection);
        Assert(native.SelectionCalls == 0 && selection == null,
            "gate rejection bypasses native AI search before interface cooldown stage");
        AssertUnchanged(mixed, before, "blocked AI selection never changes shared point cooldowns");
    }

    private static MethodInfo PatchMethod(string patch, string method)
        => typeof(ArenaBotSpawnPatches).GetNestedType(patch, BindingFlags.NonPublic)
            ?.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Production patch missing: {patch}.{method}");

    private static IEnumerable<BotZone> RunZonePostfix(IEnumerable<BotZone> result)
    {
        var arguments = new object[] { result };
        PatchMethod("SpawnZonesPatch", "Postfix").Invoke(null, arguments);
        return (IEnumerable<BotZone>)arguments[0];
    }

    private static (bool RunOriginal, List<ISpawnPoint> Result) RunAiPrefix(BotZone zone, List<ISpawnPoint> result)
    {
        var arguments = new object[] { zone, result };
        var runOriginal = (bool)PatchMethod("SelectAiPointsPatch", "Prefix").Invoke(null, arguments);
        return (runOriginal, (List<ISpawnPoint>)arguments[1]);
    }

    private static List<ISpawnPoint> RunAiPostfix(List<ISpawnPoint> result)
    {
        var arguments = new object[] { result };
        PatchMethod("SelectAiPointsPatch", "Postfix").Invoke(null, arguments);
        return (List<ISpawnPoint>)arguments[0];
    }

    private static bool RunDispatchPrefix(List<ISpawnPoint> points, BotZone zone)
        => (bool)PatchMethod("DispatchPatch", "Prefix").Invoke(null, new object[] { points, zone });

    private static void SetSession(string map, bool armed, bool active)
    {
        ArenaSession.ArmedMap = map;
        ArenaSession.ArmedForNextRaid = armed;
        ArenaSession.IsActive = active;
    }

    private static TestSpawnPoint Point(string zone, ESpawnCategoryMask category = ESpawnCategoryMask.Bot)
        => new() { BotZoneName = zone, Categories = category, NextBornTime = 23.5f, CorePointId = 17 };

    private static List<ISpawnPoint> Points()
        => new() { Point("BotZoneFloor2"), Point("BotZoneGate1"), Point("BotZoneGate2") };

    private sealed record PointSnapshot(ISpawnPoint Instance, string Zone, ESpawnCategoryMask Categories,
        bool Blocked, float NextBorn, int CoreId, Vector3 Position);

    private static PointSnapshot[] Snapshot(IEnumerable<ISpawnPoint> points)
        => points.Select(point => new PointSnapshot(point, point.BotZoneName, point.Categories,
            point.SpawnBlocked, point.NextBornTime, point.CorePointId, point.Position)).ToArray();

    private static void AssertUnchanged(List<ISpawnPoint> points, PointSnapshot[] before, string message)
        => Assert(points.Count == before.Length && points.Zip(before).All(pair =>
            ReferenceEquals(pair.First, pair.Second.Instance)
            && pair.First.BotZoneName == pair.Second.Zone && pair.First.Categories == pair.Second.Categories
            && pair.First.SpawnBlocked == pair.Second.Blocked && pair.First.NextBornTime == pair.Second.NextBorn
            && pair.First.CorePointId == pair.Second.CoreId && pair.First.Position.Equals(pair.Second.Position)), message);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _assertions++;
    }
}
