using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Client-side bot behavior for arena sessions. All patches lead with the
/// IsActive gate — normal raids are bit-identical.
/// </summary>
internal static class ArenaBotPatches
{
    public static void Enable()
    {
        new ArenaIsPlayerEnemyPatch().Enable();
        new ArenaBossAttackPatch().Enable();
        new ArenaBotCapPatch().Enable();
    }

    /// <summary>
    /// Every bot group treats the human player as an enemy, regardless of what
    /// generation-time hostility configs (SVM, faction mods) decided.
    /// </summary>
    private sealed class ArenaIsPlayerEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.IsPlayerEnemy));

        [PatchPostfix]
        private static void Postfix(IPlayer player, ref bool __result)
        {
            if (!ArenaSession.IsActive || player == null || player.IsAI)
                return;

            __result = true;
        }
    }

    /// <summary>
    /// Bosses skip their warn phase and attack immediately — nobody gets escorted
    /// off Tagilla's turf in an arena. (ShallBossAttack==true means attack; the
    /// warn flow only runs on false.)
    /// </summary>
    private sealed class ArenaBossAttackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotGroupWarnData), nameof(BotGroupWarnData.ShallBossAttack));

        [PatchPostfix]
        private static void Postfix(ref bool __result)
        {
            if (!ArenaSession.IsActive)
                return;

            __result = true;
        }
    }

    /// <summary>
    /// The alive-bot ceiling. HarmonyPriority.Low so this runs AFTER other mods'
    /// SetSettings postfixes (BotPlacementSystem overwrites _maxCount with its own
    /// per-map F12 value) — last writer wins, and in arena that must be us.
    /// </summary>
    private sealed class ArenaBotCapPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsController), nameof(BotsController.SetSettings));

        [PatchPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(BotsController __instance)
        {
            // SetSettings fires during raid INIT, before OnGameStarted latches
            // IsActive — the armed-for-next-raid flag is the correct gate here.
            if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
                return;

            var cap = ArenaClientConfig.GetBotCap(ArenaSession.ArmedMap ?? "factory4_day");
            __instance._maxCount = cap;
            __instance.BotSpawner?.SetMaxBots(cap);
            __instance.ZonesLeaveController?.SetMaxBots(cap);
            Plugin.ModLogger.LogInfo($"Arena bot cap set to {cap}.");
        }
    }
}
