using System;
using System.Linq;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckNativeAreas()
    {
        const string full = ArenaAreaCatalog.FullMapId, south = ArenaAreaCatalog.FactorySouthId,
            center = ArenaAreaCatalog.CustomsFactoryCenterId, contour = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
        const string day = ArenaMapCatalog.FactoryDayId, night = ArenaMapCatalog.FactoryNightId,
            customs = ArenaMapCatalog.CustomsId;
        var factory = ArenaAreaSelectionState.Create(day, "teamdeathmatch", false, full, 7);
        Assert(factory.Options.Select(x => x.Id).SequenceEqual(new[] { full })
            && factory.SelectedId == full && factory.Summary.Contains("7v7"),
            "Factory day offers only whole map and reports configured teams");
        factory = ArenaAreaSelectionState.Create(day, "teamdeathmatch", false, south, 7);
        Assert(factory.SelectedId == full && factory.Summary.Contains("7v7"),
            "Retired South resolves to whole Factory without imposing a bounded team size");
        var pftState = ArenaAreaSelectionState.Create(day, "tactical", true, south, 7);
        Assert(pftState.SelectedId == full && pftState.Summary == "PFT squad vs 5"
            && pftState.ModeHint == "PitFireTeam squad - own equipment", "PFT area state leaves ally count to the live view and preserves five opponents");
        foreach (string saved in new[] { null, "", full, south, center, contour, "unknown-area" })
        {
            var custom = ArenaAreaSelectionState.Create(customs, "teamdeathmatch", false, saved, 12);
            Assert(custom.Options.Select(option => option.Id).SequenceEqual(new[] { contour, ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId })
                && custom.SelectedId == contour && custom.Summary.Contains("2v2"),
                "Customs offers three surveyed areas and proposes the first for a retired selection");
            var nights = ArenaAreaSelectionState.Create(night, "teamdeathmatch", false, saved, 5);
            Assert(nights.Options.Count == 1 && nights.SelectedId == full, "Night never offers Factory South");
        }
        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch", "zombie" })
        foreach (bool pft in new[] { false, true })
        foreach (string map in new[] { day, night, customs, "laboratory", "labyrinth", "Woods", null })
        {
            var state = ArenaAreaSelectionState.Create(map, mode, pft, center, 5);
            bool admitted = map != null && map != "Woods" && (!pft || mode == "tactical" || mode == "zombie");
            Assert(state.Available == admitted, "Picker cannot create new map/mode/PFT combinations");
            foreach (var choice in state.Options)
                Assert(ArenaAreaCatalog.TryResolve(choice.Id, map, state.ModeId, pft, out _, out _),
                    "Every displayed area is accepted by the shared server contract");
        }

        ConfigureAreaNegotiation();
        Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
        Settings.RefuseAreaWrite = false;
        Settings.AreaChanges = 0;
        Settings.TeamDeathmatchArea.Value = full;
        var owner = Select(customs);
        int beforePosts = RequestHandler.PostCount;
        Assert(ArenaMenuSelection.IsMapDiscoverable(customs) && !ArenaMenuSelection.IsMapAvailable(customs),
            "Customs is discoverable but final admission requires a bounded area");
        Assert(Settings.TeamDeathmatchArea.Value == full && Settings.AreaChanges == 0,
            "Reading area proposals never writes settings");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == contour,
            "Exact Arena menu preparation replaces the retired whole-map preference with the first contour");
        Assert(ArenaMenuSelection.MatchesRequest(Request(map: customs)), "Prepared Customs selection passes strict request validation");
        for (int i = 0; i < 50; i++) ArenaMenuSelection.TryPrepareMapArea(owner, customs);
        Assert(Settings.AreaChanges == 1 && RequestHandler.PostCount == beforePosts,
            "Stable menu polling performs no repeated settings changes or raid announcements");
        Assert(!ArenaMenuSelection.TrySelectArea(owner, customs, center) && Settings.TeamDeathmatchArea.Value == contour,
            "An injected unsupported area click is rejected");
        Assert(ArenaMenuSelection.TrySelectArea(owner, customs, contour)
            && Settings.TeamDeathmatchArea.Value == contour
            && ArenaMenuSelection.MatchesRequest(Request(map: customs)),
            "The surveyed contour is explicitly selectable and passes exact request validation");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == contour,
            "Menu refresh preserves the selected contour without restoring the old rectangle");
        Assert(!ArenaMenuSelection.TrySelectArea(owner, customs, contour.ToUpperInvariant())
            && Settings.TeamDeathmatchArea.Value == contour, "Versioned contour IDs remain case-sensitive");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, day) && Settings.TeamDeathmatchArea.Value == full,
            "Switching away from Customs settles to a supported area");
        Assert(!ArenaMenuSelection.TrySelectArea(owner, day, south), "Retired Factory South cannot be selected explicitly");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, day) && Settings.TeamDeathmatchArea.Value == full,
            "Repeated compatible map preparation preserves whole Factory");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, night) && Settings.TeamDeathmatchArea.Value == full,
            "Night transition resolves South to whole map");
        Settings.RefuseAreaWrite = true;
        Assert(!ArenaMenuSelection.TryPrepareMapArea(owner, night),
            "A refused write must not be promoted to successful preparation");
        Assert(!ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == full,
            "Failed persistence cannot accept a proposed Customs default");
        Settings.RefuseAreaWrite = false;
        Assert(!ArenaMenuSelection.TryPrepareMapArea(new RaidSettings { Side = ESideType.Pmc }, customs)
            && Settings.TeamDeathmatchArea.Value == full, "Different setup owner cannot repair Arena settings");
        Settings.TeamDeathmatchArea.Value = center;
        owner.SelectedLocation.Id = customs;
        Settings.GameMode.Value = "gungame";
        Assert(ArenaMenuSelection.IsMapDiscoverable(customs) && ArenaMenuSelection.TryPrepareMapArea(owner, customs)
            && Settings.GameMode.Value == "gungame" && Settings.TeamDeathmatchArea.Value == contour,
            "Leaving TDM keeps Customs available and repairs a retired contour without changing mode");
        Settings.GameMode.Value = "teamdeathmatch";
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(false, false, "roster drift"), null);
        Assert(!ArenaMenuSelection.IsMapDiscoverable(day) && !ArenaMenuSelection.TryPrepareMapArea(owner, day),
            "Denied live eligibility cannot be repaired by area discovery");
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(true, false, ""), null);
        ArenaClientConfig.Enabled = _ => false;
        Assert(!ArenaMenuSelection.IsMapDiscoverable(customs) && !ArenaMenuSelection.TryPrepareMapArea(owner, customs),
            "Server-disabled maps remain hidden even with a valid proposed area");
        ArenaClientConfig.Enabled = Settings.IsArenaMap;
        Settings.TeamDeathmatchArea.Value = center;
        var invalid = Request(map: customs);
        Assert(!ArenaMenuSelection.TryValidateAndFreezeRequest(invalid, out _) && Settings.TeamDeathmatchArea.Value == center,
            "Final freeze must reject stale area without menu normalization");
        ArenaMenuSelection.EndFlow();
        Assert(!ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == center,
            "Retired ordinary settings object cannot mutate persisted area");
        ConfigureAreaNegotiation();
    }
}
