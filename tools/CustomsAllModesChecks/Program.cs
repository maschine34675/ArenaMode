using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;

int checks = 0;
void Need(bool result, string code) { checks++; if (!result) throw new Exception(code); }
string[] modes = { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" };
string[] areas = { ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId };
foreach (string mode in modes)
foreach (bool pft in new[] { false, true })
{
    bool admitted = !pft || mode is "tactical" or "zombie";
    foreach (string whole in new[] { null, "", " ", ArenaAreaCatalog.FullMapId })
        Need(!ArenaAreaCatalog.TryResolve(whole, "BIGMAP", mode, pft, out _, out _), "NO-WHOLE-CUSTOMS");
    var defaultProposal = ArenaAreaSelectionState.Create("bigmap", mode, pft, ArenaAreaCatalog.FullMapId, 5);
    Need(defaultProposal.Options.Select(p => p.Id).SequenceEqual(admitted ? areas : Array.Empty<string>()), "EXACT-OFFERED-AREAS");
    Need(defaultProposal.Available == admitted, "PFT-MODE-ADMISSION");
    foreach (string area in areas)
    {
        bool resolved = ArenaAreaCatalog.TryResolve(area, "bigmap", mode, pft, out var geometry, out _);
        Need(resolved == admitted, "ALL-MODE-AREA-ADMISSION");
        if (!admitted) continue;
        Need(!geometry.Bounds.LimitsHeight && geometry.TeamStarts != null && geometry.MaxTeamSize == 5, "EXISTING-GEOMETRY-AND-STARTS");
        Need(!ArenaAreaCatalog.TryResolve(area, ArenaMapCatalog.FactoryDayId, mode, pft, out _, out _), "EXACT-MAP");
        var selected = ArenaAreaSelectionState.Create("bigmap", mode, pft, area, 5, 3);
        Need(selected.SelectedId == area && ReferenceEquals(geometry, selected.SelectedArea), "EXACT-PROPOSAL");
        Need(selected.HasTeamSizeChoice == (mode == "teamdeathmatch"), "TDM-ONLY-SIZE-UI");
        Need(selected.TeamSizeOptions.Count == (mode == "teamdeathmatch" ? 4 : 0), "TDM-SIZE-CHOICES");
        int? wireSize = mode == "teamdeathmatch" ? 3 : null;
        Need(ArenaAreaCatalog.TryResolveTeamSize(geometry, mode, pft, wireSize, out int frozenSize, out _)
            && frozenSize == (wireSize ?? 0), "MODE-OWNED-FROZEN-SIZE");
        if (mode != "teamdeathmatch")
            Need(!ArenaAreaCatalog.TryResolveTeamSize(geometry, mode, pft, 3, out _, out _), "NO-TDM-SIZE-IN-OTHER-MODES");
        bool participants = mode is "deathmatch" or "gungame";
        Need(ArenaParticipantSelectionState.Create(selected, pft, 10).Available == participants, "BOUNDED-SOLO-PARTICIPANTS");
        Need(ArenaParticipantPolicy.TryResolve("bigmap", mode, true, pft, 10, out _, out _) == participants, "PARTICIPANT-WIRE-AGREEMENT");
        Need(!ArenaParticipantPolicy.TryResolve("bigmap", mode, true, pft, 25, out _, out _), "PARTICIPANT-UPPER-BOUND");
    }
}
foreach (string mode in modes)
{
    foreach (string map in new[] { "factory4_day", "factory4_night", "laboratory", "labyrinth", "arena_bay5" })
        Need(ArenaAreaCatalog.TryResolve(null, map, mode, false, out var area, out _) && area == null, "OTHER-FULL-MAPS-UNCHANGED");
    Need(ArenaAreaCatalog.TryResolve(null, "arena_equator", mode, false, out _, out _) == (mode != "tactical"), "EQUATOR-TACTICAL-CAPACITY-GUARD");
    foreach (string invalid in new[] { "unpublished-draft", ArenaAreaCatalog.CustomsFactoryCenterId,
        ArenaAreaCatalog.CustomsFactoryCenterPolygonId, "customs-factory-east-polygon-v1", ArenaAreaCatalog.FactorySouthId })
    {
        Need(!ArenaAreaCatalog.TryResolve(invalid, "bigmap", mode, false, out _, out _), "EXACT-WIRE-NO-FALLBACK");
        var proposal = ArenaAreaSelectionState.Create("bigmap", mode, false, invalid, 5);
        Need(proposal.SelectedId == areas[0], "MENU-ONLY-DEFAULT-PROPOSAL");
    }
}
foreach (string unknown in new[] { null, "", "endless", "rounds", "not-a-mode" })
foreach (string area in areas)
    Need(!ArenaAreaCatalog.TryResolve(area, "bigmap", unknown, false, out _, out _), "UNKNOWN-MODE-REJECTED");
Console.WriteLine($"CUSTOMS_ALL_MODES_CHECKS_PASSED ({checks} assertions)");
