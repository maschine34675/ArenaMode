using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// Two inline choices ("Arena kits" / "Own gear") inside the LEFT mode panel,
/// replacing only the plain EQUIPMENT value for any mode
/// <see cref="Modes.ArenaEquipmentChoice.IsOfferedFor"/> returns true for
/// (today: Zombie only). The common EQUIPMENT label stays in the same position
/// as every other mode; selected buttons carry the current value. These controls
/// retain their own state lock and exact callback ownership when hidden/retired.
/// </summary>
internal sealed class ArenaEquipmentChoiceView
{
    private const float Width = 218f;
    private sealed class Choice
    {
        internal Button Button;
        internal GameObject Marker;
        internal bool OwnGear;
    }
    private readonly List<Choice> _choices = new();
    private GameObject _root;
    private ArenaEquipmentChoiceState _state;
    private bool _dispatching;

    internal void Show(Transform parent, Button template, TMP_Text typography, ArenaEquipmentChoiceState state)
    {
        Close();
        if (parent == null || template == null || state == null)
            throw new InvalidOperationException("The Arena equipment choice view contract is unavailable.");
        _state = state;
        try
        {
            _root = new GameObject("Arena Equipment Choice", typeof(RectTransform));
            _root.SetActive(false);
            _root.transform.SetParent(parent, false);
            SetRect((RectTransform)_root.transform, 122f, 390f, Width, 30f);
            CreateChoice(template, typography, ownGear: false, index: 0);
            CreateChoice(template, typography, ownGear: true, index: 1);
            Refresh();
        }
        catch { Close(); throw; }
    }

    internal void Refresh()
    {
        if (_root == null || _state == null) return;
        _root.SetActive(_state.Visible);
        foreach (var choice in _choices)
        {
            choice.Button.interactable = _state.Editable;
            choice.Marker.SetActive(choice.OwnGear == _state.OwnGear);
        }
    }

    internal void Close()
    {
        _state = null;
        _dispatching = false;
        foreach (var choice in _choices)
            if (choice.Button != null) choice.Button.onClick.RemoveAllListeners();
        _choices.Clear();
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private void CreateChoice(Button template, TMP_Text typography, bool ownGear, int index)
    {
        const int count = 2;
        float width = (Width - (count - 1) * 4f) / count;
        var button = UnityEngine.Object.Instantiate(template, _root.transform, false);
        var choice = new Choice { Button = button, OwnGear = ownGear };
        _choices.Add(choice);
        button.name = "Arena Equipment Choice " + (ownGear ? "OwnGear" : "ArenaKits");
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        SetRect((RectTransform)button.transform, index * (width + 4f), 1f, width, 28f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native equipment choice row label is missing.");
        label.text = Modes.ArenaEquipmentChoice.Label(ownGear);
        label.fontSize = 14f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        Hud.ArenaUiTheme.StyleButton(button, label);
        SetRect(label.rectTransform, 6f, 0f, width - 10f, 28f);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            tooltip.SetMessageText(label.text, rawText: true);
            tooltip.SetUnlockStatus(true);
        }
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        choice.Marker = new GameObject("Selected", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        choice.Marker.transform.SetParent(button.transform, false);
        choice.Marker.transform.SetAsLastSibling();
        SetRect((RectTransform)choice.Marker.transform, 0f, 0f, 3f, 28f);
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
            bool accepted = _state.TrySelect(choice.OwnGear);
            if (ReferenceEquals(owner, _root) && owner != null)
            {
                Refresh();
                if (accepted && Singleton<GUISounds>.Instantiated)
                    Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
            }
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena equipment choice selection failed: {ex.Message}"); }
        finally { if (ReferenceEquals(owner, _root)) _dispatching = false; }
    }

    private static void SetRect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
