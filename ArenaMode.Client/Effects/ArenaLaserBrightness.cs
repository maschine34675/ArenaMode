using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArenaMode.Effects;

/// <summary>
/// Read-only prefab baseline, captured before CreateLight (including BrightLasers'
/// postfix). Awake may run in the hideout/preload, before Arena is armed. This
/// observation deliberately runs outside raids, but never writes any native value.
/// Weak keys retain neither pooled/destroyed beams nor players/worlds. Keep these
/// original values across raid ends: the same prefab can be reused in a later raid.
/// </summary>
internal static class ArenaLaserBrightness
{
    private sealed class Baseline { internal float Intensity; }
    private static ConditionalWeakTable<LaserBeam, Baseline> Baselines = new();

    internal static void Enable() => new AwakePatch().Enable();
    internal static void Reset() => Baselines = new ConditionalWeakTable<LaserBeam, Baseline>();

    internal static void Capture(LaserBeam laser)
    {
        try
        {
            if (laser == null || Baselines.TryGetValue(laser, out _)
                || !Positive(laser.IntensityFactor)) return;
            Baselines.Add(laser, new Baseline { Intensity = laser.IntensityFactor });
        }
        catch { /* Observation must never interfere with native initialization. */ }
    }

    internal static float Resolve(LaserBeam laser, float setting)
    {
        float current = laser.IntensityFactor;
        // 1 disables our addition, including when another mod dimmed the laser.
        // No baseline => fail open; never infer an original value from mod config.
        if (!Positive(current) || !Positive(setting) || setting <= 1f
            || !Baselines.TryGetValue(laser, out var baseline)) return current;
        float minimum = baseline.Intensity * Math.Min(setting, 5f);
        return Positive(minimum) ? Math.Max(current, minimum) : current;
    }

    internal static bool Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed class AwakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LaserBeam), nameof(LaserBeam.Awake));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(LaserBeam __instance) => Capture(__instance);
    }
}

/// <summary>
/// Native LateUpdate computes the deferred spot-light output from the same beam
/// intensity. Remember only a value matching that exact native write. Restoration
/// cannot undo an external change made after the draw. Never multiply last frame's
/// output or change LightIntensity, activation, range or any shared material.
/// </summary>
internal sealed class ArenaLaserLightIntensityLease
{
    private bool _applied;
    private float _original, _last;

    internal void Observe(float current, float expectedNativeWrite, float ratio)
    {
        if (!ArenaLaserBrightness.Positive(ratio) || ratio <= 1f
            || !ArenaLaserBrightness.Positive(expectedNativeWrite)
            || !current.Equals(expectedNativeWrite)) return;
        _last = current;
        _original = current / ratio;
        _applied = true;
    }

    internal float Restore(float current)
    {
        float result = _applied && current.Equals(_last) ? _original : current;
        _applied = false;
        return result;
    }
}
