using System;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Redeploy.Core.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Draw behind native profile/loading UI until the home screen finishes its own fade.</summary>
internal sealed class ArenaMenuTransition : MonoBehaviour
{
    private enum Phase { Off, Arrival, Profile, Home }

    private static Phase _phase;
    private static bool _installed, _warned;
    private static EftScreenManager _screens;
    private static MenuScreen.MainMenuScreenController _home;
    private static ArenaMenuTransition _view;
    private ArenaMenuArt.SplashLease _lease;
    private RawImage _image;
    private Vector2 _size;
    private bool _homeCrop;

    internal static void Install(Harmony harmony)
    {
        if (_installed || !ArenaSessionMode.GateActive) return;
        var environment = AccessTools.DeclaredMethod(typeof(EnvironmentUI), nameof(EnvironmentUI.Awake));
        var login = AccessTools.DeclaredMethod(typeof(LoginUI), nameof(LoginUI.Awake));
        var comeback = AccessTools.DeclaredMethod(typeof(TarkovApplication), nameof(TarkovApplication.ComebackToMainMenu));
        if (environment == null || login == null || comeback == null)
            throw new MissingMethodException("Arena menu loading screen lifecycle");
        harmony.Patch(environment, postfix: new HarmonyMethod(typeof(ArenaMenuTransition), nameof(EnvironmentReady)));
        harmony.Patch(login, postfix: new HarmonyMethod(typeof(ArenaMenuTransition), nameof(ObserveScreens)));
        harmony.Patch(comeback, prefix: new HarmonyMethod(typeof(ArenaMenuTransition), nameof(BeginReturn)));
        RaidLifecycle.OnRaidStarted += RaidStarted;
        _installed = true;
        _phase = Phase.Arrival;
        // Late installation must obey the current screen. Creating a screen manager here would
        // change the native startup order, so wait for EnvironmentUI.Awake if it doesn't exist yet.
        ObserveScreens();
        ShowIfReady();
    }

    private static void EnvironmentReady() => ShowIfReady();

    private static void ObserveScreens()
    {
        // LoginUI.Awake registers all initial screens on this same native manager before returning.
        // The event runs after the current controller is assigned, including backward navigation.
        if (!_installed || !Singleton<IScreenManager>.Instantiated ||
            Singleton<IScreenManager>.Instance is not EftScreenManager manager || ReferenceEquals(manager, _screens)) return;
        if (_screens != null) _screens.OnScreenChanged -= ScreenChanged;
        _screens = manager;
        _screens.OnScreenChanged += ScreenChanged;
        ScreenChanged(EEftScreenType.None);
    }

    private static void BeginReturn()
    {
        if (!_installed) return;
        _home = null;
        _phase = Phase.Arrival;
        // ComebackToMainMenu can still be waiting for the results/raid scenes to unload. CloseAllScreens
        // subsequently publishes the empty controller. Do not change the screen it is still closing.
        if (_screens?.CurrentScreenController == null) ShowIfReady();
    }

    private static void ScreenChanged(EEftScreenType screenType)
    {
        if (!_installed) return;
        var controller = _screens.CurrentScreenController;
        if (controller is ProfileLoadingScreen.ProfileLoadingScreenController)
        {
            _home = null;
            _phase = Phase.Profile;
        }
        else if (controller is MenuScreen.MainMenuScreenController home)
        {
            _home = home;
            _phase = Phase.Home;
        }
        else if (controller == null && (_phase == Phase.Arrival || _phase == Phase.Profile))
            _phase = Phase.Arrival;
        else
        {
            Finish();
            return;
        }
        ShowIfReady();
    }

    private static void ShowIfReady()
    {
        if (!_installed || _phase == Phase.Off || !ArenaSessionMode.GateActive || ArenaSession.IsActive ||
            !EnvironmentUI.Instantiated || EnvironmentUI.Instance._commonContainer == null) return;
        try
        {
            if (_view == null)
            {
                // In the shipped scenes the environment's shading canvas is -1000, Login/Common/Menu
                // UI use 0, and Preloader uses 100 and above. This draws over the scenery, behind native
                // status, windows and controls. Hiding the native environment also hides our surface.
                var host = EnvironmentUI.Instance._commonContainer;
                var root = new GameObject("Arena Menu Loading Background", typeof(RectTransform), typeof(Canvas), typeof(ArenaMenuTransition));
                _view = root.GetComponent<ArenaMenuTransition>();
                root.transform.SetParent(host.transform, false);
                root.layer = host.layer;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -1;
                _view._lease = ArenaMenuArt.AcquireSplash();
                if (_view._lease == null) { Finish(); return; }
                var art = new GameObject("Training ground", typeof(RectTransform), typeof(RawImage));
                art.layer = root.layer;
                art.transform.SetParent(root.transform, false);
                var image = _view._image = art.GetComponent<RawImage>();
                image.raycastTarget = false;
                image.texture = _view._lease.Texture;
                image.rectTransform.anchorMin = Vector2.zero;
                image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            }
            _view.RefreshCrop();
        }
        catch (Exception error) { Fail(error); }
    }

    private void RefreshCrop()
    {
        var size = _image.rectTransform.rect.size;
        bool home = _phase == Phase.Home;
        if (size.x <= 0f || size.y <= 0f || (size == _size && home == _homeCrop)) return;
        _size = size;
        _homeCrop = home;
        Texture2D texture = _lease.Texture;
        _image.uvRect = ArenaMenuArt.CoverUv(size, (float)texture.width / texture.height, home);
    }

    private void LateUpdate()
    {
        // On backward navigation EFT sets its current controller after MenuScreen.Show. Waiting for
        // both that owner and its real CanvasGroup fade keeps the stock camera from flashing through.
        try
        {
            if (ArenaSession.IsActive || (_phase == Phase.Home && ArenaMenuBackdrop.VisibleFor(_home) &&
                _home?.Screen != null && !_home.Closed && _home.Screen.CanvasGroup.alpha >= 1f)) Finish();
            else RefreshCrop();
        }
        catch (Exception error) { Fail(error); }
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(this, _view)) _view = null;
        _lease?.Dispose();
    }

    private static void Finish()
    {
        _phase = Phase.Off;
        _home = null;
        var view = _view;
        _view = null;
        if (view == null) return;
        view.gameObject.SetActive(false);
        if (view._image != null) view._image.texture = null;
        view._lease?.Dispose();
        Destroy(view.gameObject);
    }

    private static void RaidStarted(GameWorld world) => Finish();

    private static void Fail(Exception error)
    {
        Finish();
        if (!_warned) Plugin.ModLogger?.LogWarning("Arena menu loading background unavailable: " + error.Message);
        _warned = true;
    }

    internal static void Shutdown()
    {
        if (_installed) RaidLifecycle.OnRaidStarted -= RaidStarted;
        if (_screens != null) _screens.OnScreenChanged -= ScreenChanged;
        _screens = null;
        _installed = false;
        Finish();
    }
}
