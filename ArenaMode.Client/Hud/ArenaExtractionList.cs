using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using Redeploy.Core.Engine;
using System;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Hide the exit list, not the raid clock or the actual extraction countdown.</summary>
internal static class ArenaExtractionList
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ExtractionList");
    private static CanvasGroup _mask;
    private static RectTransform _failedList;
    private static float _restoreAlpha;
    private static bool _restoreEnabled;
    private static bool _enabled, _reported, _maskedLogged;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(ExtractionTimersPanel),
                nameof(ExtractionTimersPanel.ShowTimer), new[] { typeof(bool), typeof(bool) });
            if (target == null || target.ReturnType != typeof(void))
                throw new MissingMethodException("ExtractionTimersPanel.ShowTimer(bool, bool)");
            Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaExtractionList), nameof(BeforeShowTimer)));
            // Registered after ArenaSession: its synchronous confirmation must
            // succeed before the initial, already-running vanilla list is hidden.
            RaidLifecycle.OnRaidStarted += OnRaidStarted;
            RaidLifecycle.OnRaidEnded += Release;
            _enabled = true;
        }
        catch (Exception ex)
        {
            Shutdown();
            Plugin.ModLogger.LogWarning("Arena extraction-list suppression unavailable: " + ex.Message);
        }
    }

    private static void OnRaidStarted(GameWorld world)
    {
        Release();
        _failedList = null;
        _reported = _maskedLogged = false;
        Tick();
    }

    private static ExtractionTimersPanel ActivePanel()
    {
        if (!ArenaSession.IsActive || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated)
            return null;
        var game = Singleton<AbstractGame>.Instance;
        var world = Singleton<GameWorld>.Instance;
        return game != null && world != null && ArenaSession.IsExactActiveRaid(game, world)
            ? game.GameUi?.TimerPanel : null;
    }

    internal static bool BeforeShowTimer(ExtractionTimersPanel __instance, bool showExits)
    {
        if (!_enabled || !ArenaSession.IsActive || !showExits) return true;
        try
        {
            var panel = ActivePanel();
            // A failed/late mask must also leave the native show path available.
            return __instance == null || !ReferenceEquals(__instance, panel)
                || _mask == null || !_mask.enabled || panel._timersPanel == null
                || _mask.alpha != 0f
                || !ReferenceEquals(_mask.gameObject, panel._timersPanel.gameObject);
        }
        catch (Exception ex) { Report(ex); return true; }
    }

    internal static void Tick()
    {
        if (!_enabled) return;
        RectTransform list = null;
        try
        {
            var panel = ActivePanel();
            list = panel != null ? panel._timersPanel : null;
            if (list == null) { Release(); return; }
            if (_mask != null && !ReferenceEquals(_mask.gameObject, list.gameObject)) Release();
            if (ReferenceEquals(list, _failedList)) return;
            if (_mask == null)
            {
                // The installed native PointsPanel already has a CanvasGroup.
                // Unity rejects a second one and returns null (not an exception
                // from AddComponent). Never add components in this polling path.
                var native = list.GetComponent<CanvasGroup>();
                if (native == null)
                    throw new MissingComponentException("Native extraction list has no CanvasGroup; leaving it unchanged.");
                _restoreAlpha = native.alpha;
                _restoreEnabled = native.enabled;
                _mask = native;
            }
            _mask.alpha = 0f;
            _mask.enabled = true;
            if (!_maskedLogged)
            {
                _maskedLogged = true;
                Plugin.ModLogger.LogInfo("Arena extraction-list: existing native list CanvasGroup hidden.");
            }
        }
        catch (Exception ex)
        {
            Release();
            _failedList = list; // Retry a new list/raid, never a broken object every frame.
            Report(ex);
        }
    }

    internal static void Release()
    {
        var mask = _mask;
        _mask = null;
        _failedList = null;
        if (mask == null) return;
        // Borrow only alpha/enabled. Native startup animates pivot/position and
        // activeSelf, which we never touch. Restore synchronously; never destroy
        // a native component or overwrite an external value we no longer own.
        try
        {
            if (mask.alpha == 0f) mask.alpha = _restoreAlpha;
            if (mask.enabled) mask.enabled = _restoreEnabled;
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        Plugin.ModLogger.LogWarning("Arena extraction-list presentation failed; native UI remains available: " + ex);
    }

    internal static void Shutdown()
    {
        RaidLifecycle.OnRaidStarted -= OnRaidStarted;
        RaidLifecycle.OnRaidEnded -= Release;
        _enabled = false;
        Release();
        _failedList = null;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex)
        {
            // Never prevent the remaining Plugin.OnDestroy services from cleaning
            // up. If Harmony leaves this hook installed, !_enabled fails open.
            Plugin.ModLogger.LogWarning("Arena extraction-list unpatch failed; hook disabled: " + ex.Message);
        }
    }
}
