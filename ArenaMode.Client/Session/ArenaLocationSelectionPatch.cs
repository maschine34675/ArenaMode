using ArenaMode.Config;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;
using ArenaMode.Shared;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace ArenaMode.Session;

/// <summary>Filter native views, never the session's shared location database.</summary>
internal static class ArenaLocationSelectionPatch
{
    private static readonly AccessTools.FieldRef<MatchMakerSelectionLocationScreen,
        MatchMakerSelectionLocationScreen.SelectionLocationScreenController> Controller =
        AccessTools.FieldRefAccess<MatchMakerSelectionLocationScreen,
            MatchMakerSelectionLocationScreen.SelectionLocationScreenController>("ScreenController");

    private sealed class ScreenState
    {
        public readonly ArenaLocationSelectionView Map = new();
        public readonly ArenaModeSelectionView Modes = new();
        public readonly ArenaAreaSelectionView Areas = new();
        public readonly Maps.ArenaAreaMapPreview Preview = new();
        public readonly ArenaCompactMapInfo Info = new();
        public readonly List<ArenaPortedMapPresentation> PortedMaps = new();
        public readonly List<(string MapId, ArenaConditionalMapView View)> ConditionalMaps = new();
        public ArenaMapMenuUpdater Updater;
        public object Generation;
        public TextMeshProUGUI SelectionHint;
        private string _invalidMap;
        private readonly Dictionary<TMP_Text, string> _timeLabels = new();
        // Shared only by native row materialization and one synchronous refresh.
        // Native selection/claim/start callbacks never consume this snapshot.
        public ArenaMapAvailabilitySnapshot Availability;

        // DeveloperMode-only timing of one refresh, reported every five seconds as the
        // average and maximum per section. Nothing is measured when the mode is off.
        // Added 31.10.2026 to attribute the map-screen frame-time spikes that ModProfiler
        // pins on ArenaMapMenuUpdater.Update (110-210 ms per tick on GPU-2).
        private readonly RefreshTiming _timing = new();

        /// <summary>
        /// One refresh is one display pass (ArenaMenuSelection.BeginDisplayPass, 2026-10-31): the
        /// optional-mod admission and each eligibility provider run once, however often the
        /// refresh and the side screen's providers read them. Everything inside is the synchronous
        /// display work the 26.10 snapshot already shared one verdict across: row visibility, the
        /// selected map's area preparation through the snapshot, the existing display repairs of a
        /// saved mode (Modes.Synchronize) and area (TryPrepareMapArea), and the deselection of a map
        /// that became unavailable, whose native prefixes see a null map and so cannot depend on the
        /// verdict. No click, claim, freeze or start runs inside it; those arrive later from native
        /// UI events, outside any pass, and evaluate fresh.
        /// </summary>
        public void Refresh(MatchMakerSelectionLocationScreen screen)
        {
            using (ArenaMenuSelection.BeginDisplayPass())
                RefreshInPass(screen);
        }

