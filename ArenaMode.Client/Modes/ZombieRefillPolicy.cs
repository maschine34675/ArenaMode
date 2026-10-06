using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure policy for item 3 ("refill between waves"): decides exactly WHEN the
/// zombie intermission's ammo/consumable refill should be attempted, never
/// HOW — that is <see cref="ZombieLoadoutRefill"/>'s job, through the
/// existing live kit-respawn path Gun Game/Tactical already use between
/// rounds. No EFT/Unity types, so ArenaMode.Tests can link this file
/// directly (same convention as ZombieWavePolicy/ZombieRoleMix/
/// ZombieProfilePlan — see ArenaMode.Tests.csproj's ClientRules links).
///
/// <para><b>Contract.</b> Fires exactly once per Wave-&gt;Intermission
/// transition (one call per cleared wave), and only while the caller reports
/// the player eligible — alive, and not inside a death/respawn window. A
/// transiently ineligible player is not a permanent skip: Zombie's instant
/// respawn pipeline can still be finishing exactly when the wave's last
/// infected dies (measured ~0.2s on the GPU host — see
/// GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md), so the SAME intermission keeps
/// asking on every later call until the player becomes eligible, bounded by
/// <see cref="Config.MaxAttempts"/> so a caller-side path that keeps
/// reporting "did not start" for some unrelated reason cannot retry without
/// end. Only real attempts (<see cref="ReportNotStarted"/>) consume that
/// budget; a tick where the player was merely ineligible costs nothing,
/// because recovery is expected well inside one intermission.</para>
///
/// <para><b>Exactly once, not "started-at-least-once."</b> The moment the
/// caller reports an attempt actually STARTED (<see cref="ReportStarted"/>),
/// this intermission is resolved for good — Evaluate will not ask again
/// until the NEXT intermission, even if that started swap later fails
/// asynchronously (the real re-apply runs over several frames inside
/// LiveKitRespawn.AtRoundBreak's own coroutine). This mirrors
/// ZombieWavePolicy's own optimistic-commit convention for spawns
/// (SpawnedThisWave++ on DECIDING to spawn, not on confirmed success) —
/// "dispatched" and "resolved" are the same event here, by design: retrying
/// a swap that is already in flight would mean two body replacements
/// racing for the same life.</para>
/// </summary>
internal static class ZombieRefillPolicy
{
    internal struct Config
    {
        /// <summary>
        /// Ceiling on how many times Evaluate may return ShouldAttempt=true
        /// for the SAME intermission before giving up on it for good (no
        /// refill that intermission). Values &lt;= 0 are treated as 1 — the
        /// policy always allows at least one real attempt.
        /// </summary>
        internal int MaxAttempts;
    }

    /// <summary>Mutable per-match state. Default-constructed is NOT a valid start — use <see cref="Initial"/>.</summary>
    internal struct State
    {
        internal ZombieWavePolicy.Phase LastPhase;

        /// <summary>
        /// True from the moment an intermission is entered until this
        /// intermission's refill is resolved — fired (<see cref="ReportStarted"/>)
        /// or given up on (attempts exhausted). False the instant a new wave
        /// starts, so the next intermission arms fresh.
        /// </summary>
        internal bool Armed;

        /// <summary>Real attempts made THIS intermission (ReportNotStarted calls only).</summary>
        internal int AttemptsMade;
    }

    internal struct Result
    {
        /// <summary>True exactly when the caller should attempt the refill now
        /// (and must follow up with ReportStarted or ReportNotStarted).</summary>
        internal bool ShouldAttempt;
    }

    /// <summary>No intermission has happened yet, nothing armed.</summary>
    internal static State Initial() => new State
    {
        LastPhase = ZombieWavePolicy.Phase.Wave,
        Armed = false,
        AttemptsMade = 0,
    };

    /// <param name="phase">This tick's ZombieWavePolicy.Result.Phase — the
    /// same value ZombieMode already reads every WaveEvaluateIntervalSeconds.</param>
    /// <param name="playerEligible">True iff the player is alive right now
    /// and not inside a death/respawn window (e.g.
    /// !ArenaRespawnDriver.IsRespawnInProgress &amp;&amp; !StandDown &amp;&amp;
    /// HealthController.IsAlive). The caller computes this from the real
    /// Player/driver state; this pure class owns no such reference.</param>
    internal static Result Evaluate(ref State state, Config config, ZombieWavePolicy.Phase phase, bool playerEligible)
    {
        bool enteredIntermission = phase == ZombieWavePolicy.Phase.Intermission
            && state.LastPhase != ZombieWavePolicy.Phase.Intermission;
        state.LastPhase = phase;

        if (enteredIntermission)
        {
            // A fresh intermission always gets its own full budget, even if
            // the previous one gave up without ever firing.
            state.Armed = true;
            state.AttemptsMade = 0;
        }
        else if (phase == ZombieWavePolicy.Phase.Wave)
        {
            // Covers both "a new wave just started" (the normal end of an
            // intermission) and "still in a wave" (every call before the
            // first intermission ever begins) — nothing is pending either
            // way until the NEXT intermission entry.
            state.Armed = false;
            state.AttemptsMade = 0;
        }

        int maxAttempts = Math.Max(1, config.MaxAttempts);
        bool shouldAttempt = state.Armed
            && phase == ZombieWavePolicy.Phase.Intermission
            && playerEligible
            && state.AttemptsMade < maxAttempts;

        return new Result { ShouldAttempt = shouldAttempt };
    }

    /// <summary>
    /// Call when the caller's attempt actually STARTED (e.g.
    /// LiveKitRespawn.AtRoundBreak returned true — the swap was dispatched,
    /// whether or not it later succeeds). Resolves this intermission for
    /// good; see this class's own doc comment, "Exactly once, not
    /// started-at-least-once."
    /// </summary>
    internal static void ReportStarted(ref State state) => state.Armed = false;

    /// <summary>
    /// Call when Evaluate returned ShouldAttempt=true but the real attempt
    /// did NOT start (the player became ineligible between Evaluate and the
    /// attempt, or the underlying path refused for some other transient
    /// reason — e.g. another body replacement already in flight). Consumes
    /// one attempt of the bounded budget so a persistently refusing path
    /// cannot retry without end within one intermission; Evaluate will try
    /// again on the next call while attempts remain and the player is
    /// eligible.
    /// </summary>
    internal static void ReportNotStarted(ref State state) => state.AttemptsMade++;
}
