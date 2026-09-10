using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Reflection;
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
/// passes over the queue for the slack that is left, up to LoopTicks (half
/// the budget). Once the frame is over budget it makes exactly one pass and
/// stops. A polling step re-queues itself, so in that regime every chain
/// moves one step per frame and loading is bound by frames, not by the CPU.
/// A machine that cannot hold its framerate limit is in that regime
/// permanently.
///
/// That is the disproportion in the logs. On the machine that ran the raid
/// at 105-116 fps, a bot cost 0.4-2 s after the first pair and the whole kit
/// catalogue warmed in 19 s; on the one at 60 fps, whose plugin load and map
/// load were only 1.7-1.8x slower, the same bot cost 13-54 s and the same
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
/// What the boost cannot do: extra passes only help steps that are CPU work.
/// A step that waits on an asynchronous operation costs at least one frame
/// whatever the multiplier, because the engine integrates such an operation
/// in a per-frame step. Measured per kit template (consecutive preload lines
/// over the template count): the 60 fps machine went from 10-206 frames per
/// template unboosted to 3-26 with the default, median 8; the machine at
/// 100+ fps sits at 4-14 either way. Boosted, the slower machine still needs
/// one to two times the frames of the fast one for the same kit, so the
/// residual is not purely frame-bound; it is frames × waits plus a slower CPU.
///
/// The DELAY adapts, the multiplier does not. Whether a machine is over
/// budget is read from the scheduler's own moving average of its budget
/// window (see BotLoadBoostPolicy.IsOverBudget), latched per episode — over
/// budget the boost comes on after a quarter second, under budget it waits
/// the configured delay. One shared config, two machines. It bought no
/// measurable speed-up and the reason is argued at OverBudgetDelaySeconds;
/// what it is unarguably good for is telling the two regimes apart in the log:
/// the 60 fps machine reads 12-30 ms against its 8 ms budget and is always
/// over, the 116 fps one reads 5.9-8.5 ms and tips over only when a frame is
/// full.
///
/// There is a second lever, it ships OFF, and the log has now answered why.
/// Unity integrates finished asynchronous loads in a per-frame step whose
/// time slice is Application.backgroundLoadingPriority, and the game's scene
/// streamer drops that to Low — two milliseconds a frame — at the start of
/// every scene load and never puts it back. That was the case for raising it.
/// But Streamer is a component a MAP carries, and the on-line reports the
/// slice it finds: BelowNormal on BOTH machines, on Customs, which is Unity's
/// own default. No streamer, no Low, nothing to lift. The earlier plan to
/// default this to BelowNormal would have been a pure no-op on the very maps
/// it was written for. It stays wired for a map that does stream, driven
/// every tick so that streamer cannot take it back mid-hold, and defaulted to
/// Game. Raising it is real main-thread time on top of the step budget:
/// Normal is eight milliseconds more per frame than Low, High forty-eight.
///
/// Arena-only, and not the game's toggle: if force mode is already on when the
/// boost wants it, the game owns it and this service touches nothing.
/// </summary>
internal static class BotLoadBoost
{
    /// <summary>
    /// JobScheduler's private moving average of its budget window, in Stopwatch
    /// ticks. A hundred frames — the field's own size; ApplicationConfig has an
    /// AvgTimeBufferSize of 3600 that nothing applies — so it follows the raid
    /// within a second or two. The frames it starts a raid with are the deploy
    /// countdown's, under the game's own force mode; whether those read over or
    /// under decides only the first episode's delay, and the on-line prints
    /// what it saw.
    ///
    /// The type is spelled out on purpose. Assembly-CSharp has a second
    /// MovingAverage in the global namespace, and a bare name resolves to that
    /// one before any using-directive: the first build of this read compiled,
    /// deployed, and matched nothing — the window read zero and the delay
    /// never adapted. Found in the IL by the review, not by any test.
    /// </summary>
    private static readonly FieldInfo AvgFrameTimeField = AccessTools.Field(typeof(JobScheduler), "_avgFrameTime");

    private static BotLoadBoostPolicy.State _state = BotLoadBoostPolicy.State.Idle;
    private static bool _forcedByUs;
    private static float _heldSince;
    private static bool _loggedFailure;
    private static bool _loggedTrip;
    private static bool _loggedWindowTypeMismatch;
    private static bool _overBudget;
    private static double _windowTicks;
    private static long _frameTicks;
    private static bool _holdsAsyncPriority;
    private static ThreadPriority _previousAsyncPriority;
    private static ThreadPriority _writtenAsyncPriority;

