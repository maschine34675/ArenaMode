using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels;

/// <summary>Migration safety for a test host which still has the retired DLL.
/// Only the known legacy controller/canvas are suspended during this Arena HUD.
/// Never destroy the shared BepInEx host or alter native/other-mod canvases.</summary>
internal sealed class LegacyPanelSuppression : IDisposable
{
    private readonly List<(Behaviour Component, bool Enabled)> _controllers = new();
    private readonly List<(GameObject Canvas, bool Active)> _canvases = new();

    internal LegacyPanelSuppression()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.maschine.RaidInfoPanels", out var info)
                || info.Instance == null) return;
            foreach (var component in info.Instance.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().FullName != "RaidInfoPanels.HudOverlayController") continue;
                _controllers.Add((component, component.enabled));
                component.enabled = false;
            }
            foreach (var canvas in info.Instance.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null || canvas.gameObject.name != "RaidInfoPanelsCanvas") continue;
                _canvases.Add((canvas.gameObject, canvas.gameObject.activeSelf));
                canvas.gameObject.SetActive(false);
            }
            Plugin.ModLogger.LogInfo("Arena absorbed info panels: retired RaidInfoPanels is still loaded; its known HUD is temporarily hidden to prevent duplicates.");
        }
        catch (Exception ex)
        {
            Dispose();
            Plugin.ModLogger.LogWarning($"Arena legacy panel suppression unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        foreach (var entry in _canvases)
        {
            try { if (entry.Canvas != null) entry.Canvas.SetActive(entry.Active); }
            catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena legacy canvas restore failed: {ex.Message}"); }
        }
        foreach (var entry in _controllers)
        {
            try { if (entry.Component != null) entry.Component.enabled = entry.Enabled; }
            catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena legacy panel restore failed: {ex.Message}"); }
        }
        _canvases.Clear();
        _controllers.Clear();
    }
}
