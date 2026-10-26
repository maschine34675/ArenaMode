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
    private const string South = ArenaAreaCatalog.FactorySouthId, Center = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;

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
            ArenaParticipantSettings.Values.Clear(); ArenaParticipantSettings.Reject = false; ArenaParticipantSettings.Writes = 0;
            ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false; ArenaSession.Participants = null;
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
                    () => new(ArenaModeCatalog.CanSelect(Settings.SelectedGameModeId, Team),
                        Team && ArenaModeCatalog.CanSelect(Settings.SelectedGameModeId, Team), "team mode mismatch"),
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
            "Customs preflight deliberately persists its first surveyed area before selection");
        Assert(NativeHooks.Trace.Contains("native-set:" + Customs + ":" + Center),
            "incoming Customs selection uses its validated bounded identity");
        Assert(f.Screen.MaxSelectionDepth == 1, "Customs selection does not recurse into OnLocationSelected");
        Updater(f).Tick();
        Assert(ArenaAreaSelectionView.Last.State.SelectedId == Center
            && ArenaAreaSelectionView.Last.State.Options.Count == 3, "area renderer offers only three surveyed contours");
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
        Settings.TeamDeathmatchArea.Value = South;
        Settings.RejectAreaWrite = true;
        int branches = f.Screen.NonNullSelectionBranches;
        SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[Customs]),
            "rejected Customs area write enters native null branch, not its later dereference");
        Assert(f.Screen.NonNullSelectionBranches == branches && f.Screen.SelectedLocation == null,
            "failed writer cannot enter native non-null selection branch");
        Assert(Settings.TeamDeathmatchArea.Value == South && !f.Screen._readyButton.gameObject.activeSelf,
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
            "Show prefix resolves remembered whole Customs to its first surveyed area");
        Settings.GameMode.Value = ArenaModeCatalog.EndlessId;
        Updater(f).Tick();
        Assert(CustomsButton(f).gameObject.activeSelf && f.Screen.SelectedLocation?.Id == Customs,
            "F12 mode away from TDM preserves the surveyed Customs selection");
        Assert(CustomsButton(f)._spawnableToggle.Toggle.IsToggled && f.Screen._readyButton.gameObject.activeSelf,
            "supported mode switch preserves native toggle and Ready");
        Assert(ArenaModeSelectionView.Last.State.SelectedMode == ArenaModeCatalog.DeathmatchId,
            "mode view normalizes a legacy Endless F12 change to Deathmatch");
        Settings.GameMode.Value = ArenaModeCatalog.TeamDeathmatchId;
        Settings.TeamDeathmatchArea.Value = South;
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
        using var f = new Fixture(Center, Customs);
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
        Assert(!oldArea(Center), "same-screen old generation rejects an otherwise valid retained area callback");
        oldTick();
        Assert(currentUpdater != null && currentUpdater.Tick != null && !ArenaAreaSelectionView.Last.Closed,
            "stale updater generation must not destroy newer screen views");
        Assert(currentArea(Center), "current supported area callback remains usable after rejected stale callbacks");
        Assert(!currentArea(Full), "even a current callback cannot select whole Customs");
        var callbackAfterClose = ArenaAreaSelectionView.Last.Callback;
        f.Screen.Close();
        int writes = Settings.AreaWrites;
        Assert(!callbackAfterClose(Full) && Settings.AreaWrites == writes, "closed view cannot persist later area selection");
        Assert(f.Screen.GetComponentsInChildren<ArenaMapMenuUpdater>(true).Length == 0, "Close leaves no owned updater component");
    }

    private static void StrictStartAndTeamBoundaryStayStrict()
    {
        using var f = new Fixture(Full, Customs);
        Settings.TeamDeathmatchArea.Value = South;
        int writes = Settings.AreaWrites;
        var request = new LocalRaidSettings { location = Customs, playerSide = ESideType.Pmc, transitionType = ELocationTransition.Common };
        Assert(!ArenaMenuSelection.MatchesRequest(request) && Settings.AreaWrites == writes,
            "final request matching never auto-repairs stale Customs area");
        f.Team = true;
        Updater(f).Tick();
        Assert(Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId && f.Screen.SelectedLocation?.Id == Customs,
            "PFT roster refresh forces Tactical and preserves the supported Customs area");
        Assert(CustomsButton(f).gameObject.activeSelf && ArenaMenuSelection.IsMapDiscoverable(Customs),
            "PFT can discover Customs with bounded companion starts");
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
        Assert(CustomsButton(f).gameObject.activeSelf, "PFT offers the materialized Customs row with area selection");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Updater(f).Tick();
        Assert(f.Screen.SelectedLocation?.Id == Day && Settings.TeamDeathmatchArea.Value == Full,
            "repaired PFT mode can select existing whole-map Factory");
        Assert(ArenaAreaSelectionView.Last.State.Summary == "PFT squad vs 5"
            && ArenaAreaSelectionView.Last.State.ModeHint.Contains("PitFireTeam squad"),
            "PFT area state preserves five opponents and the protected squad hint");
        Assert(f.Screen.ShowCalls == 1 && f.Screen.SubscriptionCount == 2,
            "PFT repair does not rebuild native map rows or subscriptions");
    }

    private static int Main()
    {
        try
        {
            ArenaLocationSelectionPatch.Enable();
            ProtectedZombieMapSelectionKeepsReadyAndTime();
            FailedWritesRejectBeforeNonNullBranch();
            CustomsClickPreparesBeforeNativeDereference();
            FactoryTimeUsesActualVariant();
            DirectSettersAndGroupRefresh();
            RememberedMapAndF12Refresh();
            OrdinaryAndStaleOwnership();
            SameScreenGenerationAndCleanup();
            StrictStartAndTeamBoundaryStayStrict();
            PftWrongF12ModeStillMaterializesMapsBeforePostfixRepair();
            DominationMapViewsRemainOwned();
            AreaTeamSizeCallbacksStayOwned();
            EquatorOptionalDependencyAndModeTransitions();
            Bay5AndEquatorHaveIndependentOwnedRows();
            ParticipantCallbacksStayOwned();
            Console.WriteLine($"NATIVE_AREA_HOOK_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void DominationMapViewsRemainOwned()
    {
        using var f = new Fixture(mode: ArenaModeCatalog.DominationId);
        var day = f.Screen.Buttons.Single(b => b.Location.Id == Day);
        var labs = f.Screen.Buttons.Single(b => b.Location.Id == ArenaMapCatalog.LaboratoryId);
        Assert(day.gameObject.activeSelf && !labs.gameObject.activeSelf && !CustomsButton(f).gameObject.activeSelf,
            "Domination filters existing vanilla rows to Factory without destroying them");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        f.Screen.UpdateSelectedTime(EDateTime.PAST);
        Assert(f.Screen.SelectedLocation?.Id == Night && f.Screen._conditions.Open,
            "Domination retains the native Factory day/night time choice");
        Settings.GameMode.Value = ArenaModeCatalog.TeamDeathmatchId;
        Updater(f).Tick();
        Assert(labs.gameObject.activeSelf && CustomsButton(f).gameObject.activeSelf
            && f.Screen.ShowCalls == 1 && f.Screen.SubscriptionCount == 2,
            "switching from Domination restores vanilla maps without native resubscription");
        f.Screen.OnLocationSelected(f.Screen.Locations[ArenaMapCatalog.LaboratoryId]);
        Settings.GameMode.Value = ArenaModeCatalog.DominationId;
        Updater(f).Tick();
        Assert(!labs.gameObject.activeSelf && f.Screen.SelectedLocation == null,
            "switching back to Domination clears an unsupported selected vanilla map");
        ArenaClientConfig.Enabled = map => map != Day;
        Updater(f).Tick();
        Assert(day.gameObject.activeSelf, "Factory tile remains visible when only Domination night is configured");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Assert(f.Screen.SelectedLocation?.Id == Night, "Factory tile chooses the only available Domination variant");
        f.Screen.Close();
        Assert(labs.gameObject.activeSelf, "closing Arena restores the original native Labs row visibility");
    }

    private static void ProtectedZombieMapSelectionKeepsReadyAndTime()
    {
        using var f = new Fixture(mode: ArenaModeCatalog.ZombieId, team: true);
        foreach (string map in new[] { Day, Night, ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId, Customs })
        {
            // Night shares Factory's day tile; the real time toggle reaches
            // SelectedLocation directly, independently of OnLocationSelected.
            f.SettingsOwner.SelectedDateTime = map == Night ? EDateTime.PAST : EDateTime.CURR;
            SafeNative(() => f.Screen.OnLocationSelected(f.Screen.Locations[map == Night ? Day : map]),
                "protected Zombie map click safely reaches native selection: " + map);
            Assert(f.Screen.SelectedLocation?.Id == map && f.SettingsOwner.SelectedLocation?.Id == map
                && f.Screen._readyButton.gameObject.activeSelf && f.Screen._acceptButton.gameObject.activeSelf,
                "protected Zombie map keeps native selection and Ready: " + map);
            if (map is Day or Night)
                Assert(f.Screen._conditions.Open, "protected Zombie Factory exposes native time choice: " + map);
            SafeNative(f.Screen.GroupRefresh, "protected Zombie group refresh preserves native setter: " + map);
            Updater(f).Tick();
            Assert(f.Screen.SelectedLocation?.Id == map && f.Screen._readyButton.gameObject.activeSelf,
                "protected Zombie selection survives group and periodic refresh: " + map);
            var request = new LocalRaidSettings { location = map, playerSide = ESideType.Pmc,
                transitionType = ELocationTransition.Common };
            Assert(ArenaMenuSelection.MatchesRequest(request), "protected Zombie selection passes strict map request: " + map);
            Assert(ArenaAreaSelectionView.Last.State.Summary == "PitFireTeam squad"
                && !ArenaAreaSelectionView.Last.Participants.Available,
                "protected Zombie area describes its squad without opposing team or editable population: " + map);
        }
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        foreach (EDateTime time in new[] { EDateTime.PAST, EDateTime.CURR })
        {
            SafeNative(() => f.Screen.UpdateSelectedTime(time), "protected Zombie Factory native time setter stays valid");
            Assert(f.Screen.SelectedLocation?.Id == (time == EDateTime.PAST ? Night : Day)
                && f.Screen._readyButton.gameObject.activeSelf && f.Screen._conditions.Open,
                "protected Zombie Factory day/night retains Ready and time controls");
        }
        foreach (string mode in new[] { ArenaModeCatalog.TacticalId, ArenaModeCatalog.ZombieId })
        {
            Settings.GameMode.Value = mode;
            Updater(f).Tick();
            Assert(f.Screen.SelectedLocation?.Id == Day && f.Screen._readyButton.gameObject.activeSelf,
                "protected Tactical/Zombie mode switch retains valid map selection");
            Assert(ArenaAreaSelectionView.Last.State.Summary == (mode == ArenaModeCatalog.ZombieId
                ? "PitFireTeam squad" : "PFT squad vs 5"), "protected mode switch updates roster description");
        }
        foreach (string mode in new[] { ArenaModeCatalog.DeathmatchId, ArenaModeCatalog.GunGameId,
            ArenaModeCatalog.TeamDeathmatchId })
        {
            Settings.GameMode.Value = mode;
            f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
            Assert(f.Screen.SelectedLocation == null && !f.Screen._readyButton.gameObject.activeSelf
                && !f.Screen._conditions.Open, "unsupported PFT mode cannot select a map before mode repair: " + mode);
        }
        Settings.GameMode.Value = ArenaModeCatalog.ZombieId;
        foreach (string map in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id })
        {
            f.Screen.SelectedLocation = f.Screen.Locations[map];
            Assert(f.Screen.SelectedLocation == null && !f.Screen._readyButton.gameObject.activeSelf,
                "protected Zombie direct setter still rejects unavailable map: " + map);
        }
    }

    private static void ParticipantCallbacksStayOwned()
    {
        using var f = new Fixture(initialMap: Day, mode: "deathmatch");
        var view = ArenaAreaSelectionView.Last;
        var callback = view.ParticipantCallback;
        var rendered = view.Participants;
        Assert(rendered.Available && rendered.Total == 10 && rendered.MapId == Day && rendered.ModeId == "deathmatch",
            "native hook publishes map-owned participant proposal");
        Assert(callback(rendered, 13) && view.Participants.Total == 13, "native participant callback pushes saved total to view");
        Assert(!callback(rendered, 14), "stale displayed total rejects before next refresh");
        rendered = view.Participants;
        ArenaParticipantSettings.Reject = true;
        Assert(!callback(rendered, 14) && view.Participants.Total == 13, "refused persistence cannot update rendered total");
        ArenaParticipantSettings.Reject = false;
        f.Screen.OnLocationSelected(f.Screen.Locations[ArenaMapCatalog.LaboratoryId]);
        Assert(!callback(rendered, 14), "NATIVE-PARTICIPANT-MAP: map change rejects pending click before updater");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]);
        Settings.GameMode.Value = "gungame";
        Assert(!callback(rendered, 14), "NATIVE-PARTICIPANT-MODE: mode change rejects pending click before updater");
        Settings.GameMode.Value = "deathmatch";
        f.Team = true;
        Assert(!callback(rendered, 14), "new PFT roster rejects a solo participant callback");
        f.Team = false;
        ArenaClientConfig.Enabled = _ => false;
        Assert(!callback(rendered, 14), "disabled map rejects a previously rendered participant callback");
        ArenaClientConfig.Enabled = _ => true;
        ArenaSession.ArmedForNextRaid = true;
        Assert(!callback(rendered, 14), "armed participant callback cannot write");
        ArenaSession.ArmedForNextRaid = false; ArenaSession.IsActive = true;
        Assert(!callback(rendered, 14), "active participant callback cannot write");
        ArenaSession.IsActive = false;
        f.Screen.ShowInternal(f.SettingsOwner);
        Assert(!callback(rendered, 14), "NATIVE-PARTICIPANT-GENERATION: prior Show callback cannot write replacement screen");
        view = ArenaAreaSelectionView.Last; callback = view.ParticipantCallback; rendered = view.Participants;
        var originalController = f.Screen.ScreenController;
        f.Screen.ScreenController = new MatchMakerSelectionLocationScreen.SelectionLocationScreenController { RaidSettings = f.SettingsOwner };
        Assert(!callback(rendered, 14), "replaced controller rejects retained participant callback");
        f.Screen.ScreenController = originalController;
        Assert(callback(rendered, 14), "unchanged live participant owner remains writable");
        Settings.GameMode.Value = "teamdeathmatch"; Updater(f).Tick();
        rendered = view.Participants;
        Assert(rendered.Available && rendered.Step == 2, "full-map TDM receives symmetric participant controls");
        Settings.TeamDeathmatchArea.Value = Center;
        Assert(!callback(rendered, 12), "NATIVE-PARTICIPANT-AREA: changed saved area rejects old full-map click before repair");
        Settings.TeamDeathmatchArea.Value = Full;
        Assert(callback(rendered, 12), "full-map TDM accepts valid symmetric total");
        Assert(!callback(view.Participants, 13), "full-map TDM rejects odd total");
        f.Screen.OnLocationSelected(f.Screen.Locations[Customs]);
        Assert(ArenaAreaSelectionView.Last.Callback(Center), "explicit bounded choice succeeds after whole-map Customs");
        Updater(f).Tick();
        Assert(!view.Participants.Available, "bounded Customs keeps existing team-size rules");
        Assert(!callback(rendered, 12), "bounded context rejects a retained full-map proposal");
        f.Screen.OnLocationSelected(f.Screen.Locations[Day]); Updater(f).Tick();
        rendered = view.Participants;
        f.Screen.Close();
        Assert(!callback(rendered, 14), "closed screen rejects queued participant click");
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
            Assert(ArenaAreaSelectionView.Last.State.ModeHint.Contains("No Tactical or PFT Zombies"),
                "Equator area hint names its actual modes");
            foreach (string mode in new[] { "tactical", "teamdeathmatch", "gungame", "deathmatch", "zombie" })
            {
                Settings.GameMode.Value = mode;
                Updater(f).Tick();
                bool permitted = mode != "tactical";
                Assert(row.gameObject.activeSelf == permitted, "mode switch updates the owned Equator row");
                f.Screen.OnLocationSelected(f.Screen.Locations[equator]);
                Assert(permitted ? f.Screen.SelectedLocation?.Id == equator && f.Screen._readyButton.gameObject.activeSelf
                    : f.Screen.SelectedLocation == null && !f.Screen._readyButton.gameObject.activeSelf,
                    "Equator Ready and selection respect the five-opponent Tactical blocker");
            }
            f.Team = true;
            Updater(f).Tick();
            Assert(!row.gameObject.activeSelf && f.Screen.SelectedLocation == null
                && !f.Screen._readyButton.gameObject.activeSelf,
                "joining a squad invalidates an already selected solo Equator Zombie raid");
            Assert(!ArenaMenuSelection.TryValidateAndFreezeRequest(new LocalRaidSettings {
                location = equator, playerSide = ESideType.Pmc, transitionType = ELocationTransition.Common }, out _),
                "stale solo Equator selection cannot freeze as a protected Zombie squad");
            f.Team = false;
            Updater(f).Tick();
            f.Screen.OnLocationSelected(f.Screen.Locations[equator]);
            Assert(row.gameObject.activeSelf && f.Screen._readyButton.gameObject.activeSelf
                && f.Screen.SelectedLocation?.Id == equator,
                "returning to solo restores Equator Zombies without rebuilding the screen");
            ArenaClientConfig.Enabled = map => map != equator;
            Updater(f).Tick();
            Assert(!row.gameObject.activeSelf && f.Screen.SelectedLocation == null,
                "server disable hides and deselects Equator without rebuilding native screen");
            f.Screen.Close();
            ArenaClientConfig.Enabled = map => true;
            Settings.Allowlist = equator;
            Assert(ArenaMenuSelection.HasConfiguredMap(false) && !ArenaMenuSelection.HasConfiguredMap(true),
                "Equator-only configuration offers solo but no unsupported PFT format");
        }
        finally { plugins.Clear(); }
        using var ordinary = new Fixture(arena: false, mode: "tactical");
        Assert(ordinary.Screen.Buttons.Any(button => button.Location.Id == equator && button.gameObject.activeSelf),
            "ordinary native map rows stay unchanged outside Arena, even with no MapPort registry entry");
        using var ordinaryZombie = new Fixture(arena: false, mode: "zombie", team: true);
        Assert(ordinaryZombie.Screen.Buttons.Any(button => button.Location.Id == equator && button.gameObject.activeSelf),
            "saved Zombie squad preference never hides ordinary Equator selection");
    }

    private static void Bay5AndEquatorHaveIndependentOwnedRows()
    {
        const string bay5 = ArenaMapCatalog.Bay5Id, equator = ArenaMapCatalog.EquatorId;
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        plugins.Clear();
        plugins["com.maschine.ArenaMapPort.Wrong"] = new object();
        using (var absent = new Fixture(mode: "deathmatch"))
            Assert(absent.Screen.Buttons.All(button => button.Location.Id != bay5),
                "server config alone never offers Bay5 without MapPort client");
        plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        try
        {
            using var f = new Fixture(mode: "deathmatch");
            var row = f.Screen.Buttons.Single(button => button.Location.Id == bay5);
            var equatorRow = f.Screen.Buttons.Single(button => button.Location.Id == equator);
            Assert(row.gameObject.activeSelf && equatorRow.gameObject.activeSelf,
                "both installed imported maps have separate visible native rows");
            SafeNative(() => row._spawnableToggle.Toggle.IsToggled = true,
                "Bay5 click preserves native non-null selection order");
            var request = new LocalRaidSettings { location = bay5, playerSide = ESideType.Pmc,
                transitionType = ELocationTransition.Common };
            Assert(f.Screen.SelectedLocation?.Id == bay5 && ArenaMenuSelection.MatchesRequest(request),
                "Bay5 selects and passes strict final request matching");
            ArenaClientConfig.Enabled = map => map != equator;
            Updater(f).Tick();
            Assert(row.gameObject.activeSelf && !equatorRow.gameObject.activeSelf && f.Screen.SelectedLocation?.Id == bay5,
                "unavailable Equator cannot hide or deselect selected Bay5");
            Assert(ArenaAreaSelectionView.Last.State.ModeHint == string.Empty,
                "Bay5 area omits the redundant all-modes availability hint");
            ArenaClientConfig.Enabled = map => true;
            foreach (string mode in new[] { "tactical", "gungame", "teamdeathmatch", "deathmatch", "zombie" })
            {
                Settings.GameMode.Value = mode;
                Updater(f).Tick();
                Assert(row.gameObject.activeSelf && equatorRow.gameObject.activeSelf == (mode != "tactical"),
                    "mode switch updates the owned Bay5 row independently alongside Equator");
                f.Screen.OnLocationSelected(f.Screen.Locations[bay5]);
                Assert(f.Screen.SelectedLocation?.Id == bay5 && f.Screen._readyButton.gameObject.activeSelf
                    && ArenaMenuSelection.MatchesRequest(request), "every supported mode keeps Bay5 Ready and strict request");
            }
            f.Team = true;
            foreach (string squadMode in new[] { "tactical", "zombie" })
            foreach (string squadMap in new[] { bay5, equator, Customs })
            {
                Settings.GameMode.Value = squadMode;
                Updater(f).Tick();
                f.Screen.OnLocationSelected(f.Screen.Locations[squadMap]);
                bool allowed = squadMap != equator;
                Assert(allowed ? f.Screen.SelectedLocation?.Id == squadMap && f.Screen._readyButton.gameObject.activeSelf
                    : f.Screen.SelectedLocation == null && !f.Screen._readyButton.gameObject.activeSelf,
                    "protected full-map selection respects exact capacity and exposes Ready: " + squadMap + "/" + squadMode);
                if (allowed)
                    Assert(ArenaMenuSelection.MatchesRequest(new LocalRaidSettings { location = squadMap,
                        playerSide = ESideType.Pmc, transitionType = ELocationTransition.Common }),
                        "authenticated expanded map survives final PFT request validation");
                else
                    Assert(!ArenaMenuSelection.MatchesRequest(new LocalRaidSettings { location = squadMap,
                        playerSide = ESideType.Pmc, transitionType = ELocationTransition.Common }),
                        "blocked PFT Equator never reaches final request matching");
            }
            f.Team = false;
            Settings.GameMode.Value = "deathmatch";
            Updater(f).Tick();
            f.Screen.OnLocationSelected(f.Screen.Locations[equator]);
            ArenaClientConfig.Enabled = map => map != bay5;
            Updater(f).Tick();
            Assert(!row.gameObject.activeSelf && equatorRow.gameObject.activeSelf && f.Screen.SelectedLocation?.Id == equator,
                "unavailable Bay5 cannot hide or deselect selected Equator");
            ArenaClientConfig.Enabled = map => true;
            f.Screen.OnLocationSelected(f.Screen.Locations[bay5]);
            Settings.Allowlist = equator;
            Updater(f).Tick();
            Assert(!row.gameObject.activeSelf && equatorRow.gameObject.activeSelf && f.Screen.SelectedLocation == null,
                "client removal of selected Bay5 hides and deselects only its own row");
            Settings.Allowlist = bay5;
            Assert(ArenaMenuSelection.HasConfiguredMap(false) && ArenaMenuSelection.HasConfiguredMap(true),
                "Bay5-only configuration admits solo and supported PitFireTeam formats");
            Assert(f.Screen.ShowCalls == 1 && f.Screen.SubscriptionCount == 2,
                "two optional row refreshes never rebuild the native screen or duplicate subscriptions");
        }
        finally { plugins.Clear(); }
        using var ordinary = new Fixture(arena: false, mode: "tactical");
        Assert(ordinary.Screen.Buttons.Any(button => button.Location.Id == bay5 && button.gameObject.activeSelf),
            "ordinary Bay5 map selection is unchanged without loaded MapPort registry evidence");
    }
}
