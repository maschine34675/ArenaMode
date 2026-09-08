using System;
using System.Collections.Generic;
using System.Linq;
using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Session;

internal sealed class ArenaAreaOption
{
    internal ArenaAreaOption(string id, string label, int fixedTeamSize = 0)
    { Id = id; Label = label; FixedTeamSize = fixedTeamSize; }
    public string Id { get; }
    public string Label { get; }
    public int FixedTeamSize { get; }
}

/// <summary>
/// Immutable menu proposal. Its resolved ID is not a persisted or armed choice:
/// the adapter must successfully write it before accepting the native map.
/// Discovery asks whether ANY valid area exists; final start still uses the
/// exact saved area and the shared server contract without automatic repair.
/// </summary>
internal sealed class ArenaAreaSelectionState
{
    private static readonly ArenaAreaOption[] Candidates =
    {
        new(ArenaAreaCatalog.FullMapId, "Whole map"),
        new(ArenaAreaCatalog.FactorySouthId, "Factory South", ArenaAreaCatalog.FactorySouth.MaxTeamSize),
        new(ArenaAreaCatalog.CustomsFactoryCenterId, "Factory Center", ArenaAreaCatalog.CustomsFactoryCenter.MaxTeamSize),
        new(ArenaAreaCatalog.CustomsFactoryCenterPolygonId, "Factory Center (contour)", ArenaAreaCatalog.CustomsFactoryCenterPolygon.MaxTeamSize),
        new(ArenaAreaCatalog.CustomsFactoryCenterTeamsId, "Customs contour (team starts)"),
    };

    private ArenaAreaSelectionState(ArenaAreaOption[] options, string requestedArea, string mode, bool pitFireTeam, int teamSize, int areaTeamSize)
    {
        Options = Array.AsReadOnly(options);
        var selected = options.FirstOrDefault(option => option.Id == ArenaAreaCatalog.NormalizeId(requestedArea))
            ?? options.FirstOrDefault();
        SelectedId = selected?.Id;
        HasTeamSizeChoice = SelectedId == ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
        SelectedTeamSize = HasTeamSizeChoice && ArenaAreaCatalog.TryResolveTeamSize(
            ArenaAreaCatalog.CustomsFactoryCenterTeams, areaTeamSize, out var validSize, out _) ? validSize : 0;
        TeamSizeOptions = Array.AsReadOnly(HasTeamSizeChoice ? new[] { 2, 3, 4, 5 } : Array.Empty<int>());
        Summary = selected == null ? "Select a map to choose its area."
            : HasTeamSizeChoice ? SelectedTeamSize > 0 ? $"{SelectedTeamSize}v{SelectedTeamSize} - opposing team starts" : "Select a supported team size."
            : selected.FixedTeamSize > 0 ? $"{selected.FixedTeamSize}v{selected.FixedTeamSize} - fixed teams"
            : pitFireTeam ? "5v5 - fixed teams"
            : mode == ArenaModeCatalog.TeamDeathmatchId ? $"{Math.Max(2, Math.Min(12, teamSize))}v{Math.Max(2, Math.Min(12, teamSize))} - configured teams"
            : "Whole map";
        ModeHint = selected == null ? string.Empty
            : selected.FixedTeamSize > 0 || HasTeamSizeChoice ? "Team Deathmatch only - experimental"
            : pitFireTeam ? "Tactical only - PitFireTeam"
            : "Available in all Arena modes";
    }

    public IReadOnlyList<ArenaAreaOption> Options { get; }
    public string SelectedId { get; }
    public string Summary { get; }
    public string ModeHint { get; }
    public bool HasTeamSizeChoice { get; }
    public int SelectedTeamSize { get; }
    public IReadOnlyList<int> TeamSizeOptions { get; }
    public bool Available => SelectedId != null;

    public static ArenaAreaSelectionState Create(string map, string mode, bool pitFireTeam,
        string requestedArea, int fullMapTeamSize, int areaTeamSize = 2)
    {
        mode = ArenaModeCatalog.NormalizeId(mode);
        var choices = !ArenaMapCatalog.IsSupportedMap(map)
            || (pitFireTeam && mode != ArenaModeCatalog.TacticalId)
            ? Array.Empty<ArenaAreaOption>()
            : Candidates.Where(option => ArenaAreaCatalog.TryResolve(option.Id, map, mode, pitFireTeam,
                out _, out _)).ToArray();
        return new ArenaAreaSelectionState(choices, requestedArea, mode, pitFireTeam, fullMapTeamSize, areaTeamSize);
    }
}
