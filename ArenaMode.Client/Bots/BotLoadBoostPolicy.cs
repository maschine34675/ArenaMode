using System;

namespace ArenaMode.Bots;

/// <summary>
/// Decides WHEN the load boost is wanted. Pure so the tests can drive it with
/// a fake clock; the Unity-facing half lives in BotLoadBoost.
///
/// Four rules.
///
/// A boost is wanted only after loading has been in flight for DelaySeconds
/// without a real break, so a machine that finishes its loads quickly never
/// pays a frame for it.
///
/// A break shorter than ReleaseGraceSeconds is not a break. The kit warm-up
/// loads twenty kits one after another with a frame of "nothing loading"
/// between them, and two bot requests from opposite factions hand over the
/// same way; without the grace every one of those was its own episode, each
/// paying the delay unboosted and logging its own on/off pair. Holding the
/// mode across the gap costs nothing: with no continuation queued the
/// scheduler returns before it looks at the mode at all.
///
/// The boost is dropped once nothing has loaded for the grace.
///
/// An episode is capped at MaxHoldSeconds. The game's bundle counter is
/// incremented and decremented around an await with no try/finally, so an
/// exception inside a load leaves it above zero for the rest of that game.
/// (Not raid teardown: the creator is built per LocalGame, so a cancelled
/// load's count dies with it.) A stuck counter looks like loading that
/// never ends and never changes; after the cap the boost stands down until
/// the counter is seen either idle or at a DIFFERENT value — a live load
/// moves it within seconds, a stuck one never does.
/// </summary>
internal static class BotLoadBoostPolicy
{
    /// <summary>
    /// Longest single boost episode with the loading signature unchanged.
    /// Well past any real load; only a stuck counter sits still this long.
    /// </summary>
    public const float MaxHoldSeconds = 90f;

    /// <summary>
    /// How long "nothing loading" must persist before it counts as the end of
    /// an episode. Bridges the frame-sized gaps between back-to-back loads.
    /// </summary>
    public const float ReleaseGraceSeconds = 0.5f;

    /// <summary>
    /// The delay on a machine the scheduler already sees as over budget.
    ///
    /// The configured delay exists so a machine that finishes its loads
    /// quickly never pays a frame for the boost. Over budget there is no such
    /// machine: every load runs one pass per frame until the boost arrives,
    /// and the delay is simply that many frames lost — sixty of a bot's few
    /// hundred steps at the default. Not zero: with the release grace this
    /// bounds the toggle rate to one on/off pair per three quarters of a
    /// second, which is what keeps a burst of tiny loads from chattering.
    ///
    /// It is a trade, not a free lunch. "Over budget" also describes a fast
    /// machine in a heavy moment — a spawn wave, the raid-start assembly —
    /// whose frames are long because they are full, not because they are
    /// waiting on a display. There the boost adds its budget to a frame that
    /// has no wait to give. The on-line prints the window it decided on.
    /// </summary>
    public const float OverBudgetDelaySeconds = 0.25f;

    /// <summary>
    /// Whether the scheduler has, on average, been over budget lately.
    ///
    /// The scheduler decides per frame: it compares the window from the
    /// previous frame's end to LateUpdate — limiter and VSync waits included,
    /// render submission and its own continuation time excluded — against
    /// FrameTicks, raw Stopwatch ticks against raw TimeSpan ticks (they agree
    /// on this hardware, 10 MHz; the game assumes it, so the mirror is exact
    /// either way). It also keeps a hundred-frame moving average of that same
    /// window, which it never consults itself; this is what the mod reads,
    /// compared the same way. A mean is a coarse proxy — a hundred short
    /// frames with a few long hitches can read over — but it is the game's own
    /// measurement of the game's own quantity, and it costs nothing.
    ///
    /// The sample is taken before the continuations run and the stopwatch is
    /// restarted after, so the boost's STEP time is never inside a sample.
    /// Other things it does are: the display wait a longer frame may lengthen,
    /// and - if the async slice lever is ever switched on - the engine's
    /// integration step, which runs inside the sampled window. So the boost can
    /// nudge the verdict it is judged by. Latching the verdict per episode is
    /// what keeps that from becoming a loop: within an episode the reading
    /// cannot change the decision, and between episodes a wrong reading costs
    /// three quarters of a second of delay, never a hold.
    /// </summary>
    public static bool IsOverBudget(double averageWindowTicks, long frameTicks)
        => frameTicks > 0 && averageWindowTicks > 0 && averageWindowTicks >= frameTicks;

