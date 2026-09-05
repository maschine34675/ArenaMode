// Only the dispatch boundary needs game-shaped objects. Population, score and
// award policies link unchanged production sources and use no Unity stand-ins.
namespace EFT
{
    internal sealed class AbstractGame { }
    internal sealed class GameWorld { }
    internal sealed class BotSpawner { }
}

internal sealed class BotCreationData { }

namespace Comfort.Common
{
    internal static class Singleton<T> where T : class
    {
        internal static T Instance;
        internal static bool Instantiated => Instance != null;
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static string ActiveMode;
        internal static EFT.AbstractGame OwnedGame;
        internal static EFT.GameWorld OwnedWorld;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => ReferenceEquals(game, OwnedGame) && ReferenceEquals(world, OwnedWorld);
    }
}
