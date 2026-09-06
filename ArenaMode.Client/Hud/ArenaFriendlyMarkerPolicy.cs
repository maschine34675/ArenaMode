using System;
using UnityEngine;

namespace ArenaMode.Hud;

internal static class ArenaFriendlyMarkerPolicy
{
    internal const int Capacity = 11; // Largest supported team: human plus eleven bots.
    internal const int MaximumScannedPlayers = 128;
    internal const float MaximumDistance = 100f;
    internal const float MinimumDistance = 0.65f;
    internal const float EdgePadding = 0.02f;

    internal static bool Projectable(Vector3 viewport, float distance)
        => Finite(viewport.x) && Finite(viewport.y) && Finite(viewport.z) && Finite(distance)
           && viewport.z > 0f && distance >= MinimumDistance && distance <= MaximumDistance
           && viewport.x > EdgePadding && viewport.x < 1f - EdgePadding
           && viewport.y > EdgePadding && viewport.y < 1f - EdgePadding;

    internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

    internal static Color Color(Color tracer)
    {
        float peak = Math.Max(tracer.r, Math.Max(tracer.g, tracer.b));
        if (!Finite(peak) || peak <= 0f) return new Color(0.125f, 0.625f, 1f, 0.9f);
        return new Color(tracer.r / peak, tracer.g / peak, tracer.b / peak, 0.9f);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
