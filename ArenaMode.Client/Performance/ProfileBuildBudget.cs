#nullable disable
using ArenaMode.Config;
using ArenaMode.Session;
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
/// Harmony half of <see cref="ProfileBuildBudgetPolicy"/>. In an active Arena
/// raid, <c>ClientBackendSession.LoadBots</c> is replaced by a method that
/// sends the identical request (same URL, parameters and retries as
/// ../_decompiled/EFT/ClientBackendSession.cs:1886-1895) and then builds the
/// answered profiles one at a time under a per-frame time budget
/// (<c>Settings.ProfileBuildBudgetMs</c>, 0 = native), yielding to the next
/// frame whenever the budget is spent. The caller still awaits one task that
/// completes with the whole array, so nothing in the game sees a profile
/// before its batch is complete; to the game this is the server answering a
/// few frames later. The same main thread builds the profiles as in vanilla
/// (a <c>Task.Yield</c> on Unity's synchronization context resumes in the next
/// frame), so no item code runs off-thread.
///
/// Runs AFTER <c>ArenaBotDifficultyClockPatch</c>'s own prefix on the same
/// method (<c>Priority.Low</c>), so the conditions list it may have rewritten
/// is the one that is sent. Fails open: any exception before the request is
/// sent returns to the native method; after the request, the task faults
/// exactly where the native one would (a null answer, a throwing
/// constructor). Gate: <c>ArenaSession.IsActive</c>; the loading-screen
/// preload runs before it latches and stays native by design.
/// </summary>
internal static class ProfileBuildBudget
{
    private static bool _enabled, _warned;
    private static ProfileBuildBudgetPolicy.State _state;

    internal static void Initialize()
    {
        if (_enabled) return;
        try { new LoadBotsPatch().Enable(); _enabled = true; }
        catch (Exception ex) { Warn(ex); }
    }

    /// <summary>True when the native method must still run.</summary>
    internal static bool RunNative(ClientBackendSession session, List<CountTypeBotWave> conditions, ref Task<Profile[]> result)
    {
        try
        {
            float budgetMs = Settings.ProfileBuildBudgetMs?.Value ?? 0f;
            if (!ProfileBuildBudgetPolicy.ShouldTakeOver(Settings.ProfileBuildBudgetMs != null, budgetMs,
                    ArenaSession.IsActive, SynchronizationContext.Current != null, conditions?.Count ?? 0))
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

    private static async Task<Profile[]> LoadUnderBudget(ClientBackendSession session, List<CountTypeBotWave> conditions, float budgetMs)
    {
        // The native request, field for field.
        var descriptors = await session.Send<ProfileDescriptor[]>(new SendRequest
        {
            Url = session._backendUrls.Main + "/client/game/bot/generate",
            Params = new BotGenerateRequestParams<List<CountTypeBotWave>>(conditions),
            Retries = SendRequest.DefaultRetries,
        });
        // Native: (await Send(...)).Select(...) on a null answer throws here too.
        if (descriptors == null)
            throw new NullReferenceException("bot/generate answered without descriptors");

        var profiles = new Profile[descriptors.Length];
        long budgetTicks = ProfileBuildBudgetPolicy.BudgetTicks(budgetMs, Stopwatch.Frequency);
        int firstFrame = Time.frameCount, frames = 1, lastFrame = firstFrame;
        long totalTicks = 0, frameTicks = 0, longestFrameTicks = 0;
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < descriptors.Length; i++)
        {
            while (!ProfileBuildBudgetPolicy.MayStart(ref _state, Time.frameCount, budgetTicks))
                await Task.Yield();
            int frame = Time.frameCount;
            if (frame != lastFrame)
            {
                frames++;
                lastFrame = frame;
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
        double toMs = 1000.0 / Stopwatch.Frequency;
        Plugin.ModLogger.LogDebug(
            $"Arena profile build budget: {profiles.Length} bot profiles built over {frames} frame(s), "
            + $"{totalTicks * toMs:0.0} ms of construction, longest frame share {longestFrameTicks * toMs:0.0} ms "
            + $"(budget {budgetMs:0.#} ms), {(Stopwatch.GetTimestamp() - started) * toMs:0} ms wall from answer to completion.");
        return profiles;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena profile build budget: unavailable for this call; native profile loading retained. " + ex.GetBaseException().Message);
    }

    private sealed class LoadBotsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ClientBackendSession), nameof(ClientBackendSession.LoadBots),
                new[] { typeof(List<CountTypeBotWave>) });

        // Low priority: the difficulty clock's prefix rewrites `conditions` first.
        [PatchPrefix, HarmonyPriority(Priority.Low)]
        private static bool Prefix(ClientBackendSession __instance, List<CountTypeBotWave> conditions, ref Task<Profile[]> __result)
            => RunNative(__instance, conditions, ref __result);
    }
}
