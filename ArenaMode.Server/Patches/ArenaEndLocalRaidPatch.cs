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
    // The optional SVM adapter orders this prefix before SVM only after its
    // complete callback contract passed. Otherwise baseline ordering is kept.
    public static bool Prefix(
        MatchCallbacks __instance,
        string url,
        EndLocalRaidRequestData info,
        MongoId sessionID,
        CancellationToken cancellationToken,
        ref ValueTask<string> __result)
    {
        OrdinaryRaidLifecycleService? ordinary = ArenaContext.OrdinaryRaids;
        if (ordinary == null || info == null)
            return true;

        if (ordinary.TryEnterExactEndCallback(__instance, url, sessionID, info))
            return true;

        if (!ordinary.TryGetOrStartEnd(sessionID, info, __instance, url, out Task<string>? transaction, out bool callbackBridge))
            return true;

        // HarmonyX can still invoke later foreign prefixes after this one
        // returns false. Defer only this exact outer SVM invocation; the inner
        // callback gets a fresh scope and executes SVM once inside the gate.
        if (callbackBridge)
            Compat.ArenaSvmCompat.MarkOrdinaryWrapperOwned(__instance, url, sessionID, info);
        __result = new ValueTask<string>(
            transaction!.WaitAsync(cancellationToken));
        return false;
    }
}
