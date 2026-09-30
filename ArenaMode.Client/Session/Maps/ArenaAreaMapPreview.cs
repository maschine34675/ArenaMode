using System;
using System.Collections.Generic;
using System.IO;
using ArenaMode.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session.Maps;

/// <summary>
/// Optional owned presentation only. Map/area writes still belong exclusively
/// to the native selection adapter. No raid data, draft files or HTTP is read.
/// A failed preview restores vanilla information without taking the picker down.
/// </summary>
internal sealed class ArenaAreaMapPreview
{
    internal const string ResourceName = "ArenaMode.MapPreviews.customs-ground.png";
    private Transform _screen;
    private GameObject _nativeInfo, _root;
    private TMP_Text _typography;
    private TextMeshProUGUI _title, _caption, _credit;
    private Button _detailButton, _overviewButton;
    private ArenaAreaMapGraphic _map;
    private ArenaAreaBoundaryGraphic _boundary;
    private RectTransform _viewport;
    private Texture2D _texture;
    private ArenaAreaDefinition _area;
    private readonly List<ArenaAreaBounds> _outlines = new();
    private CanvasGroup _infoGroup;
    private bool _addedInfoGroup, _infoInteractable, _infoBlocksRaycasts, _infoHidden;
    private float _infoAlpha;
    private bool _overview, _disabled;
    private float _width = -1f, _height = -1f;

    internal GameObject Root => _root;

    internal void Show(Transform screen, GameObject nativeInfo, TMP_Text typography)
    {
        Close();
        _screen = screen; _nativeInfo = nativeInfo; _typography = typography;
        _disabled = false;
    }

    internal void Refresh(ArenaAreaSelectionState state)
    {
        if (_disabled || _screen == null || _nativeInfo == null) return;
        if (state?.Available != true || state.SelectedArea == null
            || !string.Equals(state.MapId, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))
        { Hide(); return; }
        try
        {
            if (_root == null) Create();
            if (!ReferenceEquals(_area, state.SelectedArea))
            {
                _area = state.SelectedArea;
                _overview = false;
                _outlines.Clear();
                foreach (var option in state.Options)
                {
                    if (!ArenaAreaCatalog.TryResolve(option.Id, state.MapId, state.ModeId, false, out var other, out _)
                        || other == null) continue;
                    bool duplicate = false;
                    foreach (var existing in _outlines)
                        if (ReferenceEquals(existing.Outline, other.Bounds.Outline)) { duplicate = true; break; }
                    if (!duplicate) _outlines.Add(other.Bounds);
                    if (option.Id == state.SelectedId) _title.text = "CUSTOMS — " + option.Label;
                }
                Draw();
            }
            Layout();
            _root.SetActive(true);
            HideNativeInfo();
        }
        catch (Exception ex)
        {
            Close();
            _disabled = true;
            Plugin.ModLogger.LogWarning($"Arena area map preview unavailable; native map information restored: {ex.Message}");
        }
    }

    internal void Close()
    {
        // Restore native visibility before destroying any owned object. The
        // info panel's active state always stays under vanilla's control.
        RestoreNativeInfo();
        // An added CanvasGroup has no native owner. Remove only our own empty
        // component immediately: deferred Destroy + same-frame Close/Show
        // would reuse a doomed group and reveal vanilla beneath the new map.
        if (_addedInfoGroup && _infoGroup != null) UnityEngine.Object.DestroyImmediate(_infoGroup);
        _infoGroup = null; _addedInfoGroup = false; _infoHidden = false;
        var root = _root; var texture = _texture;
        _root = null; _texture = null; _area = null;
        if (_detailButton != null) _detailButton.onClick.RemoveAllListeners();
        if (_overviewButton != null) _overviewButton.onClick.RemoveAllListeners();
        _detailButton = _overviewButton = null;
        _screen = null; _nativeInfo = null; _typography = null;
        _map = null; _boundary = null; _viewport = null;
        _title = _caption = _credit = null; _outlines.Clear();
        _width = _height = -1f;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
        if (texture != null) UnityEngine.Object.Destroy(texture);
    }

    private void Hide()
    {
        RestoreNativeInfo();
        if (_root != null) _root.SetActive(false);
        _area = null;
    }

