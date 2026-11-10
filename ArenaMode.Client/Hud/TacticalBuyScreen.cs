using ArenaMode.Kits;
using ArenaMode.Modes;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Solo Tactical's buy screen (docs/reviews/KIT-PICKER-20261110.md): the kit browser of the
/// map screen with the round's purse in its head. <see cref="TacticalBuyPanel"/> stays the
/// owner of the buy phase: it decides when the screen is open, holds the input block and the
/// cursor, and keeps its plain window as the fallback when this one cannot be built. This
/// class only draws and forwards a click to <see cref="TacticalMode.TryBuy"/>,
/// <see cref="TacticalMode.ClearPurchase"/> or <see cref="TacticalMode.ReadyUp"/>; prices,
/// affordability and what a purchase does are the mode's.
///
/// The raid HUD switches the game's UI event system off (BattleUIScreen.Show calls
/// UIEventSystem.Disable), so nothing drawn with the game's UI gets a click in a raid. The
/// console solves that with UIEventSystem.SetTemporaryStatus, and so does this screen: on while
/// it is drawn, off again when it goes, unless the console still needs it.
/// </summary>
internal sealed class TacticalBuyScreen
{
    private const float WindowHeight = 820f;
    private const float DetailWidth = 480f;
    private const float Margin = 28f;
    private static readonly float WindowWidth = 2f * Margin + ArenaKitBrowserView.WidthFor(DetailWidth);

    // The templates of a kit, built once per kit object: the picture gate asks a few times a
    // second for every card still waiting, and must not allocate for it.
    private static readonly ConditionalWeakTable<KitDefinition, string[]> TemplatesByKit = new();

    private readonly ArenaKitBrowserView _browser = new();
    private GameObject _canvas;
    private TextMeshProUGUI _title, _money, _state, _plan;
    private Button _keep;
    private TextMeshProUGUI _keepLabel;
    private Action _close;
    private bool _failed;
    private bool _eventsHeld;

    // What the head and the cards show now. Tick runs every frame of the buy phase; a text is
    // rebuilt only when what it says changed, the cards only when one of their inputs did.
    private bool _shown;
    private int _shownRound, _shownWon, _shownLost, _shownMoney, _shownSeconds;
    private bool _shownWaiting, _shownKeeps;
    private KitDefinition _shownBought, _shownWorn;
    private IReadOnlyList<KitDefinition> _shownKits;
    private int _shownKitCount;
    private bool _cardsStale = true;

    /// <summary>True while this screen is on, so the panel's plain window stays away.</summary>
    internal bool Drawn => _canvas != null;

    internal void Show(TacticalMode mode, Action close)
    {
        if (_failed || mode == null) return;
        if (_canvas != null) { Tick(mode); return; }
        _close = close;
        try
        {
            Build();
            Tick(mode);
        }
        catch (Exception ex)
        {
            // Once per process: the plain window serves every later buy phase.
            _failed = true;
            Hide();
            Plugin.ModLogger.LogWarning($"Tactical buy screen unavailable, using the plain buy window: {ex.Message}");
        }
    }

