using System;

namespace ArenaMode.Combat.Fire;

// Engine-independent limits. This prototype never mutates an item template.
internal static class ArenaFirePolicy
{
    internal const string DonorTemplate = "5448be9a4bdc2dfd2f8b456a"; // RGD-5
    internal const float Lifetime = 6f, Radius = 3f, CellRadius = .72f;
    internal const float TickSeconds = .25f, DamagePerSecond = 100f;
    internal const int MaxFires = 4, MaxThrows = 8;
    internal static bool Convert(bool enabled, bool live, bool exactLocal, string template)
        => enabled && live && exactLocal && template == DonorTemplate;
    internal static bool Contact(float dx, float dy, float dz, float padding = 0f)
        => Finite(dx) && Finite(dy) && Finite(dz) && Finite(padding) && padding >= 0
        && Math.Abs(dy) <= .6f && dx * dx + dz * dz <= (CellRadius + padding) * (CellRadius + padding);
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    // No catch-up burst after a pause/hitch and no extra damage for overlapping cells.
    internal static float TickDamage(float elapsed) => Math.Min(Math.Max(elapsed, 0), TickSeconds) * DamagePerSecond;
    internal static float PartDamage(float amount, float maximum, float total, float environmentMultiplier)
        => Finite(amount) && Finite(maximum) && Finite(total) && Finite(environmentMultiplier)
        && amount > 0 && maximum > 0 && total > 0 && environmentMultiplier > 0
        ? amount * maximum / total / environmentMultiplier : 0;
}
