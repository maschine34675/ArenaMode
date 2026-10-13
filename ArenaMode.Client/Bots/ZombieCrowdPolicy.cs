using System;

namespace ArenaMode.Bots;

/// <summary>
/// Pure decisions for the Factory horde crowd: pull an infected who has
/// walked out of the player's neighbourhood back onto a ring behind them,
/// and let melee infected treat a nearby player as seen without a HighPoly
/// linecast. No Unity or EFT types, so the test project can link this file.
/// Shooters stay on the native cast. A follower is never eligible.
/// </summary>
internal static class ZombieCrowdPolicy
{
    /// <summary>Beyond this, the body is recalled. Inside it, native movement stands.</summary>
    internal const float RecallDistance = 26f;

    /// <summary>Placement distance from the player, on the side opposite the look direction.</summary>
    internal const float RingDistance = 16f;

    /// <summary>Inner, middle and outer rings. A full inner ring must not stop the rest of the horde.</summary>
    internal static readonly float[] RingRadii = { 12f, 16f, 22f };

    /// <summary>Distinct approach angles. Finer than the eight legacy yaw presets.</summary>
    internal const int RingSlots = 24;

    /// <summary>Same body is not recalled again until this many seconds have passed.</summary>
    internal const float RecallCooldown = 2f;

    /// <summary>Melee vision is granted inside this range and refused beyond it, with no cast either way.</summary>
    internal const float SenseDistance = 80f;

    /// <summary>Yaw offsets applied to the behind-vector. Each body starts at its own slot.</summary>
    internal static readonly float[] RingYawDegrees = { 25f, -25f, 60f, -60f, 100f, -100f, 150f, 180f };

    /// <summary>Two recalls closer than this are treated as the same spot.</summary>
    internal const float SlotSeparation = 3f;

    internal static int SlotIndex(int bodyId)
    {
        int count = RingYawDegrees.Length;
        int slot = bodyId % count;
        return slot < 0 ? slot + count : slot;
    }

    internal static float SlotYaw(int bodyId)
    {
        int slot = bodyId % RingSlots;
        if (slot < 0) slot += RingSlots;
        return slot * (360f / RingSlots);
    }

    /// <summary>Bodies farther than this, or behind the player, use one bone.</summary>
    internal const float DetailDistance = 6f;

    /// <summary>Behind the look direction and farther than this, the mesh is not drawn.</summary>
    internal const float HideDistance = 3f;

    /// <summary>Look-direction dot below this is behind the player.</summary>
    internal const float HideLookDot = -0.2f;

    internal enum BodyPresentation { Detail, Simple, Hidden }

    /// <summary>
    /// Detail keeps two bones in front and close. Simple is one bone.
    /// Hidden is not drawn. A bad distance stays detailed so a glitch does not blank the horde.
    /// </summary>
    internal static BodyPresentation Presentation(float distance, float lookDot)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f
            || float.IsNaN(lookDot))
            return BodyPresentation.Detail;
        if (distance > HideDistance && lookDot < HideLookDot)
            return BodyPresentation.Hidden;
        if (distance > DetailDistance)
            return BodyPresentation.Simple;
        return BodyPresentation.Detail;
    }

    internal static bool ShouldSkipAvoidance(bool eligible) => eligible;

    internal static bool IsEligible(bool enabled, bool zombieRaid, bool genericInfected,
        bool alive, bool active, bool follower)
        => enabled && zombieRaid && genericInfected && alive && active && !follower;

    internal static bool ShouldRecall(bool eligible, float distanceSquared, float secondsSinceRecall)
        => eligible
           && secondsSinceRecall >= RecallCooldown
           && distanceSquared > RecallDistance * RecallDistance;

    /// <summary>Easy and normal are the native melee difficulties. Hard shoots and keeps the cast.</summary>
    internal static bool ShouldSkipSight(bool eligible, bool meleeDifficulty, float distance)
        => eligible && meleeDifficulty && distance >= 0f && distance <= SenseDistance;

    /// <summary>
    /// Offset from the player, in the horizontal plane, facing away from
    /// <paramref name="forwardX"/>/<paramref name="forwardZ"/> and then yawed.
    /// A degenerate look direction falls back to world +Z so the ring still has a length.
    /// </summary>
    internal static void RingOffset(float forwardX, float forwardZ, float yawDegrees, out float x, out float z)
        => RingOffset(forwardX, forwardZ, yawDegrees, RingDistance, out x, out z);

    internal static void RingOffset(float forwardX, float forwardZ, float yawDegrees, float radius, out float x, out float z)
    {
        float length = MathF.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
        if (length < 0.0001f)
        {
            forwardX = 0f;
            forwardZ = 1f;
            length = 1f;
        }
        forwardX /= length;
        forwardZ /= length;
        float behindX = -forwardX;
        float behindZ = -forwardZ;
        float radians = yawDegrees * (MathF.PI / 180f);
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        x = (behindX * cosine - behindZ * sine) * radius;
        z = (behindX * sine + behindZ * cosine) * radius;
    }
}
