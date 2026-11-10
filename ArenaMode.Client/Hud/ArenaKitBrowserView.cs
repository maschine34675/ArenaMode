using ArenaMode.Kits;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>A button of the detail pane for the kit it shows (copy, edit, delete in the kit screen).</summary>
internal sealed class ArenaKitDetailAction
{
    /// <summary>The label for this card; null hides the button for it.</summary>
    internal Func<ArenaKitCard, string> LabelFor;
    internal Action<ArenaKitCard> Run;
}

/// <summary>
/// The kit browser (docs/reviews/KIT-PICKER-20261110.md): a scrolling grid of kit cards on the
/// left and the detail of one kit on the right. It draws the cards it is given
/// (<see cref="ArenaKitCards"/> decides their order, state and text) and reports clicks; it
/// never reads a setting, changes a choice or touches a profile, so the map screen's picker and
/// solo Tactical's buy screen share it. Pictures are the game's own item icons, requested a few
/// per frame; a kit without a picture still has its name, facts and price. A host that draws
/// inside a raid sets <see cref="PicturesReady"/>: there a picture is built from the raid's
/// model pools, and the pictures of a kit the raid has not warmed yet wait until it has.
/// </summary>
internal sealed class ArenaKitBrowserView
{
    private const float CardWidth = 236f;
    private const float CardHeight = 150f;
    private const float Gap = 12f;
    private const int Columns = 4;
    private const float CardsWidth = Columns * CardWidth + (Columns - 1) * Gap;
    private const float ScrollbarWidth = 8f;
    private const float DetailLeft = CardsWidth + ScrollbarWidth + 30f;
    private const int IconsPerFrame = 2;
    private const int MaxGearIcons = 6;
    private const float ReadyCheckSeconds = .25f;

    /// <summary>The width a host has to give the browser for its four columns and a detail pane of the given width.</summary>
    internal static float WidthFor(float detailWidth) => DetailLeft + detailWidth;

    private sealed class CardWidgets
    {
        internal ArenaKitCard Card;
        internal KitDefinition Kit;
        internal Button Button;
        internal GameObject Marker;
        internal Image Icon;
        internal TextMeshProUGUI Tag, Title, Line, Price, FavoriteLabel;
        internal bool IconBound;
    }

    private sealed class Pump : MonoBehaviour
    {
        internal Action Tick;
        private void Update() => Tick?.Invoke();
    }

    private readonly List<CardWidgets> _widgets = new();
    private readonly Queue<CardWidgets> _pendingIcons = new();
    private readonly List<CardWidgets> _waitingIcons = new();
    private KitDefinition _detailWaiting;
    private float _nextReadyCheck;
    private ArenaKitUiStyle _style;
    private RectTransform _root, _content, _detailRows, _detailGear, _detailActions;
    private IReadOnlyList<ArenaKitDetailAction> _actions;
    private string _actionsKey;
    private ScrollRect _scroll;
    private TextMeshProUGUI _empty, _detailTitle, _detailPrice, _detailNote;
    private Image _detailWeapon, _detailWeaponBox;
    private float _height, _detailWidth;
    private Func<string, KitDefinition> _kitOf;
    private Action<ArenaKitCard> _onClick, _onFavorite;
    private string _structure;
    private string _detailKey;
    private CardWidgets _hovered;
    private bool _failed;

    internal GameObject Root => _root != null ? _root.gameObject : null;

    /// <summary>
    /// Whether the pictures of a kit may be asked for now; null means always (the menu). The
    /// game builds an item picture from a model of the item. In a raid that model comes from
    /// the raid's pools, and an item without a pool is instantiated bare: a scope then
    /// reports itself to the optic camera without a weapon above it, which throws in
    /// CameraManager.method_10. Pictures that are not ready are asked for again a few times a
    /// second.
    /// </summary>
    internal Func<KitDefinition, bool> PicturesReady { get; set; }

