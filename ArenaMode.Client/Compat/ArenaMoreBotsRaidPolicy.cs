using ArenaMode.Shared;
using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Compat;

/// <summary>
/// Remember exact protected native requests, not the last mode selected. Weak
/// keys retain late/repeated end callbacks without keeping dead raids alive.
/// The foreign revenge queue is never cleared: Arena never adds to it.
/// </summary>
internal sealed class ArenaMoreBotsRaidPolicy
{
    private readonly ConditionalWeakTable<object, Lease> _raids = new();

    internal bool TryBindProtectedRaid(object settings, string startIdentity, string endIdentity,
        string token, string map, string profileId, int side)
    {
        if (settings == null || !Protocol.IsLifecycleRequestId(token)
            || !Protocol.IsNativeStartIdentity(startIdentity, token)
            || !Protocol.HasNativeEndIdentity(endIdentity, token)
            || endIdentity.IndexOf(Protocol.NativeEndIdentitySeparator, StringComparison.Ordinal) <= 0
            || string.IsNullOrWhiteSpace(map) || string.IsNullOrWhiteSpace(profileId)) return false;
        if (_raids.TryGetValue(settings, out var existing))
            return existing.Matches(endIdentity, map, side) && existing.ProfileId == profileId;
        _raids.Add(settings, new Lease(endIdentity, map, profileId, side));
        return true;
    }

    internal bool OwnsEnd(object settings, string identity, string map, int side)
        => settings != null && _raids.TryGetValue(settings, out var lease)
            && lease.Matches(identity, map, side);

    internal bool OwnsRevenge(object settings, string identity, string map, int side,
        string reportedProfileId)
        => settings != null && _raids.TryGetValue(settings, out var lease)
            && lease.Matches(identity, map, side)
            && string.Equals(lease.ProfileId, reportedProfileId, StringComparison.Ordinal);

    private sealed class Lease
    {
        private readonly string _identity, _map;
        private readonly int _side;
        internal readonly string ProfileId;
        internal Lease(string identity, string map, string profileId, int side)
        { _identity = identity; _map = map; ProfileId = profileId; _side = side; }
        internal bool Matches(string identity, string map, int side)
            => side == _side && string.Equals(identity, _identity, StringComparison.Ordinal)
                && string.Equals(map, _map, StringComparison.OrdinalIgnoreCase);
    }
}
