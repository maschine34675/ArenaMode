namespace ArenaMode.Combat;

/// <summary>Presentation only. No attacker, score, assist or award fields.</summary>
internal sealed class ArenaUncreditedDeath
{
    internal string VictimProfileId;
    internal string VictimName;
    internal string VictimRoleLabel;
    internal bool LocalIsVictim;
    internal string DamageKind;
}
