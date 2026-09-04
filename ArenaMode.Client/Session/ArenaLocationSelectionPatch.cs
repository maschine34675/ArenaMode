using ArenaMode.Config;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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

        public void Restore()
        {
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

            // Clear Marathon and any remembered map before vanilla creates views
            // or enables Next/Ready. Factory's native time toggle also selects an
            // entirely different location, so a remembered night must be reset.
            raidSettings.transitionType = ELocationTransition.Common;
            raidSettings.SelectedDateTime = EDateTime.CURR;
            if (!ArenaMenuSelection.IsMapAvailable(raidSettings.SelectedLocation?.Id))
                raidSettings.SelectedLocation = null;
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance)
        {
            if (!IsArena(__instance))
                return;

            var state = States.GetOrCreateValue(__instance);
            state.Map.HideExtras(__instance._locationPathsContainer, __instance._marathonButton.gameObject);

            try
            {
                var eligibility = ArenaMenuSelection.CurrentEligibility;
                state.Modes.Show(
                    __instance.transform,
                    __instance._acceptButton?._headerLabel,
                    Settings.GameMode?.Value,
                    eligibility.Allowed && eligibility.IsTeam,
                    Settings.TrySelectGameMode);
            }
            catch (System.Exception ex)
            {
                state.Modes.Close();
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
                __result = ArenaMenuSelection.IsMapAvailable(location?.Id);
        }
    }

    private sealed class SelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.OnLocationSelected));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, ref LocationSettings.Location location)
        {
            if (IsArena(__instance) && !ArenaMenuSelection.IsMapAvailable(location?.Id))
                location = null;
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance)
        {
            if (IsArena(__instance) && __instance.SelectedLocation?.Id == "factory4_day")
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
            if (IsArena(__instance) && !ArenaMenuSelection.IsMapAvailable(value?.Id))
                value = null;
        }
    }

    private sealed class FactoryTimePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.GetLocationByTime));

        [PatchPrefix]
        private static void Prefix(MatchMakerSelectionLocationScreen __instance, ref EDateTime time)
        {
            if (IsArena(__instance))
                time = EDateTime.CURR;
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
