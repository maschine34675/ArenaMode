using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Linq;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    /// <summary>Only the authoritative live round may authorize a post-death goal handoff.</summary>
    internal bool AllowsPursuitGoal(int epoch, BotOwner source, BotOwner target, out bool follower)
    {
        follower = false;
        if (!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive || !_teamArena
            || _phase != Phase.Live || IsComplete || epoch <= 0 || epoch != _teamRoundEpoch
            || !ReferenceEquals(ArenaMatchService.Current, _match) || !ReferenceEquals(_match?.Mode, this)
            || _teamGame == null || _teamGame.Status != GameStatus.Started
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            || _boundLocalPlayer == null || _boundLocalPlayer.HealthController?.IsAlive != false
            || string.IsNullOrEmpty(_boundLocalProfileId) || _boundLocalPlayer.ProfileId != _boundLocalProfileId
            || _boundTeamFollowers.Length != TeamCompanionCount || _boundTeamFollowerProfileIds.Length != TeamCompanionCount
            || _boundTeamEnemies.Length != TeamEnemyCount || _boundTeamEnemyProfileIds.Length != TeamEnemyCount) return false;
        var world = Singleton<GameWorld>.Instance;
        if (!ArenaSession.IsExactActiveRaid(_teamGame, world)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(_boundLocalProfileId), _boundLocalPlayer)
            || (world.MainPlayer != null && !ReferenceEquals(world.MainPlayer, _boundLocalPlayer))
            || world.RegisteredPlayers.Any(player => player?.ProfileId == _boundLocalProfileId
                && !ReferenceEquals(player, _boundLocalPlayer))) return false;
        int sourceSide = PursuitSide(source), targetSide = PursuitSide(target);
        follower = sourceSide == 1;
        return sourceSide != 0 && targetSide != 0 && sourceSide != targetSide;
    }

    private int PursuitSide(BotOwner bot)
    {
        if (bot == null) return 0;
        for (int i = 0; i < _boundTeamFollowers.Length; i++)
            if (ReferenceEquals(bot, _boundTeamFollowers[i]) && bot.ProfileId == _boundTeamFollowerProfileIds[i]) return 1;
        for (int i = 0; i < _boundTeamEnemies.Length; i++)
            if (ReferenceEquals(bot, _boundTeamEnemies[i]) && bot.ProfileId == _boundTeamEnemyProfileIds[i]) return 2;
        return 0;
    }
}
