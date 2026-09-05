using ArenaMode.Modes;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Arena-only mode picker attached to the native location screen.</summary>
internal sealed class ArenaModeSelectionView
{
    // The first live rendering showed that the serialized navigation controls
    // extend above their zero-sized roots through Y=168.36. Keep the whole
    // Arena block above that measured stack. Factory's native Conditions Panel
    // is shifted by the same amount while this view owns the screen, preserving
    // the inspected seven-pixel gap between the two panels.
    private const float LayoutLift = 93f;
    private const float RootBottom = 82f + LayoutLift;

    private sealed class ButtonVisual
    {
        public ArenaModeDefinition Definition;
        public Button Button;
        public Image Background;
        public TextMeshProUGUI Label;
    }

    private static readonly Color PanelColor = new(0.025f, 0.03f, 0.035f, 0.94f);
    private static readonly Color IdleColor = new(0.13f, 0.15f, 0.17f, 0.98f);
    private static readonly Color HoverColor = new(0.26f, 0.29f, 0.32f, 1f);
    private static readonly Color PressedColor = new(0.36f, 0.39f, 0.41f, 1f);
    private static readonly Color SelectedColor = new(0.76f, 0.46f, 0.13f, 1f);
    private static readonly Color SelectedHoverColor = new(0.92f, 0.59f, 0.19f, 1f);
    private static readonly Color DisabledColor = new(0.07f, 0.08f, 0.09f, 0.82f);
    private static readonly Color PrimaryTextColor = new(0.94f, 0.95f, 0.96f, 1f);
    private static readonly Color SecondaryTextColor = new(0.68f, 0.71f, 0.74f, 1f);

    private readonly List<ButtonVisual> _buttons = new();
    private GameObject _root;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _description;
    private Func<string, bool> _onSelected;
    private string _selectedMode;
    private bool _tacticalOnly;
    private RectTransform _conditionsRect;
    private Vector2 _conditionsPosition;
    private bool _conditionsShifted;

    internal GameObject Root => _root;
    internal string SelectedMode => _selectedMode;

