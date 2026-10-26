using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>Resume a suspended exact setup before a native page renders or enables Ready.</summary>
internal static class ArenaSessionNavigation
{
    internal static void Enable()
    {
        new ArenaSessionNavigationAcceptPatch().Enable();
        new ArenaSessionNavigationOfflinePatch().Enable();
    }

    private sealed class ArenaSessionNavigationAcceptPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(MatchMakerAcceptScreen),
            nameof(MatchMakerAcceptScreen.Show), new[] { typeof(MatchMakerAcceptScreen.MatchmakerAcceptScreenController) });
        [PatchPrefix]
        private static void Prefix(MatchMakerAcceptScreen.MatchmakerAcceptScreenController controller)
            => ArenaMenuSelection.ResumeAfterMenu(controller?.RaidSettings);
    }

    private sealed class ArenaSessionNavigationOfflinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(MatchmakerOfflineRaidScreen),
            nameof(MatchmakerOfflineRaidScreen.Show), new[] { typeof(MatchmakerOfflineRaidScreen.OfflineRaidScreenController) });
        [PatchPrefix]
        private static void Prefix(MatchmakerOfflineRaidScreen.OfflineRaidScreenController controller)
            => ArenaMenuSelection.ResumeAfterMenu(controller?.RaidSettings);
    }
}
