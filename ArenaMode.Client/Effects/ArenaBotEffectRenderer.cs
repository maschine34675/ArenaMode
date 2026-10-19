using System;
using System.Collections.Generic;
using System.IO;
using ArenaMode.Pickups;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.HealthSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Effects;

/// <summary>
/// One private render pass per Arena world. Draws existing visible geometry into
/// an occluded mask; the composite only colors pixels INSIDE that visible mask.
/// Native renderers, pooled meshes/materials, animators and camera settings stay unchanged.
/// </summary>
internal sealed class ArenaBotEffectRenderer : MonoBehaviour
{
    private const string Resource = "ArenaMode.BotEffects.bot-effect-outlines.bundle";
    private const CameraEvent RenderEvent = CameraEvent.BeforeImageEffectsOpaque;
    private static ArenaBotEffectRenderer _current;
    private static bool _warned;
    private static readonly int MaskId = Shader.PropertyToID("_ArenaBotEffectMask");
    private static readonly int FlagsId = Shader.PropertyToID("_ArenaEffectFlags");
    private static readonly int TagId = Shader.PropertyToID("_ArenaBodyTag");
    private readonly ArenaBotEffectLeases<Player> _leases = new();
    private readonly List<BodyDraw> _bodies = new();
    private readonly Plane[] _planes = new Plane[6];
    private GameWorld _world;
    private LocalGame _game;
    private ArenaMatch _match;
    private int _epoch;
    private Camera _camera;
    private CommandBuffer _commands;
    private AssetBundle _bundle;
    private Material _mask, _edge;
    private bool _failed, _disposed;

    private sealed class BodyDraw
    {
        internal ArenaBotEffectLeases<Player>.Entry Entry;
        internal ActiveHealthController Health;
        internal PlayerBody Model;
        internal readonly List<Renderer> Renderers = new();
        internal float NextRefresh;
    }