    public void Show(Transform screen, TMP_Text nativeText, string selectedMode,
        bool tacticalOnly, Func<string, bool> onSelected,
        RectTransform conditionsRect = null)
    {
        Close();
        if (screen == null)
            throw new InvalidOperationException("The native location-screen transform is missing.");

        _tacticalOnly = tacticalOnly;
        _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
        _selectedMode = ArenaModeCatalog.ResolveSelection(selectedMode, tacticalOnly);

        try
        {
            Create(screen, nativeText, conditionsRect);
            Refresh();

            // A complete PitFireTeam roster can enter Arena while an older solo
            // mode is still stored. Commit Tactical before any later claim or
            // raid-start check sees that flow.
            if (!string.Equals(selectedMode?.Trim(), _selectedMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!_onSelected(_selectedMode))
                {
                    throw new InvalidOperationException(
                        $"Arena could not apply mode '{_selectedMode}'.");
                }
            }
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Close()
    {
        if (_conditionsShifted && _conditionsRect != null)
            _conditionsRect.anchoredPosition = _conditionsPosition;

        _conditionsRect = null;
        _conditionsPosition = default;
        _conditionsShifted = false;

        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }

        _root = null;
        _title = null;
        _description = null;
        _onSelected = null;
        _selectedMode = null;
        _tacticalOnly = false;
        _buttons.Clear();
    }

    private void Create(Transform screen, TMP_Text nativeText,
        RectTransform conditionsRect)
    {
        if (conditionsRect != null)
        {
            _conditionsRect = conditionsRect;
            _conditionsPosition = conditionsRect.anchoredPosition;
            conditionsRect.anchoredPosition = new Vector2(
                _conditionsPosition.x,
                _conditionsPosition.y + LayoutLift);
            _conditionsShifted = true;
        }

        _root = new GameObject("Arena Mode Selection",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.transform.SetParent(screen, false);
        _root.transform.SetAsLastSibling();

        var rootRect = (RectTransform)_root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, RootBottom);
        rootRect.sizeDelta = new Vector2(820f, 86f);

        var panel = _root.GetComponent<Image>();
        panel.color = PanelColor;
        panel.raycastTarget = false;

        _title = CreateText(_root.transform, "Title", nativeText, 15f,
            TextAlignmentOptions.Center, FontStyles.Bold);
        SetRect(_title.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), new Vector2(800f, 20f));
        _title.color = SecondaryTextColor;

        const float spacing = 8f;
        // Keep all five modes inside the existing panel instead of widening it
        // into native controls or changing the live-tested vertical placement.
        var width = Math.Min(190f,
            (800f - (ArenaModeCatalog.All.Count - 1) * spacing) / ArenaModeCatalog.All.Count);
        var totalWidth = ArenaModeCatalog.All.Count * width
                         + (ArenaModeCatalog.All.Count - 1) * spacing;
        var firstX = -totalWidth * 0.5f + width * 0.5f;

        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
        {
            var definition = ArenaModeCatalog.All[index];
            var buttonObject = new GameObject("Mode " + definition.Id,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_root.transform, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(firstX + index * (width + spacing), 41f);
            rect.sizeDelta = new Vector2(width, 32f);

            var background = buttonObject.GetComponent<Image>();
            background.color = IdleColor;
            background.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            var capturedId = definition.Id;
            button.onClick.AddListener(() => Select(capturedId));

            var label = CreateText(buttonObject.transform, "Label", nativeText, 16f,
                TextAlignmentOptions.Center, FontStyles.Bold);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.text = definition.Label;
            label.color = PrimaryTextColor;

            _buttons.Add(new ButtonVisual
            {
                Definition = definition,
                Button = button,
                Background = background,
                Label = label,
            });
        }

        _description = CreateText(_root.transform, "Description", nativeText, 13f,
            TextAlignmentOptions.Center, FontStyles.Normal);
        SetRect(_description.rectTransform, new Vector2(0.5f, 0f),
            new Vector2(0f, 8f), new Vector2(800f, 18f));
        _description.color = SecondaryTextColor;
    }

    private void Select(string id)
    {
        if (!ArenaModeCatalog.CanSelect(id, _tacticalOnly))
            return;

        var normalized = ArenaModeCatalog.NormalizeId(id);
        if (string.Equals(_selectedMode, normalized, StringComparison.Ordinal))
            return;

        if (!_onSelected(normalized))
            return;

        _selectedMode = normalized;
        Refresh();
    }

    private void Refresh()
    {
        _title.text = _tacticalOnly
            ? "ARENA MODE  —  PITFIRETEAM 5v5"
            : "ARENA MODE";

        ArenaModeDefinition selected = null;
        foreach (var visual in _buttons)
        {
            var isSelected = string.Equals(
                visual.Definition.Id, _selectedMode, StringComparison.Ordinal);
            var canSelect = ArenaModeCatalog.CanSelect(
                visual.Definition.Id, _tacticalOnly);
            visual.Button.interactable = canSelect;

            var colors = visual.Button.colors;
            colors.normalColor = isSelected ? SelectedColor : IdleColor;
            colors.highlightedColor = isSelected ? SelectedHoverColor : HoverColor;
            colors.pressedColor = isSelected ? SelectedHoverColor : PressedColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = DisabledColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            visual.Button.colors = colors;
            visual.Background.color = canSelect ? colors.normalColor : colors.disabledColor;
            visual.Label.color = canSelect ? PrimaryTextColor : SecondaryTextColor;

            if (isSelected)
                selected = visual.Definition;
        }

        _description.text = (selected?.Description ?? string.Empty)
            + (_tacticalOnly ? "  PitFireTeam companions require Tactical." : string.Empty);
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name,
        TMP_Text nativeText, float fontSize, TextAlignmentOptions alignment,
        FontStyles fontStyle)
    {
        var textObject = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = nativeText?.font ?? TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.fontStyle = fontStyle;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
