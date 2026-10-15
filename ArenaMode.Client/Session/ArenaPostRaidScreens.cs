using EFT;
using EFT.UI.Screens;
using EFT.UI.SessionEnd;
using HarmonyLib;
using Redeploy.Core.GameAccess;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>Presentation-only changes for one proven Arena post-raid operation.</summary>
internal static class ArenaPostRaidScreens
{
    private static LocalRaidSettings _endedRaid;
    private static RaidSettings _resultSettings;
    private static string _profileId;
    private static int _epoch;
    private static SessionResultShowOperation _operation;

    public static void Enable()
    {
        new BeginResultPatch().Enable();
        new BindOperationPatch().Enable();
        new WarningPatch().Enable();
        new SkipExperiencePatch().Enable();
        new HealthSequencePatch().Enable();
        new CompletePatch().Enable();
    }

    // Called before ArenaSession clears its exact game/world pair. Retain only
    // settings/identity, never a disposed player, world or health controller.
    internal static void CaptureEndedRaid(AbstractGame game, GameWorld world)
    {
        Clear();
        if (!ArenaSession.IsExactActiveRaid(game, world) || !(game is LocalGame local)) return;
        try
        {
            _endedRaid = LocalGameAccessor.GetLocalRaidSettings(local);
            _profileId = local.ProfileId;
            _epoch = ArenaSession.ArmEpoch;
        }
        catch (Exception ex) { Clear(); Report(ex); }
    }

    internal static void BeginResult(LocalRaidSettings endedRaid, RaidSettings settings, string profileId)
    {
        if (!IsResultPhase || _endedRaid == null || !ReferenceEquals(_endedRaid, endedRaid)
            || string.IsNullOrEmpty(_profileId) || _profileId != profileId
            || settings == null || !settings.Local || settings.Side != ESideType.Pmc)
        {
            Clear();
            return;
        }
        _resultSettings = settings;
        _operation = null;
    }

    private static bool IsResultPhase => ArenaSession.LastRaidWasArena && !ArenaSession.IsActive
        && !ArenaSession.ArmedForNextRaid && ArenaSession.ArmEpoch == _epoch;

    internal static void Bind(SessionResultShowOperation operation)
    {
        if (!IsResultPhase || _resultSettings == null || operation == null
            || !ReferenceEquals(operation._raidSettings, _resultSettings)
            || operation._matchProfileId != _profileId || operation._session?.Profile?.Id != _profileId
            || operation._savageProfile?.Id == _profileId
            || (_operation != null && !ReferenceEquals(_operation, operation))) return;
        _operation = operation; // ReInit of this same operation remains supported.
    }

    internal static bool Owns(SessionResultShowOperation operation)
        => IsResultPhase && operation != null && ReferenceEquals(operation, _operation)
            && ReferenceEquals(operation._raidSettings, _resultSettings)
            && operation._matchProfileId == _profileId && operation._session?.Profile?.Id == _profileId;

    internal static void HideWarning(SessionResultExitStatus screen,
        SessionResultExitStatus.ExitStatusScreenController controller)
    {
        if (!Owns(_operation) || controller == null
            || !ReferenceEquals(controller, _operation._exitStatusScreenController)) return;
        try { if (screen?._warningPanel != null) screen._warningPanel.SetActive(false); }
        catch (Exception ex) { Report(ex); }
    }

    internal static bool ProceedFromStatistics(SessionResultShowOperation operation)
    {
        if (!Owns(operation)) return true;
        // This is the XP page's native Next continuation. It preserves optional
        // treatment, menu completion, profile refresh and controller disposal.
        operation.ShowHealthTreatmentScreen();
        Plugin.ModLogger.LogDebug("Arena post-raid XP page skipped.");
        return false;
    }

    internal static void FilterHealthSequence(ref IBaseScreenController<EEftScreenType>[] controllers)
    {
        var operation = _operation;
        if (!Owns(operation) || controllers == null || controllers.Length != 4
            || operation._killListScreenController == null || operation._statisticsScreenController == null
            || operation._experienceScreenController == null || operation._healthTreatmentScreenController == null
            || !ReferenceEquals(controllers[0], operation._killListScreenController)
            || !ReferenceEquals(controllers[1], operation._statisticsScreenController)
            || !ReferenceEquals(controllers[2], operation._experienceScreenController)
            || !ReferenceEquals(controllers[3], operation._healthTreatmentScreenController)) return;
        // Main Menu queues XP behind treatment. Remove it from this exact native
        // sequence too, so Back cannot expose it. Do not mutate the caller array
        // or touch a foreign/mod sequence, its flags, or the treatment prompt.
        controllers = new[] { controllers[0], controllers[1], controllers[3] };
    }

    internal static void Complete(SessionResultShowOperation operation)
    {
        if (ReferenceEquals(operation, _operation)) Clear();
    }

    private static void Clear()
    {
        _endedRaid = null;
        _resultSettings = null;
        _profileId = null;
        _operation = null;
        _epoch = -1;
    }

    private static void Report(Exception ex)
        => Plugin.ModLogger.LogWarning($"Arena post-raid presentation unavailable; retaining native UI: {ex.Message}");

    private sealed class BeginResultPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(TarkovApplication), nameof(TarkovApplication.OnGameEnd));

        // OnGameEnd immediately calls TryUnloadGame, clearing _localRaidSettings.
        // Bind before that; ShowSessionResult itself must run unchanged.
        [PatchPrefix]
        private static void Prefix(TarkovApplication __instance, LocalRaidSettings ____localRaidSettings, string profileId)
            => BeginResult(____localRaidSettings, __instance.CurrentRaidSettings, profileId);
    }

    private sealed class BindOperationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(SessionResultShowOperation), nameof(SessionResultShowOperation.Init));

        // Init is async: bind before its first await and leave the task intact.
        [PatchPrefix]
        private static void Prefix(SessionResultShowOperation __instance) => Bind(__instance);
    }

    private sealed class WarningPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(SessionResultExitStatus), nameof(SessionResultExitStatus.Show),
                new[] { typeof(SessionResultExitStatus.ExitStatusScreenController) });

        [PatchPostfix]
        private static void Postfix(SessionResultExitStatus __instance, SessionResultExitStatus.ExitStatusScreenController controller)
            => HideWarning(__instance, controller);
    }

    private sealed class SkipExperiencePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(SessionResultShowOperation), nameof(SessionResultShowOperation.ProceedFromStatistics));

        [PatchPrefix]
        private static bool Prefix(SessionResultShowOperation __instance) => ProceedFromStatistics(__instance);
    }

    private sealed class HealthSequencePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ScreenManager<EEftScreenType>), "ShowScreenSequence");

        [PatchPrefix]
        private static void Prefix(ref IBaseScreenController<EEftScreenType>[] controllers)
            => FilterHealthSequence(ref controllers);
    }

    private sealed class CompletePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(SessionResultShowOperation), nameof(SessionResultShowOperation.ConcludeOperation));

        [PatchPostfix]
        private static void Postfix(SessionResultShowOperation __instance) => Complete(__instance);
    }
}
