using EFT;
using EFT.Communications;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using HarmonyLib;
using Redeploy.Core.Compat;
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
        private static void Prefix(MatchMakerSideSelectionScreen.RaidSideSelectionScreenController controller)
        {
            ArenaMenuSelection.Begin(controller, controller.RaidSettings,
                () => controller.MatchmakerPlayersController.GroupPlayers.Count == 1
                    && controller.RaidSettings.RaidMode != ERaidMode.Coop);
            ArenaMenuSelection.RestoringSideScreen = true;
        }

        [PatchPostfix]
        private static void Postfix(MatchMakerSideSelectionScreen __instance)
        {
            try
            {
                // This is BSG's existing third toggle. Keep its ToggleGroup,
                // animation listeners and native placement intact; translate its
                // otherwise unused Random selection to PMC in SelectPatch.
                var arena = __instance._randomButton;
                arena.vmethod_0("Arena", arena._headerFontSize);
                arena.gameObject.SetActive(true);
                arena.SpawnedObject.interactable = !FikaCompatCheck.IsFikaLoaded
                    && Controller(__instance).MatchmakerPlayersController.GroupPlayers.Count == 1;
                if (ArenaMenuSelection.IsSelected)
                {
                    arena.IsToggled = true;
                    SetDescription(__instance);
                }
            }
            catch (Exception ex)
            {
                ArenaMenuSelection.Clear();
                __instance._randomButton.gameObject.SetActive(false);
                Plugin.ModLogger.LogError($"Arena side selection unavailable: {ex}");
            }
            finally
            {
                ArenaMenuSelection.RestoringSideScreen = false;
            }
        }

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            ArenaMenuSelection.RestoringSideScreen = false;
            if (__exception != null)
                ArenaMenuSelection.EndFlow();
            return __exception;
        }
    }

    private sealed class SelectPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetSelectedSide));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance, ref ESideType side)
        {
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
                if (FikaCompatCheck.IsFikaLoaded || Controller(__instance).MatchmakerPlayersController.GroupPlayers.Count != 1)
                    throw new InvalidOperationException("Arena is available for solo play only.");

                // An explicit click is a suitable time to retry a failed probe.
                // Merely opening a menu must not latch an early server outage.
                if (!ArenaServerClient.RefreshAvailability() || !ArenaClientConfig.Refresh())
                    throw new InvalidOperationException("Arena server mod is unavailable. No arena raid was selected.");

                if (!ArenaMenuSelection.IsMapAvailable("factory4_day")
                    && !ArenaMenuSelection.IsMapAvailable("labyrinth")
                    && !ArenaMenuSelection.IsMapAvailable("laboratory"))
                    throw new InvalidOperationException("No arena maps are enabled on the server.");

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
            if (ArenaMenuSelection.IsSelected)
                SetDescription(__instance);
        }
    }

    private static void SetDescription(MatchMakerSideSelectionScreen screen)
    {
        screen._sideDescriptionText.text =
            "ARENA\nInstant respawns, arena kits and four game modes.\n"
            + "Factory (day), Labyrinth and The Lab.\n"
            + "Nothing gained, nothing lost — your profile stays untouched.";
        screen.SetHealthPanelVisibility(false, true);
    }

    private sealed class HealthWarningPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetObjectVisibility));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance, ref bool visibility)
        {
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
