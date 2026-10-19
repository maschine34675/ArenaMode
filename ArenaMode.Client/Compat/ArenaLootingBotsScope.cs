using System;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Recomputed for every call; no profile-id or map-based suppression lease.</summary>
internal static class ArenaLootingBotsScope
{
    internal static bool OwnsRaid(bool exactWorld, bool nativeRunning, bool pmc,
        string map, string settingsMap, bool active, bool armed, bool pending,
        string armedMap, string token, string nativeId)
    {
        if (!exactWorld || !nativeRunning || !pmc || string.IsNullOrWhiteSpace(map)
            || !string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)) return false;
        if (active) return true;
        return armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
            && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeId, token);
    }

    internal static bool Suppress(bool ownedRaid, bool exactBody, bool pftTeamRaid,
        bool frozenCompanionAccount, string mode)
    {
        if (!ownedRaid || !exactBody) return false;
        // Faction friendship is not authority: TDM allies are autonomous bots.
        bool commandable = pftTeamRaid && frozenCompanionAccount
            && (string.Equals(mode, "tactical", StringComparison.Ordinal)
                || string.Equals(mode, "zombie", StringComparison.Ordinal));
        return !commandable;
    }
}
