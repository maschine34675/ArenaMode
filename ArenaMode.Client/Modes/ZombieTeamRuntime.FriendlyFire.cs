using ArenaMode.Combat;
using EFT;
using System;

namespace ArenaMode.Modes;

internal sealed partial class ZombieTeamRuntime
{
    private readonly ArenaFriendlyFireRoster _friendlyFireRoster = new();

    private void BindFriendlyFireRoster()
    {
        if (!_friendlyFireRoster.Bind(_match, _world, _boundLocalPlayer, _boundLocalProfileId,
            _boundTeamFollowers, _boundTeamFollowerProfileIds, Array.Empty<BotOwner>(), Array.Empty<string>()))
            Plugin.ModLogger.LogWarning("Arena friendly-fire roster unavailable; unknown actors retain native damage.");
    }

    internal bool ShouldBlockFriendlyDamage(Player victim, IPlayer attacker)
        => IsLive && _friendlyFireRoster.AreTeammates(_match, _world, victim, attacker);
}
