using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps bot and kit loading moving on a machine whose frames are already
/// over the scheduler's budget.
///
/// Every bundle load and pool build in the game is a chain of small steps
/// strung together on JobScheduler.Yield. The scheduler runs those steps in
/// LateUpdate. While the frame is under its budget — 1000 / the framerate
/// limit, so 8 ms at the 120 limit, measured from the end of the previous
/// frame's render, which INCLUDES any VSync or limiter wait — it keeps making
/// passes over the queue until LoopTicks (half the budget) of work is done.
/// Once the frame is over budget it makes exactly one pass and stops. A
/// polling step re-queues itself, so in that regime every chain moves one
/// step per frame and loading is bound by frames, not by the CPU. A machine
/// held at 60 fps under a 120 limit — VSync on a 60 Hz panel is enough — is
/// in that regime permanently.
///
/// That is the disproportion in the logs. On the machine that ran the raid
/// at 105-116 fps, a bot cost 0.4-2 s after the first pair and the whole kit
/// catalogue warmed in 19 s; on the one held at 60 fps, whose plugin load and
/// map load were only 1.7-1.8x slower, the same bot cost 13-54 s and the same
/// warm-up five and a half minutes.
///
/// The game has a switch for this. During matching and level loading it
/// enables the scheduler's force mode, which ignores the frame budget and
/// grants LoopTicks × multiplier of step time per frame; it switches it off
/// once the raid is up. This service turns it back on, briefly, whenever bot
/// bundles or kit pools are loading mid-raid, and off again once they are
/// done. The cost is a longer frame while a bot loads — up to LoopTicks ×
/// multiplier, so 4 ms at the defaults — instead of a bot that arrives a
/// minute after it was asked for.
///
/// Arena-only, and not the game's toggle: if force mode is already on when the
/// boost wants it, the game owns it and this service touches nothing.
/// </summary>
internal static class BotLoadBoost
{
    private static BotLoadBoostPolicy.State _state = BotLoadBoostPolicy.State.Idle;
    private static bool _forcedByUs;
    private static float _heldSince;
    private static bool _loggedFailure;
    private static bool _loggedTrip;

    public static void Reset()
    {
        Release("session reset");
        _state = BotLoadBoostPolicy.State.Idle;
        _loggedFailure = false;
        _loggedTrip = false;
    }

    public static void Tick()
    {
        try
        {
            bool active = ArenaSession.IsActive && Singleton<GameWorld>.Instantiated;
            ReadLoading(active, out bool loading, out int signature);
            bool wasTripped = _state.Tripped;

            bool wanted = BotLoadBoostPolicy.Wanted(
                ref _state,
                active && Settings.BotLoadBoost.Value,
                loading,
                Time.realtimeSinceStartup,
                Settings.BotLoadBoostDelaySeconds.Value,
                signature);

            if (_state.Tripped && !wasTripped && !_loggedTrip)
            {
                _loggedTrip = true;
                Plugin.ModLogger.LogWarning(
                    FormattableString.Invariant($"Arena load boost: the loading counters have not moved for {BotLoadBoostPolicy.MaxHoldSeconds:F0}s; ")
                    + "assuming a stuck counter and standing down until they change or read idle.");
            }

            // The game's own SetForceMode(false) at the end of its loading
            // window does not know about this hold. If it switched the mode
            // off under us, the hold is gone, not ours to "release" later —
            // forget it, and re-acquire below if still wanted.
            if (_forcedByUs && Singleton<JobScheduler>.Instantiated
                && !Singleton<JobScheduler>.Instance.IsForceModeEnabled)
            {
                _forcedByUs = false;
            }

            if (wanted)
                Acquire();
            else
                Release(!loading ? "idle" : _state.Tripped ? "tripped" : "disabled");
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena load boost failed (silenced for this raid): {ex}");
            }
        }
    }

    /// <summary>
    /// Anything that walks the scheduler's loading chains right now: the
    /// bot creator's own bundle counter (bumped around every profile's
    /// LoadBundlesAndCreatePools) and the arena's kit preloader. The
    /// signature is those two numbers folded together; the policy only asks
    /// whether it changed.
    /// </summary>
    private static void ReadLoading(bool active, out bool loading, out int signature)
    {
        loading = false;
        signature = 0;

        if (!active)
            return;

        int bundles = 0;
        if (Singleton<IBotGame>.Instantiated)
        {
            var creator = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner?._botCreator;
            if (creator != null)
                bundles = creator.BundlesLoading;
        }

        bool kits = Kits.KitBundlePreloader.IsPreloading;

        loading = bundles > 0 || kits;
        signature = bundles + (kits ? 1 << 16 : 0);
    }

    private static void Acquire()
    {
        if (_forcedByUs)
            return;

        if (!Singleton<JobScheduler>.Instantiated)
            return;

        var scheduler = Singleton<JobScheduler>.Instance;

        // Already on means the game is loading something of its own under it.
        // Leave that alone; it will be off again before this matters.
        if (scheduler.IsForceModeEnabled)
            return;

        float budget = Mathf.Clamp(Settings.BotLoadBoostBudget.Value, 0.25f, 3f);
        scheduler.SetForceMode(true, budget);
        _forcedByUs = true;
        _heldSince = Time.realtimeSinceStartup;

        Plugin.ModLogger.LogInfo(FormattableString.Invariant(
            $"Arena load boost: on after {Time.realtimeSinceStartup - _state.LoadingSince:F1}s of loading (step budget x{budget:F2} per frame)."));
    }

    private static void Release(string why)
    {
        if (!_forcedByUs)
            return;

        _forcedByUs = false;

        if (Singleton<JobScheduler>.Instantiated)
            Singleton<JobScheduler>.Instance.SetForceMode(false);

        Plugin.ModLogger.LogInfo(FormattableString.Invariant(
            $"Arena load boost: off after {Time.realtimeSinceStartup - _heldSince:F1}s ({why})."));
    }
}
