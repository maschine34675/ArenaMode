using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    /// <summary>
    /// The frozen five-opponent roster still reads its original physical bodies
    /// after death, both for live identity validation and round retirement. Hide
    /// and trim normally, but do not dispose/destroy one until that roster is
    /// deliberately cleared or replaced. This is a live lease, never a permanent
    /// corpse flag or a profile-ID exemption across rounds.
    /// </summary>
    internal bool RetainsFrozenOpponentCorpse(Player player, GameWorld world)
    {
        if (!ArenaSession.IsActive || !_teamArena || player == null || world == null
            || !ReferenceEquals(ArenaMatchService.Current, _match)
            || !ReferenceEquals(_match?.Mode, this)
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            || !ArenaSession.IsExactActiveRaid(_teamGame, world)
            || _boundTeamEnemies.Length != TeamEnemyCount)
            return false;

        // Do not require Phase.Live or _squad.Count: EndTeamRound clears the
        // squad, but ObserveTeamEnemyRetirement still owns the frozen bodies
        // through Aftermath/Rebinding. Native death already unregisters them;
        // keeping the dead object does not keep a bot active or block retirement.
        for (int i = 0; i < TeamEnemyCount; i++)
        {
            var owner = _boundTeamEnemies[i];
            if (owner != null && ReferenceEquals(owner.GetPlayer, player))
                return true;
        }
        return false;
    }
}
