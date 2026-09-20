using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private const string Day = ArenaMapCatalog.FactoryDayId, Night = ArenaMapCatalog.FactoryNightId;
    private const string Customs = ArenaMapCatalog.CustomsId, Full = ArenaAreaCatalog.FullMapId;
    private const string South = ArenaAreaCatalog.FactorySouthId, Center = ArenaAreaCatalog.CustomsFactoryCenterPolygonId;

    private static void Assert(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("ASSERT: " + message); }
    private static void SafeNative(Action call, string assertion)
    {
        try { call(); }
        catch (Exception ex) { throw new InvalidOperationException("ASSERT: " + assertion + " (" + ex.GetType().Name + ")", ex); }
        Assert(true, assertion);
    }
    private static ArenaMapMenuUpdater Updater(Fixture fixture)
        => fixture.Screen.GetComponentsInChildren<ArenaMapMenuUpdater>(true).Single(value => value != null);
    private static LocationButton CustomsButton(Fixture fixture)
        => fixture.Screen.Buttons.Single(button => button.Location.Id == Customs);

    private sealed class Fixture : IDisposable
    {
        internal readonly RaidSettings SettingsOwner;
        internal readonly MatchMakerSelectionLocationScreen Screen;
        internal bool Team;
        internal Fixture(string area = Full, string initialMap = null, bool arena = true,
            string mode = ArenaModeCatalog.TeamDeathmatchId, ESideType side = ESideType.Pmc, bool team = false)
        {
            ArenaMenuSelection.EndFlow();
            Settings.GameMode.Value = mode; Settings.TeamDeathmatchArea.Value = area;
            Settings.TeamDeathmatchSize.Value = 7;
            Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
            Settings.AreaWrites = 0; Settings.RejectAreaWrite = false;
            ArenaClientConfig.Enabled = map => true;
            ArenaModeSelectionView.Last = null; ArenaAreaSelectionView.Last = null;
            ArenaMode.Session.Maps.ArenaAreaMapPreview.Last = null;
            ArenaMode.Plugin.ModLogger.Warnings.Clear();
            NativeHooks.Trace.Clear();
            Team = team;
            SettingsOwner = new() { Side = side, transitionType = ELocationTransition.Common, KeyId = "unchanged" };
            Screen = new GameObject("Native selection screen").AddComponent<MatchMakerSelectionLocationScreen>();
            Screen.Initialize(SettingsOwner);
            if (initialMap != null) SettingsOwner.SelectedLocation = Screen.Locations[initialMap];
            if (arena)
            {
                ArenaMenuSelection.Begin(new object(), SettingsOwner);
                ArenaMenuSelection.ConfigureEligibility(
                    () => new(!Team || Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId,
                        Team && Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId, "team mode mismatch"),
                    _ => string.Empty, () => new(true, Team, string.Empty));
                ArenaMenuSelection.Select();
            }
            Screen.ShowInternal(SettingsOwner);
        }
        public void Dispose()
        {
            Screen.Close(); ArenaMenuSelection.EndFlow(); UnityEngine.Object.Destroy(Screen.gameObject);
            Settings.RejectAreaWrite = false;
        }
    }

    private static void CustomsClickPreparesBeforeNativeDereference()
    {
        using var f = new Fixture();
        Assert(CustomsButton(f).gameObject.activeSelf, "Customs is discoverable without prior F12 selection");
        Assert(Settings.TeamDeathmatchArea.Value == Full && Settings.AreaWrites == 0,
            "merely showing the Customs tile does not persist its area");
        SafeNative(() => CustomsButton(f)._spawnableToggle.Toggle.IsToggled = true,
            "Customs click prepares its area before native non-null dereference");
        Assert(f.Screen.SelectedLocation?.Id == Customs && f.Screen.LastNativeDereference == Customs,
            "native map click reaches a non-null Customs selection");
        Assert(Settings.TeamDeathmatchArea.Value == Center && Settings.AreaWrites == 1,
            "one actual area transition despite preflight and setter checks");
        Assert(NativeHooks.Trace.IndexOf("area-write:" + Center) < NativeHooks.Trace.IndexOf("native-select:" + Customs),
            "Customs default is committed before entering native non-null branch");
        Assert(NativeHooks.Trace.Contains("native-set:" + Customs + ":" + Center),
            "incoming map rather than previous RaidSettings map owns setter repair");
        Assert(f.Screen.MaxSelectionDepth == 1, "Customs selection does not recurse into OnLocationSelected");
        Updater(f).Tick();
        Assert(ArenaAreaSelectionView.Last.State.SelectedId == Center
            && ArenaAreaSelectionView.Last.State.Summary.StartsWith("2v2"), "area renderer receives fixed bounded 2v2 state");
        Assert(f.SettingsOwner.KeyId == "unchanged", "area selection leaves unrelated raid key untouched");
    }

    private static void FactoryTimeUsesActualVariant()
    {
        using var f = new Fixture(South, Day);
        int nativeClicks = f.Screen.NonNullSelectionBranches;
        SafeNative(() => f.Screen.UpdateSelectedTime(EDateTime.PAST), "Factory time change safely passes direct setter");
        Assert(f.Screen.SelectedLocation?.Id == Night && f.SettingsOwner.SelectedDateTime == EDateTime.PAST,
            "Factory South does not suppress the configured native night variant");
        Assert(Settings.TeamDeathmatchArea.Value == Full, "night resolves to full-map before native setter");
        Assert(f.Screen.NonNullSelectionBranches == nativeClicks, "time change does not reenter map-click callback");
        Assert(NativeHooks.Trace.Contains("native-set:" + Night + ":" + Full), "night setter observes repaired full-map area");

        Settings.TeamDeathmatchArea.Value = South;
        f.SettingsOwner.SelectedDateTime = EDateTime.PAST;
        NativeHooks.Trace.Clear();
        SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[Day]),
            "Factory day tile with remembered PAST preflights actual night map");
        Assert(f.Screen.SelectedLocation?.Id == Night && Settings.TeamDeathmatchArea.Value == Full,
            "day-tile click does not restore incompatible South after time resolution");
        Assert(NativeHooks.Trace.IndexOf("area-write:" + Full) < NativeHooks.Trace.IndexOf("native-select:" + Day),
            "Factory time-dependent repair precedes native click branch");
        Assert(f.Screen.MaxSelectionDepth == 1, "Factory time repair causes no OnLocationSelected recursion");

        ArenaClientConfig.Enabled = map => map != Night;
        f.SettingsOwner.SelectedDateTime = EDateTime.PAST;
        SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[Day]), "disabled night safely resolves to configured day");
        Assert(f.Screen.SelectedLocation?.Id == Day && !f.Screen._conditions.Open,
            "single Factory variant keeps native time controls closed");
    }

    private static void FailedWritesRejectBeforeNonNullBranch()
    {
        using var f = new Fixture();
        Settings.RejectAreaWrite = true;
        int branches = f.Screen.NonNullSelectionBranches;
        SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[Customs]),
            "rejected Customs area write enters native null branch, not its later dereference");
        Assert(f.Screen.NonNullSelectionBranches == branches && f.Screen.SelectedLocation == null,
            "failed writer cannot enter native non-null selection branch");
        Assert(Settings.TeamDeathmatchArea.Value == Full && !f.Screen._readyButton.gameObject.activeSelf,
            "failed writer preserves area and leaves native Ready disabled");
        Settings.RejectAreaWrite = false;
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Settings.TeamDeathmatchArea.Value = South;
        Settings.RejectAreaWrite = true;
        f.SettingsOwner.SelectedDateTime = EDateTime.PAST;
        branches = f.Screen.NonNullSelectionBranches;
        SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[Day]),
            "rejected Factory South-to-night write is rejected before native click branch");
        Assert(f.Screen.NonNullSelectionBranches == branches && f.Screen.SelectedLocation == null,
            "time-dependent writer rejection does not become a late null dereference");
    }

    private static void DirectSettersAndGroupRefresh()
    {
        using var f = new Fixture(South, Day);
        f.Screen.SelectedLocation = f.Screen.Locations[Customs];
        Assert(f.Screen.SelectedLocation?.Id == Customs && Settings.TeamDeathmatchArea.Value == Center,
            "direct setter repairs area from incoming Customs despite old Factory settings");
        Settings.TeamDeathmatchArea.Value = Full;
        f.Screen.GroupRefresh();
        Assert(f.Screen.SelectedLocation?.Id == Customs && Settings.TeamDeathmatchArea.Value == Center,
            "group refresh passes through the same area repair");
        int writes = Settings.AreaWrites;
        f.Screen.SelectedLocation = null;
        f.Screen.GroupRefresh();
        Assert(f.Screen.SelectedLocation == null && Settings.AreaWrites == writes,
            "native group fallback to Ground Zero remains rejected without area writes");
        ArenaClientConfig.Enabled = map => map != Customs;
        f.Screen.SelectedLocation = f.Screen.Locations[Customs];
        Assert(f.Screen.SelectedLocation == null, "direct setter cannot bypass disabled Customs map");
        Assert(f.Screen.MaxSelectionDepth == 1, "direct and group setters never recurse via map click");
    }

    private static void RememberedMapAndF12Refresh()
    {
        using var f = new Fixture(Full, Customs);
        Assert(f.Screen.SelectedLocation?.Id == Customs && Settings.TeamDeathmatchArea.Value == Center,
            "Show prefix repairs remembered Customs before strict map filtering");
        Settings.GameMode.Value = ArenaModeCatalog.EndlessId;
        Updater(f).Tick();
        Assert(!CustomsButton(f).gameObject.activeSelf && f.Screen.SelectedLocation == null,
            "F12 mode away from TDM hides Customs and clears native selection");
        Assert(!CustomsButton(f)._spawnableToggle.Toggle.IsToggled && !f.Screen._readyButton.gameObject.activeSelf,
            "mode invalidation clears native toggle and Ready");
        Assert(ArenaModeSelectionView.Last.State.SelectedMode == ArenaModeCatalog.DeathmatchId,
            "mode view normalizes a legacy Endless F12 change to Deathmatch");
        Settings.GameMode.Value = ArenaModeCatalog.TeamDeathmatchId;
        Settings.TeamDeathmatchArea.Value = Full;
        Updater(f).Tick();
        Assert(CustomsButton(f).gameObject.activeSelf, "F12 return to TDM rediscovers Customs without setting area");
        f.Screen.OnLocationSelected(f.Screen.Locations[Customs]);
        var callback = ArenaAreaSelectionView.Last.Callback;
        Assert(callback(ArenaAreaCatalog.CustomsFactoryEastPolygonId), "owned native callback accepts the second surveyed Customs area");
        Assert(Settings.TeamDeathmatchArea.Value == ArenaAreaCatalog.CustomsFactoryEastPolygonId && f.Screen.SelectedLocation?.Id == Customs,
            "area choice changes the area without reselecting the native map");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Assert(!ArenaAreaSelectionView.Last.Callback(South), "owned callback rejects retired Factory South on Factory day");
        Settings.TeamDeathmatchArea.Value = Full;
        Updater(f).Tick();
        Assert(ArenaAreaSelectionView.Last.State.SelectedId == Full
            && ArenaAreaSelectionView.Last.State.Summary.StartsWith("7v7"), "F12 full-map choice restores configured team size in area view");
        Assert(f.Screen.ShowCalls == 1 && f.Screen.SubscriptionCount == 2,
            "mode and area refresh never rerun native ShowInternal or append subscriptions");
    }

    private static void OrdinaryAndStaleOwnership()
    {
        foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
        {
            using var f = new Fixture(South, Customs, arena: false, side: side);
            f.Screen.OnLocationSelected(f.Screen.Locations[Customs]);
            f.Screen.GroupRefresh();
            Assert(f.Screen.SelectedLocation?.Id == Customs && Settings.TeamDeathmatchArea.Value == South
                && Settings.AreaWrites == 0, "ordinary " + side + " callbacks leave Arena settings unchanged");
            Assert(f.Screen.GetComponentsInChildren<ArenaMapMenuUpdater>(true).Length == 0
                && ArenaAreaSelectionView.Last == null && ArenaMode.Session.Maps.ArenaAreaMapPreview.Last == null,
                "ordinary " + side + " gets no owned Arena view or updater");
            Assert(f.Screen._marathonButton.gameObject.activeSelf, "ordinary " + side + " retains Marathon visibility");
        }
        using var active = new Fixture(Full, Day);
        var oldAreaCallback = ArenaAreaSelectionView.Last.Callback;
        var oldUpdater = Updater(active);
        var oldTick = oldUpdater.Tick;
        active.Screen.ScreenController = new() { RaidSettings = new() { Side = ESideType.Pmc } };
        int writesBefore = Settings.AreaWrites;
        Assert(!oldAreaCallback(Full) && Settings.AreaWrites == writesBefore, "stale controller rejects an otherwise valid retained area callback");
        oldTick();
        Assert(oldUpdater == null && ArenaAreaSelectionView.Last.Closed, "old controller updater releases only its owned views");
    }

    private static void SameScreenGenerationAndCleanup()
    {
        using var f = new Fixture(Full, Day);
        var preview = ArenaMode.Session.Maps.ArenaAreaMapPreview.Last;
        int previewShows = preview.Shows;
        for (int i = 0; i < 20; i++) Updater(f).Tick();
        Assert(preview.Shows == previewShows && preview.Refreshes >= 20,
            "periodic refresh updates the preview without reopening or reallocating its owner");
        var oldArea = ArenaAreaSelectionView.Last.Callback;
        var oldTick = Updater(f).Tick;
        f.Screen.Close();
        Assert(ArenaAreaSelectionView.Last.Closed && ArenaModeSelectionView.Last.Closed && preview.Closed,
            "native Close disposes area, mode and map preview views");
        f.Screen.ShowInternal(f.SettingsOwner);
        Assert(!preview.Closed && preview.Shows == previewShows + 1, "same-screen reopen acquires the preview once");
        var currentUpdater = Updater(f);
        var currentArea = ArenaAreaSelectionView.Last.Callback;
        Assert(!oldArea(Full), "same-screen old generation rejects an otherwise valid retained area callback");
        oldTick();
        Assert(currentUpdater != null && currentUpdater.Tick != null && !ArenaAreaSelectionView.Last.Closed,
            "stale updater generation must not destroy newer screen views");
        Assert(currentArea(Full), "current supported area callback remains usable after rejected stale callbacks");
        var callbackAfterClose = ArenaAreaSelectionView.Last.Callback;
        f.Screen.Close();
        int writes = Settings.AreaWrites;
        Assert(!callbackAfterClose(Full) && Settings.AreaWrites == writes, "closed view cannot persist later area selection");
        Assert(f.Screen.GetComponentsInChildren<ArenaMapMenuUpdater>(true).Length == 0, "Close leaves no owned updater component");
    }

    private static void StrictStartAndTeamBoundaryStayStrict()
    {
        using var f = new Fixture(Full, Customs);
        Settings.TeamDeathmatchArea.Value = Full;
        int writes = Settings.AreaWrites;
        var request = new LocalRaidSettings { location = Customs, playerSide = ESideType.Pmc, transitionType = ELocationTransition.Common };
        Assert(!ArenaMenuSelection.MatchesRequest(request) && Settings.AreaWrites == writes,
            "final request matching never auto-repairs stale Customs area");
        f.Team = true;
        Updater(f).Tick();
        Assert(Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId && f.Screen.SelectedLocation == null,
            "PFT roster refresh forces Tactical and removes Customs selection");
        Assert(!CustomsButton(f).gameObject.activeSelf && !ArenaMenuSelection.IsMapDiscoverable(Customs),
            "PFT cannot discover or keep bounded-only Customs");
    }

    private static void PftWrongF12ModeStillMaterializesMapsBeforePostfixRepair()
    {
        // Exact distinction from production PFT eligibility: strict mode-aware
        // discovery denies the wrong saved mode; roster-only picker evidence
        // remains a complete team. Native creates map rows before postfix Show.
        using var f = new Fixture(mode: ArenaModeCatalog.EndlessId, team: true);
        var visible = f.Screen.Buttons.Where(button => button.gameObject.activeSelf).Select(button => button.Location.Id).ToArray();
        Assert(visible.Contains(Day) && visible.Contains(ArenaMapCatalog.LaboratoryId)
            && visible.Contains(ArenaMapCatalog.LabyrinthId),
            "PFT wrong saved mode must not prevent native map materialization before postfix mode repair");
        Assert(Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId
            && ArenaModeSelectionView.Last.State.TacticalOnly, "roster-only evidence repairs PFT mode without weakening final eligibility");
        Assert(!CustomsButton(f).gameObject.activeSelf, "PFT still hides materialized Customs row");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Updater(f).Tick();
        Assert(f.Screen.SelectedLocation?.Id == Day && Settings.TeamDeathmatchArea.Value == Full,
            "repaired PFT mode can select existing whole-map Factory");
        Assert(ArenaAreaSelectionView.Last.State.Summary.Contains("5v5")
            && ArenaAreaSelectionView.Last.State.ModeHint.Contains("Tactical"),
            "PFT area renderer receives actual fixed team size and Tactical restriction");
        Assert(f.Screen.ShowCalls == 1 && f.Screen.SubscriptionCount == 2,
            "PFT repair does not rebuild native map rows or subscriptions");
    }

    private static int Main()
    {
        try
        {
            ArenaLocationSelectionPatch.Enable();
            FailedWritesRejectBeforeNonNullBranch();
            CustomsClickPreparesBeforeNativeDereference();
            FactoryTimeUsesActualVariant();
            DirectSettersAndGroupRefresh();
            RememberedMapAndF12Refresh();
            OrdinaryAndStaleOwnership();
            SameScreenGenerationAndCleanup();
            StrictStartAndTeamBoundaryStayStrict();
            PftWrongF12ModeStillMaterializesMapsBeforePostfixRepair();
            AreaTeamSizeCallbacksStayOwned();
            EquatorOptionalDependencyAndModeTransitions();
            Console.WriteLine($"NATIVE_AREA_HOOK_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void AreaTeamSizeCallbacksStayOwned()
    {
        const string teams = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
        using var f = new Fixture(teams, Customs);
        Settings.AreaTeamSize.Value = 2;
        f.Screen.ShowInternal(f.SettingsOwner);
        var callback = ArenaAreaSelectionView.Last.SizeCallback;
        Assert(ArenaAreaSelectionView.Last.State.Options.Count == 3
            && ArenaAreaSelectionView.Last.State.HasTeamSizeChoice,
            "native area adapter publishes three Customs choices and size state");
        Assert(callback(teams, 5) && Settings.AreaTeamSize.Value == 5
            && ArenaAreaSelectionView.Last.State.SelectedTeamSize == 5, "native size callback pushes confirmed selection back into view");
        Assert(!callback(teams, 6) && Settings.AreaTeamSize.Value == 5, "native size callback rejects unsupported count");
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
        Assert(!callback(teams, 3), "retained callback rejects changed area before next periodic refresh");
        Settings.TeamDeathmatchArea.Value = teams;
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Assert(!callback(teams, 3), "retained callback rejects changed map");
        f.Screen.OnLocationSelected(f.Screen.Locations[Customs]);
        Settings.TeamDeathmatchArea.Value = teams;
        f.Screen.ShowInternal(f.SettingsOwner);
        Assert(!callback(teams, 4), "old Show generation cannot change new area team size");
        var current = ArenaAreaSelectionView.Last.SizeCallback;
        Assert(current(teams, 4), "replacement Show generation has independent live callback");
        f.Screen.Close();
        Assert(!current(teams, 2), "closed native screen rejects deferred size click");
    }

    private static void EquatorOptionalDependencyAndModeTransitions()
    {
        const string equator = ArenaMapCatalog.EquatorId;
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        plugins.Clear();
        using (var absent = new Fixture(mode: "deathmatch"))
            Assert(absent.Screen.Buttons.All(button => button.Location.Id != equator),
                "server config alone never offers Equator without MapPort client");
        plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        try
        {
            using var f = new Fixture(mode: "deathmatch");
            var row = f.Screen.Buttons.Single(button => button.Location.Id == equator);
            Assert(row.gameObject.activeSelf, "installed Equator is visible in Deathmatch");
            SafeNative(() => row._spawnableToggle.Toggle.IsToggled = true, "Equator click preserves native selection order");
            Assert(f.Screen.SelectedLocation?.Id == equator && ArenaMenuSelection.IsMapAvailable(equator),
                "Equator selects full map through strict admission");
            Updater(f).Tick();
            Assert(ArenaAreaSelectionView.Last.State.ModeHint == "Deathmatch and Gun Game",
                "Equator area hint names its actual modes");
            foreach (string mode in new[] { "tactical", "teamdeathmatch", "gungame", "deathmatch" })
            {
                Settings.GameMode.Value = mode;
                Updater(f).Tick();
                bool permitted = mode is "gungame" or "deathmatch";
                Assert(row.gameObject.activeSelf == permitted, "mode switch updates the owned Equator row");
                if (!permitted)
                {
                    Assert(f.Screen.SelectedLocation == null, "unsupported mode clears remembered Equator selection");
                    f.Screen.OnLocationSelected(f.Screen.Locations[equator]);
                    Assert(f.Screen.SelectedLocation == null, "direct unsupported map click fails closed");
                }
                else f.Screen.OnLocationSelected(f.Screen.Locations[equator]);
            }
            ArenaClientConfig.Enabled = map => map != equator;
            Updater(f).Tick();
            Assert(!row.gameObject.activeSelf && f.Screen.SelectedLocation == null,
                "server disable hides and deselects Equator without rebuilding native screen");
            f.Screen.Close();
            ArenaClientConfig.Enabled = map => true;
            Settings.Allowlist = equator;
            Assert(ArenaMenuSelection.HasConfiguredMap(false) && !ArenaMenuSelection.HasConfiguredMap(true),
                "Equator-only configuration offers solo Arena but never PFT");
        }
        finally { plugins.Clear(); }
        using var ordinary = new Fixture(arena: false, mode: "tactical");
        Assert(ordinary.Screen.Buttons.Any(button => button.Location.Id == equator && button.gameObject.activeSelf),
            "ordinary native map rows stay unchanged outside Arena, even with no MapPort registry entry");
    }
}
