using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Shared;
using EFT;
using EFT.Communications;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Session;

internal static class ArenaSideSelectionPatch
{
    private static readonly AccessTools.FieldRef<MatchMakerSideSelectionScreen,
        MatchMakerSideSelectionScreen.RaidSideSelectionScreenController> Controller =
        AccessTools.FieldRefAccess<MatchMakerSideSelectionScreen,
            MatchMakerSideSelectionScreen.RaidSideSelectionScreenController>("ScreenController");

    public static void Enable()
    {
        new ShowPatch().Enable();
        new ClosePatch().Enable();
        new SelectPatch().Enable();
        new HealthWarningPatch().Enable();
        new LeaveSetupPatch().Enable();
    }

    private sealed class ShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Show),
                new[] { typeof(MatchMakerSideSelectionScreen.RaidSideSelectionScreenController) });

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance,
            MatchMakerSideSelectionScreen.RaidSideSelectionScreenController controller)
        {
            Preview.ArenaPreviewFlow.Reset();
            ArenaSideSelectionLayout.Restore(__instance);
            if (PitFireTeamCompat.IsSquadManagementScreen)
            {
                ArenaMenuSelection.EndFlow();
                ArenaMenuSelection.RestoringSideScreen = false;
                return;
            }

            ArenaMenuSelection.Begin(controller, controller.RaidSettings);
            ArenaMenuSelection.ConfigureEligibility(
                () => EvaluateEligibility(controller),
                FreezeEligibility,
                () => EvaluateEligibility(controller, allowPendingModeSelection: true));
            ArenaMenuSelection.RestoringSideScreen = true;
        }

        [PatchPostfix]
        [HarmonyAfter("MatchMakerSideSelectionScreenShowPatch")]
        private static void Postfix(MatchMakerSideSelectionScreen __instance)
        {
            try
            {
                if (PitFireTeamCompat.IsSquadManagementScreen)
                {
                    ArenaMenuSelection.EndFlow();
                    ArenaSideSelectionView.Hide(__instance.transform, __instance._randomButton);
                    return;
                }

                // This is BSG's existing third toggle. Keep its ToggleGroup,
                // animation listeners intact; align its view with the other
                // character choices and translate its
                // otherwise unused Random selection to PMC in SelectPatch.
                var arena = __instance._randomButton;
                var eligibility = EvaluateEligibility(
                    Controller(__instance), allowPendingModeSelection: true);
                ArenaSideSelectionView.Show(__instance.transform, arena, eligibility.Allowed);
                ArenaSideSelectionLayout.Apply(__instance, Controller(__instance)?.Session.Profile);
                if (ArenaMenuSelection.IsSelected)
                {
                    EnsureCompatibleMode(eligibility);
                    arena.IsToggled = true;
                    SetDescription(__instance);
                }
                if (!eligibility.Allowed)
                    Plugin.ModLogger.LogInfo($"Arena side button disabled: {eligibility.Reason}");
                Plugin.ModLogger.LogInfo($"Arena side button: active={arena.gameObject.activeInHierarchy}, "
                    + $"interactable={arena.SpawnedObject.IsInteractable()}, wrapper={arena.transform.parent.name}");
            }
            catch (Exception ex)
            {
                ArenaMenuSelection.Clear();
                ArenaSideSelectionLayout.Restore(__instance);
                ArenaSideSelectionView.Hide(__instance.transform, __instance._randomButton);
                Plugin.ModLogger.LogError($"Arena side selection unavailable: {ex}");
            }
            finally
            {
                ArenaMenuSelection.RestoringSideScreen = false;
            }
        }

        [PatchFinalizer]
        private static Exception Finalizer(MatchMakerSideSelectionScreen __instance, Exception __exception)
        {
            ArenaMenuSelection.RestoringSideScreen = false;
            if (__exception != null)
            {
                ArenaSideSelectionLayout.Restore(__instance);
                ArenaMenuSelection.EndFlow();
            }
            return __exception;
        }
    }

    private sealed class ClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Close));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance)
        {
            try { Preview.ArenaPreviewFlow.KeepFromSide(__instance, Controller(__instance)?.RaidSettings); }
            catch (Exception ex)
            {
                Preview.ArenaPreviewFlow.Reset();
                Plugin.ModLogger.LogWarning($"Arena preview transfer unavailable: {ex.Message}");
            }
            finally { ArenaSideSelectionLayout.Restore(__instance); }
        }
    }

    private sealed class SelectPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetSelectedSide));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance, ref ESideType side)
        {
            if (PitFireTeamCompat.IsSquadManagementScreen)
                return;

            if (ArenaMenuSelection.RestoringSideScreen)
            {
                if (side == ESideType.Random)
                    side = ESideType.Pmc;
                return;
            }

            if (side != ESideType.Random)
            {
                ArenaMenuSelection.Clear();
                return;
            }

            side = ESideType.Pmc;
            try
            {
                var eligibility = EvaluateEligibility(
                    Controller(__instance), allowPendingModeSelection: true);
                if (!eligibility.Allowed)
                    throw new InvalidOperationException(eligibility.Reason);

                // An explicit click is a suitable time to retry a failed probe.
                // Merely opening a menu must not latch an early server outage.
                if (!ArenaServerClient.RefreshAvailability() || !ArenaClientConfig.Refresh())
                    throw new InvalidOperationException("Arena server mod is unavailable. No arena raid was selected.");

                if (!ArenaMenuSelection.HasConfiguredMap(eligibility.IsTeam))
                    throw new InvalidOperationException("No arena maps are enabled on the server.");

                // Let a complete PFT roster reach the new map-screen picker even
                // if the previous solo raid left another mode in config. Team
                // Arena remains Tactical-only at the strict final validation.
                EnsureCompatibleMode(eligibility);

                ArenaMenuSelection.Select();
                Controller(__instance).RaidSettings.Side = ESideType.Pmc;
            }
            catch (Exception ex)
            {
                ArenaMenuSelection.Clear();
                __instance._pmcButton.IsToggled = true;
                Plugin.ModLogger.LogWarning($"Arena selection rejected: {ex.Message}");
                NotificationManager.DisplayWarningNotification(ex.Message);
            }
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSideSelectionScreen __instance)
        {
            if (PitFireTeamCompat.IsSquadManagementScreen)
                return;

            if (ArenaMenuSelection.IsSelected)
                SetDescription(__instance);
        }
    }

    private static void SetDescription(MatchMakerSideSelectionScreen screen)
    {
        var eligibility = ArenaMenuSelection.CurrentEligibility;
        screen._sideDescriptionText.text = eligibility.IsTeam
            ? "<nobr>Tactical 5v5 with PitFireTeam.</nobr>\n<nobr>Your profile and gear stay untouched.</nobr>"
            : "<nobr>Solo and team arena combat.</nobr>\n<nobr>Your profile and gear stay untouched.</nobr>";
        screen.SetHealthPanelVisibility(false, true);
    }

    private static void EnsureCompatibleMode(ArenaMenuSelection.Eligibility eligibility)
    {
        if (eligibility?.IsTeam != true)
            return;

        Settings.TrySelectGameMode(ArenaModeCatalog.TacticalId);
        if (!string.Equals(Settings.SelectedGameModeId, ArenaModeCatalog.TacticalId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PitFireTeam companions require Tactical Arena, but the mode selection could not be saved.");
        }
    }

    private static ArenaMenuSelection.Eligibility EvaluateEligibility(
        MatchMakerSideSelectionScreen.RaidSideSelectionScreenController controller,
        bool allowPendingModeSelection = false)
    {
        var decision = PitFireTeamCompat.EvaluateSelection(
            controller?.MatchmakerPlayersController, controller?.RaidSettings,
            allowPendingModeSelection);
        return new ArenaMenuSelection.Eligibility(
            decision.Allowed, decision.IsTeam, decision.Reason, decision);
    }

    private static string FreezeEligibility(ArenaMenuSelection.Eligibility eligibility)
    {
        if (eligibility?.State is not PitFireTeamSelectionDecision decision)
            return "Arena lost the authenticated PitFireTeam selection before raid start.";

        return PitFireTeamCompat.TryFreezeTeamRoster(decision, out var reason)
            ? string.Empty
            : reason;
    }

    private sealed class HealthWarningPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetObjectVisibility));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance, ref bool visibility)
        {
            if (PitFireTeamCompat.IsSquadManagementScreen)
                return;

            // Update and PMC hover would otherwise re-show ordinary raid health
            // warnings every frame, since Arena intentionally uses the PMC side.
            if (ArenaMenuSelection.IsSelectedFor(Controller(__instance)?.RaidSettings))
                visibility = false;
        }
    }

    private sealed class LeaveSetupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.ScreenChangeHandler));

        [PatchPrefix]
        private static void Prefix(EEftScreenType eftScreenType)
        {
            if (ArenaSession.IsActive)
                return;

            // Back restores an existing root screen without calling
            // ShowMenuScreen. Observe real navigation, including the taskbar.
            switch (eftScreenType)
            {
                case EEftScreenType.MainMenu:
                case EEftScreenType.Hideout:
                case EEftScreenType.Inventory:
                case EEftScreenType.Traders:
                case EEftScreenType.Trader:
                case EEftScreenType.FleaMarket:
                case EEftScreenType.WeaponModding:
                case EEftScreenType.Handbook:
                    break;
                default:
                    return;
            }

            ArenaMenuSelection.EndFlow();
            ArenaSession.ClearArmed("left raid setup");
        }
    }
}
