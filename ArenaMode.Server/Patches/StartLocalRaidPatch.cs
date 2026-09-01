using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Replaces MatchCallbacks' thin controller/body wrapper for every local start.
/// The service calls the same SPT controller and serializer while holding
/// Arena's per-session gate across every await. Token-tagged starts additionally
/// share one protected task; ordinary starts keep the caller's cancellation and
/// otherwise preserve SPT's native behavior.
/// </summary>
public sealed class StartLocalRaidPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(MatchCallbacks).GetMethod(
            nameof(MatchCallbacks.StartLocalRaidAsync),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(
        StartLocalRaidRequestData info,
        MongoId sessionID,
        CancellationToken cancellationToken,
        ref ValueTask<string> __result)
    {
        string? nativeStartIdentity = info?.ServerId;
        bool ordinaryTagged = Protocol.IsNativeOrdinaryStartIdentity(
            nativeStartIdentity ?? string.Empty);
        bool ordinaryMarked = Protocol.HasNativeOrdinaryStartMarker(
            nativeStartIdentity ?? string.Empty);
        bool arenaTagged = !ordinaryMarked && Protocol.HasNativeStartMarker(
            nativeStartIdentity ?? string.Empty);

        ArenaSessionService? sessions = ArenaContext.Sessions;
        ProfileSnapshotService? snapshots = ArenaContext.Snapshots;
        ArenaProtectedRaidStartService? protectedStart = ArenaContext.ProtectedRaidStart;
        OrdinaryRaidLifecycleService? ordinaryRaids = ArenaContext.OrdinaryRaids;

        if (ordinaryMarked)
        {
            if (!ordinaryTagged || ordinaryRaids == null || info == null)
            {
                __result = new ValueTask<string>(Task.FromException<string>(
                    new InvalidOperationException(
                        "Ordinary local-start identity was malformed or its lifecycle service is unavailable")));
                return false;
            }

            Task<string> transaction = ordinaryRaids.GetOrStart(sessionID, info);
            __result = new ValueTask<string>(transaction.WaitAsync(cancellationToken));
            return false;
        }

        if (arenaTagged)
        {
            if (sessions == null || snapshots == null || protectedStart == null || info == null)
            {
                __result = new ValueTask<string>(Task.FromException<string>(
                    new InvalidOperationException(
                        "Arena protected-start services are unavailable; tagged local-start blocked")));
                return false;
            }

            // GetOrStart commits one internal task before applying the caller's
            // cancellation only to its wait. A lost HTTP response cannot cancel
            // or duplicate SPT's protected start pipeline.
            Task<string> transaction = protectedStart.GetOrStart(sessionID, info);
            __result = new ValueTask<string>(transaction.WaitAsync(cancellationToken));
            return false;
        }

        if (sessions != null && snapshots != null && protectedStart != null && info != null)
        {
            __result = new ValueTask<string>(protectedStart.StartOrdinaryAsync(
                sessionID,
                info,
                cancellationToken));
            return false;
        }

        // During partial initialization, preserve vanilla only when there is no
        // evidence of an Arena-owned state. Tagged requests already failed
        // above; an observable snapshot/transition must remain fail-closed.
        if (sessions?.IsArmedOrInRaid(sessionID) == true
            || snapshots?.HasSnapshot(sessionID) == true)
        {
            __result = new ValueTask<string>(Task.FromException<string>(
                new InvalidOperationException(
                    "An Arena snapshot requires explicit token-bound recovery before local-start")));
            return false;
        }

        return true;
    }
}
