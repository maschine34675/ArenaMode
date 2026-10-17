#nullable disable
using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Performance;

/// <summary>
/// Harmony half of <see cref="BotHotPathPolicy"/>: two prefixes that remove
/// per-frame bot work the native game repeats without a result, in any
/// active Arena raid. Both are gated on <c>ArenaSession.IsActive</c> and their
/// own F12 switch, and both fail open to the native path on any exception.
///
/// 1. <c>AICoreController.Update</c>: the removal queue of dead brains is
///    applied and emptied before the native pass (see
///    <see cref="BotHotPathPolicy.TrimDeletedAgents{T}"/>). A prefix, not a
///    postfix: agents queue themselves for removal from inside the native
///    agent loop, so clearing after the loop would drop a queued agent before
///    it was ever removed.
/// 2. <c>ProceduralWeaponAnimation.StartFovCoroutine</c>: for an AI body under
///    <c>ThirdPersonStrategy</c> whose ribcage scale is one (the only state
///    in which the coroutine's deferred reset is a no-op) the coroutine's
///    remaining statement runs directly instead of allocating an iterator and
///    a Coroutine per bot per frame
///    (<c>Player.LateUpdate</c>, ../_decompiled/EFT/Player.cs:25979, for every
///    alive body). Measured context: ≈ 13 MB/s managed allocation in a
///    32-zombie raid, see ZOMBIE-PERFORMANCE-IDEAS-20261017.md.
/// </summary>
internal static class ArenaBotHotPaths
{
    private static bool _deadAgentsEnabled, _fovEnabled, _warned;
    private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, IProceduralAnimationStrategy> Strategy = ResolveStrategy();

    private static WeakReference _diagnosticWorld;
    private static float _nextDiagnostic;
    private static long _agentsTrimmed, _fovShortCircuits;

    internal static void Initialize()
    {
        if (!_deadAgentsEnabled)
        {
            try { new DeadAgentsPatch().Enable(); _deadAgentsEnabled = true; }
            catch (Exception ex) { Warn("dead-brain queue trim", ex); }
        }
        if (!_fovEnabled)
        {
            if (Strategy == null)
            {
                Warn("bot FOV coroutine short-circuit", new MissingFieldException(nameof(ProceduralWeaponAnimation), "_strategy"));
            }
            else
            {
                try { new FovCoroutinePatch().Enable(); _fovEnabled = true; }
                catch (Exception ex) { Warn("bot FOV coroutine short-circuit", ex); }
            }
        }
    }

    private static AccessTools.FieldRef<ProceduralWeaponAnimation, IProceduralAnimationStrategy> ResolveStrategy()
    {
        try
        {
            var field = AccessTools.DeclaredField(typeof(ProceduralWeaponAnimation), "_strategy");
            if (field == null || field.IsStatic || !typeof(IProceduralAnimationStrategy).IsAssignableFrom(field.FieldType))
                return null;
            return AccessTools.FieldRefAccess<ProceduralWeaponAnimation, IProceduralAnimationStrategy>(field);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void TrimDeadAgents(AICoreController controller)
    {
        try
        {
            if (controller == null || !ArenaSession.IsActive || Settings.TrimDeadBrainList?.Value != true)
                return;
            int trimmed = BotHotPathPolicy.TrimDeletedAgents(controller._listAgents, controller._listToDel, true, controller._enable);
            if (trimmed > 0)
            {
                _agentsTrimmed += trimmed;
                Diagnose();
            }
        }
        catch (Exception ex) { Warn("dead-brain queue trim", ex); }
    }

    /// <summary>True when the native coroutine must still run.</summary>
    internal static bool RunNativeFovCoroutine(ProceduralWeaponAnimation animation, Player player)
    {
        try
        {
            if (animation == null || player == null)
                return true;
            bool thirdPerson = Strategy != null && Strategy(animation) is ThirdPersonStrategy;
            if (!BotHotPathPolicy.ShouldShortCircuitFov(Settings.SkipBotFovCoroutine?.Value == true,
                    ArenaSession.IsActive, player.IsAI, thirdPerson))
                return true;
            // The coroutine body for this strategy: ApplyFovAdjustments now
            // (RibcageScaleCurrent = 1), then, after the frame has rendered,
            // ResetFovAdjustments, which only acts when the ribcage scale is not
            // one. Taken over only while that scale IS one, where the reset is a
            // no-op and the frame renders the same pose either way; a drifted
            // scale keeps the native coroutine so the reset stays after the render.
            var ribcage = player.PlayerBones?.Ribcage?.Original;
            if (ribcage == null || !Mathf.Approximately(ribcage.localScale.z, 1f))
                return true;
            player.RibcageScaleCurrent = 1f;
            _fovShortCircuits++;
            Diagnose();
            return false;
        }
        catch (Exception ex)
        {
            Warn("bot FOV coroutine short-circuit", ex);
            return true;
        }
    }

    private static void Diagnose()
    {
        // Per-raid counters keyed on the exact world, like ZombiePerformancePatches:
        // a weak reference cannot retain a departed world, and a new world starts
        // fresh even after a missed end callback.
        var world = Comfort.Common.Singleton<GameWorld>.Instantiated ? Comfort.Common.Singleton<GameWorld>.Instance : null;
        if (world == null)
            return;
        if (!ReferenceEquals(_diagnosticWorld?.Target, world))
        {
            _diagnosticWorld = new WeakReference(world);
            _nextDiagnostic = Time.realtimeSinceStartup + 60f;
            _agentsTrimmed = 0;
            _fovShortCircuits = 0;
            Plugin.ModLogger.LogDebug("Arena bot hot paths: dead-brain queue trim "
                + (Settings.TrimDeadBrainList?.Value == true ? "on" : "off")
                + ", bot FOV coroutine short-circuit "
                + (Settings.SkipBotFovCoroutine?.Value == true ? "on" : "off")
                + "; 60 s aggregate counters, native results retained.");
            return;
        }
        if (Time.realtimeSinceStartup < _nextDiagnostic)
            return;
        _nextDiagnostic = Time.realtimeSinceStartup + 60f;
        Plugin.ModLogger.LogDebug($"Arena bot hot paths (last interval): deadBrainsTrimmed={_agentsTrimmed}, fovCoroutinesSkipped={_fovShortCircuits}; counts are avoided work, not frame-time savings.");
        _agentsTrimmed = 0;
        _fovShortCircuits = 0;
    }

    private static void Warn(string what, Exception ex)
    {
        if (_warned)
            return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot hot paths: " + what + " unavailable for this call; native behavior retained. " + ex.GetBaseException().Message);
    }

    private sealed class DeadAgentsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(AICoreController), nameof(AICoreController.Update), Type.EmptyTypes);

        [PatchPrefix]
        private static void Prefix(AICoreController __instance) => TrimDeadAgents(__instance);
    }

    private sealed class FovCoroutinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.StartFovCoroutine),
                new[] { typeof(Player) });

        [PatchPrefix]
        private static bool Prefix(ProceduralWeaponAnimation __instance, Player player) => RunNativeFovCoroutine(__instance, player);
    }
}
