using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core.View;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Spectator;

/// <summary>
/// Removes first-person health presentation owned by the dead local body from
/// the persistent FPS camera. The health controller and its effects remain
/// untouched; Redeploy later binds this same EffectsController to the replacement
/// body.
/// </summary>
internal static class SpectatorHealthEffects
{
    private static readonly FieldInfo BoundPlayerField =
        AccessTools.Field(typeof(EffectsController), "player");

    private static readonly FieldInfo AccumulatorsField =
        AccessTools.Field(typeof(EffectsController), "_effectAccumulators");

    private static readonly FieldInfo BloodDropsControllerField =
        AccessTools.Field(typeof(BloodOnScreen), "_bloodDropsController");

    private static readonly FieldInfo BloodTextureField =
        AccessTools.Field(typeof(BloodOnScreen), "_duDvMap");

    private static readonly FieldInfo LastBloodHitField =
        AccessTools.Field(typeof(BloodOnScreen), "_lastHitTime");

    private static readonly FieldInfo LastHeavyBleedHitField =
        AccessTools.Field(typeof(BloodOnScreen), "_lastHeavyBleedingHitTime");

    private static readonly FieldInfo BloodLockedField =
        AccessTools.Field(typeof(BloodOnScreen), "_locked");

    private static bool _failureLogged;
    private static Player _leaseBody;
    private static int _automaticAttempts;

    internal static void ResetForLease()
    {
        _failureLogged = false;
        _leaseBody = null;
        _automaticAttempts = 0;
    }

    internal static void ClearForDeadLocal(Player expectedDeadPlayer, bool force = false)
        => ClearForDeadLocal(expectedDeadPlayer, force, replayGame: null);

    /// <summary>
    /// Replay-only entrance for non-PFT modes. A waiting respawn supplies the
    /// exact native game and corpse; no spectator lease or camera ownership is
    /// created. Ordinary raids and idle optional cameras remain untouched.
    /// </summary>
    internal static void ClearForDeathReplay(LocalGame game, Player expectedDeadPlayer)
    {
        if (!HasExactReplayContext(game, expectedDeadPlayer))
            return;
        ClearForDeadLocal(expectedDeadPlayer, force: false, replayGame: game);
    }

    internal static bool HasExactReplayContext(LocalGame game, Player expectedDeadPlayer)
    {
        if (!ArenaSession.IsActive || ArenaReplaySelection.Seconds <= 0f
            || game == null || game.Status != GameStatus.Started
            || expectedDeadPlayer == null || !expectedDeadPlayer.IsYourPlayer
            || expectedDeadPlayer.HealthController?.IsAlive != false
            || !ReferenceEquals(game.LocalPlayer, expectedDeadPlayer))
            return false;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return world != null && ArenaSession.IsExactActiveRaid(game, world)
            && ReferenceEquals(world.MainPlayer, expectedDeadPlayer)
            && KillcamCompat.ReadState(out _, out _) == OptionalCameraOwnerState.Busy;
    }

    private static void ClearForDeadLocal(Player expectedDeadPlayer, bool force, LocalGame replayGame)
    {
        // One synchronous pass handles effects already present at death. A second
        // pass on the next spectator tick closes event-ordering races before
        // Killcam's lead-in ends, then this becomes allocation-free for the rest
        // of the observed round.
        if (!ReferenceEquals(_leaseBody, expectedDeadPlayer))
        {
            _leaseBody = expectedDeadPlayer;
            _automaticAttempts = 0;
        }

        if (!force && _automaticAttempts >= 2)
            return;

        // A broken optional/custom accumulator must not turn this cleanup into
        // a per-frame exception and allocation loop for the rest of the round.
        if (!force)
            _automaticAttempts++;

        try
        {
            string problem;
            bool cleared = replayGame == null
                ? TryClearForDeadLocal(expectedDeadPlayer, out problem)
                : TryClearForDeathReplay(replayGame, expectedDeadPlayer, out problem);
            if (cleared)
            {
                return;
            }

            LogFailureOnce(problem);
        }
        catch (Exception ex)
        {
            LogFailureOnce("unexpected visual cleanup failure: " + ex.GetBaseException().Message);
        }
    }

