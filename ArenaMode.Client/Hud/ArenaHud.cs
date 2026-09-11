using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Modes;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// The arena overlay: score panel, killfeed, callouts, hitmarker and damage
/// numbers and absorbed weapon/status panels on one ScreenSpaceOverlay canvas.
/// Built per match, destroyed at raid end — no player state
/// survives between raids by construction.
/// </summary>
internal static class ArenaHud
{
    private static GameObject _canvasGo;
    private static ArenaMatch _match;
    private static ScorePanel _score;
    private static KillfeedView _killfeed;
    private static CalloutElement _callout;
    private static HitmarkerElement _hitmarker;
    private static DamageNumbersElement _numbers;
    private static ArenaAssemblyBanner _assembly;
    private static ArenaFriendlyMarkers _friendlyMarkers;
    private static InfoPanels.ArenaInfoPanels _infoPanels;

    public static void Attach(ArenaMatch match)
    {
        Detach();
        if (!Settings.HudEnabled.Value || match == null)
            return;

        try
        {
            Build(match);
            _match = match;
            match.OnKill += OnKill;
            match.OnCallout += OnCallout;
            ArenaEvents.OnLocalDamageDealt += OnDamage;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena HUD build failed: {ex}");
            Detach();
        }
    }

    public static void Detach()
    {
        _infoPanels?.Dispose();
        _infoPanels = null;
        _friendlyMarkers?.Dispose();
        _friendlyMarkers = null;
        if (_match != null)
        {
            _match.OnKill -= OnKill;
            _match.OnCallout -= OnCallout;
            ArenaEvents.OnLocalDamageDealt -= OnDamage;
            _match = null;
        }

        if (_canvasGo != null)
        {
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
        }

        _score = null;
        _killfeed = null;
        _callout = null;
        _hitmarker = null;
        _numbers = null;
        _assembly = null;
    }

    public static void Tick()
    {
        if (_match == null || _canvasGo == null)
            return;

        try
        {
            _score.Tick(_match);
            _killfeed.Tick();
            _callout.Tick();
            _hitmarker.Tick();
            _numbers.Tick();
            _assembly.Tick(_match);
            _friendlyMarkers.Tick(_match);
            _infoPanels?.Tick();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena HUD tick failed — HUD disabled for this raid: {ex}");
            Detach();
        }
    }

    private static void Build(ArenaMatch match)
    {
        _canvasGo = new GameObject("ArenaHudCanvas");
        _canvasGo.transform.SetParent(Plugin.Instance.transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 130;
        canvas.overrideSorting = true;
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // No DontDestroyOnLoad: the canvas is a CHILD of the persistent plugin
        // object (DDOL on non-roots is a no-op with a warning) and is torn down
        // per match anyway.

        var root = (RectTransform)_canvasGo.transform;
        _score = new ScorePanel();
        _score.Create(root);
        _killfeed = new KillfeedView();
        _killfeed.Create(root);
        _callout = new CalloutElement();
        _callout.Create(root);
        _hitmarker = new HitmarkerElement();
        _hitmarker.Create(root);
        _numbers = new DamageNumbersElement();
        _numbers.Create(root, canvas);
        _assembly = new ArenaAssemblyBanner();
        _assembly.Create(root);
        _friendlyMarkers = new ArenaFriendlyMarkers();
        _friendlyMarkers.Create(root);
        _infoPanels = new InfoPanels.ArenaInfoPanels(root, match);
    }

    private static void OnKill(ArenaKill kill)
    {
        if (_killfeed == null || !Settings.ShowKillfeed.Value)
            return;

        if (!kill.KilledByLocal && !kill.LocalIsVictim && !Settings.KillfeedOtherKills.Value)
            return;

        _killfeed.Push(kill);
    }

    private static void OnCallout(string label) => _callout?.Show(label);

    private static void OnDamage(ArenaDamage damage)
    {
        if (Settings.ShowHitmarker.Value)
            _hitmarker?.Show(damage);
        if (Settings.ShowDamageNumbers.Value && damage.BodyDamage + damage.ArmorDamage > 0.5f)
            _numbers?.Spawn(damage);
    }
}
