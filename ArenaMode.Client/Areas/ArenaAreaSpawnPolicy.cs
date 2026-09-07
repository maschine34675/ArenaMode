using EFT;
using EFT.Game.Spawning;

namespace ArenaMode.Areas;

/// <summary>
/// Player category alone also includes Customs' Savage-only bot markers.
/// Match the actual PMC's native side mask independently of bot eligibility.
/// Geometry, blocked markers and live navigation remain the caller's checks.
/// </summary>
internal static class ArenaAreaSpawnPolicy
{
    internal static bool IsPlayerPoint(ISpawnPoint point, EPlayerSide? playerSide)
    {
        if (point == null || (point.Categories & ESpawnCategoryMask.Player) == 0)
            return false;

        // The supported native masks are flags: Pmc=Usec|Bear, All also includes
        // Savage. Never substitute a default faction when the loading profile
        // is absent, a Scav profile, or an unsupported enum value.
        EPlayerSideMask required = playerSide == EPlayerSide.Usec ? EPlayerSideMask.Usec
            : playerSide == EPlayerSide.Bear ? EPlayerSideMask.Bear : EPlayerSideMask.None;
        EPlayerSideMask available = point.Sides;
        return required != EPlayerSideMask.None
            && (available & ~EPlayerSideMask.All) == 0
            && (available & required) == required;
    }
}
