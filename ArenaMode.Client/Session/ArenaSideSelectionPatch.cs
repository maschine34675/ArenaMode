using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Shared;
using ArenaMode.Shared.Session;
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
        new ArenaSideSelectionShowPatch().Enable();
        new ArenaSideSelectionClosePatch().Enable();
        new ArenaSideSelectionSelectPatch().Enable();
        new ArenaSideSelectionHealthWarningPatch().Enable();
        new ArenaSideSelectionLeaveSetupPatch().Enable();
        // Arena sessions only: PMC and Scav stay out of reach. A normal session installs nothing extra.
        if (ArenaSessionMode.IsArenaSession)
            new ArenaSideSelectionSessionLockPatch().Enable();
    }

    private sealed class ArenaSideSelectionShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Show),
                new[] { typeof(MatchMakerSideSelectionScreen.RaidSideSelectionScreenController) });

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance,
            MatchMakerSideSelectionScreen.RaidSideSelectionScreenController controller)
        {
            Preview.ArenaPreviewFlow.Reset();
            ArenaAvailabilityNotice.Hide(__instance);
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
                // otherwise unused Random selection to PMC in ArenaSideSelectionSelectPatch.
                var arena = __instance._randomButton;
                // The whitelist verdict needs the server's ping (its mods, the Arena-instance flag).
                ArenaWhitelistAdmission.PrepareForScreen();
                var eligibility = EvaluateEligibility(
                    Controller(__instance), allowPendingModeSelection: true);
                ArenaSideSelectionView.Show(__instance.transform, arena, eligibility.Allowed);
                ArenaSideSelectionLayout.Apply(__instance, Controller(__instance)?.Session.Profile);
                // Arena session: ARENA preselected, PMC and Scav locked. Any other session: unchanged.
                eligibility = PrepareArenaSession(__instance, eligibility);
                ShowNotice(__instance, eligibility);
                if (ArenaMenuSelection.IsSelected)
                {
                    EnsureCompatibleMode(eligibility);
                    arena.IsToggled = true;
                    SetDescription(__instance);
                }
                // A restored ARENA selection shows "ARENA ZONE" (never throws; the
                // native base Show above has already written "PVE ZONE").
                ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);
                if (!eligibility.Allowed)
                    Plugin.ModLogger.LogInfo($"Arena side button disabled: {eligibility.Reason}");
                Plugin.ModLogger.LogDebug($"Arena side button: active={arena.gameObject.activeInHierarchy}, "
                    + $"interactable={arena.SpawnedObject.IsInteractable()}, wrapper={arena.transform.parent.name}");
            }
            catch (Exception ex)
            {
                ArenaMenuSelection.Clear();
                ArenaSideSelectionLayout.Restore(__instance);
                ArenaSideSelectionView.Hide(__instance.transform, __instance._randomButton);
                ArenaAvailabilityNotice.Show(__instance, "The Arena menu could not initialize. Restart the game; if this persists, check the ArenaMode installation and client log.");
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

    private sealed class ArenaSideSelectionClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Close));

        [PatchPrefix]
        private static void Prefix(MatchMakerSideSelectionScreen __instance)
        {
            ArenaAvailabilityNotice.Hide(__instance);
            try { Preview.ArenaPreviewFlow.KeepFromSide(__instance, Controller(__instance)?.RaidSettings); }
            catch (Exception ex)
            {
                Preview.ArenaPreviewFlow.Reset();
                Plugin.ModLogger.LogWarning($"Arena preview transfer unavailable: {ex.Message}");
            }
            finally { ArenaSideSelectionLayout.Restore(__instance); }
        }
    }

    private sealed class ArenaSideSelectionSelectPatch : ModulePatch
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
                // A whitelist-warning note belongs to the ARENA choice; an "unavailable" notice stays,
                // and so does an Arena session's own notice (it carries the way back).
                if (!ArenaSessionMode.IsArenaSession)
                    ArenaAvailabilityNotice.HideInfo(__instance);
                return;
            }

            side = ESideType.Pmc;
            try
            {
                var eligibility = EvaluateEligibility(
                    Controller(__instance), allowPendingModeSelection: true);
                if (!eligibility.Allowed)
                    throw new InvalidOperationException(eligibility.Reason);

                SelectArena(__instance, eligibility);
            }
            catch (Exception ex)
            {
                ArenaMenuSelection.Clear();
                // An Arena session has no other side to fall back to: PMC stays locked there.
                if (!ArenaSessionMode.IsArenaSession)
                    __instance._pmcButton.IsToggled = true;
                else
                    SetNextAvailable(__instance, false);
                Plugin.ModLogger.LogWarning($"Arena selection rejected: {ex.Message}");
                ShowUnavailable(__instance, ex.Message);
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
            // Switching the ARENA toggle on shows "ARENA ZONE"; any other side puts
            // back the native label this patch replaced (a flow that never selected
            // Arena has nothing to put back and is not touched).
            ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);
        }
    }

    private static void SetDescription(MatchMakerSideSelectionScreen screen)
    {
        var eligibility = ArenaMenuSelection.CurrentEligibility;
        screen._sideDescriptionText.text = eligibility.IsTeam
            ? "<nobr>Tactical or Zombies with PitFireTeam.</nobr>\n<nobr>Your profile and gear stay untouched.</nobr>"
            : "<nobr>Solo and team arena combat.</nobr>\n<nobr>Your profile and gear stay untouched.</nobr>";
        screen.SetHealthPanelVisibility(false, true);
    }

    /// <summary>
    /// The body of an Arena choice, shared by the click on the ARENA toggle and the Arena session's
    /// automatic selection. Throws with the player-visible reason when Arena cannot be selected.
    /// </summary>
    private static void SelectArena(MatchMakerSideSelectionScreen screen, ArenaMenuSelection.Eligibility eligibility)
    {
        // An explicit click is a suitable time to retry a failed probe.
        // Merely opening a menu must not latch an early server outage.
        if (!ArenaServerClient.RefreshAvailability() || !ArenaClientConfig.Refresh())
            throw new InvalidOperationException("Arena server mod is unavailable. No arena raid was selected.");

        // A server that reports an admission failure in its ping (an older build) refuses the choice.
        if (!string.IsNullOrEmpty(ArenaServerClient.ServerAdmissionFailure))
            throw new InvalidOperationException(ArenaServerClient.ServerAdmissionFailure);

        // The fresh ping also brings the server's mods and its Arena-instance flag, which the
        // whitelist verdict depends on.
        var whitelist = ArenaWhitelistAdmission.GetRejectionReason();
        if (!string.IsNullOrEmpty(whitelist))
            throw new InvalidOperationException(whitelist);

        if (!ArenaMenuSelection.HasConfiguredMap(eligibility.IsTeam))
            throw new InvalidOperationException("No arena maps are enabled on the server.");

        // Let a complete PFT roster reach the new map-screen picker even
        // if the previous solo raid left another mode in config. Team
        // Final validation still requires an authenticated supported team mode.
        EnsureCompatibleMode(eligibility);

        ArenaMenuSelection.Select();
        ShowSelectedNotice(screen);
        Controller(screen).RaidSettings.Side = ESideType.Pmc;
        if (ArenaSessionMode.IsArenaSession)
            SetNextAvailable(screen, true);
    }

    // ------------------------------------------------------------------ whitelist / Arena session

    /// <summary>
    /// Arena session: lock PMC and Scav, select ARENA on its own, and keep Next closed until Arena is
    /// really selected (the server refuses ordinary raids in a session anyway). Returns the
    /// eligibility to display, which is the input unless the automatic selection failed. Outside an
    /// Arena session this does nothing.
    /// </summary>
    private static ArenaMenuSelection.Eligibility PrepareArenaSession(MatchMakerSideSelectionScreen screen,
        ArenaMenuSelection.Eligibility eligibility)
    {
        if (!ArenaSessionMode.IsArenaSession)
            return eligibility;

        LockCharacterChoices(screen);
        if (!eligibility.Allowed)
        {
            SetNextAvailable(screen, false);
            return eligibility;
        }

        if (ArenaMenuSelection.IsSelected)
        {
            SetNextAvailable(screen, true);
            return eligibility;
        }

        // Selecting asks the server several times without a time limit, as a click does. Do that on
        // opening only when the bounded ping at screen open has shown the server to be answering;
        // otherwise ARENA stays unselected and the player's click (which waits) decides.
        if (!ArenaServerClient.IsServerModKnownAvailable)
        {
            SetNextAvailable(screen, false);
            return eligibility;
        }

        try
        {
            SelectArena(screen, eligibility);
            return eligibility;
        }
        catch (Exception ex)
        {
            ArenaMenuSelection.Clear();
            SetNextAvailable(screen, false);
            Plugin.ModLogger.LogWarning($"Arena session could not preselect Arena: {ex.Message}");
            return new ArenaMenuSelection.Eligibility(false, eligibility.IsTeam, ex.Message);
        }
    }

    /// <summary>
    /// PMC and Scav are not selectable in an Arena session: the toggles and the big model buttons
    /// (which bypass a toggle's interactable flag) are disabled. The native Scav timer re-enables
    /// its own controls every quarter second, so ArenaSideSelectionSessionLockPatch repeats this.
    /// </summary>
    private static void LockCharacterChoices(MatchMakerSideSelectionScreen screen)
    {
        screen._pmcButton.SpawnedObject.interactable = false;
        screen._pmcBigButton.interactable = false;
        screen._savagesButton.SpawnedObject.interactable = false;
        screen._savagesBigButton.interactable = false;
        screen._savagesBigButton.gameObject.SetActive(false);
        screen._savageBlocker.alpha = .3f;
        screen._savageBlocker.interactable = false;
    }

    private static void SetNextAvailable(MatchMakerSideSelectionScreen screen, bool available)
    {
        if (screen._nextButton != null)
            screen._nextButton.Interactable = available;
    }

    /// <summary>The notice for the screen as shown: refusal, session note, or the whitelist warnings.</summary>
    private static void ShowNotice(MatchMakerSideSelectionScreen screen, ArenaMenuSelection.Eligibility eligibility)
    {
        string warnings = ArenaWhitelistAdmission.WarningLine;
        if (ArenaSessionMode.IsArenaSession)
        {
            var back = ReturnAction(screen);
            if (eligibility.Allowed)
                ArenaAvailabilityNotice.ShowInfo(screen, WithWarnings(ArenaWhitelistAdmission.SessionNoticeText, warnings), back);
            else
                ArenaAvailabilityNotice.Show(screen, WithWarnings(eligibility.Reason, warnings), back);
            return;
        }

        if (!eligibility.Allowed)
            ShowUnavailable(screen, eligibility.Reason);
        else if (warnings != null && ArenaMenuSelection.IsSelected)
            ShowVersionNotes(screen, warnings);
        else
            ArenaAvailabilityNotice.Show(screen, null);
    }

    /// <summary>Arena was just selected: the session note, the whitelist warnings, or nothing.</summary>
    private static void ShowSelectedNotice(MatchMakerSideSelectionScreen screen)
    {
        string warnings = ArenaWhitelistAdmission.WarningLine;
        if (ArenaSessionMode.IsArenaSession)
            ArenaAvailabilityNotice.ShowInfo(screen, WithWarnings(ArenaWhitelistAdmission.SessionNoticeText, warnings), ReturnAction(screen));
        else if (warnings != null)
            ShowVersionNotes(screen, warnings);
        else
            ArenaAvailabilityNotice.Hide(screen);
    }

    /// <summary>An "unavailable" notice; a whitelist refusal that can start an Arena session gets the action.</summary>
    private static void ShowUnavailable(MatchMakerSideSelectionScreen screen, string reason)
    {
        string text = WithWarnings(reason, ArenaWhitelistAdmission.WarningLine);
        if (ArenaSessionMode.IsArenaSession)
            ArenaAvailabilityNotice.Show(screen, text, ReturnAction(screen));
        else if (ArenaWhitelistAdmission.CanOfferSessionStart(reason) || ArenaWhitelistAdmission.CanOfferVersionSessionStart())
            ArenaAvailabilityNotice.Show(screen, WithWarnings(text, ArenaWhitelistAdmission.VersionSessionText), StartAction(screen));
        else
            ArenaAvailabilityNotice.Show(screen, text);
    }

    private static void ShowVersionNotes(MatchMakerSideSelectionScreen screen, string warnings)
    {
        if (ArenaWhitelistAdmission.CanOfferSessionStart(null))
            ArenaAvailabilityNotice.ShowInfo(screen, WithWarnings(warnings, ArenaWhitelistAdmission.VersionSessionText), StartAction(screen));
        else
            ArenaAvailabilityNotice.ShowInfo(screen, warnings);
    }

    private static string WithWarnings(string text, string warnings)
        => string.IsNullOrEmpty(warnings) ? text : text + "\n" + warnings;

    private static ArenaNoticeAction StartAction(MatchMakerSideSelectionScreen screen)
        => new ArenaNoticeAction(ArenaWhitelistAdmission.StartActionLabel,
            () => SwitchSession(screen, ArenaSessionContract.DirectionEnter));

    private static ArenaNoticeAction ReturnAction(MatchMakerSideSelectionScreen screen)
        => new ArenaNoticeAction(ArenaWhitelistAdmission.ReturnActionLabel,
            () => SwitchSession(screen, ArenaSessionContract.DirectionLeave));

    /// <summary>
    /// The confirmed action: restart the game into (or out of) an Arena session. The request starts the
    /// helper and the game quits once the helper has acknowledged it (a moment later, no action to click
    /// meanwhile: the notice only says so). On failure, at once or when the helper stops or stays silent, the
    /// reason replaces the notice, the game keeps running and the action stays available.
    /// </summary>
    private static void SwitchSession(MatchMakerSideSelectionScreen screen, string direction)
    {
        string profileId = null;
        try { profileId = Controller(screen)?.Session?.Profile?.Id; }
        catch (Exception) { }

        if (ArenaSessionSwitch.Request(direction, profileId, out var error, failure => ShowSwitchFailure(screen, direction, failure)))
        {
            // The first look at the helper comes a quarter second later, but a failure that already reached the
            // callback has shown its own notice, which this must not cover.
            if (ArenaSessionSwitch.HelperPending) ArenaAvailabilityNotice.ShowInfo(screen, ArenaSessionSwitch.WaitingText);
            return;
        }

        ShowSwitchFailure(screen, direction, error);
    }

    private static void ShowSwitchFailure(MatchMakerSideSelectionScreen screen, string direction, string error)
    {
        var retry = direction == ArenaSessionContract.DirectionEnter ? StartAction(screen) : ReturnAction(screen);
        ArenaAvailabilityNotice.Show(screen, error, retry);
        try { NotificationManager.DisplayWarningNotification(error); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena session failure notification could not be shown: " + ex.Message); }
    }

    private static void EnsureCompatibleMode(ArenaMenuSelection.Eligibility eligibility)
    {
        if (eligibility?.IsTeam != true)
            return;

        if (!PitFireTeamSelectionPolicy.SupportsMode(Settings.SelectedGameModeId))
            Settings.TrySelectGameMode(ArenaModeCatalog.TacticalId);
        if (!PitFireTeamSelectionPolicy.SupportsMode(Settings.SelectedGameModeId))
        {
            throw new InvalidOperationException(
                "PitFireTeam companions require Tactical or Zombies Arena, but the mode selection could not be saved.");
        }
    }

    private static ArenaMenuSelection.Eligibility EvaluateEligibility(
        MatchMakerSideSelectionScreen.RaidSideSelectionScreenController controller,
        bool allowPendingModeSelection = false)
    {
        var rejection = ArenaMenuSelection.CompatibilityRejection;
        if (!string.IsNullOrEmpty(rejection))
            return new ArenaMenuSelection.Eligibility(false, false, rejection);
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

    private sealed class ArenaSideSelectionHealthWarningPatch : ModulePatch
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

    /// <summary>
    /// Arena session only (installed by Enable only then): the native Scav timer re-enables the Scav
    /// controls every quarter second; this puts the lock back right after each run.
    /// </summary>
    private sealed class ArenaSideSelectionSessionLockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetScavAvailable));

        [PatchPostfix]
        private static void Postfix(MatchMakerSideSelectionScreen __instance)
        {
            if (PitFireTeamCompat.IsSquadManagementScreen)
                return;

            try { LockCharacterChoices(__instance); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena session lock unavailable: {ex.Message}"); }
        }
    }

    private sealed class ArenaSideSelectionLeaveSetupPatch : ModulePatch
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
