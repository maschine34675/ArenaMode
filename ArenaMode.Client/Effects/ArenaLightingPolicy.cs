using System;

namespace ArenaMode.Effects;

internal enum ArenaLightingEffect
{
    Off,
    Fight,
    Countdown,
    TimePressure,
    Victory,
    Defeat,
}

internal enum ArenaLightingOutcome
{
    None,
    Won,
    Lost,
}

/// <summary>
/// Engine-independent RGB value. Keeping the phase policy free of Unity types
/// lets its precedence, fades and safety bounds run in the offline harness.
/// </summary>
internal readonly struct ArenaLightColor
{
    internal ArenaLightColor(float red, float green, float blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    internal float Red { get; }
    internal float Green { get; }
    internal float Blue { get; }

    internal ArenaLightColor Normalized()
    {
        float red = SafeChannel(Red);
        float green = SafeChannel(Green);
        float blue = SafeChannel(Blue);
        float peak = Math.Max(1f, Math.Max(red, Math.Max(green, blue)));
        return new ArenaLightColor(red / peak, green / peak, blue / peak);
    }

    private static float SafeChannel(float value)
        => IsFinite(value) ? Math.Max(0f, value) : 0f;

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal struct ArenaLightingContext
{
    internal bool Enabled { get; set; }
    internal bool SupportedMap { get; set; }
    internal float Now { get; set; }
    internal float Strength { get; set; }
    internal float CountdownRemaining { get; set; }
    internal float LiveRemaining { get; set; }
    internal float PressureWindow { get; set; }
    internal float FightRemaining { get; set; }
    internal ArenaLightingOutcome Outcome { get; set; }
    internal float OutcomeRemaining { get; set; }
    internal float OutcomeDuration { get; set; }
    internal ArenaLightColor FriendlyColor { get; set; }
    internal ArenaLightColor OpponentColor { get; set; }
}

internal readonly struct ArenaLightingFrame
{
    internal ArenaLightingFrame(
        ArenaLightingEffect effect,
        ArenaLightColor color,
        float intensity)
    {
        Effect = effect;
        Color = color;
        Intensity = intensity;
    }

    internal ArenaLightingEffect Effect { get; }
    internal ArenaLightColor Color { get; }
    internal float Intensity { get; }
    internal bool Visible => Effect != ArenaLightingEffect.Off && Intensity > 0f;

    internal static ArenaLightingFrame Off
        => new ArenaLightingFrame(ArenaLightingEffect.Off, default, 0f);
}

/// <summary>
/// Resolves one restrained, additive lighting layer from the current round
/// state. Result feedback outranks the start flash, which outranks countdown
/// and time pressure. The runtime owns one light and applies only this frame.
/// </summary>
internal static class ArenaLightingPolicy
{
    internal const float FightFlashSeconds = 0.75f;
    internal const float CountdownWindowSeconds = 3.05f;
    internal const float DefaultOutcomeSeconds = 3f;

    private static readonly ArenaLightColor CountdownColor = new(1f, 0.28f, 0.04f);
    private static readonly ArenaLightColor PressureColor = new(1f, 0.035f, 0.02f);

    internal static bool SupportsMap(string locationId)
        => string.Equals(locationId?.Trim(), "laboratory", StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId?.Trim(), "factory4_day", StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId?.Trim(), "factory4_night", StringComparison.OrdinalIgnoreCase);

    internal static ArenaLightingFrame Evaluate(in ArenaLightingContext context)
    {
        float strength = BoundStrength(context.Strength);
        if (!context.Enabled || !context.SupportedMap || strength <= 0f)
            return ArenaLightingFrame.Off;

        if (context.Outcome != ArenaLightingOutcome.None
            && IsPositive(context.OutcomeRemaining)
            && IsPositive(context.OutcomeDuration))
        {
            float remaining = Clamp01(context.OutcomeRemaining / context.OutcomeDuration);
            // Ease out instead of snapping the whole map back to neutral.
            float fade = remaining * remaining * (3f - 2f * remaining);
            float pulse = 0.88f + 0.12f * UnitWave(context.Now * 1.5f);
            bool won = context.Outcome == ArenaLightingOutcome.Won;
            return new ArenaLightingFrame(
                won ? ArenaLightingEffect.Victory : ArenaLightingEffect.Defeat,
                (won ? context.FriendlyColor : context.OpponentColor).Normalized(),
                strength * 0.52f * fade * pulse);
        }

        if (IsPositive(context.FightRemaining))
        {
            float fade = Clamp01(context.FightRemaining / FightFlashSeconds);
            return new ArenaLightingFrame(
                ArenaLightingEffect.Fight,
                context.FriendlyColor.Normalized(),
                strength * 0.42f * fade);
        }

        if (IsPositive(context.CountdownRemaining)
            && context.CountdownRemaining <= CountdownWindowSeconds)
        {
            // Cosine peaks at the integer 3, 2 and 1 second boundaries.
            float pulse = 0.2f + 0.8f * UnitWave(context.CountdownRemaining);
            return new ArenaLightingFrame(
                ArenaLightingEffect.Countdown,
                CountdownColor,
                strength * 0.38f * pulse);
        }

        if (IsPositive(context.PressureWindow)
            && IsFinite(context.LiveRemaining)
            && context.LiveRemaining >= 0f
            && context.LiveRemaining <= context.PressureWindow)
        {
            float urgency = 1f - Clamp01(context.LiveRemaining / context.PressureWindow);
            float heartbeat = 0.72f + 0.28f * UnitWave(context.Now * 1.25f);
            return new ArenaLightingFrame(
                ArenaLightingEffect.TimePressure,
                PressureColor,
                strength * (0.08f + 0.18f * urgency) * heartbeat);
        }

        return ArenaLightingFrame.Off;
    }

    internal static float BoundStrength(float value)
    {
        if (!IsFinite(value))
            return 0f;
        return Math.Max(0f, Math.Min(2f, value));
    }

    internal static float BoundOutcomeDuration(float value)
    {
        if (!IsFinite(value) || value < 0.25f)
            return DefaultOutcomeSeconds;
        return Math.Min(8f, value);
    }

    private static float UnitWave(float cycles)
        => 0.5f + 0.5f * (float)Math.Cos(cycles * Math.PI * 2d);

    private static float Clamp01(float value)
        => Math.Max(0f, Math.Min(1f, value));

    private static bool IsPositive(float value)
        => IsFinite(value) && value > 0f;

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
