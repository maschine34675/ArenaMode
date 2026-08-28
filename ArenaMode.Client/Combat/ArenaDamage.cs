using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>One damage event dealt BY the local player (hitmarker/number feed).</summary>
public sealed class ArenaDamage
{
    /// <summary>Actual HP removed from the body part (ApplyDamage return value).</summary>
    public float BodyDamage;

    public float ArmorDamage;

    public bool Headshot;

    /// <summary>Victim died from this hit.</summary>
    public bool Kill;

    /// <summary>Armor stopped the round (blunt-only or deflected).</summary>
    public bool ArmorOnly;

    public Vector3 WorldPos;

    /// <summary>Time.unscaledTime at capture.</summary>
    public float Time;
}