    public static void Reset()
    {
        Release("session reset");
        DriveAsyncPriority(held: false);
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

            bool enabled = active && Settings.BotLoadBoost.Value;

            // Read only where it can matter; a normal raid does no arena work.
            if (enabled)
            {
                ReadBudgetWindow(out _windowTicks, out _frameTicks);
                _overBudget = BotLoadBoostPolicy.IsOverBudget(_windowTicks, _frameTicks);
            }

            bool wanted = BotLoadBoostPolicy.Wanted(
                ref _state,
                enabled,
                loading,
                Time.realtimeSinceStartup,
                Settings.BotLoadBoostDelaySeconds.Value,
                _overBudget,
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

            // Every frame, not once at Acquire. The setting can change under a
            // hold that lasts a minute; on a streamed map the game writes Low
            // at the start of every scene load, which a one-shot raise would
            // lose for the rest of the hold in silence; and driving it off the
            // live hold means the reconcile branch above cannot strand it.
            DriveAsyncPriority(_forcedByUs);
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
    /// signature is those two folded together with the preloader's count of
    /// finished loads; the policy only asks whether it changed.
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
        // The kit part carries the preloader's completed-load count as well:
        // a single flag would sit still for the whole of a long warm-up and
        // read as a stuck counter after ninety seconds, releasing the boost
        // from a load that is genuinely progressing.
        signature = unchecked(bundles + (kits ? 1 << 16 : 0) + (Kits.KitBundlePreloader.LoadsCompleted << 20));
    }

    /// <summary>
    /// The scheduler's average budget window and its budget, both in the raw
    /// ticks it compares itself. Zero for either means "unknown", which the
    /// policy reads as under budget — the configured delay, today's behaviour.
    /// </summary>
    private static void ReadBudgetWindow(out double windowTicks, out long frameTicks)
    {
        windowTicks = 0;
        frameTicks = 0;

        if (!Singleton<JobScheduler>.Instantiated)
            return;

        frameTicks = Singleton<JobScheduler>.Instance.FrameTicks;

        // The field is replaced whenever AvgTimeBufferSize is set, so it is
        // read every time rather than cached once.
        object value = AvgFrameTimeField?.GetValue(null);
        if (value is Diz.Jobs.MovingAverage average)
        {
            windowTicks = average.Average;
        }
        else if (value != null && !_loggedWindowTypeMismatch)
        {
            // Loud once, so a renamed or retyped field reads as a warning and
            // not as a machine that is permanently under budget.
            _loggedWindowTypeMismatch = true;
            Plugin.ModLogger.LogWarning(
                $"Arena load boost: JobScheduler._avgFrameTime is a {value.GetType().FullName}, not Diz.Jobs.MovingAverage; the delay cannot adapt this session.");
        }
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

        // Both machines' logs then show the decision and what it rested on.
        double windowMs = _windowTicks * 1000.0 / Stopwatch.Frequency;
        double budgetMs = _frameTicks / (double)TimeSpan.TicksPerMillisecond;
        string verdict = _state.OverBudget ? "over" : "under";
        string window = _windowTicks > 0
            ? FormattableString.Invariant($"frame window {windowMs:F1} ms vs {budgetMs:F1} ms budget, {verdict}")
            : "frame window unknown";

        // The OBSERVED slice, always, because that is the open question:
        // nothing has yet shown an arena map to run at Low. A bare "untouched"
        // would not say whether the setting is off or the value already
        // matched, and those are the two answers worth telling apart.
        var observed = Application.backgroundLoadingPriority;
        var asked = Settings.BotLoadBoostAsyncPriority.Value;
        string slice = asked == AsyncLoadPriority.Game
            ? $"async slice {observed}, left alone"
            : $"async slice {observed} -> {asked}";

        float sinceLoading = Time.realtimeSinceStartup - _state.LoadingSince;

        Plugin.ModLogger.LogInfo(FormattableString.Invariant(
            $"Arena load boost: on after {sinceLoading:F1}s of loading ({window}; step budget x{budget:F2} per frame; {slice})."));
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

    /// <summary>
    /// Holds the engine's per-frame slice for integrating asynchronous loads
    /// at the configured level while the boost is held, and hands it back.
    ///
    /// Off by default (AsyncLoadPriority.Game), because the case for it is
    /// unproven: the slice only decides how many frames ONE wait needs, and
    /// only when a single integration overruns it — which nothing here has
    /// measured. The floor of one frame per wait comes from integration being
    /// a per-frame step and stands at any slice.
    ///
    /// It is process-global and it is shared. Everything that awaits an
    /// AsyncOperation reads it: the game's own scene streamer and chunk
    /// activation, and any other plugin's loading. Stomping it for the length
    /// of a hold is defensible only because a hold is bounded and because the
    /// value is handed back — and handed back only if it is still the value
    /// this wrote, so a streamer that chose Low mid-hold keeps its choice.
    ///
    /// Driven every tick rather than raised once, because that streamer writes
    /// Low at the start of every scene load: a one-shot raise would be taken
    /// back by the first load and stay off for the rest of a minute-long hold.
    /// </summary>
    private static void DriveAsyncPriority(bool held)
    {
        var wanted = held ? Settings.BotLoadBoostAsyncPriority.Value : AsyncLoadPriority.Game;

        if (wanted == AsyncLoadPriority.Game)
        {
            RestoreAsyncPriority();
            return;
        }

        var target = wanted switch
        {
            AsyncLoadPriority.Low => ThreadPriority.Low,
            AsyncLoadPriority.BelowNormal => ThreadPriority.BelowNormal,
            AsyncLoadPriority.High => ThreadPriority.High,
            _ => ThreadPriority.Normal,
        };

        var current = Application.backgroundLoadingPriority;

        if (!_holdsAsyncPriority)
        {
            // Nothing to write also means nothing to hand back later.
            if (current == target)
                return;

            _previousAsyncPriority = current;
            _holdsAsyncPriority = true;
        }
        else if (current == target)
        {
            return;
        }

        Application.backgroundLoadingPriority = target;
        _writtenAsyncPriority = target;
    }

    private static void RestoreAsyncPriority()
    {
        if (!_holdsAsyncPriority)
            return;

        _holdsAsyncPriority = false;

        // Only ours goes back. If something else chose a value since — the
        // streamer starting a scene load is the case that exists — that choice
        // is more current than the one captured when this took over.
        if (Application.backgroundLoadingPriority == _writtenAsyncPriority)
            Application.backgroundLoadingPriority = _previousAsyncPriority;
    }
}
