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
        public readonly List<(string MapId, ArenaConditionalMapView View)> ConditionalMaps = new();
        public ArenaMapMenuUpdater Updater;
        public object Generation;

        public void Refresh(MatchMakerSelectionLocationScreen screen)
        {
            var eligibility = ArenaMenuSelection.CurrentModeEligibility;
            Modes.Synchronize(Settings.GameMode?.Value, eligibility.Allowed && eligibility.IsTeam);
            foreach (var row in ConditionalMaps)
                row.View.Refresh(ArenaMenuSelection.IsMapDiscoverable(row.MapId),
                    string.Equals(screen.SelectedLocation?.Id, row.MapId, System.StringComparison.OrdinalIgnoreCase));
            var map = screen.SelectedLocation?.Id;
            if (map != null && !ArenaMenuSelection.TryPrepareMapArea(Controller(screen)?.RaidSettings, map))
                screen.OnLocationSelected(null);
            var areas = ArenaMenuSelection.AreaSelection(screen.SelectedLocation?.Id);
            var participants = ArenaParticipantSelectionState.Create(areas, eligibility.IsTeam,
                ArenaParticipantSettings.Get(areas.MapId, areas.ModeId));
            Areas.Refresh(areas, eligibility.Allowed && eligibility.IsTeam, participants);
            Preview.Refresh(areas);
        }

        public void Restore()
        {
            Generation = null;
            Preview.Close();
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
            Modes.Close();
            Map.Restore();
        }
    }

    private static readonly ConditionalWeakTable<MatchMakerSelectionLocationScreen, ScreenState> States = new();

    public static void Enable()
    {
        new ShowPatch().Enable();
        new DisplayPatch().Enable();
        new SelectionPatch().Enable();
        new SelectedValuePatch().Enable();
        new ButtonLocationPatch().Enable();
        new FactoryTimePatch().Enable();
        new ClosePatch().Enable();
    }

    private static bool IsArena(MatchMakerSelectionLocationScreen screen)
        => ArenaMenuSelection.IsSelectedFor(Controller(screen)?.RaidSettings);

    private static void Restore(MatchMakerSelectionLocationScreen screen)
    {
        if (States.TryGetValue(screen, out var state))
            state.Restore();
    }

    private sealed class ShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.ShowInternal));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {
            Restore(__instance);
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
                        || ArenaParticipantPolicy.IsTeam(rendered.ModeId)
                            && Settings.TeamDeathmatchArea?.Value != rendered.AreaId) return false;
                    var current = ArenaMenuSelection.AreaSelection(rendered.MapId);
                    var live = ArenaParticipantSelectionState.Create(current, ArenaMenuSelection.CurrentEligibility.IsTeam,
                        ArenaParticipantSettings.Get(current.MapId, current.ModeId));
                    if (!rendered.SameContext(live) || !live.Available || live.Total != rendered.Total) return false;
                    var accepted = ArenaParticipantSettings.TrySelect(rendered.MapId, rendered.ModeId, total);
                    if (accepted) state.Refresh(__instance);
                    return accepted;
                });
                state.Preview.Show(__instance.transform, __instance._infoPanel?.gameObject,
                    __instance._acceptButton?._headerLabel);
                state.Refresh(__instance);
            }
            catch (System.Exception ex)
            {
                state.Preview.Close();
                state.Modes.Close();
                state.Areas.Close();
                // The native map screen remains fully usable if our optional
                // picker cannot be constructed on an unexpected UI layout.
                Plugin.ModLogger.LogWarning($"Arena map-screen mode picker unavailable: {ex.Message}");
            }

            var buttons = __instance._locationButtonsContainer.GetComponentsInChildren<LocationButton>(true);
            Plugin.ModLogger.LogInfo($"Arena map view: buttons={buttons.Length}, "
                + $"visible={buttons.Count(button => button.gameObject.activeInHierarchy)}, "
                + $"sharedContainer={__instance._locationButtonsContainer == __instance._locationPathsContainer}, "
                + $"mode={Settings.SelectedGameModeId}");
        }
    }

    private sealed class DisplayPatch : ModulePatch
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

    private sealed class SelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.OnLocationSelected));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, ref LocationSettings.Location location)
        {
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
            if (IsArena(__instance)
                && ArenaMapCatalog.IsFactoryVariant(__instance.SelectedLocation?.Id)
                && !ArenaMapCatalog.HasFactoryTimeChoice(
                    ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryDayId),
                    ArenaMenuSelection.IsMapConfigured(ArenaMapCatalog.FactoryNightId)))
                __instance._conditions.Close();
        }
    }

    private sealed class SelectedValuePatch : ModulePatch
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

    private sealed class ButtonLocationPatch : ModulePatch
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
            if (!ArenaMapCatalog.RequiresModeFilteredView(location?.Id)
                || onSelected?.Target is not MatchMakerSelectionLocationScreen screen
                || onSelected.Method.Name != nameof(MatchMakerSelectionLocationScreen.OnLocationSelected)
                || onSelected.Method.DeclaringType != typeof(MatchMakerSelectionLocationScreen)
                || !IsArena(screen)) return;

            var row = new ArenaConditionalMapView(__instance.gameObject.activeSelf,
                visible => { if (__instance != null) __instance.gameObject.SetActive(visible); },
                () =>
                {
                    // Native Select(false) actually selects unconditionally.
                    // Turning off the native toggle does not invoke OnSelected.
                    if (__instance != null) __instance._spawnableToggle.Toggle.IsToggled = false;
                    if (screen != null) screen.OnLocationSelected(null);
                });
            States.GetOrCreateValue(screen).ConditionalMaps.Add((location.Id, row));
            row.Refresh(ArenaMenuSelection.IsMapDiscoverable(location.Id),
                string.Equals(screen.SelectedLocation?.Id, location.Id, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class FactoryTimePatch : ModulePatch
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

    private sealed class ClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.Close));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance) => Restore(__instance);
    }
}
