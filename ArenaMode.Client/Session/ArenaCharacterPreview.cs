using Comfort.Common;
using Diz.Resources;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using JsonType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using ArenaMode.Session.Preview;

namespace ArenaMode.Session;

/// <summary>Owned menu model, detached equipment, native posing and a separate camera.</summary>
internal sealed class ArenaCharacterPreview : IDisposable
{
    private readonly PlayerModelLoader _loader = new();
    private GameObject _world;
    private GameObject _view;
    private Camera _camera;
    private CameraImage _image;
    private ProgressSpinner _spinner;
    private Material _filterMaterial;
    private Action _releaseBundles;
    private readonly ArenaPreviewLifetimeState _lifetime = new();
    private Action _onUnavailable;
    private bool _ready;
    private ArenaPreviewLifetime _viewLifetime;
    private EFT.Utilities.XCoordRotation _rotator;

    public bool IsAvailable => !_lifetime.IsDisposed && _view != null && _world != null;
    public MenuPlayerPoser Poser => _ready && IsAvailable ? _loader.ModelPlayerPoser : null;

    public ArenaCharacterPreview(MatchMakerSideSelectionScreen screen, Profile profile, Action onUnavailable)
    {
        _onUnavailable = onUnavailable;
        try
        {
            var sourceImage = screen._pmcModelView.GetComponentInParent<CameraImage>();
            var sourceCamera = sourceImage?.targetCamera;
            if (profile?.Customization == null || sourceCamera == null
                || sourceCamera.cullingMask != (1 << LayersMaskController.WeaponPreview))
                throw new InvalidOperationException("Native character preview contract is unavailable.");
            // Only the native loader's side is needed. No profile/health references.
            var visual = new PlayerVisualRepresentation(new PlayerInfo { Side = profile.Info.Side },
                new BodyCustomization(profile.Customization), ArenaPreviewEquipment.Build(profile.Info.Side));
            CreateView(screen, sourceImage);
            CreateCamera(sourceCamera, screen._pmcModelView.transform);
            if (!_lifetime.TryBeginLoad()) throw new InvalidOperationException("Preview load already started.");
            _ = Load(visual);
        }
        catch { Dispose(); throw; }
    }

    private void CreateView(MatchMakerSideSelectionScreen screen, CameraImage source)
    {
        _view = new GameObject("ArenaCharacterPreview", typeof(RectTransform));
        _view.SetActive(false);
        var rect = (RectTransform)_view.transform;
        var nativeRect = (RectTransform)source.transform;
        rect.SetParent(screen.transform, false);
        rect.SetSiblingIndex(screen._randomButton.transform.parent.GetSiblingIndex());
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = nativeRect.rect.size;
        var center = screen.transform.InverseTransformPoint(nativeRect.TransformPoint(nativeRect.rect.center));
        rect.localPosition = new Vector3(0f, center.y, center.z);
        var raw = _view.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.enabled = false; // A texture-less RawImage would cover the menu white while loading.
        raw.color = source.GetComponent<RawImage>().color;
        _image = _view.AddComponent<CameraImage>();
        _image.TextureDepth = source.TextureDepth;
        _image.TextureFormat = source.TextureFormat;
        _image.RenderTextureReadWrite = source.RenderTextureReadWrite;
        _image.Antialiasing = source.Antialiasing;
        _spinner = Object.Instantiate(screen._pmcModelView._progressSpinner, rect, false);
        _spinner.Show();
        _viewLifetime = _view.AddComponent<ArenaPreviewLifetime>();
        _viewLifetime.OnHidden = Dispose;
        _rotator = _view.AddComponent<EFT.Utilities.XCoordRotation>();
        _view.SetActive(true);
    }