    internal void Create(Transform parent, ArenaKitUiStyle style, float left, float top, float width, float height)
    {
        Close();
        if (parent == null) throw new InvalidOperationException("The kit browser has no parent.");
        _style = style ?? ArenaKitUiStyle.Hud();
        _height = height;
        _detailWidth = Mathf.Max(260f, width - DetailLeft);
        _root = ArenaKitUi.Box(parent, "Arena Kit Browser", left, top, width, height);
        try
        {
            CreateCardsArea();
            CreateDetail();
            _root.gameObject.AddComponent<Pump>().Tick = PumpIcons;
        }
        catch { Close(); throw; }
    }

    /// <summary>
    /// Draws <paramref name="cards"/>. Cards are rebuilt only when their set or order changes;
    /// a changed selection, favourite mark or price updates the existing ones in place, so the
    /// scroll position and the hovered kit survive a click.
    /// </summary>
    internal void Show(IReadOnlyList<ArenaKitCard> cards, Func<string, KitDefinition> kitOf,
        Action<ArenaKitCard> onClick, Action<ArenaKitCard> onFavorite, string emptyText, string note,
        string priceLabel = "Value", IReadOnlyList<ArenaKitDetailAction> actions = null)
    {
        if (_root == null || cards == null) return;
        _actions = actions;
        _actionsKey = null;
        _kitOf = kitOf;
        _onClick = onClick;
        _onFavorite = onFavorite;
        _priceLabel = priceLabel ?? string.Empty;
        string structure = Structure(cards, onFavorite != null);
        if (!string.Equals(structure, _structure, StringComparison.Ordinal))
        {
            Rebuild(cards);
            _structure = structure;
        }

        for (int index = 0; index < _widgets.Count && index < cards.Count; index++)
            Apply(_widgets[index], cards[index]);
        _empty.text = cards.Count == 0 ? emptyText ?? string.Empty : string.Empty;
        _detailNote.text = note ?? string.Empty;

        CardWidgets shown = _hovered != null && _widgets.Contains(_hovered) ? _hovered : null;
        if (shown == null)
            foreach (CardWidgets widgets in _widgets)
                if (widgets.Card.Selected) { shown = widgets; break; }
        ShowDetail(shown ?? (_widgets.Count > 0 ? _widgets[0] : null));
    }

