using Comfort.Common;
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
    private const float RowTop = 44f;
    private const float RowHeight = 34f;
    private const float RowStride = 38f;
    private const int MaxRows = 2;
    private const string SelectMapPrompt = "Select a map to choose its area.";

    private sealed class Row
    {
        internal readonly string Id;
        internal readonly Button Button;
        internal TextMeshProUGUI Label;
        internal GameObject Marker;

        internal Row(string id, Button button) { Id = id; Button = button; }
    }

    private readonly List<Row> _rows = new(MaxRows);
    private GameObject _root;
    private Button _template;
    private TMP_Text _typography;
    private TextMeshProUGUI _summary, _modeHint;
    private ArenaAreaSelectionState _state;
    private Func<string, bool> _onSelected;
    private bool _dispatching, _layoutTactical;
    private int _layoutRows = -1;

    internal GameObject Root => _root;

    public void Show(Transform screen, TMP_Text nativeText, Func<string, bool> onSelected)
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

    public void Refresh(ArenaAreaSelectionState state, bool tacticalOnly)
    {
        if (_root == null) return;
        try
        {
            ValidateOptions(state);
            _state = state;
            // A periodic refresh may supply a new immutable state object with
            // the same choices. Preserve every row, native event and listener.
            if (!SameOptionIds(state))
            {
                ClearRows();
                if (state != null)
                    foreach (var option in state.Options) CreateRow(option);
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
            SetText(_summary, available ? state.Summary : SelectMapPrompt);
            SetText(_modeHint, available ? state.ModeHint : string.Empty);
            SetActive(_modeHint.gameObject, available && !string.IsNullOrEmpty(state.ModeHint));
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
        _state = null;
        _dispatching = false;
        var root = _root;
        _root = null;
        ClearRows();
        _template = null;
        _typography = null;
        _summary = _modeHint = null;
        _layoutRows = -1;
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
        background.color = new Color(0f, 0f, 0f, .68f);
        background.raycastTarget = false;

        var heading = CreateText("Area heading", "PLAY AREA", 14f, 14f, 22f);
        heading.color = Dim(_typography, .65f);
        _summary = CreateText("Team size", SelectMapPrompt, 17f, RowTop, 40f);
        _modeHint = CreateText("Mode restriction", string.Empty, 14f, 0f, 20f);
        _modeHint.color = Dim(_typography, .75f);
    }

    private void CreateRow(ArenaAreaOption option)
    {
        // Clone only the dropdown's list-item prefab, never the dropdown owner
        // or context menu. Replace its UnityEvent so no native callback leaks.
        var button = UnityEngine.Object.Instantiate(_template, _root.transform, false);
        var row = new Row(option.Id, button);
        _rows.Add(row);
        button.name = "Arena Area " + row.Id;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        SetRect((RectTransform)button.transform, Inset, RowTop + (_rows.Count - 1) * RowStride,
            PanelWidth - 2f * Inset, RowHeight);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        row.Label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("The native area-row label is missing.");
        row.Label.text = option.Label;
        row.Label.fontSize = 20f;
        row.Label.enableWordWrapping = false;
        row.Label.overflowMode = TextOverflowModes.Ellipsis;
        row.Label.alignment = TextAlignmentOptions.MidlineLeft;
        row.Label.raycastTarget = false;
        SetRect(row.Label.rectTransform, 14f, 0f, PanelWidth - 2f * Inset - 28f, RowHeight);

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
        row.Marker.transform.SetAsFirstSibling();
        SetRect((RectTransform)row.Marker.transform, 0f, 0f, 3f, RowHeight);
        var markerImage = row.Marker.GetComponent<Image>();
        markerImage.color = _typography != null ? _typography.color : Color.white;
        markerImage.raycastTarget = false;
        row.Marker.SetActive(false);
        // Stable identity plus row ownership, never a mutable option index.
        button.onClick.AddListener(() => Select(row));
        button.gameObject.SetActive(true);
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
            throw new InvalidOperationException("The area panel supports at most two visible choices.");
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
        if (_layoutRows == _rows.Count && _layoutTactical == tacticalOnly) return;
        _layoutRows = _rows.Count;
        _layoutTactical = tacticalOnly;
        float summaryTop = _rows.Count == 0 ? RowTop : RowTop + _rows.Count * RowStride + 8f;
        float height = _rows.Count == 0 ? 100f : summaryTop + 60f;
        // Mode panel: top 168, height 488/524. Keep a 12px gap, and at most 188px
        // height, ending at 856/892 in the native 1080-unit reference layout.
        SetRect((RectTransform)_root.transform, 56f, tacticalOnly ? 704f : 668f, PanelWidth, height);
        SetRect(_summary.rectTransform, Inset, summaryTop, PanelWidth - 2f * Inset,
            _rows.Count == 0 ? 40f : 22f);
        _summary.enableWordWrapping = _rows.Count == 0;
        SetRect(_modeHint.rectTransform, Inset, summaryTop + 26f, PanelWidth - 2f * Inset, 20f);
    }

    private void ClearRows()
    {
        var retired = _rows.ToArray();
        _rows.Clear();
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
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena area panel cleanup: {ex.Message}"); }
    }
}
