using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>One slot row of the kit editor.</summary>
internal sealed class ArenaKitEditorRow
{
    internal string Slot;
    internal string Label;
    internal string ItemName;
    internal string Detail;
    /// <summary>A detached item for the row's picture; null for none.</summary>
    internal Func<Item> Picture;
    internal bool CanChange;
    internal bool CanClear;
    internal bool Active;
}

/// <summary>One entry of the editor's pick list.</summary>
internal sealed class ArenaKitEditorChoice
{
    internal string Key;
    internal string Name;
    internal string Detail;
    internal Func<Item> Picture;
    internal bool Current;
}

/// <summary>Everything the editor draws, decided by the kit screen.</summary>
internal sealed class ArenaKitEditorModel
{
    internal string Name;
    internal string PriceLine;
    internal string CapLine;
    internal string Status;
    internal bool StatusIsError;
    internal bool Locked;
    internal bool CanRename;
    internal string DeleteLabel;
    internal readonly List<ArenaKitEditorRow> Rows = new();
    internal string ListTitle;
    /// <summary>The pick list; null when no slot is being changed.</summary>
    internal List<ArenaKitEditorChoice> Choices;
    internal string ListEmpty;
    internal readonly List<KeyValuePair<string, string>> Facts = new();
}

/// <summary>
/// The kit editor of the kit screen (docs/reviews/CUSTOM-KITS-20261110.md): the kit's name, its
/// slot rows with the game's own pictures, and, while a slot is being changed, the list of items
/// that slot takes. It draws what the kit screen gives it and reports clicks; it never reads a
/// setting, the kit file or a template itself. Pictures are requested two per frame and switch off
/// on the first failure, like the browser's.
/// </summary>
internal sealed class ArenaKitEditorView
{
    private const float LeftWidth = 700f;
    private const float RowTop = 132f;
    private const float RowHeight = 66f;
    private const float ChoiceHeight = 58f;
    private const int IconsPerFrame = 2;

    private sealed class Pump : MonoBehaviour
    {
        internal Action Tick;
        private void Update() => Tick?.Invoke();
    }

    private sealed class PendingIcon
    {
        internal Image Image;
        internal Func<Item> Picture;
        internal float Left, Top, Width, Height;
    }

    private static readonly Color ErrorColour = new Color(1f, .45f, .40f, 1f);

    private readonly Queue<PendingIcon> _icons = new();
    private ArenaKitUiStyle _style;
    private RectTransform _root, _left, _right, _content;
    private float _width, _height;
    private TMP_InputField _name;
    private TextMeshProUGUI _status;
    private string _shownName, _listTitle;
    private bool _nameFailed;
    private bool _iconsFailed;

    internal Action<string> Rename;
    internal Action<string> Change;
    internal Action<string> Clear;
    internal Action<string> Pick;
    internal Action CancelPick;
    internal Action Done;
    internal Action Copy;
    internal Action Delete;

    internal bool Open => _root != null;

    internal void Create(Transform parent, ArenaKitUiStyle style, float left, float top, float width, float height)
    {
        Close();
        _style = style ?? ArenaKitUiStyle.Hud();
        _width = width;
        _height = height;
        _root = ArenaKitUi.Box(parent, "Arena Kit Editor", left, top, width, height);
        _root.gameObject.AddComponent<Pump>().Tick = PumpIcons;
    }

