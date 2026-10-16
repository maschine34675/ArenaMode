using ArenaMode.Config;
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
    private readonly ArenaTdmMatchRulesView _tdmRulesView = new();
    private ArenaTdmMatchRulesState _tdmRules;
    private readonly ArenaDeathmatchRulesView _dmRulesView = new();
    private ArenaDeathmatchRulesState _dmRules;
    private readonly ArenaEquipmentChoiceView _equipmentChoiceView = new();
    private ArenaEquipmentChoiceState _equipmentChoice;
    private GameObject _root;
    private TextMeshProUGUI _rules;
    private TextMeshProUGUI _respawn;
    private TextMeshProUGUI _equipment;
    private TextMeshProUGUI _subtitle;
    private TextMeshProUGUI _description;
    private ArenaModeSelectionState _selection;
    private readonly ArenaMutatorsView _mutators = new();
    private readonly ArenaZombieRulesView _zombieRules = new();

    internal GameObject Root => _root;
    internal string SelectedMode => _selection?.SelectedMode;

    internal void Synchronize(string selectedMode, bool tacticalOnly)
    {
        if (_selection == null || _root == null) return;
        if (!_selection.Synchronize(selectedMode, tacticalOnly))
        {
            // F12 can change a limit without changing the selected mode.
            Refresh();
            return;
        }
        _subtitle.text = tacticalOnly ? "GAME MODE / PITFIRETEAM" : "GAME MODE";
        ((RectTransform)_root.transform).sizeDelta = new Vector2(PanelWidth, PanelHeight);
        for (var index = 0; index < _buttons.Count; index++)
        {
            var allowed = _selection.CanSelectIndex(index);
            _buttons[index].interactable = allowed;
            var tooltip = _buttons[index].GetComponent<HoverTooltipArea>();
            if (tooltip == null) continue;
            tooltip.SetMessageText(allowed ? string.Empty : "Companions require Tactical or Zombies.", rawText: true);
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
        _tdmRules = new ArenaTdmMatchRulesState(
            () => Settings.TeamDeathmatchKillLimit?.Value ?? -1,
            () => Settings.TeamDeathmatchTimeLimitSeconds?.Value ?? -1,
            Settings.TrySelectTeamDeathmatchKillLimit,
            Settings.TrySelectTeamDeathmatchTimeLimit,
            () => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid);
        _dmRules = new ArenaDeathmatchRulesState(row => row switch {
            0 => Settings.DeathmatchRounds.Value, 1 => Settings.DeathmatchKills.Value,
            2 => Settings.DeathmatchSeconds.Value, _ => Settings.DeathmatchFreeForAll.Value ? 0 : 1 },
            Settings.TrySelectDeathmatchRule, () => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid);
        // Item 1 of the 2026-10-06 follow-up ("own gear to the left"): a
        // mode-independent row driven entirely by Settings' own per-mode
        // dispatch (today only Zombie has a backing entry) plus the pure
        // ArenaEquipmentChoice table that decides whether it is offered at
        // all — same lock shape as every other frozen-at-arm menu choice.
        _equipmentChoice = new ArenaEquipmentChoiceState(
            Settings.GetEquipmentChoiceOwnGear,
            Settings.TrySelectEquipmentChoice,
            () => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid);
        try
        {
            Create(screen, template._elementTemplate, template._currentValueText ?? nativeText);
            _selection.CommitInitialSelection();
            _root.SetActive(true);
            Refresh();
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
        _tdmRules = null;
        _dmRules = null;
        _equipmentChoice = null;
        Safe(_dmRulesView.Close);
        Safe(_tdmRulesView.Close);
        Safe(_equipmentChoiceView.Close);
        Safe(_zombieRules.Close);
        foreach (var button in _buttons)
            Safe(() => { if (button != null) button.onClick.RemoveAllListeners(); });
        _buttons.Clear();
        Safe(_mutators.Close);
        _markers.Clear();
        var root = _root;
        _root = null;
        _rules = _respawn = _equipment = _subtitle = null;
        _description = null;
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
        rect.anchoredPosition = new Vector2(56f, -ArenaModeCatalog.ModePanelTop);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var background = _root.GetComponent<Image>();
        background.color = Hud.ArenaUiTheme.WithAlpha(Hud.ArenaUiTheme.Surface, .94f);
        background.raycastTarget = false;
        Hud.ArenaUiTheme.Decorate(rect);

        var title = CreateText("Title", typography, "ARENA", 26f, 16f, 32f);
        var subtitle = CreateText("Mode heading", typography,
            _selection.TacticalOnly ? "GAME MODE / PITFIRETEAM" : "GAME MODE", 14f, 51f, 22f);
        subtitle.color = Dim(typography, .65f);
        _subtitle = subtitle;

        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
            CreateRow(template, typography, index, 84f + index * 36f);

        CreateRule(274f, typography);
        _description = CreateText("Mode description", typography, string.Empty, 14f, 284f, 42f);
        _description.enableWordWrapping = true;
        _description.color = Dim(typography, .75f);
        _mutators.Create(screen, _root, typography, Refresh);
        _rules = CreateFact("OBJECTIVE", typography, 330f);
        _respawn = CreateFact("RESPAWN", typography, 360f);
        _equipment = CreateFact("EQUIPMENT", typography, 390f);
        _tdmRulesView.Show(_root.transform, template, typography, _tdmRules);
        _dmRulesView.Show(_root.transform, template, typography, _dmRules, Refresh);
        _equipmentChoiceView.Show(_root.transform, template, typography, _equipmentChoice);
        _zombieRules.Show(_root.transform, typography);
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
        SetRect((RectTransform)button.transform, Inset, top, PanelWidth - 2f * Inset, 34f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native mode-row label is missing.");
        label.text = ArenaModeCatalog.All[index].Label;
        label.fontSize = 19f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        Hud.ArenaUiTheme.StyleButton(button, label);
        SetRect(label.rectTransform, 14f, 0f, PanelWidth - 2f * Inset - 28f, 34f);

        var allowed = _selection.CanSelectIndex(index);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            // Only the LOCKED reason is a tooltip. HoverTooltipArea.SetUnlockStatus
            // (true) disables the component (decompile EFT.UI/HoverTooltipArea.cs:
            // 131-135), so an allowed row never receives pointer events and could
            // not show ArenaModeDefinition.Description anyway.
            tooltip.SetMessageText(allowed ? string.Empty : "Companions require Tactical or Zombies.", rawText: true);
            tooltip.SetUnlockStatus(allowed);
        }
        button.interactable = allowed;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        var marker = new GameObject("Selected", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        marker.transform.SetParent(button.transform, false);
        marker.transform.SetAsLastSibling();
        SetRect((RectTransform)marker.transform, 0f, 0f, 3f, 34f);
        var markerImage = marker.GetComponent<Image>();
        markerImage.color = Hud.ArenaUiTheme.Accent;
        markerImage.raycastTarget = false;
        _markers.Add(marker);
        var owner = _root;
        button.onClick.AddListener(() =>
        {
            if (!ReferenceEquals(owner, _root) || owner == null || !owner.activeInHierarchy
                || !_buttons.Contains(button) || ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) return;
            Select(index);
        });
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
        // Never hide these common facts behind a mode's limit controls. The
        // roster belongs in the mode label, and the short description explains
        // the selected variant; neither changes the objective's meaning.
        _rules.text = mode.Rules;
        bool zombieTeam = _selection.TacticalOnly && mode.Id == ArenaModeCatalog.ZombieId;
        _description.text = zombieTeam
            ? "Fallen allies return; team wipe ends play. All entry gear refills each wave."
            : mode.Id == ArenaModeCatalog.TacticalId && _selection.TacticalOnly
                ? "Eliminate the enemy squad. Fallen allies return next round."
            : mode.Id == ArenaModeCatalog.DeathmatchId && !Settings.DeathmatchFreeForAll.Value
                ? "All opponents hunt you. Set limits to end rounds."
            : mode.Description;
        ((RectTransform)_root.transform).sizeDelta = new Vector2(PanelWidth, PanelHeight);
        for (int i = 0; i < _buttons.Count; i++)
            if (ArenaModeCatalog.All[i].Id == ArenaModeCatalog.TacticalId)
                _buttons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = _selection.TacticalOnly
                    ? "TACTICAL / " + (Compat.PitFireTeamCompat.SelectedCompanionCount + 1) + "v5 PFT (WIP)"
                    : "TACTICAL / 1v" + Settings.TacticalSquadSize.Value + " (WIP)";
            else if (ArenaModeCatalog.All[i].Id == ArenaModeCatalog.ZombieId)
                _buttons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = _selection.TacticalOnly
                    ? "ZOMBIES / PFT (WIP)" : "ZOMBIES (WIP)";
        _respawn.text = zombieTeam ? "Next wave" : mode.Respawn;
        if (!_selection.TacticalOnly && mode.Respawn == "Instant" && Settings.GetReplaySeconds(mode.Id) > 0f
            && Spectator.KillcamReplayCompat.CanPlay(out _)) _respawn.text = "After killcam";
        // Same special-case line protected 5v5's TacticalOnly branch already
        // uses, generalized for item 1 of the 2026-10-06 follow-up ("own gear
        // to the left"): reads the pure table plus Settings' own per-mode
        // dispatch instead of a Zombie-only literal, so a future mode added to
        // ArenaEquipmentChoice needs no edit here. Self-contained (does not
        // depend on _equipmentChoice.Synchronize having run yet this frame).
        _equipment.text = _selection.TacticalOnly ? "PMC entry gear"
            : Modes.ArenaEquipmentChoice.IsOfferedFor(mode.Id) && Settings.GetEquipmentChoiceOwnGear(mode.Id)
                ? Modes.ArenaEquipmentChoice.OwnGearLabel
            : mode.Equipment;
        _mutators.Synchronize(_selection.SelectedMode, _selection.TacticalOnly);
        RefreshTdmRules();
    }

    private void RefreshTdmRules()
    {
        if (_selection == null || _tdmRules == null) return;
        _tdmRules.Synchronize(_selection.SelectedMode, _selection.TacticalOnly);
        _dmRules.Synchronize(_selection.SelectedMode, _selection.TacticalOnly);
        _equipmentChoice.Synchronize(_selection.SelectedMode, _selection.TacticalOnly);
        // The equipment selector occupies only the value column. Its label is
        // the same EQUIPMENT label every other mode uses, at the same height.
        _equipment.gameObject.SetActive(!_equipmentChoice.Visible);
        _dmRulesView.Refresh();
        _tdmRulesView.Refresh();
        _equipmentChoiceView.Refresh();
        _zombieRules.Synchronize(_selection.SelectedMode);
    }

    private float PanelHeight => ArenaModeCatalog.ModePanelHeight(_selection?.SelectedMode);

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
        text.color = Hud.ArenaUiTheme.Text;
        text.fontStyle = FontStyles.Normal;
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
        var color = Hud.ArenaUiTheme.Text;
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
