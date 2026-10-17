using System;

namespace ArenaMode.Modes;

/// <summary>One physical spawn, never refreshed by polling, settings changes or repeated activation.</summary>
internal sealed class TeamSpawnProtectionLease
{
    private readonly float _seconds;
    private float? _started;
    private bool _ended;

    internal TeamSpawnProtectionLease(float seconds)
    {
        _seconds = float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0f : Math.Max(0f, Math.Min(30f, seconds));
        _ended = _seconds <= 0f;
    }

    internal bool IsProtected(float now, bool combatReady)
    {
        if (_ended) return false;
        if (float.IsNaN(now) || float.IsInfinity(now)) { End(); return false; }
        // Preparation already has its own guard. A newly admitted PreActive body
        // remains safe until its native activation completes; its existing
        // activation watchdog still retires it if it never becomes usable.
        if (!_started.HasValue && combatReady) _started = now;
        if (_started.HasValue && (now < _started.Value || now - _started.Value >= _seconds)) End();
        return !_ended;
    }

    internal void End() => _ended = true;
}
