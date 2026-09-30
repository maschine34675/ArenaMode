using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>One damage event dealt BY the local player (hitmarker/number feed).</summary>
public sealed class ArenaDamage
{
    /// <summary>Physical Unity victim identity for presentation grouping only;
    /// zero means unknown and must never combine with another hit.</summary>
    public int VictimBodyId;

    /// <summary>Actual common HP removed, including overflow (ApplyDamage return value).</summary>
    public float BodyDamage;

    public float ArmorDamage;

    public bool Headshot;

    /// <summary>Victim died from this hit.</summary>
    public bool Kill;

    /// <summary>Audio-only: direct local hostile contact, target alive and round
    /// eligible before damage; positive damage confirmed afterwards.</summary>
    public bool AudioEligible;

    /// <summary>Armor stopped the round (blunt-only or deflected).</summary>
    public bool ArmorOnly;

    public Vector3 WorldPos;

    /// <summary>Time.unscaledTime at capture.</summary>
    public float Time;
}
