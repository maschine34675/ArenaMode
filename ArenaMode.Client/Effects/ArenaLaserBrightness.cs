using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

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
    private sealed class Baseline
    {
        internal float Intensity, BeamFactor;
        internal WeakReference InitializedBeamMaterial;
        internal bool Initialized;
    }
    private static readonly int FactorId = Shader.PropertyToID("_Factor");
    private static ConditionalWeakTable<LaserBeam, Baseline> Baselines = new();

    internal static void Enable() => new ArenaLaserBrightnessAwakePatch().Enable();
    internal static void Reset() => Baselines = new ConditionalWeakTable<LaserBeam, Baseline>();

    internal static void Capture(LaserBeam laser)
    {
        try
        {
            if (laser == null || Baselines.TryGetValue(laser, out _)) return;
            float factor = VisibleBeamFactor(laser.BeamMaterial);
            if (!Positive(laser.IntensityFactor) && !Positive(factor)) return;
            Baselines.Add(laser, new Baseline { Intensity = laser.IntensityFactor, BeamFactor = factor });
        }
        catch { /* Observation must never interfere with native initialization. */ }
    }

    /// <summary>
    /// Native Awake has now completed its CreateLight call (and any BrightLasers
    /// material clone). Bind that exact resulting material weakly. A later
    /// unrelated replacement must not inherit this prefab's original factor.
    /// This observation has no rendering effects, even outside Arena.
    /// </summary>
    internal static void ObserveInitialized(LaserBeam laser)
    {
        try
        {
            if (laser == null || !Baselines.TryGetValue(laser, out var baseline) || baseline.Initialized) return;
            baseline.Initialized = true;
            if (Positive(VisibleBeamFactor(laser.BeamMaterial)))
                baseline.InitializedBeamMaterial = new WeakReference(laser.BeamMaterial);
        }
        catch { /* A missing initialization observation disables only the extra beam factor. */ }
    }

    internal static float ResolveBeamFactor(LaserBeam laser, float setting)
    {
        float current = VisibleBeamFactor(laser.BeamMaterial);
        if (!Positive(current) || !Positive(setting) || setting <= 1f
            || !Baselines.TryGetValue(laser, out var baseline) || !Positive(baseline.BeamFactor)
            || !ReferenceEquals(laser.BeamMaterial, baseline.InitializedBeamMaterial?.Target)) return current;
        float minimum = baseline.BeamFactor * Math.Min(setting, 5f);
        // The beam shader multiplies _Factor after its vertex intensity taper.
        // Override only the draw's visible factor, never the material or _IkFactor.
        return Positive(minimum) ? Math.Max(current, minimum) : current;
    }

    private static float VisibleBeamFactor(Material material)
        => material != null && string.Equals(material.shader?.name, "Custom/LaserBeam", StringComparison.Ordinal)
           && material.HasProperty(FactorId) ? material.GetFloat(FactorId) : 0f;

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

    private sealed class ArenaLaserBrightnessAwakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LaserBeam), nameof(LaserBeam.Awake));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(LaserBeam __instance) => Capture(__instance);
        [PatchPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(LaserBeam __instance) => ObserveInitialized(__instance);
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
