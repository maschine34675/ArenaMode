using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Server.Services;

/// <summary>
/// First native Arena end: claim the exact transaction, then restore.
/// Restore-first can consume the clone while FinalizingEnd is never reached,
/// leaving InRaid with nothing left to retry.
/// </summary>
internal static class ArenaNativeEndAdmission
{
    public static bool TryClaimAndRestore(
        ArenaSessionService sessions,
        ProfileSnapshotService snapshots,
        MongoId sessionId,
        string? nativeEndIdentity)
    {
        if (!sessions.BeginNativeEndFinalization(sessionId, nativeEndIdentity))
            return false;

        if (snapshots.RestoreSnapshot(sessionId))
            return true;

        sessions.AbortNativeEndFinalization(sessionId, nativeEndIdentity);
        return false;
    }
}