    internal void Close()
    {
        _pendingIcons.Clear();
        _waitingIcons.Clear();
        _detailWaiting = null;
        foreach (CardWidgets widgets in _widgets)
            if (widgets.Button != null) widgets.Button.onClick.RemoveAllListeners();
        _widgets.Clear();
        _hovered = null;
        _structure = null;
        _detailKey = null;
        _kitOf = null;
        _onClick = _onFavorite = null;
        var root = _root;
        _root = _content = _detailRows = _detailGear = _detailActions = null;
        _actions = null;
        _actionsKey = null;
        _scroll = null;
        _empty = _detailTitle = _detailPrice = _detailNote = null;
        _detailWeapon = _detailWeaponBox = null;
        if (root != null)
        {
            root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    private void CreateCardsArea()
    {
        // The scroll surface is a pointer target of its own: the wheel has to work over the
        // gaps between the cards as well.
        Image surface = ArenaKitUi.Panel(_root, "Cards", new Color(0f, 0f, 0f, 0f), true, 0f, 0f, CardsWidth, _height);
        _scroll = surface.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = ArenaKitUi.Box(surface.transform, "Viewport", 0f, 0f, CardsWidth, _height);
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = ArenaKitUi.Box(viewport, "Content", 0f, 0f, CardsWidth, _height);
        _scroll.viewport = viewport;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 48f;
        _scroll.inertia = false;

        Image track = ArenaKitUi.Panel(_root, "Scrollbar", ArenaUiTheme.WithAlpha(ArenaUiTheme.Edge, .18f), true,
            CardsWidth + 6f, 0f, ScrollbarWidth, _height);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        RectTransform sliding = ArenaKitUi.Box(track.transform, "Sliding Area", 0f, 0f, ScrollbarWidth, _height);
        Stretch(sliding);
        Image handle = ArenaKitUi.Panel(sliding, "Handle", ArenaUiTheme.WithAlpha(ArenaUiTheme.Accent, .70f), true,
            0f, 0f, ScrollbarWidth, 40f);
        Stretch(handle.rectTransform);
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var navigation = scrollbar.navigation;
        navigation.mode = Navigation.Mode.None;
        scrollbar.navigation = navigation;
        _scroll.verticalScrollbar = scrollbar;
        _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        _empty = ArenaKitUi.Text(_root, "Empty", _style, string.Empty, 16f, ArenaUiTheme.Muted,
            TextAlignmentOptions.TopLeft, 4f, 8f, CardsWidth - 8f, 60f);
        _empty.enableWordWrapping = true;
    }

    private void CreateDetail()
    {
        float width = _detailWidth, inner = width - 32f;
        Image pane = ArenaKitUi.Panel(_root, "Detail", ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .55f), false,
            DetailLeft, 0f, width, _height);
        ArenaUiTheme.Decorate(pane.rectTransform);
        _detailTitle = ArenaKitUi.Text(pane.transform, "Kit name", _style, string.Empty, 24f, ArenaUiTheme.Text,
            TextAlignmentOptions.MidlineLeft, 16f, 12f, inner, 32f);
        _detailPrice = ArenaKitUi.Text(pane.transform, "Kit price", _style, string.Empty, 15f, ArenaUiTheme.Accent,
            TextAlignmentOptions.MidlineLeft, 16f, 46f, inner, 22f);
        _detailWeaponBox = ArenaKitUi.Panel(pane.transform, "Weapon box", ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .70f), false,
            16f, 78f, inner, 150f);
        _detailWeapon = ArenaKitUi.Panel(pane.transform, "Weapon icon", Color.white, false, 16f, 78f, inner, 150f);
        _detailWeapon.enabled = false;
        _detailRows = ArenaKitUi.Box(pane.transform, "Facts", 16f, 242f, inner, 8f * 24f);
        _detailGear = ArenaKitUi.Box(pane.transform, "Gear", 16f, 446f, inner, 68f);
        _detailActions = ArenaKitUi.Box(pane.transform, "Actions", 16f, _height - 76f - 46f, inner, 34f);
        _detailNote = ArenaKitUi.Text(pane.transform, "Note", _style, string.Empty, 13f, ArenaUiTheme.Muted,
            TextAlignmentOptions.BottomLeft, 16f, _height - 76f, inner, 64f);
        _detailNote.enableWordWrapping = true;
    }

