using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI.Screens;
using JsonType;
using Redeploy.Core.GameAccess;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Areas;

/// <summary>
/// Opt-in drafting aid. Reads camera/world positions and writes explicit exports;
/// never moves players, disables barriers, changes spawns or arms an edited area.
/// Existing Waypoints spawn visualization remains independent.
/// </summary>
internal sealed class ArenaAreaCapture : MonoBehaviour
{
    private GameWorld _world;
    private AbstractGame _game;
    private ArenaAreaDraft _draft;
    private string _status = "Capture points around the outline; the last edge closes automatically.";
    private bool _valid;
    private bool _faulted;
    private bool _arenaScope;
    private float _minY, _maxY, _spawnMinY, _spawnMaxY;
    private GUIStyle _text, _pointText;

    private bool TryScope(out AbstractGame game, out GameWorld world)
    {
        game = null; world = null;
        if (Settings.AreaCaptureEnabled?.Value != true) return false;
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (!(game is LocalGame local) || game.Status != GameStatus.Started || world == null
            || !ReferenceEquals(local.GameWorld, world) || !ArenaMapCatalog.IsSupportedMap(world.LocationId)) return false;
        // An active but mismatched Arena may never fall through to ordinary capture.
        if (ArenaSession.IsActive) return ArenaSession.IsExactActiveRaid(game, world);
        if (Settings.AreaCaptureAllowInNormalPmc?.Value != true || ArenaSession.ArmedForNextRaid) return false;

        var settings = LocalGameAccessor.GetLocalRaidSettings(local);
        var side = local.LocalPlayer?.Profile?.Info?.Side;
        // The native game retains the server response ID, including Arena's
        // end marker after IsActive clears. Never reclassify that old game as
        // ordinary. A valid ordinary-start tag is also accepted for integrations
        // retaining the request ID. This path only reads metadata; it arms nothing.
        return settings != null && settings.playerSide == ESideType.Pmc
            && (side == EPlayerSide.Usec || side == EPlayerSide.Bear)
            && string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.serverId)
            && (Protocol.IsNativeOrdinaryStartIdentity(settings.serverId) || !Protocol.HasNativeEndMarker(settings.serverId));
    }

    private bool CanInteract()
        => _world?.MainPlayer != null && _world.MainPlayer.IsYourPlayer
           && _game is LocalGame local && ReferenceEquals(local.LocalPlayer, _world.MainPlayer)
           && _world.MainPlayer.HealthController?.IsAlive == true
           && !Respawn.ArenaRespawnDriver.IsRespawnInProgress && !Spectator.ArenaSpectatorService.IsActive
           && Cursor.lockState == CursorLockMode.Locked && EftScreenManager.Instance != null
           && EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI);

    private static bool Down(ConfigEntry<KeyboardShortcut> entry)
        => entry != null && Input.GetKeyDown(entry.Value.MainKey) && entry.Value.Modifiers.All(Input.GetKey);

    private void Update()
    {
        try
        {
            if (!TryScope(out var game, out var world)) { Reset(); return; }
            if (!ReferenceEquals(_world, world) || !ReferenceEquals(_game, game)
                || _arenaScope != ArenaSession.IsActive
                || !string.Equals(_draft?.MapId, world.LocationId, StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                _world = world; _game = game;
                _arenaScope = ArenaSession.IsActive;
                _draft = new ArenaAreaDraft(world.LocationId, _arenaScope ? ArenaAreaRuntime.CurrentArea?.Id : null);
                Revalidate();
            }
            if (_faulted) return;
            if (!_minY.Equals(Settings.AreaCaptureMinY.Value) || !_maxY.Equals(Settings.AreaCaptureMaxY.Value)
                || !_spawnMinY.Equals(Settings.AreaCaptureSpawnMinY.Value) || !_spawnMaxY.Equals(Settings.AreaCaptureSpawnMaxY.Value))
                Revalidate();
            if (!CanInteract()) return;
            if (Down(Settings.AreaCaptureUndo))
            {
                if (_draft.Undo()) Revalidate();
                return;
            }
            if (Down(Settings.AreaCaptureExport)) { Export(); return; }
            if (Down(Settings.AreaCaptureFeet)) { Add(_world.MainPlayer.Position); return; }
            if (Down(Settings.AreaCaptureAim)) CaptureAim();
        }
        catch (Exception ex)
        {
            if (!_faulted)
                Plugin.ModLogger.LogWarning("Arena outline capture stopped; toggle it off/on to retry: " + ex.Message);
            _faulted = true;
        }
    }

    private void CaptureAim()
    {
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        if (camera == null || !camera.isActiveAndEnabled) { _status = "Game camera is unavailable."; return; }
        var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        // Only on an explicit key press. AllHits lets an author point beyond the
        // CURRENT Arena barrier without disabling its physical/nav protection.
        var hits = Physics.RaycastAll(ray, 250f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || ArenaAreaRuntime.OwnsCollider(hit.collider)
                || hit.collider.GetComponentInParent<Player>() != null) continue;
            Add(hit.point);
            return;
        }
        _status = "No map surface within 250m. Aim at ground/wall or capture your feet.";
    }

    private void Add(Vector3 point)
    {
        if (!_draft.Add(new ArenaAreaPoint(point.x, point.y, point.z), out var reason))
        { _status = reason; return; }
        Revalidate();
        ArenaToasts.Show($"Outline point {_draft.Points.Count}: {point.x:F2}, {point.y:F2}, {point.z:F2}");
    }

    private void Revalidate()
    {
        _minY = Settings.AreaCaptureMinY.Value; _maxY = Settings.AreaCaptureMaxY.Value;
        _spawnMinY = Settings.AreaCaptureSpawnMinY.Value; _spawnMaxY = Settings.AreaCaptureSpawnMaxY.Value;
        _valid = _draft.Validate(_minY, _maxY, _spawnMinY, _spawnMaxY, out _, out var reason);
        _status = _valid ? "Valid outline draft. Export for review; live area stays unchanged." : reason;
    }

    private void Export()
    {
        if (!_draft.TryExport(_minY, _maxY, _spawnMinY, _spawnMaxY, out var json, out var reason))
        { _status = "Export refused: " + reason; return; }
        var directory = Path.Combine(ArenaAssetPaths.FromPluginFile(Plugin.Instance.Info.Location), "AreaDrafts");
        string path = ArenaAreaDraftFile.Write(directory, json);
        _status = "Saved " + Path.GetFileName(path);
        Plugin.ModLogger.LogInfo("Arena outline draft exported (not activated): " + path);
        ArenaToasts.Show("Outline saved to BepInEx/plugins/maschine-ArenaMode/AreaDrafts. Live area unchanged.");
    }

    private void OnGUI()
    {
        if (_draft == null || _faulted || Event.current.type != EventType.Repaint) return;
        var oldColor = GUI.color;
        var oldMatrix = GUI.matrix;
        try
        {
            if (!TryScope(out var game, out var world) || !ReferenceEquals(_game, game)
                || !ReferenceEquals(_world, world) || _arenaScope != ArenaSession.IsActive
                || !string.Equals(_draft.MapId, world.LocationId, StringComparison.OrdinalIgnoreCase) || !CanInteract()) return;
            var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
            if (camera == null || !camera.isActiveAndEnabled) return;
            _text ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _pointText ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            GUI.color = _valid ? new Color(.3f, 1f, .55f) : new Color(1f, .7f, .2f);
            var points = _draft.Points;
            bool drawHeight = ArenaAreaBounds.Finite(_minY) && ArenaAreaBounds.Finite(_maxY) && _minY < _maxY;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var a = new Vector3(p.X, _minY, p.Z);
                var top = new Vector3(p.X, _maxY, p.Z);
                if (drawHeight) DrawWorldLine(camera, a, top);
                if (drawHeight && points.Count > 1 && (i + 1 < points.Count || points.Count > 2))
                {
                    var next = points[(i + 1) % points.Count];
                    DrawWorldLine(camera, a, new Vector3(next.X, _minY, next.Z));
                    DrawWorldLine(camera, top, new Vector3(next.X, _maxY, next.Z));
                }
                Vector3 projected = camera.WorldToScreenPoint(new Vector3(p.X, p.Y, p.Z));
                if (projected.z > 0)
                    GUI.Label(new Rect(projected.x + 4, Screen.height - projected.y - 24, 130, 45),
                        $"{i + 1} ({p.Y:F1}m)", _pointText);
            }
            GUI.color = Color.white;
            float height = _arenaScope ? 175f : 205f;
            var box = new Rect(20f, Mathf.Max(20, Screen.height - height - 25), 465f, height);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 12, box.y + 8, box.width - 24, box.height - 16),
                $"AREA OUTLINE DRAFT — {_draft.MapId} — {points.Count}/64 points\n"
                + $"{Settings.AreaCaptureAim.Value}: aim point  |  {Settings.AreaCaptureFeet.Value}: feet\n"
                + $"{Settings.AreaCaptureUndo.Value}: undo  |  {Settings.AreaCaptureExport.Value}: export\n"
                + $"Floor/ceiling: {_minY:F1} .. {_maxY:F1}m; spawn feet: {_spawnMinY:F1} .. {_spawnMaxY:F1}m\n"
                + _status + "\nPreview only; visible through walls. Spawn display: Waypoints."
                + (_arenaScope ? "" : "\nNormal PMC raid: ordinary gear/profile consequences still apply."), _text);
        }
        catch (Exception ex)
        {
            _faulted = true;
            Plugin.ModLogger.LogWarning("Arena outline preview stopped: " + ex.Message);
        }
        finally { GUI.color = oldColor; GUI.matrix = oldMatrix; }
    }

    private static void DrawWorldLine(Camera camera, Vector3 a, Vector3 b)
    {
        var start = camera.WorldToScreenPoint(a); var end = camera.WorldToScreenPoint(b);
        if (start.z <= 0 || end.z <= 0) return;
        var from = new Vector2(start.x, Screen.height - start.y);
        var to = new Vector2(end.x, Screen.height - end.y);
        var delta = to - from;
        float length = delta.magnitude;
        if (length < .1f || float.IsInfinity(length) || float.IsNaN(length)) return;
        var matrix = GUI.matrix;
        try
        {
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - 1, length, 2), Texture2D.whiteTexture);
        }
        finally { GUI.matrix = matrix; }
    }

    private void Reset()
    {
        _draft = null; _world = null; _game = null; _faulted = false; _valid = false; _arenaScope = false;
        _text = null; _pointText = null;
    }
    private void OnDisable() => Reset();
    private void OnDestroy() => Reset();
}
