using Comfort.Common;
using ArenaMode.Modes;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// Owned area rows below Arena's mode panel. The adapter owns selection writes:
/// clicks never manufacture a selected state or change native map/settings data.
/// </summary>
internal sealed class ArenaAreaSelectionView
{
    private const float PanelWidth = 360f;
    private const float Inset = 20f;
    private const float RowTop = 36f;
    private const float RowHeight = 28f;
    private const float RowStride = 30f;
    private const int MaxRows = 4;
    private const string SelectMapPrompt = "Select a map to choose its area.";

    private sealed class Row
    {
        internal readonly string Id;
        internal readonly Button Button;
        internal readonly int Size;
        internal TextMeshProUGUI Label;
        internal GameObject Marker;

        internal Row(string id, Button button, int size = 0) { Id = id; Button = button; Size = size; }
    }

    private readonly List<Row> _rows = new(MaxRows);
    private readonly List<Row> _sizeRows = new(4);
    private readonly List<Row> _participantRows = new(2);
    private GameObject _root;
    private Button _template;
    private TMP_Text _typography;
    private TextMeshProUGUI _summary, _modeHint, _participantValue;
    private ArenaAreaSelectionState _state;
    private ArenaParticipantSelectionState _participants;
    private Func<string, bool> _onSelected;
    private Func<string, int, bool> _onTeamSizeSelected;
    private Func<ArenaParticipantSelectionState, int, bool> _onParticipantsSelected;
    private bool _dispatching, _layoutTactical, _layoutSummary, _layoutHint;
    private int _layoutRows = -1, _layoutSizes = -1, _layoutParticipants = -1;
    private float _layoutTop = -1f;

    internal GameObject Root => _root;