    internal static bool TryClearForDeadLocal(Player expectedDeadPlayer, out string problem)
    {
        problem = null;
        if (!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive)
        {
            problem = "the protected team Arena gates were no longer active";
            return false;
        }

        return TryClearVisualState(expectedDeadPlayer, out problem);
    }

    private static bool TryClearForDeathReplay(LocalGame game, Player expectedDeadPlayer, out string problem)
    {
        if (!HasExactReplayContext(game, expectedDeadPlayer))
        {
            problem = "the exact dead-player Arena replay was no longer active";
            return false;
        }
        return TryClearVisualState(expectedDeadPlayer, out problem);
    }

    private static bool TryClearVisualState(Player expectedDeadPlayer, out string problem)
    {
        problem = null;

        if (ReferenceEquals(expectedDeadPlayer, null)
            || expectedDeadPlayer == null
            || !expectedDeadPlayer.IsYourPlayer
            || expectedDeadPlayer.HealthController?.IsAlive != false)
        {
            problem = "the supplied body was not the exact dead local player";
            return false;
        }

        if (!CameraManager.Exist
            || CameraManager.Instance?.Camera == null
            || CameraManager.Instance.EffectsController == null)
        {
            problem = "the FPS camera or EffectsController was unavailable";
            return false;
        }

        if (BoundPlayerField == null || AccumulatorsField == null)
        {
            problem = "the SPT 4.1 EffectsController contract was unavailable";
            return false;
        }

        EffectsController controller = CameraManager.Instance.EffectsController;
        if (!ReferenceEquals(BoundPlayerField.GetValue(controller), expectedDeadPlayer))
        {
            problem = "EffectsController was not bound to the exact dead local body";
            return false;
        }

        if (AccumulatorsField.GetValue(controller)
            is not List<EffectsController.EffectAccumulator> accumulators)
        {
            problem = "EffectsController exposed no health-effect accumulator list";
            return false;
        }

        var failures = new List<string>();
        foreach (EffectsController.EffectAccumulator accumulator in accumulators)
        {
            if (accumulator?.ActiveEffects == null)
            {
                failures.Add(accumulator == null
                    ? "a null accumulator"
                    : accumulator.GetType().Name + " had no active-effect list");
                continue;
            }

            while (accumulator.ActiveEffects.Count > 0)
            {
                int before = accumulator.ActiveEffects.Count;
                try
                {
                    // DeleteEffect is the native visual unsubscribe path. Calling
                    // ActiveEffects.Clear/OnDisable would leave components enabled;
                    // FlashAccumulator would then call Max() on an empty list.
                    accumulator.DeleteEffect(accumulator.ActiveEffects[0]);
                }
                catch (Exception ex)
                {
                    failures.Add(
                        accumulator.GetType().Name + " delete failed: "
                        + ex.GetBaseException().Message);
                    break;
                }

                if (accumulator.ActiveEffects.Count >= before)
                {
                    failures.Add(accumulator.GetType().Name + " made no delete progress");
                    break;
                }
            }

            try
            {
                ClearResidualState(accumulator);
            }
            catch (Exception ex)
            {
                failures.Add(
                    accumulator.GetType().Name + " residual reset failed: "
                    + ex.GetBaseException().Message);
            }
        }

        ClearBloodOverlay(CameraManager.Instance.Camera, failures);

        // Eye burn is event-driven rather than accumulator-driven. Disabling it
        // runs its native OnDisable cleanup and a later burn re-enables it.
        EyeBurn eyeBurn = CameraManager.Instance.Camera.GetComponent<EyeBurn>();
        if (eyeBurn != null)
            eyeBurn.enabled = false;

        Shader.SetGlobalFloat("_BlindnessCoef", 0f);
        DeathEffectsHelper.ClearDeathScreenEffects();

        if (failures.Count == 0)
            return true;

        problem = string.Join("; ", failures);
        return false;
    }

