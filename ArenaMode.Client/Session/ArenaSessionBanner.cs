using ArenaMode.Compat;
using ArenaMode.Shared.Session;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>One compact menu-only session strip; it never owns raid admission.</summary>
internal static class ArenaSessionBanner
{
    private static readonly AccessTools.FieldRef<ItemUiContext, List<ItemUiContext.WindowData>> Windows =
        AccessTools.FieldRefAccess<ItemUiContext, List<ItemUiContext.WindowData>>("_windows");
    private static readonly AccessTools.FieldRef<ItemUiContext, SplitDialog> Split =
        AccessTools.FieldRefAccess<ItemUiContext, SplitDialog>("_splitDialog");
    private static readonly List<UIInputNode> WindowNodes = new();
    private static readonly List<BaseUiWindow> PanelWindows = new();
    private static readonly Dictionary<Type, bool> WindowTypes = new();
    private static GameObject _root;
    private static TextMeshProUGUI _status, _actionText;
    private static Button _action;
    private static readonly ArenaTwoClickGate Confirmation = new();
    private static EEftScreenType _lastScreen;
    private static bool _loggedFailure;
    private static float _nextRefresh;

    internal static void Tick()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return;
            _nextRefresh = now + .1f;
            var screen = EftScreenManager.Instance.CurrentScreenController?.ScreenType ?? EEftScreenType.None;
            bool privateSession = ArenaSessionMode.IsArenaSession;
            bool visible = !RaidWorldActive() && !ArenaSession.IsActive
                && !PitFireTeamCompat.IsSquadManagementScreen && !ModalOpen()
                && (privateSession ? IsMenu(screen) : screen == EEftScreenType.MainMenu);
            if (!visible)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                Confirmation.Reset();
                return;
            }
            bool created = _root == null;
            if (created) Create();
            if (_root == null) return;
            if (created || _lastScreen != screen)
            {
                Confirmation.Reset();
                UpdateLayout(screen);
            }
            _lastScreen = screen;
            _root.SetActive(true);
            string status = privateSession
                ? "ARENA SESSION · FILTERED MODS\nChanges reset · Rewards optional"
                : "ARENA · FAST COMBAT · PROTECTED GEAR\nEscape from Tarkov → Arena";
            if (_status.text != status) _status.text = status;
            _action.gameObject.SetActive(privateSession);
            _action.interactable = !ArenaSessionSwitch.HelperPending && ProfileId() != null;
            int seconds = Confirmation.SecondsRemaining(now);
            string actionText = ArenaSessionSwitch.HelperPending ? "PREPARING RESTART…"
                : seconds > 0 ? "CONFIRM RESTART · " + seconds + "s"
                : ArenaWhitelistAdmission.ReturnActionLabel;
            if (_actionText.text != actionText) _actionText.text = actionText;
            _action.targetGraphic.color = seconds > 0
                ? new Color(.72f, .30f, .22f, 1f) : new Color(.22f, .48f, .62f, 1f);
        }
        catch (Exception error)
        {
            if (_root != null) _root.SetActive(false);
            if (!_loggedFailure)
                Plugin.ModLogger?.LogWarning("Arena menu session strip unavailable: " + error.Message);
            _loggedFailure = true;
        }
    }

    private static bool IsMenu(EEftScreenType screen) => screen is
        EEftScreenType.MainMenu or EEftScreenType.SelectRaidSide or EEftScreenType.SelectLocation
        or EEftScreenType.MatchMakerAccept or EEftScreenType.OfflineRaid or EEftScreenType.Inventory
        or EEftScreenType.Traders or EEftScreenType.Trader or EEftScreenType.FleaMarket
        or EEftScreenType.Hideout;

    private static string ProfileId()
    {
        if (!Singleton<ClientApplication<IEftSession>>.Instantiated) return null;
        return Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession()?.Profile?.Id;
    }

    // The hideout keeps its world alive after returning to menus; only a raid world blocks the strip.
    private static bool RaidWorldActive() => Singleton<GameWorld>.Instantiated
        && Singleton<GameWorld>.Instance is not HideoutGameWorld;

    private static bool ModalOpen()
    {
        var context = ItemUiContext.Instance;
        if (context == null) return true;
        if (context.MessageWindowActive || context.ScrolledMessageWindowActive
            || Windows(context)?.Count > 0 || Split(context)?.gameObject.activeInHierarchy == true) return true;
        // Buy/renew, quest handover and quest-item unload call Window.Show directly, without
        // registering in ItemUiContext._windows. Inspect active native windows under the UI roots.
        if (HasActiveWindow(context) || HasActiveWindow(MenuUI.Instance) || HasActiveWindow(CommonUI.Instance)
            || MenuUI.Instance?.TradingScreen?._ragfairScreen?._filterWindow?.gameObject.activeInHierarchy == true) return true;
        var preloader = PreloaderUI.Instance;
        if (preloader == null) return true;
        if (HasActiveWindow(preloader) || preloader.ErrorScreen?.gameObject.activeInHierarchy == true
            || preloader.UnknownErrorScreen?.gameObject.activeInHierarchy == true
            || preloader.Console?.IsConsoleVisible == true) return true;
        var errors = preloader._criticalErrorScreenContainer?.transform;
        if (errors != null)
            for (int i = 0; i < errors.childCount; i++)
                if (errors.GetChild(i).gameObject.activeInHierarchy) return true;
        return false;
    }

    private static bool HasActiveWindow(Component root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return false;
        // Raid/co-op settings and build selection use the other native window hierarchy.
        PanelWindows.Clear();
        root.GetComponentsInChildren(false, PanelWindows);
        foreach (var panel in PanelWindows)
            if (panel != null && panel.gameObject.activeInHierarchy) return true;
        WindowNodes.Clear();
        root.GetComponentsInChildren(false, WindowNodes);
        foreach (var node in WindowNodes)
        {
            if (node == null || !node.gameObject.activeInHierarchy) continue;
            var type = node.GetType();
            if (!WindowTypes.TryGetValue(type, out bool isWindow))
            {
                for (var parent = type; parent != null; parent = parent.BaseType)
                    if (parent.IsGenericType && parent.GetGenericTypeDefinition() == typeof(Window<>))
                    {
                        isWindow = true;
                        break;
                    }
                WindowTypes[type] = isWindow;
            }
            if (isWindow) return true;
        }
        return false;
    }

    private static void Return()
    {
        if (_root == null || !_root.activeInHierarchy || !ArenaSessionMode.IsArenaSession
            || RaidWorldActive() || ArenaSession.IsActive || ArenaSessionSwitch.HelperPending
            || PitFireTeamCompat.IsSquadManagementScreen || ModalOpen()
            || !IsMenu(EftScreenManager.Instance.CurrentScreenController?.ScreenType ?? EEftScreenType.None)) return;
        string profile = ProfileId();
        if (profile == null || !Confirmation.Click(Time.realtimeSinceStartup)) return;
        if (!ArenaSessionSwitch.Request(ArenaSessionContract.DirectionLeave, profile, out var error, Fail)) Fail(error);
    }

    private static void Fail(string error)
    {
        Confirmation.Reset();
        NotificationManager.DisplayWarningNotification(error);
    }

    private static void Create()
    {
        var template = MenuUI.Instance?.MatchMakerSideSelectionScreen?._sideDescriptionText;
        if (template?.font == null) return;
        _root = new GameObject("Arena Session Strip", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(Plugin.Instance.transform, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        var strip = new GameObject("Session status", typeof(RectTransform), typeof(Image));
        strip.transform.SetParent(_root.transform, false);
        var rect = (RectTransform)strip.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, -2f);
        rect.sizeDelta = new Vector2(580f, 36f);
        strip.GetComponent<Image>().color = new Color(.06f, .08f, .10f, .96f);
        strip.GetComponent<Image>().raycastTarget = false;
        _status = Text(strip.transform, template, "Status", 13f, 10f, 2f, 320f, 32f);
        var button = new GameObject("Return to SPT", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(strip.transform, false);
        Place((RectTransform)button.transform, 340f, 4f, 230f, 28f);
        _action = button.GetComponent<Button>();
        _action.targetGraphic = button.GetComponent<Image>();
        var navigation = _action.navigation; navigation.mode = Navigation.Mode.None; _action.navigation = navigation;
        _action.onClick.AddListener(Return);
        _actionText = Text(button.transform, template, "Return label", 12f, 4f, 0f, 222f, 28f);
        _actionText.alignment = TextAlignmentOptions.Center;
    }

    private static void UpdateLayout(EEftScreenType screen)
    {
        bool inventory = screen == EEftScreenType.Inventory;
        bool trading = screen is EEftScreenType.Trader or EEftScreenType.Traders or EEftScreenType.FleaMarket;
        var strip = (RectTransform)_status.transform.parent;
        strip.anchorMin = strip.anchorMax = strip.pivot = inventory
            ? Vector2.zero : new Vector2(trading ? .5f : 0f, 1f);
        strip.anchoredPosition = inventory ? new Vector2(14f, 76f)
            : new Vector2(trading ? 0f : 14f, -2f);
        strip.sizeDelta = new Vector2(inventory ? 520f : 580f, 36f);
        Place(_status.rectTransform, 10f, 2f, inventory ? 280f : 320f, 32f);
        Place((RectTransform)_action.transform, inventory ? 300f : 340f, 4f, inventory ? 210f : 230f, 28f);
        Place(_actionText.rectTransform, 4f, 0f, inventory ? 202f : 222f, 28f);
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_Text template, string name, float size,
        float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.richText = false;
        text.raycastTarget = false;
        Place(text.rectTransform, x, y, width, height);
        return text;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    internal static void Shutdown()
    {
        Confirmation.Reset();
        WindowNodes.Clear();
        PanelWindows.Clear();
        WindowTypes.Clear();
        if (_action != null) _action.onClick.RemoveAllListeners();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null; _status = null; _action = null; _actionText = null;
    }
}