    private void CreateCamera(Camera source, Transform nativeModelRoot)
    {
        _world = new GameObject("ArenaCharacterPreviewWorld");
        _world.SetActive(false);
        // Native matchmaker cameras have far=25. Separate bounded space also
        // keeps this body out of item previews using the same native layer.
        // MenuPlayerPoser solves hands/weapon grips in world coordinates. At
        // 10000 each float step is nearly 1 mm, enough to quantize those solves.
        // Keep the isolated camera volume outside native 25 m frusta but near
        // origin. Do not change animator speed or disable native hand posing.
        _world.transform.position = new Vector3(128f, -128f, 128f);
        Object.DontDestroyOnLoad(_world);
        var cameraObject = new GameObject("ArenaCharacterCamera");
        cameraObject.transform.SetParent(_world.transform, false);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.CopyFrom(source);
        cameraObject.transform.localPosition = source.transform.localPosition;
        cameraObject.transform.localRotation = source.transform.localRotation;
        _camera.ResetWorldToCameraMatrix();
        _camera.enabled = false;
        _camera.targetTexture = null;
        _camera.farClipPlane = 25f;

        // Reuse shader assets with owned effect instances/materials. Never clone
        // an active camera, command buffers or global post-processing owners.
        var nativeUndither = source.GetComponent<Undithering>();
        if (nativeUndither != null && nativeUndither.enabled)
        {
            var undither = cameraObject.AddComponent<Undithering>();
            undither.shader = nativeUndither.shader;
            undither.useTriangleBlit = nativeUndither.useTriangleBlit;
            undither._distortion = nativeUndither._distortion;
        }
        var nativeFilter = source.GetComponent<PreviewFilter>();
        if (nativeFilter?.material != null && nativeFilter.enabled)
        {
            _filterMaterial = new Material(nativeFilter.material);
            cameraObject.AddComponent<PreviewFilter>().material = _filterMaterial;
        }

        // The native switcher enables even directional fill only for this camera.
        var nativeLights = source.GetComponent<CameraLightSwitcher>()?.Lights;
        if (nativeLights == null || nativeLights.Count == 0)
            throw new InvalidOperationException("Native character lighting is unavailable.");
        var switcher = cameraObject.AddComponent<CameraLightSwitcher>();
        switcher.Lights = new List<Light>();
        foreach (var light in nativeLights)
        {
            if (light == null) throw new InvalidOperationException("Native character light is missing.");
            var copy = Object.Instantiate(light, _world.transform, false);
            copy.name = "Arena " + light.name;
            copy.transform.localPosition = nativeModelRoot.InverseTransformPoint(light.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(nativeModelRoot.rotation) * light.transform.rotation;
            copy.enabled = false;
            switcher.Lights.Add(copy);
        }
        _world.SetActive(true);
    }

    private async Task Load(PlayerVisualRepresentation visual)
    {
        try
        {
            var solver = Singleton<CustomizationSolver>.Instance;
            var customization = ThirdPersonCustomizationFilter.Default.FilterCustomization(visual.Customization);
            // Retain before awaiting. Native Load/LoadBundles can lose the token
            // on cancellation or failure. This owner releases after loader.Clear.
            var keys = new[] { "assets/content/commonprefabs/menuplayer.bundle" }
                .Concat(customization.Values.Select(solver.GetBundle).Where(x => x != null).Select(x => x.path))
                .Concat(customization.Select(x => solver.GetWatchBundle(x.Value).WatchPrefab?.path))
                .Concat(visual.Equipment.GetAllVisibleItems().Select(x => x.Prefab.path))
                .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            var token = Singleton<IEasyAssets>.Instance.Retain(keys, null);
            _releaseBundles = token.Release;
            await EasyAssetsExtensions.LoadBundles(token);
            if (_lifetime.IsDisposed) return;
            var weapon = PlayerModelLoader.GetItemInHands(visual, null);
            await _loader.CreatePlayerBody(visual, customization, weapon, _world.transform,
                new Vector3(0f, -1f, 5f), LayersMaskController.WeaponPreview, null, true);
            if (_lifetime.IsDisposed) return;
            _loader.ModelPlayerPoser.gameObject.SetActive(true);
            _loader.CreateWeapon(.3f, true, weapon, LayersMaskController.WeaponPreview);
            _rotator.Init(_loader.ModelPlayerPoser.transform);
            _spinner.Close();
            _ready = true;
            if (_view.activeInHierarchy) _image.InitCamera(_camera);
            var renderers = _loader.ModelPlayerPoser.GetComponentsInChildren<Renderer>();
            var planes = GeometryUtility.CalculateFrustumPlanes(_camera);
            Plugin.ModLogger.LogInfo($"Arena character preview ready: side={visual.Info.Side}; separate equipment/camera; "
                + $"camera={_camera.transform.position}; body={_loader.ModelPlayerPoser.transform.position}; "
                + $"visibleBounds={renderers.Count(r => GeometryUtility.TestPlanesAABB(planes, r.bounds))}/{renderers.Length}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena character preview unavailable: {ex}");
            Dispose();
            Safe(() => _onUnavailable?.Invoke());
        }
        finally
        {
            if (_lifetime.FinishLoadAndClaimRelease()) Release();
        }
    }

    /// <summary>Transfer from the character-row layout before its parent is hidden.</summary>
    public void HoldForFlow(Action onUnavailable)
    {
        _onUnavailable = onUnavailable;
        if (_viewLifetime != null) _viewLifetime.OnHidden = null;
        Suspend();
        if (_view != null) Object.DontDestroyOnLoad(_view);
    }

    public void Suspend()
    {
        if (!IsAvailable) return;
        if (_image != null) _image.InitCamera(null);
        if (_view != null)
        {
            _view.SetActive(false);
            _view.transform.SetParent(null, false);
        }
    }

    /// <summary>The only moving object is the UI image. The world, camera and body keep stable transforms.</summary>
    public void ShowAt(Transform parent, RectTransform nativeImage = null)
    {
        if (!IsAvailable || parent == null) throw new InvalidOperationException("Arena preview host is unavailable.");
        Suspend();
        var rect = (RectTransform)_view.transform;
        if (nativeImage != null)
        {
            rect.SetParent(nativeImage, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        else
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(180f, 280f);
            rect.anchoredPosition = new Vector2(-24f, 100f);
        }
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        var anchored = rect.anchoredPosition3D;
        rect.anchoredPosition3D = new Vector3(anchored.x, anchored.y, 0f);
        rect.SetAsLastSibling();
        _view.SetActive(true);
        if (_ready) _image.InitCamera(_camera);
    }

    public void Rotate(float delta, float speed)
    {
        if (!_ready || !IsAvailable) return;
        _rotator._rotationSpeed = speed;
        _rotator.Rotate(delta);
    }

    public void SetFlowGuard(Func<bool> isCurrent, Action onInvalid, Action onTick)
    {
        var guard = _world.AddComponent<ArenaPreviewFlowGuard>();
        guard.IsCurrent = isCurrent;
        guard.OnInvalid = onInvalid;
        guard.OnTick = onTick;
    }

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        bool releaseNow = _lifetime.DisposeAndClaimRelease();
        Safe(() => { if (_image != null) _image.InitCamera(null); });
        Safe(() => { if (_camera != null) { _camera.enabled = false; _camera.targetTexture = null; } });
        Safe(() => { if (_view != null) _view.SetActive(false); });
        Safe(() => { if (_world != null) _world.SetActive(false); });
        Safe(() => { if (_loader.ModelPlayerPoser != null) _loader.ModelPlayerPoser.gameObject.SetActive(false); });
        // Never tear down PlayerBody during awaited Init. Its continuation owns
        // cleanup even when another screen instance has already begun loading.
        if (releaseNow) Release();
    }

    private void Release()
    {
        var ownedPoser = _loader.ModelPlayerPoser;
        Safe(_loader.Clear);
        // Only this loader's retained model, if native teardown threw partway.
        Safe(() => { if (ownedPoser != null) Object.Destroy(ownedPoser.gameObject); });
        var release = _releaseBundles;
        _releaseBundles = null;
        Safe(() => release?.Invoke());
        Safe(() => { if (_spinner != null) _spinner.Close(); });
        Safe(() => { if (_view != null) Object.Destroy(_view); });
        Safe(() => { if (_world != null) Object.Destroy(_world); });
        Safe(() => { if (_filterMaterial != null) Object.Destroy(_filterMaterial); });
        _view = null;
        _world = null;
        _filterMaterial = null;
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena character preview cleanup: {ex.Message}"); }
    }
}

/// <summary>Also covers a parent hidden without the normal screen Close patch.</summary>
internal sealed class ArenaPreviewLifetime : MonoBehaviour
{
    public Action OnHidden;
    private void OnDisable() => OnHidden?.Invoke();
    private void OnDestroy() => OnHidden?.Invoke();
}
