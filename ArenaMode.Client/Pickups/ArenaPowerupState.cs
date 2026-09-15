using System;

namespace ArenaMode.Pickups;

internal readonly struct ArenaPowerupSnapshot
{
    internal readonly float DamageSeconds, ShieldSeconds, ShieldPoints;

    internal ArenaPowerupSnapshot(float damageSeconds, float shieldSeconds, float shieldPoints)
    {
        DamageSeconds = damageSeconds;
        ShieldSeconds = shieldSeconds;
        ShieldPoints = shieldPoints;
    }
}

/// <summary>One physical life. Collection refreshes a bounded timer/pool; it never adds to it.</summary>
internal sealed class ArenaPowerupState
{
    internal const float MaximumDuration = 20f;
    internal const float MaximumShield = 100f;
    private float _damageUntil, _shieldUntil, _shieldPoints;

    internal bool GrantDamage(float now, float duration)
    {
        if (!TryDeadline(now, duration, out var deadline)) return false;
        _damageUntil = deadline;
        return true;
    }

    internal bool GrantShield(float now, float points, float duration)
    {
        if (!PositiveFinite(points) || !TryDeadline(now, duration, out var deadline)) return false;
        _shieldUntil = deadline;
        _shieldPoints = Math.Min(points, MaximumShield);
        return true;
    }

    internal ArenaPowerupSnapshot Read(float now)
    {
        if (!Finite(now) || now < 0f) return default;
        if (now >= _damageUntil) _damageUntil = 0f;
        if (now >= _shieldUntil || !PositiveFinite(_shieldPoints))
        {
            _shieldUntil = 0f;
            _shieldPoints = 0f;
        }
        return new ArenaPowerupSnapshot(Math.Max(0f, _damageUntil - now),
            Math.Max(0f, _shieldUntil - now), _shieldPoints);
    }

    /// <summary>
    /// Shield points absorb post-armor impact damage after EFT's incoming damage
    /// multiplier, BEFORE per-part DownMult, health clamping and blacked-limb spread.
    /// Thus a point is not a promise of one total HP across every body-part rule.
    /// Only the argument of the one native health call changes. Its own multiplier
    /// still runs exactly once; no shared item/template or health coefficient changes.
    /// </summary>
    internal static float Adjust(float damage, float incomingMultiplier, bool doubled,
        ArenaPowerupState shield, float now)
    {
        if (!PositiveFinite(damage) || !PositiveFinite(incomingMultiplier)
            || !Finite(now) || now < 0f) return damage;
        double scaled = (double)damage * (doubled ? 2d : 1d);
        double impact = scaled * incomingMultiplier;
        // Fail open on unrepresentable inputs without spending a shield charge.
        if (scaled > float.MaxValue || impact > float.MaxValue) return damage;
        var available = shield?.Read(now).ShieldPoints ?? 0f;
        if (!(available > 0f)) return (float)scaled;
        float absorbed = (float)Math.Min(impact, available);
        var remaining = (float)((impact - absorbed) / incomingMultiplier);
        shield._shieldPoints = Math.Max(0f, available - absorbed);
        if (shield._shieldPoints <= 0f) shield._shieldUntil = 0f;
        return Math.Max(0f, remaining);
    }

    internal static bool PositiveFinite(float value) => value > 0f && Finite(value);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool TryDeadline(float now, float duration, out float deadline)
    {
        deadline = 0f;
        if (!Finite(now) || now < 0f || !PositiveFinite(duration)) return false;
        deadline = now + Math.Min(duration, MaximumDuration);
        return Finite(deadline) && deadline > now;
    }
}
