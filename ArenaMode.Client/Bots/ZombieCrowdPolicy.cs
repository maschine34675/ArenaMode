using System;

namespace ArenaMode.Bots;

/// <summary>
/// Pure decisions for the optional horde: pull an infected who has
/// walked out of the player's neighbourhood back onto a ring around them,
/// and let melee infected treat a nearby player as seen without a HighPoly
/// linecast. No Unity or EFT types, so the test project can link this file.
/// Shooters stay on the native cast. A follower is never eligible.
/// </summary>
internal static class ZombieCrowdPolicy
{
    /// <summary>Beyond this, the body is recalled. Inside it, native movement stands.</summary>
    internal const float RecallDistance = 26f;

    /// <summary>Nominal placement distance; each body's yaw spans the full ring.</summary>
    internal const float RingDistance = 16f;

    /// <summary>Try inner, middle and outer rings; leave the body alone if all are occupied.</summary>
    internal static readonly float[] RingRadii = { 12f, 16f, 22f };

    /// <summary>Distinct approach angles. Finer than the eight legacy yaw presets.</summary>
    internal const int RingSlots = 24;

    /// <summary>Same body is not recalled again until this many seconds have passed.</summary>
    internal const float RecallCooldown = 2f;

    /// <summary>Melee vision is granted inside this range; beyond it the native cast runs.</summary>
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
