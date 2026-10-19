using System;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>
/// No retained raid lease: every limiter invocation must prove its current world.
/// A stale IsActive, a selected map or yesterday's token cannot authorize a skip.
/// </summary>
internal static class ArenaAiLimitScope
{
    internal static bool ShouldSuppress(bool exactLocalWorld, bool nativeRaidLive, bool pmc,
        string worldMap, string settingsMap, bool exactActiveRaid, bool armed, bool pendingArm,
        string armedMap, string pendingToken, string nativeIdentity)
    {
        if (!exactLocalWorld || !nativeRaidLive || !pmc || string.IsNullOrWhiteSpace(worldMap)
            || !string.Equals(worldMap, settingsMap, StringComparison.OrdinalIgnoreCase)) return false;
        // The exact active pair is published only after ConfirmRaidStarted has
        // consumed PendingArmToken. A late Enable call uses that stronger proof.
        if (exactActiveRaid) return true;
        return armed && pendingArm
            && string.Equals(worldMap, armedMap, StringComparison.OrdinalIgnoreCase)
            && Protocol.IsLifecycleRequestId(pendingToken)
            // At OnGameStarted, TarkovApplication has already replaced the
            // request's start marker with the server-confirmed native end id.
            // A request-only marker does not prove that protected load finished.
            && Protocol.HasNativeEndIdentity(nativeIdentity, pendingToken);
    }
}
