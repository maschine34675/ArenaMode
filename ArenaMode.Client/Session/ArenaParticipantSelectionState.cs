using System;
using ArenaMode.Shared;

namespace ArenaMode.Session;

/// <summary>An immutable rendered proposal; the native adapter rechecks its owner before saving.</summary>
internal sealed class ArenaParticipantSelectionState
{
    private ArenaParticipantSelectionState(ArenaAreaSelectionState area, bool pitFireTeam, int total)
    {
        MapId = area?.MapId;
        ModeId = area?.ModeId;
        AreaId = area?.SelectedId;
        Available = area?.Available == true && AreaId == ArenaAreaCatalog.FullMapId
            && ArenaParticipantPolicy.TryResolve(MapId, ModeId, false, pitFireTeam, total, out _, out _);
        Total = total;
        Recommended = ArenaParticipantPolicy.DefaultTotal(MapId, ModeId);
        Minimum = ArenaParticipantPolicy.MinimumTotal(ModeId);
        Step = ArenaParticipantPolicy.Step(ModeId);
    }

    internal string MapId { get; }
    internal string ModeId { get; }
    internal string AreaId { get; }
    internal bool Available { get; }
    internal int Total { get; }
    internal int Recommended { get; }
    internal int Minimum { get; }
    internal int Step { get; }
    internal bool CanDecrease => Available && Total > Minimum;
    internal bool CanIncrease => Available && Total < ArenaParticipantPolicy.MaximumTotal;
    internal string ValueLabel => Format(Total);
    internal string RecommendationLabel => "Recommended: " + Format(Recommended);

    internal bool SameContext(ArenaParticipantSelectionState other)
        => other != null && Available == other.Available
            && string.Equals(MapId, other.MapId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ModeId, other.ModeId, StringComparison.Ordinal)
            && string.Equals(AreaId, other.AreaId, StringComparison.Ordinal);

    private string Format(int total)
        => ArenaParticipantPolicy.IsTeam(ModeId) ? $"{total} ({total / 2}v{total / 2})" : total.ToString();

    internal static ArenaParticipantSelectionState Create(ArenaAreaSelectionState area, bool pitFireTeam, int total)
        => new(area, pitFireTeam, total);
}