    internal void Close()
    {
        _icons.Clear();
        _name = null;
        _status = null;
        _shownName = _listTitle = null;
        var root = _root;
        _root = _left = _right = _content = null;
        if (root != null)
        {
            root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    /// <summary>Draws <paramref name="model"/>; the editor is rebuilt as a whole, it holds at most a few hundred rows.</summary>
    internal void Show(ArenaKitEditorModel model)
    {
        if (_root == null || model == null) return;
        // The same list drawn again (a refused pick) keeps where the player had scrolled to.
        Vector2 scrolled = _content != null && model.Choices != null && string.Equals(_listTitle, model.ListTitle, StringComparison.Ordinal)
            ? _content.anchoredPosition : Vector2.zero;
        _icons.Clear();
        _name = null;
        _status = null;
        _content = null;
        _listTitle = null;
        DestroyChildren(_root);
        _left = ArenaKitUi.Box(_root, "Kit", 0f, 0f, LeftWidth, _height);
        _right = ArenaKitUi.Box(_root, "Choices", LeftWidth + 30f, 0f, _width - LeftWidth - 30f, _height);

        BuildHead(model);
        for (int index = 0; index < model.Rows.Count; index++)
            BuildRow(model.Rows[index], RowTop + index * RowHeight, model.Locked);

        Button done = ArenaKitUi.Button(_left, "Arena Kit Editor Done", _style, "DONE", 15f, 0f, _height - 34f, 130f, 34f, out _);
        done.onClick.AddListener(() => Done?.Invoke());
        Button copy = ArenaKitUi.Button(_left, "Arena Kit Editor Copy", _style, "COPY", 14f, 140f, _height - 34f, 110f, 34f, out _);
        copy.interactable = !model.Locked;
        copy.onClick.AddListener(() => Copy?.Invoke());
        Button delete = ArenaKitUi.Button(_left, "Arena Kit Editor Delete", _style, model.DeleteLabel ?? "DELETE", 13f, 260f, _height - 34f, 190f, 34f, out _);
        delete.interactable = !model.Locked;
        delete.onClick.AddListener(() => Delete?.Invoke());

        if (model.Choices != null) BuildChoices(model, scrolled);
        else BuildFacts(model);
    }

    /// <summary>
    /// The name and the status line changed in place, nothing rebuilt. A rename is saved from the
    /// name field's end of editing, which the input module raises on the very press that takes the
    /// focus away, before it hands that press to the button under the pointer; rebuilding there
    /// would destroy the DONE or CHANGE button the player is clicking, and the click would be lost.
    /// </summary>
    internal void ShowRenamed(string name, string status, bool error)
    {
        if (_root == null) return;
        if (name != null)
        {
            _shownName = name;
            if (_name != null && !string.Equals(_name.text, name, StringComparison.Ordinal)) _name.text = name;
        }

        if (_status != null)
        {
            _status.text = status ?? string.Empty;
            _status.color = error ? ErrorColour : ArenaUiTheme.Text;
        }
    }

    private void BuildHead(ArenaKitEditorModel model)
    {
        ArenaKitUi.Text(_left, "Name label", _style, "NAME", 12f, ArenaUiTheme.Muted, TextAlignmentOptions.MidlineLeft, 0f, 0f, 80f, 34f);
        if (model.CanRename && !model.Locked && !_nameFailed)
        {
            try { _name = NameField(model.Name); }
            catch (Exception ex)
            {
                _nameFailed = true;
                Plugin.ModLogger.LogWarning($"Arena kit editor: the name field is unavailable, kits keep their names: {ex.Message}");
            }
        }

        if (_name == null)
            ArenaKitUi.Text(_left, "Name", _style, model.Name ?? string.Empty, 22f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 84f, 0f, LeftWidth - 84f, 34f);

        ArenaKitUi.Text(_left, "Price", _style, model.PriceLine ?? string.Empty, 17f, ArenaUiTheme.Accent, TextAlignmentOptions.MidlineLeft, 0f, 44f, LeftWidth, 24f);
        ArenaKitUi.Text(_left, "Cap", _style, model.CapLine ?? string.Empty, 13f, ArenaUiTheme.Muted, TextAlignmentOptions.MidlineLeft, 0f, 70f, LeftWidth, 20f);
        _status = ArenaKitUi.Text(_left, "Status", _style, model.Status ?? string.Empty, 13f,
            model.StatusIsError ? ErrorColour : ArenaUiTheme.Text, TextAlignmentOptions.TopLeft, 0f, 94f, LeftWidth, 34f);
        _status.enableWordWrapping = true;
        _shownName = model.Name;
    }

    private TMP_InputField NameField(string value)
    {
        Image surface = ArenaKitUi.Panel(_left, "Name field", ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .92f), true, 84f, 0f, 420f, 34f);
        RectTransform area = ArenaKitUi.Box(surface.transform, "Text Area", 10f, 0f, 400f, 34f);
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI text = ArenaKitUi.Text(area, "Text", _style, string.Empty, 20f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, 400f, 34f);
        text.enableWordWrapping = false;
        var field = surface.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        if (text.font != null) field.fontAsset = text.font;
        field.characterLimit = Kits.CustomKitStructure.MaxNameLength;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.targetGraphic = surface;
        field.text = value ?? string.Empty;
        var navigation = field.navigation;
        navigation.mode = Navigation.Mode.None;
        field.navigation = navigation;
        field.onEndEdit.AddListener(entered =>
        {
            if (_root == null || !ReferenceEquals(field, _name) || string.Equals(entered, _shownName, StringComparison.Ordinal)) return;
            Rename?.Invoke(entered);
        });
        return field;
    }

    private void BuildRow(ArenaKitEditorRow row, float top, bool locked)
    {
        Image back = ArenaKitUi.Panel(_left, "Row " + row.Slot, ArenaUiTheme.WithAlpha(row.Active ? ArenaUiTheme.Hover : ArenaUiTheme.Raised, .55f), false, 0f, top, LeftWidth, RowHeight - 6f);
        Transform parent = back.transform;
        Image icon = ArenaKitUi.Panel(parent, "Picture", Color.white, false, 8f, 4f, 116f, RowHeight - 14f);
        icon.enabled = false;
        if (row.Picture != null) Queue(icon, row.Picture, 8f, 4f, 116f, RowHeight - 14f);
        ArenaKitUi.Text(parent, "Slot", _style, row.Label ?? string.Empty, 11f, ArenaUiTheme.Muted, TextAlignmentOptions.MidlineLeft, 136f, 4f, 300f, 16f);
        ArenaKitUi.Text(parent, "Item", _style, row.ItemName ?? "none", 16f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 136f, 20f, 330f, 22f);
        ArenaKitUi.Text(parent, "Detail", _style, row.Detail ?? string.Empty, 12f, ArenaUiTheme.Muted, TextAlignmentOptions.MidlineLeft, 136f, 40f, 330f, 16f);
        string slot = row.Slot;
        if (row.CanChange)
        {
            Button change = ArenaKitUi.Button(parent, "Change " + slot, _style, row.Active ? "CHOOSING" : "CHANGE", 13f, LeftWidth - 210f, 14f, 110f, 30f, out _);
            change.interactable = !locked;
            change.onClick.AddListener(() => Change?.Invoke(slot));
        }

        if (row.CanClear)
        {
            Button clear = ArenaKitUi.Button(parent, "Clear " + slot, _style, "NONE", 13f, LeftWidth - 92f, 14f, 84f, 30f, out _);
            clear.interactable = !locked;
            clear.onClick.AddListener(() => Clear?.Invoke(slot));
        }
    }

    private void BuildChoices(ArenaKitEditorModel model, Vector2 scrolled)
    {
        float width = _right.sizeDelta.x;
        ArenaKitUi.Text(_right, "Title", _style, model.ListTitle ?? string.Empty, 18f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, width - 130f, 30f);
        Button cancel = ArenaKitUi.Button(_right, "Arena Kit Editor Cancel", _style, "CANCEL", 13f, width - 120f, 0f, 120f, 30f, out _);
        cancel.onClick.AddListener(() => CancelPick?.Invoke());

        float listTop = 40f, listHeight = _height - listTop;
        Image surface = ArenaKitUi.Panel(_right, "List", new Color(0f, 0f, 0f, 0f), true, 0f, listTop, width - 14f, listHeight);
        var scroll = surface.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = ArenaKitUi.Box(surface.transform, "Viewport", 0f, 0f, width - 14f, listHeight);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = ArenaKitUi.Box(viewport, "Content", 0f, 0f, width - 14f, listHeight);
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 48f;
        scroll.inertia = false;

        if (model.Choices.Count == 0)
        {
            var empty = ArenaKitUi.Text(content, "Empty", _style, model.ListEmpty ?? "Nothing fits here.", 14f, ArenaUiTheme.Muted, TextAlignmentOptions.TopLeft, 4f, 8f, width - 30f, 60f);
            empty.enableWordWrapping = true;
        }

        var pictures = new Image[model.Choices.Count];
        for (int index = 0; index < model.Choices.Count; index++)
        {
            ArenaKitEditorChoice choice = model.Choices[index];
            float top = index * ChoiceHeight;
            Button row = ArenaKitUi.Button(content, "Choice " + choice.Key, _style, null, 0f, 0f, top, width - 22f, ChoiceHeight - 6f, out _);
            row.interactable = !model.Locked && !choice.Current;
            Image icon = ArenaKitUi.Panel(row.transform, "Picture", Color.white, false, 6f, 3f, 100f, ChoiceHeight - 12f);
            icon.enabled = false;
            pictures[index] = icon;
            ArenaKitUi.Text(row.transform, "Name", _style, choice.Name ?? choice.Key, 15f, choice.Current ? ArenaUiTheme.Accent : ArenaUiTheme.Text,
                TextAlignmentOptions.MidlineLeft, 118f, 4f, width - 160f, 24f);
            ArenaKitUi.Text(row.transform, "Detail", _style, (choice.Current ? "in the kit  ·  " : string.Empty) + (choice.Detail ?? string.Empty), 12f, ArenaUiTheme.Muted,
                TextAlignmentOptions.MidlineLeft, 118f, 28f, width - 160f, 18f);
            string key = choice.Key;
            row.onClick.AddListener(() => Pick?.Invoke(key));
        }

        content.sizeDelta = new Vector2(width - 14f, Mathf.Max(listHeight, model.Choices.Count * ChoiceHeight));
        float furthest = Mathf.Max(0f, model.Choices.Count * ChoiceHeight - listHeight);
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled.y, 0f, furthest));
        _content = content;
        _listTitle = model.ListTitle;

