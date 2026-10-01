using System;
using ArenaMode.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>A screen-local presentation lease. Never changes location metadata or toggle events.</summary>
internal sealed class ArenaPortedMapPresentation
{
    private RectTransform _rect;
    private TMP_Text _header, _size;
    private TextMeshProUGUI _label;
    private Func<bool> _selected;
    private Vector2 _anchorMin, _anchorMax, _position, _target;
    private string _headerText, _sizeText, _name;
    private bool _headerEnabled;
    private CanvasGroup _headerGroup;
    private bool _addedGroup, _groupInteractable, _groupBlocksRaycasts;
    private float _groupAlpha;
    internal static readonly Color Gold = new(.95f, .76f, .35f, 1f);
    private static readonly Color SelectedGold = new(.38f, .25f, .06f, 1f);

    internal static string DisplayName(string id, string fallback)
        => ArenaMapCatalog.IsBay5(id) ? "BAY 5" : ArenaMapCatalog.IsEquator(id) ? "EQUATOR" : fallback;

    internal static ArenaPortedMapPresentation Create(string id, RectTransform rect,
        TMP_Text header, TMP_Text size, Func<bool> selected)
    {
        if (!ArenaMapCatalog.IsOptionalMap(id) || rect == null || header == null) return null;
        var view = new ArenaPortedMapPresentation
        {
            _rect = rect, _header = header, _size = size, _selected = selected,
            _anchorMin = rect.anchorMin, _anchorMax = rect.anchorMax, _position = rect.anchoredPosition,
            _headerText = header.text, _sizeText = size?.text, _headerEnabled = header.enabled,
            _name = DisplayName(id, header.text),
            // Same normalized map coordinates that native LocationButton.Show uses.
            // Bay 5: northern docks; Equator: immediately south-east of Interchange.
            _target = ArenaMapCatalog.IsBay5(id) ? new Vector2(.47f, .82f) : new Vector2(.70f, .29f)
        };
        try
        {
            var go = new GameObject("Arena ported map label " + id, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(header.transform.parent, false);
            view._label = go.GetComponent<TextMeshProUGUI>();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            view._label.font = header.font;
            view._label.fontSharedMaterial = header.fontSharedMaterial;
            view._label.alignment = header.alignment;
            view._label.characterSpacing = header.characterSpacing;
            view._label.enableWordWrapping = false;
            view._label.richText = false;
            view._label.overflowMode = TextOverflowModes.Overflow;
            // Preserve the original hit surface if the native header was one.
            // Pointer events still bubble to the same native toggle; no new handler.
            view._label.raycastTarget = header.raycastTarget;
            view._label.enabled = view._headerEnabled;
            // Disabling TMP also removes its preferred dimensions from native
            // layout, collapsing the toggle/background to an almost empty strip.
            // Hide only its rendering/input; keep its native layout participation.
            view._headerGroup = header.GetComponent<CanvasGroup>();
            view._addedGroup = view._headerGroup == null;
            if (view._addedGroup) view._headerGroup = header.gameObject.AddComponent<CanvasGroup>();
            view._groupAlpha = view._headerGroup.alpha;
            view._groupInteractable = view._headerGroup.interactable;
            view._groupBlocksRaycasts = view._headerGroup.blocksRaycasts;
            view._headerGroup.alpha = 0f;
            view._headerGroup.interactable = false;
            view._headerGroup.blocksRaycasts = false;
            view.Refresh();
            return view;
        }
        catch { view.Close(); throw; }
    }

    internal void Refresh()
    {
        if (_rect == null || _header == null || _label == null) return;
        _rect.anchorMin = _rect.anchorMax = _target;
        _rect.anchoredPosition = Vector2.zero;
        _header.text = _name;
        if (_size != null) _size.text = _name;
        _label.text = _name;
        _label.fontSize = _header.fontSize;
        _label.fontStyle = _header.fontStyle;
        _label.color = _selected?.Invoke() == true ? SelectedGold : Gold;
        // Keep native layout sizing and animation, without inheriting its text tint.
        var from = _header.rectTransform;
        var to = _label.rectTransform;
        to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
        to.sizeDelta = from.sizeDelta; to.anchoredPosition = from.anchoredPosition;
        to.localScale = from.localScale;
    }

    internal void Close()
    {
        if (_rect != null)
        { _rect.anchorMin = _anchorMin; _rect.anchorMax = _anchorMax; _rect.anchoredPosition = _position; }
        if (_header != null) { _header.text = _headerText; _header.enabled = _headerEnabled; }
        if (_size != null) _size.text = _sizeText;
        if (_headerGroup != null)
        {
            _headerGroup.alpha = _groupAlpha;
            _headerGroup.interactable = _groupInteractable;
            _headerGroup.blocksRaycasts = _groupBlocksRaycasts;
            if (_addedGroup) UnityEngine.Object.DestroyImmediate(_headerGroup);
        }
        _headerGroup = null; _addedGroup = false;
        var label = _label; _label = null;
        _rect = null; _header = _size = null; _selected = null;
        if (label != null) { label.gameObject.SetActive(false); UnityEngine.Object.Destroy(label.gameObject); }
    }
}
