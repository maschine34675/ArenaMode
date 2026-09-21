using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArenaMode.Bots;

internal static class ArenaBotSpawnPatches
{
    private static int _rejectedBatches;
    private static int _rejectedTeamBatches;
    internal static bool SoloPopulationReady { get; private set; }

    public static void Enable()
    {
        SoloPopulationReady = false;
        new SpawnZonesPatch().Enable();
        new SelectAiPointsPatch().Enable();
        new DispatchPatch().Enable();
        new SoloActivationPatch().Enable();
        new SoloProfileActivationPatch().Enable();
        new SoloCreationPatch().Enable();
        new ArenaTeamSpawnPointsPatch().Enable();
        SoloPopulationReady = true;
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
                ArenaSoloSpawnDispatch.MarkRejected(__instance, data);
                return false;
            }
            if (!ArenaSoloSpawnDispatch.CanDispatch(__instance, data))
                return false;
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
            ArenaSoloSpawnDispatch.MarkRejected(__instance, data);
            _rejectedBatches++;
            if (_rejectedBatches <= 3 || _rejectedBatches % 25 == 0)
                Plugin.ModLogger.LogWarning($"Arena: rejected excluded bot spawn batch #{_rejectedBatches}.");
            return false;
        }
    }

    // ABPS and other native integrations may bypass BotSpawner dispatch and
    // call the creator directly. All three entry points must carry the same
    // synchronous lease. Native one-profile creation reaches CreateBot before
    // its first await; no global or AsyncLocal permission survives that call.
    private sealed class SoloActivationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotCreatorClient), nameof(BotCreatorClient.ActivateBot),
                new[] { typeof(BotCreationData), typeof(BotZone), typeof(bool), typeof(Func<BotOwner, BotZone, BotsGroup>),
                    typeof(Action<BotOwner>), typeof(CancellationToken) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(BotCreatorClient __instance, BotCreationData data, ref Task __result)
        {
            if (ArenaSoloSpawnDispatch.CanActivate(__instance, data)) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    private sealed class SoloProfileActivationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotCreatorClient), nameof(BotCreatorClient.ActivateBot),
                new[] { typeof(Profile), typeof(PositionNote), typeof(BotZone), typeof(bool), typeof(Func<BotOwner, BotZone, BotsGroup>),
                    typeof(Action<BotOwner>), typeof(CancellationToken) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ref Task __result)
        {
            if (!ArenaSoloSpawnDispatch.IsActive) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    private sealed class SoloCreationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotCreatorClient), nameof(BotCreatorClient.CreateBot),
                new[] { typeof(Profile), typeof(PositionNote), typeof(Action<BotOwner>), typeof(bool), typeof(CancellationToken) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(BotCreatorClient __instance, Profile profile, ref Task __result)
        {
            if (ArenaSoloSpawnDispatch.CanCreate(__instance, profile)) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }
}
