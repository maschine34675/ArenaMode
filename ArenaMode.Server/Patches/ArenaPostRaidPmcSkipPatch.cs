using ArenaMode.Server.Services;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Replaces the complete native local-end pipeline for an exact Arena raid.
/// Both native start and end routes are retryable in EFT. The arm-bound serverId
/// lets the session service distinguish the first end, an overlapping retry, a
/// completed retry and a delayed foreign request before any post-raid side
/// effect runs.
/// </summary>
public sealed class ArenaPostRaidPmcSkipPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            nameof(LocationLifecycleService.EndLocalRaidAsync),
            BindingFlags.Instance | BindingFlags.Public,
            Type.DefaultBinder,
            [
                typeof(MongoId),
                typeof(EndLocalRaidRequestData),
                typeof(CancellationToken),
            ],
            null)!;
    }

    [PatchPrefix]
    public static bool Prefix(
        MongoId sessionId,
        EndLocalRaidRequestData request,
        CancellationToken cancellationToken,
        ref Task __result)
    {
        var sessions = ArenaContext.Sessions;
        var snapshots = ArenaContext.Snapshots;
        var protectedEnd = ArenaContext.ProtectedRaidEnd;
        string? nativeEndIdentity = request?.ServerId;
        if (ArenaContext.OrdinaryRaids?.IsExecutingExactEnd(
                sessionId,
                nativeEndIdentity) == true)
        {
            // The outer MatchCallbacks prefix already owns the exact ordinary
            // transaction and holds ArenaRaidStartGate across this complete
            // vanilla call. Re-entering the semaphore here would deadlock.
            return true;
        }

        bool arenaTagged = ArenaMode.Shared.Protocol.HasNativeEndMarker(
            nativeEndIdentity ?? string.Empty);
        if (sessions == null)
        {
            if (arenaTagged)
            {
                __result = Task.FromException(new InvalidOperationException(
                    "Arena session service is unavailable; tagged local-end blocked"));
                return false;
            }

            return true;
        }

        if (snapshots == null || protectedEnd == null)
        {
            if (!arenaTagged && !sessions.IsArmedOrInRaid(sessionId))
                return true;

            __result = Task.FromException(new InvalidOperationException(
                "Arena protected-end services are unavailable; local-end blocked"));
            return false;
        }

        using (ArenaRaidStartGate.Enter(sessionId))
        {
            ArenaLocalRaidEndDisposition disposition = sessions.ClassifyLocalRaidEnd(
                sessionId,
                nativeEndIdentity);

            switch (disposition)
            {
                case ArenaLocalRaidEndDisposition.Ordinary:
                    return true;

                case ArenaLocalRaidEndDisposition.CompletedArenaEndRetry:
                    // The exact request already persisted the restored snapshot.
                    // Returning success is the idempotent response EFT expected.
                    __result = Task.CompletedTask;
                    return false;

                case ArenaLocalRaidEndDisposition.FinalizingArenaEndRetry:
                    __result = protectedEnd.GetOrStart(
                            sessionId,
                            nativeEndIdentity!)
                        .WaitAsync(cancellationToken);
                    return false;

                case ArenaLocalRaidEndDisposition.RejectedProtectedEnd:
                    ArenaContext.Logger?.Error(
                        "[ArenaMode] Rejected native local-end whose serverId did not match the protected Arena arm.");
                    __result = Task.FromException(new InvalidOperationException(
                        "Native local-end did not match the protected Arena raid identity"));
                    return false;

                case ArenaLocalRaidEndDisposition.FirstArenaEnd:
                    if (!ArenaNativeEndAdmission.TryClaimAndRestore(
                            sessions,
                            snapshots,
                            sessionId,
                            nativeEndIdentity))
                    {
                        // Claim-first: a failed restore aborts FinalizingEnd so
                        // the snapshot and InRaid retry stay intact. No ordinary
                        // post-raid save is allowed.
                        __result = Task.FromException(new InvalidOperationException(
                            "Arena could not claim and restore its protected profile snapshot; local-end remains retryable"));
                        return false;
                    }

                    __result = protectedEnd.GetOrStart(
                            sessionId,
                            nativeEndIdentity!)
                        .WaitAsync(cancellationToken);
                    return false;

                default:
                    __result = Task.FromException(new InvalidOperationException(
                        "Arena native-end classification was unsupported"));
                    return false;
            }
        }
    }
}
