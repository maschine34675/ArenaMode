using ArenaMode.Bots;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }
    private static void Reset()
    {
        ZombieSpawnService.Reset(); BurstSpawnService.Requests.Clear(); BurstSpawnService.Inline = null;
        BurstSpawnService.Preflight = true; Time.time = 0; ZombieMeleeDiagnostics.Arrivals = 0;
    }
    private static BurstSpawnService.Request Request()
    {
        Check(ZombieSpawnService.TryRequest(WildSpawnType.infectedAssault, BotDifficulty.normal), "request accepted");
        return BurstSpawnService.Requests.Last();
    }
    public static void Main()
    {
        Reset(); var slow = Request(); Time.time = 25;
        Check(ZombieSpawnService.Pending == 1 && slow.Current(), "25-second preparation retains pending ownership");
        Time.time = 250; Check(slow.Begin(), "slow preparation still enters native phase within separate preparation bound");
        slow.Finish(true); Time.time = 305;
        Check(ZombieSpawnService.Pending == 1 && slow.Current(), "native clock starts at handoff, not initial request");
        slow.Arrival(new());
        Check(ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConfirmedArrivals == 1, "late valid arrival settles its original request");

        Reset(); BurstSpawnService.Inline = r => { Check(r.Current() && r.Begin(), "inline callbacks see pre-registered reservation"); r.Arrival(new()); r.Finish(true); };
        Request(); Check(ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConfirmedArrivals == 1, "synchronous arrival cannot leave ghost debt after return");

        Reset(); var first = Request(); var second = Request();
        second.Begin(); second.Arrival(new());
        Check(ZombieSpawnService.Pending == 1 && first.Current() && !second.Current(), "out-of-order arrival settles only matching ticket");
        first.Finish(false);
        Check(ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConsumeFailedDispatches() == 1, "known asynchronous predispatch failure restores exactly one slot");
        first.Finish(false); Check(ZombieSpawnService.ConsumeFailedDispatches() == 0, "failure notification is idempotent");

        Reset(); BurstSpawnService.Preflight = false;
        Check(!ZombieSpawnService.TryRequest(WildSpawnType.infectedCivil, BotDifficulty.normal)
            && ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConsumeFailedDispatches() == 0,
            "synchronous preflight failure leaves rollback solely to caller");

        Reset(); BurstSpawnService.Inline = r => r.Finish(false);
        Request(); Check(ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConsumeFailedDispatches() == 1, "inline async failure remains acknowledged once");

        Reset(); var uncertain = Request(); uncertain.Begin(); uncertain.Finish(true); Time.time = 59;
        Check(!ZombieSpawnService.TryGetPendingFailure(out _) && ZombieSpawnService.Pending == 1, "uncertain native request cannot age into free capacity");
        Time.time = 60;
        Check(ZombieSpawnService.TryGetPendingFailure(out var reason) && reason.Contains("native activation") && ZombieSpawnService.Pending == 1,
            "native timeout signals no-contest while retaining debt");
        Check(!uncertain.Current() && !ZombieSpawnService.TryRequest(WildSpawnType.infectedPmc, BotDifficulty.normal), "terminal uncertainty rejects continuations and replacement reservations");
        uncertain.Finish(false); Check(ZombieSpawnService.Pending == 1 && ZombieSpawnService.ConsumeFailedDispatches() == 0, "late failure cannot reopen timed-out work");

        Reset(); var neverPrepared = Request(); Time.time = 299;
        Check(neverPrepared.Current(), "preparation has independent longer deadline");
        Time.time = 300;
        Check(!neverPrepared.Current() && !neverPrepared.Begin() && ZombieSpawnService.TryGetPendingFailure(out reason) && reason.Contains("preparation"),
            "expired preparation cannot enter native dispatch");

        Reset(); var stale = Request(); ZombieSpawnService.Reset(); var fresh = Request();
        stale.Finish(false); stale.Arrival(new()); stale.Rejected();
        Check(!stale.Current() && fresh.Current() && ZombieSpawnService.Pending == 1 && ZombieSpawnService.ConfirmedArrivals == 0
            && ZombieSpawnService.ConsumeFailedDispatches() == 0 && !ZombieSpawnService.TryGetPendingFailure(out _),
            "old generation callbacks cannot settle or poison next raid");

        Reset(); var rejected = Request(); rejected.Begin(); rejected.Rejected();
        Check(ZombieSpawnService.TryGetPendingFailure(out _) && ZombieSpawnService.Pending == 1, "unverified native arrival fails closed without releasing slot");
        Reset(); var invalid = Request(); invalid.Begin(); invalid.Arrival(null);
        Check(ZombieSpawnService.TryGetPendingFailure(out _) && ZombieSpawnService.Pending == 1, "null arrival cannot prove live or dead settlement");
        Reset(); var dead = Request(); dead.Begin(); var body = new BotOwner(); body.HealthController.IsAlive = false; dead.Arrival(body);
        Check(ZombieSpawnService.Pending == 0 && ZombieSpawnService.ConfirmedArrivals == 0 && ZombieSpawnService.ConsumeFailedDispatches() == 1,
            "confirmed dead arrival retries instead of counting a missing infected as cleared");

        Console.WriteLine($"ZOMBIE_PENDING_SERVICE_CHECKS_PASSED {_checks} assertions");
    }
}
