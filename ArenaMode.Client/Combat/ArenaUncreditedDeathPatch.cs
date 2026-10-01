using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat;

/// <summary>
/// Null/self aggressors do not produce a credited ArenaKill. Local deaths and
/// existing uncredited wire deaths get a HUD notice, never a scoring event.
/// </summary>
internal sealed class ArenaUncreditedDeathPatch : ModulePatch
{
    private sealed class Reported { }
    private static ConditionalWeakTable<Player, Reported> _seen = new();
    internal static void Reset() => _seen = new();

    protected override MethodBase GetTargetMethod()
        => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnDead), new[] { typeof(EDamageType) });

    [PatchPrefix]
    internal static void Prefix(EDamageType damageType, out ArenaMatch __state)
    {
        __state = ArenaSession.IsActive ? ArenaMatchService.Current : null;
        // A native death subscriber may end the match before our postfix.
        if (__state?.Mode.IsComplete == true) __state = null;
    }

    [PatchPostfix]
    internal static void Postfix(Player __instance, EDamageType damageType, ArenaMatch __state, IPlayer ___LastAggressor)
    {
        if (!ArenaSession.IsActive || __state == null || !ReferenceEquals(__state, ArenaMatchService.Current)) return;
        try
        {
            if (__instance == null || (!__instance.IsYourPlayer && damageType != EDamageType.Barbed)
                || ___LastAggressor != null && (!__instance.IsYourPlayer || ___LastAggressor.ProfileId != __instance.ProfileId)) return;
            Publish(__instance, __state, damageType.ToString());
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena uncredited death display failed: {ex.Message}"); }
    }

    // Called only after GiveUp's synchronous Kill has proved successful and its
    // administrative lease ended. Do not weaken the retirement guard for OnDead.
    internal static void ReportGiveUp(Player player, ArenaMatch match)
    {
        try { if (player != null && player.IsYourPlayer) Publish(player, match, "GiveUp"); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena give-up death display failed: {ex.Message}"); }
    }

    private static void Publish(Player player, ArenaMatch match, string damageKind)
    {
        if (!ArenaSession.IsActive || match == null || !ReferenceEquals(match, ArenaMatchService.Current)
            || player.HealthController?.IsAlive != false || string.IsNullOrEmpty(player.ProfileId)
            || ArenaAdministrativeDeathGate.IsActive(player.ProfileId)
            || _seen.TryGetValue(player, out _)) return;
        // Separate weak identity: never consume the combat scoring dedupe.
        _seen.Add(player, new Reported());
        ArenaHud.ShowUncreditedDeath(match, new ArenaUncreditedDeath
        {
            VictimProfileId = player.ProfileId,
            VictimName = RoleClassifier.DisplayName(player),
            VictimRoleLabel = RoleClassifier.Label(player),
            LocalIsVictim = player.IsYourPlayer,
            DamageKind = damageKind,
        });
    }
}