    /// <summary>
    /// The delay that applies: the configured one where the boost is
    /// insurance, the short one where it is the only thing moving loads. A
    /// configured value below the short one is honoured either way.
    /// </summary>
    public static float DelayFor(bool overBudget, float configuredDelaySeconds)
    {
        float configured = Math.Max(0f, configuredDelaySeconds);
        return overBudget ? Math.Min(OverBudgetDelaySeconds, configured) : configured;
    }

    public struct State
    {
        /// <summary>When the current loading episode began; NaN when idle.</summary>
        public float LoadingSince;

        /// <summary>When loading was last seen to stop inside an episode; NaN while loading.</summary>
        public float IdleSince;

        /// <summary>When the boost was first wanted in this episode; NaN when not wanted.</summary>
        public float WantedSince;

        /// <summary>
        /// The over-budget verdict as it stood when this episode began. Latched,
        /// because the delay is re-checked every tick against a running clock:
        /// a verdict that flipped under a hold would shrink or grow the delay
        /// mid-episode, drop the hold, and re-acquire it a moment later.
        /// </summary>
        public bool OverBudget;

        /// <summary>The signature the cap clock is measuring against; reset whenever it moves.</summary>
        public int HeldSignature;

        /// <summary>Set when an episode overran MaxHoldSeconds without the signature moving.</summary>
        public bool Tripped;

        /// <summary>The loading signature at the moment of the trip.</summary>
        public int TrippedSignature;

        public static State Idle => new State
        {
            LoadingSince = float.NaN,
            IdleSince = float.NaN,
            WantedSince = float.NaN,
        };
    }

    /// <summary>
    /// Whether the boost should be held right now.
    /// </summary>
    /// <param name="state">Carried between ticks.</param>
    /// <param name="enabled">The setting; false releases at once.</param>
    /// <param name="loading">Anything loading bundles or building pools this tick.</param>
    /// <param name="now">Unscaled seconds.</param>
    /// <param name="configuredDelaySeconds">The setting: how long loading must persist before boosting where the boost is insurance.</param>
    /// <param name="overBudget">The current verdict; latched into the episode when one begins.</param>
    /// <param name="signature">
    /// Any number that changes whenever the set of loads in flight changes —
    /// the raw counters are fine. Only compared for equality.
    /// </param>
    public static bool Wanted(ref State state, bool enabled, bool loading, float now, float configuredDelaySeconds, bool overBudget, int signature)
    {
        if (!enabled)
        {
            // The trip survives a toggle: flipping a setting is no evidence
            // the counter recovered.
            state.LoadingSince = float.NaN;
            state.IdleSince = float.NaN;
            state.WantedSince = float.NaN;
            return false;
        }

        if (!loading)
        {
            // Idle is the proof the counter is not stuck.
            state.Tripped = false;

            if (float.IsNaN(state.LoadingSince))
                return false;

            if (float.IsNaN(state.IdleSince))
                state.IdleSince = now;

            if (now - state.IdleSince <= ReleaseGraceSeconds)
            {
                // Inside the grace the episode goes on, and so does a hold.
                return !float.IsNaN(state.WantedSince);
            }

            state.LoadingSince = float.NaN;
            state.IdleSince = float.NaN;
            state.WantedSince = float.NaN;
            return false;
        }

        if (!float.IsNaN(state.IdleSince))
        {
            // Ticks are frames in the game and sparse in a test; either way a
            // resume after a gap longer than the grace is a new episode, not
            // the old one carrying on.
            if (now - state.IdleSince > ReleaseGraceSeconds)
            {
                state.LoadingSince = float.NaN;
                state.WantedSince = float.NaN;
            }

            state.IdleSince = float.NaN;
        }

        if (state.Tripped)
        {
            if (signature == state.TrippedSignature)
                return false;

            // The counter moved: something real is loading after all.
            state.Tripped = false;
        }

        if (float.IsNaN(state.LoadingSince))
        {
            state.LoadingSince = now;
            state.OverBudget = overBudget;
        }

        if (now - state.LoadingSince < DelayFor(state.OverBudget, configuredDelaySeconds))
            return false;

        // The cap measures how long the signature has sat STILL, not how long
        // the episode has run: a raid of back-to-back loads moves it every few
        // seconds and must never trip.
        if (float.IsNaN(state.WantedSince) || signature != state.HeldSignature)
        {
            state.WantedSince = now;
            state.HeldSignature = signature;
        }

        if (now - state.WantedSince > MaxHoldSeconds)
        {
            state.Tripped = true;
            state.TrippedSignature = signature;
            state.WantedSince = float.NaN;
            return false;
        }

        return true;
    }
}