    private void Rebuild(IReadOnlyList<ArenaKitCard> cards)
    {
        _pendingIcons.Clear();
        _waitingIcons.Clear();
        _detailWaiting = null;
        _hovered = null;
        _detailKey = null;
        foreach (CardWidgets old in _widgets)
            if (old.Button != null)
            {
                old.Button.onClick.RemoveAllListeners();
                old.Button.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(old.Button.gameObject);
            }
        _widgets.Clear();

        for (int index = 0; index < cards.Count; index++)
        {
            ArenaKitCard card = cards[index];
            float x = index % Columns * (CardWidth + Gap), y = index / Columns * (CardHeight + Gap);
            var widgets = new CardWidgets { Card = card };
            widgets.Button = ArenaKitUi.Button(_content, "Arena Kit Card " + (card.IsRandom ? "random" : card.Id),
                _style, null, 0f, x, y, CardWidth, CardHeight, out _);
            Transform parent = widgets.Button.transform;
            widgets.Marker = ArenaKitUi.Panel(parent, "Selected", ArenaUiTheme.Accent, false, 0f, 0f, 3f, CardHeight).gameObject;
            if (card.IsRandom)
            {
                var hint = ArenaKitUi.Text(parent, "Random hint", _style, "A different kit every life,\nnever the same twice in a row.",
                    13f, ArenaUiTheme.Muted, TextAlignmentOptions.Center, 12f, 28f, CardWidth - 24f, 60f);
                hint.enableWordWrapping = true;
            }
            else
            {
                widgets.Icon = ArenaKitUi.Panel(parent, "Weapon icon", Color.white, false, 12f, 26f, CardWidth - 24f, 64f);
                widgets.Icon.enabled = false;
            }

            widgets.Tag = ArenaKitUi.Text(parent, "Tag", _style, string.Empty, 12f, ArenaUiTheme.Accent,
                TextAlignmentOptions.MidlineLeft, 12f, 5f, 150f, 18f);
            widgets.Title = ArenaKitUi.Text(parent, "Title", _style, string.Empty, 17f, ArenaUiTheme.Text,
                TextAlignmentOptions.MidlineLeft, 12f, 93f, CardWidth - 24f, 22f);
            widgets.Line = ArenaKitUi.Text(parent, "Facts", _style, string.Empty, 12f, ArenaUiTheme.Muted,
                TextAlignmentOptions.MidlineLeft, 12f, 114f, CardWidth - 24f, 16f);
            widgets.Price = ArenaKitUi.Text(parent, "Price", _style, string.Empty, 12f, ArenaUiTheme.Muted,
                TextAlignmentOptions.MidlineLeft, 12f, 130f, CardWidth - 24f, 16f);
            if (_onFavorite != null && card.CanFavorite)
            {
                Button favorite = ArenaKitUi.Button(parent, "Favourite", _style, "FAV", 11f,
                    CardWidth - 48f, 4f, 44f, 18f, out widgets.FavoriteLabel);
                favorite.onClick.AddListener(() => Favorite(widgets));
            }

            widgets.Button.gameObject.AddComponent<ArenaKitCardHover>().Entered = () => Hover(widgets);
            widgets.Button.onClick.AddListener(() => Click(widgets));
            _widgets.Add(widgets);
        }

        int rows = (cards.Count + Columns - 1) / Columns;
        float contentHeight = Mathf.Max(_height, rows * CardHeight + Mathf.Max(0, rows - 1) * Gap);
        _content.sizeDelta = new Vector2(CardsWidth, contentHeight);
        _content.anchoredPosition = Vector2.zero;
    }

    private void Apply(CardWidgets widgets, ArenaKitCard card)
    {
        widgets.Card = card;
        KitDefinition kit = card.IsRandom ? null : Kit(card.Id);
        if (!ReferenceEquals(kit, widgets.Kit))
        {
            // A re-fetched catalogue hands out new kit objects: the picture is asked for again.
            widgets.Kit = kit;
            widgets.IconBound = false;
        }

        bool usable = card.Enabled || card.Selected;
        widgets.Button.interactable = usable;
        widgets.Marker.SetActive(card.Selected);
        widgets.Tag.text = card.Tag ?? string.Empty;
        widgets.Title.text = card.Title ?? string.Empty;
        widgets.Title.color = usable ? ArenaUiTheme.Text : ArenaUiTheme.Muted;
        widgets.Price.text = card.PriceText ?? string.Empty;
        widgets.Line.text = kit != null ? KitFacts.For(kit).CardLine : string.Empty;
        if (widgets.FavoriteLabel != null)
            widgets.FavoriteLabel.color = card.Favorite ? ArenaUiTheme.Accent : ArenaUiTheme.WithAlpha(ArenaUiTheme.Muted, .55f);
        if (!widgets.IconBound && widgets.Icon != null && kit != null
            && !_pendingIcons.Contains(widgets) && !_waitingIcons.Contains(widgets))
            _pendingIcons.Enqueue(widgets);
    }