        private void RefreshInPass(MatchMakerSelectionLocationScreen screen)
        {
            var timing = ArenaMenuSelection.CallTiming.IsEnabled ? _timing : null;
            timing?.Begin();
            foreach (var presentation in PortedMaps) presentation.Refresh();
            timing?.Mark("ported");
            var eligibility = ArenaMenuSelection.CurrentModeEligibility;
            timing?.Mark("modeEligibility");
            Modes.Synchronize(Settings.GameMode?.Value, eligibility.Allowed && eligibility.IsTeam);
            timing?.Mark("modes");
            // Synchronize may repair a saved PFT mode, so inspect strict map
            // eligibility afterwards and share it across every existing row.
            var availability = Availability = new ArenaMapAvailabilitySnapshot(ArenaMenuSelection.CurrentEligibility);
            timing?.Mark("eligibility");
            var previousMap = screen.SelectedLocation?.Id;
            foreach (var row in ConditionalMaps)
                row.View.Refresh(ArenaMapCatalog.CanSelectNativeLocation(row.MapId, availability.IsMapDiscoverable),
                    string.Equals(screen.SelectedLocation?.Id, row.MapId, System.StringComparison.OrdinalIgnoreCase));
            timing?.Mark("rows");
            var map = screen.SelectedLocation?.Id;
            if (map != null && !availability.TryPrepareMapArea(Controller(screen)?.RaidSettings, map))
                screen.OnLocationSelected(null);
            if (previousMap != null && screen.SelectedLocation == null) _invalidMap = previousMap;
            if (screen.SelectedLocation != null || _invalidMap != null && availability.IsMapDiscoverable(_invalidMap))
                _invalidMap = null;
            if (SelectionHint != null)
            {
                SelectionHint.text = _invalidMap == null ? string.Empty
                    : ArenaMapCatalog.IsEquator(_invalidMap) ? "Equator: too small for this mode. Choose a map."
                    : "Map unavailable in this mode. Choose another.";
                SelectionHint.gameObject.SetActive(_invalidMap != null);
            }
            timing?.Mark("prepare");
            var areas = availability.AreaSelection(screen.SelectedLocation?.Id);
            var participants = ArenaParticipantSelectionState.Create(areas, eligibility.IsTeam,
                ArenaParticipantSettings.Get(areas.MapId, areas.ModeId));
            Areas.Refresh(areas, eligibility.Allowed && eligibility.IsTeam, participants);
            timing?.Mark("areas");
            Info.Refresh(ArenaPortedMapPresentation.DisplayName(screen.SelectedLocation?.Id,
                screen.SelectedLocation?.LocalizedName), screen._infoPanel?._banner?.sprite);
            timing?.Mark("info");
            Preview.Refresh(areas);
            timing?.Mark("preview");
            timing?.End();
        }

        private sealed class RefreshTiming
        {
            private static readonly double TicksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            private readonly List<string> _order = new();
            private readonly Dictionary<string, double> _sum = new(), _max = new();
            private long _start, _last, _windowStart;
            private int _ticks;
            private double _maxTotal;

            public void Begin()
            {
                _start = _last = System.Diagnostics.Stopwatch.GetTimestamp();
                if (_windowStart == 0) _windowStart = _start;
            }

            public void Mark(string section)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double ms = (now - _last) * TicksToMs;
                _last = now;
                if (!_sum.ContainsKey(section)) { _order.Add(section); _sum[section] = 0; _max[section] = 0; }
                _sum[section] += ms;
                if (ms > _max[section]) _max[section] = ms;
            }

