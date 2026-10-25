using System;
using System.Reflection;
using ArenaMode.Shared;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.Interactive.SecretExfiltrations;
using HarmonyLib;
using Redeploy.Core.GameAccess;

namespace ArenaMode.Session;

/// <summary>
/// Arena ends through its match rules or the native Leave action, never by
/// standing in a map extraction. No exit object, profile or global status is
/// changed. The native extraction-only completion callback covers shared
/// timers that can finish without calling Proceed for the local player again.
/// </summary>
internal static class ArenaExtractionIsolation
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ExtractionIsolation");
    private static bool _enabled, _reported;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            foreach (var type in new[] { typeof(ExfiltrationPoint), typeof(SecretExfiltrationPoint) })
            {
                var method = AccessTools.DeclaredMethod(type, nameof(ExfiltrationPoint.Proceed),
                    new[] { typeof(Player), typeof(bool) });
                if (method == null || method.ReturnType != typeof(void))
                    throw new MissingMethodException(type.FullName, "Proceed(Player, bool)");
                Patches.Patch(method, prefix: new HarmonyMethod(typeof(ArenaExtractionIsolation), nameof(BeforeProceed)));
            }

            // This explicit interface implementation is called only by the
            // extraction scenario. Never patch general LocalGame.Stop: Leave,
            // match results, failed starts and native death must keep working.
            var callback = typeof(EndByExitTrigerScenario.IGame).GetMethod("StopSession",
                new[] { typeof(string), typeof(ExitStatus), typeof(string) });
            var map = typeof(BaseLocalGame<EftGamePlayerOwner>).GetInterfaceMap(typeof(EndByExitTrigerScenario.IGame));
            int index = Array.IndexOf(map.InterfaceMethods, callback);
            if (callback == null || index < 0 || map.TargetMethods[index].ReturnType != typeof(void))
                throw new MissingMethodException("Native extraction-only StopSession contract changed.");
            Patches.Patch(map.TargetMethods[index],
                prefix: new HarmonyMethod(typeof(ArenaExtractionIsolation), nameof(BeforeNativeExtractionStop)));
            _enabled = true;
        }
        catch (Exception ex)
        {
            Shutdown();
            Plugin.ModLogger.LogError("Arena extraction isolation unavailable: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        Patches.UnpatchSelf();
    }

    internal static bool BeforeProceed(ExfiltrationPoint __instance, Player player)
    {
        if (!_enabled || (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)) return true;
        try
        {
            return !TryCurrentLocal(out _, out var world, out var local)
                || !ReferenceEquals(player, local) || !RegisteredPoint(world, __instance);
        }
        catch (Exception ex) { Report(ex); return true; }
    }

    internal static bool BeforeNativeExtractionStop(object __instance,
        string __0, ExitStatus __1, string __2)
    {
        if (!_enabled || (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
            || (__1 != ExitStatus.Survived && __1 != ExitStatus.Runner)) return true;
        try
        {
            // An individual escape already queued by native code does not
            // recheck IsAlive. Keep it blocked during death/spectating and
            // the interval with no published replacement body as well.
            return !TryCurrentRaid(out var game, out var world)
                || !ReferenceEquals(__instance, game) || __0 != game.Profile.Id
                || !RegisteredExitName(world, __2);
        }
        catch (Exception ex) { Report(ex); return true; }
    }

    private static bool TryCurrentLocal(out LocalGame game, out GameWorld world, out Player local)
    {
        local = null;
        if (!TryCurrentRaid(out game, out world)) return false;
        local = world.MainPlayer;
        return local != null && !local.IsAI && local.HealthController?.IsAlive == true
            && local.ProfileId == game.Profile.Id
            && ReferenceEquals(world.GetAlivePlayerByProfileID(local.ProfileId), local);
    }

    private static bool TryCurrentRaid(out LocalGame game, out GameWorld world)
    {
        game = null; world = null;
        if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated) return false;
        game = Singleton<AbstractGame>.Instance as LocalGame;
        world = Singleton<GameWorld>.Instance;
        if (game == null || world == null || string.IsNullOrEmpty(game.Profile?.Id)
            || !ReferenceEquals(game.GameWorld, world)) return false;
        if (ArenaSession.IsActive)
        {
            // Native extraction remains live during the timer's SoftStopping
            // grace period. Stopping/Stopped, unlike that grace period, own
            // actual teardown and must pass through unchanged.
            if ((game.Status != GameStatus.Started && game.Status != GameStatus.SoftStopping)
                || !ArenaSession.IsExactActiveRaid(game, world)) return false;
        }
        else
        {
            // A local body can meet an exit before OnGameStarted publishes
            // IsActive. Only the acknowledged arm's native loading request may
            // use this exception; menu selection or a same-named map is not proof.
            if (!ArenaSession.ArmedForNextRaid || string.IsNullOrEmpty(ArenaSession.ArmedMode)
                || (game.Status != GameStatus.Running && game.Status != GameStatus.Starting && game.Status != GameStatus.Started)
                || !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)) return false;
            string token = ArenaServerClient.PendingArmToken;
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            if (!Protocol.IsLifecycleRequestId(token) || settings == null
                || !string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
                || (!Protocol.IsNativeStartIdentity(settings.serverId, token)
                    && !Protocol.HasNativeEndIdentity(settings.serverId, token))) return false;
        }

        return true;
    }

    private static bool RegisteredPoint(GameWorld world, ExfiltrationPoint point)
    {
        if (point == null) return false;
        var controller = world.ExfiltrationController;
        return controller != null && (Contains(controller.ExfiltrationPoints, point, null)
            || Contains(controller.ScavExfiltrationPoints, point, null)
            || Contains(controller.SecretExfiltrationPoints, point, null));
    }

    private static bool RegisteredExitName(GameWorld world, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var controller = world.ExfiltrationController;
        return controller != null && (Contains(controller.ExfiltrationPoints, null, name)
            || Contains(controller.ScavExfiltrationPoints, null, name)
            || Contains(controller.SecretExfiltrationPoints, null, name));
    }

    private static bool Contains(ExfiltrationPoint[] points, ExfiltrationPoint point, string name)
    {
        if (points == null) return false;
        foreach (var candidate in points)
            if (candidate != null && (point != null ? ReferenceEquals(candidate, point)
                : string.Equals(candidate.Settings?.Name, name, StringComparison.Ordinal))) return true;
        return false;
    }

    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        Plugin.ModLogger.LogWarning("Arena extraction ownership could not be verified; native behavior retained: "
            + ex.GetBaseException().Message);
    }
}
