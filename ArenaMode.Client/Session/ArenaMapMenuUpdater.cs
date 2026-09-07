using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>Screen-owned polling for F12/native mode changes; no global subscriptions.</summary>
internal sealed class ArenaMapMenuUpdater : MonoBehaviour
{
    internal Action Tick;
    private float _next;

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.2f;
        try { Tick?.Invoke(); }
        catch (Exception ex)
        {
            // Final map/start validation remains fail-closed if a foreign UI
            // change breaks presentation. Do not flood logs every fifth second.
            Tick = null;
            Plugin.ModLogger.LogWarning($"Arena conditional map refresh stopped: {ex.Message}");
        }
    }

    private void OnDestroy() => Tick = null;
}
