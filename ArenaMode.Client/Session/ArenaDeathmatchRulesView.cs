using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Four compact rule rows inside the Deathmatch mode panel.</summary>
internal sealed class ArenaDeathmatchRulesView
{
    private const float Width = 320f;
    private const float RowHeight = 39f;
    private sealed class Choice
    {
        internal Button Button;
        internal GameObject Marker;
        internal int Value;
        internal int Row;
    }
    private readonly List<Choice> _choices = new();
    private GameObject _root;
    private ArenaDeathmatchRulesState _state;
    private readonly List<TextMeshProUGUI> _labels = new();
    private bool _dispatching;
    private Action _onChanged;

    internal void Show(Transform parent, Button template, TMP_Text typography, ArenaDeathmatchRulesState state,
        Action onChanged = null)
    {
        Close();
        if (parent == null || template == null || state == null)
            throw new InvalidOperationException("The Arena Deathmatch rules view contract is unavailable.");
        _state = state;
        _onChanged = onChanged;
        try
        {
            _root = new GameObject("Arena Deathmatch Match Rules", typeof(RectTransform));
            _root.SetActive(false);
            _root.transform.SetParent(parent, false);
            // Mode descriptions and all three common facts remain visible
            // above the controls, without extending the accepted panel height.
            SetRect((RectTransform)_root.transform, 20f, 396f, Width, 156f);
            for (int row = 0; row < 4; row++)
            {
                _labels.Add(CreateText("Deathmatch rule " + row, typography, row * RowHeight));
                for (int index = 0; index < ArenaDeathmatchRulesState.Choices[row].Length; index++)
                    CreateChoice(template, typography, ArenaDeathmatchRulesState.Choices[row][index], row, index);
            }
            Refresh();
        }
        catch { Close(); throw; }
    }

    internal void Refresh()
    {
        if (_root == null || _state == null) return;
        _root.SetActive(_state.Visible);
        for (int row = 0; row < _labels.Count; row++) _labels[row].text = _state.Label(row);
        foreach (var choice in _choices)
        {
            choice.Button.interactable = _state.CanSelect(choice.Row, choice.Value);
            choice.Marker.SetActive(choice.Value == _state.Value(choice.Row));
        }
    }

    internal void Close()
    {
        _state = null;
        _onChanged = null;
        _dispatching = false;
        foreach (var choice in _choices)
            if (choice.Button != null) choice.Button.onClick.RemoveAllListeners();
        _choices.Clear();
        _labels.Clear();
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private void CreateChoice(Button template, TMP_Text typography, int value, int row, int index)
    {
        int count = ArenaDeathmatchRulesState.Choices[row].Length;
        float width = (Width - (count - 1) * 4f) / count;
        var button = UnityEngine.Object.Instantiate(template, _root.transform, false);
        var choice = new Choice { Button = button, Value = value, Row = row };
        _choices.Add(choice);
        button.name = "Arena Deathmatch " + row + " " + value;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        SetRect((RectTransform)button.transform, index * (width + 4f), row * RowHeight + 16f, width, 22f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native Deathmatch rules row label is missing.");
        label.text = ArenaDeathmatchRulesState.ChoiceLabel(row, value);
        label.fontSize = 14f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        Hud.ArenaUiTheme.StyleButton(button, label);
        SetRect(label.rectTransform, 6f, 0f, width - 10f, 22f);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            tooltip.SetMessageText(row == 2 ? (value == 0 ? "No time limit" : value / 60 + " minutes per round")
                : ArenaDeathmatchRulesState.ChoiceLabel(row, value), rawText: true);
            tooltip.SetUnlockStatus(true);
        }
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        choice.Marker = new GameObject("Selected", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        choice.Marker.transform.SetParent(button.transform, false);
        choice.Marker.transform.SetAsLastSibling();
        SetRect((RectTransform)choice.Marker.transform, 0f, 0f, 3f, 22f);
        var marker = choice.Marker.GetComponent<Image>();
        marker.color = Hud.ArenaUiTheme.Accent;
        marker.raycastTarget = false;
        button.onClick.AddListener(() => Select(choice));
        button.gameObject.SetActive(true);
    }

    private void Select(Choice choice)
    {
        // Retired listeners can already be on a Unity dispatch stack when a
        // screen closes/reopens. Exact row membership also rejects those calls.
        if (_dispatching || _root == null || !_root.activeInHierarchy
            || _state == null || !_choices.Contains(choice)) return;
        var owner = _root;
        _dispatching = true;
        try
        {
            bool accepted = _state.Select(choice.Row, choice.Value);
            if (ReferenceEquals(owner, _root) && owner != null)
            {
                Refresh();
                if (accepted) _onChanged?.Invoke();
                if (accepted && Singleton<GUISounds>.Instantiated)
                    Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
            }
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena Deathmatch rules selection failed: {ex.Message}"); }
        finally { if (ReferenceEquals(owner, _root)) _dispatching = false; }
    }

    private TextMeshProUGUI CreateText(string name, TMP_Text typography, float top)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(_root.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = typography?.font ?? TMP_Settings.defaultFontAsset;
        if (typography != null)
        {
            text.fontSharedMaterial = typography.fontSharedMaterial;
            text.color = typography.color;
            text.fontStyle = typography.fontStyle;
            text.characterSpacing = typography.characterSpacing;
        }
        text.fontSize = 13f;
        text.color = Hud.ArenaUiTheme.Muted;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        SetRect(text.rectTransform, 0f, top, Width, 15f);
        return text;
    }

    private static void SetRect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
