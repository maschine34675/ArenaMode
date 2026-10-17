#nullable disable
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Bots;

/// <summary>
/// Unity/EFT half of <see cref="ZombieBodyCullingPolicy"/>. Two independent
/// F12 switches, both only for a live, exact Arena Zombie raid and only for
/// generic infected bodies (never the player, companions or other roles):
///
/// <b>CullOffscreenAnimators</b> — a postfix on <c>Player.VisualPass</c> that,
/// inside the native 10 m AlwaysAnimate radius, sets the body animator to
/// <c>CullUpdateTransforms</c> exactly as the game itself does beyond that
/// radius, so Unity skips retargeting and transform writes while no camera
/// (shadow passes included) renders the body. The state machine and root
/// motion keep running, so the infected keeps walking; its hit zones travel
/// with the root at the last written pose. Stateless by design: the native
/// method writes the culling mode every frame, so the moment this code stops
/// overriding (switch off, body near a target, swing starting) the native
/// value is back on the same frame; where the native method returned early
/// (<c>CustomAnimationsAreProcessing</c>) the override is undone explicitly.
///
/// <b>HideOffscreenShadows</b> — for a body outside the padded main-camera
/// frustum (and not possibly seen through a rendering optic) every renderer
/// <c>PlayerBody.GetRenderersNonAlloc</c> reports (body skins, clothing,
/// gear) has its <c>shadowCastingMode</c> set to Off. Ownership rules learned
/// from the retracted 2026-10-14 Crowd renderer writes
/// (GPU-ZOMBIE-REVIEW-20261014.md P1/P2): only this code's own writes are
/// remembered, per renderer, with the mode they replaced; they are put back
/// when the body is in view again, when it dies or leaves eligibility, when
/// the switch goes off, when the raid ends, and — because clothing,
/// gear and the item in the hands are pooled models, and the native reset on
/// return covers only dresses of a model that owns a ContainerCollectionView
/// (<c>AssetPoolObject.ReturnToPool</c> → <c>DisposeContainerCollectionView</c>
/// → <c>IDress.Deinit</c> → <c>AdjustShadowMode(ThirdPerson)</c>; rigs and
/// backpacks), while weapon and other slot models get none — in a prefix on
/// the instance <c>AssetPoolObject.ReturnToPool()</c> for any pooled object
/// above a changed renderer, before the pool sees it. Shadows are only ever
/// hidden while that prefix is installed. A renderer whose mode somebody
/// else changed meanwhile is left as they set it. The item in the hands is
/// included on purpose: <c>SlotView</c> leaves it out of
/// <c>GetRenderersNonAlloc</c>, and one casting renderer under the body's
/// animator keeps Unity from culling it.
///
/// What the shadow switch does NOT see: local lights. The reach test follows
/// the sky's directional light only (TOD_Sky), up to 15 m along the ground,
/// so the shadow an unseen infected throws into the picture from the sun or
/// moon stays, while a shadow from a lamp behind the player can vanish while
/// its caster is outside the padded view. The camera pose is the one of this
/// frame's LateUpdate; a hard cut (spectator switch, killcam start) can show
/// one frame without the shadows of bodies near the new view.
///
/// The player's own body, PitFireTeam companions, bosses and every
/// non-Zombie raid are untouched; both switches fail open to the native
/// behavior on any exception.
/// </summary>
internal static class ZombieBodyCullingRuntime
{
    private sealed class State
    {
        public Player Body;
        public bool EligibilityKnown, Eligible;
        public float NearRadius = ZombieBodyCullingPolicy.MinimumNearRadius;
        public int LastFrame = -1;
        public bool Tracked;
        public bool AnimatorCulled;
        public bool ShadowsHidden;
        public float OutsideSince = float.NaN;
        public float NextRefresh;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<ShadowCastingMode> Original = new List<ShadowCastingMode>();
        public readonly HashSet<Renderer> Changed = new HashSet<Renderer>();
        public readonly List<AssetPoolObject> Pools = new List<AssetPoolObject>();
    }

