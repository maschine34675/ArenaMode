using System;

namespace ArenaMode.Bots;

/// <summary>
/// Pure decisions for the two Zombie body-culling switches in
/// <c>ZombieBodyCullingRuntime</c>: letting Unity cull a near infected body's
/// animator while nothing renders it, and switching the shadows of an
/// infected body off while it is outside the camera's view. No Unity or EFT
/// types, so the test project links this file.
///
/// Native facts these rules answer to (../_decompiled, EFT 0.16.9.5):
/// <c>Player.VisualPass</c> sets <c>BodyAnimatorCommon.cullingMode =
/// AlwaysAnimate</c> for every alive body closer to the camera than
/// <c>EFTHardSettings.AnimatorCullDistance</c> (10 m) and
/// <c>CullUpdateTransforms</c> beyond it (EFT/Player.cs:25910-25919);
/// <c>CullUpdateTransforms</c> only takes effect while none of the body's
/// renderers is visible to any camera — shadow passes included
/// (<c>Renderer.isVisible</c>) — which is why the animator switch only pays
/// off together with the shadow switch. The offline bot culler keeps every
/// body within <c>CULLING_PLAYER_DISTANCE</c> (30 m) rendered regardless of
/// the view (ObservedCullingManager.cs:31-44), so the near horde is drawn
/// into every shadow cascade even behind the player. An infected's melee
/// hit depends on its BODY animator twice: for an AI owner
/// <c>KnifeCollider.ManualUpdate</c> takes <c>ManualUpdateServerAI</c>
/// (EFT/KnifeCollider.cs:76-88, 156-168), an overlap box from the skeleton
/// bone <c>Owner.WeaponRoot</c> along the look direction, and it runs only
/// while <c>OnHit</c> is set, a window the body animator's own animation
/// events open and close (OnZombieFireBullet/OnZombieFireEnd,
/// EFT/Player.cs:17776-17777, 18191-18218) during the Kick that
/// <c>MakeKnifeKick</c> triggers on the body animator for simplified
/// skeletons (EFT/Player.cs:18590-18609). A culled body animator would
/// deliver neither the bone pose nor, provably, those events. The hit
/// attempt itself requires <c>GoalEnemy.Distance &lt; DIST_TO_HIT_MELEE</c>
/// (BotMeleeWeaponData.cs:151-168, default 2 m, BotGlobalShootData.cs:157)
/// and the near radius is twice that, so a body is exempt before its first
/// swing frame can even be observed as a Kick. That CullUpdateTransforms
/// keeps root motion and events running is Unity's documented behavior and
/// vanilla's own precedent beyond 10 m, not something the decompile proves;
/// the GPU run checks that culled bodies still approach.
/// </summary>
internal static class ZombieBodyCullingPolicy
{
    /// <summary>Native <c>BotGlobalShootData.DIST_TO_HIT_MELEE</c> default.</summary>
    internal const float DefaultMeleeHitDistance = 2f;

    /// <summary>Never cull a body that is closer than this to a possible target.</summary>
    internal const float MinimumNearRadius = 4f;

    /// <summary>A body must be outside the view this long before its shadows go off; showing is immediate.</summary>
    internal const float ShadowHideDelaySeconds = 0.3f;

    /// <summary>Half extents of the box tested against the view, around a point 1 m above the feet.</summary>
    internal const float ViewBoxHalfX = 2f, ViewBoxHalfY = 1.5f, ViewBoxHalfZ = 2f;

    /// <summary>Height of the view-box centre above the body position.</summary>
    internal const float ViewBoxCentreHeight = 1f;

    /// <summary>While a body's shadows are off, its renderer list is re-read this often (late-loaded gear).</summary>
    internal const float RendererRefreshSeconds = 1f;

    /// <summary>Twice the configured melee hit distance, never below the minimum.</summary>
    internal static float NearRadius(float meleeHitDistance)
        => float.IsNaN(meleeHitDistance) || meleeHitDistance <= 0f
            ? MinimumNearRadius
            : Math.Max(MinimumNearRadius, 2f * meleeHitDistance);

    internal static bool IsEligible(bool zombieRaid, bool isAI, bool simplifiedSkeleton, bool genericInfected,
        bool alive, bool follower, bool yourPlayer)
        => zombieRaid && isAI && simplifiedSkeleton && genericInfected && alive && !follower && !yourPlayer;

