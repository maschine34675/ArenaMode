using ArenaMode.Modes;
using ArenaMode.Hud.InfoPanels.UI;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels;

/// <summary>Match-owned status/weapon modules on Arena's existing canvas.
/// No polling in menus, independent plugin, coroutine or gameplay patch.</summary>
internal sealed class ArenaInfoPanels : IDisposable
{
    private GameObject _root;
    private readonly ArenaMatch _match;
    private WeaponInfoPanel _weapon;
    private PlayerStatusPanel _status;
    private Player _player;
    private LegacyPanelSuppression _legacy;
    private float _nextRefresh;
    private bool _weaponError, _statusError, _contextError;
    private bool _subscribed;
    private bool _disposed;
    private bool _weaponVisible, _statusVisible;
    private bool _weaponValid, _statusValid;

    internal ArenaInfoPanels(RectTransform parent, ArenaMatch match)
    {
        _match = match;
        try
        {
            _root = new GameObject("ArenaInfoPanels", typeof(RectTransform));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // TMP's Awake must run while these children are constructed:
            // its outline setters need the CanvasRenderer initialized there.
            // Build synchronously on the active Arena canvas, then hide before
            // returning (no render frame occurs between these operations).
            _weapon = new WeaponInfoPanel(rect);
            _status = new PlayerStatusPanel(rect);
            _root.SetActive(false);
            InfoPanelSettings.PanelOpacity.SettingChanged += OnThemeChanged;
            _subscribed = true;
            _legacy = new LegacyPanelSuppression();
            Plugin.ModLogger.LogInfo("Arena info panels: status and weapon UI constructed; awaiting live local body.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena info panels unavailable this match: {ex}");
            Dispose(); // A cosmetic failure must not tear down the other Arena HUD.
        }
    }

    internal void Tick()
    {
        if (_root == null) return;
        try
        {
            if (!ReferenceEquals(_match, ArenaMatchService.Current)
                || !Config.Settings.HudEnabled.Value || !InfoPanelSettings.Enabled.Value
                || !RaidPlayerContext.TryGetLocalPlayer(out var player))
            { Hide(); return; }

            bool changedBody = !ReferenceEquals(player, _player);
            if (changedBody) _weaponValid = _statusValid = false;
            _player = player;
            bool weaponVisible = InfoPanelSettings.ShowWeaponPanel.Value;
            bool statusVisible = InfoPanelSettings.ShowStatusPanel.Value;
            float now = Time.unscaledTime;
            bool refresh = changedBody || now >= _nextRefresh;
            if (refresh) _nextRefresh = now + RefreshInterval(InfoPanelSettings.UpdateIntervalSeconds.Value);
            _root.SetActive(true);
            RefreshWeapon(player, refresh || weaponVisible != _weaponVisible, weaponVisible);
            RefreshStatus(player, refresh || statusVisible != _statusVisible, statusVisible);
            _weaponVisible = weaponVisible;
            _statusVisible = statusVisible;
        }
        catch (Exception ex)
        {
            Hide();
            LogOnce("context", ex, ref _contextError);
        }
    }

    internal static float RefreshInterval(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0.1f : Math.Max(0.05f, Math.Min(2f, value));

    private void RefreshWeapon(Player player, bool refresh, bool visible)
    {
        try
        {
            if (!visible) { _weapon.SetVisible(false); return; }
            _weapon.ApplyLayout();
            if (refresh) { _weapon.UpdateFromPlayer(player); _weaponValid = true; }
            _weapon.SetVisible(_weaponValid);
        }
        catch (Exception ex)
        {
            _weaponValid = false;
            try { _weapon.SetVisible(false); } catch { /* destroyed UI is already invisible */ }
            LogOnce("weapon", ex, ref _weaponError);
        }
    }

    private void RefreshStatus(Player player, bool refresh, bool visible)
    {
        try
        {
            if (!visible) { _status.SetVisible(false); return; }
            _status.ApplyLayout();
            if (refresh) { _status.UpdateFromPlayer(player); _statusValid = true; }
            _status.SetVisible(_statusValid);
        }
        catch (Exception ex)
        {
            _statusValid = false;
            try { _status.SetVisible(false); } catch { /* destroyed UI is already invisible */ }
            LogOnce("status", ex, ref _statusError);
        }
    }

    private static void LogOnce(string panel, Exception ex, ref bool logged)
    {
        if (logged) return;
        logged = true;
        Plugin.ModLogger.LogWarning($"Arena {panel} panel update failed; hidden until a valid update: {ex}");
    }

    private void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _player = null;
        _nextRefresh = 0f;
        _weaponVisible = _statusVisible = false;
        _weaponValid = _statusValid = false;
    }

    private void OnThemeChanged(object sender, EventArgs args) => PanelStyle.ApplyTheme();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clean(Hide, "hide");
        if (_subscribed) InfoPanelSettings.PanelOpacity.SettingChanged -= OnThemeChanged;
        _subscribed = false;
        if (_root != null) Clean(() => UnityEngine.Object.Destroy(_root), "canvas");
        _root = null;
        _weapon = null;
        _status = null;
        Clean(() => _legacy?.Dispose(), "legacy HUD");
        _legacy = null;
        PanelStyle.Reset();
        EmbeddedIcons.Reset();
        GameIconLoader.Reset();
        MagazineInventoryCounter.Reset();
        LooseAmmoCounter.Reset();
        GrenadeInventoryCounter.Reset();
        ArmorDurabilityCounter.Reset();
    }

    private static void Clean(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena info panel {label} cleanup failed: {ex.Message}"); }
    }
}
