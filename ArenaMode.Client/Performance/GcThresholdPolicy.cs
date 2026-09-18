using System;

namespace ArenaMode.Performance;

/// <summary>
/// Decides WHEN to switch Unity's incremental garbage collector back on
/// during a long Arena raid. Pure so the tests can drive it with a fake
/// clock and a fake heap reading; the Unity/EFT-facing half lives in
/// GcThresholdMonitor.
///
/// EFT disables Unity's GC for the whole raid — BaseLocalGame.PrepareSession
/// sets InGameMemoryManagement.GCEnabled = false, which only takes effect on
/// a machine with enough RAM (InGameMemoryManagement.GCEnabled's setter
/// refuses to DISABLE below Settings.GigabytesRequiredToDisableGCDuringRaid,
/// SPT serves 25) — and only collects again at raid end, through a forced,
/// BLOCKING sweep (InGameMemoryManagement.Collect: a synchronous full pass
/// plus, when AggressiveGC is on, a second "aggressive" pass; measured at
/// 0.46-0.9s plus ~1.9s for that second pass) and in the menu. Left alone in
/// a long endless raid the managed heap grows without bound — measured
/// ~7 MB/s on a Labyrinth deathmatch raid, ~26 GB extrapolated for an hour.
///
/// This class answers one question every few seconds: has the heap grown
/// past the point where trading vanilla's zero GC overhead for a bounded one
/// is worth it, and if so, when is the least disruptive moment to make that
/// trade. It never decides to collect anything — collecting is Unity's own
/// incremental collector's job once switched back on (bounded per frame by
/// boot.config's gc-max-time-slice) — it only decides when to flip the
/// collector's mode, which GcThresholdMonitor does through EFT's own
/// InGameMemoryManagement.GCEnabled setter (verified in the decompile: that
/// setter's low-memory guard only ever refuses to DISABLE, so enabling is
/// never refused, and it is a plain mode assignment plus a log line — never
/// a forced collection).
///
/// Four states, one raid, one switch:
///
///   idle -&gt; ARMED (heap crossed the threshold) -&gt; SWITCHED (done for this raid)
///
/// Armed does not mean switched: an abrupt collector flip mid-firefight is
/// exactly the kind of thing this feature exists to avoid, so arming only
/// requests the switch at the next least-visible moment (a local death, or a
/// round break — see ShouldSwitchAtMoment) and a bounded ArmedTimeoutSeconds
/// wait covers the player who never dies and whose raid never breaks between
/// rounds. Switching happens at most once per raid: once Switched is set,
/// ShouldArm always answers false, so a heap that keeps growing after the
/// switch (it will — the collector is now doing bounded, incremental work,
/// not none) is never re-armed. ResetForNewRaid clears both flags at the
/// next raid boundary; see GcThresholdMonitor.Reset for where that is called
/// and GC-THRESHOLD-REVIEW.md for the argument that nothing here can leak
/// into the next raid regardless, because PrepareSession always re-asserts
/// vanilla state for itself.
/// </summary>
internal static class GcThresholdPolicy
{
    /// <summary>
    /// Longest an arming may wait for a death or round break before the
    /// switch happens anyway, so a player who never dies still gets it.
    /// </summary>
    public const float ArmedTimeoutSeconds = 120f;

    /// <summary>
    /// How long after our own switch a second switch-to-Enabled request may
    /// run again, if something else disabled the collector a second time in
    /// the same raid. Not a retry loop — EFT's own setter is a single cheap
    /// assignment, so nothing here is waiting for an operation to finish —
    /// this exists purely to bound how often the log line it produces can
    /// repeat if something keeps flipping the mode back and forth.
    /// </summary>
    public const float ReassertCooldownSeconds = 30f;

    /// <summary>State carried between polls, one instance per raid.</summary>
    public struct State
    {
        /// <summary>Time.time the heap first crossed the threshold; NaN until armed.</summary>
        public float ArmedAt;

        /// <summary>True once the switch to Enabled has been requested this raid.</summary>
        public bool Switched;

        /// <summary>Time.time of the last re-enable after an external disable; NaN until one happens.</summary>
        public float LastReassertAt;

        public readonly bool Armed => !float.IsNaN(ArmedAt);

        public static State Fresh => new State { ArmedAt = float.NaN, LastReassertAt = float.NaN };
    }

    /// <summary>Call at every raid boundary (armed or not), never mid-raid.</summary>
    public static void ResetForNewRaid(ref State state) => state = State.Fresh;

    /// <summary>
    /// Whether the heap just crossed the threshold and arming should start.
    ///
    /// Gated on BOTH an active Arena session and the collector currently
    /// being disabled: on a machine EFT itself kept the collector enabled
    /// (under GigabytesRequiredToDisableGCDuringRaid, i.e. SPT's default
    /// 25 GB), this always answers false and the feature does nothing — there
    /// is nothing to switch on, and vanilla already behaves the way this
    /// feature would otherwise force. Once armed or switched this raid it
    /// never re-arms, so a heap that keeps climbing after the switch cannot
    /// retrigger anything.
    /// </summary>
    public static bool ShouldArm(in State state, bool arenaActive, bool gcCurrentlyDisabled, long thresholdBytes, long heapBytes)
    {
        if (state.Armed || state.Switched)
            return false;

        if (!arenaActive || !gcCurrentlyDisabled)
            return false;

        // 0 = never (pure vanilla).
        if (thresholdBytes <= 0)
            return false;

        return heapBytes >= thresholdBytes;
    }

    /// <summary>Latches the arm moment. Idempotent only in the sense that ShouldArm never asks twice.</summary>
    public static void Arm(ref State state, float now) => state.ArmedAt = now;

    /// <summary>
    /// True at the moment hook (a local death, or a round break) once armed
    /// and not yet switched. The moment itself decides WHICH of those two
    /// fired; this only answers whether the moment should act on it.
    /// </summary>
    public static bool ShouldSwitchAtMoment(in State state) => state.Armed && !state.Switched;

    /// <summary>
    /// True once armed for longer than the bounded wait — the player who
    /// never dies and whose raid never breaks between rounds still gets the
    /// switch eventually, rather than carrying an ever-growing heap for the
    /// rest of the raid.
    /// </summary>
    public static bool ShouldSwitchOnTimeout(in State state, float now)
        => state.Armed && !state.Switched && now - state.ArmedAt >= ArmedTimeoutSeconds;

    /// <summary>Marks the once-per-raid switch as done. Never undone within a raid.</summary>
    public static void MarkSwitched(ref State state) => state.Switched = true;

    /// <summary>
    /// True if, after our own switch, the collector reads disabled again
    /// (neither EFT nor this feature disables it mid-raid on its own, so
    /// this is purely defensive against some other mod or an EFT path this
    /// review did not find) and the rate limit has cleared.
    ///
    /// Deliberately chooses to re-enable rather than accept the disable: the
    /// alternative — leave it disabled once something else asked for that —
    /// can silently undo this whole feature for the rest of a long raid on
    /// the strength of one unrelated decision, which is worse than a rare,
    /// rate-limited extra log line.
    /// </summary>
    public static bool ShouldReassert(in State state, bool arenaActive, bool gcCurrentlyDisabled, float now)
    {
        if (!arenaActive || !state.Switched || !gcCurrentlyDisabled)
            return false;

        return float.IsNaN(state.LastReassertAt) || now - state.LastReassertAt >= ReassertCooldownSeconds;
    }

    /// <summary>Records a reassert so the cooldown above can rate-limit the next one.</summary>
    public static void MarkReasserted(ref State state, float now) => state.LastReassertAt = now;
}
