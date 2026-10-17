using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ArenaMode.Session;

// Vanilla's delayed raid-intro fade stops the same coroutine that owns Stop's
// GameEnd callback. One-frame deferral is insufficient: WaitSeconds runs later.
// Keep all native scheduling, except that exact obsolete cosmetic callback once
// this Arena raid has committed a protected failed-start exit.
internal static class ArenaFailedStartIntro
{
    [ThreadStatic] private static PreloaderUI _intro;

    public static void Enable()
    {
        new IntroScopePatch().Enable();
        new DeferredFadePatch().Enable();
    }

    internal static void EnterIntro(PreloaderUI preloader, out PreloaderUI previous)
    {
        previous = _intro;
        _intro = preloader;
    }

    internal static void LeaveIntro(PreloaderUI previous) => _intro = previous;

    internal static Coroutine WaitForFade(MonoBehaviour component, float seconds, Action action)
    {
        var preloader = component as PreloaderUI;
        if (preloader == null || !ReferenceEquals(_intro, preloader))
            return Utils.WaitSeconds(component, seconds, action);

        // Capture even before OnGameStarted: intro scheduling and confirmation
        // can occur in either order. Only the exact confirmed abort may suppress
        // it later; merely selecting Arena or LastRaidWasArena is insufficient.
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        var world = Singleton<GameWorld>.Instance;
        var profileId = game?.ProfileId;
        var epoch = ArenaSession.ArmEpoch;
        return Utils.WaitSeconds(component, seconds, () =>
        {
            if (ReferenceEquals(MonoBehaviourSingleton<PreloaderUI>.Instance, preloader)
                && epoch == ArenaSession.ArmEpoch
                && ArenaMatchService.OwnsFailedStartTeardown(game, world, profileId))
            {
                Plugin.ModLogger.LogDebug("Arena failed-start exit retained its native end callback; obsolete raid-intro fade skipped.");
                return;
            }
            action();
        });
    }

    private sealed class IntroScopePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(PreloaderUI), nameof(PreloaderUI.ShowRaidStartInfo));

        [PatchPrefix]
        private static void Prefix(PreloaderUI __instance, out PreloaderUI __state)
            => EnterIntro(__instance, out __state);

        [PatchFinalizer]
        private static void Finalizer(PreloaderUI __state) => LeaveIntro(__state);
    }

    private sealed class DeferredFadePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(PreloaderUI), nameof(PreloaderUI.FadeBlackScreen));

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var wait = AccessTools.DeclaredMethod(typeof(Utils), nameof(Utils.WaitSeconds),
                new[] { typeof(MonoBehaviour), typeof(float), typeof(Action) });
            var calls = code.Where(i => i.Calls(wait)).ToArray();
            if (wait == null || calls.Length != 1)
                throw new InvalidOperationException("Native raid-intro deferred fade contract changed.");
            calls[0].operand = AccessTools.DeclaredMethod(typeof(ArenaFailedStartIntro), nameof(WaitForFade));
            return code;
        }
    }
}
