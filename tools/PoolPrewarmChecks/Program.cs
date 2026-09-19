using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ArenaMode.Performance;

internal static class Program
{
    private static int checks;

    private static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new Exception("FAIL: " + label);
    }

    private static string Meta(string key)
    {
        var attr = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key);

        if (attr == null || string.IsNullOrEmpty(attr.Value))
            throw new Exception($"Missing assembly metadata: {key}");

        return attr.Value;
    }

    private static void Main()
    {
        SourceGuard();
        ClampDecisions();
        LedgerLogging();

        Console.WriteLine($"ARENA_POOL_PREWARM_BASELINE_PASSED checks={checks}");
    }

    /// <summary>
    /// The feature must never force a blocking collection: not GC.Collect,
    /// not GC.GetTotalMemory(true). This clamp only ever bounds pool SIZES,
    /// not the heap directly, so it has no legitimate reason to touch either
    /// — the guard is inherited from the GC-threshold feature's own habit
    /// check and costs nothing, but it does catch an accidental copy-paste.
    ///
    /// PoolPrewarmMonitor.cs is NOT scanned here yet: it does not exist until
    /// the next implementation step (the pure policy is proven correct in
    /// isolation first). That step extends this method with the monitor's
    /// own structural guards (no __originalMethod, the exact "objectsCount"
    /// spelling, __result set before every false return, and the
    /// ShouldLogLedger-before-Clear ordering in Reset()) once there is a
    /// real file to scan.
    /// </summary>
    private static void SourceGuard()
    {
        var policyText = File.ReadAllText(Meta("PolicySourcePath"));
        Check(!policyText.Contains("GC.Collect("), "policy source never forces a collection");
        Check(!policyText.Contains("GetTotalMemory(true"), "policy source never reads the heap with a forced collection");
    }

    private static void ClampDecisions()
    {
        // keepIdle = 0 never clamps, regardless of arenaActive/reason/demand (the "0 = never" contract).
        Check(!Decide(true, "LoadPlayerResources", 0, 0, 100).Clamped, "a zero keepIdle never clamps");
        Check(!Decide(false, "LoadPlayerResources", 0, 0, 100).Clamped, "a zero keepIdle never clamps even outside arena");

        // arenaActive = false never clamps even with reason = LoadPlayerResources and demand far exceeding keepIdle.
        var outsideArena = Decide(false, "LoadPlayerResources", 4, 0, 100);
        Check(!outsideArena.Clamped, "never clamps outside an active arena session");
        Check(outsideArena.ObjectsCount == 100, "outside arena the request passes through unchanged");

        // Pool creation and EFT's own periodic top-up must be untouched, even with demand far exceeding keepIdle.
        foreach (var reason in new[] { "initial", "MaintainMinPoolSize", "" })
        {
            var decision = Decide(true, reason, 4, 0, 100);
            Check(!decision.Clamped, $"reason \"{reason}\" never clamps inside an active arena");
            Check(decision.ObjectsCount == 100, $"reason \"{reason}\" passes the request through unchanged");
        }

        // Demand at or under allowed = keepIdle - futurePoolSize returns Unclamped, ObjectsCount unchanged.
        var atBudget = Decide(true, "LoadPlayerResources", 4, 1, 3);
        Check(!atBudget.Clamped, "demand at the budget is not clamped");
        Check(atBudget.ObjectsCount == 3, "demand at the budget passes through unchanged");
        Check(atBudget.RunOriginal, "an unclamped decision always runs the original");

        var underBudget = Decide(true, "LoadPlayerResources", 4, 0, 2);
        Check(!underBudget.Clamped, "demand under the budget is not clamped");
        Check(underBudget.ObjectsCount == 2, "demand under the budget passes through unchanged");

        // Demand over allowed, with allowed > 0, returns Clamped, ObjectsCount == allowed, RunOriginal == true.
        var overBudget = Decide(true, "LoadPlayerResources", 4, 1, 10);
        Check(overBudget.Clamped, "demand over the budget is clamped");
        Check(overBudget.ObjectsCount == 3, "clamped ObjectsCount equals the remaining allowed headroom");
        Check(overBudget.RunOriginal, "a clamp to a positive count still runs the (shrunk) original");

        // allowed == 0 (pool already at or above keepIdle, counting the queue): skip entirely.
        var fullPool = Decide(true, "LoadPlayerResources", 4, 4, 10);
        Check(fullPool.Clamped, "a pool already at keepIdle is still a clamp");
        Check(!fullPool.RunOriginal, "a pool already at keepIdle skips the original entirely");
        Check(fullPool.ObjectsCount == 0, "a skipped call carries ObjectsCount 0");

        var overfullPool = Decide(true, "LoadPlayerResources", 4, 9, 10);
        Check(!overfullPool.RunOriginal, "a pool already over keepIdle skips the original entirely");
        // futurePoolSize already greater than keepIdle must never produce a negative allowed/ObjectsCount
        // (the Math.Max(0, ...) guard) — checked directly rather than against a hardcoded "== 0", so this
        // is the one assertion a "drop the Math.Max" mutant actually trips.
        Check(overfullPool.ObjectsCount >= 0, "allowed never goes negative once futurePoolSize exceeds keepIdle");

        // requestedCount <= 0 is a defensive no-op (Unclamped), never a negative ObjectsCount.
        var zeroRequest = Decide(true, "LoadPlayerResources", 4, 0, 0);
        Check(!zeroRequest.Clamped, "a zero request is never clamped");
        Check(zeroRequest.ObjectsCount == 0, "a zero request stays zero");

        var negativeRequest = Decide(true, "LoadPlayerResources", 4, 0, -5);
        Check(!negativeRequest.Clamped, "a negative request is never clamped");
        Check(negativeRequest.ObjectsCount == -5, "a negative request passes through unchanged rather than being coerced");

        // Idempotence: feeding the first call's own ObjectsCount back in as the second call's
        // requestedCount, against the same futurePoolSize, must never clamp a second time. This is
        // the direct unit-test encoding of the ref-parameter-chain argument in POOL-PREWARM-REVIEW.md.
        var first = Decide(true, "LoadPlayerResources", 4, 1, 10);
        Check(first.Clamped, "idempotence setup: the first call must actually clamp");
        var second = Decide(true, "LoadPlayerResources", 4, 1, first.ObjectsCount);
        Check(!second.Clamped, "a repeated call with the already-clamped count and the same futurePoolSize never clamps again");
        Check(second.ObjectsCount == first.ObjectsCount, "a repeated call leaves the already-clamped count unchanged");
    }

    private static void LedgerLogging()
    {
        Check(!PoolPrewarmPolicy.ShouldLogLedger(0), "an empty ledger is never logged");
        Check(PoolPrewarmPolicy.ShouldLogLedger(1), "a non-empty ledger is logged");
        Check(PoolPrewarmPolicy.ShouldLogLedger(50), "a large ledger is logged");

        // Restated explicitly, not just implicitly by the first assertion above: calling it
        // twice with 0 (as Reset() would after clearing) still returns false both times —
        // trivial for a pure function today, but a named assertion so a future refactor that
        // adds hidden state to this method trips a labelled failure, not silence.
        Check(!PoolPrewarmPolicy.ShouldLogLedger(0), "an empty ledger is still never logged on a repeated call");
    }

    private static PoolPrewarmPolicy.Decision Decide(bool arenaActive, string reason, int keepIdle, int futurePoolSize, int requestedCount)
        => PoolPrewarmPolicy.Decide(arenaActive, reason, keepIdle, futurePoolSize, requestedCount);
}
