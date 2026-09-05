#nullable disable

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

    public float Distance;

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

    /// <summary>Time.time at capture.</summary>
    public float Time;
}
