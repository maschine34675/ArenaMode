using ArenaMode.Hud;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Three integer sliders below the common mode facts; never edits a live match.</summary>
internal sealed class ArenaZombieRulesView
{
    private sealed class Row
    {
        internal ArenaZombieRule Rule;
        internal Slider Slider;
        internal TMP_Text Value;
    }
    private readonly ArenaZombieRulesState _state = new();
    private readonly List<Row> _rows = new();
    private GameObject _root;
    private bool _dispatching;

    internal void Show(Transform parent, TMP_Text typography)
    {
        Close();
        _root = new GameObject("Arena Zombie Rules", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        Rect((RectTransform)_root.transform, 20f, 432f, 320f, 150f);
        _root.SetActive(false);
        try
        {
            foreach (ArenaZombieRule rule in Enum.GetValues(typeof(ArenaZombieRule)))
            {
                float top = _rows.Count * 50f;
                Text(typography, "Zombie label " + rule, ArenaZombieRulesState.Label(rule), 0f, top, 250f);
                var value = Text(typography, "Zombie value " + rule, "", 262f, top, 58f);
                value.alignment = TextAlignmentOptions.Center;
                var go = new GameObject("Arena Zombie " + rule, typeof(RectTransform), typeof(Image), typeof(Slider));
                go.transform.SetParent(_root.transform, false);
                Rect((RectTransform)go.transform, 0f, top + 22f, 320f, 22f);
                // A full-height invisible target lets clicks beside the thin track
                // reach Slider.OnPointerDown. Child art never intercepts input.
                go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                go.GetComponent<Image>().raycastTarget = true;
                var track = Image(go.transform, "Track", ArenaUiTheme.Raised, 8f, 9f, 304f, 4f);
                var fill = Image(track.transform, "Fill", ArenaUiTheme.Accent, 0f, 0f, 304f, 4f);
                var handleArea = new GameObject("Handle area", typeof(RectTransform));
                handleArea.transform.SetParent(go.transform, false);
                Rect((RectTransform)handleArea.transform, 8f, 0f, 304f, 22f);
                var handle = Image(handleArea.transform, "Handle", ArenaUiTheme.Text, 0f, 2f, 16f, 18f);
                var handleRect = (RectTransform)handle.transform;
                handleRect.pivot = new Vector2(.5f, .5f);
                handleRect.anchoredPosition = Vector2.zero;
                // Slider owns horizontal anchors; use offsets to keep fixed art
                // thickness when Unity stretches the vertical anchors to 0..1.
                handleRect.sizeDelta = new Vector2(16f, -4f);
                var fillRect = (RectTransform)fill.transform;
                fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
                var slider = go.GetComponent<Slider>();
                slider.direction = Slider.Direction.LeftToRight;
                slider.wholeNumbers = true;
                slider.fillRect = fillRect;
                slider.handleRect = handleRect;
                slider.targetGraphic = handle;
                var navigation = slider.navigation; navigation.mode = Navigation.Mode.None; slider.navigation = navigation;
                var row = new Row { Rule = rule, Slider = slider, Value = value };
                _rows.Add(row);
                var generation = _root;
                slider.onValueChanged.AddListener(requested => Select(generation, row, requested));
            }
        }
        catch { Close(); throw; }
    }

    internal void Synchronize(string mode)
    {
        _state.Synchronize(mode);
        Refresh();
    }

    private void Refresh()
    {
        if (_root == null) return;
        _root.SetActive(_state.Visible);
        foreach (var row in _rows)
        {
            var range = ArenaZombieRulesState.Range(row.Rule);
            // Bounds setters may raise a native value event; suppress callbacks
            // for the complete refresh, including an out-of-date F12 value.
            bool dispatching = _dispatching;
            _dispatching = true;
            try
            {
                row.Slider.interactable = _state.Editable && range != null;
                if (range != null) { row.Slider.maxValue = range.MaxValue; row.Slider.minValue = range.MinValue; }
                int value = ArenaZombieRulesState.Value(row.Rule);
                row.Slider.SetValueWithoutNotify(value);
                row.Value.text = value.ToString();
            }
            finally { _dispatching = dispatching; }
        }
    }

    private void Select(GameObject generation, Row row, float requested)
    {
        if (_dispatching || !ReferenceEquals(generation, _root) || _root == null
            || !_root.activeInHierarchy || !_rows.Contains(row)) return;
        _dispatching = true;
        try
        {
            if (!float.IsNaN(requested) && !float.IsInfinity(requested)
                && requested >= int.MinValue && requested < int.MaxValue && requested == (int)requested)
                _state.TrySelect(row.Rule, (int)requested);
            if (ReferenceEquals(generation, _root)) Refresh();
        }
        finally { if (ReferenceEquals(generation, _root)) _dispatching = false; }
    }

    internal void Close()
    {
        foreach (var row in _rows) if (row.Slider != null) row.Slider.onValueChanged.RemoveAllListeners();
        _rows.Clear();
        _dispatching = false;
        _state.Synchronize(null);
        var root = _root; _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private TMP_Text Text(TMP_Text source, string name, string value, float left, float top, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(_root.transform, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) text.fontSharedMaterial = source.fontSharedMaterial;
        text.text = value; text.fontSize = 14f; text.color = ArenaUiTheme.Text;
        text.enableWordWrapping = false; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        Rect(text.rectTransform, left, top, width, 22f);
        return text;
    }

    private static Image Image(Transform parent, string name, Color color, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Rect((RectTransform)go.transform, left, top, width, height);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        return image;
    }

    private static void Rect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
