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

    public static void Enable()
    {
        new SpawnZonesPatch().Enable();
        new SelectAiPointsPatch().Enable();
        new DispatchPatch().Enable();
    }

    public static void ResetRaidState() => _rejectedBatches = 0;

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
        private static bool Prefix(List<ISpawnPoint> openedPositions, BotZone botZone)
        {
            if (ArenaBotSpawnFilter.CanDispatch(botZone, openedPositions))
                return true;

            // Forced/direct spawners can bypass native AI point selection. Deny
            // the WHOLE batch before method_7 increments _inSpawnProcess or adds
            // positions to its profiles; partial removal would break accounting.
            // Do not stop or modify BotCreationData: other requests may share it.
            _rejectedBatches++;
            if (_rejectedBatches <= 3 || _rejectedBatches % 25 == 0)
                Plugin.ModLogger.LogWarning($"Arena: rejected Labs gate bot spawn batch #{_rejectedBatches}.");
            return false;
        }
    }
}
