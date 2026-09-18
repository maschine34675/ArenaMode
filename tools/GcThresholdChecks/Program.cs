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

    private const long OneGb = 1024L * 1024L * 1024L;

    private static void Main()
    {
        SourceGuard();
        ThresholdAndGating();
        ArmLatching();
        MomentAndTimeoutSwitch();
        OncePerRaidAndReset();
        Reassert();

        Console.WriteLine($"ARENA_GC_THRESHOLD_BASELINE_PASSED checks={checks}");
    }

    /// <summary>
    /// The feature must never force a blocking collection: not GC.Collect,
    /// not GC.GetTotalMemory(true). "GC.Collect(" (with the open paren) is
    /// the banned substring rather than bare "GC.Collect", because
    /// GC.CollectionCount(0) — which the follow-up log line legitimately
    /// calls — contains "GC.Collect" as a prefix and would otherwise trip
    /// a false positive.
    /// </summary>
    private static void SourceGuard()
    {
        var policyText = File.ReadAllText(Meta("PolicySourcePath"));
        Check(!policyText.Contains("GC.Collect("), "policy source never forces a collection");
        Check(!policyText.Contains("GetTotalMemory(true"), "policy source never reads the heap with a forced collection");

        var monitorText = File.ReadAllText(Meta("MonitorSourcePath"));
        Check(!monitorText.Contains("GC.Collect("), "monitor source never forces a collection");
        Check(!monitorText.Contains("GetTotalMemory(true"), "monitor source never reads the heap with a forced collection");
    }

    private static void ThresholdAndGating()
    {
        var fresh = GcThresholdPolicy.State.Fresh;

        // 0 = never, even with a huge heap and every other gate open.
        Check(!GcThresholdPolicy.ShouldArm(fresh, true, true, 0, 100L * OneGb), "a zero threshold never arms");

        // Below threshold: no arm.
        Check(!GcThresholdPolicy.ShouldArm(fresh, true, true, 4 * OneGb, 3 * OneGb), "under threshold never arms");

        // At/above threshold, Arena active, collector disabled: arms.
        Check(GcThresholdPolicy.ShouldArm(fresh, true, true, 4 * OneGb, 4 * OneGb), "at threshold arms");
        Check(GcThresholdPolicy.ShouldArm(fresh, true, true, 4 * OneGb, 5 * OneGb), "over threshold arms");

        // Not gated on Arena: no session, no arm even with the heap over threshold.
        Check(!GcThresholdPolicy.ShouldArm(fresh, false, true, 4 * OneGb, 5 * OneGb), "never arms outside an active arena session");

        // Not gated on the collector already being enabled: a machine EFT kept
        // enabled (under its own RAM floor) must never arm — there is nothing
        // to switch, and arming there would be silent dead bookkeeping at best.
        Check(!GcThresholdPolicy.ShouldArm(fresh, true, false, 4 * OneGb, 5 * OneGb), "never arms while the collector is already enabled");

        // Once armed, does not arm again (idempotent latch).
        var armed = fresh;
        GcThresholdPolicy.Arm(ref armed, 10f);
        Check(!GcThresholdPolicy.ShouldArm(armed, true, true, 4 * OneGb, 5 * OneGb), "an already-armed raid does not arm twice");

        // Once switched, never re-arms even on a fresh, much larger heap reading.
        var switched = armed;
        GcThresholdPolicy.MarkSwitched(ref switched);
        Check(!GcThresholdPolicy.ShouldArm(switched, true, true, 4 * OneGb, 50 * OneGb), "a switched raid never re-arms");
    }

    private static void ArmLatching()
    {
        var state = GcThresholdPolicy.State.Fresh;
        Check(!state.Armed, "fresh state starts unarmed");

        GcThresholdPolicy.Arm(ref state, 100f);
        Check(state.Armed, "Arm sets the armed flag");
        Check(!state.Switched, "arming alone does not switch");
    }

    private static void MomentAndTimeoutSwitch()
    {
        var state = GcThresholdPolicy.State.Fresh;
        Check(!GcThresholdPolicy.ShouldSwitchAtMoment(state), "an unarmed raid never switches at a moment");
        Check(!GcThresholdPolicy.ShouldSwitchOnTimeout(state, 99999f), "an unarmed raid never times out");

        GcThresholdPolicy.Arm(ref state, 100f);
        Check(GcThresholdPolicy.ShouldSwitchAtMoment(state), "an armed raid switches at the next moment");
        Check(!GcThresholdPolicy.ShouldSwitchOnTimeout(state, 100f + GcThresholdPolicy.ArmedTimeoutSeconds - 1f), "the bounded wait has not elapsed yet");
        Check(GcThresholdPolicy.ShouldSwitchOnTimeout(state, 100f + GcThresholdPolicy.ArmedTimeoutSeconds), "the bounded wait switches without a moment");

        GcThresholdPolicy.MarkSwitched(ref state);
        Check(!GcThresholdPolicy.ShouldSwitchAtMoment(state), "an already-switched raid does not switch again at a moment");
        Check(!GcThresholdPolicy.ShouldSwitchOnTimeout(state, 100f + GcThresholdPolicy.ArmedTimeoutSeconds + 1000f), "an already-switched raid does not switch again on timeout");
    }

    private static void OncePerRaidAndReset()
    {
        var state = GcThresholdPolicy.State.Fresh;
        GcThresholdPolicy.Arm(ref state, 5f);
        GcThresholdPolicy.MarkSwitched(ref state);
        Check(state.Switched, "MarkSwitched sets the switched flag");

        GcThresholdPolicy.ResetForNewRaid(ref state);
        Check(!state.Armed, "reset clears the armed flag for the next raid");
        Check(!state.Switched, "reset clears the switched flag for the next raid");
        Check(!GcThresholdPolicy.ShouldSwitchAtMoment(state), "a reset raid does not switch at a moment");
        Check(!GcThresholdPolicy.ShouldReassert(state, true, true, 0f), "a reset raid has nothing to reassert");
    }

    private static void Reassert()
    {
        var state = GcThresholdPolicy.State.Fresh;

        // Never reasserts before this raid's own switch happened.
        Check(!GcThresholdPolicy.ShouldReassert(state, true, true, 0f), "reassert never fires before this raid's own switch");

        GcThresholdPolicy.Arm(ref state, 0f);
        GcThresholdPolicy.MarkSwitched(ref state);

        // Collector reads enabled (nothing disabled it again): no reassert.
        Check(!GcThresholdPolicy.ShouldReassert(state, true, false, 10f), "reassert does not fire while the collector reads enabled");

        // Something disabled it again after our switch: reassert fires.
        Check(GcThresholdPolicy.ShouldReassert(state, true, true, 10f), "reassert fires once the collector is disabled again after our switch");
        GcThresholdPolicy.MarkReasserted(ref state, 10f);

        // Rate limited: not again inside the cooldown window.
        Check(!GcThresholdPolicy.ShouldReassert(state, true, true, 10f + GcThresholdPolicy.ReassertCooldownSeconds - 1f), "reassert is rate limited");
        Check(GcThresholdPolicy.ShouldReassert(state, true, true, 10f + GcThresholdPolicy.ReassertCooldownSeconds), "reassert fires again once the cooldown clears");

        // Never outside an active arena session, whatever the collector reads.
        Check(!GcThresholdPolicy.ShouldReassert(state, false, true, 10f + GcThresholdPolicy.ReassertCooldownSeconds), "reassert never fires outside an active arena session");
    }
}