    /// <summary>
    /// Inside the native AlwaysAnimate radius, hand the body to Unity's
    /// renderer-based culling unless it may need its exact pose: it is within
    /// <paramref name="nearRadius"/> of a possible target, or mid-swing. Beyond
    /// the native radius the game already culls, so there is nothing to do.
    /// NaN distances count as "unknown" and keep the native setting.
    /// </summary>
    internal static bool ShouldCullAnimator(bool enabled, bool eligible, float cameraDistance, float animatorCullDistance,
        float nearestTargetDistance, float nearRadius, bool attacking)
        => enabled && eligible && !attacking
           && cameraDistance <= animatorCullDistance
           && nearestTargetDistance > nearRadius;

    /// <summary>
    /// Shadows go off only for a body that has been outside the padded view
    /// for the hide delay and is not covered by a rendering optic camera.
    /// </summary>
    internal static bool ShouldHideShadows(bool enabled, bool eligible, bool inView, bool opticCovers, float outsideSeconds)
        => enabled && eligible && !inView && !opticCovers && outsideSeconds >= ShadowHideDelaySeconds;

    /// <summary>
    /// A magnified optic renders through its own camera, whose view this code
    /// does not track. While one renders, every body in front of the main
    /// camera counts as possibly seen through it.
    /// </summary>
    internal static bool OpticCovers(bool opticRendering, float forwardDot)
        => opticRendering && forwardDot > 0f;

    /// <summary>
    /// Box-versus-frustum test with the frustum as six planes of four floats
    /// (nx, ny, nz, d), normals pointing inward, as Unity's
    /// <c>GeometryUtility.CalculateFrustumPlanes</c> produces them. The box is
    /// outside when it lies wholly behind any one plane. An unusable plane
    /// array counts as "in view": nothing is culled on unknown data.
    /// </summary>
    /// <summary>Longest shadow this code accounts for, along the ground.</summary>
    internal const float MaxShadowReach = 15f;

    /// <summary>Caster height used for the shadow length.</summary>
    internal const float CasterHeight = 1.8f;

    /// <summary>
    /// Horizontal offset from a caster's feet to the far end of its shadow for a
    /// directional light shining along (dx, dy, dz): the light's forward,
    /// pointing down and away from the light. A light at or above the horizon
    /// gives the maximum reach along its horizontal part; a light straight
    /// down, or an unusable direction, gives no reach.
    /// </summary>
    internal static void ShadowReachOffset(float dx, float dy, float dz, out float ox, out float oz)
    {
        ox = 0f;
        oz = 0f;
        if (float.IsNaN(dx) || float.IsNaN(dy) || float.IsNaN(dz))
            return;
        float horizontal = (float)Math.Sqrt(dx * dx + dz * dz);
        if (horizontal < 1e-4f)
            return;
        float reach = dy < -1e-3f ? CasterHeight * horizontal / -dy : MaxShadowReach;
        reach = Math.Min(reach, MaxShadowReach);
        ox = dx / horizontal * reach;
        oz = dz / horizontal * reach;
    }

    /// <summary>
    /// In view when the body's box, the far end of its shadow or the point
    /// between them intersects the frustum, so a shadow that falls into the
    /// picture from an unseen caster is kept. (ox, oz) from
    /// <see cref="ShadowReachOffset"/>; zero means "no light known".
    /// </summary>
    internal static bool InsideFrustumWithShadow(float[] planes, float cx, float cy, float cz, float ex, float ey, float ez,
        float ox, float oz)
    {
        if (InsideFrustum(planes, cx, cy, cz, ex, ey, ez))
            return true;
        if (ox == 0f && oz == 0f)
            return false;
        return InsideFrustum(planes, cx + ox * 0.5f, cy, cz + oz * 0.5f, ex, ey, ez)
            || InsideFrustum(planes, cx + ox, cy, cz + oz, ex, ey, ez);
    }

    internal static bool InsideFrustum(float[] planes, float cx, float cy, float cz, float ex, float ey, float ez)
    {
        if (planes == null || planes.Length < 24)
            return true;
        for (int p = 0; p < 24; p += 4)
        {
            float nx = planes[p], ny = planes[p + 1], nz = planes[p + 2], d = planes[p + 3];
            float centre = nx * cx + ny * cy + nz * cz + d;
            float reach = Math.Abs(nx) * ex + Math.Abs(ny) * ey + Math.Abs(nz) * ez;
            if (float.IsNaN(centre) || float.IsNaN(reach))
                return true;
            if (centre + reach < 0f)
                return false;
        }
        return true;
    }
}
