using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

internal static class ArenaBotSpawnPatches
{
    private static int _rejectedBatches;
    private static int _rejectedTeamBatches;

    public static void Enable()
    {
        new SpawnZonesPatch().Enable();
        new SelectAiPointsPatch().Enable();
        new DispatchPatch().Enable();
        new ArenaTeamSpawnPointsPatch().Enable();
    }

    public static void ResetRaidState()
    {
        _rejectedBatches = 0;
        _rejectedTeamBatches = 0;
    }

    private sealed class SpawnZonesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotSpawner), nameof(BotSpawner.SpawnZones));

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref IEnumerable<BotZone> __result)
            => __result = ArenaBotSpawnFilter.FilterZones(__result);
    }

    private sealed class SelectAiPointsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(SpawnSystem), nameof(SpawnSystem.SelectAISpawnPoints),
                new[] { typeof(BotCreationData), typeof(BotZone), typeof(int), typeof(float),
                    typeof(Vector3?), typeof(ActionIfNotEnoughPoints) });

        [PatchPrefix]
        private static bool Prefix(BotZone zone, ref List<ISpawnPoint> __result)
        {
            if (!ArenaBotSpawnFilter.IsExcludedZone(zone))
                return true;

            // Native callers already handle null as no suitable AI point.
            __result = null;
            return false;
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref List<ISpawnPoint> __result)
            => __result = ArenaBotSpawnFilter.FilterBotPoints(__result);
    }

    private sealed class DispatchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotSpawner), nameof(BotSpawner.SpawnBotsInZoneOnPositions));

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BotSpawner __instance, List<ISpawnPoint> openedPositions,
            BotZone botZone, BotCreationData data)
        {
            if (Areas.ArenaAreaRuntime.ShouldBlockLoadingBotDispatch())
            {
                TeamDeathmatchSpawnDispatch.MarkRejected(__instance, data);
                return false;
            }
            if (!TeamDeathmatchSpawnDispatch.CanDispatch(__instance, data))
            {
                // Deny unowned batches before the native method charges its
                // per-point counter. Never modify another spawner's data.
                _rejectedTeamBatches++;
                if (_rejectedTeamBatches <= 3 || _rejectedTeamBatches % 25 == 0)
                    Plugin.ModLogger.LogWarning(
                        $"Arena team deathmatch: rejected unowned bot spawn batch #{_rejectedTeamBatches}.");
                return false;
            }

            if (ArenaBotSpawnFilter.CanDispatch(botZone, openedPositions))
                return true;

            // Forced/direct spawners can bypass native AI point selection. Deny
            // the WHOLE batch before method_7 increments _inSpawnProcess or adds
            // positions to its profiles; partial removal would break accounting.
            // Do not stop or modify BotCreationData: other requests may share it.
            TeamDeathmatchSpawnDispatch.MarkRejected(__instance, data);
            _rejectedBatches++;
            if (_rejectedBatches <= 3 || _rejectedBatches % 25 == 0)
                Plugin.ModLogger.LogWarning($"Arena: rejected excluded bot spawn batch #{_rejectedBatches}.");
            return false;
        }
    }
}
