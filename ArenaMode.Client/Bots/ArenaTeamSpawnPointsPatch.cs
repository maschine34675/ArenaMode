using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// PFT creates followers at the local leader's position, bypassing AI marker
/// selection. Its protected 5v5 leader must therefore avoid the Factory courtyard
/// marker too. Filter only the returned collection, never destroy scene markers
/// or change the shared player categories/SpawnBlocked flag.
/// </summary>
internal sealed class ArenaTeamSpawnPointsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(SpawnPointsCollection), nameof(SpawnPointsCollection.CreateFromScene),
            new[] { typeof(DateTime?), typeof(SpawnPointParams[]) });

    [PatchPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref SpawnPointsCollection __result)
    {
        if (!ArenaBotSpawnFilter.IsPointRestrictedRaid
            || !string.Equals(ArenaSession.ArmedForNextRaid ? ArenaSession.ArmedMode : ArenaSession.ActiveMode,
                "tactical", StringComparison.OrdinalIgnoreCase)
            || !PitFireTeamCompat.TeamArenaActive
            || __result?._dictionary == null)
            return;

        var original = __result._dictionary;
        if (!original.Keys.Any(point => ArenaBotSpawnPolicy.IsExcludedPoint(ArenaSession.ArmedMap, point?.Id)))
            return;

        // Both native raid entry and Redeploy's cached respawn system obtain
        // this collection through CreateFromScene. Filter AFTER FixSpawns: EFT
        // may reject server parameters when export dates differ. Copy the index
        // only; the original collection and all marker objects remain intact.
        var allowed = original.Where(pair =>
            !ArenaBotSpawnPolicy.IsExcludedPoint(ArenaSession.ArmedMap, pair.Key?.Id))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        __result = new SpawnPointsCollection(allowed, __result._logger);
        Plugin.ModLogger.LogInfo(
            $"Arena protected 5v5: excluded Factory courtyard team spawn; {allowed.Count}/{original.Count} markers retained.");
    }
}
