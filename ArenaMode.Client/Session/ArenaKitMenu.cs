using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Kits;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// The kit screen of the Arena map screen (docs/reviews/KIT-PICKER-20261110.md): a KITS button in
/// the head of the mode panel and the window it opens, where the player picks the kit of the
/// base draw (or the random draw) and marks favourites. It hangs on the two notifications of
/// <see cref="ArenaLocationSelectionPatch"/>, so it exists only on an Arena map screen and dies
/// with the mode panel; it adds no patch of its own. A choice is saved through
/// <see cref="Settings.TrySelectKit"/>, which refuses while a raid is armed or running; a
/// failure here never takes the mode panel or the native screen with it.
/// </summary>
internal static class ArenaKitMenu
{
    private const float PanelWidth = 360f;
    private const float WindowHeight = 840f;
    private const float DetailWidth = 480f;
    private const float Margin = 28f;
    private static readonly float WindowWidth = 2f * Margin + ArenaKitBrowserView.WidthFor(DetailWidth);

    private static GameObject _panel;
    private static Transform _screen;
    private static ArenaKitUiStyle _style;
    private static GameObject _window;
    private static readonly ArenaKitBrowserView Browser = new();
    private static TextMeshProUGUI _choiceLine, _scopeLine;
    private static GameObject _allMarker, _favoritesMarker;
    private static bool _favoritesOnly;
    // The catalogue of this opening: fetched once in Open, never again per click.
    private static IReadOnlyList<KitDefinition> _catalogue = Array.Empty<KitDefinition>();

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
            shadeButton.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Close(); });

            var frame = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)frame.transform;
            rect.SetParent(dim, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            var surface = frame.GetComponent<Image>();
            surface.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .98f);
            surface.raycastTarget = true;
            ArenaUiTheme.Decorate(rect);

            ArenaKitUi.Text(rect, "Title", _style, "KITS", 30f, ArenaUiTheme.Text, TextAlignmentOptions.MidlineLeft,
                Margin, 16f, 300f, 38f);
            _choiceLine = ArenaKitUi.Text(rect, "Choice", _style, string.Empty, 15f, ArenaUiTheme.Accent,
                TextAlignmentOptions.MidlineLeft, Margin, 54f, 760f, 22f);
            _scopeLine = ArenaKitUi.Text(rect, "Scope", _style, string.Empty, 13f, ArenaUiTheme.Muted,
                TextAlignmentOptions.MidlineLeft, Margin, 78f, 980f, 20f);

            float right = WindowWidth - Margin;
            Button back = ArenaKitUi.Button(rect, "Arena Kits Back", _style, "BACK", 15f, right - 110f, 20f, 110f, 30f, out _);
            back.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) Close(); });
            Button favorites = ArenaKitUi.Button(rect, "Arena Kits Favourites", _style, "FAVOURITES", 14f,
                right - 110f - 12f - 140f, 20f, 140f, 30f, out _);
            _favoritesMarker = Marker(favorites.transform, 140f);
            favorites.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) { _favoritesOnly = true; Refresh(); } });
            Button all = ArenaKitUi.Button(rect, "Arena Kits All", _style, "ALL KITS", 14f,
                right - 110f - 12f - 140f - 8f - 120f, 20f, 120f, 30f, out _);
            _allMarker = Marker(all.transform, 120f);
            all.onClick.AddListener(() => { if (ReferenceEquals(owner, _window)) { _favoritesOnly = false; Refresh(); } });

            Browser.Create(rect, _style, Margin, 112f, WindowWidth - 2f * Margin, WindowHeight - 112f - Margin);
            Refresh();
        }
        catch (Exception ex)
        {
            Close();
            Plugin.ModLogger.LogWarning($"Arena kit screen unavailable: {ex.Message}");
        }
    }

    private static void Refresh()
    {
        if (_window == null) return;
        var kits = _catalogue;
        var sources = new List<ArenaKitCardSource>(kits.Count);
        foreach (KitDefinition kit in kits)
            if (kit != null) sources.Add(new ArenaKitCardSource { Id = kit.Id, Name = kit.Name, Price = kit.Price, Order = kit.Order });
        ArenaKitChoice choice = KitCatalog.CurrentChoice;
        bool locked = ArenaSession.IsActive || ArenaSession.ArmedForNextRaid;
        var favorites = ArenaKitFavorites.Parse(Settings.FavoriteKits?.Value);
        var cards = ArenaKitCards.ForPicker(sources, choice, favorites, _favoritesOnly, locked);
        string empty = kits.Count == 0 ? "The kit catalogue could not be loaded from the server."
            : "No favourites yet. Mark a kit with FAV on its card.";
        Browser.Show(cards, id => Find(kits, id), OnCard, OnFavorite, empty, "Attachments are drawn again for every raid.");
        _choiceLine.text = ArenaKitCards.ChoiceLine(choice, sources);
        _scopeLine.text = locked ? "Locked while a raid is starting or running." : ScopeNote() ?? string.Empty;
        _allMarker.SetActive(!_favoritesOnly);
        _favoritesMarker.SetActive(_favoritesOnly);
    }

    private static void OnCard(ArenaKitCard card)
    {
        if (_window == null || card == null) return;
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

    private static KitDefinition Find(IReadOnlyList<KitDefinition> kits, string id)
    {
        foreach (KitDefinition kit in kits)
            if (kit != null && string.Equals(kit.Id, id, StringComparison.OrdinalIgnoreCase)) return kit;
        return null;
    }

    private static bool _handbookReported;

    /// <summary>
    /// Once per process, at Debug: whether the game's handbook is loaded where the kit screen
    /// runs. Custom kits (the next stage) are priced from it on the client, so a session whose
    /// menu has no handbook could not price them; this line is how a native run tells.
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
        _choiceLine = _scopeLine = null;
        _allMarker = _favoritesMarker = null;
        _catalogue = Array.Empty<KitDefinition>();
        var window = _window;
        _window = null;
        if (window != null)
        {
            window.SetActive(false);
            UnityEngine.Object.Destroy(window);
        }
    }
}
