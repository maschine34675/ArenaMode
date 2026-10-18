#nullable disable
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Performance;

/// <summary>
/// Harmony half of <see cref="ProfileBuildBudgetPolicy"/>. In the exact live
/// Arena raid, <c>ClientBackendSession.LoadBots</c> is replaced by a method
/// that sends the identical request (same URL, parameters and retries as
/// ../_decompiled/EFT/ClientBackendSession.cs:1886-1895) and then constructs
/// the answered profiles one at a time under a per-frame time budget
/// (<c>Settings.ProfileBuildBudgetMs</c>, 0 = native), yielding whenever the
/// frame has had its share. The caller keeps awaiting one task, which
/// completes only with the complete array: the batch is handed over whole,
/// just later than the single frame the native method would have spent on it.
/// Construction stays on the main thread as in vanilla: the request's
/// continuation lands on the main thread (EFT's HTTP layer completes there),
/// and <c>Task.Yield</c> posts to Unity's synchronization context, whose next
/// pass runs on the main thread again. The very first yield can resume later
/// in the SAME frame (Unity runs the posted queue after Update, the HTTP
/// completion arrives before that), every later one resumes in a later frame;
/// the budget is keyed on <c>Time.frameCount</c>, so that does not matter.
///
/// Fairness: every open batch gets one guaranteed build per frame, see
/// <see cref="ProfileBuildBudgetPolicy.MayStart"/>; the open-batch count is
/// kept here.
///
/// Prefix order: <c>Priority.Low</c>, so <c>ArenaBotDifficultyClockPatch</c>'s
/// prefix on the same method runs first and the conditions list it may have
/// rewritten is the one that is sent. If another prefix already replaced the
/// call (<c>__runOriginal</c> false), this one leaves its result alone rather
/// than sending a second request.
///
/// Fail-open covers the gate: any exception while deciding returns to the
/// native method. Once the replacement task runs, it faults where the native
/// one would (a request failure, a null answer, a throwing constructor); it
/// adds no catch of its own. Gate: the exact started Arena raid
/// (<c>ArenaSession.IsExactActiveRaid</c>), not the bare <c>IsActive</c>
/// latch, so a latch left over from a raid that missed its GameEnd cannot
/// reach the next ordinary raid's loading-screen preload. The Zombie opening
/// preload runs in the loading screen before any latch and stays native.
/// </summary>
internal static class ProfileBuildBudget
{
    private static bool _enabled, _warned;
    private static ProfileBuildBudgetPolicy.State _state;
    private static int _activeBatches;

    internal static void Initialize()
    {
        if (_enabled) return;
        try { new ArenaProfileBudgetLoadBotsPatch().Enable(); _enabled = true; }
        catch (Exception ex) { Warn(ex); }
    }

    /// <summary>True when the native method must still run (or another prefix already replaced it).</summary>
    internal static bool RunNative(ClientBackendSession session, List<CountTypeBotWave> conditions, bool runOriginal, ref Task<Profile[]> result)
    {
        try
        {
            if (!runOriginal)
                return false;
            float budgetMs = Settings.ProfileBuildBudgetMs?.Value ?? 0f;
            if (!ProfileBuildBudgetPolicy.ShouldTakeOver(Settings.ProfileBuildBudgetMs != null, budgetMs,
                    ExactArenaRaid(), SynchronizationContext.Current != null, conditions?.Count ?? 0))
                return true;
            if (session == null || session._backendUrls == null)
                return true;
            result = LoadUnderBudget(session, conditions, budgetMs);
            return false;
        }
        catch (Exception ex)
        {
            Warn(ex);
            return true;
        }
    }

    private static bool ExactArenaRaid()
    {
        if (!ArenaSession.IsActive || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
            return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance;
        return world != null && game != null && ArenaSession.IsExactActiveRaid(game, world);
    }

    private static async Task<Profile[]> LoadUnderBudget(ClientBackendSession session, List<CountTypeBotWave> conditions, float budgetMs)
    {
        // The native request, field for field.
        var descriptors = await session.Send<ProfileDescriptor[]>(new SendRequest
        {
            Url = session._backendUrls.Main + "/client/game/bot/generate",
            Params = new BotGenerateRequestParams<List<CountTypeBotWave>>(conditions),
            Retries = SendRequest.DefaultRetries,
        });
        // Native: Enumerable.Select over a null answer throws ArgumentNullException("source").
        if (descriptors == null)
            throw new ArgumentNullException("source", "bot/generate answered without descriptors");

        var profiles = new Profile[descriptors.Length];
        long budgetTicks = ProfileBuildBudgetPolicy.BudgetTicks(budgetMs, Stopwatch.Frequency);
        int frames = 0, lastBuiltFrame = -1;
        long totalTicks = 0, frameTicks = 0, longestFrameTicks = 0;
        long started = Stopwatch.GetTimestamp();
        _activeBatches++;
        try
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                while (!ProfileBuildBudgetPolicy.MayStart(ref _state, Time.frameCount, budgetTicks, _activeBatches,
                           lastBuiltFrame == Time.frameCount))
                    await Task.Yield();
                int frame = Time.frameCount;
                if (frame != lastBuiltFrame)
                {
                    frames++;
                    lastBuiltFrame = frame;
                    frameTicks = 0;
                }
                long t0 = Stopwatch.GetTimestamp();
                profiles[i] = new Profile(descriptors[i]);
                long took = Stopwatch.GetTimestamp() - t0;
                ProfileBuildBudgetPolicy.Record(ref _state, took);
                totalTicks += took;
                frameTicks += took;
                if (frameTicks > longestFrameTicks) longestFrameTicks = frameTicks;
            }
        }
        finally
        {
            _activeBatches--;
        }
        double toMs = 1000.0 / Stopwatch.Frequency;
        Plugin.ModLogger.LogDebug(
            $"Arena profile build budget: {profiles.Length} bot profiles built over {frames} frame(s), "
            + $"{totalTicks * toMs:0.0} ms of construction, longest frame share {longestFrameTicks * toMs:0.0} ms "
            + $"(budget {budgetMs:0.#} ms, one build per open batch per frame guaranteed), "
            + $"{(Stopwatch.GetTimestamp() - started) * toMs:0} ms wall from answer to completion.");
        return profiles;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena profile build budget: unavailable for this call; native profile loading retained. " + ex.GetBaseException().Message);
    }

    private sealed class ArenaProfileBudgetLoadBotsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ClientBackendSession), nameof(ClientBackendSession.LoadBots),
                new[] { typeof(List<CountTypeBotWave>) });

        // Low priority: the difficulty clock's prefix rewrites `conditions` first.
        [PatchPrefix, HarmonyPriority(Priority.Low)]
        private static bool Prefix(ClientBackendSession __instance, List<CountTypeBotWave> conditions, bool __runOriginal, ref Task<Profile[]> __result)
            => RunNative(__instance, conditions, __runOriginal, ref __result);
    }
}
