#nullable disable
using System;
using System.Collections.Generic;

namespace ArenaMode.Combat;

/// <summary>
/// One captured kill, fully resolved to plain data at capture time — consumers
/// (match, killfeed, announcer) must never touch Player instances, which die
/// with every respawn.
/// </summary>
public sealed class ArenaKill
{
    public string KillerProfileId;
    public string KillerName;
    public string KillerRoleLabel;

    public string VictimProfileId;
    public string VictimName;

    public ArenaRole VictimRole;

    /// <summary>Human-readable role label for the killfeed ("Scav", "Raider", "PMC"…).</summary>
    public string VictimRoleLabel;

    /// <summary>True when the local player scored the kill.</summary>
    public bool KilledByLocal;

    /// <summary>True when the local player IS the victim.</summary>
    public bool LocalIsVictim;

    /// <summary>Localized short weapon name; null when unknown.</summary>
    public string WeaponName;

    /// <summary>Native class and final death cause, for icons only; never inferred from localized names.</summary>
    public string WeaponClass;
    public string DamageKind;

    public float Distance;

    /// <summary>True only when both positions yielded a finite distance at kill capture.</summary>
    public bool DistanceKnown;

    public bool Headshot;

    // Presentation metadata captured while the physical bodies still exist.
    // Zero/false means unknown and never earns a special award.
    internal long KillerBodyId;
    internal long VictimBodyId;
    internal bool AwardEligible;
    // Optional faction classification captured by Team Deathmatch. Other modes
    // retain their existing scoreboard rules and opponent fallback.
    internal bool ScoreEligible = true;
    internal bool? KillerIsFriendly;
    internal bool? VictimIsFriendly;
    internal bool SingleHitHeadshot;
    // Verified direct lethal bullet facts captured at firing, never sampled
    // from the player's current posture after impact or a weapon swap.
    internal bool AirborneHipFire;
    internal bool BoltActionHipFire;

    /// <summary>Time.time at capture.</summary>
    public float Time;

    /// <summary>Settled match-local contributions; never additional kill events.</summary>
    public IReadOnlyList<ArenaAssist> Assists { get; internal set; } = Array.Empty<ArenaAssist>();
}

/// <summary>Actual native HP loss, already clamped and armor-adjusted. No Unity references.</summary>
internal sealed class ArenaAssistDamage
{
    internal long VictimBodyId, AttackerBodyId;
    internal string AttackerProfileId;
    internal bool AttackerIsLocal;
    internal bool? AttackerIsFriendly;
    internal double Damage;
}

/// <summary>An immutable proportional reward for one logical participant.</summary>
public readonly struct ArenaAssist
{
    internal ArenaAssist(string contributorKey, string displayName, bool isLocal,
        long victimBodyId, int points, double damageShare)
    {
        ContributorKey = contributorKey; DisplayName = displayName; IsLocal = isLocal;
        VictimBodyId = victimBodyId; Points = points; DamageShare = damageShare;
    }
    public string ContributorKey { get; }
    public string DisplayName { get; }
    public bool IsLocal { get; }
    public long VictimBodyId { get; }
    public int Points { get; }
    public double DamageShare { get; }
}
