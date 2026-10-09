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
        CheckInfectedTerminalAdmission();
        Console.WriteLine($"PASS: {_checks} extracted production burst preparation integration checks.");
        Console.WriteLine("ARENA_TDM_BURST_PREPARATION_CHECKS_PASSED");
    }

    private static void CheckInfectedTerminalAdmission()
    {
        foreach (bool afterDispatch in new[] { false, true })
        foreach (bool matchReplaced in new[] { false, true })
        {
            Harness.Reset(); Harness.DelayArrival = true;
            var mode = new ZombieMode();
            ArenaMatchService.Current = new ArenaMatch { Mode = mode };
            var data = new BotCreationData();
            data.Profiles[0].Info.Settings.Role = WildSpawnType.infectedAssault;
            Harness.Body.Role = WildSpawnType.infectedAssault;
            int arrivals = 0;
            Check(BurstSpawnService.SpawnInfected(WildSpawnType.infectedAssault, BotDifficulty.normal,
                _ => arrivals++), "live Zombie mode accepts infected preparation");
            if (afterDispatch)
            {
                BotCreationData.Pending.SetResult(data);
                Check(Harness.NativeCalls == 1 && BurstSpawnService.PendingScavReservations == 1,
                    "dispatched infected owns one outstanding reservation before delayed arrival");
            }
            if (matchReplaced) ArenaMatchService.Current = new ArenaMatch { Mode = new ZombieMode() };
            else mode.IsSpawnAdmissionOpen = false;
            if (afterDispatch)
            {
                Harness.Game.BotsController.BotSpawner.Arrival(Harness.Body);
                Check(arrivals == 0 && Harness.Body.Retired && Harness.AcceptedBodies == 0
                    && Harness.ReservationReleases == 1 && BurstSpawnService.PendingScavReservations == 0
                    && data.Stops == 0,
                    "terminal infected callback is retired before observer and releases its reservation");
            }
            else
            {
                BotCreationData.Pending.SetResult(data);
                Check(Harness.NativeCalls == 0 && arrivals == 0 && data.Stops == 1
                    && BurstSpawnService.PendingScavReservations == 0,
                    "terminal or replaced Zombie match discards awaited infected before native dispatch");
            }
        }

        Harness.Reset();
        var closed = new ZombieMode { IsSpawnAdmissionOpen = false };
        ArenaMatchService.Current = new ArenaMatch { Mode = closed };
        Check(!BurstSpawnService.SpawnInfected(WildSpawnType.infectedAssault, BotDifficulty.normal, _ => { })
            && !Harness.Events.Contains("create"), "terminal Zombie mode refuses fresh infected requests");

        // A surviving PFT squad keeps mode admission open after local death.
        // Its real policy is verified in ZombieTeamChecks; this boundary must
        // accept its delayed native body while that admission remains current.
        Harness.Reset(); Harness.DelayArrival = true;
        ArenaMatchService.Current = new ArenaMatch { Mode = new ZombieMode() };
        var activeData = new BotCreationData();
        activeData.Profiles[0].Info.Settings.Role = WildSpawnType.infectedCivil;
        Harness.Body.Role = WildSpawnType.infectedCivil;
        int accepted = 0;
        Check(BurstSpawnService.SpawnInfected(WildSpawnType.infectedCivil, BotDifficulty.hard,
            _ => accepted++), "live team wave retains infected admission");
        BotCreationData.Pending.SetResult(activeData);
        Harness.Game.BotsController.BotSpawner.Arrival(Harness.Body);
        Check(accepted == 1 && Harness.AcceptedBodies == 1 && !Harness.Body.Retired
            && BurstSpawnService.PendingScavReservations == 0 && activeData.Stops == 0,
            "current open Zombie wave accepts delayed infected and settles its reservation");
    }
}
