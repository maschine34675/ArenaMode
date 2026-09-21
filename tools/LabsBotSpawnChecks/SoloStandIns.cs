// This older harness tests native placement and TDM. Solo ownership is inactive
// here; its real implementation is covered by SoloPopulationChecks separately.
public sealed class PositionNote { }
public sealed class BotsGroup { }
namespace EFT
{
    public sealed class Profile { }
    public sealed class BotCreatorClient
    {
        public System.Threading.Tasks.Task ActivateBot(BotCreationData data, BotZone zone, bool isAssault,
            System.Func<BotOwner, BotZone, BotsGroup> group, System.Action<BotOwner> callback, System.Threading.CancellationToken token)
            => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ActivateBot(Profile profile, PositionNote position, BotZone zone, bool isAssault,
            System.Func<BotOwner, BotZone, BotsGroup> group, System.Action<BotOwner> callback, System.Threading.CancellationToken token)
            => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task CreateBot(Profile profile, PositionNote position,
            System.Action<BotOwner> callback, bool group, System.Threading.CancellationToken token)
            => System.Threading.Tasks.Task.CompletedTask;
    }
}
namespace ArenaMode.Bots
{
    internal static class ArenaSoloSpawnDispatch
    {
        internal static bool IsActive => false;
        internal static bool CanDispatch(EFT.BotSpawner spawner, EFT.BotCreationData data) => true;
        internal static void MarkRejected(EFT.BotSpawner spawner, EFT.BotCreationData data) { }
        internal static bool CanActivate(EFT.BotCreatorClient creator, EFT.BotCreationData data) => true;
        internal static bool CanCreate(EFT.BotCreatorClient creator, EFT.Profile profile) => true;
    }
}
