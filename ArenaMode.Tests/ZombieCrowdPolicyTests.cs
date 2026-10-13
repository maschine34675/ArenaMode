using ArenaMode.Bots;

namespace ArenaMode.Tests;

public sealed class ZombieCrowdPolicyTests
{
    [Fact]
    public void RecallsOnlyADistantEligibleBodyAfterTheCooldown()
    {
        float edge = ZombieCrowdPolicy.RecallDistance * ZombieCrowdPolicy.RecallDistance;
        Assert.False(ZombieCrowdPolicy.ShouldRecall(true, edge, 10f));
        Assert.True(ZombieCrowdPolicy.ShouldRecall(true, edge + 0.01f, ZombieCrowdPolicy.RecallCooldown));
        Assert.False(ZombieCrowdPolicy.ShouldRecall(true, edge + 1f, ZombieCrowdPolicy.RecallCooldown - 0.01f));
        Assert.False(ZombieCrowdPolicy.ShouldRecall(false, edge + 1f, 10f));
    }

    [Fact]
    public void EligibilityRequiresTheSwitchTheRaidAndALiveGenericNonFollower()
    {
        Assert.True(ZombieCrowdPolicy.IsEligible(true, true, true, true, true, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(false, true, true, true, true, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(true, false, true, true, true, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(true, true, false, true, true, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(true, true, true, false, true, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(true, true, true, true, false, false));
        Assert.False(ZombieCrowdPolicy.IsEligible(true, true, true, true, true, true));
    }

    [Fact]
    public void SightSkipIsMeleeOnlyAndInsideTheSenseRange()
    {
        Assert.True(ZombieCrowdPolicy.ShouldSkipSight(true, true, 0f));
        Assert.True(ZombieCrowdPolicy.ShouldSkipSight(true, true, ZombieCrowdPolicy.SenseDistance));
        Assert.False(ZombieCrowdPolicy.ShouldSkipSight(true, true, ZombieCrowdPolicy.SenseDistance + 0.01f));
        Assert.False(ZombieCrowdPolicy.ShouldSkipSight(true, false, 10f));
        Assert.False(ZombieCrowdPolicy.ShouldSkipSight(false, true, 10f));
        Assert.False(ZombieCrowdPolicy.ShouldSkipSight(true, true, float.NaN));
    }

    [Fact]
    public void RingSitsBehindTheLookDirectionAtTheConfiguredDistance()
    {
        ZombieCrowdPolicy.RingOffset(0f, 1f, 0f, out float x, out float z);
        Assert.Equal(0f, x, 3);
        Assert.Equal(-ZombieCrowdPolicy.RingDistance, z, 3);
        float radius = MathF.Sqrt(x * x + z * z);
        Assert.Equal(ZombieCrowdPolicy.RingDistance, radius, 3);

        ZombieCrowdPolicy.RingOffset(0f, 0f, 0f, out float fallbackX, out float fallbackZ);
        Assert.Equal(0f, fallbackX, 3);
        Assert.Equal(-ZombieCrowdPolicy.RingDistance, fallbackZ, 3);
        Assert.NotEmpty(ZombieCrowdPolicy.RingYawDegrees);
    }

    [Fact]
    public void NeighbouringBodiesStartOnDifferentRingSlots()
    {
        int first = ZombieCrowdPolicy.SlotIndex(0);
        int next = ZombieCrowdPolicy.SlotIndex(1);
        int wrapped = ZombieCrowdPolicy.SlotIndex(ZombieCrowdPolicy.RingYawDegrees.Length);
        int negative = ZombieCrowdPolicy.SlotIndex(-1);
        Assert.NotEqual(first, next);
        Assert.Equal(first, wrapped);
        Assert.Equal(ZombieCrowdPolicy.RingYawDegrees.Length - 1, negative);
        Assert.InRange(first, 0, ZombieCrowdPolicy.RingYawDegrees.Length - 1);
        Assert.NotEqual(ZombieCrowdPolicy.SlotYaw(0), ZombieCrowdPolicy.SlotYaw(1));
        ZombieCrowdPolicy.RingOffset(0f, 1f, 0f, 12f, out float innerX, out float innerZ);
        Assert.Equal(12f, MathF.Sqrt(innerX * innerX + innerZ * innerZ), 3);
    }

    [Fact]
    public void PresentationHidesWhatIsBehindAndSimplifiesWhatIsFar()
    {
        Assert.Equal(ZombieCrowdPolicy.BodyPresentation.Detail, ZombieCrowdPolicy.Presentation(2f, 1f));
        Assert.Equal(ZombieCrowdPolicy.BodyPresentation.Detail, ZombieCrowdPolicy.Presentation(2f, -1f));
        Assert.Equal(ZombieCrowdPolicy.BodyPresentation.Simple, ZombieCrowdPolicy.Presentation(12f, 1f));
        Assert.Equal(ZombieCrowdPolicy.BodyPresentation.Hidden,
            ZombieCrowdPolicy.Presentation(12f, ZombieCrowdPolicy.HideLookDot - 0.01f));
        Assert.Equal(ZombieCrowdPolicy.BodyPresentation.Detail, ZombieCrowdPolicy.Presentation(float.NaN, 1f));
        Assert.True(ZombieCrowdPolicy.ShouldSkipAvoidance(true));
        Assert.False(ZombieCrowdPolicy.ShouldSkipAvoidance(false));
    }
}
