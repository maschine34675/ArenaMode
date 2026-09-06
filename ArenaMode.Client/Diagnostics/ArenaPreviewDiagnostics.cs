using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EFT.UI.WeaponModding;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Diagnostics;

/// <summary>
/// Observes stationary foreign gear in native Item Overlook. Frustum membership
/// is a candidate, not proof of a drawn pixel (occlusion/materials still apply).
/// Never hide, relayer, reparent, return or destroy any scene object here.
/// The menu/Hideout exception is read-only: the reported symptom can survive an
/// Arena raid. An unrelated game, including a not-yet-started LocalGame, is refused.
/// </summary>
internal static class ArenaPreviewDiagnostics
{
    private const float SnapshotInterval = 10f;
    private const int DetailLimit = 16;
    private static Harmony _harmony;
    private static FieldInfo _originalObject;
    private static FieldInfo _currentItem;
    private static float _nextSnapshotAt;
    private static int _request;
    private static bool _warned;

    internal static void Enable()
    {
        if (_harmony != null) return;
        var harmony = new Harmony(Plugin.PluginGuid + ".previewdiagnostics");
        try
        {
            // Installed SPT 4.1: this callback runs after bundles have loaded,
            // the original model is parented and the preview layer is assigned.
            _originalObject = AccessTools.Field(typeof(WeaponPreview), "_originalObject")
                ?? throw new MissingFieldException("WeaponPreview._originalObject");
            _currentItem = AccessTools.Field(typeof(WeaponPreview), "_currentItem")
                ?? throw new MissingFieldException("WeaponPreview._currentItem");
            var ready = AccessTools.Method(typeof(WeaponPreview.CG_SetupItemPreview), "method_1")
                ?? throw new MissingMethodException("WeaponPreview preview completion");
            harmony.Patch(ready, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ArenaPreviewDiagnostics), nameof(PreviewReadyPostfix)))
                { priority = Priority.Last });
            _harmony = harmony;
            _nextSnapshotAt = 0;
            _warned = false;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchSelf(); } catch { }
            Warn(ex);
        }
    }

    internal static void Shutdown()
    {
        ++_request;
        var harmony = _harmony;
        _harmony = null;
        try { harmony?.UnpatchSelf(); } catch (Exception ex) { Warn(ex); }
    }

    private static bool TryContext(out AbstractGame game, out GameWorld world, out string context)
    {
        game = null;
        world = null;
        context = null;
        if (_harmony == null || (!ArenaSession.IsActive && !ArenaSession.LastRaidWasArena)) return false;
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (ArenaSession.IsActive && game is LocalGame local && local.Status == GameStatus.Started
            && ArenaSession.IsExactActiveRaid(game, world))
        {
            context = "active-arena";
            return true;
        }
        if (!ArenaSession.IsActive && ArenaSession.LastRaidWasArena
            && (ReferenceEquals(game, null) || game is HideoutGame))
        {
            context = game is HideoutGame ? "hideout-after-arena" : "menu-after-arena";
            return true;
        }
        return false;
    }

    private static void PreviewReadyPostfix(WeaponPreview.CG_SetupItemPreview __instance)
    {
        try
        {
            if (!TryContext(out var game, out var world, out _) || Plugin.Instance == null
                || Time.realtimeSinceStartup < _nextSnapshotAt) return;
            var preview = __instance.weaponPreview_0;
            if (preview == null) return;
            var original = _originalObject.GetValue(preview) as GameObject;
            if (original == null) return;
            Plugin.Instance.StartCoroutine(InspectNextFrame(preview, original, __instance.item,
                game, world, ++_request));
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static IEnumerator InspectNextFrame(WeaponPreview preview, GameObject original, Item item,
        AbstractGame game, GameWorld world, int request)
    {
        // Let other completion postfixes and native UI/camera state settle. Never
        // carry a snapshot over closure, item replacement, raid end or world swap.
        yield return null;
        try
        {
            if (request != _request || Time.realtimeSinceStartup < _nextSnapshotAt
                || !TryContext(out var currentGame, out var currentWorld, out var context)
                || !ReferenceEquals(game, currentGame) || !ReferenceEquals(world, currentWorld)
                || preview == null || original == null || !preview.isActiveAndEnabled
                || !ReferenceEquals(_originalObject.GetValue(preview), original)
                || !ReferenceEquals(_currentItem.GetValue(preview), item)
                || preview.WeaponPreviewCamera == null || !preview.WeaponPreviewCamera.isActiveAndEnabled)
                yield break;
            _nextSnapshotAt = Time.realtimeSinceStartup + SnapshotInterval;
            Snapshot(preview, original, item, world, context);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Snapshot(WeaponPreview preview, GameObject original, Item item,
        GameWorld world, string context)
    {
        var camera = preview.WeaponPreviewCamera;
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var candidates = new List<Renderer>();
        var externalSkins = new List<Renderer>();
        int own = 0, foreign = 0, externalSkin = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();
        // One scene enumeration per settled preview, at most once per ten seconds.
        // Nothing runs in Update or a camera's render callbacks.
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
            bool belongs = renderer.transform.IsChildOf(original.transform);
            if (belongs && renderer is SkinnedMeshRenderer skin && skin.rootBone != null
                && !skin.rootBone.IsChildOf(original.transform))
            {
                ++externalSkin;
                if (externalSkins.Count < 8) externalSkins.Add(renderer);
            }
            if (!Candidate(renderer, camera, planes)) continue;
            if (belongs) ++own;
            else { ++foreign; candidates.Add(renderer); }
        }
        candidates.Sort((left, right) =>
            (left.bounds.center - camera.transform.position).sqrMagnitude.CompareTo(
                (right.bounds.center - camera.transform.position).sqrMagnitude));
        Log($"context={context}; item={item?.TemplateId}; original={Path(original.transform)}; "
            + $"ownCandidates={own}; foreignCandidates={foreign}; externalSkinRoots={externalSkin}; "
            + $"scanMs={started.ElapsedMilliseconds}; details={Math.Min(candidates.Count, DetailLimit)}/{foreign}");
        Log("previewCamera " + CameraInfo(camera));
        for (int i = 0; i < Math.Min(candidates.Count, DetailLimit); ++i)
            Log("foreign " + RendererInfo(candidates[i]));
        foreach (var renderer in externalSkins) Log("selectedExternalSkin " + RendererInfo(renderer));

        // The item view is composited over a menu/raid/Hideout camera. Listing
        // those cameras and headwear also exposes the alternative: visible own
        // first-person gear, with no foreign renderer in the preview's mask.
        var cameras = Camera.allCameras;
        for (int i = 0; i < Math.Min(cameras.Length, 12); ++i)
            if (cameras[i] != camera) Log("otherCamera " + CameraInfo(cameras[i]));
        var player = world != null ? world.MainPlayer : null;
        if (player?.PlayerBody == null) return;
        Log($"mainPlayer={player.GetInstanceID()}; body={player.PlayerBody.GetInstanceID()}; "
            + $"pov={player.PointOfView}; alive={player.HealthController?.IsAlive}");
        foreach (var slot in new[] { EquipmentSlot.Headwear, EquipmentSlot.FaceCover })
        {
            if (!player.PlayerBody.SlotViews.TryGetByKey(slot, out var view)) continue;
            if (view?.Renderers == null) continue;
            int count = 0;
            foreach (var renderer in view.Renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy || ++count > 8) continue;
                Log($"mainGear slot={slot}; cameras={CameraCandidates(renderer, cameras)}; " + RendererInfo(renderer));
            }
        }
    }

    private static bool Candidate(Renderer renderer, Camera camera, Plane[] planes)
        => renderer.enabled && !renderer.forceRenderingOff
           && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly
           && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0
           && GeometryUtility.TestPlanesAABB(planes, renderer.bounds);

    private static string RendererInfo(Renderer renderer)
    {
        var pool = renderer.GetComponentInParent<AssetPoolObject>();
        var owner = renderer.GetComponentInParent<Player>();
        var skin = renderer as SkinnedMeshRenderer;
        return $"path={Path(renderer.transform)}; root={Path(renderer.transform.root)}; "
            + $"scene={renderer.gameObject.scene.name}; type={renderer.GetType().Name}; "
            + $"layer={renderer.gameObject.layer}/{LayerMask.LayerToName(renderer.gameObject.layer)}; "
            + $"enabled={renderer.enabled}; forcedOff={renderer.forceRenderingOff}; shadows={renderer.shadowCastingMode}; "
            + $"position={renderer.transform.position:F2}; bounds={renderer.bounds}; "
            + $"pool={(pool == null ? "none" : pool.GetInstanceID() + "/" + pool.Name + "/inPool=" + pool.IsInPool + "/stub=" + pool.IsStub)}; "
            + $"player={(owner == null ? "none" : owner.GetInstanceID().ToString())}; "
            + $"skinRoot={Path(skin != null ? skin.rootBone : null)}";
    }

    private static string CameraInfo(Camera camera)
        => $"path={Path(camera.transform)}; mask=0x{camera.cullingMask:X8}; "
           + $"position={camera.transform.position:F2}; rotation={camera.transform.eulerAngles:F1}; "
           + $"near={camera.nearClipPlane}; far={camera.farClipPlane}; depth={camera.depth}; "
           + $"clear={camera.clearFlags}; target={(camera.targetTexture == null ? "screen" : camera.targetTexture.GetInstanceID().ToString())}";

    private static string CameraCandidates(Renderer renderer, Camera[] cameras)
    {
        var ids = new List<string>();
        // The same camera cap as the metadata rows above. A match is still only
        // mask/frustum eligibility, not proof that the object survives occlusion.
        for (int i = 0; i < Math.Min(cameras.Length, 12); ++i)
            if (Candidate(renderer, cameras[i], GeometryUtility.CalculateFrustumPlanes(cameras[i])))
                ids.Add(cameras[i].transform.GetInstanceID().ToString());
        return ids.Count == 0 ? "none" : string.Join(",", ids);
    }

    private static string Path(Transform transform)
    {
        if (transform == null) return "none";
        var parts = new List<string>();
        for (int i = 0; transform != null && i < 8; ++i, transform = transform.parent)
        {
            string name = transform.name.Replace('\r', ' ').Replace('\n', ' ');
            parts.Add((name.Length > 80 ? name.Substring(0, 80) : name) + "#" + transform.GetInstanceID());
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void Log(string message) => Plugin.ModLogger.LogInfo("Arena preview probe: " + message);

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena preview probe unavailable (native rendering unchanged): "
            + ex.GetBaseException().Message);
    }
}