            public void End()
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double total = (now - _start) * TicksToMs;
                _ticks++;
                if (total > _maxTotal) _maxTotal = total;
                if ((now - _windowStart) * TicksToMs < 5000) return;
                var parts = new System.Text.StringBuilder();
                foreach (var section in _order)
                    parts.Append(parts.Length == 0 ? string.Empty : ", ")
                        .Append(section).Append(' ').Append((_sum[section] / _ticks).ToString("F1"))
                        .Append('/').Append(_max[section].ToString("F1"));
                // Info on purpose, like ArenaLog.Developer: DeveloperMode alone must make the probe visible.
                Plugin.ModLogger.LogInfo($"[dev] Arena map menu refresh: {_ticks} ticks in 5 s, max {_maxTotal:F1} ms; avg/max per section (ms): {parts}; "
                    + ArenaMenuSelection.CallTiming.ReportAllAndReset() + ".");
                _sum.Clear(); _max.Clear(); _order.Clear();
                _ticks = 0; _maxTotal = 0; _windowStart = now;
            }
        }

        public void ShowSelectionHint(Transform parent, TMP_Text typography)
        {
            if (typography == null) return;
            var go = new GameObject("Arena Map Selection Hint", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            SelectionHint = go.GetComponent<TextMeshProUGUI>();
            SelectionHint.font = typography.font;
            SelectionHint.fontSharedMaterial = typography.fontSharedMaterial;
            SelectionHint.fontSize = 13f;
            SelectionHint.color = new Color(.95f, .77f, .46f, 1f);
            SelectionHint.enableWordWrapping = true;
            SelectionHint.richText = false;
            SelectionHint.raycastTarget = false;
            var rect = SelectionHint.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(56f, -134f);
            rect.sizeDelta = new Vector2(360f, 30f);
            go.SetActive(false);
        }

        public void LabelFactoryTime(MatchMakerSelectionLocationScreen screen)
        {
            RestoreTimeLabels();
            if ((!ArenaMapCatalog.IsFactoryVariant(screen.SelectedLocation?.Id)
                && !ArenaMapCatalog.IsEquator(screen.SelectedLocation?.Id)) || screen._conditions == null) return;
            SetTimeLabel(screen._conditions._currentPhaseTime, "DAY");
            SetTimeLabel(screen._conditions._nextPhaseTime, "NIGHT");
        }

        private void SetTimeLabel(TMP_Text label, string text)
        {
            if (label == null) return;
            _timeLabels[label] = label.text;
            label.text = text;
        }

        public void RestoreTimeLabels()
        {
            foreach (var pair in _timeLabels)
                if (pair.Key != null) pair.Key.text = pair.Value;
            _timeLabels.Clear();
        }

        public void Restore()
        {
            Generation = null;
            _invalidMap = null;
            RestoreTimeLabels();
            if (SelectionHint != null) UnityEngine.Object.Destroy(SelectionHint.gameObject);
            SelectionHint = null;
            Availability = null;
            foreach (var presentation in PortedMaps) presentation.Close();
            PortedMaps.Clear();
            Preview.Close();
            Info.Close();
            if (Updater != null)
            {
                Updater.Tick = null;
                Updater.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(Updater.gameObject);
                Updater = null;
            }
            foreach (var row in ConditionalMaps) row.View.Close();
            ConditionalMaps.Clear();
            Areas.Close();
            NotifyModePanelClosing(Modes.Root);
            Modes.Close();
            Map.Restore();
        }
    }

    private static readonly ConditionalWeakTable<MatchMakerSelectionLocationScreen, ScreenState> States = new();

    /// <summary>
    /// Companions of the Arena map screen that own no patch (the kit screen,
    /// docs/reviews/KIT-PICKER-20261110.md): told once the mode panel of a screen stands, with
    /// the screen, the panel root and the native typography, and told again before that panel
    /// is torn down. A companion that throws is logged and never fails the map screen.
    /// </summary>
    internal static event System.Action<Transform, GameObject, TMP_Text> ModePanelShown;
    internal static event System.Action<GameObject> ModePanelClosing;

    private static void NotifyModePanelShown(MatchMakerSelectionLocationScreen screen, GameObject panel)
    {
        if (screen == null || panel == null) return;
        try { ModePanelShown?.Invoke(screen.transform, panel, screen._acceptButton?._headerLabel); }
        catch (System.Exception ex) { Plugin.ModLogger.LogWarning($"Arena map-screen companion failed to open: {ex.Message}"); }
    }

    private static void NotifyModePanelClosing(GameObject panel)
    {
        if (panel == null) return;
        try { ModePanelClosing?.Invoke(panel); }
        catch (System.Exception ex) { Plugin.ModLogger.LogWarning($"Arena map-screen companion failed to close: {ex.Message}"); }
    }

    public static void Enable()
    {
        new ArenaLocationSelectionShowPatch().Enable();
        new ArenaLocationSelectionDisplayPatch().Enable();
        new ArenaLocationSelectionSelectionPatch().Enable();
        new ArenaLocationSelectionSelectedValuePatch().Enable();
        new ArenaLocationSelectionButtonLocationPatch().Enable();
        new ArenaLocationSelectionFactoryTimePatch().Enable();
        new ArenaLocationSelectionClosePatch().Enable();
    }

    private static bool IsArena(MatchMakerSelectionLocationScreen screen)
        => ArenaMenuSelection.IsSelectedFor(Controller(screen)?.RaidSettings);

    private static void Restore(MatchMakerSelectionLocationScreen screen)
    {
        if (States.TryGetValue(screen, out var state))
            state.Restore();
    }

    private sealed class ArenaLocationSelectionShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.ShowInternal));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {
            Restore(__instance);
            ArenaMenuSelection.ResumeAfterMenu(raidSettings);
            if (!IsArena(__instance))
                return;

            // Clear Marathon and any unsupported remembered map before vanilla
            // creates views or enables Next/Ready. A supported Factory variant
            // keeps its matching CURR/PAST value when returning from the next screen.
            raidSettings.transitionType = ELocationTransition.Common;
            if (!ArenaMenuSelection.TryPrepareMapArea(raidSettings, raidSettings.SelectedLocation?.Id))
            {
                raidSettings.SelectedLocation = null;
                raidSettings.SelectedDateTime = EDateTime.CURR;
            }
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance)
        {
            if (!IsArena(__instance))
                return;

            var state = States.GetOrCreateValue(__instance);
            state.Map.HideExtras(__instance._locationPathsContainer, __instance._marathonButton.gameObject);

            // Own a single updater for this exact native controller. This also
            // observes direct F12 changes. Display-time stored-profile HTTP is
            // cached across these ticks; freeze re-authenticates before arming.
            var owner = Controller(__instance);
            var generation = state.Generation = new object();
            var updaterRoot = new GameObject("Arena Conditional Map Updater");
            updaterRoot.transform.SetParent(__instance.transform, false);
            state.Updater = updaterRoot.AddComponent<ArenaMapMenuUpdater>();
            state.Updater.Tick = () =>
            {
                // A retired callback cannot dispose a replacement panel owned
                // by a newer Show, even when Unity delivers it late.
                if (!ReferenceEquals(state.Generation, generation)) return;
                if (__instance == null || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance))
                { state.Restore(); return; }
                state.Refresh(__instance);
            };

            try
            {
                state.ShowSelectionHint(__instance.transform, __instance._acceptButton?._headerLabel);
                // Show is deliberately not a display pass; only the Refresh that ends this block is.
                // The prefix resumes a suspended flow (ResumeAfterMenu: fresh admission by contract)
                // and repairs the remembered map through the selection path, vanilla ShowInternal
                // assigns the remembered selection through the setter prefix, Modes.Show commits a
                // mode repair, and Areas.Show installs the click callbacks. Rows already share one
                // snapshot (state.Availability), so a pass here would save one read per screen open
                // while widening it over a mode commit. Outside any pass, one open with a remembered
                // map makes about eight strict reads (two in the prefix's map repair, about five in
                // the OnLocationSelected and SelectedLocation setter prefixes vanilla runs, one for
                // the row snapshot, more for a Labyrinth row) plus this mode read, and each of them
                // evaluates the optional-mod admission twice (directly and in the side screen's
                // provider); the refresh pass then adds one evaluation. What that costs with Core
                // 1.0.19, whose absent adapters no longer unpatch per check, is still to be measured
                // natively.
                var eligibility = ArenaMenuSelection.CurrentModeEligibility;
                state.Modes.Show(
                    __instance.transform,
                    __instance._acceptButton?._headerLabel,
                    Settings.GameMode?.Value,
                    eligibility.Allowed && eligibility.IsTeam,
                    Settings.TrySelectGameMode);
                state.Areas.Show(__instance.transform, __instance._acceptButton?._headerLabel, areaId =>
                {
                    if (__instance == null || !ReferenceEquals(state.Generation, generation)
                        || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance)) return false;
                    var accepted = ArenaMenuSelection.TrySelectArea(owner.RaidSettings, __instance.SelectedLocation?.Id, areaId);
                    if (accepted) state.Refresh(__instance);
                    return accepted;
                }, (areaId, size) =>
                {
                    if (__instance == null || !ReferenceEquals(state.Generation, generation)
                        || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance)) return false;
                    var accepted = ArenaMenuSelection.TrySelectAreaTeamSize(owner.RaidSettings,
                        __instance.SelectedLocation?.Id, areaId, size);
                    if (accepted) state.Refresh(__instance);
                    return accepted;
                }, (rendered, total) =>
                {
                    if (__instance == null || !ReferenceEquals(state.Generation, generation)
                        || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance)
                        || rendered?.Available != true
                        || !string.Equals(__instance.SelectedLocation?.Id, rendered.MapId, System.StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(owner.RaidSettings.SelectedLocation?.Id, rendered.MapId, System.StringComparison.OrdinalIgnoreCase)
                        || Settings.SelectedGameModeId != rendered.ModeId
                        || Settings.TeamDeathmatchArea?.Value != rendered.AreaId) return false;
                    var current = ArenaMenuSelection.AreaSelection(rendered.MapId);
                    var live = ArenaParticipantSelectionState.Create(current, ArenaMenuSelection.CurrentEligibility.IsTeam,
                        ArenaParticipantSettings.Get(current.MapId, current.ModeId));
                    if (!rendered.SameContext(live) || !live.Available || live.Total != rendered.Total) return false;
                    var accepted = ArenaParticipantSettings.TrySelect(rendered.MapId, rendered.ModeId, total);
                    if (accepted) state.Refresh(__instance);
                    return accepted;
                });
                state.Info.Show(__instance.transform, __instance._infoPanel?.gameObject,
                    __instance._acceptButton?._headerLabel);
                state.Preview.Show(__instance.transform, state.Info.Root,
                    __instance._acceptButton?._headerLabel);
                state.Refresh(__instance);
                NotifyModePanelShown(__instance, state.Modes.Root);
            }
            catch (System.Exception ex)
            {
                state.Preview.Close();
                state.Info.Close();
                NotifyModePanelClosing(state.Modes.Root);
                state.Modes.Close();
                state.Areas.Close();
                // The native map screen remains fully usable if our optional
                // picker cannot be constructed on an unexpected UI layout.
                Plugin.ModLogger.LogWarning($"Arena map-screen mode picker unavailable: {ex.Message}");
            }

            var buttons = __instance._locationButtonsContainer.GetComponentsInChildren<LocationButton>(true);
            Plugin.ModLogger.LogDebug($"Arena map view: buttons={buttons.Length}, "
                + $"visible={buttons.Count(button => button.gameObject.activeInHierarchy)}, "
                + $"sharedContainer={__instance._locationButtonsContainer == __instance._locationPathsContainer}, "
                + $"mode={Settings.SelectedGameModeId}");
        }
    }

    private sealed class ArenaLocationSelectionDisplayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.DisplayLocation));

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance, LocationSettings.Location location, ref bool __result)
        {
            if (IsArena(__instance))
                // Labyrinth ships with Enabled=false. The server's Arena
                // allowlist is authoritative for THIS view; normal raids still
                // use vanilla DisplayLocation and retain that restriction.
                // Factory night remains the time choice on the single day tile.
                // Materialize configured rows before the mode view exists. A
                // PFT roster with a stale F12 mode is repaired only in Postfix;
                // strict discovery here would permanently omit all its rows.
                // Mode-dependent map visibility and all selections are guarded separately.
                __result = ArenaMapCatalog.ShouldDisplayNativeLocation(
                    location?.Id, ArenaMenuSelection.IsMapConfigured);
        }
    }

    private sealed class ArenaLocationSelectionSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.OnLocationSelected));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, ref LocationSettings.Location location)
        {
            if (States.TryGetValue(__instance, out var state)) state.RestoreTimeLabels();
            if (!IsArena(__instance)) return;
            if (!ArenaMapCatalog.CanSelectNativeLocation(location?.Id, ArenaMenuSelection.IsMapDiscoverable))
            { location = null; return; }
            // Resolve the actual Factory time before entering vanilla's non-null
            // branch, which dereferences SelectedLocation after its setter.
            // An area write failure must reach vanilla as a null input instead.
            var settings = Controller(__instance)?.RaidSettings;
            var target = __instance.GetLocationByTime(location, settings.SelectedDateTime);
            if (!ArenaMenuSelection.TryPrepareMapArea(settings, target?.Id)) location = null;
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance)
        {
            if (IsArena(__instance)) States.GetOrCreateValue(__instance).LabelFactoryTime(__instance);
            if (IsArena(__instance)
                && ArenaMapCatalog.IsFactoryVariant(__instance.SelectedLocation?.Id)
                && !ArenaMapCatalog.HasFactoryTimeChoice(
                    ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryDayId),
                    ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryNightId)))
                __instance._conditions.Close();
        }
    }

    private sealed class ArenaLocationSelectionSelectedValuePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertySetter(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.SelectedLocation));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, ref LocationSettings.Location value)
        {
            // Group/level refreshes assign this property directly and can restore
            // Ground Zero even after a null selection. Guard that path as well.
            if (IsArena(__instance))
            {
                if (!ArenaMenuSelection.TryPrepareMapArea(Controller(__instance)?.RaidSettings, value?.Id))
                    value = null;
                else
                    value = ArenaLabyrinthAccess.Project(Controller(__instance)?.RaidSettings, value);
            }
        }
    }

    private sealed class ArenaLocationSelectionButtonLocationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(LocationButton), nameof(LocationButton.Show), new[]
            {
                typeof(LocationSettings.Location), typeof(ESideType), typeof(bool), typeof(int),
                typeof(bool), typeof(bool), typeof(System.Action<LocationSettings.Location>),
                typeof(bool), typeof(bool),
            });

        [PatchPrefix]
        private static void Prefix(ref LocationSettings.Location location, ref bool onAtStart,
            System.Action<LocationSettings.Location> onSelected)
        {
            // Other menus use LocationButton too. Only the callback owned by
            // this exact Arena map screen may receive projected metadata.
            if (onSelected?.Target is not MatchMakerSelectionLocationScreen screen
                || onSelected.Method.Name != nameof(MatchMakerSelectionLocationScreen.OnLocationSelected)
                || onSelected.Method.DeclaringType != typeof(MatchMakerSelectionLocationScreen)
                || !IsArena(screen))
                return;

            if (!ArenaLabyrinthAccess.CanProject(Controller(screen)?.RaidSettings, location))
                return;
            location = ArenaLabyrinthAccess.Project(Controller(screen)?.RaidSettings, location);
            // Vanilla compares references before Show. Our selected projection
            // and the original database row have the same map identity, so
            // restore the selected highlight when returning to this screen.
            if (ArenaLabyrinthAccess.IsLabyrinth(location)
                && string.Equals(screen.SelectedLocation?.Id, location.Id, System.StringComparison.OrdinalIgnoreCase))
                onAtStart = true;
        }

        [PatchPostfix]
        private static void Postfix(LocationButton __instance, LocationSettings.Location location,
            System.Action<LocationSettings.Location> onSelected)
        {
            // Domination restricts even vanilla maps. Keep a conditional view
            // for every Arena tile so switching modes restores the same native
            // row instead of leaving a previously hidden Labs/Factory tile lost.
            if (!ArenaMapCatalog.IsSupportedMap(location?.Id)
                || onSelected?.Target is not MatchMakerSelectionLocationScreen screen
                || onSelected.Method.Name != nameof(MatchMakerSelectionLocationScreen.OnLocationSelected)
                || onSelected.Method.DeclaringType != typeof(MatchMakerSelectionLocationScreen)
                || !IsArena(screen)) return;

            try
            {
                var toggle = __instance._spawnableToggle;
                var presentation = ArenaPortedMapPresentation.Create(location.Id,
                    __instance.transform as RectTransform, toggle?._headerLabel, toggle?._sizeLabel,
                    () => toggle != null && toggle.Toggle != null && toggle.Toggle.IsToggled);
                if (presentation != null) States.GetOrCreateValue(screen).PortedMaps.Add(presentation);
            }
            catch (System.Exception ex)
            { Plugin.ModLogger.LogWarning($"Arena map marker presentation unavailable: {ex.Message}"); }

            var row = new ArenaConditionalMapView(__instance.gameObject.activeSelf,
                visible => { if (__instance != null) __instance.gameObject.SetActive(visible); },
                () =>
                {
                    // Native Select(false) actually selects unconditionally.
                    // Turning off the native toggle does not invoke OnSelected.
                    if (__instance != null) __instance._spawnableToggle.Toggle.IsToggled = false;
                    if (screen != null) screen.OnLocationSelected(null);
                });
            var state = States.GetOrCreateValue(screen);
            state.ConditionalMaps.Add((location.Id, row));
            var availability = state.Availability ??= new ArenaMapAvailabilitySnapshot(ArenaMenuSelection.CurrentEligibility);
            row.Refresh(ArenaMapCatalog.CanSelectNativeLocation(location.Id, availability.IsMapDiscoverable),
                string.Equals(screen.SelectedLocation?.Id, location.Id, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class ArenaLocationSelectionFactoryTimePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.GetLocationByTime));

        [PatchPrefix]
        private static void Prefix(
            MatchMakerSelectionLocationScreen __instance,
            LocationSettings.Location location,
            ref EDateTime time)
        {
            if (!IsArena(__instance) || !ArenaMapCatalog.IsFactoryVariant(location?.Id))
                return;

            var selectNight = ArenaMapCatalog.ShouldSelectFactoryNight(
                time != EDateTime.CURR,
                ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryDayId),
                ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryNightId));
            time = selectNight ? EDateTime.PAST : EDateTime.CURR;
        }
    }

    private sealed class ArenaLocationSelectionClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.Close));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance) => Restore(__instance);
    }
}
