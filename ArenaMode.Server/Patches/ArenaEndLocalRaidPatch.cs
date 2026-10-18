using ArenaMode.Server.Services;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Owns the outer callback boundary for an ordinary local end. The exact SPT
/// server id is classified before MatchController runs, the complete vanilla
/// async pipeline remains under the per-session lifecycle gate, and identical
/// HTTP retries share one task/response.
/// </summary>
public sealed class ArenaEndLocalRaidPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(MatchCallbacks).GetMethod(
            nameof(MatchCallbacks.EndLocalRaidAsync),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(
        EndLocalRaidRequestData info,
        MongoId sessionID,
        CancellationToken cancellationToken,
        ref ValueTask<string> __result)
    {
        OrdinaryRaidLifecycleService? ordinary = ArenaContext.OrdinaryRaids;
        if (ordinary == null || info == null)
            return true;

        if (!ordinary.TryGetOrStartEnd(sessionID, info, out Task<string>? transaction))
            return true;

        __result = new ValueTask<string>(
            transaction!.WaitAsync(cancellationToken));
        return false;
    }
}