        // Pictures from the first row in view onwards, then the ones above it.
        int first = model.Choices.Count == 0 ? 0 : Mathf.Clamp((int)(content.anchoredPosition.y / ChoiceHeight), 0, model.Choices.Count - 1);
        for (int step = 0; step < model.Choices.Count; step++)
        {
            int index = (first + step) % model.Choices.Count;
            if (model.Choices[index].Picture != null) Queue(pictures[index], model.Choices[index].Picture, 6f, 3f, 100f, ChoiceHeight - 12f);
        }
    }

    private void BuildFacts(ArenaKitEditorModel model)
    {
        float width = _right.sizeDelta.x;
        ArenaKitUi.Text(_right, "Title", _style, "IN THIS KIT", 18f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, width, 30f);
        for (int index = 0; index < model.Facts.Count; index++)
        {
            ArenaKitUi.Text(_right, "Label " + index, _style, model.Facts[index].Key, 12f, ArenaUiTheme.Muted, TextAlignmentOptions.MidlineLeft, 0f, 44f + index * 26f, 120f, 24f);
            ArenaKitUi.Text(_right, "Value " + index, _style, model.Facts[index].Value, 14f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft, 124f, 44f + index * 26f, width - 124f, 24f);
        }

        var hint = ArenaKitUi.Text(_right, "Hint", _style,
            "CHANGE a row to pick another item: it comes as the game's own default build, a weapon loaded with its rounds. "
            + "Every change is checked and saved at once. Your kit is issued exactly as built: no attachment draw, no extra grenades.",
            13f, ArenaUiTheme.Muted, TextAlignmentOptions.BottomLeft, 0f, _height - 80f, width, 80f);
        hint.enableWordWrapping = true;
    }

    private void Queue(Image image, Func<Item> picture, float left, float top, float width, float height)
    {
        if (_iconsFailed) return;
        _icons.Enqueue(new PendingIcon { Image = image, Picture = picture, Left = left, Top = top, Width = width, Height = height });
    }

    private void PumpIcons()
    {
        if (_iconsFailed) return;
        try
        {
            for (int done = 0; done < IconsPerFrame && _icons.Count > 0; done++)
            {
                PendingIcon pending = _icons.Dequeue();
                if (pending.Image == null) continue;
                Item item = pending.Picture();
                if (item != null) ArenaKitItemIcon.Show(pending.Image, item, pending.Left, pending.Top, pending.Width, pending.Height);
            }
        }
        catch (Exception ex)
        {
            _iconsFailed = true;
            _icons.Clear();
            Plugin.ModLogger.LogWarning($"Arena kit editor: pictures unavailable: {ex.Message}");
        }
    }

    private static void DestroyChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            GameObject child = parent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }
}
