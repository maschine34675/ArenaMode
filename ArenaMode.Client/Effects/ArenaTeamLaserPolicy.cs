using System;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>Visible-light policy; tracer HDR values contribute hue, not extra brightness.</summary>
internal static class ArenaTeamLaserPolicy
{
    internal static bool IsVisibleMaterial(string shaderName, bool point, bool hasColor,
        bool hasFactor, float factor)
        => hasColor && hasFactor && Finite(factor) && factor > 0f
           && string.Equals(shaderName, point ? "Custom/LaserPoint" : "Custom/LaserBeam",
               StringComparison.Ordinal);

    internal static Color Tint(Color original, Color palette)
    {
        float originalPeak = Math.Max(original.r, Math.Max(original.g, original.b));
        float palettePeak = Math.Max(palette.r, Math.Max(palette.g, palette.b));
        if (!Finite(original.r) || !Finite(original.g) || !Finite(original.b)
            || !Finite(original.a) || !Finite(palettePeak) || palettePeak <= 0f
            || originalPeak <= 0f) return original;
        float scale = originalPeak / palettePeak;
        return new Color(palette.r * scale, palette.g * scale, palette.b * scale, original.a);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Restore only our last write; later native/third-party light updates win.</summary>
internal sealed class ArenaTeamLaserLightLease
{
    private bool _applied;
    private Color _original;
    private Color _last;

    internal Color Apply(Color current, Color palette)
    {
        if (!_applied || !current.Equals(_last)) _original = current;
        _last = ArenaTeamLaserPolicy.Tint(_original, palette);
        _applied = true;
        return _last;
    }

    internal Color Restore(Color current)
    {
        Color result = _applied && current.Equals(_last) ? _original : current;
        _applied = false;
        return result;
    }
}
