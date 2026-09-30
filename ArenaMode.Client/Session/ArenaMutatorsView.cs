using ArenaMode.Hud;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Screen-owned, click-away panel. Native screen navigation keeps its lifetime.</summary>
internal sealed class ArenaMutatorsView
{
    private sealed class Row
    {
        internal ArenaMutator Id;
        internal Button Button;
        internal TextMeshProUGUI Label, Description;
    }
    private readonly ArenaMutatorSelection _selection = new();
    private readonly List<Row> _rows = new();
    private readonly List<Button> _buttons = new();
    private GameObject _root, _owner;
    private TextMeshProUGUI _subtitle;
    private Action _changed;
    private bool _dispatching;

    internal void Create(Transform screen, GameObject owner, Button template, TMP_Text typography, Action changed)
    {
        Close();
        _owner = owner;
        _changed = changed;
        try
        {
            _root = new GameObject("Arena Mutators", typeof(RectTransform));
            _root.SetActive(false);
            _root.transform.SetParent(screen, false);
            Stretch((RectTransform)_root.transform);
            var backdrop = new GameObject("Arena Mutators backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(_root.transform, false);
            Stretch((RectTransform)backdrop.transform);
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, .65f);
            backdrop.GetComponent<Image>().raycastTarget = true;
            var dismiss = backdrop.GetComponent<Button>();
            dismiss.targetGraphic = backdrop.GetComponent<Image>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.interactable = true;
            _buttons.Add(dismiss);
            var generation = _root;
            dismiss.onClick.AddListener(() => { if (Owns(generation)) Hide(); });

            var panel = new GameObject("Arena Mutators panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(760f, 510f);
            panel.GetComponent<Image>().color = ArenaUiTheme.Surface;
            panel.GetComponent<Image>().raycastTarget = true;
            ArenaUiTheme.Decorate(rect);
            Text(panel.transform, typography, "Title", "MUTATORS", 26f, 24f, 18f, 510f, 34f);
            _subtitle = Text(panel.transform, typography, "Mode", string.Empty, 14f, 24f, 55f, 690f, 25f);
            Text(panel.transform, typography, "Movement heading", "MOVEMENT & EQUIPMENT", 14f, 24f, 96f, 340f, 24f);
            Text(panel.transform, typography, "Effects heading", "MATCH & EFFECTS", 14f, 396f, 96f, 340f, 24f);
            for (int index = 0; index < 8; index++)
            {
                var id = (ArenaMutator)index;
                float left = index < 4 ? 24f : 396f;
                float top = 128f + (index % 4) * 78f;
                var button = MakeButton(panel.transform, template, "Arena Mutator " + id, left, top, 340f, 32f);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                var row = new Row { Id = id, Button = button, Label = label,
                    Description = Text(panel.transform, typography, "Mutator detail " + id,
                        string.Empty, 13f, left + 8f, top + 33f, 324f, 38f) };
                _rows.Add(row);
                button.onClick.AddListener(() => Toggle(generation, row));
            }
            Text(panel.transform, typography, "Saved note",
                "Saved immediately. Same settings as F12.\nKillcam: this mode only. Other switches: all Arena modes.",
                13f, 24f, 451f, 540f, 42f);
            var close = MakeButton(panel.transform, template, "Arena Mutators close", 610f, 456f, 126f, 32f);
            close.GetComponentInChildren<TextMeshProUGUI>(true).text = "CLOSE";
            close.onClick.AddListener(() => { if (Owns(generation)) Hide(); });
        }
        catch { Close(); throw; }
    }

    internal void Show()
    {
        if (_root == null || _owner == null || !_owner.activeInHierarchy || _selection.Locked) return;
        _root.transform.SetAsLastSibling();
        _root.SetActive(true);
        Refresh();
    }

    internal void Synchronize(string mode, bool team)
    {
        _selection.Synchronize(mode, team);
        if (_selection.Locked || _owner == null || !_owner.activeInHierarchy) Hide();
        Refresh();
    }

    private void Refresh()
    {
        if (_root == null) return;
        _subtitle.text = _selection.Mode.ToUpperInvariant() + " / ARENA FEATURES";
        foreach (var row in _rows)
        {
            string reason = _selection.UnavailableReason(row.Id);
            row.Label.text = ArenaMutatorSelection.Title(row.Id) + ": "
                + (reason != null ? "N/A" : _selection.Enabled(row.Id) ? "ON" : "OFF");
            row.Button.interactable = reason == null && !_selection.Locked;
            row.Label.color = reason != null ? ArenaUiTheme.Muted
                : _selection.Enabled(row.Id) ? ArenaUiTheme.Positive : ArenaUiTheme.Text;
            row.Description.text = reason ?? ArenaMutatorSelection.Description(row.Id);
        }
    }

    private bool Owns(GameObject generation) => ReferenceEquals(generation, _root)
        && _root != null && _root.activeInHierarchy && _owner != null && _owner.activeInHierarchy;

    private void Toggle(GameObject generation, Row row)
    {
        // Old dispatch-stack callbacks must not target a new screen's configuration.
        if (!Owns(generation) || !_rows.Contains(row) || _dispatching) return;
        _dispatching = true;
        try
        {
            bool accepted = _selection.TryToggle(row.Id);
            if (!Owns(generation)) return;
            Refresh();
            _changed?.Invoke();
            if (accepted && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena mutator selection failed: {ex.Message}"); }
        finally { if (ReferenceEquals(generation, _root)) _dispatching = false; }
    }

    private void Hide() { if (_root != null) _root.SetActive(false); }

    internal void Close()
    {
        foreach (var button in _buttons) if (button != null) button.onClick.RemoveAllListeners();
        _buttons.Clear();
        _rows.Clear();
        _owner = null;
        _changed = null;
        _subtitle = null;
        _dispatching = false;
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private Button MakeButton(Transform parent, Button template, string name, float left, float top, float width, float height)
    {
        var button = UnityEngine.Object.Instantiate(template, parent, false);
        _buttons.Add(button);
        button.name = name;
        button.onClick = new Button.ButtonClickedEvent();
        button.transform.localScale = Vector3.one;
        Rect((RectTransform)button.transform, left, top, width, height);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
            ?? throw new InvalidOperationException("Native mutator row label missing.");
        label.fontSize = 16f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        Rect(label.rectTransform, 10f, 0f, width - 20f, height);
        ArenaUiTheme.StyleButton(button, label);
        var tooltip = button.GetComponent<HoverTooltipArea>();
        if (tooltip != null) { tooltip.SetMessageText(string.Empty, rawText: true); tooltip.SetUnlockStatus(true); }
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.interactable = true;
        button.gameObject.SetActive(true);
        return button;
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_Text source, string name, string value,
        float size, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) { text.fontSharedMaterial = source.fontSharedMaterial; text.characterSpacing = source.characterSpacing; }
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.color = size >= 20f ? ArenaUiTheme.Text : ArenaUiTheme.Muted;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Rect(text.rectTransform, left, top, width, height);
        return text;
    }

    private static void Rect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
