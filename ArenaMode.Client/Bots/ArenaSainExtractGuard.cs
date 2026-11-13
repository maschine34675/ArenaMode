using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Optional SAIN 4.5.1+ guard: no bot leaves an Arena raid through a map exit. SAIN's own
/// extraction (ExtractLayer, then ExtractAction: OnItemTransferred on the exit and
/// LeaveData.RemoveFromMap) needs the preset's SAIN_EXTRACT_TOGGLE, which is off in the shipped
/// and the installed preset; a user can switch it on, and then SAIN also drops BSG's own
/// Exfiltration layer. Since 2026-11-13 the MapPort exits sit in the team start pockets, so such
/// a bot would leave a Team DM or Domination match near its own spawn.
///
/// The one admission both SAIN callers share is the static, side-effect-free
/// BotExtractManager.IsBotAllowedToExfil(BotComponent): ExtractLayer.allowedToExtract (behind
/// the toggle, so this postfix never runs while it is off) and TryFindExfilForBot. During an
/// armed or exact active Arena raid the answer becomes false, for every bot of that raid,
/// PitFireTeam companions included: Arena ends by its match rules, never at an exit. SAIN's own
/// method still runs, no preset value is written, and normal raids keep SAIN's answer.
/// </summary>
internal static class ArenaSainExtractGuard
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SainExtractGuard");
    private static bool _ready, _warned;

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetName().Name == "SAIN");
        if (assembly == null) return; // Soft dependency: no SAIN installation required.
        TryBind(assembly);
    }

    internal static bool TryBind(Assembly assembly)
    {
        try
        {
            ArenaSainVersion.Require(assembly);
            var component = assembly.GetType("SAIN.Components.BotComponent", true);
            var manager = assembly.GetType("SAIN.Components.BotController.BotExtractManager", true);
            var allowed = manager.GetMethod("IsBotAllowedToExfil", BindingFlags.Public | BindingFlags.Static,
                null, new[] { component }, null);
            if (allowed?.ReturnType != typeof(bool))
                throw new InvalidOperationException("SAIN extraction admission contract changed");
            // Lowest priority: postfixes of a higher priority (Harmony's default included) run
            // first and cannot reopen the exit; one at the same lowest priority is not ordered.
            Patches.Patch(allowed, postfix: new HarmonyMethod(typeof(ArenaSainExtractGuard)
                .GetMethod(nameof(AfterAllowed), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.Last });
            _ready = true;
            Plugin.ModLogger.LogDebug($"Arena: optional SAIN {assembly.GetName().Version} extraction guard ready.");
            return true;
        }
        catch (Exception exception)
        {
            Shutdown();
            Warn("SAIN extraction guard unavailable; SAIN's own extraction setting applies", exception);
            return false;
        }
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
    }

    private static void AfterAllowed(ref bool __result)
    {
        try
        {
            if (__result && _ready && InArenaRaid()) __result = false;
        }
        catch (Exception exception) { Warn("SAIN extraction guard failed; SAIN's answer kept", exception); }
    }

    // The personality filter's raid scope: armed for this world's map, or the exact active raid.
    private static bool InArenaRaid()
    {
        if ((!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid) || !Singleton<GameWorld>.Instantiated)
            return false;
        var world = Singleton<GameWorld>.Instance;
        if (string.IsNullOrEmpty(ArenaSession.ArmedMap)
            || !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase))
            return false;
        return ArenaSession.ArmedForNextRaid
            || (Singleton<AbstractGame>.Instantiated
                && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world));
    }

    private static void Warn(string message, Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning($"Arena: {message}: {exception.GetBaseException().Message}");
    }
}
