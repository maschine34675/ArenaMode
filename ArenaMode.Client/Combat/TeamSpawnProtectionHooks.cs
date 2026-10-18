using ArenaMode.Modes;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Combat;

internal static class TeamSpawnProtectionHooks
{
    internal static void Enable()
    {
        new ArenaTeamSpawnProtectionHooksShotPatch().Enable();
        new ArenaTeamSpawnProtectionHooksGrenadePatch().Enable();
    }

    internal static void Attacked(IPlayer attacker)
    {
        if (ArenaSession.IsActive && ArenaMatchService.Current?.Mode is TeamDeathmatchMode teams)
            teams.EndBotSpawnProtection(attacker as Player);
    }

    // Native InitiateShot calls this before BallisticsCalculator.Shoot. It covers
    // offline bot shots and underbarrels without ending protection on aiming or
    // merely holding the fire button with an empty chamber.
    private sealed class ArenaTeamSpawnProtectionHooksShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnMakingShot));
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(Player __instance, bool __runOriginal)
        { if (__runOriginal) Attacked(__instance); }
    }

    private sealed class ArenaTeamSpawnProtectionHooksGrenadePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Player.BaseGrenadeHandsController),
            nameof(Player.BaseGrenadeHandsController.CreateAndRegisterGrenade),
            new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
        [PatchPostfix]
        private static void Postfix(Player ____player, Grenade __result, bool __runOriginal)
        { if (__runOriginal && __result != null) Attacked(____player); }
    }
}