    public void Show(Transform screen, TMP_Text nativeText, Func<string, bool> onSelected,
        Func<string, int, bool> onTeamSizeSelected = null,
        Func<ArenaParticipantSelectionState, int, bool> onParticipantsSelected = null)
    {
        Close();
        var dropdown = MenuUI.Instance?.MatchmakerOfflineRaidScreen
            ?._raidSettingsWindow?._aiDifficultyDropdown;
        if (screen == null || dropdown?._elementTemplate == null
            || ItemUiContext.Instance == null || onSelected == null)
            throw new InvalidOperationException("The native area-row template or selection callback is unavailable.");

        _template = dropdown._elementTemplate;
        _typography = dropdown._currentValueText ?? nativeText;
        _onSelected = onSelected;
        _onTeamSizeSelected = onTeamSizeSelected;
        _onParticipantsSelected = onParticipantsSelected;
        try
        {
            Create(screen);
            Refresh(null, false);
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Refresh(ArenaAreaSelectionState state, bool tacticalOnly, ArenaParticipantSelectionState participants = null)
    {
        if (_root == null) return;
        try
        {
            ValidateOptions(state);
            _state = state;
            if (participants?.Available == true && (state?.Available != true
                || !string.Equals(participants.MapId, state.MapId, StringComparison.OrdinalIgnoreCase)
                || participants.ModeId != state.ModeId || participants.AreaId != state.SelectedId))
                throw new InvalidOperationException("Participant controls do not belong to the rendered area.");
            // A map/mode/area switch retires the actual native buttons. A queued
            // delegate from the previous context cannot acquire a new owner.
            if (_participantRows.Count > 0
                && (participants?.Available != true || _participants?.SameContext(participants) != true))
            {
                ClearRows(_participantRows);
                _layoutParticipants = -1;
            }
            _participants = participants;
            if (participants?.Available == true && _participantRows.Count == 0)
            {
                CreateRow(new ArenaAreaOption("minus", "-"), participantStep: -1);
                CreateRow(new ArenaAreaOption("plus", "+"), participantStep: 1);
            }
            // A periodic refresh may supply a new immutable state object with
            // the same choices. Preserve every row, native event and listener.
            if (!SameOptionIds(state))
            {
                ClearRows(_rows);
                if (state != null)
                    foreach (var option in state.Options) CreateRow(option);
            }

            int sizeCount = state?.TeamSizeOptions.Count ?? 0;
            bool sameSizes = _sizeRows.Count == sizeCount;
            for (int i = 0; sameSizes && i < sizeCount; i++) sameSizes = _sizeRows[i].Size == state.TeamSizeOptions[i];
            if (!sameSizes)
            {
                ClearRows(_sizeRows);
                if (state != null)
                    foreach (int size in state.TeamSizeOptions)
                        CreateRow(new ArenaAreaOption(size.ToString(), $"{size}v{size}"), size);
            }

            bool available = state?.Available == true;
            for (int index = 0; index < _rows.Count; index++)
            {
                Row row = _rows[index];
                SetText(row.Label, state.Options[index].Label);
                row.Button.interactable = available && _onSelected != null;
                bool selected = available && string.Equals(row.Id, state.SelectedId, StringComparison.Ordinal);
                SetActive(row.Marker, selected);
            }
            foreach (var row in _sizeRows)
            {
                row.Button.interactable = available && _onTeamSizeSelected != null;
                SetActive(row.Marker, row.Size == state.SelectedTeamSize);
            }
            bool participantChoice = participants?.Available == true;
            foreach (var row in _participantRows)
                row.Button.interactable = _onParticipantsSelected != null
                    && (row.Size < 0 ? participants.CanDecrease : participants.CanIncrease);
            SetText(_participantValue, participantChoice ? participants.ValueLabel : string.Empty);
            SetActive(_participantValue.gameObject, participantChoice);
            string summary = available ? state.Summary : SelectMapPrompt;
            if (available && tacticalOnly && Compat.PitFireTeamCompat.SelectedCompanionCount > 0)
            {
                int squadSize = Compat.PitFireTeamCompat.SelectedCompanionCount + 1;
                summary = state.ModeId == ArenaModeCatalog.ZombieId ? $"{squadSize}-player squad" : $"{squadSize}v5 - PitFireTeam";
            }
            SetText(_summary, participantChoice ? _rows.Count > 1 ? "PARTICIPANTS" : "PARTICIPANTS - you included" : summary);
            SetActive(_summary.gameObject, participantChoice || !string.IsNullOrEmpty(summary));
            SetText(_modeHint, participantChoice ? participants.RecommendationLabel + (_rows.Count > 1 ? " - you included" : "") : available ? state.ModeHint : string.Empty);
            SetActive(_modeHint.gameObject, participantChoice || available && !string.IsNullOrEmpty(state.ModeHint));
            UpdateLayout(tacticalOnly);
            SetActive(_root, true);
        }
        catch
        {
            // Never leave stale clickable choices visible after a drifted view
            // contract. Normal empty-map state is supported without throwing.
            Close();
            throw;
        }
    }

    public void Close()
    {
        _onSelected = null;
        _onTeamSizeSelected = null;
        _onParticipantsSelected = null;
        _state = null;
        _participants = null;
        _dispatching = false;
        var root = _root;
        _root = null;
        ClearRows(_rows);
        ClearRows(_sizeRows);
        ClearRows(_participantRows);
        _template = null;
        _typography = null;
        _summary = _modeHint = _participantValue = null;
        _layoutRows = -1;
        _layoutSizes = -1;
        _layoutParticipants = -1;
        Safe(() => { if (root != null) root.SetActive(false); });
        Safe(() => { if (root != null) UnityEngine.Object.Destroy(root); });
    }

    private void Create(Transform screen)
    {
        _root = new GameObject("Arena Area Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.SetActive(false);
        _root.transform.SetParent(screen, false);
        _root.transform.SetAsLastSibling();
        var background = _root.GetComponent<Image>();
        background.color = Hud.ArenaUiTheme.WithAlpha(Hud.ArenaUiTheme.Surface, .94f);
        background.raycastTarget = false;
        Hud.ArenaUiTheme.Decorate((RectTransform)_root.transform);

        var heading = CreateText("Area heading", "PLAY AREA", 14f, 14f, 22f);
        heading.color = Dim(_typography, .65f);
        _summary = CreateText("Team size", SelectMapPrompt, 17f, RowTop, 40f);
        _modeHint = CreateText("Mode restriction", string.Empty, 14f, 0f, 20f);
        _modeHint.color = Dim(_typography, .75f);
        _participantValue = CreateText("Participant value", string.Empty, 19f, 0f, RowHeight);
        _participantValue.gameObject.SetActive(false);
    }

    private void CreateRow(ArenaAreaOption option, int size = 0, int participantStep = 0)
    {
        // Clone only the dropdown's list-item prefab, never the dropdown owner
        // or context menu. Replace its UnityEvent so no native callback leaks.
        var button = UnityEngine.Object.Instantiate(_template, _root.transform, false);
        var row = new Row(option.Id, button, participantStep == 0 ? size : participantStep);
        var rows = participantStep != 0 ? _participantRows : size == 0 ? _rows : _sizeRows;
        rows.Add(row);
        button.name = (participantStep != 0 ? "Arena Participants " : size == 0 ? "Arena Area " : "Arena Team Size ") + row.Id;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        float width = participantStep != 0 ? 46f : size == 0 ? PanelWidth - 2f * Inset : 74f;
        SetRect((RectTransform)button.transform, size == 0 ? Inset : Inset + (rows.Count - 1) * 82f,
            size == 0 ? RowTop + (rows.Count - 1) * RowStride : RowTop + _rows.Count * RowStride + 6f,
            width, RowHeight);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        row.Label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native area-row label is missing.");
        row.Label.text = option.Label;
        row.Label.fontSize = 18f;
        row.Label.enableWordWrapping = false;
        row.Label.overflowMode = TextOverflowModes.Ellipsis;
        row.Label.alignment = TextAlignmentOptions.MidlineLeft;
        row.Label.raycastTarget = false;
        Hud.ArenaUiTheme.StyleButton(button, row.Label);
        SetRect(row.Label.rectTransform, 14f, 0f, width - 28f, RowHeight);

        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
        {
            tooltip.SetMessageText(string.Empty, rawText: true);
            tooltip.SetUnlockStatus(true);
        }
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        row.Marker = new GameObject("Selected", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        row.Marker.transform.SetParent(button.transform, false);
        row.Marker.transform.SetAsLastSibling();
        SetRect((RectTransform)row.Marker.transform, 0f, 0f, 3f, RowHeight);
        var markerImage = row.Marker.GetComponent<Image>();
        markerImage.color = Hud.ArenaUiTheme.Accent;
        markerImage.raycastTarget = false;
        row.Marker.SetActive(false);
        // Stable identity plus row ownership, never a mutable option index.
        button.onClick.AddListener(() =>
        {
            if (participantStep != 0) SelectParticipants(row);
            else if (size == 0) Select(row);
            else SelectTeamSize(row);
        });
        button.gameObject.SetActive(true);
    }

    private void SelectParticipants(Row row)
    {
        if (_dispatching || _root == null || !_root.activeInHierarchy
            || _participants?.Available != true || _onParticipantsSelected == null
            || !_participantRows.Contains(row)
            || (row.Size < 0 ? !_participants.CanDecrease : !_participants.CanIncrease)) return;
        var owner = _root;
        var rendered = _participants;
        _dispatching = true;
        try
        {
            bool accepted = _onParticipantsSelected(rendered, rendered.Total + row.Size * rendered.Step);
            if (accepted && ReferenceEquals(owner, _root) && owner != null && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena participant selection could not be saved: {ex.Message}"); }
        finally { if (ReferenceEquals(owner, _root)) _dispatching = false; }
    }

    private void SelectTeamSize(Row row)
    {
        if (_dispatching || _root == null || !_root.activeInHierarchy
            || _state?.HasTeamSizeChoice != true || _onTeamSizeSelected == null || !_sizeRows.Contains(row)) return;
        var owner = _root;
        var areaId = _state.SelectedId;
        _dispatching = true;
        try
        {
            bool accepted = _onTeamSizeSelected(areaId, row.Size);
            if (accepted && ReferenceEquals(owner, _root) && owner != null && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena team-size selection could not be saved: {ex.Message}"); }
        finally { if (ReferenceEquals(owner, _root)) _dispatching = false; }
    }

    private void Select(Row row)
    {
        if (_dispatching || _root == null || !_root.activeInHierarchy
            || _state?.Available != true || _onSelected == null || !_rows.Contains(row)) return;
        var owner = _root;
        var callback = _onSelected;
        _dispatching = true;
        try
        {
            // The callback may synchronously Refresh or Close/Show the panel.
            // Only those pushed states change markers, including after refusal.
            bool accepted = callback(row.Id);
            if (accepted && ReferenceEquals(owner, _root) && owner != null
                && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena area selection could not be saved: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(owner, _root)) _dispatching = false;
        }
    }

    private bool SameOptionIds(ArenaAreaSelectionState state)
    {
        int count = state?.Options.Count ?? 0;
        if (_rows.Count != count) return false;
        for (int index = 0; index < count; index++)
            if (!string.Equals(_rows[index].Id, state.Options[index].Id, StringComparison.Ordinal)) return false;
        return true;
    }

    private static void ValidateOptions(ArenaAreaSelectionState state)
    {
        if (state == null) return;
        if (state.Options.Count > MaxRows)
            throw new InvalidOperationException("The area panel supports at most four visible choices.");
        for (int index = 0; index < state.Options.Count; index++)
        {
            var option = state.Options[index];
            if (option == null || string.IsNullOrWhiteSpace(option.Id))
                throw new InvalidOperationException("An area option has no stable identity.");
            for (int previous = 0; previous < index; previous++)
                if (string.Equals(state.Options[previous].Id, option.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Area options repeat the same identity.");
        }
    }

    private void UpdateLayout(bool tacticalOnly)
    {
        // PFT can select Zombies too; its three wave sliders need the same
        // clearance as the solo Zombie panel, not Tactical's shorter footprint.
        float top = ArenaModeCatalog.ModePanelTop + ArenaModeCatalog.ModePanelHeight(_state?.ModeId) + 12f;
        bool showSummary = _summary.gameObject.activeSelf, showHint = _modeHint.gameObject.activeSelf;
        if (_layoutRows == _rows.Count && _layoutSizes == _sizeRows.Count && _layoutParticipants == _participantRows.Count
            && _layoutTactical == tacticalOnly && _layoutTop == top
            && _layoutSummary == showSummary && _layoutHint == showHint) return;
        _layoutTop = top;
        _layoutRows = _rows.Count;
        _layoutTactical = tacticalOnly;
        _layoutSizes = _sizeRows.Count;
        _layoutParticipants = _participantRows.Count;
        _layoutSummary = showSummary;
        _layoutHint = showHint;
        float summaryTop = _rows.Count == 0 ? RowTop : RowTop + _rows.Count * RowStride + 6f + (_sizeRows.Count == 0 ? 0f : 34f);
        bool participantChoice = _participantRows.Count > 0;
        // Customs has three choices even for Deathmatch's taller rules panel.
        // Put its participant stepper beside the label, leaving READY clear.
        bool compactParticipants = participantChoice && _rows.Count > 1;
        float height = _rows.Count == 0 ? 100f : summaryTop + (participantChoice ? compactParticipants ? 50f : 80f
            : (showSummary ? 24f : 0f) + (showHint ? 20f : 0f) + 8f);
        // Share the actual selected mode's footprint. Deathmatch has four
        // rules rows; a same-area mode change must invalidate this position.
        SetRect((RectTransform)_root.transform, 56f, top, PanelWidth, height);
        SetRect(_summary.rectTransform, Inset, summaryTop, PanelWidth - 2f * Inset,
            _rows.Count == 0 ? 40f : 22f);
        _summary.enableWordWrapping = _rows.Count == 0;
        _summary.fontSize = participantChoice ? 15f : 17f;
        SetRect(_modeHint.rectTransform, Inset, summaryTop + (participantChoice ? 56f : showSummary ? 24f : 0f), PanelWidth - 2f * Inset, 20f);
        if (participantChoice)
        {
            SetRect((RectTransform)_participantRows[0].Button.transform, Inset, summaryTop + 24f, 46f, RowHeight);
            SetRect((RectTransform)_participantRows[1].Button.transform, PanelWidth - Inset - 46f, summaryTop + 24f, 46f, RowHeight);
            SetRect(_participantValue.rectTransform, Inset + 64f, summaryTop + 24f, PanelWidth - 2f * Inset - 128f, RowHeight);
            if (compactParticipants)
            {
                SetRect(_summary.rectTransform, Inset, summaryTop, 148f, RowHeight);
                SetRect((RectTransform)_participantRows[0].Button.transform, 178f, summaryTop, 46f, RowHeight);
                SetRect(_participantValue.rectTransform, 228f, summaryTop, 58f, RowHeight);
                SetRect((RectTransform)_participantRows[1].Button.transform, 294f, summaryTop, 46f, RowHeight);
                SetRect(_modeHint.rectTransform, Inset, summaryTop + 30f, PanelWidth - 2f * Inset, 20f);
            }
        }
    }

    private void ClearRows(List<Row> rows)
    {
        var retired = rows.ToArray();
        rows.Clear();
        foreach (var row in retired)
        {
            Safe(() => { if (row.Button != null) row.Button.onClick.RemoveAllListeners(); });
            Safe(() => { if (row.Button != null) row.Button.gameObject.SetActive(false); });
            Safe(() => { if (row.Button != null) UnityEngine.Object.Destroy(row.Button.gameObject); });
        }
    }

    private TextMeshProUGUI CreateText(string name, string value, float size, float top, float height)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(_root.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = _typography?.font ?? TMP_Settings.defaultFontAsset;
        if (_typography != null)
        {
            text.fontSharedMaterial = _typography.fontSharedMaterial;
            text.color = _typography.color;
            text.fontStyle = _typography.fontStyle;
            text.characterSpacing = _typography.characterSpacing;
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

    private static void SetText(TMP_Text text, string value)
    {
        value ??= string.Empty;
        if (text.text != value) text.text = value;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active) target.SetActive(active);
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
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena area panel cleanup: {ex.Message}"); }
    }
}
