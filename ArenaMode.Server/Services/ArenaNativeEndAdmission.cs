using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Server.Services;

/// <summary>
/// First native Arena end: claim the exact transaction before asynchronous restoration.
/// Restore-first can consume the clone while FinalizingEnd is never reached,
/// leaving InRaid with nothing left to retry.
/// </summary>
internal static class ArenaNativeEndAdmission
{
    public static bool TryClaim(
        ArenaSessionService sessions,
        MongoId sessionId,
        string? nativeEndIdentity)
    {
        return sessions.BeginNativeEndFinalization(sessionId, nativeEndIdentity);
    }
}
