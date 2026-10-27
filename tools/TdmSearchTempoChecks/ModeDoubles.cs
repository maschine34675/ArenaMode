using EFT;
namespace EFT { public class LocalGame : AbstractGame { } }
namespace ArenaMode.Session
{
    internal static partial class ArenaSession
    {
        // Contract-only local side facade; production's independent-team policy
        // and relationship ownership are linked, not recreated here.
        internal static EPlayerSide? TeamDeathmatchBodySide(Player player, LocalGame game, GameWorld world)
            => IsExactActiveRaid(game, world) && ReferenceEquals(world?.MainPlayer, player)
                ? player?.Profile.Info.Side : null;
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaMatchService { internal static Match Current; }
    internal class Match { internal object Mode; }
    internal class TeamDeathmatchMode
    {
        internal bool IsSearchActive = true;
        internal readonly HashSet<Player> Adopted = new();
        internal bool TryClassifyCurrentBody(Player player, out bool friendly)
        { friendly = player?.Profile.Info.Side == EPlayerSide.Usec; return player != null && Adopted.Contains(player); }
    }
}
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { internal static bool StandDown; } }
namespace ArenaMode.Compat { internal static class PitFireTeamCompat { internal static bool HasFrozenTeamRoster; } }
