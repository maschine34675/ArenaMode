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
    private static Canvas _canvas;
    private static CanvasGroup _combatGroup;
    private static ArenaMatch _match;
    private static ScorePanel _score;
    private static KillfeedView _killfeed;
    private static CalloutElement _callout;
    private static HitmarkerElement _hitmarker;
    private static DamageNumbersElement _numbers;
    private static ArenaAssemblyBanner _assembly;
    private static ArenaPowerupPanel _powerups;
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
            match.OnCallout += OnAwardCallout;
            match.OnTextCallout += OnTextCallout;
            match.OnAssist += OnAssist;
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
            _match.OnCallout -= OnAwardCallout;
            _match.OnTextCallout -= OnTextCallout;
            _match.OnAssist -= OnAssist;
            ArenaEvents.OnLocalDamageDealt -= OnDamage;
            _match = null;
        }

        if (_canvasGo != null)
        {
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
        }
        _canvas = null;
        _combatGroup = null;

        _score = null;
        _killfeed = null;
        _callout = null;
        _hitmarker = null;
        _numbers = null;
        _assembly = null;
        _powerups = null;
    }

    public static void Tick()
    {
        if (_match == null || _canvasGo == null)
            return;

        try
        {
            ArenaHudVisibility.Apply(_canvas);
            _combatGroup.alpha = Settings.CombatHudEnabled.Value ? 1f : 0f;
            _score.Tick(_match);
            _killfeed.Tick();
            _callout.Tick();
            _hitmarker.Tick();
            _numbers.Tick();
            _assembly.Tick(_match);
            _powerups.Tick();
            _friendlyMarkers.Tick(_match);
            _infoPanels?.Tick();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena HUD tick failed — HUD disabled for this raid: {ex}");
            Detach();
        }
    }

    internal static void RefreshMatchNotice() => _assembly?.Tick(_match);

    private static void Build(ArenaMatch match)
    {
        _canvasGo = new GameObject("ArenaHudCanvas");
        _canvasGo.transform.SetParent(Plugin.Instance.transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        _canvas = canvas;
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
        // Keep TMP objects active/ticking so expired cues cannot accumulate
        // while hidden. The separate group leaves essential match UI visible.
        var combatGo = new GameObject("ArenaCombatHud", typeof(RectTransform), typeof(CanvasGroup));
        var combatRoot = (RectTransform)combatGo.transform;
        combatRoot.SetParent(root, false);
        combatRoot.anchorMin = Vector2.zero;
        combatRoot.anchorMax = Vector2.one;
        combatRoot.offsetMin = combatRoot.offsetMax = Vector2.zero;
        _combatGroup = combatGo.GetComponent<CanvasGroup>();
        _combatGroup.interactable = _combatGroup.blocksRaycasts = false;
        _combatGroup.alpha = Settings.CombatHudEnabled.Value ? 1f : 0f;
        _score = new ScorePanel();
        _score.Create(root);
        _killfeed = new KillfeedView();
        _killfeed.Create(combatRoot);
        _callout = new CalloutElement();
        _callout.Create(root);
        _hitmarker = new HitmarkerElement();
        _hitmarker.Create(combatRoot);
        _numbers = new DamageNumbersElement();
        _numbers.Create(combatRoot, canvas);
        _assembly = new ArenaAssemblyBanner();
        _assembly.Create(root);
        _powerups = new ArenaPowerupPanel();
        _powerups.Create(root);
        _friendlyMarkers = new ArenaFriendlyMarkers();
        _friendlyMarkers.Create(root);
        _infoPanels = new InfoPanels.ArenaInfoPanels(combatRoot, match);
    }

    private static void OnKill(ArenaKill kill)
    {
        if (!Settings.CombatHudEnabled.Value) return;
        // Independent of the killfeed toggle and its other-player filter.
        if (Settings.ShowHitmarker.Value)
            _hitmarker?.ShowKill(kill);
        if (_killfeed == null || !Settings.ShowKillfeed.Value)
            return;

        if (!kill.KilledByLocal && !kill.LocalIsVictim && !Settings.KillfeedOtherKills.Value)
            return;

        _killfeed.Push(kill);
    }

    /// <summary>
    /// Kill/award callouts ("DOUBLE KILL", "SHUTDOWN", ...) are the announcer's
    /// on-screen half: they follow the same gate as its stingers (live master
    /// switch AND the per-mode selection frozen at arm), so the ANNOUNCER
    /// mutator hides the text together with the sound. tools/AnnouncerChecks
    /// pins this routing by source text because the HUD is Unity-bound.
    /// </summary>
    private static void OnAwardCallout(string label)
    {
        if (!Audio.AnnouncerService.IsEnabledForLiveMatch())
            return;
        _callout?.Show(label);
    }

    /// <summary>
    /// HUD-only text ("WAVE n", the own-grenade taunt) is not announcer output
    /// and keeps showing whatever the announcer switch says.
    /// </summary>
    private static void OnTextCallout(string label) => _callout?.Show(label);

    internal static void ShowUncreditedDeath(ArenaMatch match, ArenaUncreditedDeath death)
    {
        if (!Settings.CombatHudEnabled.Value || !ReferenceEquals(_match, match) || _killfeed == null || !Settings.ShowKillfeed.Value
            || (!death.LocalIsVictim && !Settings.KillfeedOtherKills.Value)) return;
        _killfeed.PushUncreditedDeath(death);
    }

    private static void OnAssist(ArenaAssist assist)
    {
        if (assist.IsLocal) _score?.ShowAssist(assist.Points);
    }

    private static void OnDamage(ArenaDamage damage)
    {
        if (!Settings.CombatHudEnabled.Value) return;
        if (Settings.ShowHitmarker.Value)
            _hitmarker?.Show(damage);
        if (Settings.ShowDamageNumbers.Value && damage.BodyDamage + damage.ArmorDamage > 0.5f)
            _numbers?.Spawn(damage);
    }
}
