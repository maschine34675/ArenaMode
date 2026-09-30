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
/// ProceedBarb clears LastAggressor before the lethal HP change, so the normal
/// OnBeenKilledByAggressor capture never runs. Publish a HUD notice, not a kill.
/// </summary>
internal sealed class ArenaBarbedDeathPatch : ModulePatch
{
    private sealed class Reported { }
    private static ConditionalWeakTable<Player, Reported> _seen = new();
    internal static void Reset() => _seen = new();

    protected override MethodBase GetTargetMethod()
        => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnDead), new[] { typeof(EDamageType) });

    [PatchPrefix]
    internal static void Prefix(EDamageType damageType, out ArenaMatch __state)
    {
        __state = ArenaSession.IsActive && damageType == EDamageType.Barbed
            ? ArenaMatchService.Current : null;
        // A native death subscriber may end the match before our postfix.
        if (__state?.Mode.IsComplete == true) __state = null;
    }

    [PatchPostfix]
    internal static void Postfix(Player __instance, EDamageType damageType, ArenaMatch __state, IPlayer ___LastAggressor)
    {
        if (!ArenaSession.IsActive || damageType != EDamageType.Barbed || __state == null
            || !ReferenceEquals(__state, ArenaMatchService.Current)) return;
        try
        {
            if (__instance == null || __instance.HealthController?.IsAlive != false
                || ___LastAggressor != null || string.IsNullOrEmpty(__instance.ProfileId)
                || ArenaAdministrativeDeathGate.IsActive(__instance.ProfileId)
                || _seen.TryGetValue(__instance, out _)) return;
            // Separate weak identity: never consume the combat scoring dedupe.
            _seen.Add(__instance, new Reported());
            ArenaHud.ShowBarbedDeath(__state, new ArenaBarbedDeath
            {
                VictimProfileId = __instance.ProfileId,
                VictimName = RoleClassifier.DisplayName(__instance),
                VictimRoleLabel = RoleClassifier.Label(__instance),
                LocalIsVictim = __instance.IsYourPlayer,
            });
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena barbed-wire death display failed: {ex.Message}");
        }
    }
}
