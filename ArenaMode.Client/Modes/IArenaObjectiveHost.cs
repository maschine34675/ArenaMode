using EFT;

namespace ArenaMode.Modes;

/// <summary>
/// A mode that feeds the shared bot objective bridge (<c>Bots.DominationObjectiveBridge</c>, the
/// BigBrain <c>ArenaDominationLayer</c>). Domination publishes its three points for both factions;
/// Tactical publishes one capture point or a few opening lanes for its enemy squad only
/// (docs/reviews/TACTICAL-OBJECTIVE-20261108.md). The host owns the body admission and the team
/// split; the bridge only spreads and leases. Kept in its own file so the bot harnesses can link
/// it without the rest of the mode contract.
/// </summary>
public interface IArenaObjectiveHost
{
    /// <summary>True only while the host wants its bots on objectives (live phase, combat counts, something published).</summary>
    bool ObjectivesLive { get; }

    /// <summary>The number of points the host currently publishes; a snapshot of another size revokes every lease.</summary>
    int ObjectiveCount { get; }

    /// <summary>
    /// True for a live body the host owns in this exact raid; <paramref name="teamA"/> is the team
    /// the bridge spreads leases by (Domination: USEC; Tactical: the whole enemy squad).
    /// </summary>
    bool TryClassifyObjectiveBody(Player body, out bool teamA);
}
