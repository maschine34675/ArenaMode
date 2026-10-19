using System;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Current native ownership, never a map-only or last-raid exemption.</summary>
internal static class ArenaRemoveTheDeadScope
{
    internal static bool ShouldPause(bool exactLocalWorld, bool pmc, string worldMap,
        string settingsMap, bool exactActiveRaid, bool nativeStarted, bool armed,
        bool pendingArm, string armedMap, string token, string nativeIdentity)
    {
        if (!exactLocalWorld) return false;
        // Frozen PFT corpses also remain owned through the protected exit. Do not
        // lift this hold just because the same native game entered Stopping.
        if (exactActiveRaid) return true;
        if (!pmc || string.IsNullOrWhiteSpace(worldMap)
            || !string.Equals(worldMap, settingsMap, StringComparison.OrdinalIgnoreCase)) return false;
        return nativeStarted && armed && pendingArm
            && string.Equals(worldMap, armedMap, StringComparison.OrdinalIgnoreCase)
            && Protocol.IsLifecycleRequestId(token)
            && Protocol.HasNativeEndIdentity(nativeIdentity, token);
    }
}
