using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Combat;

/// <summary>
/// Speeds the local PMC's medical applications during an active Arena raid.
///
/// SPT's MedEffect owns both the start delay and the working duration. Its
/// RegularUpdate divides the fixed amount of healing by WorkStateTime, so
/// scaling the duration and the current-state clock together preserves the
/// total health/resource transaction while delivering it sooner. The matching
/// item animator is accelerated by the same factor from its exact pre-Arena
/// speed; this keeps the hands animation and health effect in step without
/// changing a shared item template, skill or global health setting.
///
/// The live-effect route was compared with BlackHawk-QuickMeds (MIT), then
/// independently verified against the local SPT 4.1 decompile. MedicalAttention
/// (MIT) instead edits server item templates globally, which is deliberately not
/// suitable here because ordinary raids must remain untouched.
/// </summary>
internal static class ArenaMedicalPatches
{
    private static bool _enabled;
    private static bool _reportedActive;
    private static bool _reportedFailure;
    private static Player.MedsController _acceleratedController;
    private static float _originalAnimationSpeed = 1f;

    internal static void Enable()
    {
        if (_enabled)
            return;

        new MedicalEffectPatch().Enable();
        new MedsControllerDestroyPatch().Enable();
        RaidLifecycle.EnsurePatched();
        RaidLifecycle.OnRaidStarted += OnRaidStarted;
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
        _enabled = true;
    }

    internal static void Shutdown()
    {
        if (_enabled)
        {
            RaidLifecycle.OnRaidStarted -= OnRaidStarted;
            RaidLifecycle.OnRaidEnded -= OnRaidEnded;
            _enabled = false;
        }

        RestoreAnimator();
    }

    private static void OnRaidStarted(GameWorld _)
    {
        RestoreAnimator();
        _reportedActive = false;
        _reportedFailure = false;
    }

    private static void OnRaidEnded()
    {
        RestoreAnimator();
        _reportedActive = false;
        _reportedFailure = false;
    }

    private static bool TryGetLocalArenaUse(
        ActiveHealthController health,
        Item item,
        out Player.MedsController controller,
        out float multiplier)
    {
        controller = null;
        multiplier = ArenaMedicalTiming.NormalizeMultiplier(Settings.MedicalUseSpeed.Value);
        if (!_enabled)
            return false;

        if (!ArenaSession.IsActive
            || multiplier <= ArenaMedicalTiming.MinimumMultiplier
            || item is not Meds
            || !Singleton<GameWorld>.Instantiated)
        {
            return false;
        }

        Player player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player == null
            || player.IsAI
            || player.HealthController?.IsAlive != true
            || !ReferenceEquals(player.ActiveHealthController, health)
            || player.HandsController is not Player.MedsController medsController
            || !ReferenceEquals(medsController.GetItem(), item))
        {
            return false;
        }

        controller = medsController;
        return true;
    }

    private static void AccelerateAnimator(Player.MedsController controller, float multiplier)
    {
        var firearmsAnimator = controller?._firearmsAnimator;
        var animator = firearmsAnimator?.Animator;
        if (animator == null)
            throw new InvalidOperationException("the local medical animator was unavailable");

        if (!ReferenceEquals(_acceleratedController, controller))
        {
            RestoreAnimator();
            float currentSpeed = animator.speed;
            float originalSpeed = float.IsNaN(currentSpeed)
                || float.IsInfinity(currentSpeed)
                || currentSpeed <= 0f
                    ? 1f
                    : currentSpeed;

            // Publish ownership only after the native write succeeds. A failed
            // SetAnimationSpeed must not leave a controller registered as if we
            // had changed it, or shutdown would attempt a second failing write.
            firearmsAnimator.SetAnimationSpeed(originalSpeed * multiplier);
            _originalAnimationSpeed = originalSpeed;
            _acceleratedController = controller;
            return;
        }

        // Always derive from the captured baseline. Multi-limb medkits call the
        // patch once per limb; multiplying the already accelerated value would
        // otherwise grow 2x into 4x, 8x and so on.
        firearmsAnimator.SetAnimationSpeed(_originalAnimationSpeed * multiplier);
    }

    private static void RestoreAnimator()
    {
        Player.MedsController controller = _acceleratedController;
        float originalSpeed = _originalAnimationSpeed;
        _acceleratedController = null;
        _originalAnimationSpeed = 1f;
        if (ReferenceEquals(controller, null))
            return;

        try
        {
            controller._firearmsAnimator?.SetAnimationSpeed(originalSpeed);
        }
        catch (Exception ex)
        {
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena medical animator restore failed safely: {ex.Message}");
            }
        }
    }

    private sealed class MedicalEffectPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(ActiveHealthController),
                nameof(ActiveHealthController.DoMedEffect),
                new[] { typeof(Item), typeof(EBodyPart), typeof(float?) });

        [PatchPostfix]
        private static void Postfix(
            ActiveHealthController __instance,
            Item item,
            IHealthEffect __result)
        {
            if (!TryGetLocalArenaUse(__instance, item, out var controller, out float multiplier)
                || __result is not ActiveHealthController.MedEffect effect
                || !ReferenceEquals(effect.MedItem, item))
            {
                return;
            }

            bool waitingForStart;
            if (effect.State == EEffectState.Added)
                waitingForStart = true;
            else if (effect.State == EEffectState.Started)
                waitingForStart = false;
            else
                return;

            try
            {
                if (!ArenaMedicalTiming.TryScale(
                        multiplier,
                        waitingForStart,
                        effect.DelayTime,
                        effect.WorkStateTime,
                        effect._stateTime,
                        effect.StateCurrentTime,
                        effect.WholeTime,
                        effect.BuildUpTime,
                        out ArenaMedicalTimingState scaled))
                {
                    return;
                }

                // Change the animator first. If its native object is unavailable,
                // leave every health timer untouched instead of creating a fast
                // effect whose hands animation still runs at vanilla speed.
                AccelerateAnimator(controller, multiplier);
                effect.DelayTime = scaled.Delay;
                effect.WorkStateTime = scaled.Work;
                effect._stateTime = scaled.StateDuration;
                effect.StateCurrentTime = scaled.StateElapsed;
                effect.WholeTime = scaled.WholeElapsed;
                effect.BuildUpTime = scaled.BuildUp;

                if (!_reportedActive)
                {
                    _reportedActive = true;
                    Plugin.ModLogger.LogInfo(
                        $"Arena medical speed active: {multiplier:0.##}x for the local PMC.");
                }
            }
            catch (Exception ex)
            {
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    Plugin.ModLogger.LogWarning(
                        $"Arena medical speed failed safely; vanilla timing retained: {ex.Message}");
                }
            }
        }
    }

    private sealed class MedsControllerDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.MedsController), nameof(Player.MedsController.Destroy));

        [PatchPrefix]
        private static void Prefix(Player.MedsController __instance)
        {
            if (ReferenceEquals(_acceleratedController, __instance))
                RestoreAnimator();
        }
    }
}