    internal static ArenaBotEffectLeases<Player>.Lease Acquire(GameWorld world, Player body)
    {
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        var match = ArenaMatchService.Current;
        if (!ArenaSession.IsActive || world == null || body == null || !body.IsAI
            || game == null || match == null || game.Status != GameStatus.Started
            || !ArenaSession.IsExactActiveRaid(game, world) || !ReferenceEquals(game.GameWorld, world)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world))
            throw new InvalidOperationException("bot effect requires the current Arena world/body");
        if (_current != null && (!ReferenceEquals(_current._world, world)
            || !ReferenceEquals(_current._game, game) || !ReferenceEquals(_current._match, match)
            || _current._epoch != ArenaSession.ArmEpoch)) _current.DisposeOwned();
        if (_current == null)
        {
            var root = new GameObject("ArenaBotEffectRenderer");
            root.transform.SetParent(world.transform, false);
            _current = root.AddComponent<ArenaBotEffectRenderer>();
            _current._world = world;
            _current._game = game; _current._match = match; _current._epoch = ArenaSession.ArmEpoch;
        }
        for (int i = _current._bodies.Count - 1; i >= 0; i--)
        {
            var old = _current._bodies[i];
            if (ReferenceEquals(old.Entry.Body, body) && !ReferenceEquals(old.Health, body.ActiveHealthController))
            { _current._leases.Retire(old.Entry); old.Renderers.Clear(); old.Model = null; old.Health = null; _current._bodies.RemoveAt(i); }
        }
        var lease = _current._leases.Add(body);
        foreach (var item in _current._bodies)
            if (ReferenceEquals(item.Entry, lease.Entry)) return lease;
        _current._bodies.Add(new BodyDraw { Entry = lease.Entry, Health = body.ActiveHealthController });
        return lease;
    }

    private void OnEnable() => Camera.onPreCull += BeforeCamera;
    private void OnDisable() { Camera.onPreCull -= BeforeCamera; Detach(); }
    private void OnDestroy() => DisposeOwned();

    private bool CurrentWorld() => ArenaSession.IsActive && _world != null
        && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
        && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
        && _game != null && _game.Status == GameStatus.Started && ReferenceEquals(_game.GameWorld, _world)
        && ArenaSession.ArmEpoch == _epoch && ArenaSession.IsExactActiveRaid(_game, _world)
        && ReferenceEquals(ArenaMatchService.Current, _match) && !ArenaMatchService.TeamStartAbortPending
        && (_match.Mode is TeamDeathmatchMode teams ? teams.AssistScoringOpen && !teams.IsComplete
            : _match.Mode is RoundsMode rounds && rounds.AssistScoringOpen && !rounds.IsComplete);

    private bool CurrentBody(BodyDraw draw)
    {
        var body = draw.Entry.Body;
        return body != null && body.IsAI && body.ActiveHealthController?.IsAlive == true
            && body.AIData?.BotOwner?.BotState == EBotState.Active
            && ReferenceEquals(body.ActiveHealthController, draw.Health)
            && !string.IsNullOrEmpty(body.ProfileId)
            && ReferenceEquals(_world.GetAlivePlayerByProfileID(body.ProfileId), body);
    }

    private void LateUpdate()
    {
        if (!CurrentWorld()) { DisposeOwned(); return; }
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            var draw = _bodies[i];
            if (!CurrentBody(draw) || draw.Entry.Leases.Count == 0)
            { _leases.Retire(draw.Entry); draw.Renderers.Clear(); draw.Health = null; draw.Model = null; _bodies.RemoveAt(i); }
        }
        if (_leases.Entries.Count == 0) DisposeOwned();
    }

    private void BeforeCamera(Camera camera)
    {
        // Clear a previously recorded frame BEFORE any early return. A disabled
        // body, lost depth buffer or expired lease may never leave a stale outline.
        if (ReferenceEquals(camera, _camera)) _commands?.Clear();
        if (_disposed || _failed || !CurrentWorld()) { Detach(); return; }
        try
        {
            var currentCamera = CameraManager.Instance?.Camera;
            if (_camera != null && !ReferenceEquals(_camera, currentCamera)) Detach();
            if (camera == null || !ReferenceEquals(camera, currentCamera) || !camera.isActiveAndEnabled
                || camera.cameraType != CameraType.Game) return;
            // Deferred EFT supplies depth. For another rendering path require
            // its existing depth request; never enable/disable shared camera flags.
            if (camera.actualRenderingPath != RenderingPath.DeferredShading
                && (camera.depthTextureMode & DepthTextureMode.Depth) == 0) return;
            if (!EnsureResources()) return;
            if (!ReferenceEquals(_camera, camera))
            { _camera = camera; camera.AddCommandBuffer(RenderEvent, _commands); }
            _commands.Clear();
            BuildFrame(camera);
        }
        catch (Exception ex)
        {
            _failed = true; ReleaseRenderResources();
            if (!_warned) { _warned = true; Plugin.ModLogger.LogWarning("Arena bot effect outlines unavailable: " + ex.GetBaseException().Message); }
        }
    }

    private bool EnsureResources()
    {
        if (_commands != null) return true;
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            throw new NotSupportedException("full-float effect mask unsupported");
        using (var input = typeof(ArenaBotEffectRenderer).Assembly.GetManifestResourceStream(Resource))
        {
            if (input == null) throw new FileNotFoundException("embedded bot effect shader bundle missing");
            var bytes = new byte[input.Length];
            int offset = 0, read;
            while (offset < bytes.Length && (read = input.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
            if (offset != bytes.Length) throw new EndOfStreamException("bot effect shader bundle truncated");
            _bundle = AssetBundle.LoadFromMemory(bytes);
        }
        if (_bundle == null) throw new InvalidOperationException("bot effect shader bundle could not load");
        var shader = _bundle.LoadAsset<Shader>("Assets/ArenaBotEffects.shader");
        if (shader == null || !shader.isSupported || shader.passCount != 2)
            throw new InvalidOperationException("bot effect shader unsupported");
        _mask = new Material(shader) { name = "ArenaBotEffectMask", hideFlags = HideFlags.HideAndDontSave };
        _edge = new Material(shader) { name = "ArenaBotEffectInnerContour", hideFlags = HideFlags.HideAndDontSave };
        _edge.SetColor("_ArenaSpawnColor", new Color(.65f, 1f, 1f, .9f));
        _edge.SetColor("_ArenaShieldColor", ArenaPickupColors.Shield);
        _edge.SetColor("_ArenaDamageColor", ArenaPickupColors.DoubleDamage);
        _commands = new CommandBuffer { name = "Arena visible bot effects" };
        return true;
    }

    private void BuildFrame(Camera camera)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, _planes);
        bool started = false;
        for (int i = 0; i < _bodies.Count; i++)
        {
            var draw = _bodies[i];
            var flags = draw.Entry.Read(Time.frameCount);
            if (flags == ArenaBotEffectKind.None || !CurrentBody(draw)) continue;
            var body = draw.Entry.Body;
            if (!body.gameObject.activeInHierarchy || body.PlayerBody == null
                || !body.PlayerBody.gameObject.activeInHierarchy) continue;
            if (!ReferenceEquals(draw.Model, body.PlayerBody) || Time.unscaledTime >= draw.NextRefresh)
            {
                draw.Renderers.Clear(); draw.Model = body.PlayerBody;
                draw.Model.GetRenderersNonAlloc(draw.Renderers);
                draw.NextRefresh = Time.unscaledTime + .5f;
            }
            foreach (var renderer in draw.Renderers)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.isVisible
                    || !renderer.gameObject.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly
                    || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                    || !renderer.transform.IsChildOf(body.gameObject.transform)
                    || !GeometryUtility.TestPlanesAABB(_planes, renderer.bounds)) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                    : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null || mesh.subMeshCount == 0) continue;
                if (!started)
                {
                    // -1 dimensions resolve at camera execution before upscaling,
                    // rather than copying the unrelated Screen.width/height.
                    _commands.GetTemporaryRT(MaskId, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat,
                        RenderTextureReadWrite.Linear);
                    _commands.SetRenderTarget(MaskId);
                    _commands.ClearRenderTarget(false, true, Color.clear);
                    started = true;
                }
                _commands.SetGlobalFloat(FlagsId, (int)flags);
                _commands.SetGlobalFloat(TagId, i + 1);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    _commands.DrawRenderer(renderer, _mask, submesh, 0);
            }
        }
        if (!started) return;
        _commands.Blit(MaskId, BuiltinRenderTextureType.CameraTarget, _edge, 1);
        _commands.ReleaseTemporaryRT(MaskId);
    }

    private void Detach()
    {
        _commands?.Clear();
        if (_camera != null && _commands != null) _camera.RemoveCommandBuffer(RenderEvent, _commands);
        _camera = null;
    }

    private void DisposeOwned()
    {
        if (_disposed) return;
        _disposed = true;
        Camera.onPreCull -= BeforeCamera;
        ReleaseRenderResources();
        _leases.Clear();
        foreach (var body in _bodies) { body.Renderers.Clear(); body.Model = null; body.Health = null; }
        _bodies.Clear(); _world = null; _game = null; _match = null;
        if (ReferenceEquals(_current, this)) _current = null;
        if (gameObject != null) Destroy(gameObject);
    }

    private void ReleaseRenderResources()
    {
        Detach(); _commands?.Dispose(); _commands = null;
        if (_mask != null) Destroy(_mask);
        if (_edge != null) Destroy(_edge);
        _mask = null; _edge = null;
        if (_bundle != null) _bundle.Unload(true);
        _bundle = null;
    }
}
