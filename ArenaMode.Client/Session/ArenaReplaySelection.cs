using System;

namespace ArenaMode.Session;

/// <summary>Immutable for an armed raid, including replay duration queries during playback.</summary>
internal static class ArenaReplaySelection
{
    internal static float Seconds { get; private set; }

    internal static void Capture(float seconds, bool available)
        => Seconds = available && !float.IsNaN(seconds) && !float.IsInfinity(seconds)
            ? Math.Max(0f, Math.Min(10f, seconds)) : 0f;

    internal static void Clear() => Seconds = 0f;
}
