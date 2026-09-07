using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Visible mode rows beside the map, built from EFT's dropdown-row prefab.</summary>
internal sealed class ArenaModeSelectionView
{
    private const float PanelWidth = 360f;
    private const float Inset = 20f;
    private readonly List<Button> _buttons = new();
    private readonly List<GameObject> _markers = new();
    private GameObject _root;
    private TextMeshProUGUI _rules;
    private TextMeshProUGUI _respawn;
    private TextMeshProUGUI _equipment;
    private TextMeshProUGUI _subtitle;
    private GameObject _restrictionNote;
    private ArenaModeSelectionState _selection;

    internal GameObject Root => _root;
    internal string SelectedMode => _selection?.SelectedMode;

    internal void Synchronize(string selectedMode, bool tacticalOnly)
    {
        if (_selection == null || _root == null) return;
        if (!_selection.Synchronize(selectedMode, tacticalOnly)) return;
        _subtitle.text = tacticalOnly ? "GAME MODE / PITFIRETEAM 5v5" : "GAME MODE";
        ((RectTransform)_root.transform).sizeDelta = new Vector2(PanelWidth, tacticalOnly ? 524f : 488f);
        _restrictionNote.SetActive(tacticalOnly);
        for (var index = 0; index < _buttons.Count; index++)
        {
            var allowed = _selection.CanSelectIndex(index);
            _buttons[index].interactable = allowed;
            var tooltip = _buttons[index].GetComponent<HoverTooltipArea>();
            if (tooltip == null) continue;
            tooltip.SetMessageText(allowed ? string.Empty : "Companions require Tactical.", rawText: true);
            tooltip.SetUnlockStatus(allowed);
        }
        Refresh();
    }

