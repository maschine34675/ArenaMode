using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Kits;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// The kit screen of the Arena map screen (docs/reviews/KIT-PICKER-20261110.md and, for the player's
/// own kits, docs/reviews/CUSTOM-KITS-20261110.md): a KITS button in the head of the mode panel and
/// the window it opens, where the player picks the kit of the base draw (or the random draw), marks
/// favourites, copies a kit into an own kit and edits it slot by slot. It hangs on the two
/// notifications of <see cref="ArenaLocationSelectionPatch"/>, so it exists only on an Arena map
/// screen and dies with the mode panel; it adds no patch of its own. A choice is saved through
/// <see cref="Settings.TrySelectKit"/> and an own kit through <see cref="ArenaKitWorkshop"/>, both
/// refused while a raid is armed or running; a failure here never takes the mode panel or the native
/// screen with it, and nothing of an edit is lost with the window, because each change is saved.
/// </summary>
internal static class ArenaKitMenu
{
    private const float PanelWidth = 360f;
    private const float WindowHeight = 840f;
    private const float DetailWidth = 480f;
    private const float Margin = 28f;
    private const float BodyTop = 112f;
    private static readonly float WindowWidth = 2f * Margin + ArenaKitBrowserView.WidthFor(DetailWidth);

    private enum Filter { All, Mine, Favourites }

    private static GameObject _panel;
    private static Transform _screen;
    private static ArenaKitUiStyle _style;
    private static GameObject _window;
    private static RectTransform _frame;
    private static readonly ArenaKitBrowserView Browser = new();
    private static readonly ArenaKitEditorView Editor = new();
    private static TextMeshProUGUI _choiceLine, _scopeLine;
    private static GameObject _allMarker, _mineMarker, _favoritesMarker;
    private static Filter _filter;
    // The catalogue of this opening: fetched once in Open, never again per click.
    private static IReadOnlyList<KitDefinition> _catalogue = Array.Empty<KitDefinition>();
    // The own kits as this session sees them: read once per opening and again after every change.
    private static CustomKitView _custom = CustomKitView.Empty;
    private static string _editing, _picking, _status, _confirmDelete;
    private static bool _statusIsError;

    internal static void Enable()
    {
        ArenaLocationSelectionPatch.ModePanelShown += OnModePanelShown;
        ArenaLocationSelectionPatch.ModePanelClosing += OnModePanelClosing;
    }

    private static void OnModePanelShown(Transform screen, GameObject panel, TMP_Text typography)
    {
        Close();
        if (screen == null || panel == null) return;
        try
        {
            _screen = screen;
            _panel = panel;
            _style = ArenaKitUiStyle.From(typography);
            // The head of the mode panel is free to the right of its title; the rows below keep
            // their places, so no other view moves.
            Button entry = ArenaKitUi.Button(panel.transform, "Arena Kits Entry", _style, "KITS", 14f,
                PanelWidth - 20f - 92f, 19f, 92f, 26f, out _);
            GameObject owner = panel;
            entry.onClick.AddListener(() =>
            {
                if (!ReferenceEquals(owner, _panel) || owner == null || !owner.activeInHierarchy) return;
                Open();
            });
        }
        catch (Exception ex)
        {
            _panel = null;
            _screen = null;
            Plugin.ModLogger.LogWarning($"Arena kit screen entry unavailable: {ex.Message}");
        }
    }

    private static void OnModePanelClosing(GameObject panel)
    {
        if (!ReferenceEquals(panel, _panel)) return;
        Close();
        _panel = null;
        _screen = null;
        _style = null;
    }

