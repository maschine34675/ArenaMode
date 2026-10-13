using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    private readonly ArenaFriendlyFireRoster _friendlyFireRoster = new();

    private void BindFriendlyFireRoster()
    {
        if (!_friendlyFireRoster.Bind(_match, TeamCompanionCount, Singleton<GameWorld>.Instance,
            _boundLocalPlayer, _boundLocalProfileId, _boundTeamFollowers,
            _boundTeamFollowerProfileIds, _boundTeamEnemies, _boundTeamEnemyProfileIds))
            Plugin.ModLogger.LogWarning("Arena friendly-fire roster unavailable; unknown actors retain native damage.");
    }

    internal bool ShouldBlockFriendlyDamage(Player victim, IPlayer attacker)
        => _teamArena && _phase == Phase.Live && !IsComplete && _teamRoundEpoch > 0
            && PitFireTeamCompat.TeamArenaActive && _teamGame?.Status == GameStatus.Started
            && ReferenceEquals(ArenaMatchService.Current, _match) && ReferenceEquals(_match?.Mode, this)
            && ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            && ArenaSession.IsExactActiveRaid(_teamGame, Singleton<GameWorld>.Instance)
            && _friendlyFireRoster.AreTeammates(_match, Singleton<GameWorld>.Instance, victim, attacker);
}