    private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();
    private static readonly ConditionalWeakTable<Player, State>.CreateValueCallback NewState = body => new State { Body = body };
    private static readonly List<State> Tracked = new List<State>();
    private static readonly Dictionary<AssetPoolObject, State> ByPool = new Dictionary<AssetPoolObject, State>();
    private static readonly List<Renderer> Scratch = new List<Renderer>(64);
    private static readonly List<Renderer> HandsScratch = new List<Renderer>(16);
    private static readonly Plane[] PlaneScratch = new Plane[6];
    private static readonly float[] Planes = new float[24];

    private static bool _visualEnabled, _poolEnabled, _warned;
    private static int _frame = -1;
    private static bool _raidOk, _hasCamera, _optic, _mainValid;
    private static Vector3 _cameraPosition, _cameraForward, _mainPosition;
    private static float _cullDistance;
    private static float _shadowOffsetX, _shadowOffsetZ;
    private static bool _poolWarned;

    private static WeakReference _diagnosticWorld;
    private static float _nextDiagnostic;
    private static long _animatorCullFrames, _shadowHides, _shadowShows, _poolRestores;
    private static int _animatorCulledNow, _shadowsHiddenNow, _animatorCulledPeak, _shadowsHiddenPeak;

    internal static void EnablePatch()
    {
        if (!_visualEnabled)
        {
            try { new VisualPassPatch().Enable(); _visualEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
        if (!_poolEnabled)
        {
            try { new PoolReturnPatch().Enable(); _poolEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
    }

    /// <summary>Raid end: put every own write back and forget every body.</summary>
    internal static void Reset()
    {
        try { RestoreAll(); }
        catch (Exception ex) { Warn(ex); }
        _frame = -1;
        _raidOk = false;
        _warned = false;
        _diagnosticWorld = null;
    }

    private static bool AnimatorsOn => Settings.ZombieCullOffscreenAnimators != null && Settings.ZombieCullOffscreenAnimators.Value;
    private static bool ShadowsOn => Settings.ZombieHideOffscreenShadows != null && Settings.ZombieHideOffscreenShadows.Value;

    internal static void AfterVisualPass(Player body)
    {
        try
        {
            if (body == null)
                return;
            bool animators = AnimatorsOn, shadows = ShadowsOn;
            if (shadows && !_poolEnabled)
            {
                // Without the pool-return restore there is no safe way to hide a
                // pooled model's shadow: fail open to native shadows.
                shadows = false;
                if (!_poolWarned)
                {
                    _poolWarned = true;
                    Plugin.ModLogger.LogWarning("Arena zombie body culling: HideOffscreenShadows is on but the pool-return restore patch is not installed; shadows stay native.");
                }
            }
            if (!animators && !shadows)
            {
                if (Tracked.Count > 0) RestoreAll();
                return;
            }
            NextFrame();
            if (!_raidOk)
            {
                if (Tracked.Count > 0) RestoreAll();
                return;
            }

            var state = States.GetValue(body, NewState);
            state.LastFrame = _frame;
            bool alive = body.HealthController != null && body.HealthController.IsAlive;
            if (!state.EligibilityKnown && !TryLearnEligibility(body, state))
                return;
            if (!state.Eligible || !alive)
            {
                if (state.Tracked) Restore(state);
                return;
            }

            Vector3 position = body.Position;
            float cameraDistance = _hasCamera ? Vector3.Distance(position, _cameraPosition) : float.NaN;

            if (animators) UpdateAnimator(body, state, position, cameraDistance);
            else if (state.AnimatorCulled) ReleaseAnimator(body, state, cameraDistance);

            if (shadows && _hasCamera) UpdateShadows(body, state, position);
            else if (state.ShadowsHidden) ShowShadows(state);

            if (!state.AnimatorCulled && !state.ShadowsHidden && state.Tracked) Untrack(state);
            Diagnose();
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    private static bool TryLearnEligibility(Player body, State state)
    {
        // Role, skeleton and follower status are fixed for a body's life, but the
        // BotOwner arrives a few frames after the body: decide only once it is there.
        var owner = body.AIData?.BotOwner;
        if (owner == null)
            return false;
        var settings = body.Profile?.Info?.Settings;
        bool generic = settings != null && GenericInfected(settings.Role);
        bool follower = owner.IsFollower() || owner.BotFollower?.HaveBoss == true;
        state.Eligible = ZombieBodyCullingPolicy.IsEligible(true, body.IsAI, body.UsedSimplifiedSkeleton, generic,
            true, follower, body.IsYourPlayer);
        float melee = owner.Settings?.FileSettings?.Shoot?.DIST_TO_HIT_MELEE ?? ZombieBodyCullingPolicy.DefaultMeleeHitDistance;
        state.NearRadius = ZombieBodyCullingPolicy.NearRadius(melee);
        state.EligibilityKnown = true;
        return true;
    }

    // Same four roles as ZombiePerformancePatches: infectedTagilla's boss behavior
    // is outside the wave contract and keeps every native setting.
    private static bool GenericInfected(WildSpawnType role)
        => role == WildSpawnType.infectedAssault || role == WildSpawnType.infectedCivil
            || role == WildSpawnType.infectedLaborant || role == WildSpawnType.infectedPmc;

    private static void UpdateAnimator(Player body, State state, Vector3 position, float cameraDistance)
    {
        var owner = body.AIData?.BotOwner;
        float target = float.PositiveInfinity;
        var enemy = owner?.Memory?.GoalEnemy;
        if (enemy != null) target = enemy.Distance;
        if (_mainValid) target = Mathf.Min(target, Vector3.Distance(position, _mainPosition));
        bool attacking = body.HandsController is Player.KnifeController knife
            && knife.CurrentOperation is Player.KnifeController.Kick;
        bool cull = ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, cameraDistance, _cullDistance,
            target, state.NearRadius, attacking);
        if (cull)
        {
            var animator = body.BodyAnimatorCommon;
            if (animator != null && animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (!state.AnimatorCulled)
            {
                state.AnimatorCulled = true;
                Track(state);
            }
            _animatorCullFrames++;
            _animatorCulledNow++;
        }
        else if (state.AnimatorCulled)
        {
            ReleaseAnimator(body, state, cameraDistance);
        }
    }

    /// <summary>
    /// The native VisualPass already wrote AlwaysAnimate this frame for a body
    /// inside the radius; writing it again covers the one path where it did
    /// not (its early return) and is otherwise the same value.
    /// </summary>
    private static void ReleaseAnimator(Player body, State state, float cameraDistance)
    {
        state.AnimatorCulled = false;
        if (!(cameraDistance <= _cullDistance))
            return;
        var animator = body.BodyAnimatorCommon;
        if (animator != null && animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private static void UpdateShadows(Player body, State state, Vector3 position)
    {
        float now = Time.time;
        Vector3 centre = position;
        centre.y += ZombieBodyCullingPolicy.ViewBoxCentreHeight;
        bool inView = ZombieBodyCullingPolicy.InsideFrustumWithShadow(Planes, centre.x, centre.y, centre.z,
            ZombieBodyCullingPolicy.ViewBoxHalfX, ZombieBodyCullingPolicy.ViewBoxHalfY, ZombieBodyCullingPolicy.ViewBoxHalfZ,
            _shadowOffsetX, _shadowOffsetZ);
        bool opticCovers = ZombieBodyCullingPolicy.OpticCovers(_optic, Vector3.Dot(_cameraForward, position - _cameraPosition));
        if (inView || opticCovers) state.OutsideSince = float.NaN;
        else if (float.IsNaN(state.OutsideSince)) state.OutsideSince = now;
        float outside = float.IsNaN(state.OutsideSince) ? 0f : now - state.OutsideSince;
        bool hide = ZombieBodyCullingPolicy.ShouldHideShadows(true, true, inView, opticCovers, outside);
        if (hide)
        {
            if (!state.ShadowsHidden || now >= state.NextRefresh)
                HideShadows(body, state, now);
            _shadowsHiddenNow++;
        }
        else if (state.ShadowsHidden)
        {
            ShowShadows(state);
        }
    }

    private static void HideShadows(Player body, State state, float now)
    {
        var playerBody = body.PlayerBody;
        if (playerBody == null)
            return;
        Scratch.Clear();
        playerBody.GetRenderersNonAlloc(Scratch);
        // The item in the hands (SlotView leaves it out): its renderer sits under the
        // body animator's hierarchy and would keep the animator visible on its own.
        var hands = body.HandsController?.ControllerGameObject;
        if (hands != null)
        {
            HandsScratch.Clear();
            hands.GetComponentsInChildren(true, HandsScratch);
            Scratch.AddRange(HandsScratch);
            HandsScratch.Clear();
        }
        int changed = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var renderer = Scratch[i];
            if (renderer == null || state.Changed.Contains(renderer))
                continue;
            var mode = renderer.shadowCastingMode;
            // Already off (the hands skin, by the game's own choice): nothing to
            // change and nothing to put back.
            if (mode == ShadowCastingMode.Off)
                continue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            state.Renderers.Add(renderer);
            state.Original.Add(mode);
            state.Changed.Add(renderer);
            changed++;
            for (var pool = renderer.GetComponentInParent<AssetPoolObject>(); pool != null; pool = Above(pool))
            {
                if (ByPool.TryGetValue(pool, out var holder) && ReferenceEquals(holder, state))
                    continue;
                ByPool[pool] = state;
                state.Pools.Add(pool);
            }
        }
        Scratch.Clear();
        state.NextRefresh = now + ZombieBodyCullingPolicy.RendererRefreshSeconds;
        if (!state.ShadowsHidden)
        {
            state.ShadowsHidden = true;
            _shadowHides++;
            Track(state);
        }
        else if (changed > 0)
        {
            _shadowHides++;
        }
    }

    private static AssetPoolObject Above(AssetPoolObject pool)
    {
        var parent = pool.transform.parent;
        return parent != null ? parent.GetComponentInParent<AssetPoolObject>() : null;
    }

    /// <summary>Own writes back, in place; a renderer somebody else changed since is left as they set it.</summary>
    private static void ShowShadows(State state)
    {
        for (int i = 0; i < state.Renderers.Count; i++)
        {
            var renderer = state.Renderers[i];
            if (renderer != null && renderer.shadowCastingMode == ShadowCastingMode.Off)
                renderer.shadowCastingMode = state.Original[i];
        }
        foreach (var pool in state.Pools)
        {
            if (pool != null && ByPool.TryGetValue(pool, out var holder) && ReferenceEquals(holder, state))
                ByPool.Remove(pool);
        }
        state.Renderers.Clear();
        state.Original.Clear();
        state.Changed.Clear();
        state.Pools.Clear();
        state.OutsideSince = float.NaN;
        if (state.ShadowsHidden)
        {
            state.ShadowsHidden = false;
            _shadowShows++;
        }
    }

    internal static void BeforePoolReturn(AssetPoolObject pool)
    {
        try
        {
            if (pool == null || ByPool.Count == 0 || !ByPool.TryGetValue(pool, out var state))
                return;
            // The whole body's shadows come back, not only this model's: the next
            // refresh re-reads the remaining renderers if the body is still out of view.
            ShowShadows(state);
            _poolRestores++;
            if (!state.AnimatorCulled && state.Tracked) Untrack(state);
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    private static void Restore(State state)
    {
        if (state.ShadowsHidden || state.Renderers.Count > 0) ShowShadows(state);
        // A dead body's animators are disabled by Player.OnDead; a live body that
        // merely left eligibility gets the native value back from its own next
        // VisualPass. Nothing to write here.
        state.AnimatorCulled = false;
        if (state.Tracked) Untrack(state);
    }

    private static void RestoreAll()
    {
        for (int i = Tracked.Count - 1; i >= 0; i--)
            Restore(Tracked[i]);
        Tracked.Clear();
        ByPool.Clear();
        _animatorCulledNow = _shadowsHiddenNow = 0;
    }

    private static void Track(State state)
    {
        if (state.Tracked) return;
        state.Tracked = true;
        Tracked.Add(state);
    }

    private static void Untrack(State state)
    {
        state.Tracked = false;
        Tracked.Remove(state);
    }

    private static void NextFrame()
    {
        if (_frame == Time.frameCount)
            return;
        _frame = Time.frameCount;
        _animatorCulledPeak = Math.Max(_animatorCulledPeak, _animatorCulledNow);
        _shadowsHiddenPeak = Math.Max(_shadowsHiddenPeak, _shadowsHiddenNow);
        _animatorCulledNow = _shadowsHiddenNow = 0;

        _raidOk = false;
        _hasCamera = false;
        _mainValid = false;
        if (!ZombieNativeEventBridge.IsActiveZombieRaid()
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
            return;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        if (world == null || game == null || game.Status != GameStatus.Started
            || !ArenaSession.IsExactActiveRaid(game, world))
            return;
        _raidOk = true;

        // Bodies whose VisualPass stopped running (dead, replaced, removed) get
        // their own writes back. LastFrame is set per body AFTER this runs, so a
        // body processed later in this same frame still carries the previous one.
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            var state = Tracked[i];
            var body = state.Body;
            if (body == null || state.LastFrame < _frame - 1
                || body.HealthController == null || !body.HealthController.IsAlive)
                Restore(state);
        }

        var main = world.MainPlayer;
        if (main != null && main.HealthController != null && main.HealthController.IsAlive)
        {
            _mainPosition = main.Position;
            _mainValid = true;
        }

        var hard = EFTHardSettings.Instance;
        _cullDistance = hard != null ? hard.AnimatorCullDistance : 10f;

        var manager = CameraManager.Instance;
        var camera = manager?.Camera;
        if (camera == null)
            return;
        _hasCamera = true;
        var cameraTransform = camera.transform;
        _cameraPosition = cameraTransform.position;
        _cameraForward = cameraTransform.forward;
        GeometryUtility.CalculateFrustumPlanes(camera, PlaneScratch);
        for (int p = 0; p < 6; p++)
        {
            var normal = PlaneScratch[p].normal;
            Planes[p * 4] = normal.x;
            Planes[p * 4 + 1] = normal.y;
            Planes[p * 4 + 2] = normal.z;
            Planes[p * 4 + 3] = PlaneScratch[p].distance;
        }
        var optic = manager.OpticCameraManager;
        _optic = optic != null && optic.IsAnyOpticCameraRendering;

        // The sky's directional light (sun by day, moon by night) decides how far
        // an unseen body's shadow can reach into the picture. No sky: no reach,
        // the plain padded frustum test remains.
        _shadowOffsetX = _shadowOffsetZ = 0f;
        if (TOD_Sky.Instantiated)
        {
            var light = TOD_Sky.Instance?.Components?.LightTransform;
            if (light != null)
            {
                var forward = light.forward;
                ZombieBodyCullingPolicy.ShadowReachOffset(forward.x, forward.y, forward.z, out _shadowOffsetX, out _shadowOffsetZ);
            }
        }
    }

    private static void Diagnose()
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (world == null)
            return;
        if (!ReferenceEquals(_diagnosticWorld?.Target, world))
        {
            _diagnosticWorld = new WeakReference(world);
            _nextDiagnostic = Time.realtimeSinceStartup + 30f;
            _animatorCullFrames = _shadowHides = _shadowShows = _poolRestores = 0;
            _animatorCulledPeak = _shadowsHiddenPeak = 0;
            Plugin.ModLogger.LogDebug("Arena zombie body culling: offscreen animator culling "
                + (AnimatorsOn ? "on" : "off") + " (inside " + _cullDistance.ToString("0.#")
                + " m, never within the near radius or mid-swing), offscreen shadows "
                + (ShadowsOn ? "on" : "off")
                + "; generic infected only, own writes restored on view/death/pool return/raid end; 30 s aggregate counters.");
            return;
        }
        if (Time.realtimeSinceStartup < _nextDiagnostic)
            return;
        _nextDiagnostic = Time.realtimeSinceStartup + 30f;
        Plugin.ModLogger.LogDebug($"Arena zombie body culling (last interval): animatorCulledBodyFrames={_animatorCullFrames}, animatorCulledPeak={_animatorCulledPeak}, shadowHides={_shadowHides}, shadowShows={_shadowShows}, shadowsHiddenPeak={_shadowsHiddenPeak}, poolRestores={_poolRestores}, trackedNow={Tracked.Count}; counts are avoided work, not frame-time savings.");
        _animatorCullFrames = _shadowHides = _shadowShows = _poolRestores = 0;
        _animatorCulledPeak = _shadowsHiddenPeak = 0;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena zombie body culling: optimization unavailable for this call; native behavior retained. " + ex.GetBaseException().Message);
    }

    private sealed class VisualPassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.VisualPass), Type.EmptyTypes);

        [PatchPostfix]
        private static void Postfix(Player __instance) => AfterVisualPass(__instance);
    }

    private sealed class PoolReturnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(AssetPoolObject), nameof(AssetPoolObject.ReturnToPool), Type.EmptyTypes);

        [PatchPrefix]
        private static void Prefix(AssetPoolObject __instance) => BeforePoolReturn(__instance);
    }
}
