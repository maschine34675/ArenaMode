using EFT;
using Redeploy.Core.Compat;

namespace ArenaMode.Session;

/// <summary>
/// Whether a raid, as configured right now in the menu, is one ArenaMode will
/// take over.
///
/// Its own type because two very different menu patches need the same answer and
/// must not drift apart: the one that skips the pre-raid screens, and the one
/// that opens a keycard-locked map. The session is not armed yet at this point —
/// LocalRaidStarted, which arms it, is several screens downstream — so the
/// conditions have to be read off the raid settings instead of off ArenaSession.
/// </summary>
internal static class ArenaClaim
{
    public static bool WouldTakeOver(RaidSettings settings)
    {
        if (!ArenaMenuSelection.IsSelectedFor(settings))
            return false;

        if (!ArenaMenuSelection.CanStart)
            return false;

        if (!ArenaMenuSelection.IsMapAvailable(settings.SelectedLocation?.Id))
            return false;

        return !FikaCompatCheck.IsFikaLoaded && ArenaServerClient.IsServerModAvailable;
    }
}
