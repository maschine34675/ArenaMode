using System;
using Comfort.Common;
using EFT;
using EFT.UI;
using Redeploy.Core.Engine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaMode.Session;

internal static class ArenaNativeNotices
{
    private static MenuScreen.MainMenuScreenController _home;
    private static CanvasGroup _betaMask, _versionMask;
    private static GameWorld _raidWorld;
    private static LocalizedText _label;

    internal static void Enable()
    {
        // Registered after ArenaSession: only a confirmed game/world pair receives the raid mask.
        RaidLifecycle.OnRaidStarted += RaidStarted;
        RaidLifecycle.OnRaidEnded += ReleaseRaid;
    }

    internal static void MenuShown(MenuScreen screen, MenuScreen.MainMenuBaseScreenController controller)
    {
        if (ArenaSessionMode.GateActive && !ArenaSession.IsActive &&
            controller is MenuScreen.MainMenuScreenController home && screen._alphaWarningGameObject != null)
        {
            if (ReferenceEquals(_home, home) && _betaMask != null &&
                _betaMask.gameObject == screen._alphaWarningGameObject) return;
            ReleaseHome();
            _betaMask = Hide(screen._alphaWarningGameObject);
            if (_betaMask == null) return;
            _home = home;
            _home.OnClose += ReleaseHome;
        }
        else ReleaseHome(); // EFT also uses this screen for raid pause and reconnect.
    }

    private static void ReleaseHome()
    {
        if (_home != null) _home.OnClose -= ReleaseHome;
        _home = null;
        Remove(ref _betaMask);
    }

    private static void RaidStarted(GameWorld world)
    {
        ReleaseRaid();
        if (!IsArenaWorld(world)) return;
        _raidWorld = world;
        Canvas.willRenderCanvases += RefreshVersion;
        RefreshVersion();
    }

    private static void RefreshVersion()
    {
        if (!IsArenaWorld(_raidWorld))
        {
            // The preloader outlives the world, including a world whose end callback was missed.
            ReleaseRaid();
            return;
        }
        var label = PreloaderUI.Instantiated ? PreloaderUI.Instance._alphaVersionLabel : null;
        if (ReferenceEquals(_label, label)) return;
        Remove(ref _versionMask);
        _label = label;
        if (label != null) _versionMask = Hide(label.gameObject);
    }

    private static bool IsArenaWorld(GameWorld world) => ArenaSession.IsActive && world != null &&
        Singleton<GameWorld>.Instantiated && ReferenceEquals(world, Singleton<GameWorld>.Instance) &&
        Singleton<AbstractGame>.Instantiated && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world);

    private static void ReleaseRaid()
    {
        if (!ReferenceEquals(_raidWorld, null)) Canvas.willRenderCanvases -= RefreshVersion;
        _raidWorld = null;
        _label = null;
        Remove(ref _versionMask);
    }

    // Keep native activeSelf, text and other mods' groups intact. EFT continues to update the
    // corner string while hidden, so the menu can immediately display its current value.
    private static CanvasGroup Hide(GameObject target)
    {
        CanvasGroup group = null;
        try
        {
            group = target.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }
        catch (Exception ex)
        {
            Remove(ref group);
            Plugin.ModLogger?.LogWarning("Arena could not hide " + target.name + ": " + ex.Message);
            return null;
        }
    }

    private static void Remove(ref CanvasGroup group)
    {
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = group.blocksRaycasts = true;
            group.enabled = false;
            Object.Destroy(group);
        }
        group = null;
    }

    internal static void Shutdown()
    {
        RaidLifecycle.OnRaidStarted -= RaidStarted;
        RaidLifecycle.OnRaidEnded -= ReleaseRaid;
        ReleaseRaid();
        ReleaseHome();
    }
}
