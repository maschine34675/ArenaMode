using ArenaMode.Shared;
using EFT.Game.Spawning;

namespace ArenaMode.Areas;

// Scene/nav readiness is an explicit external verdict. The actual linked bot
// filter and dispatch prefix must honor it; no NavMesh behavior is simulated.
internal static class ArenaAreaRuntime
{
    internal static ArenaAreaDefinition CurrentArea;
    internal static bool LoadingBlocked;
    internal static Func<ISpawnPoint, bool> SpawnAccepted = _ => true;
    internal static bool ShouldBlockLoadingBotDispatch() => LoadingBlocked;
    internal static bool AllowsSpawn(ISpawnPoint point) => CurrentArea == null || SpawnAccepted(point);
    internal static void Reset()
    { CurrentArea = null; LoadingBlocked = false; SpawnAccepted = _ => true; }
}
