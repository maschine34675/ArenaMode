using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.UI.Matchmaker;
using UnityEngine;

internal static class Program
{
    private static int _checks, _compatibilityReads, _strictReads, _modeReads, _freezes;
    private static string _refusal;
    private static bool _team;
    private static void Need(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("ASSERT: " + message); }

    private static ArenaMenuSelection.Eligibility Inspect(bool mode)
    {
        if (mode) _modeReads++; else _strictReads++;
        // Real SideSelection's provider also evaluates this process-lifetime
        // boundary. Count it honestly rather than making the double cheaper.
        var refusal = ArenaMenuSelection.CompatibilityRejection;
        return new(refusal == null && (mode || ArenaModeCatalog.CanSelect(Settings.SelectedGameModeId, _team)),
            _team, refusal ?? "team mode mismatch");
    }

    private static void Main()
    {
        ArenaLocationSelectionPatch.Enable();
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        ArenaMenuSelection.RegisterCompatibilityAdmission(() => { _compatibilityReads++; return _refusal; });
        Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
        ArenaClientConfig.Enabled = _ => true;
        Settings.GameMode.Value = ArenaModeCatalog.DominationId;
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        Settings.TeamDeathmatchSize.Value = 5;
        Settings.AreaTeamSize.Value = 2;
        var settings = new RaidSettings { Side = ESideType.Pmc, transitionType = ELocationTransition.Common };
        var screen = new GameObject("menu performance fixture").AddComponent<MatchMakerSelectionLocationScreen>();
        screen.Initialize(settings);
        foreach (string map in new[] { ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId })
            screen.Locations[map] = new JsonType.LocationSettings.Location { Id = map };
        ArenaMenuSelection.Begin(new object(), settings);
        ArenaMenuSelection.ConfigureEligibility(() => Inspect(false), _ => { _freezes++; return ""; }, () => Inspect(true));
        ArenaMenuSelection.Select();
        screen.ShowInternal(settings);
        var updater = screen.GetComponentsInChildren<ArenaMapMenuUpdater>(true).Single();
        int before = _compatibilityReads;
        updater.Tick();
        int perRefresh = _compatibilityReads - before;
        Console.WriteLine($"MENU_PERFORMANCE_OBSERVED: rows={screen.Buttons.Count}, compatibility checks per idle refresh={perRefresh}");
        Need(perRefresh <= 4, "one display pass evaluates each eligibility source once, not once per map");
        Need(_strictReads > 0 && _modeReads > 0, "strict and roster-only mode eligibility remain distinct");
        for (int i = 0; i < 20; i++) updater.Tick();
        Need(_compatibilityReads - before <= 84, "idle compatibility work is bounded independently of map count");
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.FactoryDayId]);
        before = _compatibilityReads;
        updater.Tick();
        Need(_compatibilityReads - before <= 4, "selected-map repair and rendering share display eligibility");
        var request = new LocalRaidSettings { location = ArenaMapCatalog.FactoryDayId, playerSide = ESideType.Pmc,
            transitionType = ELocationTransition.Common };
        Need(ArenaMenuSelection.TryValidateAndFreezeRequest(request, out _) && _freezes == 1,
            "valid request still freezes the freshly inspected roster");
        _refusal = "contract removed after the display pass";
        before = _compatibilityReads;
        Need(!ArenaMenuSelection.TryValidateAndFreezeRequest(request, out var reason) && reason == _refusal
            && _compatibilityReads > before && _freezes == 1,
            "final start rechecks compatibility immediately instead of trusting the display snapshot");
        Need(!ArenaMenuSelection.MatchesRequest(request), "claim boundary also stays fresh");
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.FactoryDayId]);
        Need(screen.SelectedLocation == null, "map click remains fresh when compatibility changes between refreshes");
        _refusal = null;
        Settings.GameMode.Value = ArenaModeCatalog.TeamDeathmatchId;
        updater.Tick();
        Need(screen.Buttons.Single(row => row.Location.Id == ArenaMapCatalog.CustomsId).gameObject.activeSelf,
            "Domination to TDM restores existing Customs row");
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.CustomsId]);
        Settings.GameMode.Value = ArenaModeCatalog.DominationId;
        updater.Tick();
        Need(screen.SelectedLocation == null && !screen.Buttons.Single(row => row.Location.Id == ArenaMapCatalog.CustomsId).gameObject.activeSelf,
            "switching to Domination hides and deselects unsupported map");
        Need(screen.ShowCalls == 1 && screen.SubscriptionCount == 2, "refresh never rebuilds native rows or subscriptions");
        var stale = updater.Tick;
        var customsRow = screen.Buttons.Single(row => row.Location.Id == ArenaMapCatalog.CustomsId);
        screen.Close();
        Need(customsRow.gameObject.activeSelf,
            "closing restores original native row visibility");
        before = _compatibilityReads;
        stale();
        Need(_compatibilityReads == before, "retired screen callback cannot perform another compatibility survey");
        ArenaMenuSelection.EndFlow();
        ArenaMenuSelection.RegisterCompatibilityAdmission(null);
        UnityEngine.Object.Destroy(screen.gameObject);
        Console.WriteLine($"DOMINATION_MENU_PERFORMANCE_PASSED ({_checks} assertions)");
    }
}
