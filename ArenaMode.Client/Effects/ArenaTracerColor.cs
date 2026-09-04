using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>High-dynamic-range colors suitable for EFT's native tracer mesh.</summary>
public enum ArenaTracerColor
{
    Cyan,
    Blue,
    Green,
    Yellow,
    Orange,
    Red,
    Magenta,
    White,
}

internal static class ArenaTracerPalette
{
    internal static Color Resolve(ArenaTracerColor color)
        => color switch
        {
            ArenaTracerColor.Cyan => new Color(0.25f, 1.25f, 2f, 1f),
            ArenaTracerColor.Blue => new Color(0.3f, 0.55f, 2f, 1f),
            ArenaTracerColor.Green => new Color(0.457f, 1.5f, 0.507f, 1f),
            ArenaTracerColor.Yellow => new Color(2f, 1.311f, 0.573f, 1f),
            ArenaTracerColor.Orange => new Color(2f, 0.65f, 0.2f, 1f),
            ArenaTracerColor.Red => new Color(2f, 0.235f, 0.235f, 1f),
            ArenaTracerColor.Magenta => new Color(2f, 0.25f, 1.5f, 1f),
            ArenaTracerColor.White => new Color(1.5f, 1.5f, 1.5f, 1f),
            _ => new Color(0.25f, 1.25f, 2f, 1f),
        };
}
