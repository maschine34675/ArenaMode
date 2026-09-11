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
    private static bool _enabled, _reported;

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
        _reported = false;
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
                || !ReferenceEquals(_mask.gameObject, panel._timersPanel.gameObject);
        }
        catch (Exception ex) { Report(ex); return true; }
    }

    internal static void Tick()
    {
        if (!_enabled) return;
        try
        {
            var panel = ActivePanel();
            var list = panel != null ? panel._timersPanel : null;
            if (list == null) { Release(); return; }
            if (_mask != null && !ReferenceEquals(_mask.gameObject, list.gameObject)) Release();
            if (_mask == null)
            {
                // CanvasGroup allows multiple components. Always add our own;
                // never overwrite native/other-mod alpha or reparent the list.
                _mask = list.gameObject.AddComponent<CanvasGroup>();
                _mask.interactable = false;
                _mask.blocksRaycasts = false;
                _mask.ignoreParentGroups = false;
            }
            _mask.alpha = 0f;
        }
        catch (Exception ex) { Release(); Report(ex); }
    }

    internal static void Release()
    {
        var mask = _mask;
        _mask = null;
        if (mask == null) return;
        // Destroy is deferred by Unity. Disable first so a reused normal-raid
        // panel is restored in this frame, without touching any borrowed group.
        try { mask.enabled = false; UnityEngine.Object.Destroy(mask); }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        Plugin.ModLogger.LogWarning("Arena extraction-list presentation failed; native UI remains available: " + ex.Message);
    }

    internal static void Shutdown()
    {
        RaidLifecycle.OnRaidStarted -= OnRaidStarted;
        RaidLifecycle.OnRaidEnded -= Release;
        _enabled = false;
        Release();
        try { Patches.UnpatchSelf(); }
        catch (Exception ex)
        {
            // Never prevent the remaining Plugin.OnDestroy services from cleaning
            // up. If Harmony leaves this hook installed, !_enabled fails open.
            Plugin.ModLogger.LogWarning("Arena extraction-list unpatch failed; hook disabled: " + ex.Message);
        }
    }
}