    private void PumpIcons()
    {
        if (_failed) return;
        try
        {
            if ((_waitingIcons.Count > 0 || _detailWaiting != null) && Time.unscaledTime >= _nextReadyCheck)
            {
                _nextReadyCheck = Time.unscaledTime + ReadyCheckSeconds;
                for (int index = 0; index < _waitingIcons.Count; index++)
                {
                    CardWidgets waiting = _waitingIcons[index];
                    if (waiting.Kit != null && !Ready(waiting.Kit)) continue;
                    _waitingIcons.RemoveAt(index--);
                    _pendingIcons.Enqueue(waiting);
                }

                if (_detailWaiting != null && Ready(_detailWaiting))
                {
                    KitDefinition kit = _detailWaiting;
                    _detailWaiting = null;
                    if (ReferenceEquals(kit, _detailKit)) BindDetailPictures(kit);
                }
            }

            for (int done = 0; done < IconsPerFrame && _pendingIcons.Count > 0;)
            {
                CardWidgets widgets = _pendingIcons.Dequeue();
                if (widgets.Icon == null || widgets.Kit == null || widgets.IconBound) continue;
                if (!Ready(widgets.Kit))
                {
                    _waitingIcons.Add(widgets);
                    continue;
                }

                widgets.IconBound = true;
                done++;
                ArenaKitItemIcon.Show(widgets.Icon, KitPreviewItems.For(widgets.Kit).Weapon, 12f, 26f, CardWidth - 24f, 64f);
            }
        }
        catch (Exception ex)
        {
            // Pictures are decoration: the browser stays usable without them.
            _failed = true;
            _pendingIcons.Clear();
            _waitingIcons.Clear();
            _detailWaiting = null;
            Plugin.ModLogger.LogWarning($"Arena kit browser: kit pictures unavailable: {ex.Message}");
        }
    }

