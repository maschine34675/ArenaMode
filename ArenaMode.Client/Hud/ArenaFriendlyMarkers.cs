using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Small, pooled friendly-bot diamonds. Every render obtains exact current bodies
/// and fresh head line-of-sight; no last-known-position or positive LOS cache.
/// Activate UI before CanvasUpdateRegistry builds its graphics. Camera pre-cull
/// is too late: disabling a Graphic clears its mesh, and enabling only queues a
/// rebuild. Screen-space UI never intercepts input.
/// </summary>
internal sealed class ArenaFriendlyMarkers : IDisposable
{
    private sealed class View
    {
        internal RectTransform Rect;
        internal Image Fill;
    }

    private readonly View[] _views = new View[ArenaFriendlyMarkerPolicy.Capacity];
    private readonly HashSet<Player> _seen = new();
    private RectTransform _root;
    private ArenaMatch _match;
    private bool _subscribed;
    private bool _warned;
    private int _canvasPasses, _samples, _scanned, _friendly, _heads, _projected, _blocked, _shown;
    private float _nextDiagnostic;

    internal void Create(RectTransform root)
    {
        Dispose();
        if (root == null) return;
        _root = root;
        _warned = false;
        _canvasPasses = _samples = 0;
        _nextDiagnostic = Time.unscaledTime + 5f;
        for (int i = 0; i < _views.Length; i++)
        {
            var outer = new GameObject("ArenaFriendlyDiamond" + i, typeof(RectTransform));
            var rect = (RectTransform)outer.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(10f, 10f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var border = outer.AddComponent<Image>();
            border.color = new Color(0f, 0f, 0f, 0.8f);
            border.raycastTarget = false;
            var inner = new GameObject("Fill", typeof(RectTransform));
            var innerRect = (RectTransform)inner.transform;
            innerRect.SetParent(rect, false);
            innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
            innerRect.sizeDelta = new Vector2(7f, 7f);
            var fill = inner.AddComponent<Image>();
            fill.raycastTarget = false;
            outer.SetActive(false);
            _views[i] = new View { Rect = rect, Fill = fill };
        }
        Canvas.preWillRenderCanvases += BeforeCanvasRebuild;
        _subscribed = true;
    }

    internal void Tick(ArenaMatch match)
    {
        // Hide previous-frame data immediately, even if no game camera renders.
        HideAll();
        _match = IsScope(match) ? match : null;
        // This watchdog is outside the render callback: zero callbacks must be
        // diagnosable too. Three samples per HUD lifetime, no per-frame log spam.
        if (ArenaSession.IsActive && _samples < 3 && Time.unscaledTime >= _nextDiagnostic
            && (match?.Mode is TeamDeathmatchMode || match?.Mode is TacticalMode tactical && tactical.IsTeamArena))
        {
            _samples++;
            _nextDiagnostic = Time.unscaledTime + 10f;
            var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
            Plugin.ModLogger.LogInfo($"Arena friendly markers: phase=canvas-pre-rebuild, scope={_match != null}, " +
                $"cameraReady={camera != null && camera.isActiveAndEnabled}, canvasPasses={_canvasPasses}, " +
                $"lastPass alive={_scanned}, friendly={_friendly}, heads={_heads}, projected={_projected}, " +
                $"blocked={_blocked}, shown={_shown}.");
        }
    }

    public void Dispose()
    {
        if (_subscribed) Canvas.preWillRenderCanvases -= BeforeCanvasRebuild;
        _subscribed = false;
        _match = null;
        for (int i = 0; i < _views.Length; i++)
        {
            if (_views[i]?.Rect != null) UnityEngine.Object.Destroy(_views[i].Rect.gameObject);
            _views[i] = null;
        }
        _seen.Clear();
        _root = null;
    }

    private static bool IsScope(ArenaMatch match)
        => ArenaSession.IsActive && Settings.HudEnabled?.Value == true
           && Settings.ShowFriendlyMarkers?.Value == true
           && match?.Mode != null && ReferenceEquals(match, ArenaMatchService.Current)
           && !match.Mode.IsComplete
           && ((match.Mode is TeamDeathmatchMode && ArenaSession.ActiveMode == "teamdeathmatch")
               || (match.Mode is TacticalMode tactical && tactical.IsTeamArena
                   && ArenaSession.ActiveMode == "tactical"))
           && Singleton<GameWorld>.Instantiated && Singleton<AbstractGame>.Instantiated
           && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance);