    private static void Open()
    {
        Close();
        if (_screen == null) return;
        try
        {
            // Fetched again per opening: a kit file edited since the last raid would be missing
            // from the cards otherwise. A failed fetch keeps what was loaded.
            _catalogue = Array.Empty<KitDefinition>();
            try { _catalogue = KitCatalog.RefreshForMenu() ?? (IReadOnlyList<KitDefinition>)Array.Empty<KitDefinition>(); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena kit screen: catalogue refresh failed: {ex.Message}"); }
            ReportHandbookOnce();
            ArenaKitWorkshop.Reset();
            ReloadCustom();

            _window = new GameObject("Arena Kit Screen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var dim = (RectTransform)_window.transform;
            dim.SetParent(_screen, false);
            dim.SetAsLastSibling();
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            var shade = _window.GetComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, .72f);
            // The shade takes every click that misses the window: nothing of the map screen
            // behind it can be pressed while the kits are open.
            shade.raycastTarget = true;
            var shadeButton = _window.AddComponent<Button>();
            shadeButton.transition = Selectable.Transition.None;
            var shadeNavigation = shadeButton.navigation;
            shadeNavigation.mode = Navigation.Mode.None;
            shadeButton.navigation = shadeNavigation;
            GameObject owner = _window;
            shadeButton.onClick.AddListener(() => { if (ReferenceEquals(owner, _window) && _editing == null) Close(); });

            var frame = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _frame = (RectTransform)frame.transform;
            _frame.SetParent(dim, false);
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(.5f, .5f);
            _frame.anchoredPosition = Vector2.zero;
            _frame.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            var surface = frame.GetComponent<Image>();
            surface.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .98f);
            surface.raycastTarget = true;
            ArenaUiTheme.Decorate(_frame);

            ArenaKitUi.Text(_frame, "Title", _style, "KITS", 30f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft,
                Margin, 16f, 300f, 38f);
            _choiceLine = ArenaKitUi.Text(_frame, "Choice", _style, string.Empty, 15f, ArenaUiTheme.Accent,
                TextAlignmentOptions.MidlineLeft, Margin, 54f, 760f, 22f);
            _scopeLine = ArenaKitUi.Text(_frame, "Scope", _style, string.Empty, 13f, ArenaUiTheme.Muted,
                TextAlignmentOptions.MidlineLeft, Margin, 78f, 1100f, 20f);

            float right = WindowWidth - Margin;
            Button back = ArenaKitUi.Button(_frame, "Arena Kits Back", _style, "DONE", 15f, right - 110f, 20f, 110f, 30f, out _);
            back.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Close(); });
            Button favorites = ArenaKitUi.Button(_frame, "Arena Kits Favourites", _style, "FAVOURITES", 14f,
                right - 110f - 12f - 140f, 20f, 140f, 30f, out _);
            _favoritesMarker = Marker(favorites.transform, 140f);
            favorites.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Show(Filter.Favourites); });
            Button mine = ArenaKitUi.Button(_frame, "Arena Kits Mine", _style, "MY KITS", 14f,
                right - 110f - 12f - 140f - 8f - 120f, 20f, 120f, 30f, out _);
            _mineMarker = Marker(mine.transform, 120f);
            mine.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Show(Filter.Mine); });
            Button all = ArenaKitUi.Button(_frame, "Arena Kits All", _style, "ALL KITS", 14f,
                right - 110f - 12f - 140f - 8f - 120f - 8f - 120f, 20f, 120f, 30f, out _);
            _allMarker = Marker(all.transform, 120f);
            all.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Show(Filter.All); });

            Browser.Create(_frame, _style, Margin, BodyTop, WindowWidth - 2f * Margin, WindowHeight - BodyTop - Margin);
            Refresh();
        }
        catch (Exception ex)
        {
            Close();
            Plugin.ModLogger.LogWarning($"Arena kit screen unavailable: {ex.Message}");
        }
    }

    private static void Show(Filter filter)
    {
        _filter = filter;
        StopEditing();
        Refresh();
    }

    private static void Refresh()
    {
        if (_window == null) return;
        if (_editing != null)
        {
            RefreshEditor();
            return;
        }

        var sources = Sources();
        ArenaKitChoice choice = KitCatalog.CurrentChoice;
        bool locked = ArenaSession.IsActive || ArenaSession.ArmedForNextRaid;
        var favorites = ArenaKitFavorites.Parse(Settings.FavoriteKits?.Value);
        var shown = _filter == Filter.Mine ? sources.Where(source => source.Custom).ToList() : sources;
        var cards = ArenaKitCards.ForPicker(shown, choice, favorites, _filter == Filter.Favourites, locked);
        if (_filter == Filter.Mine) cards.RemoveAll(card => card.IsRandom);
        string empty = _catalogue.Count == 0 ? "The kit catalogue could not be loaded from the server."
            : _filter == Filter.Mine ? "No kits of your own yet. Pick any kit and press COPY TO MY KITS."
            : "No favourites yet. Mark a kit with FAV on its card.";
        Browser.Show(cards, FindAny, OnCard, OnFavorite, empty,
            "Stock kits draw their attachments again for every raid; your own kits come exactly as built.", "Value", Actions(locked));
        _choiceLine.text = ArenaKitCards.ChoiceLine(choice, sources);
        _scopeLine.text = Scope(locked);
        Markers();
    }

    private static List<ArenaKitCardSource> Sources()
    {
        var sources = new List<ArenaKitCardSource>(_catalogue.Count + _custom.Entries.Count);
        foreach (KitDefinition kit in _catalogue)
            if (kit != null) sources.Add(new ArenaKitCardSource { Id = kit.Id, Name = kit.Name, Price = kit.Price, Order = kit.Order });
        foreach (CustomKitEntry entry in _custom.Entries)
        {
            if (entry.Kit == null) continue;
            sources.Add(new ArenaKitCardSource
            {
                Id = entry.Id, Name = entry.Kit.Name, Price = entry.Price, Order = entry.Kit.Order, Custom = true,
                Blocked = ArenaCustomKitPolicy.PickerBlock(entry.State, entry.Reason, entry.Price, _custom.StockCap),
            });
        }

        return sources;
    }

    private static IReadOnlyList<ArenaKitDetailAction> Actions(bool locked)
    {
        if (locked) return null;
        return new[]
        {
            new ArenaKitDetailAction { LabelFor = card => card.IsRandom ? null : "COPY TO MY KITS", Run = card => CopyKit(FindAny(card.Id)) },
            new ArenaKitDetailAction { LabelFor = card => card.Custom ? "EDIT" : null, Run = card => StartEditing(card.Id) },
            new ArenaKitDetailAction
            {
                LabelFor = card => card.Custom ? (_confirmDelete == card.Id ? "CONFIRM DELETE" : "DELETE") : null,
                Run = card => DeleteKit(card.Id),
            },
        };
    }

    private static string Scope(bool locked)
    {
        if (locked) return "Locked while a raid is starting or running.";
        if (_status != null) return _status;
        if (_custom.Banner != null) return _custom.Banner;
        int unreadable = _custom.Entries.Count(entry => entry.Kit == null);
        if (unreadable > 0) return unreadable + " stored kit record(s) could not be read; they stay in your kit file untouched.";
        return ScopeNote() ?? string.Empty;
    }

    private static void OnCard(ArenaKitCard card)
    {
        if (_window == null || card == null) return;
        _confirmDelete = null;
        _status = null;
        if (Settings.TrySelectKit(card.IsRandom, card.Id) && Singleton<GUISounds>.Instantiated)
            Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        Refresh();
    }

    private static void OnFavorite(ArenaKitCard card)
    {
        if (_window == null || card == null || card.IsRandom) return;
        Settings.TryToggleFavoriteKit(card.Id);
        Refresh();
    }

    // --- own kits

    private static void CopyKit(KitDefinition source)
    {
        _confirmDelete = null;
        string failed = ArenaKitWorkshop.Copy(source, _catalogue, _custom, out string id);
        ReloadCustom();
        if (failed != null)
        {
            Say("Not copied: " + failed, true);
            Refresh();
            return;
        }

        // The message goes in with the editor, which draws it; set afterwards it was never shown.
        StartEditing(id, "Copied. Change a row to make it your own; every change is saved at once.");
    }

    private static void DeleteKit(string id)
    {
        if (_confirmDelete != id)
        {
            _confirmDelete = id;
            _status = null;
            Refresh();
            return;
        }

        _confirmDelete = null;
        string failed = ArenaKitWorkshop.Delete(id);
        StopEditing();
        ReloadCustom();
        Say(failed == null ? "Deleted." : "Not deleted: " + failed, failed != null);
        Refresh();
    }

    /// <summary>Opens the editor on kit <paramref name="id"/> with <paramref name="status"/> on its status line (none: a fresh start, nothing of the last kit's).</summary>
    private static void StartEditing(string id, string status = null)
    {
        if (_window == null || _custom.Find(id)?.Kit == null) return;
        _editing = id;
        _picking = null;
        _confirmDelete = null;
        Say(status, false);
        Browser.Root?.SetActive(false);
        if (!Editor.Open)
        {
            Editor.Create(_frame, _style, Margin, BodyTop, WindowWidth - 2f * Margin, WindowHeight - BodyTop - Margin);
            Editor.Rename = Rename;
            Editor.Change = slot => { _picking = slot; _status = null; _confirmDelete = null; Refresh(); };
            Editor.Clear = ClearSlot;
            Editor.Pick = PickItem;
            Editor.CancelPick = () => { _picking = null; _confirmDelete = null; Refresh(); };
            Editor.Done = () => { StopEditing(); Refresh(); };
            Editor.Copy = () => { var entry = _custom.Find(_editing); if (entry?.Kit != null) CopyKit(entry.Kit); };
            Editor.Delete = () => { if (_editing != null) DeleteKit(_editing); };
        }

        Refresh();
    }

    /// <summary>
    /// Back to the cards with nothing of the editor left: its last message would otherwise hide the
    /// scope line's banner and mode note, and an armed DELETE would delete with one click later.
    /// </summary>
    private static void StopEditing()
    {
        _editing = null;
        _picking = null;
        _confirmDelete = null;
        Say(null, false);
        Editor.Close();
        Browser.Root?.SetActive(true);
    }

    private static void RefreshEditor()
    {
        CustomKitEntry entry = _custom.Find(_editing);
        if (entry?.Kit == null)
        {
            StopEditing();
            Refresh();
            return;
        }

        KitDefinition kit = entry.Kit;
        bool locked = ArenaKitWorkshop.Locked;
        var model = new ArenaKitEditorModel
        {
            Name = kit.Name,
            CanRename = true,
            Locked = locked,
            PriceLine = entry.Price > 0 ? "Price  " + ArenaKitCards.Purse(entry.Price) : "No price",
            CapLine = CapLine(entry),
            Status = locked ? ArenaKitWorkshop.LockedReason : _status,
            StatusIsError = locked || _statusIsError,
            DeleteLabel = _confirmDelete == _editing ? "CONFIRM DELETE" : "DELETE",
        };
        model.Rows.AddRange(ArenaKitWorkshop.Rows(kit, _picking));
        if (_picking != null)
        {
            model.ListTitle = "Choose: " + ArenaKitWorkshop.Label(_picking);
            model.Choices = ArenaKitWorkshop.Choices(kit, _picking, out string reason);
            model.ListEmpty = reason ?? "Nothing fits here.";
        }
        else
        {
            foreach (var row in KitFacts.For(kit).Rows) model.Facts.Add(row);
        }

        Editor.Show(model);
        _choiceLine.text = "Editing your kit";
        _scopeLine.text = entry.State == CustomKitState.Ready ? "Ready." : "Not usable here: " + (entry.Reason ?? "no price");
        Markers();
    }

    private static string CapLine(CustomKitEntry entry)
    {
        if (entry.State != CustomKitState.Ready) return entry.Reason ?? "not priced";
        if (_custom.StockCap <= 0) return "No stock price to compare with: used in solo Tactical only.";
        return entry.Price <= _custom.StockCap
            ? "Within the stock cap of " + ArenaKitCards.Purse(_custom.StockCap) + ": handed out in every mode."
            : "Above the stock cap of " + ArenaKitCards.Purse(_custom.StockCap) + ": bought in solo Tactical only.";
    }

    private static void Rename(string name)
    {
        CustomKitEntry entry = _custom.Find(_editing);
        if (entry?.Kit == null) return;
        KitDefinition renamed = ArenaKitWorkshop.Renamed(entry.Kit, name, out string reason);
        // Drawn in place, not rebuilt (ArenaKitEditorView.ShowRenamed says why); a refused name
        // puts the stored one back into the field. An armed DELETE stays armed: its label is not
        // redrawn here, and disarming it unseen would swallow the CONFIRM DELETE click that ended
        // the typing.
        bool saved = Commit(renamed, reason, "Renamed.", redraw: false);
        Editor.ShowRenamed(saved ? _custom.Find(_editing)?.Kit?.Name : entry.Kit.Name, _status, _statusIsError);
    }

    private static void ClearSlot(string slot)
    {
        CustomKitEntry entry = _custom.Find(_editing);
        if (entry?.Kit == null) return;
        _confirmDelete = null;
        Commit(ArenaKitWorkshop.Without(entry.Kit, slot), null, ArenaKitWorkshop.Label(slot) + " removed.");
    }

    private static void PickItem(string tpl)
    {
        CustomKitEntry entry = _custom.Find(_editing);
        if (entry?.Kit == null || _picking == null) return;
        _confirmDelete = null;
        string slot = _picking;
        KitDefinition changed = ArenaKitWorkshop.WithItem(entry.Kit, slot, tpl, out string reason);
        // The list closes before the one redraw: drawn while still open, it was built in full and
        // thrown away again by the next redraw.
        Commit(changed, reason, ArenaKitWorkshop.Label(slot) + ": " + CustomKitItems.ShortName(tpl) + ".", saved: () => _picking = null);
    }

    /// <summary>
    /// Saves <paramref name="changed"/> after both checks; false (and the reason on the status line)
    /// when it was refused. <paramref name="saved"/> runs before the redraw when it was saved.
    /// </summary>
    private static bool Commit(KitDefinition changed, string reason, string done, Action saved = null, bool redraw = true)
    {
        string failed = reason ?? (changed == null ? "nothing to change" : ArenaKitWorkshop.Apply(changed, _catalogue));
        if (failed == null)
        {
            ReloadCustom();
            Say(done + " Saved.", false);
            saved?.Invoke();
            if (Singleton<GUISounds>.Instantiated) Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
        }
        else
        {
            Say("Not changed: " + failed, true);
        }

        if (redraw) Refresh();
        return failed == null;
    }

    private static void Say(string status, bool error)
    {
        _status = status;
        _statusIsError = error;
    }

    private static void ReloadCustom()
    {
        try { _custom = CustomKits.Load(_catalogue); }
        catch (Exception ex)
        {
            _custom = new CustomKitView { Banner = "Your kits could not be read: " + ex.Message, ReadOnly = true };
            Plugin.ModLogger.LogWarning($"Arena kit screen: own kits unavailable: {ex.Message}");
        }
    }

    private static KitDefinition FindAny(string id)
    {
        foreach (KitDefinition kit in _catalogue)
            if (kit != null && string.Equals(kit.Id, id, StringComparison.OrdinalIgnoreCase)) return kit;
        return _custom.Find(id)?.Kit;
    }

    private static void Markers()
    {
        _allMarker?.SetActive(_editing == null && _filter == Filter.All);
        _mineMarker?.SetActive(_editing != null || _filter == Filter.Mine);
        _favoritesMarker?.SetActive(_editing == null && _filter == Filter.Favourites);
    }

    /// <summary>
    /// One short line when the mode on the map screen does not hand out the picked kit as it is;
    /// null when it does. The choice itself is saved either way and serves the other modes.
    /// </summary>
    private static string ScopeNote()
    {
        string mode = Settings.SelectedGameModeId;
        var eligibility = ArenaMenuSelection.CurrentModeEligibility;
        bool team = eligibility.Allowed && eligibility.IsTeam;
        if (mode == ArenaModeCatalog.GunGameId)
            return "Gun Game: the ladder sets the weapon, this kit the rest (without a ladder, kits are the rungs).";
        if (mode == ArenaModeCatalog.TacticalId && !team) return "Tactical: kits are bought in the raid.";
        if (team && mode != ArenaModeCatalog.TacticalId) return "PitFireTeam: your PMC enters with its own gear.";
        if (ArenaEquipmentChoice.IsOfferedFor(mode, team) && Settings.GetEquipmentChoiceOwnGear(mode, team))
            return "EQUIPMENT is Own gear: no kit is handed out.";
        return null;
    }

    private static bool _handbookReported;

    /// <summary>
    /// Once per process, at Debug: whether the game's handbook is loaded where the kit screen
    /// runs. Own kits are priced from it on the client, so a session whose menu has no handbook
    /// cannot price them; this line is how a native run tells.
    /// </summary>
    private static void ReportHandbookOnce()
    {
        if (_handbookReported) return;
        _handbookReported = true;
        try
        {
            bool loaded = Singleton<EFT.HandBook.Handbook>.Instantiated;
            int items = loaded ? Singleton<EFT.HandBook.Handbook>.Instance.Items?.Length ?? 0 : 0;
            Plugin.ModLogger.LogDebug(loaded
                ? $"Arena kit screen: game handbook loaded, {items} item prices."
                : "Arena kit screen: game handbook not loaded.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogDebug($"Arena kit screen: handbook check failed: {ex.Message}");
        }
    }

    private static GameObject Marker(Transform button, float width)
        => ArenaKitUi.Panel(button, "Selected", ArenaUiTheme.Accent, false, 0f, 27f, width, 3f).gameObject;

    private static void Close()
    {
        try { Browser.Close(); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena kit screen cleanup: {ex.Message}"); }
        try { Editor.Close(); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena kit editor cleanup: {ex.Message}"); }
        _choiceLine = _scopeLine = null;
        _allMarker = _mineMarker = _favoritesMarker = null;
        _catalogue = Array.Empty<KitDefinition>();
        _custom = CustomKitView.Empty;
        _editing = _picking = _status = _confirmDelete = null;
        _statusIsError = false;
        _frame = null;
        var window = _window;
        _window = null;
        if (window != null)
        {
            window.SetActive(false);
            UnityEngine.Object.Destroy(window);
        }
    }
}
