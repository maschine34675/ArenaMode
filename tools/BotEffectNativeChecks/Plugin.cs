using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.HealthSystem;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace BotEffectNativeChecks;

/// <summary>
/// Explicitly installed GPU2-only fixture. No Harmony, persistent settings, native
/// transforms, health values, inventory or AI decisions are changed. Commands are
/// fresh single-use files, never automatically selected targets or automatic buffs.
/// </summary>
[BepInPlugin("com.maschine.BotEffectNativeChecks", "Bot effect native checks (temporary)", "0.1.0")]
[BepInDependency("com.maschine.ArenaMode")]
[BepInDependency("com.maschine.UiBridge")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Root = @"C:\Temp\ArenaBotEffectNativeChecks";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float MaximumSeconds = 15f;
    private Type _session, _powerups, _runtime;
    private GameWorld _world;
    private LocalGame _game;
    private Player _local, _target;
    private ActiveHealthController _health;
    private Camera _camera;
    private GameObject _cover;
    private Material _coverMaterial;
    private string _coverKind, _localId, _lastId;
    private float _poll, _until;
    private int _commands;
    private bool _available;

    private sealed class Request
    {
        public string Id { get; set; }
        public string Action { get; set; }
        public string ProfileId { get; set; }
        public string BotId { get; set; }
        public string Effect { get; set; }
        public string Cover { get; set; }
        public DateTime CreatedUtc { get; set; }
        public float Seconds { get; set; } = 10f;
    }

    private void Awake()
    {
        // Local admin copies this one DLL only after candidate review. It remains
        // inert on the VM/other GPU host even if Syncthing carries that copy there.
        if (!string.Equals(Environment.MachineName, "SPT-GPU-2", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "maschine-ArenaMode.Client");
            _session = assembly.GetType("ArenaMode.Session.ArenaSession", true);
            _powerups = assembly.GetType("ArenaMode.Pickups.ArenaPowerups", true);
            _runtime = assembly.GetType("ArenaMode.Pickups.ArenaPickupRuntime", true);
            Directory.CreateDirectory(Root);
            // Never execute a request left by a previous EFT process.
            string request = Path.Combine(Root, "request.json");
            if (File.Exists(request)) File.Move(request, Path.Combine(Root, "stale-" + Guid.NewGuid().ToString("N") + ".json"));
            Camera.onPreCull += BeforeCamera;
            _available = true;
            Logger.LogInfo("Temporary bot-effect fixture ready on GPU2; no action until a fresh explicit request. Remove its DLL after testing.");
        }
        catch (Exception ex) { Logger.LogWarning("Bot-effect fixture unavailable: " + ex.GetBaseException().Message); }
    }

    private void Update()
    {
        if (!_available) return;
        if (_cover != null && (Time.realtimeSinceStartup >= _until || !CurrentLease())) ClearCover();
        if (Time.realtimeSinceStartup < _poll) return;
        _poll = Time.realtimeSinceStartup + .2f;
        string path = Path.Combine(Root, "request.json");
        if (!File.Exists(path)) return;
        Request request = null;
        string consumed = Path.Combine(Root, "consumed.json");
        try
        {
            // Consume before parsing, including malformed/oversize inputs; never
            // replay a failed action or emit an unbounded warning every poll.
            if (File.Exists(consumed)) File.Delete(consumed);
            File.Move(path, consumed);
            if (new FileInfo(consumed).Length > 4096) throw new InvalidOperationException("request exceeds 4096 bytes");
            request = JsonConvert.DeserializeObject<Request>(File.ReadAllText(consumed));
            File.Delete(consumed);
            ValidateRequest(request);
            object result = Execute(request);
            Write(request.Id, true, result, null);
        }
        catch (Exception ex)
        {
            // Don't retry malformed/failed commands; a read lock can retry the file.
            if (request != null)
                Write(request.Id, false, null, ex.GetBaseException().Message);
            else Logger.LogWarning("Bot-effect fixture command rejected: " + ex.GetBaseException().Message);
        }
        finally { if (File.Exists(consumed)) File.Delete(consumed); }
    }

    private void ValidateRequest(Request r)
    {
        if (r == null || !Guid.TryParseExact(r.Id, "N", out _) || r.Id == _lastId)
            throw new InvalidOperationException("fresh 32-character request GUID required");
        double age = (DateTime.UtcNow - r.CreatedUtc.ToUniversalTime()).TotalSeconds;
        if (age < -2 || age > 20) throw new InvalidOperationException("request must be no more than 20 seconds old");
        _lastId = r.Id;
        if (!Chainloader.PluginInfos.TryGetValue("com.maschine.UiBridge", out var bridge)
            || !ReadSwitch(bridge.Instance, "AllowActions") || !ReadSwitch(bridge.Instance, "AllowTestHooks"))
            throw new InvalidOperationException("UiBridge actions/test hooks must be explicitly enabled");
        if (r.Action == "clear") { ClearCover(); return; }
        if (!Admit(r.ProfileId, out var game, out var world, out var local))
            throw new InvalidOperationException("requires GPU2 test-profile, exact started solo-human Arena DM/TDM raid");
        if (!ReferenceEquals(world, _world) || !ReferenceEquals(game, _game))
        {
            ClearCover(); _commands = 0;
            _world = world; _game = game;
        }
        if (++_commands > 80) throw new InvalidOperationException("80-command per-raid fixture budget exhausted");
        _local = local; _localId = r.ProfileId;
        if (!(r.Seconds > 0 && r.Seconds <= MaximumSeconds) || float.IsNaN(r.Seconds))
            throw new InvalidOperationException("duration must be in (0,15] seconds");
    }

    private static bool ReadSwitch(BaseUnityPlugin plugin, string key)
    {
        var definition = new BepInEx.Configuration.ConfigDefinition("Actions", key);
        return plugin.Config.TryGetEntry<bool>(definition, out var entry) && entry.Value;
    }

    private bool Admit(string profileId, out LocalGame game, out GameWorld world, out Player local)
    {
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        local = world == null ? null : world.MainPlayer;
        if (game == null || game.Status != GameStatus.Started || world == null || local == null
            || !local.IsYourPlayer || local.IsAI || local.HealthController?.IsAlive != true
            || !string.Equals(local.Profile?.Nickname, "SPT-GPU-2", StringComparison.Ordinal)
            || !string.Equals(local.ProfileId, profileId, StringComparison.Ordinal)
            || world.RegisteredPlayers.Count(p => p != null && !p.IsAI) != 1
            || !(bool)_session.GetProperty("IsActive", Static).GetValue(null)) return false;
        string mode = (string)_session.GetProperty("ActiveMode", Static).GetValue(null);
        if (mode != "teamdeathmatch" && mode != "deathmatch" && mode != "rounds" && mode != "endless") return false;
        return (bool)_session.GetMethod("IsExactActiveRaid", Static).Invoke(null, new object[] { game, world });
    }

    private bool TargetValid(Player target, ActiveHealthController health)
    {
        var owner = target == null ? null : target.AIData?.BotOwner;
        return target != null && !target.IsYourPlayer && target.IsAI && owner != null
            && owner.BotState == EBotState.Active && ReferenceEquals(owner.GetPlayer, target)
            && health != null && health.IsAlive && ReferenceEquals(target.ActiveHealthController, health)
            && _world.RegisteredPlayers.Any(p => ReferenceEquals(p, target))
            && ReferenceEquals(_world.GetAlivePlayerByProfileID(target.ProfileId), target)
            && (bool)_runtime.GetMethod("IsLiveActor", Static).Invoke(null, new object[] { target });
    }

    private bool CurrentLease()
    {
        return Admit(_localId, out var game, out var world, out var local)
            && ReferenceEquals(game, _game) && ReferenceEquals(world, _world) && ReferenceEquals(local, _local)
            && TargetValid(_target, _health) && CameraManager.Exist
            && ReferenceEquals(CameraManager.Instance.Camera, _camera);
    }

    private object Execute(Request r)
    {
        if (r.Action == "clear") return new { Cleared = true };
        if (r.Action == "status") return Snapshot(null);
        var target = _world.GetAlivePlayerByProfileID(r.BotId);
        if (!TargetValid(target, target?.ActiveHealthController))
            throw new InvalidOperationException("target must be the explicitly named, active, exact live pickup-eligible bot");
        if (r.Action == "observe") return Snapshot(target);
        if (r.Action == "buff")
        {
            if (r.Effect != "shield" && r.Effect != "damage" && r.Effect != "both")
                throw new InvalidOperationException("effect must be shield, damage, or both");
            object state = Read(target);
            // Do not replace/extend any naturally collected state, including boots/ammo.
            if (Float(state, "DamageSeconds") > 0 || Float(state, "ShieldSeconds") > 0
                || Float(state, "JumpSeconds") > 0 || Float(state, "AmmoSeconds") > 0)
                throw new InvalidOperationException("existing buff left untouched; await its expiry or select another bot");
            bool shield = r.Effect == "damage" || (bool)_powerups.GetMethod("GrantShield", Static)
                .Invoke(null, new object[] { target, 220f, r.Seconds });
            bool damage = r.Effect == "shield" || (bool)_powerups.GetMethod("GrantDamage", Static)
                .Invoke(null, new object[] { target, r.Seconds });
            if (!shield || !damage) throw new InvalidOperationException("production grant refused (any accepted short effect expires naturally); no fake visual created");
            return new { SyntheticGrant = true, Seconds = r.Seconds, Effect = r.Effect, State = Snapshot(target) };
        }
        if (r.Action == "cover")
        {
            if (r.Cover == "none") { ClearCover(); return Snapshot(target); }
            if (r.Cover != "full" && r.Cover != "lower-half")
                throw new InvalidOperationException("cover must be full, lower-half, or none");
            ClearCover();
            _target = target; _health = target.ActiveHealthController;
            _camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
            if (_camera == null) throw new InvalidOperationException("native current camera unavailable");
            var shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Standard opaque shader unavailable; no unreliable cover fallback");
            _coverKind = r.Cover;
            _until = Time.realtimeSinceStartup + r.Seconds;
            _coverMaterial = new Material(shader) { name = "ArenaEffectTestOpaqueMaterial", color = new Color(.28f, .28f, .28f, 1f), renderQueue = 2000 };
            _coverMaterial.SetFloat("_Mode", 0); _coverMaterial.SetFloat("_ZWrite", 1);
            _coverMaterial.SetFloat("_Metallic", 0); _coverMaterial.SetFloat("_Glossiness", 0);
            _cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cover.name = "ArenaEffectTestOpaqueCover";
            _cover.transform.SetParent(_world.transform, false);
            var collider = _cover.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            var renderer = _cover.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _coverMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            try { PlaceCover(_camera); }
            catch { ClearCover(); throw; }
            return new { SyntheticCover = true, Kind = r.Cover, Seconds = r.Seconds, State = Snapshot(target) };
        }
        throw new InvalidOperationException("unknown action (status, observe, buff, cover, clear)");
    }

    private void BeforeCamera(Camera camera)
    {
        if (_cover == null || !ReferenceEquals(camera, _camera)) return;
        try
        {
            if (Time.realtimeSinceStartup >= _until || !CurrentLease()) { ClearCover(); return; }
            PlaceCover(camera);
        }
        catch (Exception ex) { ClearCover(); Logger.LogWarning("Temporary cover removed: " + ex.GetBaseException().Message); }
    }

    private void PlaceCover(Camera camera)
    {
        var bounds = BodyBounds(_target);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, nearest = float.MaxValue;
        foreach (var corner in Corners(bounds))
        {
            var p = camera.WorldToViewportPoint(corner);
            if (p.z <= camera.nearClipPlane + .5f) throw new InvalidOperationException("bot intersects/behind camera; no valid test cover");
            minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
            minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y); nearest = Math.Min(nearest, p.z);
        }
        if (minX < .03f || maxX > .97f || minY < .03f || maxY > .97f)
            throw new InvalidOperationException("entire target must be inside viewport with margin");
        float middle = (minY + maxY) * .5f;
        float marginX = (maxX - minX) * .15f + .005f, marginY = (maxY - minY) * .15f + .005f;
        minX -= marginX; maxX += marginX; minY -= marginY;
        maxY = _coverKind == "lower-half" ? middle : maxY + marginY;
        float depth = Math.Max(camera.nearClipPlane + .2f, nearest * .55f);
        if (depth >= nearest - .2f) throw new InvalidOperationException("no depth interval for safe occluder");
        var lower = camera.ViewportToWorldPoint(new Vector3(minX, minY, depth));
        var upper = camera.ViewportToWorldPoint(new Vector3(maxX, maxY, depth));
        _cover.transform.SetPositionAndRotation((lower + upper) * .5f, camera.transform.rotation);
        var extent = camera.transform.InverseTransformVector(upper - lower);
        _cover.transform.localScale = new Vector3(Math.Abs(extent.x), Math.Abs(extent.y), .02f);
        // Choose the first visible layer already admitted by the actual camera.
        for (int layer = 0; layer < 32; layer++)
            if ((camera.cullingMask & (1 << layer)) != 0) { _cover.layer = layer; break; }
    }

    private object Snapshot(Player target)
    {
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        var views = _world.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("ArenaBot", StringComparison.Ordinal) || t.name.StartsWith("ArenaSpawn", StringComparison.Ordinal))
            .Select(t => new { t.name, Instance = t.GetInstanceID(), Active = t.gameObject.activeInHierarchy,
                Components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToArray() }).Take(100).ToArray();
        var rings = _world.GetComponentsInChildren<LineRenderer>(true)
            .Where(r => r.name.StartsWith("ArenaBot", StringComparison.Ordinal) || r.name.StartsWith("ArenaSpawn", StringComparison.Ordinal)).ToArray();
        return new
        {
            Frame = Time.frameCount, Realtime = Time.realtimeSinceStartup, Location = _world.LocationId,
            Mode = _session.GetProperty("ActiveMode", Static).GetValue(null), Profile = _local.ProfileId,
            Camera = camera == null ? null : new { camera.name, Id = camera.GetInstanceID(), Width = camera.pixelWidth,
                Height = camera.pixelHeight, ScreenWidth = Screen.width, ScreenHeight = Screen.height,
                ScaledWidth = camera.scaledPixelWidth, ScaledHeight = camera.scaledPixelHeight,
                RenderingPath = camera.actualRenderingPath.ToString(), TargetTexture = TextureState(camera.targetTexture),
                Rect = new[] { camera.rect.x, camera.rect.y, camera.rect.width, camera.rect.height },
                DepthMode = camera.depthTextureMode.ToString(), Msaa = camera.allowMSAA },
            DepthTexture = TextureState(Shader.GetGlobalTexture("_CameraDepthTexture")),
            DepthTexelSize = Vec4(Shader.GetGlobalVector("_CameraDepthTexture_TexelSize")),
            Match = MatchState(),
            Target = target == null ? null : new { target.ProfileId, Name = target.Profile?.Nickname,
                BodyId = target.GetInstanceID(), HealthId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target.ActiveHealthController),
                Position = Vec(target.Position), BoundsCorners = Corners(BodyBounds(target)).Select(Vec).ToArray(),
                ViewportCorners = camera == null ? null : Corners(BodyBounds(target)).Select(p => Vec(camera.WorldToViewportPoint(p))).ToArray(),
                Buffs = State(Read(target)), SpawnProtected = SpawnProtected(target),
                Renderers = BodyRenderers(target).Select(RendererState).ToArray() },
            Bots = _world.AllAlivePlayersList.Where(p => p != null && p.IsAI).Select(p => new { p.ProfileId,
                Name = p.Profile?.Nickname, BodyId = p.GetInstanceID(), Position = Vec(p.Position),
                Buffs = State(Read(p)), SpawnProtected = SpawnProtected(p) }).ToArray(),
            Cover = _cover == null ? null : new { Kind = _coverKind, Remaining = _until - Time.realtimeSinceStartup,
                CameraId = _camera.GetInstanceID(), Shader = _coverMaterial.shader.name, Queue = _coverMaterial.renderQueue,
                ZWrite = _coverMaterial.GetFloat("_ZWrite"), ColliderCount = _cover.GetComponents<Collider>().Length,
                Corners = Corners(_cover.GetComponent<Renderer>().bounds).Select(Vec).ToArray(),
                ViewportCorners = Corners(_cover.GetComponent<Renderer>().bounds).Select(p => Vec(_camera.WorldToViewportPoint(p))).ToArray() },
            EffectObjects = views, LegacyNamedRingCount = rings.Length, ContourRenderer = ContourState(),
            Warning = "Readback is evidence metadata, not proof of pixel occlusion. Injected buffs and collider-free cover are test fixtures, not natural pickup/gameplay acceptance."
        };
    }

    private bool? SpawnProtected(Player player)
    {
        object mode = CurrentMode();
        var method = mode?.GetType().GetMethod("IsBotSpawnProtected", Instance);
        return method == null ? (bool?)null : (bool)method.Invoke(mode, new object[] { player });
    }

    private object CurrentMode()
    {
        var service = _session.Assembly.GetType("ArenaMode.Modes.ArenaMatchService", true);
        object current = service.GetProperty("Current", Static)?.GetValue(null);
        return current?.GetType().GetProperty("Mode", Instance)?.GetValue(current);
    }
    private object MatchState()
    {
        object mode = CurrentMode();
        object rules = Field(mode, "_rules");
        return mode == null ? null : new { Type = mode.GetType().FullName,
            FrozenSpawnShieldSeconds = Field(mode, "_spawnShieldSeconds"),
            TrackedSpawnShields = (Field(mode, "_spawnShields") as ICollection)?.Count,
            Phase = rules?.GetType().GetProperty("Phase", Instance)?.GetValue(rules)?.ToString() };
    }

    private object ContourState()
    {
        // Optional introspection, never a prerequisite for a production effect.
        // No artificial lease/source renderer is added to make a check pass.
        var type = _session.Assembly.GetType("ArenaMode.Effects.ArenaBotEffectRenderer");
        var renderer = type?.GetField("_current", Static)?.GetValue(null);
        if (renderer == null || renderer is UnityEngine.Object u && u == null) return null;
        var camera = Field(renderer, "_camera") as Camera;
        var buffer = Field(renderer, "_commands") as CommandBuffer;
        var bodies = Field(renderer, "_bodies") as IEnumerable;
        var rows = new List<object>();
        if (bodies != null) foreach (var draw in bodies)
        {
            object entry = Field(draw, "Entry");
            var body = Field(entry, "Body") as Player;
            var source = Field(draw, "Renderers") as IEnumerable;
            var sourceRows = new List<object>();
            if (source != null) foreach (var sourceRenderer in source)
                if (sourceRenderer is Renderer native && native != null) sourceRows.Add(RendererState(native));
            object flags = entry?.GetType().GetMethod("Read", Instance)?.Invoke(entry, new object[] { Time.frameCount });
            rows.Add(new { ProfileId = body?.ProfileId, BodyId = body == null ? (int?)null : body.GetInstanceID(),
                Flags = flags?.ToString(), Sources = sourceRows });
        }
        return new { Type = type.FullName, Failed = Field(renderer, "_failed"), Disposed = Field(renderer, "_disposed"),
            CameraId = camera == null ? (int?)null : camera.GetInstanceID(), BufferName = buffer?.name,
            CommandBytes = buffer?.sizeInBytes, Mask = MaterialState(Field(renderer, "_mask") as Material),
            Edge = MaterialState(Field(renderer, "_edge") as Material), Bodies = rows };
    }
    private static object Field(object owner, string name) => owner?.GetType().GetField(name, Instance)?.GetValue(owner);
    private static object MaterialState(Material material) => material == null ? null : new
    {
        material.name, Id = material.GetInstanceID(), Shader = material.shader?.name,
        Queue = material.renderQueue, Supported = material.shader?.isSupported
    };
    private static object TextureState(Texture texture) => texture == null ? null : new
    {
        texture.name, Width = texture.width, Height = texture.height,
        Id = texture.GetInstanceID(), Type = texture.GetType().FullName
    };

    private static object RendererState(Renderer renderer) => new
    {
        renderer.name, Id = renderer.GetInstanceID(), renderer.enabled, renderer.isVisible,
        Active = renderer.gameObject.activeInHierarchy, Layer = renderer.gameObject.layer,
        Kind = renderer.GetType().Name, Bounds = Corners(renderer.bounds).Select(Vec).ToArray(),
        Materials = renderer.sharedMaterials.Select(m => m == null ? null : new { m.name, Id = m.GetInstanceID(),
            Shader = m.shader == null ? null : m.shader.name, Queue = m.renderQueue }).ToArray()
    };

    private static Renderer[] BodyRenderers(Player player) => player.PlayerBody == null ? Array.Empty<Renderer>()
        : player.PlayerBody.GetComponentsInChildren<Renderer>(true)
            .Where(r => r != null && (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled && r.gameObject.activeInHierarchy).ToArray();

    private static Bounds BodyBounds(Player player)
    {
        var renderers = BodyRenderers(player);
        if (renderers.Length == 0) throw new InvalidOperationException("target has no enabled native body renderers");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        if (bounds.size.magnitude > 8f || bounds.size.magnitude < .2f)
            throw new InvalidOperationException("native body bounds not suitable for a controlled fixture");
        return bounds;
    }

    private static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++) yield return b.center + Vector3.Scale(b.extents,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
    }
    private object Read(Player player) => _powerups.GetMethod("Read", Static).Invoke(null, new object[] { player });
    private static float Float(object state, string field) => (float)state.GetType().GetField(field, Instance).GetValue(state);
    private static object State(object state) => new { DamageSeconds = Float(state, "DamageSeconds"),
        ShieldSeconds = Float(state, "ShieldSeconds"), ShieldPoints = Float(state, "ShieldPoints"),
        JumpSeconds = Float(state, "JumpSeconds"), AmmoSeconds = Float(state, "AmmoSeconds") };
    private static float[] Vec(Vector3 value) => new[] { value.x, value.y, value.z };
    private static float[] Vec4(Vector4 value) => new[] { value.x, value.y, value.z, value.w };

    private void ClearCover()
    {
        if (_cover != null) { _cover.SetActive(false); Destroy(_cover); }
        if (_coverMaterial != null) Destroy(_coverMaterial);
        _cover = null; _coverMaterial = null; _target = null; _health = null; _camera = null; _coverKind = null;
    }
    private void Write(string id, bool ok, object result, string error)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return;
        string destination = Path.Combine(Root, id + ".json");
        File.WriteAllText(destination, JsonConvert.SerializeObject(new { Id = id, Ok = ok, Utc = DateTime.UtcNow,
            Frame = Time.frameCount, Result = result, Error = error }, Formatting.Indented));
        Logger.LogInfo("Temporary bot-effect command " + id + ": " + (ok ? "completed" : "rejected: " + error));
    }
    private void OnDestroy() { Camera.onPreCull -= BeforeCamera; ClearCover(); }
}
