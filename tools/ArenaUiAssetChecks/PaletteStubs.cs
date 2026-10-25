namespace ArenaMode.Session
{
    internal static class ArenaTeamSideSelection { internal static bool? SelectedUsec { get; set; } }
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static string ActiveMode;
        internal static object ExactGame, ExactWorld;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
}
namespace ArenaMode.Modes { internal static class ArenaModeCatalog { internal const string TeamDeathmatchId = "teamdeathmatch"; } }
namespace EFT { public sealed class AbstractGame { } public sealed class GameWorld { } }
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
