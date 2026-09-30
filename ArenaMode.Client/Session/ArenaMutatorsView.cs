using ArenaMode.Hud;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Always-visible, screen-owned feature panel; no modal or global input hook.</summary>
internal sealed class ArenaMutatorsView
{
    private sealed class Row
    {
        internal ArenaMutator Id;
        internal Button Button;
        internal TextMeshProUGUI Label, Status, Description;
        internal Image Marker, Chip;
    }
    private readonly ArenaMutatorSelection _selection = new();
    private readonly List<Row> _rows = new();
    private GameObject _root, _owner;
    private Transform _screen;
    private TextMeshProUGUI _subtitle, _note;
    private Action _changed;
    private bool _dispatching;

    internal void Create(Transform screen, GameObject owner, TMP_Text typography, Action changed)
    {
        Close();
        _owner = owner; _screen = screen; _changed = changed;
        try
        {
            _root = new GameObject("Arena Mutators", typeof(RectTransform), typeof(Image));
            _root.SetActive(false);
            _root.transform.SetParent(screen, false);
            _root.GetComponent<Image>().color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .96f);
            _root.GetComponent<Image>().raycastTarget = false;
            ArenaUiTheme.Decorate((RectTransform)_root.transform);
            Text(_root.transform, typography, "Title", "MUTATORS", 24f, 20f, 14f, 420f, 30f);
            _subtitle = Text(_root.transform, typography, "Mutator legend", "Green: ON   /   Grey: OFF   /   N/A: unavailable", 13f, 20f, 48f, 420f, 24f);
            var generation = _root;
            for (int index = 0; index < 8; index++)
            {
                var id = (ArenaMutator)index;
                // Fresh controls deliberately avoid native prefab label animations.
                var go = new GameObject("Arena Mutator " + id, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_root.transform, false);
                var button = go.GetComponent<Button>();
                go.GetComponent<Image>().color = ArenaUiTheme.Raised;
                go.GetComponent<Image>().raycastTarget = true;
                button.targetGraphic = go.GetComponent<Image>();
                var label = Text(go.transform, typography, "Mutator title " + id,
                    ArenaMutatorSelection.Title(id), 15f, 12f, 0f, 200f, 32f);
                ArenaUiTheme.StyleButton(button, label);
                var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
                var marker = new GameObject("Mutator marker " + id, typeof(RectTransform), typeof(Image));
                marker.transform.SetParent(go.transform, false);
                Rect((RectTransform)marker.transform, 0f, 0f, 3f, 32f);
                marker.GetComponent<Image>().raycastTarget = false;
                var chip = new GameObject("Mutator chip " + id, typeof(RectTransform), typeof(Image));
                chip.transform.SetParent(go.transform, false);
                chip.GetComponent<Image>().raycastTarget = false;
                var row = new Row { Id = id, Button = button, Label = label,
                    Marker = marker.GetComponent<Image>(), Chip = chip.GetComponent<Image>(),
                    Status = Text(go.transform, typography, "Mutator status " + id, "", 14f, 220f, 0f, 52f, 32f),
                    Description = Text(_root.transform, typography, "Mutator detail " + id, "", 13f, 0f, 0f, 272f, 34f) };
                row.Status.alignment = TextAlignmentOptions.Center;
                _rows.Add(row);
                button.onClick.AddListener(() => Toggle(generation, row));
            }
            _note = Text(_root.transform, typography, "Mutators saved note",
                "Same settings as F12. Killcam: this mode only.\nOther switches apply to all Arena modes.",
                13f, 20f, 370f, 600f, 30f);
            Layout();
        }
        catch { Close(); throw; }
    }

    private void Layout()
    {
        if (_root == null) return;
        ArenaMapPanelLayout.Place((RectTransform)_root.transform, _screen,
            ArenaMapPanelLayout.MutatorsTop, ArenaMapPanelLayout.MutatorsHeight);
        float width = ArenaMapPanelLayout.Width(_screen), cell = (width - 60f) / 2f;
        _subtitle.rectTransform.sizeDelta = new Vector2(width - 40f, 24f);
        _note.rectTransform.sizeDelta = new Vector2(width - 40f, 30f);
        for (int index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            float left = 20f + (index < 4 ? 0f : cell + 20f), top = 84f + (index % 4) * 70f;
            Rect((RectTransform)row.Button.transform, left, top, cell, 32f);
            Rect(row.Label.rectTransform, 12f, 0f, cell - 74f, 32f);
            Rect(row.Status.rectTransform, cell - 55f, 0f, 50f, 32f);
            Rect((RectTransform)row.Chip.transform, cell - 55f, 4f, 50f, 24f);
            Rect(row.Description.rectTransform, left + 8f, top + 34f, cell - 16f, 34f);
        }
    }

    internal void Synchronize(string mode, bool team)
    {
        _selection.Synchronize(mode, team);
        if (_root == null) return;
        _root.SetActive(_owner != null && _owner.activeInHierarchy && !_selection.Locked);
        Layout();
        foreach (var row in _rows)
        {
            string reason = _selection.UnavailableReason(row.Id);
            bool on = reason == null && _selection.Enabled(row.Id);
            row.Status.text = reason != null ? "N/A" : on ? "ON" : "OFF";
            row.Button.interactable = reason == null && !_selection.Locked;
            row.Label.color = reason != null ? ArenaUiTheme.Muted : ArenaUiTheme.Text;
            row.Status.color = on ? ArenaUiTheme.Positive : ArenaUiTheme.Muted;
            row.Marker.color = on ? ArenaUiTheme.Positive : ArenaUiTheme.WithAlpha(ArenaUiTheme.Muted, .35f);
            row.Chip.color = on ? new Color(.08f, .25f, .16f, 1f) : ArenaUiTheme.Surface;
            row.Description.text = reason ?? ArenaMutatorSelection.Description(row.Id);
        }
    }

    private bool Owns(GameObject generation) => ReferenceEquals(generation, _root)
        && _root != null && _root.activeInHierarchy && _owner != null && _owner.activeInHierarchy;

    private void Toggle(GameObject generation, Row row)
    {
        if (!Owns(generation) || !_rows.Contains(row) || _dispatching) return;
        _dispatching = true;
        try
        {
            bool accepted = _selection.TryToggle(row.Id);
            if (!Owns(generation)) return;
            _changed?.Invoke();
            if (accepted && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena mutator selection failed: {ex.Message}"); }
        finally { if (ReferenceEquals(generation, _root)) _dispatching = false; }
    }

    internal void Close()
    {
        foreach (var row in _rows) if (row.Button != null) row.Button.onClick.RemoveAllListeners();
        _rows.Clear();
        _owner = null; _screen = null; _changed = null; _subtitle = _note = null;
        _dispatching = false;
        var root = _root; _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_Text source, string name, string value,
        float size, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) { text.fontSharedMaterial = source.fontSharedMaterial; text.characterSpacing = source.characterSpacing; }
        text.text = value; text.fontSize = size; text.fontStyle = FontStyles.Normal;
        text.color = size >= 15f ? ArenaUiTheme.Text : ArenaUiTheme.Muted;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = size < 15f; text.raycastTarget = false;
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
}
