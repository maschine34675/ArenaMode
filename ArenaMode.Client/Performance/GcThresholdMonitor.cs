using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Session;
using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace ArenaMode.Performance;

/// <summary>
/// Unity/EFT-facing half of the GC threshold feature. GcThresholdPolicy
/// decides WHEN; this reads the heap, flips the collector's mode through
/// EFT's own setter, and logs. See Config.Settings.GcHeapThresholdGB for the
/// setting and GC-THRESHOLD-REVIEW.md for the measurements this design
/// answers to.
/// </summary>
internal static class GcThresholdMonitor
{
    /// <summary>How often the poll actually reads the heap and the collector's mode.</summary>
    private const float PollCadenceSeconds = 5f;

    /// <summary>When the follow-up proof line fires after a switch, inside the requested 30-60s window.</summary>
    private const float FollowUpDelaySeconds = 45f;

    private static GcThresholdPolicy.State _state = GcThresholdPolicy.State.Fresh;
    private static float _nextPollAt;
    private static float _raidStartedAt;
    private static bool _subscribed;

    private static bool _followUpPending;
    private static float _followUpAt;
    private static int _collectionCountAtSwitch;
    private static long _heapAtSwitch;

    /// <summary>Called once from Plugin.Awake, alongside the other event subscribers.</summary>
    public static void Initialize()
    {
        if (_subscribed)
            return;

        _subscribed = true;
        ArenaEvents.OnPlayerDied += OnPlayerDied;
        ArenaEvents.OnKitApplied += OnKitApplied;
    }

    /// <summary>
    /// Called at every raid boundary from ArenaRespawnDriver.ResetSession —
    /// the same place CorpseCleanupService.Reset and its neighbours run —
    /// so an arm or a switch from a previous raid can never survive into a
    /// new one even if something upstream of this class ever changed.
    /// EFT's own PrepareSession re-asserts GCEnabled = false unconditionally
    /// at the start of every raid regardless of what this class did in the
    /// last one (see the argument at Config.Settings.GcHeapThresholdGB), so
    /// this reset is about OUR bookkeeping, not about EFT's collector mode.
    /// </summary>
    public static void Reset()
    {
        GcThresholdPolicy.ResetForNewRaid(ref _state);
        _nextPollAt = 0f;
        _raidStartedAt = Time.time;
        _followUpPending = false;
    }

