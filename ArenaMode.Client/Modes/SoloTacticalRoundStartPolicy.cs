namespace ArenaMode.Modes;

internal enum SoloTacticalRoundStartDecision
{
    /// <summary>Nothing in the way: start the round.</summary>
    Start,

    /// <summary>
    /// The raid-start kit swap is still in flight, so the buy phase stays open.
    /// The first frame of that wait, so the caller can say so once.
    /// </summary>
    HoldBegun,

    /// <summary>Still waiting for that swap.</summary>
    Hold,

    /// <summary>The swap landed or stood down while the round waited: start now.</summary>
    StartAfterHold,

    /// <summary>
    /// The swap outlived the cap: start regardless, and never wait for it
    /// again this match.
    /// </summary>
    StartAfterGivingUp,
}

/// <summary>
/// Pure decision for the end of a Solo Tactical buy phase: does the round
/// start now, or does it wait for a body swap that is still in flight?
///
/// Why wait: the buy phase (five seconds at its shortest, or the Ready
/// button) can end before the raid-start kit swap has landed — it waits for
/// the auto-draw animation and for the kit's bundles, and a cold kit is the
/// 22 s KitPreloadPlan measured on the slow machine. StartRound then charges
/// the purchase, records it as worn and starts a round-break swap for it
/// while the raid-start swap is still preparing on the same body. Both
/// coroutines wait on the same gates; the warm base-kit one lands first, the
/// purchase's finds the body changed hands and stands down with nothing to
/// tell — the player fights round one in the base kit, has paid for the
/// other, and is handed it a round late if they survive. The same hole opens
/// behind a death in the buy phase: Tactical does not count it as being
/// down, so the driver replaces the body at once, and a round that starts
/// while that pipeline runs charges a purchase the round-break swap then
/// refuses (it stands down on IsRespawnInProgress). Team Deathmatch already
/// refuses to go live while either flag is set; this is the same wait for
/// Tactical, in the one place where a round starts.
///
/// Why a cap: the raid-start flag is cleared by the swap coroutine's own
/// exit, and a coroutine that never exits (a bundle load that never
/// completes; a child coroutine that threw, whose parent Unity never
/// resumes) would otherwise hold the buy phase open for the rest of the raid
/// — a deadlocked mode, the class of bug this project has shipped before.
/// After the cap the round starts anyway, with the race back in play and a
/// warning that says so, and the mode never waits again: a swap that
/// outlived the cap is stuck, not slow.
///
/// Pure so the tests can drive it with a fake clock; the Unity-facing half
/// (the flag, the toast, the log) lives in TacticalMode.
/// </summary>
internal static class SoloTacticalRoundStartPolicy
{
    /// <summary>
    /// Longest the round start waits once the buy phase is over. Well past
    /// the 22 s a cold kit took on the slow machine and the respawn engine's
    /// own 30 s body-creation deadline; only a swap that will never land
    /// waits this long.
    /// </summary>
    public const float InitialSwapHoldSeconds = 60f;

    /// <summary>
    /// Mutable state, one per match. Its default is "not waiting, not given
    /// up" on purpose, so a field that was never initialised behaves.
    /// </summary>
    public struct State
    {
        /// <summary>A wait is in progress.</summary>
        public bool Holding;

        /// <summary>When the current wait began; meaningful only while Holding.</summary>
        public float HoldStartedAt;

        /// <summary>How long the last wait lasted, for the log line that ends it.</summary>
        public float LastHoldSeconds;

        /// <summary>Set once the cap expired: the mode never waits again this match.</summary>
        public bool GaveUp;
    }

    public static SoloTacticalRoundStartDecision Evaluate(ref State state, bool swapPending, float now)
    {
        if (!swapPending || state.GaveUp)
        {
            if (!state.Holding)
                return SoloTacticalRoundStartDecision.Start;

            state.Holding = false;
            state.LastHoldSeconds = now - state.HoldStartedAt;
            return SoloTacticalRoundStartDecision.StartAfterHold;
        }

        if (!state.Holding)
        {
            state.Holding = true;
            state.HoldStartedAt = now;
            return SoloTacticalRoundStartDecision.HoldBegun;
        }

        if (now - state.HoldStartedAt < InitialSwapHoldSeconds)
            return SoloTacticalRoundStartDecision.Hold;

        state.Holding = false;
        state.LastHoldSeconds = now - state.HoldStartedAt;
        state.GaveUp = true;
        return SoloTacticalRoundStartDecision.StartAfterGivingUp;
    }
}
