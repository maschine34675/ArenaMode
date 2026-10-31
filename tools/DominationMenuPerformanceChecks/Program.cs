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

    // Every admission read, split into real evaluations (the provider runs, the DeveloperMode compat
    // timer counts it) and reads a display pass answered from its first verdict. TotalCalls is
    // cumulative, so the probe's five-second report cannot reset a reading in between.
    private readonly record struct Reads(int Admission, int Timer, int Reused, int Strict, int Mode, int EligibilityReused)
    {
        public static Reads Now() => new(_compatibilityReads, ArenaMenuSelection.CompatibilityTiming.TotalCalls,
            ArenaMenuSelection.CompatibilityReuseTiming.TotalCalls, _strictReads, _modeReads,
            ArenaMenuSelection.EligibilityReuseTiming.TotalCalls);
        public static Reads operator -(Reads a, Reads b) => new(a.Admission - b.Admission, a.Timer - b.Timer,
            a.Reused - b.Reused, a.Strict - b.Strict, a.Mode - b.Mode, a.EligibilityReused - b.EligibilityReused);
    }

    private static bool AnyRowVisible(MatchMakerSelectionLocationScreen screen)
        => screen.Buttons.Any(row => row.gameObject.activeSelf);

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
        // The DeveloperMode probe's counters double as the pass's reuse counters here.
        ArenaMenuSelection.CallTiming.Enabled = () => true;
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
        // Show ended with a refresh, i.e. a display pass. A native click after it is outside any
        // pass and must evaluate admission again rather than reuse that refresh's verdict.
        int before = _compatibilityReads;
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.FactoryDayId]);
        Need(_compatibilityReads > before && screen.SelectedLocation?.Id == ArenaMapCatalog.FactoryDayId,
            "a native click after a display pass evaluates admission fresh");
        screen.OnLocationSelected(null);
        // Each pass takes its own verdict: a refusal that arrives between two refreshes is shown by
        // the next one, and lifting it is shown by the one after.
        Need(AnyRowVisible(screen), "allowed admission shows the Arena rows");
        _refusal = "contract refused between two refreshes";
        updater.Tick();
        Need(!AnyRowVisible(screen), "a refusal that arrives between two refreshes reaches the next display pass");
        _refusal = null;
        updater.Tick();
        Need(AnyRowVisible(screen), "a lifted refusal reaches the next display pass");
        var start = Reads.Now();
        updater.Tick();
        var idle = Reads.Now() - start;
        Console.WriteLine($"MENU_PERFORMANCE_OBSERVED: rows={screen.Buttons.Count}, compatibility checks per idle refresh={idle.Admission} "
            + $"(compat timer {idle.Timer}, reads answered by the display pass {idle.Reused}; 4 per refresh without the pass), "
            + $"strict={idle.Strict}, mode={idle.Mode}, eligibility reads answered by the pass={idle.EligibilityReused}");
        Need(idle.Admission == 1 && idle.Timer == 1, "one display pass evaluates optional-mod admission exactly once");
        Need(idle.Strict <= 1 && idle.Mode <= 1 && idle.EligibilityReused == 0,
            "one display pass evaluates each eligibility source once, not once per map");
        Need(_strictReads > 0 && _modeReads > 0, "strict and roster-only mode eligibility remain distinct");
        start = Reads.Now();
        for (int i = 0; i < 20; i++) updater.Tick();
        Need((Reads.Now() - start).Admission == 20, "idle compatibility work is one check per refresh, independent of map count");
        // A refresh that throws still ends its pass (using, so a finally): a drifted native row whose
        // GameObject is gone throws inside Refresh when the refusal hides it, after the rows before it
        // were hidden. The next native click must evaluate admission fresh, and once the row is back
        // the next refresh is one pass again and shows the hidden rows again.
        int visibleRows = screen.Buttons.Count(row => row.gameObject.activeSelf);
        var drifted = screen.Buttons.Last(row => row.gameObject.activeSelf && ArenaMapCatalog.IsSupportedMap(row.Location.Id)).gameObject;
        drifted.Destroyed = true;
        _refusal = "refusal that hides a drifted row";
        Exception thrown = null;
        try { updater.Tick(); } catch (InvalidOperationException ex) { thrown = ex; }
        Need(drifted.activeSelf && thrown?.Message.StartsWith("Changed destroyed view") == true,
            "a drifted row view throws inside the refresh");
        drifted.Destroyed = false;
        _refusal = null;
        before = _compatibilityReads;
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.FactoryDayId]);
        Need(_compatibilityReads > before && screen.SelectedLocation?.Id == ArenaMapCatalog.FactoryDayId,
            "a native click after a refresh that threw evaluates admission fresh");
        screen.OnLocationSelected(null);
        start = Reads.Now();
        updater.Tick();
        var recovered = Reads.Now() - start;
        Need(recovered.Admission == 1 && recovered.Timer == 1
             && screen.Buttons.Count(row => row.gameObject.activeSelf) == visibleRows,
            "the refresh after one that threw is one display pass again and restores the rows");
        // Passes nest by depth: an inner pass shares the outer verdict and its end does not drop it;
        // only the end of the outermost pass does, so the next read outside evaluates again.
        before = _compatibilityReads;
        int insideOuter;
        using (ArenaMenuSelection.BeginDisplayPass())
        {
            _ = ArenaMenuSelection.CompatibilityRejection;
            using (ArenaMenuSelection.BeginDisplayPass()) _ = ArenaMenuSelection.CompatibilityRejection;
            _ = ArenaMenuSelection.CompatibilityRejection;
            insideOuter = _compatibilityReads - before;
        }
        _ = ArenaMenuSelection.CompatibilityRejection;
        Need(insideOuter == 1 && _compatibilityReads - before == 2,
            "a nested display pass keeps the outer verdict and only the outermost end drops it");
        screen.OnLocationSelected(screen.Locations[ArenaMapCatalog.FactoryDayId]);
        start = Reads.Now();
        updater.Tick();
        var selected = Reads.Now() - start;
        Need(selected.Admission == 1 && selected.Strict <= 1 && selected.Mode <= 1 && selected.EligibilityReused == 0,
            "selected-map repair and rendering share display eligibility");
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
