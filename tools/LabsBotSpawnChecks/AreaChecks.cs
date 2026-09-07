using ArenaMode.Areas;
using ArenaMode.Bots;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;

internal static partial class Program
{
    private static void CheckBoundedAreaDispatch()
    {
        foreach (bool loading in new[] { true, false })
        {
            SetSession("factory4_day", loading, !loading);
            ArenaSession.ActiveMode = ArenaSession.ArmedMode = "teamdeathmatch";
            // Loading has no active CurrentArea yet. The separate readiness
            // verdict must still prevent native dispatch before counters.
            ArenaAreaRuntime.CurrentArea = loading ? null : ArenaAreaCatalog.FactorySouth;
            ArenaAreaRuntime.LoadingBlocked = true;
            var points = new List<ISpawnPoint> { PointAt("BotZone", 0) };
            var before = Snapshot(points);
            var spawner = new BotSpawner();
            var data = new BotCreationData();
            using (var lease = TeamDeathmatchSpawnDispatch.Enter(spawner, data))
            {
                bool permitted = RunDispatchPrefix(spawner, data, points, new BotZone("BotZone"));
                if (permitted) spawner.SpawnBotsInZoneOnPositions(points, new BotZone("BotZone"), data);
                Assert(!permitted && lease.WasRejected && spawner.DispatchCalls == 0 && spawner.InSpawnProcess == 0,
                    "area loading/preparing veto proves no native handoff, including before CurrentArea exists");
                Assert(!ArenaBotSpawnFilter.CanDispatch(new BotZone("BotZone"), points),
                    "direct dispatch predicate also honors area preparation");
            }
            AssertUnchanged(points, before, "pending area does not rewrite shared markers or batch contents");
            Assert(data.Positions.Count == 0 && !data.Stopped, "pending refusal leaves caller-owned creation data unchanged");
        }

        SetSession("factory4_day", false, true);
        ArenaSession.ActiveMode = "teamdeathmatch";
        ArenaAreaRuntime.CurrentArea = ArenaAreaCatalog.FactorySouth;
        var inside = PointAt("BotZone", 0);
        var outside = PointAt("BotZone", 90);
        var disconnected = PointAt("BotZone", 5);
        // Runtime verdicts are supplied independently, including a geometrically
        // contained point that failed connectivity. The production filter may
        // neither bypass nor reinterpret those verdicts.
        ArenaAreaRuntime.SpawnAccepted = point => ReferenceEquals(point, inside);
        Assert(!ArenaBotSpawnFilter.IsExcludedPoint(inside)
            && ArenaBotSpawnFilter.IsExcludedPoint(outside)
            && ArenaBotSpawnFilter.IsExcludedPoint(disconnected),
            "ready-area filter honors both outside and disconnected-point rejections");
        var valid = new List<ISpawnPoint> { inside };
        Assert(ReferenceEquals(valid, RunAiPostfix(valid)), "verified AI choices retain their original list");
        var mixed = new List<ISpawnPoint> { inside, outside, disconnected };
        var mixedBefore = Snapshot(mixed);
        Assert(RunAiPostfix(mixed) == null, "one invalid point rejects the whole native AI choice");
        AssertUnchanged(mixed, mixedBefore, "area AI refusal preserves caller list and point state");

        foreach (var rejected in new List<ISpawnPoint>[] { null, new(), mixed, new() { null } })
        {
            var spawner = new BotSpawner();
            var data = new BotCreationData();
            using var lease = TeamDeathmatchSpawnDispatch.Enter(spawner, data);
            Assert(!RunDispatchPrefix(spawner, data, rejected, new BotZone("BotZone")) && lease.WasRejected,
                "bounded dispatch rejects null, empty, mixed or invalid-marker batches before native selection fallback");
            Assert(spawner.InSpawnProcess == 0 && data.Positions.Count == 0 && !data.Stopped,
                "all rejected bounded batches preserve native counters and caller-owned data");
        }
        var acceptedSpawner = new BotSpawner();
        var acceptedData = new BotCreationData();
        using (var lease = TeamDeathmatchSpawnDispatch.Enter(acceptedSpawner, acceptedData))
        {
            Assert(RunDispatchPrefix(acceptedSpawner, acceptedData, valid, new BotZone("BotZone")),
                "owned ready-area batch with only approved points reaches native dispatch");
            acceptedSpawner.SpawnBotsInZoneOnPositions(valid, new BotZone("BotZone"), acceptedData);
            Assert(!lease.WasRejected && acceptedSpawner.InSpawnProcess == 1 && acceptedData.Positions.Count == 1,
                "accepted bounded dispatch still charges exactly its original one-profile point count");
        }

        SetSession("factory4_day", false, false);
        var ordinary = new List<ISpawnPoint> { outside };
        ArenaAreaRuntime.SpawnAccepted = _ => false; // ignored without an active area verdict
        Assert(ReferenceEquals(ordinary, RunAiPostfix(ordinary))
            && RunDispatchPrefix(ordinary, new BotZone("BotZone"))
            && ArenaBotSpawnFilter.CanDispatch(new BotZone("BotZone"), null),
            "ordinary/full-map flow stays fail-open even at coordinates outside the prototype area");
        SetSession("laboratory", false, true);
    }
}
