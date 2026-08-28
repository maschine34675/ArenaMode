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

    /// <summary>Time.time at capture.</summary>
    public float Time;
}
