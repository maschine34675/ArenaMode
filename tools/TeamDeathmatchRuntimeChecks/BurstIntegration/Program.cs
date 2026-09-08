using ArenaMode.Bots;
using ArenaMode.Modes;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }

    private sealed class Attempt
    {
        internal readonly TeamDeathmatchPopulation Population = new(2, true);
        internal readonly TeamDeathmatchPopulation.Ticket Ticket;
        internal readonly BotCreationData Data = new();
        internal int Begins, Observers, Finishes;
        internal bool Handed, Admitted, BeginAfterPreparation, BeginAfterPlacement;
        internal bool ThrowBegin, RefuseBegin, DriftDuringBegin, FullAtSubmit;
        internal Attempt()
        { Harness.Reset(); Population.TryReserve(true, 0, 0f, out Ticket); }
        internal bool Current() => Population.IsCurrent(Ticket, Time.realtimeSinceStartup);
        internal bool Begin()
        {
            Begins++; Harness.Events.Add("begin");
            BeginAfterPreparation = BotCreationData.Pending.Task.IsCompleted;
            BeginAfterPlacement = Harness.RealPlans == 1;
            if (ThrowBegin) throw new InvalidOperationException("submission callback");
            if (RefuseBegin) return false;
            if (DriftDuringBegin) Harness.LeaseCurrent = false;
            return Population.TryBeginDispatch(Ticket, FullAtSubmit ? 1 : 0, Time.realtimeSinceStartup);
        }
        internal void Observe(BotOwner bot)
        {
            Observers++; Harness.Events.Add("observer");
            Admitted = Population.CanAdmit(Ticket, Time.realtimeSinceStartup);
            if (Admitted) Population.Settle(Ticket);
        }
        internal void Finish(bool handed)
        { Finishes++; Handed = handed; Harness.Events.Add("finish"); Population.DispatchFinished(Ticket, handed); }
        internal int Start(bool noBegin = false)
            => BurstSpawnService.SpawnFactionBot(WildSpawnType.pmcUSEC, BotDifficulty.normal, Observe, Current,
                noBegin ? null : Begin, Finish, () => Population.Settle(Ticket), Ticket.Id);
        internal void Complete(float at = 154f)
        { Time.realtimeSinceStartup = at; BotCreationData.Pending.SetResult(Data); }
    }

    private static void Main()
    {
        var cold = new Attempt();
        Check(cold.Start() == 1 && cold.Begins == 0 && Harness.NativeCalls == 0 && Harness.RealPlans == 0,
            "submission and real placement never run while profile preparation is unresolved");
        cold.Complete();
        Check(cold.Begins == 1 && cold.BeginAfterPreparation && cold.BeginAfterPlacement,
            "one-shot transition runs after actual await and fresh area placement");
        Check(cold.Admitted && cold.Observers == 1 && cold.Finishes == 1 && cold.Handed && Harness.NativeOwned,
            "synchronous native observer receives valid submission ownership before terminal notification");
        Check(Harness.Events.IndexOf("begin") < Harness.Events.IndexOf("native")
            && Harness.Events.IndexOf("observer") < Harness.Events.IndexOf("finish"),
            "production transition precedes native call and synchronous completion cannot resurrect debt");
        Check(cold.Population.OutstandingCount == 0 && cold.Data.Stops == 0 && Harness.LastLease.Disposed,
            "successful native dispatch settles only its own request and never StopSpawn cancels it");
        Check(Harness.Log.Any(line => line.Contains("BotCreationData.Create=154.000s"))
            && Harness.Log.Any(line => line.Contains("fresh plan=")),
            "production stage diagnostics distinguish native profile pools from placement");

        var warm = new Attempt();
        BotCreationData.Pending.SetResult(warm.Data);
        warm.Start();
        Check(warm.Admitted && warm.Finishes == 1 && warm.Begins == 1,
            "already-completed preparation preserves fully synchronous dispatch ordering");

        foreach (int failure in new[] { 0, 1, 2, 3 })
        {
            var denied = new Attempt { ThrowBegin = failure == 0, RefuseBegin = failure == 1,
                FullAtSubmit = failure == 2, DriftDuringBegin = failure == 3 };
            denied.Start(); denied.Complete();
            Check(Harness.NativeCalls == 0 && denied.Observers == 0 && denied.Finishes == 1 && !denied.Handed
                && denied.Data.Stops > 0 && Harness.LastLease.Disposed,
                "throwing refused full or stale submission cannot cross native dispatch");
        }
        var absent = new Attempt();
        Check(absent.Start(noBegin: true) == 0 && !Harness.Events.Contains("create"),
            "public faction preflight refuses missing submission ownership");
        var directMissing = new Attempt();
        BurstSpawnService.Direct(directMissing.Current, null, directMissing.Finish);
        directMissing.Complete();
        Check(Harness.NativeCalls == 0 && directMissing.Data.Stops > 0,
            "actual async dispatch also fails closed on a missing transition callback");

        foreach (int cancellation in new[] { 0, 1, 2 })
        {
            var cancelled = new Attempt(); cancelled.Start();
            if (cancellation == 0) cancelled.Population.Stop();
            if (cancellation == 1) Harness.LeaseCurrent = false;
            cancelled.Complete(cancellation == 2 ? 300f : 154f);
            Check(cancelled.Begins == 0 && Harness.NativeCalls == 0 && cancelled.Data.Stops > 0,
                "stopped stale or watchdog-expired preparation is discarded before placement and submission");
        }
        var blocked = new Attempt(); blocked.Start(); Harness.PlacementAvailable = false; blocked.Complete();
        Check(blocked.Begins == 0 && Harness.NativeCalls == 0 && blocked.Data.Stops > 0,
            "occupied or unavailable arrival marker never consumes native admission");

        var prefix = new Attempt(); Harness.RejectNative = true; prefix.Start(); prefix.Complete();
        Check(!prefix.Handed && prefix.Data.Stops > 0 && prefix.Population.OutstandingCount == 0,
            "proven prefix rejection cancels only undispatched creation data");
        var uncertain = new Attempt(); Harness.ThrowNative = true; uncertain.Start(); uncertain.Complete();
        Check(uncertain.Handed && uncertain.Data.Stops == 0 && uncertain.Population.OutstandingCount == 1,
            "throw after native handoff retains uncertain debt without StopSpawn cancellation");

        var delayed = new Attempt(); Harness.DelayArrival = true; delayed.Start(); delayed.Complete();
        Time.realtimeSinceStartup = 199f;
        Harness.Game.BotsController.BotSpawner.Arrival(Harness.Body);
        Check(delayed.Observers == 0 && Harness.Body.Retired && delayed.Data.Stops == 0,
            "expired native callback is individually retired without cancelling dispatched work");
        var duplicate = new Attempt(); duplicate.Start(); duplicate.Complete();
        Harness.Game.BotsController.BotSpawner.Arrival(Harness.Body);
        Check(duplicate.Observers == 1 && !Harness.Body.Retired,
            "duplicate callback for admitted exact body cannot kill the valid actor");
        Console.WriteLine($"PASS: {_checks} extracted production burst preparation integration checks.");
        Console.WriteLine("ARENA_TDM_BURST_PREPARATION_CHECKS_PASSED");
    }
}