    private void BeforeCanvasRebuild()
    {
        try
        {
            _canvasPasses++;
            if (_root == null) { Dispose(); return; }
            if (!IsScope(_match)) { HideAll(); return; }
            // Never create CameraManager from a UI callback. Only the current
            // gameplay camera provides projection; scope/secondary cameras don't.
            if (!CameraManager.Exist || CameraManager.Instance.Camera == null) { HideAll(); return; }
            var camera = CameraManager.Instance.Camera;
            if (!camera.isActiveAndEnabled) { HideAll(); return; }
            Render(camera);
        }
        catch (Exception ex)
        {
            HideAll();
            if (!_warned)
            {
                _warned = true;
                Plugin.ModLogger.LogWarning("Arena friendly markers stayed hidden: " + ex.GetBaseException().Message);
            }
        }
    }

    private void Render(Camera camera)
    {
        HideAll();
        _seen.Clear();
        _scanned = _friendly = _heads = _projected = _blocked = _shown = 0;
        var world = Singleton<GameWorld>.Instance;
        var players = world.AllAlivePlayersList;
        if (players == null) return;
        Vector3 origin = camera.transform.position;
        if (!ArenaFriendlyMarkerPolicy.Finite(origin)) return;
        Color color = ArenaFriendlyMarkerPolicy.Color(ArenaTracerPalette.Resolve(Settings.FriendlyTracerColor.Value));
        // Native high-poly scenery plus physical doors and low-poly blocking
        // proxies. Transparent colliders conservatively hide markers at glass.
        int mask = (int)LayersMaskController.HighPolyWithTerrainMask
                   | (int)LayersMaskController.LowPolyColliderLayerMask
                   | (int)LayersMaskController.PlayerStaticDoorMask
                   | (int)LayersMaskController.TransparentLayerMask;
        if (mask == 0) return;
        int displayed = 0, rays = 0;
        int count = Math.Min(players.Count, ArenaFriendlyMarkerPolicy.MaximumScannedPlayers);
        _scanned = count;
        for (int i = 0; i < count && displayed < _views.Length && rays < _views.Length; i++)
        {
            Player player = players[i];
            if (player == null || !player.IsAI || ReferenceEquals(player, world.MainPlayer)
                || player.HealthController?.IsAlive != true || !_seen.Add(player)
                || !IsFriendly(player)) continue;
            _friendly++;
            // EFT can virtualize bone poses. Its wrapper selects the current
            // accumulated/imitated pose; Original alone can be stale/offscreen.
            var head = player.PlayerBones?.Head;
            if (head == null || (!head.UseImitation && !head.AccumulatePositionAndRotation && head.Original == null)) continue;
            Vector3 headPosition = head.position;
            if (!ArenaFriendlyMarkerPolicy.Finite(headPosition)) continue;
            _heads++;
            Vector3 viewport = camera.WorldToViewportPoint(headPosition);
            float distance = Vector3.Distance(origin, headPosition);
            if (!ArenaFriendlyMarkerPolicy.Projectable(viewport, distance)) continue;
            _projected++;
            rays++;
            if (Physics.Linecast(origin, headPosition, mask, QueryTriggerInteraction.Ignore)) { _blocked++; continue; }
            Vector3 screen = camera.WorldToScreenPoint(headPosition);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local)) continue;
            View view = _views[displayed++];
            view.Rect.anchoredPosition = local + new Vector2(0f, 30f);
            view.Fill.color = color;
            view.Rect.gameObject.SetActive(true);
            _shown = displayed;
        }
        // Only native bodies encountered during this render stay temporarily in
        // the dedupe set. No corpse or old-round object is retained between frames.
        _seen.Clear();
    }

    private bool IsFriendly(Player player)
        => _match.Mode is TeamDeathmatchMode team
            ? team.TryClassifyCurrentBody(player, out bool friendly) && friendly
            : _match.Mode is TacticalMode tactical && tactical.IsCurrentFriendlyMarkerBody(player);

    private void HideAll()
    {
        _seen.Clear();
        foreach (var view in _views)
            if (view?.Rect != null && view.Rect.gameObject.activeSelf) view.Rect.gameObject.SetActive(false);
    }
}