    private void Click(CardWidgets widgets)
    {
        if (_root == null || !_widgets.Contains(widgets) || widgets.Card?.Enabled != true) return;
        try { _onClick?.Invoke(widgets.Card); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena kit browser: selection failed: {ex.Message}"); }
    }

    private void Favorite(CardWidgets widgets)
    {
        if (_root == null || !_widgets.Contains(widgets) || widgets.Card == null) return;
        try { _onFavorite?.Invoke(widgets.Card); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena kit browser: favourite change failed: {ex.Message}"); }
    }

    private void Hover(CardWidgets widgets)
    {
        if (_root == null || !_widgets.Contains(widgets)) return;
        _hovered = widgets;
        ShowDetail(widgets);
    }

    private void ShowDetail(CardWidgets widgets)
    {
        if (_detailTitle == null) return;
        RefreshActions(widgets?.Card);
        string key = widgets == null ? "none" : widgets.Card.IsRandom ? "random" : "kit:" + widgets.Card.Id;
        if (widgets != null)
        {
            // Name and price follow every refresh; the rows and pictures only a changed kit.
            _detailTitle.text = widgets.Card.Title ?? string.Empty;
            _detailPrice.text = widgets.Card.IsRandom || string.IsNullOrEmpty(widgets.Card.PriceText) ? string.Empty
                : _priceLabel + "  " + widgets.Card.PriceText;
        }

        if (string.Equals(key, _detailKey, StringComparison.Ordinal) && ReferenceEquals(widgets?.Kit, _detailKit)) return;
        _detailKey = key;
        _detailKit = widgets?.Kit;
        _detailWaiting = null;
        Clear(_detailRows);
        Clear(_detailGear);
        // Unbound, not only hidden: a picture of the previous kit that is still being rendered
        // would otherwise show itself under the new kit's name when it arrives.
        ArenaKitItemIcon.Show(_detailWeapon, null, 0f, 0f, 0f, 0f);
        // The picture box belongs to a kit; the random draw and an empty list have none.
        _detailWeaponBox.enabled = widgets?.Kit != null;
        _detailRows.anchoredPosition = new Vector2(16f, widgets?.Kit != null ? -242f : -86f);
        if (widgets == null)
        {
            _detailTitle.text = _detailPrice.text = string.Empty;
            return;
        }

        float inner = _detailWidth - 32f;
        if (widgets.Card.IsRandom || widgets.Kit == null)
        {
            var text = ArenaKitUi.Text(_detailRows, "Random", _style,
                widgets.Card.IsRandom
                    ? "Every life hands out another kit of the catalogue. Pick a kit to fight with the same one every life instead."
                    : "This kit is not in the catalogue any more.",
                15f, ArenaUiTheme.Muted, TextAlignmentOptions.TopLeft, 0f, 0f, inner, 96f);
            text.enableWordWrapping = true;
            return;
        }

        try
        {
            var rows = KitFacts.For(widgets.Kit).Rows;
            for (int index = 0; index < rows.Count && index < 8; index++)
            {
                ArenaKitUi.Text(_detailRows, "Label " + rows[index].Key, _style, rows[index].Key, 12f, ArenaUiTheme.Muted,
                    TextAlignmentOptions.MidlineLeft, 0f, index * 24f, 104f, 22f);
                ArenaKitUi.Text(_detailRows, "Value " + rows[index].Key, _style, rows[index].Value, 14f, ArenaUiTheme.Text,
                    TextAlignmentOptions.MidlineLeft, 108f, index * 24f, inner - 108f, 22f);
            }

            if (_failed) return;
            if (Ready(widgets.Kit)) BindDetailPictures(widgets.Kit);
            else _detailWaiting = widgets.Kit;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena kit browser: kit detail unavailable: {ex.Message}");
        }
    }

    private void BindDetailPictures(KitDefinition kit)
    {
        if (_detailWeapon == null || _detailGear == null || kit == null) return;
        float inner = _detailWidth - 32f;
        Clear(_detailGear);
        KitPreviewItems preview = KitPreviewItems.For(kit);
        ArenaKitItemIcon.Show(_detailWeapon, preview.Weapon, 16f, 78f, inner, 150f);
        for (int index = 0; index < preview.Gear.Count && index < MaxGearIcons; index++)
        {
            float left = index * 76f;
            ArenaKitUi.Panel(_detailGear, "Gear box " + index, ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .70f), false,
                left, 0f, 68f, 68f);
            Image icon = ArenaKitUi.Panel(_detailGear, "Gear icon " + index, Color.white, false, left, 0f, 68f, 68f);
            icon.enabled = false;
            ArenaKitItemIcon.Show(icon, preview.Gear[index], left + 2f, 2f, 64f, 64f);
        }
    }

    private bool Ready(KitDefinition kit)
    {
        Func<KitDefinition, bool> ready = PicturesReady;
        return ready == null || (kit != null && ready(kit));
    }

    /// <summary>The detail pane's buttons for <paramref name="card"/>, rebuilt only when their labels change.</summary>
    private void RefreshActions(ArenaKitCard card)
    {
        if (_detailActions == null) return;
        var labels = new List<string>();
        var runs = new List<Action<ArenaKitCard>>();
        if (card != null && _actions != null)
        {
            foreach (ArenaKitDetailAction action in _actions)
            {
                string label = action?.LabelFor?.Invoke(card);
                if (string.IsNullOrEmpty(label) || action.Run == null) continue;
                labels.Add(label);
                runs.Add(action.Run);
            }
        }

        string key = (card?.Id ?? "<none>") + "|" + string.Join("|", labels.ToArray());
        if (string.Equals(key, _actionsKey, StringComparison.Ordinal)) return;
        _actionsKey = key;
        Clear(_detailActions);
        for (int index = 0; index < labels.Count; index++)
        {
            Action<ArenaKitCard> run = runs[index];
            ArenaKitCard target = card;
            Button button = ArenaKitUi.Button(_detailActions, "Arena Kit Action " + labels[index], _style, labels[index], 13f,
                index * 150f, 0f, 140f, 32f, out _);
            button.onClick.AddListener(() =>
            {
                if (_root == null) return;
                try { run(target); }
                catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena kit browser: action failed: {ex.Message}"); }
            });
        }
    }

    private KitDefinition _detailKit;
    private string _priceLabel = "Value";

    private KitDefinition Kit(string id)
    {
        try { return _kitOf?.Invoke(id); }
        catch (Exception) { return null; }
    }

    private static string Structure(IReadOnlyList<ArenaKitCard> cards, bool favorites)
    {
        var builder = new System.Text.StringBuilder(favorites ? "f|" : "n|");
        foreach (ArenaKitCard card in cards)
            builder.Append(card.IsRandom ? "<random>" : card.Id).Append('|');
        return builder.ToString();
    }

    private static void Clear(RectTransform parent)
    {
        if (parent == null) return;
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            GameObject child = parent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
