using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Movement;

/// <summary>
/// Shrinks the local deploy countdown for arena raids. The wait is pure padding:
/// player, loot and the full bot system are initialized BEFORE the countdown
/// coroutine, and GameTimer/spawn waves start after it — nothing races.
///
/// GlobalConfiguration.TimeBeforeDeployLocal is mutated ONLY between the
/// countdown coroutine's construction and vmethod_6 (restored there, plus
/// defensively at raid end). The field has exactly one reader (the countdown),
/// so the "never touch GlobalConfiguration" rule does not bite here — noted
/// deliberately. Floor of 2s: a 0s countdown pops the audio fade and shows the
/// pre-raid GC hitch.
/// </summary>
internal static class FastDeployPatches
{
    private const int ArenaCountdownSeconds = 2;

    private static int _saved = -1;

    public static void Enable()
    {
        new ShortenCountdownPatch().Enable();
        new RestoreAfterCountdownPatch().Enable();
    }

    public static void Restore()
    {
        if (_saved < 0)
            return;

        var config = Singleton<GlobalConfiguration>.Instance;
        if (config != null)
            config.TimeBeforeDeployLocal = _saved;

        _saved = -1;
    }

    private sealed class ShortenCountdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(LocalGame), "vmethod_2");

        [PatchPrefix]
        private static void Prefix()
        {
            // ArmedForNextRaid, NOT IsActive: the IsActive latch fires after this
            // countdown (GameWorld.OnGameStarted).
            if (!ArenaSession.ArmedForNextRaid || !Settings.FastDeploy.Value)
                return;

            var config = Singleton<GlobalConfiguration>.Instance;
            if (config == null || config.TimeBeforeDeployLocal <= ArenaCountdownSeconds)
                return;

            _saved = config.TimeBeforeDeployLocal;
            config.TimeBeforeDeployLocal = ArenaCountdownSeconds;
            Plugin.ModLogger.LogInfo($"Arena fast deploy: countdown {_saved}s -> {ArenaCountdownSeconds}s.");
        }
    }

    private sealed class RestoreAfterCountdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "vmethod_6");

        [PatchPostfix]
        private static void Postfix() => Restore();
    }
}
