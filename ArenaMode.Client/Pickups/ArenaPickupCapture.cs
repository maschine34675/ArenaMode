using ArenaMode.Areas;
using ArenaMode.Config;
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

namespace ArenaMode.Pickups;

/// <summary>Explicit read-only point drafts. Never arms pickups or mutates an ordinary raid.</summary>
internal sealed class ArenaPickupCapture : MonoBehaviour
{
    private LocalGame _game;
    private GameWorld _world;
    private ArenaPickupLayout _draft;
    private string _status = "Capture a surface or your feet; F12 chooses the pickup type.";
    private GUIStyle _style;

    private bool Scope(out LocalGame game, out GameWorld world)
    {
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (ArenaPickupSettings.CaptureEnabled?.Value != true || game == null || world == null
            || game.Status != GameStatus.Started || !ReferenceEquals(game.GameWorld, world)
            || !ArenaMapCatalog.IsSupportedMap(world.LocationId)) return false;
        if (ArenaSession.IsActive) return ArenaSession.IsExactActiveRaid(game, world);
        if (Settings.AreaCaptureAllowInNormalPmc?.Value != true || ArenaSession.ArmedForNextRaid) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        var side = game.LocalPlayer?.Profile?.Info?.Side;
        return settings != null && settings.playerSide == ESideType.Pmc && (side == EPlayerSide.Usec || side == EPlayerSide.Bear)
            && string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.serverId)
            && (Protocol.IsNativeOrdinaryStartIdentity(settings.serverId) || !Protocol.HasNativeEndMarker(settings.serverId));
    }

    private bool CanCapture() => _world?.MainPlayer != null && _world.MainPlayer.IsYourPlayer
        && ReferenceEquals(_game.LocalPlayer, _world.MainPlayer) && _world.MainPlayer.HealthController?.IsAlive == true
        && !Respawn.ArenaRespawnDriver.IsRespawnInProgress && !Spectator.ArenaSpectatorService.IsActive
        && Cursor.lockState == CursorLockMode.Locked && EftScreenManager.Instance != null
        && EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI);

    private static bool Down(ConfigEntry<KeyboardShortcut> key) => key != null
        && Input.GetKeyDown(key.Value.MainKey) && key.Value.Modifiers.All(Input.GetKey);

    private void Update()
    {
        try
        {
            if (!Scope(out var game, out var world)) { _world = null; _game = null; _draft = null; return; }
            if (!ReferenceEquals(game, _game) || !ReferenceEquals(world, _world))
            {
                _game = game; _world = world;
                _draft = new ArenaPickupLayout { MapId = world.LocationId, AreaId = ArenaAreaRuntime.CurrentArea?.Id };
            }
            if (!CanCapture()) return;
            if (Down(ArenaPickupSettings.Undo))
            {
                if (_draft.Points.Count > 0) _draft.Points.RemoveAt(_draft.Points.Count - 1);
                _status = "Last point removed."; return;
            }
            if (Down(ArenaPickupSettings.Export))
            {
                if (_draft.Points.Count == 0) { _status = "Capture at least one point first."; return; }
                var file = new ArenaPickupLayoutFile(); file.Layouts.Add(_draft);
                string directory = Path.Combine(ArenaAssetPaths.FromPluginFile(Plugin.Instance.Info.Location), "PickupDrafts");
                string path = ArenaPickupLayouts.Export(directory, file);
                _status = "Saved " + Path.GetFileName(path) + ". Review/copy as Pickups.json; next match only.";
                Plugin.ModLogger.LogInfo("Arena pickup draft exported: " + path); return;
            }
            if (Down(ArenaPickupSettings.CaptureFeet)) { Add(_world.MainPlayer.Position); return; }
            if (!Down(ArenaPickupSettings.CaptureAim)) return;
            var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
            if (camera == null) return;
            var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            var hits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.GetComponentInParent<Player>() != null
                    || ArenaAreaRuntime.OwnsCollider(hit.collider) || ArenaPickupRuntime.OwnsCollider(hit.collider)) continue;
                if (hit.normal.y < .7f) { _status = "Aim at a floor surface, not a wall."; return; }
                Add(hit.point); return;
            }
            _status = "No floor surface in range.";
        }
        catch (Exception ex) { _status = "Capture failed: " + ex.GetBaseException().Message; }
    }

    private void Add(Vector3 position)
    {
        if (_draft.Points.Count >= 64) { _status = "Maximum 64 points per layout."; return; }
        var kind = ArenaPickupSettings.CaptureKind.Value;
        _draft.Points.Add(new ArenaPickupPoint
        {
            Id = "pickup-" + Guid.NewGuid().ToString("N"), Kind = kind,
            X = position.x, Y = position.y, Z = position.z,
            Yaw = _world.MainPlayer.Transform.rotation.eulerAngles.y,
            KitId = kind == ArenaPickupKind.Weapon ? ArenaPickupSettings.WeaponKit.Value : null,
            RespawnSeconds = ArenaPickupSettings.CaptureCooldown.Value
        });
        _status = $"{kind}: {position.x:F2}, {position.y:F2}, {position.z:F2}. Draft only; no live pickup spawned.";
    }

    private void OnGUI()
    {
        if (_draft == null || !Scope(out _, out _) || !CanCapture()) return;
        _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, wordWrap = true };
        GUI.Box(new Rect(18f, 310f, 580f, 125f), $"PICKUP DRAFT — {_draft.Points.Count}/64  |  {ArenaPickupSettings.CaptureKind.Value}\n"
            + $"{ArenaPickupSettings.CaptureAim.Value}: aim   {ArenaPickupSettings.CaptureFeet.Value}: feet\n"
            + $"{ArenaPickupSettings.Undo.Value}: undo   {ArenaPickupSettings.Export.Value}: export\n{_status}", _style);
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        if (camera == null) return;
        for (int i = 0; i < _draft.Points.Count; i++)
        {
            var point = _draft.Points[i];
            var screen = camera.WorldToScreenPoint(new Vector3(point.X, point.Y + .5f, point.Z));
            if (screen.z > 0) GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y, 180f, 24f), $"{i + 1}: {point.Kind}");
        }
    }
}