    public void Show(Transform screen, TMP_Text nativeText, string selectedMode,
        bool tacticalOnly, Func<string, bool> onSelected)
    {
        Close();
        var template = MenuUI.Instance?.MatchmakerOfflineRaidScreen
            ?._raidSettingsWindow?._aiDifficultyDropdown;
        if (screen == null || template?._elementTemplate == null
            || ItemUiContext.Instance == null)
            throw new InvalidOperationException("The native mode-row template is unavailable.");

        _selection = new ArenaModeSelectionState(selectedMode, tacticalOnly, onSelected);
        try
        {
            Create(screen, template._elementTemplate, template._currentValueText ?? nativeText);
            _selection.CommitInitialSelection();
            Refresh();
            _root.SetActive(true);
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Close()
    {
        // Each clone has a fresh event; no callback or popup belongs to another screen.
        _selection = null;
        foreach (var button in _buttons)
            Safe(() => { if (button != null) button.onClick.RemoveAllListeners(); });
        _buttons.Clear();
        _markers.Clear();
        var root = _root;
        _root = null;
        _rules = _respawn = _equipment = _subtitle = null;
        _restrictionNote = null;
        Safe(() => { if (root != null) root.SetActive(false); });
        Safe(() => { if (root != null) UnityEngine.Object.Destroy(root); });
    }

    private void Create(Transform screen, Button template, TMP_Text typography)
    {
        _root = new GameObject("Arena Mode Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.SetActive(false);
        _root.transform.SetParent(screen, false);
        _root.transform.SetAsLastSibling();
        var rect = (RectTransform)_root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        // The native map and all its click targets remain in place. At the
        // 1080-unit reference height this also leaves room below for future rows.
        rect.anchoredPosition = new Vector2(56f, -168f);
        rect.sizeDelta = new Vector2(PanelWidth, _selection.TacticalOnly ? 524f : 488f);
        var background = _root.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, .68f);
        background.raycastTarget = false;

        var title = CreateText("Title", typography, "ARENA", 26f, 16f, 32f);
        var subtitle = CreateText("Mode heading", typography,
            _selection.TacticalOnly ? "GAME MODE / PITFIRETEAM 5v5" : "GAME MODE", 14f, 51f, 22f);
        subtitle.color = Dim(typography, .65f);
        _subtitle = subtitle;

        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
            CreateRow(template, typography, index, 84f + index * 42f);

        CreateRule(308f, typography);
        var heading = CreateText("Rules heading", typography, "MATCH RULES", 14f, 323f, 22f);
        heading.color = Dim(typography, .65f);
        _rules = CreateFact("RULES", typography, 354f);
        _respawn = CreateFact("RESPAWN", typography, 394f);
        _equipment = CreateFact("EQUIPMENT", typography, 434f);

        var note = CreateText("Team restriction", typography,
            "Companions require Tactical.", 15f, 484f, 24f);
        note.color = Dim(typography, .75f);
        _restrictionNote = note.gameObject;
        _restrictionNote.SetActive(_selection.TacticalOnly);
    }

    private void CreateRow(Button template, TMP_Text typography, int index, float top)
    {
        // This is the same prefab BaseDropDownBox.ToggleMenu clones for each
        // list item. Do not clone the dropdown or its ContextMenuArea owner.
        var button = UnityEngine.Object.Instantiate(template, _root.transform, false);
        _buttons.Add(button);
        button.name = "Arena Mode " + ArenaModeCatalog.All[index].Id;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        SetRect((RectTransform)button.transform, Inset, top, PanelWidth - 2f * Inset, 38f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native mode-row label is missing.");
        label.text = ArenaModeCatalog.All[index].Label;
        label.fontSize = 20f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        SetRect(label.rectTransform, 14f, 0f, PanelWidth - 2f * Inset - 28f, 38f);

        var allowed = _selection.CanSelectIndex(index);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            tooltip.SetMessageText(allowed ? string.Empty : "Companions require Tactical.", rawText: true);
            tooltip.SetUnlockStatus(allowed);
        }
        button.interactable = allowed;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        var marker = new GameObject("Selected", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        marker.transform.SetParent(button.transform, false);
        marker.transform.SetAsFirstSibling();
        SetRect((RectTransform)marker.transform, 0f, 0f, 3f, 38f);
        var markerImage = marker.GetComponent<Image>();
        markerImage.color = typography != null ? typography.color : Color.white;
        markerImage.raycastTarget = false;
        _markers.Add(marker);
        button.onClick.AddListener(() => Select(index));
        button.gameObject.SetActive(true);
    }

    private void Select(int index)
    {
        if (_selection == null) return;
        try
        {
            if (_selection.TrySelectIndex(index) && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena mode selection could not be saved: {ex.Message}");
        }
        // The accepted saved state, not focus or hover, owns the persistent marker.
        Refresh();
    }

    private void Refresh()
    {
        if (_selection == null) return;
        for (var index = 0; index < _markers.Count; index++)
            _markers[index].SetActive(index == _selection.SelectedIndex);
        var mode = _selection.SelectedDefinition;
        _rules.text = mode.Rules;
        _respawn.text = mode.Respawn;
        _equipment.text = _selection.TacticalOnly ? "PMC entry gear" : mode.Equipment;
    }

    private TextMeshProUGUI CreateFact(string label, TMP_Text typography, float top)
    {
        var key = CreateText(label + " label", typography, label, 14f, top, 30f);
        key.rectTransform.sizeDelta = new Vector2(98f, 30f);
        key.color = Dim(typography, .65f);
        var value = CreateText(label + " value", typography, string.Empty, 17f, top, 30f);
        SetRect(value.rectTransform, 122f, top, PanelWidth - 142f, 30f);
        return value;
    }

    private void CreateRule(float top, TMP_Text typography)
    {
        var rule = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.transform.SetParent(_root.transform, false);
        SetRect((RectTransform)rule.transform, Inset, top, PanelWidth - 2f * Inset, 1f);
        rule.GetComponent<Image>().color = Dim(typography, .25f);
        rule.GetComponent<Image>().raycastTarget = false;
    }

    private TextMeshProUGUI CreateText(string name, TMP_Text source, string value,
        float size, float top, float height)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(_root.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null)
        {
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.color = source.color;
            text.fontStyle = source.fontStyle;
            text.characterSpacing = source.characterSpacing;
        }
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        SetRect(text.rectTransform, Inset, top, PanelWidth - 2f * Inset, height);
        return text;
    }

    private static Color Dim(TMP_Text source, float alpha)
    {
        var color = source != null ? source.color : Color.white;
        color.a *= alpha;
        return color;
    }

    private static void SetRect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena mode panel cleanup: {ex.Message}"); }
    }
}
