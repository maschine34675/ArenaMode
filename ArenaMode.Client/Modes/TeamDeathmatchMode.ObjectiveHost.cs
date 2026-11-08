using EFT;

namespace ArenaMode.Modes;

/// <summary>
/// Team DM as an objective host: only Domination publishes points, only in its live phase, and
/// only for the PMC bodies the mode itself admitted (<see cref="TryClassifyCurrentBody"/>); the
/// team is the faction. Kept in its own file so the bot harness can link the exact production
/// rule beside its doubles (<c>ObjectiveCount</c> lives in the Domination partial).
/// </summary>
public sealed partial class TeamDeathmatchMode : IArenaObjectiveHost
{
    public bool ObjectivesLive => IsDomination && IsSearchActive;

    public bool TryClassifyObjectiveBody(Player body, out bool teamA)
    {
        teamA = false;
        if (!TryClassifyCurrentBody(body, out _)) return false;
        var side = body.Side;
        if (side != EPlayerSide.Usec && side != EPlayerSide.Bear) return false;
        teamA = side == EPlayerSide.Usec;
        return true;
    }
}
