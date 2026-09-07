using ArenaMode.Modes;
using EFT.UI;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Arena settings using EFT's own raid-settings dropdown.</summary>
internal sealed class ArenaModeSelectionView
{
    // Native navigation extends through Y=168.36 in the verified EFT scene.
    // The settings group stays above it; Factory conditions retain a 7px gap.
    private const float RootBottom = 175f;
    private const float PanelWidth = 620f;
    private const float PanelHeight = 126f;
    private const float ConditionsLift = PanelHeight + 7f;

    private GameObject _root;
    private DropDownBox _dropdown;
    private TextMeshProUGUI _description;
    private ArenaModeSelectionState _selection;
    private Action _unsubscribe;
    private RectTransform _conditionsRect;
    private Vector2 _conditionsPosition;

    internal GameObject Root => _root;
    internal string SelectedMode => _selection?.SelectedMode;

    public void Show(Transform screen, TMP_Text nativeText, string selectedMode,
        bool tacticalOnly, Func<string, bool> onSelected,
        RectTransform conditionsRect = null)
    {
        Close();
        if (screen == null)
            throw new InvalidOperationException("The native location-screen transform is missing.");

        // Clone only this control, never RaidSettingsWindow: its Show/Apply
        // methods own unrelated native RaidSettings and group state.
        var template = MenuUI.Instance?.MatchmakerOfflineRaidScreen
            ?._raidSettingsWindow?._aiDifficultyDropdown;
        if (template == null || template.CurrentState || template.OpenPanel == null
            || !template.OpenPanel.IsChildOf(template.transform)
            || ItemUiContext.Instance?.ContextMenuArea == null)
            throw new InvalidOperationException("The native raid-settings dropdown template is unavailable.");

        _selection = new ArenaModeSelectionState(selectedMode, tacticalOnly, onSelected);
        try
        {
            Create(screen, template, nativeText, conditionsRect);
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
        _unsubscribe?.Invoke();
        _unsubscribe = null;
        try
        {
            // An open native dropdown lives temporarily in ContextMenuArea.
            // Close reparents it before the owning group is destroyed.
            if (_dropdown != null)
                _dropdown.Close();
        }
        finally
        {
            if (_conditionsRect != null)
                _conditionsRect.anchoredPosition = _conditionsPosition;
            _conditionsRect = null;
            _conditionsPosition = default;

            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _dropdown = null;
            _description = null;
            _selection = null;
        }
    }

    private void Create(Transform screen, DropDownBox template,
        TMP_Text nativeText, RectTransform conditionsRect)
    {
        _root = new GameObject("Arena Settings",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.SetActive(false);
        _root.transform.SetParent(screen, false);
        _root.transform.SetAsLastSibling();

        var rootRect = (RectTransform)_root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, RootBottom);
        rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var background = _root.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.62f);
        background.raycastTarget = false;

        // Typography and dropdown chrome come from EFT. Shared assets are
        // referenced without changing the original control or its materials.
        var typography = template._currentValueText != null
            ? template._currentValueText : nativeText;
        var title = CreateText("Title", typography, 18f);
        title.text = _selection.TacticalOnly
            ? "ARENA SETTINGS — PITFIRETEAM 5v5" : "ARENA SETTINGS";
        SetRect(title.rectTransform, new Vector2(0f, 1f),
            new Vector2(20f, -8f), new Vector2(PanelWidth - 40f, 24f));

        var ruleObject = new GameObject("Divider",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ruleObject.transform.SetParent(_root.transform, false);
        SetRect((RectTransform)ruleObject.transform, new Vector2(0f, 1f),
            new Vector2(20f, -35f), new Vector2(PanelWidth - 40f, 1f));
        var rule = ruleObject.GetComponent<Image>();
        var textColor = typography != null ? typography.color : Color.white;
        rule.color = new Color(textColor.r, textColor.g, textColor.b, 0.25f);
        rule.raycastTarget = false;

        // Future implemented settings can add rows to this same group.
        var label = CreateText("Game mode label", typography, 18f);
        label.text = "GAME MODE";
        SetRect(label.rectTransform, new Vector2(0f, 1f),
            new Vector2(20f, -43f), new Vector2(180f, 34f));

        _dropdown = UnityEngine.Object.Instantiate(template, _root.transform, false);
        _dropdown.name = "Arena Game Mode";
        _dropdown.transform.localScale = Vector3.one;
        SetRect(_dropdown.RectTransform, new Vector2(0f, 1f),
            new Vector2(210f, -43f), new Vector2(PanelWidth - 230f, 34f));
        // At the bottom of the map screen, a downward list covers Next/Back.
        // Set the cloned popup's geometry before Show caches its closed position.
        var popup = _dropdown.OpenPanel;
        popup.anchorMin = new Vector2(0f, 1f);
        popup.anchorMax = new Vector2(1f, 1f);
        popup.pivot = new Vector2(0.5f, 0f);
        popup.anchoredPosition = new Vector2(0f, 2f);
        popup.sizeDelta = new Vector2(0f, popup.sizeDelta.y);
        _dropdown.gameObject.SetActive(true);
        // Show initializes inactive clones itself. Calling Init separately
        // would duplicate the native hover handlers.
        _dropdown.Show(ArenaModeCatalog.All.Select(mode => mode.Label),
            index => _selection != null && _selection.CanSelectIndex(index));
        _dropdown.Interactable = true;
        _dropdown._maxVisibleHeight = 200f;
        _dropdown.UpdateValue(_selection.SelectedIndex, sendCallback: false);
        _unsubscribe = _dropdown.OnValueChanged.Subscribe(Select);

        _description = CreateText("Description", typography, 16f);
        _description.enableWordWrapping = true;
        _description.alignment = TextAlignmentOptions.TopLeft;
        SetRect(_description.rectTransform, new Vector2(0f, 1f),
            new Vector2(20f, -84f), new Vector2(PanelWidth - 40f, 38f));

        if (conditionsRect != null)
        {
            _conditionsRect = conditionsRect;
            _conditionsPosition = conditionsRect.anchoredPosition;
            conditionsRect.anchoredPosition = _conditionsPosition
                + new Vector2(0f, ConditionsLift);
        }
    }

    private void Select(int index)
    {
        if (_selection == null)
            return;
        try
        {
            _selection.TrySelectIndex(index);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena mode selection could not be saved: {ex.Message}");
        }
        // Native UpdateValue displays its label before invoking us. Restore the
        // saved selection too when the callback rejects or fails to save.
        Refresh();
    }

    private void Refresh()
    {
        _dropdown.UpdateValue(_selection.SelectedIndex, sendCallback: false);
        _description.text = _selection.SelectedDefinition.Description
            + (_selection.TacticalOnly ? "\nPitFireTeam companions require Tactical." : string.Empty);
    }

    private TextMeshProUGUI CreateText(string name, TMP_Text source, float size)
    {
        var textObject = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
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
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
