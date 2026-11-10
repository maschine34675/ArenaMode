using System;
using System.Collections.Generic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ArenaMode.Session;

internal static class ArenaBootSplash
{
    private static ArenaBootSplashView _view;
    private static bool _installed;

    internal static void Install(Harmony harmony)
    {
        if (_installed || !ArenaSessionMode.GateActive) return;
        var awake = AccessTools.DeclaredMethod(typeof(SplashScreenPanel), nameof(SplashScreenPanel.Awake));
        if (awake == null) throw new MissingMethodException(nameof(SplashScreenPanel), nameof(SplashScreenPanel.Awake));
        harmony.Patch(awake, postfix: new HarmonyMethod(typeof(ArenaBootSplash), nameof(AfterAwake)));
        _installed = true;
        // Scene Awake can precede Plugin.Awake. Use only a panel EFT has already opened.
        Attach(Object.FindObjectOfType<SplashScreenPanel>());
    }

    private static void AfterAwake(SplashScreenPanel __instance) => Attach(__instance);

    private static void Attach(SplashScreenPanel panel)
    {
        if (!_installed || !ArenaSessionMode.GateActive || panel == null || !panel.isActiveAndEnabled ||
            panel._imageCanvasGroup == null || panel._images == null || panel._images.Length == 0) return;
        if (_view != null && _view.gameObject == panel.gameObject) return;
        try
        {
            if (_view != null) _view.Release();
            _view = panel.gameObject.AddComponent<ArenaBootSplashView>();
            _view.AttachImage(panel);
        }
        catch (Exception ex)
        {
            if (_view != null) _view.Release();
            Warn(ex);
        }
    }

    internal static void Shutdown()
    {
        _installed = false;
        if (_view != null) _view.Release();
    }

    internal static void Released(ArenaBootSplashView view)
    {
        if (!ReferenceEquals(view, _view)) return;
        _view = null;
    }

    internal static void Warn(Exception ex) => Plugin.ModLogger?.LogWarning("Arena boot artwork unavailable: " + ex.Message);
}

internal sealed class ArenaBootSplashView : MonoBehaviour
{
    private readonly List<(CanvasRenderer Renderer, float Alpha)> _hidden = new();
    private GameObject _art;
    private RawImage _image;
    private ArenaMenuArt.SplashLease _lease;
    private Image[] _nativeImages;
    private Vector2 _size;
    private float _aspect;
    private bool _released;

    internal void AttachImage(SplashScreenPanel panel)
    {
        _lease = ArenaMenuArt.AcquireSplash();
        if (_lease == null) { Release(); return; }
        var texture = _lease.Texture;
        // EFT fits its sharp sprite to 16:9 and stretches four other sprites into the margins.
        // Their common group spans the viewport and owns the fade. The native footer follows
        // that group as a sibling, so this one surface covers ultrawide without covering text.
        var group = panel._imageCanvasGroup;
        _art = new GameObject("Arena Boot Artwork", typeof(RectTransform), typeof(RawImage));
        _art.layer = group.gameObject.layer;
        _art.transform.SetParent(group.transform, false);
        _image = _art.GetComponent<RawImage>();
        _image.enabled = false;
        _image.raycastTarget = false;
        _image.texture = texture;
        var rect = _image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _aspect = (float)texture.width / texture.height;
        _nativeImages = panel._images;
        Refresh();
    }

    private void LateUpdate()
    {
        try { Refresh(); }
        catch (Exception ex) { Release(); ArenaBootSplash.Warn(ex); }
    }

    private void Refresh()
    {
        if (_released || _image == null) return;
        var size = _image.rectTransform.rect.size;
        if (size.x <= 0f || size.y <= 0f || size == _size) return;
        _size = size;
        _image.uvRect = ArenaMenuArt.CoverUv(size, _aspect);
        if (_image.enabled) return;
        // Prevent the stock picture showing through our picture during EFT's translucent fade.
        // Renderer alpha preserves the original Graphic, its sprite, hit testing and hierarchy.
        foreach (var native in _nativeImages)
        {
            if (native == null) continue;
            var renderer = native.canvasRenderer;
            float alpha = renderer.GetAlpha();
            if (alpha == 0f) continue;
            _hidden.Add((renderer, alpha));
            renderer.SetAlpha(0f);
        }
        _image.enabled = true;
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();

    internal void Release()
    {
        if (_released) return;
        _released = true;
        if (_art != null) { _art.SetActive(false); Destroy(_art); }
        foreach (var slot in _hidden)
            if (slot.Renderer != null && slot.Renderer.GetAlpha() == 0f) slot.Renderer.SetAlpha(slot.Alpha);
        _hidden.Clear();
        _nativeImages = null;
        _lease?.Dispose();
        _lease = null;
        ArenaBootSplash.Released(this);
        Destroy(this);
    }
}
