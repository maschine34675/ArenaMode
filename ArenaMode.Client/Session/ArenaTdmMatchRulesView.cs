using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Two compact rows inside the existing mode panel; never extends its footprint.</summary>
internal sealed class ArenaTdmMatchRulesView
{
    private const float Width = 320f;
    private sealed class Choice
    {
        internal Button Button;
        internal GameObject Marker;
        internal int Value;
        internal bool Time;
    }
    private readonly List<Choice> _choices = new();
    private GameObject _root;
    private ArenaTdmMatchRulesState _state;
    private TextMeshProUGUI _kills, _time;
    private bool _dispatching;

    internal void Show(Transform parent, Button template, TMP_Text typography, ArenaTdmMatchRulesState state)
    {
        Close();
        if (parent == null || template == null || state == null)
            throw new InvalidOperationException("The Arena TDM rules view contract is unavailable.");
        _state = state;
        try
        {
            _root = new GameObject("Arena " + _state.ModeName + " Match Rules", typeof(RectTransform));
            _root.SetActive(false);
            _root.transform.SetParent(parent, false);
            SetRect((RectTransform)_root.transform, 20f, 396f, Width, 78f);
            _kills = CreateText(_state.Domination ? "Domination point limit" : "TDM kill limit", typography, 0f);
            _time = CreateText(_state.Domination ? "Domination time limit" : "TDM time limit", typography, 39f);
            foreach (int value in _state.ScoreOptions)
                CreateChoice(template, typography, value, false);
            foreach (int value in ArenaTdmMatchRulesState.TimeOptions)
                CreateChoice(template, typography, value, true);
            Refresh();
        }
        catch { Close(); throw; }
    }

    internal void Refresh()
    {
        if (_root == null || _state == null) return;
        _root.SetActive(_state.Visible);
        _kills.text = _state.ScoreHeading + ": " + _state.KillLabel;
        _time.text = "TIME LIMIT: " + _state.TimeLabel;
        foreach (var choice in _choices)
        {
            choice.Button.interactable = _state.Editable;
            choice.Marker.SetActive(choice.Value == (choice.Time ? _state.TimeLimitSeconds : _state.KillLimit));
        }
    }

    internal void Close()
    {
        _state = null;
        _dispatching = false;
        foreach (var choice in _choices)
            if (choice.Button != null) choice.Button.onClick.RemoveAllListeners();
        _choices.Clear();
        _kills = _time = null;
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private void CreateChoice(Button template, TMP_Text typography, int value, bool time)
    {
        int count = time ? ArenaTdmMatchRulesState.TimeOptions.Count : _state.ScoreOptions.Count;
        int index = time ? _choices.Count - _state.ScoreOptions.Count : _choices.Count;
        float width = (Width - (count - 1) * 4f) / count;
        var button = UnityEngine.Object.Instantiate(template, _root.transform, false);
        var choice = new Choice { Button = button, Value = value, Time = time };
        _choices.Add(choice);
        button.name = "Arena " + _state.ModeName + " " + (time ? "Seconds " : _state.Domination ? "Points " : "Kills ") + value;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        SetRect((RectTransform)button.transform, index * (width + 4f), time ? 55f : 16f, width, 22f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native TDM rules row label is missing.");
        label.text = value == 0 ? "Off" : (time ? (value / 60).ToString() : value.ToString());
        label.fontSize = 14f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        Hud.ArenaUiTheme.StyleButton(button, label);
        SetRect(label.rectTransform, 10f, 0f, width - 16f, 22f);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            tooltip.SetMessageText(time ? ArenaTdmMatchRulesState.FormatTime(value)
                : ArenaTdmMatchRulesState.FormatKills(value) + (_state.Domination ? " team points" : " team kills"), rawText: true);
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
            bool accepted = choice.Time ? _state.TrySelectTime(choice.Value) : _state.TrySelectKills(choice.Value);
            if (ReferenceEquals(owner, _root) && owner != null)
            {
                Refresh();
                if (accepted && Singleton<GUISounds>.Instantiated)
                    Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
            }
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena TDM rules selection failed: {ex.Message}"); }
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
