using System;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// Coverless imported maps still have real NavMesh paths. Do not enter native
/// cover-graph fallbacks whose first node cannot exist, or fabricate cover data.
/// Configured only after the existing exact-version PFT contract is accepted.
/// </summary>
internal static class PitFireTeamPortedMapCombat
{
    private static WeakReference _loggedCoverWorld, _loggedPathWorld;

    internal static void Configure(Harmony harmony)
    {
        var cover = typeof(CoverPointMaster).GetMethod(nameof(CoverPointMaster.GetCoverPointMain2),
            new[] { typeof(CoverSearchData) });
        var path = typeof(BotPathFinderCorePoints).GetMethod(nameof(BotPathFinderCorePoints.GoToPosition),
            new[] { typeof(Vector3), typeof(bool), typeof(float), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool) });
        if (cover == null || cover.IsStatic || cover.ReturnType != typeof(CustomNavigationPoint)
            || path == null || path.IsStatic || path.ReturnType != typeof(UnityEngine.AI.NavMeshPathStatus))
            throw new MissingMethodException("PFT coverless-map native navigation contract changed.");
        harmony.Patch(cover, prefix: new HarmonyMethod(typeof(PitFireTeamPortedMapCombat), nameof(CoverPrefix))
            { priority = Priority.Last });
        harmony.Patch(path, prefix: new HarmonyMethod(typeof(PitFireTeamPortedMapCombat), nameof(PathPrefix))
            { priority = Priority.Last });
    }

    private static bool CoverPrefix(bool __runOriginal, CoverPointMaster __instance, CoverSearchData data,
        ref CustomNavigationPoint __result)
    {
        // PFT's utility accepts no result and keeps its own combat decision.
        // Keep ordinary/native enemies' cover queries outside this adapter.
        if (!__runOriginal || data?.Bot is not BotCoverSearchInfo search || !IsScoped(search._owner, out var world)
            || !ReferenceEquals(search._owner.CoverSearchInfo, search)
            || !ReferenceEquals(search._owner.BotsGroup?.CoverPointMaster, __instance)
            || !PitFireTeamCompat.IsTeamMember(search._owner)) return true;
        __result = null;
        LogOnce(ref _loggedCoverWorld, world, "companion cover search returns no cover; authored cover graph is empty");
        return false;
    }

    private static void PathPrefix(bool __runOriginal, BotPathFinderCorePoints __instance,
        ref bool onlyShortTrie, ref bool mustHaveWay)
    {
        if (!__runOriginal || __instance == null || !IsScoped(__instance._owner, out var world)
            || !ReferenceEquals(__instance._covers, __instance._owner.BotsController.CoversData)) return;
        // Native CalculatePathFull runs FindPath first and accepts only an
        // actually complete NavMesh path. Its short mode returns failure before
        // dereferencing a nonexistent cover node. Disable only the subsequent
        // mustHaveWay cover teleport too; no fake success or alternate path.
        // Preserve the separate slowCalcUsingNativeNavMesh branch as supplied by
        // the caller, including its native handling of partial NavMesh paths.
        onlyShortTrie = true;
        mustHaveWay = false;
        LogOnce(ref _loggedPathWorld, world, "native NavMesh paths retained; unavailable cover fallback/teleport disabled");
    }

    private static bool IsScoped(BotOwner bot, out GameWorld world)
    {
        world = null;
        if (!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive || PitFireTeamCompat.IsFinalizing
            || (ArenaSession.ActiveMode != "tactical" && ArenaSession.ActiveMode != "zombie")
            || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame game || game.Status != GameStatus.Started)
            return false;
        world = Singleton<GameWorld>.Instance;
        if (world == null || !ReferenceEquals(game.GameWorld, world) || !ArenaSession.IsExactActiveRaid(game, world)
            || (!string.Equals(world.LocationId, "arena_bay5", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(world.LocationId, "arena_equator", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(world.LocationId, "arena_airpit", StringComparison.OrdinalIgnoreCase))) return false;
        var controller = ((IBotGame)game).BotsController;
        var player = bot?.GetPlayer;
        return bot != null && controller != null && ReferenceEquals(bot.BotsController, controller)
            && player != null && player.IsAI && player.HealthController?.IsAlive == true
            && ReferenceEquals(player.GameWorld, world) && ReferenceEquals(player.AIData?.BotOwner, bot)
            && ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)
            && controller.CoversData?.Points != null && controller.CoversData.Points.Count == 0;
    }

    private static void LogOnce(ref WeakReference logged, GameWorld world, string message)
    {
        if (logged != null && ReferenceEquals(logged.Target, world)) return;
        logged = new WeakReference(world);
        Plugin.ModLogger.LogDebug($"Arena PFT coverless map {world.LocationId}: {message}.");
    }
}
