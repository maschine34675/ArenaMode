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
            center = ArenaAreaCatalog.CustomsFactoryCenterId, contour = ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
        const string day = ArenaMapCatalog.FactoryDayId, night = ArenaMapCatalog.FactoryNightId,
            customs = ArenaMapCatalog.CustomsId;
        var factory = ArenaAreaSelectionState.Create(day, "teamdeathmatch", false, full, 7);
        Assert(factory.Options.Select(x => x.Id).SequenceEqual(new[] { full, south })
            && factory.SelectedId == full && factory.Summary.Contains("7v7"),
            "Factory day offers whole map and South; whole map reports configured teams");
        factory = ArenaAreaSelectionState.Create(day, "teamdeathmatch", false, south, 7);
        Assert(factory.SelectedId == south && factory.Summary == "2v2 - fixed teams"
            && factory.ModeHint.Contains("Team Deathmatch only"), "Bounded Factory reports fixed 2v2 regardless of full-map size");
        var pftState = ArenaAreaSelectionState.Create(day, "tactical", true, south, 7);
        Assert(pftState.SelectedId == full && pftState.Summary == "5v5 - fixed teams"
            && pftState.ModeHint == "Tactical only - PitFireTeam", "PFT display describes protected Tactical 5v5 without bounded choices");
        foreach (string saved in new[] { null, "", full, south, center, contour, "unknown-area" })
        {
            var custom = ArenaAreaSelectionState.Create(customs, "teamdeathmatch", false, saved, 12);
            Assert(custom.Options.Select(option => option.Id).SequenceEqual(new[] { center, contour })
                && custom.SelectedId == (saved == contour ? contour : center) && custom.Summary.Contains("2v2"),
                "Customs exposes both versioned areas, preserves a saved contour, and retains the legacy default");
            var nights = ArenaAreaSelectionState.Create(night, "teamdeathmatch", false, saved, 5);
            Assert(nights.Options.Count == 1 && nights.SelectedId == full, "Night never offers Factory South");
        }
        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch" })
        foreach (bool pft in new[] { false, true })
        foreach (string map in new[] { day, night, customs, "laboratory", "labyrinth", "Woods", null })
        {
            var state = ArenaAreaSelectionState.Create(map, mode, pft, center, 5);
            bool admitted = map != null && map != "Woods" && (!pft || mode == "tactical")
                && (map != customs || mode == "teamdeathmatch" && !pft);
            Assert(state.Available == admitted, "Picker cannot create new map/mode/PFT combinations");
            foreach (var choice in state.Options)
                Assert(ArenaAreaCatalog.TryResolve(choice.Id, map, mode, pft, out _, out _),
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
            "Customs discovery must not require its saved area");
        Assert(Settings.TeamDeathmatchArea.Value == full && Settings.AreaChanges == 0,
            "Reading area proposals never writes settings");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == center,
            "Exact Arena menu selection persists the Customs default");
        Assert(ArenaMenuSelection.MatchesRequest(Request(map: customs)), "Prepared Customs selection passes strict request validation");
        for (int i = 0; i < 50; i++) ArenaMenuSelection.TryPrepareMapArea(owner, customs);
        Assert(Settings.AreaChanges == 1 && RequestHandler.PostCount == beforePosts,
            "Stable menu polling performs no repeated settings changes or raid announcements");
        Assert(!ArenaMenuSelection.TrySelectArea(owner, customs, full) && Settings.TeamDeathmatchArea.Value == center,
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
        Assert(ArenaMenuSelection.TrySelectArea(owner, day, south), "Factory South is selected explicitly");
        Assert(ArenaMenuSelection.TryPrepareMapArea(owner, day) && Settings.TeamDeathmatchArea.Value == south,
            "Repeated compatible map preparation preserves South");
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
        Assert(!ArenaMenuSelection.IsMapDiscoverable(customs) && !ArenaMenuSelection.TryPrepareMapArea(owner, customs),
            "Leaving TDM makes Customs unavailable without silently changing mode");
        Settings.GameMode.Value = "teamdeathmatch";
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(false, false, "roster drift"), null);
        Assert(!ArenaMenuSelection.IsMapDiscoverable(day) && !ArenaMenuSelection.TryPrepareMapArea(owner, day),
            "Denied live eligibility cannot be repaired by area discovery");
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(true, false, ""), null);
        ArenaClientConfig.Enabled = _ => false;
        Assert(!ArenaMenuSelection.IsMapDiscoverable(customs) && !ArenaMenuSelection.TryPrepareMapArea(owner, customs),
            "Server-disabled maps remain hidden even with a valid proposed area");
        ArenaClientConfig.Enabled = Settings.IsArenaMap;
        Settings.TeamDeathmatchArea.Value = full;
        var invalid = Request(map: customs);
        Assert(!ArenaMenuSelection.TryValidateAndFreezeRequest(invalid, out _) && Settings.TeamDeathmatchArea.Value == full,
            "Final freeze must reject stale area without menu normalization");
        ArenaMenuSelection.EndFlow();
        Assert(!ArenaMenuSelection.TryPrepareMapArea(owner, customs) && Settings.TeamDeathmatchArea.Value == full,
            "Retired ordinary settings object cannot mutate persisted area");
        ConfigureAreaNegotiation();
    }
}
