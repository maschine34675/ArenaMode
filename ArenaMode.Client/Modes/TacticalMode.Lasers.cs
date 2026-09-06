using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    /// <summary>Active weapon cosmetics use this round's exact bodies, not historical tracer IDs.</summary>
    internal bool TryClassifyLaserBody(Player player, out bool friendly)
    {
        friendly = false;
        if (!ArenaSession.IsActive || !_teamArena || !PitFireTeamCompat.TeamArenaActive
            || _phase != Phase.Live || IsComplete || _teamRoundEpoch <= 0
            || !ReferenceEquals(ArenaMatchService.Current, _match) || !ReferenceEquals(_match?.Mode, this)
            || _teamGame == null || _teamGame.Status != GameStatus.Started
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            || _boundLocalPlayer == null || string.IsNullOrEmpty(_boundLocalProfileId)
            || !string.Equals(_boundLocalPlayer.ProfileId, _boundLocalProfileId, StringComparison.Ordinal)
            || _boundTeamFollowers.Length != TeamCompanionCount || _boundTeamFollowerProfileIds.Length != TeamCompanionCount
            || _boundTeamEnemies.Length != TeamEnemyCount || _boundTeamEnemyProfileIds.Length != TeamEnemyCount
            || player == null || player.HealthController?.IsAlive != true || string.IsNullOrEmpty(player.ProfileId)) return false;
        var world = Singleton<GameWorld>.Instance;
        if (!ArenaSession.IsExactActiveRaid(_teamGame, world)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(_boundLocalProfileId), _boundLocalPlayer)
            || (world.MainPlayer != null && !ReferenceEquals(world.MainPlayer, _boundLocalPlayer))
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)) return false;
        if (ReferenceEquals(player, _boundLocalPlayer)) { friendly = true; return true; }
        var owner = player.AIData?.BotOwner;
        if (!player.IsAI || owner == null || owner.BotState != EBotState.Active || owner.IsDead
            || owner.HealthController?.IsAlive != true || !ReferenceEquals(owner.GetPlayer, player)
            || !string.Equals(owner.ProfileId, player.ProfileId, StringComparison.Ordinal)) return false;
        for (int i = 0; i < _boundTeamFollowers.Length; i++)
            if (ReferenceEquals(owner, _boundTeamFollowers[i])
                && string.Equals(player.ProfileId, _boundTeamFollowerProfileIds[i], StringComparison.Ordinal))
            { friendly = true; return true; }
        for (int i = 0; i < _boundTeamEnemies.Length; i++)
            if (ReferenceEquals(owner, _boundTeamEnemies[i])
                && string.Equals(player.ProfileId, _boundTeamEnemyProfileIds[i], StringComparison.Ordinal)) return true;
        return false;
    }
}
