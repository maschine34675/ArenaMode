using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    /// <summary>
    /// Cosmetic read only: neither discovers nor admits PFT followers. The live
    /// round owns exact bodies; historical account/profile IDs are insufficient.
    /// </summary>
    internal bool IsCurrentFriendlyMarkerBody(Player player)
    {
        if (!ArenaSession.IsActive || !_teamArena || _phase != Phase.Live || IsComplete
            || !PitFireTeamCompat.TeamArenaActive || _teamRoundEpoch <= 0
            || !ReferenceEquals(ArenaMatchService.Current, _match)
            || !ReferenceEquals(_match?.Mode, this)
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            || _boundLocalPlayer == null || string.IsNullOrEmpty(_boundLocalProfileId)
            || !string.Equals(_boundLocalPlayer.ProfileId, _boundLocalProfileId, StringComparison.Ordinal)
            || player == null || !player.IsAI || player.HealthController?.IsAlive != true
            || string.IsNullOrEmpty(player.ProfileId)
            || _boundTeamFollowers.Length != TeamCompanionCount
            || _boundTeamFollowerProfileIds.Length != TeamCompanionCount)
            return false;

        var world = Singleton<GameWorld>.Instance;
        if (!ArenaSession.IsExactActiveRaid(_teamGame, world)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(_boundLocalProfileId), _boundLocalPlayer)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)) return false;

        for (int i = 0; i < _boundTeamFollowers.Length; i++)
        {
            var owner = _boundTeamFollowers[i];
            if (owner == null || owner.BotState != EBotState.Active || owner.IsDead
                || owner.HealthController?.IsAlive != true
                || !ReferenceEquals(owner.GetPlayer, player)
                || !ReferenceEquals(player.AIData?.BotOwner, owner)) continue;
            return string.Equals(player.ProfileId, _boundTeamFollowerProfileIds[i], StringComparison.Ordinal)
                && string.Equals(owner.ProfileId, _boundTeamFollowerProfileIds[i], StringComparison.Ordinal);
        }
        return false;
    }
}