    /// <summary>
    /// Called every frame from ArenaController.Update. Allocation-free off
    /// the poll cadence: the two time comparisons below are the whole cost
    /// on a frame that is neither a poll tick nor a pending follow-up.
    /// </summary>
    public static void Tick()
    {
        if (!ArenaSession.IsActive)
            return;

        if (_followUpPending && Time.time >= _followUpAt)
            LogFollowUp();

        if (Time.time < _nextPollAt)
            return;

        _nextPollAt = Time.time + PollCadenceSeconds;

        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena GC threshold poll failed: {ex.Message}");
        }
    }

    private static void Poll()
    {
        // No early return for 0 = never: ShouldArm refuses a zero threshold, and a
        // raid that already armed or switched must keep its bounded wait and its
        // reassert even if the setting is turned down to 0 mid-raid through the
        // hot-reloadable config page.
        var thresholdGb = Settings.GcHeapThresholdGB.Value;
        var gcDisabled = GarbageCollector.GCMode == GarbageCollector.Mode.Disabled;

        // The one heap reading this feature is allowed to take: the runtime's
        // own byte counter, read with force=false so it never triggers a
        // collection by asking. Reading it costs nothing the runtime was not
        // already tracking. See GC-THRESHOLD-REVIEW.md and the source guard
        // in tools/GcThresholdChecks for why "false" here is load-bearing.
        var heapBytes = GC.GetTotalMemory(false);
        var thresholdBytes = (long)((double)thresholdGb * 1024d * 1024d * 1024d);

        if (GcThresholdPolicy.ShouldArm(_state, ArenaSession.IsActive, gcDisabled, thresholdBytes, heapBytes))
        {
            GcThresholdPolicy.Arm(ref _state, Time.time);
            Plugin.ModLogger.LogDebug(
                $"Arena GC threshold armed: heap {GB(heapBytes):0.00} GB >= {thresholdGb:0.0} GB threshold "
                + $"at raid minute {RaidMinute:0.0}; switching on the next death, round break, or after "
                + $"{GcThresholdPolicy.ArmedTimeoutSeconds:0} s.");
        }

        if (GcThresholdPolicy.ShouldSwitchOnTimeout(_state, Time.time))
            Switch("the bounded wait");

        if (GcThresholdPolicy.ShouldReassert(_state, ArenaSession.IsActive, gcDisabled, Time.time))
        {
            GcThresholdPolicy.MarkReasserted(ref _state, Time.time);
            Plugin.ModLogger.LogDebug(
                "Arena GC threshold: the collector was disabled again after this raid's switch; re-enabling "
                + "rather than letting whatever did that silently undo the feature for the rest of the raid.");
            InGameMemoryManagement.GCEnabled = true;
        }
    }

    /// <summary>
    /// ArenaEvents.OnPlayerDied — the local player's own death, before the
    /// instant respawn starts. Chosen as the primary moment because it is
    /// already the least visible point in an Arena raid: the screen is about
    /// to change regardless, and nothing needs to react to player input
    /// during the respawn's own delay.
    /// </summary>
    private static void OnPlayerDied(EFT.Player _)
    {
        if (!ArenaSession.IsActive)
            return;

        if (GcThresholdPolicy.ShouldSwitchAtMoment(_state))
            Switch("a local death");
    }

    /// <summary>
    /// ArenaEvents.OnKitApplied — raised once a new body wears its kit: after a
    /// death respawn, the raid-start swap and the round-break swap
    /// (LiveKitRespawn.AtRoundBreak from RoundsMode and TacticalMode). This is the
    /// round-break moment: the player stands protected in a fresh body at a
    /// scoreboard pause. After a death it only repeats the death's moment, and at
    /// raid start nothing can be armed yet (Reset runs at the raid boundary).
    /// </summary>
    private static void OnKitApplied(Kits.KitDefinition _)
    {
        if (!ArenaSession.IsActive)
            return;

        if (GcThresholdPolicy.ShouldSwitchAtMoment(_state))
            Switch("a new body (round break or respawn)");
    }

    private static void Switch(string moment)
    {
        GcThresholdPolicy.MarkSwitched(ref _state);

        var heapBytes = GC.GetTotalMemory(false);
        _collectionCountAtSwitch = GC.CollectionCount(0);
        _heapAtSwitch = heapBytes;
        _followUpPending = true;
        _followUpAt = Time.time + FollowUpDelaySeconds;

        Plugin.ModLogger.LogDebug(
            $"Arena GC threshold switch: heap {GB(heapBytes):0.00} GB, threshold "
            + $"{Settings.GcHeapThresholdGB.Value:0.0} GB, raid minute {RaidMinute:0.0}, triggered by {moment}. "
            + "Switching Unity's incremental collector back on for the rest of this raid.");

        // EFT's own setter (InGameMemoryManagement.GCEnabled): verified in the
        // decompile that enabling is never refused — its low-memory guard only
        // ever blocks DISABLING — and that it logs "GC mode switched to
        // Enabled" to EFT's own application.log. This is a plain
        // GarbageCollector.GCMode assignment, never a forced sweep of the
        // heap; see GC-THRESHOLD-REVIEW.md for why that distinction is the
        // whole point of this feature.
        InGameMemoryManagement.GCEnabled = true;
    }

    /// <summary>
    /// The proof line: did the collector actually do bounded, incremental
    /// work after the switch, and how much did the heap move. Read-only
    /// counters, both allowed by the source guard (GC.CollectionCount is not
    /// a collection request, and the heap reading is the same force=false
    /// call the poll above uses).
    /// </summary>
    private static void LogFollowUp()
    {
        _followUpPending = false;
        var heapBytes = GC.GetTotalMemory(false);
        var collections = GC.CollectionCount(0) - _collectionCountAtSwitch;
        var deltaGb = GB(heapBytes) - GB(_heapAtSwitch);
        Plugin.ModLogger.LogDebug(
            $"Arena GC threshold follow-up: heap {GB(heapBytes):0.00} GB ({(deltaGb >= 0 ? "+" : string.Empty)}{deltaGb:0.00} GB "
            + $"since the switch), {collections} gen0 collection(s) since the switch.");
    }

    private static float GB(long bytes) => bytes / 1073741824f;

    private static float RaidMinute => (Time.time - _raidStartedAt) / 60f;
}