    private static void ClearResidualState(EffectsController.EffectAccumulator accumulator)
    {
        switch (accumulator)
        {
            case EffectsController.CC_WiggleAccumulator wiggle:
                wiggle._stimulatorBuffs?.Clear();
                wiggle.UpdateScaleAndSpeed();
                if (wiggle.cc_Wiggle_0 != null)
                {
                    wiggle.cc_Wiggle_0.speed = 0f;
                    wiggle.cc_Wiggle_0.scale = 0f;
                    wiggle.cc_Wiggle_0.str = 0f;
                }
                wiggle.Toggle(false);
                break;

            case EffectsController.MotionBlurAccumulator motionBlur:
                motionBlur._stimulatorBuffs?.Clear();
                motionBlur.UpdateGoalValue();
                if (motionBlur._effect != null)
                    motionBlur._effect.blurAmount = 0f;
                motionBlur.Toggle(false);
                break;

            case EffectsController.FlashAccumulator flash:
                flash._indoorEffects?.Clear();
                if (flash._screenEffect != null)
                    flash._screenEffect.EffectStrength = 0f;
                flash.Toggle(false);
                break;

            case EffectsController.CC_BlendAccumulator blend:
                blend.Toggle(false);
                break;

            case EffectsController.FrostbiteAccumulator frostbite:
                frostbite._restore = false;
                frostbite.MaxEffectValue = 1f;
                if (frostbite._effect != null)
                {
                    frostbite._effect.Radius = 1f;
                    frostbite._effect.enabled = false;
                }
                frostbite.Toggle(false);
                break;

            case EffectsController.DesaturateAccumulator desaturate:
                ResetHealthDesaturation(desaturate.cc_Sharpen_0);
                break;

            case EffectsController.DesaturateMaskAccumulator mask:
                mask._restoreDesaturateMask = false;
                mask.MaxEffectValue = 0f;
                ResetHealthDesaturation(mask.cc_Sharpen_0);
                mask.Toggle(false);
                break;
        }
    }

    private static void ResetHealthDesaturation(CC_Sharpen sharpen)
    {
        if (sharpen == null)
            return;

        sharpen.HealthDesaturate = 0f;
        sharpen.MaskDesaturate = 0f;
        sharpen.Radius = sharpen.MinMaxRadius.y;

        DesaturateEffect provider = sharpen.DesaturateEffectSettingsProvider;
        if (provider == null)
            return;

        provider.HealthDesaturate = 0f;
        provider.MaskDesaturate = 0f;
        provider.Radius = provider.MinMaxRadius.y;
    }

    private static void ClearBloodOverlay(Camera camera, ICollection<string> failures)
    {
        BloodOnScreen blood = camera.GetComponent<BloodOnScreen>();
        if (blood == null)
            return;

        if (BloodDropsControllerField == null
            || BloodTextureField == null
            || LastBloodHitField == null
            || LastHeavyBleedHitField == null
            || BloodLockedField == null)
        {
            failures.Add("the BloodOnScreen reset contract was unavailable");
            return;
        }

        try
        {
            if (BloodDropsControllerField.GetValue(blood) is BloodDropsController drops)
            {
                if (drops._dropsToRender != null && drops._freeDrops != null)
                {
                    while (drops._dropsToRender.Count > 0)
                        drops._freeDrops.Enqueue(drops._dropsToRender.Dequeue());
                }

                ClearRenderTexture(drops._dropsTex);
            }

            ClearRenderTexture(BloodTextureField.GetValue(blood) as RenderTexture);
            LastBloodHitField.SetValue(blood, float.MinValue);
            LastHeavyBleedHitField.SetValue(blood, float.MinValue);
            BloodLockedField.SetValue(blood, false);
        }
        catch (Exception ex)
        {
            failures.Add("BloodOnScreen reset failed: " + ex.GetBaseException().Message);
        }
    }

    private static void ClearRenderTexture(RenderTexture texture)
    {
        if (texture == null)
            return;

        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.SetRenderTarget(texture);
            GL.Clear(clearDepth: true, clearColor: true, Color.black);
        }
        finally
        {
            Graphics.SetRenderTarget(previous);
        }
    }

    private static void LogFailureOnce(string problem)
    {
        if (_failureLogged)
            return;

        _failureLogged = true;
        Plugin.ModLogger.LogWarning(
            "Arena spectator could not fully clear the dead player's visual health effects: "
            + (problem ?? "unspecified failure"));
    }
}