    internal void Hide()
    {
        ReleaseEvents();
        try { _browser.Close(); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Tactical buy screen cleanup: {ex.Message}"); }
        _title = _money = _state = _plan = _keepLabel = null;
        _keep = null;
        _close = null;
        _shown = false;
        _shownBought = _shownWorn = null;
        _shownKits = null;
        _cardsStale = true;
        var canvas = _canvas;
        _canvas = null;
        if (canvas != null)
        {
            canvas.SetActive(false);
            UnityEngine.Object.Destroy(canvas);
        }
    }

    /// <summary>The head while the countdown runs; the cards only when the purse, the purchase, the kept kit or the catalogue changed.</summary>
    internal void Tick(TacticalMode mode)
    {
        if (_canvas == null || mode == null) return;
        try
        {
            // Every frame: a console closed in between takes the event system with it.
            HoldEvents();

            int round = mode.RoundNumber, won = mode.RoundsWon, lost = mode.RoundsLost, money = mode.Money;
            bool waiting = mode.WaitingForKitSwap;
            int seconds = waiting ? -1 : (int)Math.Round(mode.PhaseRemaining, MidpointRounding.AwayFromZero);
            KitDefinition bought = mode.Bought;
            // The kit a no-purchase round hands out; a player who is down keeps nothing.
            bool keeps = mode.KeepsCurrentGear;
            KitDefinition worn = keeps ? mode.Worn : null;
            IReadOnlyList<KitDefinition> kits = KitCatalog.Kits ?? (IReadOnlyList<KitDefinition>)Array.Empty<KitDefinition>();

            bool fresh = !_shown;
            if (fresh || round != _shownRound || won != _shownWon || lost != _shownLost)
                _title.text = $"BUY  ·  ROUND {round}   {won}-{lost}";
            if (fresh || money != _shownMoney)
                _money.text = ArenaKitCards.Purse(money);
            if (fresh || waiting != _shownWaiting || seconds != _shownSeconds)
                _state.text = waiting ? "starting when your kit has landed" : $"starting in {seconds}s";
            if (fresh || !ReferenceEquals(bought, _shownBought) || round != _shownRound || keeps != _shownKeeps)
            {
                // NoPurchaseDescription follows the round, whether the player is down and the starter.
                _plan.text = bought != null ? $"Buying: {bought.Name ?? bought.Id}" : mode.NoPurchaseDescription;
                _keep.interactable = bought != null;
                _keepLabel.color = bought != null ? ArenaUiTheme.Text : ArenaUiTheme.Muted;
            }

            bool cards = _cardsStale || fresh || money != _shownMoney || round != _shownRound
                || !ReferenceEquals(bought, _shownBought) || !ReferenceEquals(worn, _shownWorn)
                || !ReferenceEquals(kits, _shownKits) || kits.Count != _shownKitCount;

            _shown = true;
            _shownRound = round; _shownWon = won; _shownLost = lost; _shownMoney = money;
            _shownWaiting = waiting; _shownSeconds = seconds; _shownKeeps = keeps;
            _shownBought = bought; _shownWorn = worn; _shownKits = kits; _shownKitCount = kits.Count;
            if (!cards) return;
            _cardsStale = false;

            var sources = new List<ArenaKitCardSource>(kits.Count);
            foreach (KitDefinition kit in kits)
                if (kit != null)
                    sources.Add(new ArenaKitCardSource { Id = kit.Id, Name = kit.Name, Price = mode.PriceOf(kit), Order = kit.Order });
            var shop = ArenaKitCards.ForShop(sources, money, bought?.Id, worn?.Id);
            _browser.Show(shop, id => Find(kits, id), card => Buy(card, kits), null,
                "The kit catalogue is empty.", "A round you survive keeps what you wear.", "Price");
        }
        catch (Exception ex)
        {
            _failed = true;
            Hide();
            Plugin.ModLogger.LogWarning($"Tactical buy screen failed, using the plain buy window: {ex.Message}");
        }
    }

    /// <summary>
    /// Pictures only of kits this raid has warmed (ArenaKitBrowserView.PicturesReady has the
    /// reason). The raid's catalogue warm-up loads one kit after another from a few seconds
    /// into the raid; how soon it reaches the dearer kits depends on the machine and on what
    /// the bundle cache already holds. Until then, and for a kit whose warm-up failed, a card
    /// has its name, facts and price without a picture.
    /// </summary>
    private static bool Warmed(KitDefinition kit)
    {
        if (kit == null) return false;
        string[] templates = TemplatesByKit.GetValue(kit, BuildTemplates);
        foreach (string template in templates)
            if (!KitBundlePreloader.IsTemplateWarmed(template)) return false;
        return true;
    }

    // A field, not a method group: a method group converts to a new delegate on every call.
    private static readonly ConditionalWeakTable<KitDefinition, string[]>.CreateValueCallback BuildTemplates =
        kit => KitPreloadPlan.TemplatesOf(kit).ToArray();

    private void Build()
    {
        _canvas = new GameObject("ArenaTacticalBuyCanvas");
        _canvas.transform.SetParent(Plugin.Instance.transform, false);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 140; // above the Arena HUD (130)
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();

        // The shade dims the raid HUD around the window and takes every click that misses it,
        // so nothing of the HUD behind is pressed while the event system is on.
        var shadeObject = new GameObject("Shade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var shadeRect = (RectTransform)shadeObject.transform;
        shadeRect.SetParent(_canvas.transform, false);
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
        var shade = shadeObject.GetComponent<Image>();
        shade.color = new Color(0f, 0f, 0f, .62f);
        shade.raycastTarget = true;

        var style = ArenaKitUiStyle.Hud();
        var frame = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)frame.transform;
        rect.SetParent(_canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
        var surface = frame.GetComponent<Image>();
        // Opaque: the score strip of the raid HUD lies behind the head of this window.
        surface.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, 1f);
        surface.raycastTarget = true;
        ArenaUiTheme.Decorate(rect);

        _title = ArenaKitUi.Text(rect, "Title", style, string.Empty, 28f, ArenaUiTheme.Text,
            TextAlignmentOptions.MidlineLeft, Margin, 14f, 620f, 36f);
        _money = ArenaKitUi.Text(rect, "Money", style, string.Empty, 24f, ArenaUiTheme.Accent,
            TextAlignmentOptions.MidlineLeft, Margin, 50f, 300f, 30f);
        _state = ArenaKitUi.Text(rect, "State", style, string.Empty, 15f, ArenaUiTheme.Muted,
            TextAlignmentOptions.MidlineLeft, Margin + 310f, 54f, 420f, 24f);
        _plan = ArenaKitUi.Text(rect, "Plan", style, string.Empty, 14f, ArenaUiTheme.Text,
            TextAlignmentOptions.MidlineLeft, Margin, 82f, 1000f, 22f);

        float right = WindowWidth - Margin;
        GameObject owner = _canvas;
        Button close = ArenaKitUi.Button(rect, "Tactical Buy Close", style, "CLOSE", 15f, right - 110f, 20f, 110f, 32f, out _);
        close.onClick.AddListener(() => { if (ReferenceEquals(owner, _canvas)) _close?.Invoke(); });
        Button ready = ArenaKitUi.Button(rect, "Tactical Buy Ready", style, "READY", 15f, right - 110f - 10f - 130f, 20f, 130f, 32f, out _);
        ready.onClick.AddListener(() =>
        {
            if (!ReferenceEquals(owner, _canvas)) return;
            Mode?.ReadyUp();
            _close?.Invoke();
        });
        _keep = ArenaKitUi.Button(rect, "Tactical Buy Keep", style, "NO PURCHASE", 14f,
            right - 110f - 10f - 130f - 10f - 150f, 20f, 150f, 32f, out _keepLabel);
        _keep.onClick.AddListener(() =>
        {
            if (!ReferenceEquals(owner, _canvas)) return;
            if (Mode?.ClearPurchase() == true) _cardsStale = true;
        });

        _browser.PicturesReady = Warmed;
        _browser.Create(rect, style, Margin, 116f, WindowWidth - 2f * Margin, WindowHeight - 116f - Margin);
    }

    private void Buy(ArenaKitCard card, IReadOnlyList<KitDefinition> kits)
    {
        TacticalMode mode = Mode;
        KitDefinition kit = card == null ? null : Find(kits, card.Id);
        if (mode == null || kit == null) return;
        if (mode.TryBuy(kit)) _cardsStale = true;
    }

    private void HoldEvents()
    {
        UIEventSystem.Instance.SetTemporaryStatus(isActive: true);
        _eventsHeld = true;
    }

    private void ReleaseEvents()
    {
        if (!_eventsHeld) return;
        _eventsHeld = false;
        try
        {
            // The flag is one bool the console shares: leave it on while the console is open,
            // which switches it off itself when it closes.
            ConsoleScreen console = MonoBehaviourSingleton<PreloaderUI>.Instantiated
                ? MonoBehaviourSingleton<PreloaderUI>.Instance.Console : null;
            if (console != null && console.IsConsoleVisible) return;
            UIEventSystem.Instance.SetTemporaryStatus(isActive: false);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Tactical buy screen could not hand the UI event system back: {ex.Message}");
        }
    }

    private static TacticalMode Mode =>
        Session.ArenaSession.IsActive ? ArenaMatchService.Current?.Mode as TacticalMode : null;

    private static KitDefinition Find(IReadOnlyList<KitDefinition> kits, string id)
    {
        foreach (KitDefinition kit in kits)
            if (kit != null && string.Equals(kit.Id, id, StringComparison.OrdinalIgnoreCase)) return kit;
        return null;
    }
}
