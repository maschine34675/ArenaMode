using EFT;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Threading.Tasks;

namespace ArenaMode.Session.Preview;

/// <summary>Only the known setup owners are adapted. Inventory/profile/reconnect and teammate previews remain native.</summary>
internal static class ArenaPreviewFlowPatch
{
    public static void Enable()
    {
        new MapShowPatch().Enable();
        new OfflineShowPatch().Enable();
        new AcceptShowPatch().Enable();
        new AcceptVisualPatch().Enable();
        new LoadingShowPatch().Enable();
        new LoadingModelPatch().Enable();
        new LoadingPlayerClickPatch().Enable();
        new ClearSelectionPatch().Enable();
        new ScreenClosePatch().Enable();
    }

    public static void Reset() => ArenaPreviewFlow.Reset();

    private sealed class ScreenClosePatch : ModulePatch
    {
        // Loading inherits its Close implementation. Hook the non-generic base,
        // restricted to these known setup screens and the exact owned subtree.
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(UIScreen), nameof(UIScreen.Close));
        [PatchPrefix]
        private static void Prefix(UIScreen __instance)
        {
            if (__instance is MatchMakerSelectionLocationScreen || __instance is MatchmakerOfflineRaidScreen
                || __instance is MatchMakerAcceptScreen || __instance is MatchmakerTimeHasCome)
                ArenaPreviewFlow.BeforeScreenClose(__instance.transform);
        }
    }

    private sealed class MapShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.Show),
                new[] { typeof(MatchMakerSelectionLocationScreen.SelectionLocationScreenController) });
        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance,
            MatchMakerSelectionLocationScreen.SelectionLocationScreenController controller)
            => ArenaPreviewFlow.HideForMap(controller.RaidSettings);
    }

    private sealed class OfflineShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchmakerOfflineRaidScreen), nameof(MatchmakerOfflineRaidScreen.Show),
                new[] { typeof(MatchmakerOfflineRaidScreen.OfflineRaidScreenController) });
        [PatchPostfix]
        private static void Postfix(MatchmakerOfflineRaidScreen __instance,
            MatchmakerOfflineRaidScreen.OfflineRaidScreenController controller)
            => ArenaPreviewFlow.ShowCompact(__instance.transform, controller.RaidSettings);
    }

    private sealed class AcceptShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerAcceptScreen), nameof(MatchMakerAcceptScreen.Show),
                new[] { typeof(MatchMakerAcceptScreen.MatchmakerAcceptScreenController) });
        [PatchPrefix]
        private static void Prefix(MatchMakerAcceptScreen __instance,
            MatchMakerAcceptScreen.MatchmakerAcceptScreenController controller)
            => ArenaPreviewFlow.PrepareAccept(__instance, controller.RaidSettings);
    }

    private sealed class AcceptVisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerPlayerPreview), nameof(MatchMakerPlayerPreview.UpdateVisual));
        [PatchPrefix]
        private static bool Prefix(MatchMakerPlayerPreview __instance)
            => !ArenaPreviewFlow.TryShowAccept(__instance);
    }

    private sealed class LoadingShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.Show),
                new[] { typeof(MatchmakerTimeHasCome.TimeHasComeScreenController) });
        [PatchPrefix]
        private static void Prefix(MatchmakerTimeHasCome __instance,
            MatchmakerTimeHasCome.TimeHasComeScreenController controller)
            => ArenaPreviewFlow.PrepareLoading(__instance, controller);
    }

    private sealed class LoadingModelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.ShowPlayerModel),
                new[] { typeof(PlayerVisualRepresentation) });
        [PatchPrefix]
        private static bool Prefix(MatchmakerTimeHasCome __instance,
            PlayerVisualRepresentation playerVisual, ref Task __result)
        {
            if (playerVisual != null)
            {
                ArenaPreviewFlow.ShowNativeLoading(__instance);
                return true;
            }
            if (!ArenaPreviewFlow.TryShowLoading(__instance)) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    private sealed class LoadingPlayerClickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.HandleOnPlayerClick));
        [PatchPrefix]
        private static bool Prefix(MatchmakerTimeHasCome __instance, RaidPlayer raidPlayer)
            => !ArenaPreviewFlow.IsLocalLoadingPlayer(__instance, raidPlayer)
               || !ArenaPreviewFlow.TryShowLoading(__instance);
    }

    private sealed class ClearSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ArenaMenuSelection), nameof(ArenaMenuSelection.Clear));
        [PatchPostfix]
        private static void Postfix() => ArenaPreviewFlow.Reset();
    }
}