    private void Create()
    {
        // The PNG and its licence/provenance are embedded together. Every
        // screen owns its texture, so repeated open/close cannot leak a cache.
        using (var stream = typeof(ArenaAreaMapPreview).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded Customs ground map is missing."))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!_texture.LoadImage(buffer.ToArray(), true))
                throw new InvalidOperationException("Embedded Customs ground map could not be decoded.");
            _texture.name = "Arena Customs ground map";
            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }
        _root = new GameObject("Arena Area Map Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.SetActive(false);
        _root.transform.SetParent(_screen, false);
        _root.transform.SetAsLastSibling();
        var background = _root.GetComponent<Image>();
        background.color = Hud.ArenaUiTheme.WithAlpha(Hud.ArenaUiTheme.Surface, .96f); background.raycastTarget = false;
        Hud.ArenaUiTheme.Decorate((RectTransform)_root.transform);
        _title = Text("Area map title", "CUSTOMS", 21f);
        _caption = Text("Area map caption", "", 14f);
        _credit = Text("Area map attribution", "Map: Shebuka / Tarkov.dev, modified by acidphantasm\nArena adaptation: CC BY-NC-SA 4.0 · Ground floor", 11f);
        _credit.enableWordWrapping = true;
        var viewport = new GameObject("Area map viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(_root.transform, false);
        _viewport = (RectTransform)viewport.transform;
        _map = Layer<ArenaAreaMapGraphic>("Customs ground map", _viewport);
        _boundary = Layer<ArenaAreaBoundaryGraphic>("Arena boundaries", _viewport);
        var owner = _root;
        _detailButton = Button("AREA", () => SetOverview(owner, false));
        _overviewButton = Button("OVERVIEW", () => SetOverview(owner, true));
    }

    private void SetOverview(GameObject owner, bool overview)
    {
        if (!ReferenceEquals(owner, _root) || owner == null || !owner.activeInHierarchy || _area == null) return;
        _overview = overview;
        Draw();
    }

    private void Draw()
    {
        _map.Set(_texture, _area.Bounds, _overview);
        _boundary.Set(_area.Bounds, _outlines, _overview);
        _caption.text = _overview ? "Selected area in blue · Other available areas in grey" : "Blue boundary: playable area · Outside area dimmed";
        _detailButton.interactable = _overview;
        _overviewButton.interactable = !_overview;
    }

    private void Layout()
    {
        float width = ArenaMapPanelLayout.Width(_screen);
        float height = ArenaMapPanelLayout.PreviewHeight;
        ArenaMapPanelLayout.Place((RectTransform)_root.transform, _screen, ArenaMapPanelLayout.InfoTop, height);
        if (_width == width && _height == height) return;
        _width = width; _height = height;
        Place(_title.rectTransform, 20f, 12f, width - 40f, 28f);
        Place((RectTransform)_detailButton.transform, 20f, 44f, 104f, 26f);
        Place((RectTransform)_overviewButton.transform, 132f, 44f, 140f, 26f);
        Place(_viewport, 16f, 80f, width - 32f, height - 144f);
        Place(_caption.rectTransform, 20f, height - 58f, width - 40f, 20f);
        _caption.fontSize = 12f;
        Place(_credit.rectTransform, 20f, height - 36f, width - 40f, 30f);
    }

    private void HideNativeInfo()
    {
        if (_nativeInfo == null || _infoHidden) return;
        if (_infoGroup == null)
        {
            _infoGroup = _nativeInfo.GetComponent<CanvasGroup>();
            _addedInfoGroup = _infoGroup == null;
            if (_addedInfoGroup) _infoGroup = _nativeInfo.AddComponent<CanvasGroup>();
        }
        _infoAlpha = _infoGroup.alpha; _infoInteractable = _infoGroup.interactable; _infoBlocksRaycasts = _infoGroup.blocksRaycasts;
        _infoGroup.alpha = 0f; _infoGroup.interactable = false; _infoGroup.blocksRaycasts = false;
        _infoHidden = true;
    }

    private void RestoreNativeInfo()
    {
        if (_infoGroup == null || !_infoHidden) return;
        _infoGroup.alpha = _infoAlpha; _infoGroup.interactable = _infoInteractable; _infoGroup.blocksRaycasts = _infoBlocksRaycasts;
        _infoHidden = false;
    }

    private TextMeshProUGUI Text(string name, string value, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(_root.transform, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = _typography?.font ?? TMP_Settings.defaultFontAsset;
        if (_typography != null) text.fontSharedMaterial = _typography.fontSharedMaterial;
        text.color = size >= 20f ? Hud.ArenaUiTheme.Text : Hud.ArenaUiTheme.Muted;
        text.fontSize = size; text.text = value; text.raycastTarget = false;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private Button Button(string label, UnityEngine.Events.UnityAction callback)
    {
        var go = new GameObject("Area map " + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(_root.transform, false);
        var image = go.GetComponent<Image>(); image.color = new Color(.22f, .23f, .23f, 1f);
        var button = go.GetComponent<Button>(); button.targetGraphic = image;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        button.onClick.AddListener(callback);
        var text = Text(label + " label", label, 16f);
        text.transform.SetParent(go.transform, false);
        text.alignment = TextAlignmentOptions.Midline;
        Hud.ArenaUiTheme.StyleButton(button, text);
        Stretch(text.rectTransform);
        return button;
    }

    private static T Layer<T>(string name, Transform parent) where T : MaskableGraphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.transform.SetParent(parent, false);
        var graphic = go.GetComponent<T>(); graphic.raycastTarget = false;
        Stretch(graphic.rectTransform);
        return graphic;
    }

    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
