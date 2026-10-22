using System;
using System.Reflection;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// PFT resolves spawn core metadata through the nearest cover. Bay5/Equator/AirPit/Bowl
/// have real core points but no baked covers. Resolve the existing core directly
/// in the exact protected Arena raid; do not invent a cover or move any spawn.
/// </summary>
internal static class PitFireTeamPortedMapSpawns
{
    private const string BotsPatch = "pitTeam.Patches.BotsControllerPatch";
    private static WeakReference _loggedController;

    internal static void Configure(Assembly assembly, string pluginVersion, Harmony harmony)
    {
        if (assembly == null || !PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)
            || !PitFireTeamVersions.IsSupportedAssemblyVersion(assembly.GetName().Version)
            || !PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assembly.GetName().Version))
            throw new InvalidOperationException("PFT ported-map spawn adapter has an unsupported version.");
        var target = assembly.GetType(BotsPatch, true).GetMethod("GetClosestCorePoint",
            BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(BotsController), typeof(Vector3) }, null);
        if (target == null || target.ReturnType != typeof(AICorePoint) || target.IsGenericMethod)
            throw new MissingMethodException(BotsPatch, "GetClosestCorePoint(BotsController, Vector3)");
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(PitFireTeamPortedMapSpawns), nameof(Prefix)));
    }

    private static bool Prefix(BotsController __0, Vector3 __1, ref AICorePoint __result)
    {
        if (!TryResolve(__0, __1, out var point)) return true;
        __result = point;
        if (_loggedController == null || !ReferenceEquals(_loggedController.Target, __0))
        {
            _loggedController = new WeakReference(__0);
            Plugin.ModLogger.LogDebug($"Arena PFT ported-map spawn: map={Singleton<GameWorld>.Instance.LocationId}, "
                + $"no baked covers; resolved existing core {point.Id} for companion activation.");
        }
        return false;
    }

    internal static bool TryResolve(BotsController controller, Vector3 position, out AICorePoint point)
    {
        point = null;
        if (!PitFireTeamCompat.TeamArenaActive || PitFireTeamCompat.IsFinalizing
            || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame game) return false;
        var world = Singleton<GameWorld>.Instance;
        if (world == null || !ReferenceEquals(game.GameWorld, world)
            || controller == null || !ReferenceEquals(((IBotGame)game).BotsController, controller)
            || world.MainPlayer == null || !world.MainPlayer.IsYourPlayer
            || world.MainPlayer.ProfileId != game.Profile?.Id) return false;
        string map = world.LocationId;
        if (!string.Equals(map, "arena_bay5", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(map, "arena_equator", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(map, "arena_airpit", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(map, "arena_bowl", StringComparison.OrdinalIgnoreCase)) return false;
        string mode;
        if (ArenaSession.IsActive)
        {
            if (!ArenaSession.IsExactActiveRaid(game, world) || game.Status != GameStatus.Started) return false;
            mode = ArenaSession.ActiveMode;
        }
        else
        {
            // PFT's first SpawnGroupBots runs during LocalGame.vmethod_1,
            // before OnGameStarted. Require the server-acknowledged same-map arm.
            if (!ArenaSession.ArmedForNextRaid
                || !string.Equals(ArenaSession.ArmedMap, map, StringComparison.OrdinalIgnoreCase)
                || game.Status != GameStatus.Running) return false;
            mode = ArenaSession.ArmedMode;
        }
        if (mode != "tactical" && mode != "zombie") return false;
        var covers = controller.CoversData;
        if (covers?.Points == null || covers.Points.Count != 0 || !Finite(position)) return false;
        var holder = covers.AICorePointsHolder;
        var points = holder?.CorePoints;
        if (points == null) return false;
        float best = float.PositiveInfinity;
        foreach (var candidate in points)
        {
            if (candidate == null || candidate.Id < 0 || !Finite(candidate.Position)
                || !ReferenceEquals(holder.GetCorePoint(candidate.Id), candidate)) continue;
            float distance = (candidate.Position - position).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            point = candidate;
        }
        return point != null;
    }

    private static bool Finite(Vector3 p)
        => !float.IsNaN(p.x) && !float.IsInfinity(p.x)
            && !float.IsNaN(p.y) && !float.IsInfinity(p.y)
            && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
}
