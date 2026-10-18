using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>
/// Arena mounted visible lasers. EFT draws its meshes with per-call property
/// blocks: tint those draws without changing pooled/shared materials. Restore
/// the exact original block references after every call, including exceptions.
/// TDM and protected Tactical use exact current teams. Brightness temporarily
/// raises native IntensityFactor to a prefab-relative minimum; it never stacks
/// another multiplier on BrightLasers. An independent beam-only minimum overrides
/// _Factor in its draw block, after the shader's native near-to-far taper. No
/// material, shader, activation, mode, alpha, IR factor, LightIntensity or width is changed.
/// </summary>
internal static class ArenaTeamLaserColors
{
    private static readonly FieldInfo BeamBlock = AccessTools.Field(typeof(LaserBeam), "_beamBlock");
    private static readonly FieldInfo PointBlock = AccessTools.Field(typeof(LaserBeam), "_pointBlock");
    private static readonly FieldInfo BeamLight = AccessTools.Field(typeof(LaserBeam), "_light");
    private static readonly FieldInfo WeaponOwner = AccessTools.Field(typeof(WeaponPrefab), "_player");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FactorId = Shader.PropertyToID("_Factor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly Dictionary<LaserBeam, RenderState> States = new();
    private static readonly List<LaserBeam> Retired = new();
    private static bool _warned;
    private static float _nextSweep;

    internal sealed class RenderState
    {
        internal readonly MaterialPropertyBlock Beam = new();
        internal readonly MaterialPropertyBlock Point = new();
        internal readonly ArenaTeamLaserLightLease LightLease = new();
        internal readonly ArenaLaserLightIntensityLease LightIntensityLease = new();
        internal LaserBeam Laser;
        internal Player Owner;
        internal GameWorld World;
        internal AbstractGame Game;
        internal ArenaMatch Match;
        internal Light Light;
        internal MaterialPropertyBlock OriginalBeam, OriginalPoint;
        internal float OriginalIntensity, AppliedIntensity;
        internal bool Rendering;
    }

    internal static void Enable()
    {
        ArenaLaserBrightness.Enable();
        new ArenaTeamLaserColorsLateUpdatePatch().Enable();
    }

    /// <summary>Cleanup catches inactive/pooled beams which no longer call LateUpdate.</summary>
    internal static void Tick()
    {
        if (!InScope()) { Reset(); return; }
        if (Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + 0.25f;
        Retired.Clear();
        foreach (var pair in States)
        {
            try
            {
                if (!TryOwner(pair.Key, out var owner, out _) || !SameContext(pair.Value, owner)
                    || !Visible(pair.Key)) Retired.Add(pair.Key);
            }
            catch { Retired.Add(pair.Key); }
        }
        foreach (var laser in Retired) Release(laser);
        Retired.Clear();
    }

    internal static void Reset()
    {
        foreach (var state in States.Values) Restore(state);
        States.Clear();
        Retired.Clear();
        _nextSweep = 0f;
    }

    private static bool InScope()
        => ArenaSession.IsActive && (Settings.TeamLasers?.Value == true || Settings.VisibleLaserBrightness?.Value > 1f
                                    || Settings.VisibleLaserBeamBrightness?.Value > 1f)
           && ArenaMatchService.Current?.Mode != null && !ArenaMatchService.Current.Mode.IsComplete
           && string.Equals(ArenaSession.ActiveMode, ArenaMatchService.Current.Mode.Id, StringComparison.Ordinal)
           && Singleton<GameWorld>.Instantiated && Singleton<AbstractGame>.Instantiated
           && Singleton<AbstractGame>.Instance.Status == GameStatus.Started
           && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance);

    private static bool SameContext(RenderState state, Player owner)
        => ReferenceEquals(owner, state.Owner) && ReferenceEquals(Singleton<GameWorld>.Instance, state.World)
           && ReferenceEquals(Singleton<AbstractGame>.Instance, state.Game)
           && ReferenceEquals(ArenaMatchService.Current, state.Match);

    private static bool TryOwner(LaserBeam laser, out Player owner, out bool? friendly)
    {
        owner = null;
        friendly = null;
        if (!InScope() || laser == null || !laser.isActiveAndEnabled) return false;
        var tactical = laser.GetComponentInParent<TacticalComboVisualController>();
        var weapon = laser.GetComponentInParent<WeaponPrefab>();
        if (tactical == null || tactical.LightMod?.IsActive != true || weapon == null) return false;
        owner = WeaponOwner?.GetValue(weapon) as Player;
        var hands = owner?.HandsController as Player.FirearmController;
        // Cached prefab owners survive pooling. Both the live controller and the
        // mode's exact physical roster must agree; names/profile IDs alone do not.
        if (hands?.ControllerGameObject == null
            || !ReferenceEquals(hands.ControllerGameObject.GetComponent<WeaponPrefab>(), weapon)
            || !ReferenceEquals(tactical.GetComponentInParent<WeaponPrefab>(), weapon)) return false;
        var world = Singleton<GameWorld>.Instance;
        if (owner == null || owner.HealthController?.IsAlive != true || string.IsNullOrEmpty(owner.ProfileId)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(owner.ProfileId), owner)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(owner.ProfileId), owner)) return false;
        var mode = ArenaMatchService.Current.Mode;
        if (mode is TeamDeathmatchMode teams)
        {
            if (!teams.TryClassifyCurrentBody(owner, out bool side)) return false;
            friendly = side;
        }
        else if (mode is TacticalMode tacticalMode && tacticalMode.IsTeamArena)
        {
            if (!tacticalMode.TryClassifyLaserBody(owner, out bool side)) return false;
            friendly = side;
        }
        else if (mode is ZombieMode zombieMode && zombieMode.IsTeamArena)
        {
            if (!zombieMode.IsCurrentFriendlyMarkerBody(owner)
                && !ReferenceEquals(owner, world.MainPlayer)) return false;
            friendly = true;
        }
        else
        {
            // Solo modes have no cosmetic team recoloring. Their brightness still
            // requires the current local body or a live native AI body in this raid.
            // Solo Zombie is deliberately NOT in this list: lasers are excluded
            // there; its protected team variant is handled above. An unrecognized
            // mode id falling through to "return false" below is the intended
            // behavior for it, not an oversight to "fix" by adding it here.
            if (mode.Id != ArenaMode.Shared.DeathmatchSelection.ModeId && mode.Id != "endless"
                && mode.Id != "rounds" && mode.Id != "gungame" && mode.Id != "tactical") return false;
            if (!ReferenceEquals(owner, world.MainPlayer))
            {
                var bot = owner.AIData?.BotOwner;
                if (!owner.IsAI || bot == null || bot.IsDead || bot.BotState != EBotState.Active
                    || bot.HealthController?.IsAlive != true || !ReferenceEquals(bot.GetPlayer, owner)
                    || !string.Equals(bot.ProfileId, owner.ProfileId, StringComparison.Ordinal)) return false;
            }
        }
        return (friendly.HasValue && Settings.TeamLasers?.Value == true)
               || Settings.VisibleLaserBrightness?.Value > 1f || Settings.VisibleLaserBeamBrightness?.Value > 1f;
    }

    private static bool Visible(LaserBeam laser)
        => laser != null && Visible(laser.BeamMaterial, false) && Visible(laser.PointMaterial, true);

    private static bool Visible(Material material, bool point)
        => material != null && ArenaTeamLaserPolicy.IsVisibleMaterial(material.shader?.name, point,
            material.HasProperty(ColorId), material.HasProperty(FactorId),
            material.HasProperty(FactorId) ? material.GetFloat(FactorId) : 0f);

    internal static RenderState Begin(LaserBeam laser)
    {
        RenderState state = null;
        try
        {
            if (!TryOwner(laser, out var owner, out bool? friendly) || !Visible(laser)
                || BeamBlock == null || PointBlock == null || BeamLight == null)
            {
                Release(laser);
                return null;
            }
            if (States.TryGetValue(laser, out state) && !SameContext(state, owner))
            {
                Release(laser);
                state = null;
            }
            if (state == null)
            {
                state = new RenderState { Laser = laser, Owner = owner, World = Singleton<GameWorld>.Instance,
                    Game = Singleton<AbstractGame>.Instance, Match = ArenaMatchService.Current };
                States.Add(laser, state);
            }
            // Reentrant rendering is not a native contract. Keep its existing
            // blocks rather than replacing the outer invocation's restore target.
            if (state.Rendering) return null;
            var originalBeam = BeamBlock.GetValue(laser) as MaterialPropertyBlock;
            var originalPoint = PointBlock.GetValue(laser) as MaterialPropertyBlock;
            if (originalBeam == null || originalPoint == null) { Release(laser); return null; }
            bool tint = friendly.HasValue && Settings.TeamLasers?.Value == true;
            Color palette = ArenaTracerPalette.Resolve(friendly == true
                ? Settings.FriendlyTracerColor.Value : Settings.OpponentTracerColor.Value);
            // Native LateUpdate writes every float consumed by each draw. These
            // blocks are reused, never installed permanently and never shared.
            state.Beam.Clear();
            state.Point.Clear();
            float beamFactor = ArenaLaserBrightness.ResolveBeamFactor(laser, Settings.VisibleLaserBeamBrightness?.Value ?? 1f);
            if (beamFactor > laser.BeamMaterial.GetFloat(FactorId))
                state.Beam.SetFloat(FactorId, beamFactor);
            if (tint)
            {
                state.Beam.SetColor(ColorId, ArenaTeamLaserPolicy.Tint(laser.BeamMaterial.GetColor(ColorId), palette));
                state.Point.SetColor(ColorId, ArenaTeamLaserPolicy.Tint(laser.PointMaterial.GetColor(ColorId), palette));
            }
            state.OriginalBeam = originalBeam;
            state.OriginalPoint = originalPoint;
            state.OriginalIntensity = laser.IntensityFactor;
            state.AppliedIntensity = ArenaLaserBrightness.Resolve(laser, Settings.VisibleLaserBrightness?.Value ?? 1f);
            state.Rendering = true;
            laser.IntensityFactor = state.AppliedIntensity;
            BeamBlock.SetValue(laser, state.Beam);
            PointBlock.SetValue(laser, state.Point);
            var light = BeamLight.GetValue(laser) as Light;
            if (!ReferenceEquals(state.Light, light))
            {
                RestoreLight(state);
                state.Light = light;
            }
            // Lights are rendered later than LateUpdate: keep this instance's
            // color until owner/mode/setting/inactive cleanup, not just this call.
            if (state.Light != null)
            {
                state.Light.intensity = state.LightIntensityLease.Restore(state.Light.intensity);
                state.Light.color = tint ? state.LightLease.Apply(state.Light.color, palette)
                    : state.LightLease.Restore(state.Light.color);
            }
            return state;
        }
        catch (Exception ex)
        {
            if (state != null) Restore(state);
            Release(laser);
            if (!_warned)
            {
                _warned = true;
                Plugin.ModLogger.LogWarning("Arena laser kept native rendering: " + ex.GetBaseException().Message);
            }
            return null;
        }
    }

    internal static void End(RenderState state)
    {
        if (state == null || !state.Rendering) return;
        try
        {
            if (state.Laser != null)
            {
                try
                {
                    if (state.Light != null && ArenaLaserBrightness.Positive(state.OriginalIntensity))
                        state.LightIntensityLease.Observe(state.Light.intensity,
                            state.Beam.GetFloat(IntensityId) * state.Laser.LightIntensity,
                            state.AppliedIntensity / state.OriginalIntensity);
                }
                catch { /* A disposed light must not prevent scalar/block restoration. */ }
                // Preserve an external scalar replacement just like external blocks.
                if (state.Laser.IntensityFactor.Equals(state.AppliedIntensity))
                    state.Laser.IntensityFactor = state.OriginalIntensity;
                // Do not overwrite a third-party replacement made during draw.
                if (ReferenceEquals(BeamBlock.GetValue(state.Laser), state.Beam))
                    BeamBlock.SetValue(state.Laser, state.OriginalBeam);
                if (ReferenceEquals(PointBlock.GetValue(state.Laser), state.Point))
                    PointBlock.SetValue(state.Laser, state.OriginalPoint);
            }
        }
        finally
        {
            state.Rendering = false;
            state.OriginalBeam = null;
            state.OriginalPoint = null;
        }
    }

    private static void RestoreLight(RenderState state)
    {
        if (state.Light != null)
        {
            state.Light.color = state.LightLease.Restore(state.Light.color);
            state.Light.intensity = state.LightIntensityLease.Restore(state.Light.intensity);
        }
        state.Light = null;
    }

    private static void Restore(RenderState state)
    {
        try { End(state); }
        catch { /* destroyed native component: nothing remains to restore */ }
        try { RestoreLight(state); }
        catch { /* destroyed native light */ }
    }

    private static void Release(LaserBeam laser)
    {
        if (ReferenceEquals(laser, null) || !States.TryGetValue(laser, out var state)) return;
        Restore(state);
        States.Remove(laser);
    }

    private sealed class ArenaTeamLaserColorsLateUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LaserBeam), nameof(LaserBeam.LateUpdate));
        [PatchPrefix]
        private static void Prefix(LaserBeam __instance, out RenderState __state) => __state = Begin(__instance);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, RenderState __state)
        {
            try { End(__state); }
            catch { /* Never replace an existing native exception. */ }
            return __exception;
        }
    }
}
