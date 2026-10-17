using ArenaMode.Bots;

namespace ArenaMode.Tests;

public sealed class ZombieBodyCullingPolicyTests
{
    // A symmetric frustum looking down +Z from the origin: near 0.3, far 100,
    // 90 degree horizontal and vertical opening. Normals point inward, Unity style.
    private static float[] Frustum()
    {
        float s = (float)Math.Sqrt(0.5);
        return new[]
        {
            s, 0f, s, 0f,      // left   (x >= -z)
            -s, 0f, s, 0f,     // right  (x <= z)
            0f, s, s, 0f,      // bottom (y >= -z)
            0f, -s, s, 0f,     // top    (y <= z)
            0f, 0f, 1f, -0.3f, // near   (z >= 0.3)
            0f, 0f, -1f, 100f, // far    (z <= 100)
        };
    }

    [Fact]
    public void NearRadiusIsTwiceTheMeleeDistanceButNeverBelowTheMinimum()
    {
        Assert.Equal(4f, ZombieBodyCullingPolicy.NearRadius(ZombieBodyCullingPolicy.DefaultMeleeHitDistance));
        Assert.Equal(4f, ZombieBodyCullingPolicy.NearRadius(1f));
        Assert.Equal(6f, ZombieBodyCullingPolicy.NearRadius(3f));
        Assert.Equal(4f, ZombieBodyCullingPolicy.NearRadius(0f));
        Assert.Equal(4f, ZombieBodyCullingPolicy.NearRadius(float.NaN));
    }

    [Fact]
    public void EligibilityRequiresALiveGenericInfectedBotBodyThatIsNobodysFollower()
    {
        Assert.True(ZombieBodyCullingPolicy.IsEligible(true, true, true, true, true, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(false, true, true, true, true, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, false, true, true, true, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, true, false, true, true, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, true, true, false, true, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, true, true, true, false, false, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, true, true, true, true, true, false));
        Assert.False(ZombieBodyCullingPolicy.IsEligible(true, true, true, true, true, false, true));
    }

    [Fact]
    public void AnimatorIsCulledOnlyInsideTheNativeRadiusAwayFromEveryTargetAndNotMidSwing()
    {
        Assert.True(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, 12f, 4f, false));
        Assert.True(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 10f, 10f, 4.01f, 4f, false));
        // Beyond the native radius the game already culls: leave it alone.
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 10.01f, 10f, 12f, 4f, false));
        // A possible target within reach keeps the exact pose.
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, 4f, 4f, false));
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, 0f, 4f, false));
        // Mid-swing: the knife box cast comes from the animated hand.
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, 12f, 4f, true));
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(false, true, 6f, 10f, 12f, 4f, false));
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, false, 6f, 10f, 12f, 4f, false));
    }

    [Fact]
    public void UnknownDistancesKeepTheNativeAnimatorSetting()
    {
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, float.NaN, 10f, 12f, 4f, false));
        Assert.False(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, float.NaN, 4f, false));
        // No target at all is "infinitely far", which is fine to cull.
        Assert.True(ZombieBodyCullingPolicy.ShouldCullAnimator(true, true, 6f, 10f, float.PositiveInfinity, 4f, false));
    }

    [Fact]
    public void ShadowsHideOnlyAfterTheDelayOutsideTheViewAndNeverUnderAnOptic()
    {
        float delay = ZombieBodyCullingPolicy.ShadowHideDelaySeconds;
        Assert.True(ZombieBodyCullingPolicy.ShouldHideShadows(true, true, false, false, delay));
        Assert.False(ZombieBodyCullingPolicy.ShouldHideShadows(true, true, false, false, delay - 0.01f));
        Assert.False(ZombieBodyCullingPolicy.ShouldHideShadows(true, true, true, false, 10f));
        Assert.False(ZombieBodyCullingPolicy.ShouldHideShadows(true, true, false, true, 10f));
        Assert.False(ZombieBodyCullingPolicy.ShouldHideShadows(false, true, false, false, 10f));
        Assert.False(ZombieBodyCullingPolicy.ShouldHideShadows(true, false, false, false, 10f));
    }

    [Fact]
    public void OpticCoversOnlyTheFrontHemisphereWhileItRenders()
    {
        Assert.True(ZombieBodyCullingPolicy.OpticCovers(true, 0.5f));
        Assert.False(ZombieBodyCullingPolicy.OpticCovers(true, 0f));
        Assert.False(ZombieBodyCullingPolicy.OpticCovers(true, -3f));
        Assert.False(ZombieBodyCullingPolicy.OpticCovers(false, 0.5f));
    }

    [Fact]
    public void FrustumTestKeepsBodiesInFrontAndDropsBodiesBehindOrBeside()
    {
        var planes = Frustum();
        float ex = ZombieBodyCullingPolicy.ViewBoxHalfX, ey = ZombieBodyCullingPolicy.ViewBoxHalfY, ez = ZombieBodyCullingPolicy.ViewBoxHalfZ;

        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(planes, 0f, 0f, 8f, ex, ey, ez));
        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(planes, 7f, 0f, 8f, ex, ey, ez));
        // Just outside the right plane, but the padding box still reaches in.
        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(planes, 9.5f, 0f, 8f, ex, ey, ez));
        // Well beside the view: the whole box is behind the right plane.
        Assert.False(ZombieBodyCullingPolicy.InsideFrustum(planes, 14f, 0f, 8f, ex, ey, ez));
        // Behind the camera.
        Assert.False(ZombieBodyCullingPolicy.InsideFrustum(planes, 0f, 0f, -6f, ex, ey, ez));
        // Past the far plane.
        Assert.False(ZombieBodyCullingPolicy.InsideFrustum(planes, 0f, 0f, 110f, ex, ey, ez));
    }

    [Fact]
    public void FrustumTestCountsUnusableDataAsInView()
    {
        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(null, 0f, 0f, -6f, 2f, 1.5f, 2f));
        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(new float[8], 0f, 0f, -6f, 2f, 1.5f, 2f));
        var planes = Frustum();
        planes[3] = float.NaN;
        Assert.True(ZombieBodyCullingPolicy.InsideFrustum(planes, 0f, 0f, -6f, 2f, 1.5f, 2f));
    }

    [Fact]
    public void PaddingIsSymmetricAcrossTheView()
    {
        var planes = Frustum();
        float ex = ZombieBodyCullingPolicy.ViewBoxHalfX, ey = ZombieBodyCullingPolicy.ViewBoxHalfY, ez = ZombieBodyCullingPolicy.ViewBoxHalfZ;
        Assert.Equal(
            ZombieBodyCullingPolicy.InsideFrustum(planes, 9.5f, 0f, 8f, ex, ey, ez),
            ZombieBodyCullingPolicy.InsideFrustum(planes, -9.5f, 0f, 8f, ex, ey, ez));
        Assert.Equal(
            ZombieBodyCullingPolicy.InsideFrustum(planes, 14f, 0f, 8f, ex, ey, ez),
            ZombieBodyCullingPolicy.InsideFrustum(planes, -14f, 0f, 8f, ex, ey, ez));
    }
}
