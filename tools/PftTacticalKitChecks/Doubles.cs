namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Diz.Jobs { public enum JobYieldPriority { General } }
namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public readonly record struct MongoID(string Value) { public override string ToString() => Value; }
    public sealed class Profile
    {
        public string Id = "pmc", AccountId = "account";
        public Info Info = new();
        public InventoryLogic.Inventory Inventory = new();
    }
    public sealed class Info { public EPlayerSide Side; }
    public sealed class GameWorld { public string LocationId; public object MainPlayer; }
    public sealed class ItemFactory { public Dictionary<MongoID, ItemTemplate> ItemTemplates = new() { [new("equipment")] = new(), [new("rifle")] = new() }; }
    public sealed class ItemTemplate
    {
        public bool Malformed;
        public ResourceKey[] Resources = new[] { new ResourceKey("prefab") };
        public IEnumerable<ResourceKey> AllResources => Malformed ? throw new InvalidOperationException("malformed prefab") : Resources;
    }
    public sealed class ObjectsFactory
    {
        public enum PoolsCategory { Raid }
        public enum AssemblyType { Local }
        public static CancellationToken DefaultCancellationToken;
        public bool Ready = true;
        public int Loads;
        public TaskCompletionSource<bool> Pending;
        public Exception Failure;
        public bool IsPoolReady(PoolsCategory category) => Ready;
        public Task LoadBundlesAndCreatePools(PoolsCategory a, AssemblyType b, ResourceKey[] keys, Diz.Jobs.JobYieldPriority c, object d, CancellationToken e)
        { Loads++; return Failure != null ? Task.FromException(Failure) : Pending?.Task ?? Task.CompletedTask; }
    }
}
namespace EFT.InventoryLogic
{
    public sealed class Item { public string TemplateId; }
    public sealed class InventoryEquipment
    {
        public List<Item> Items = new() { new() { TemplateId = "equipment" }, new() { TemplateId = "rifle" } };
        public IEnumerable<Item> GetAllItems() => Items;
    }
    public sealed class Inventory { public InventoryEquipment Equipment = new(); }
}
namespace ArenaMode.Shared { public static class ArenaPopulationPolicy { public const string Tactical = "tactical"; } }
namespace ArenaMode.Compat { public static class PitFireTeamCompat { public static bool TeamArenaActive = true; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool ArmedForNextRaid = true, IsActive, TacticalTeamOwnGearSelection;
        public static int ArmEpoch = 1;
        public static string ArmedMode = "tactical", ActiveMode, ArmedMap = "factory4_day";
    }
    public static class ArenaServerClient { public static bool HasPendingArm = true; public static string PendingArmToken = "token"; }
}
namespace ArenaMode
{
    public static class Plugin { public static Log ModLogger = new(); }
    public sealed class Log { public void LogInfo(string value) { } public void LogDebug(string value) { } }
}
namespace ArenaMode.Kits
{
    public sealed class KitDefinition { public string Id = "selected", Name = "Issued"; }
    public static class KitCatalog
    {
        public static int Draws;
        public static KitDefinition Kit = new();
        public static KitDefinition GetBaseKit() { Draws++; return Kit; }
    }
    public static class TeamArenaEntryKit
    {
        public static int Prepares, Commits;
        public static bool FailPrepare, FailCommit;
        public static EFT.InventoryLogic.InventoryEquipment Prepare(EFT.Profile p, KitDefinition k)
        { Prepares++; if (FailPrepare) throw new InvalidOperationException("prepare fault"); return new(); }
        public static void Commit(EFT.Profile p, EFT.InventoryLogic.InventoryEquipment old, EFT.InventoryLogic.InventoryEquipment next)
        { Commits++; if (FailCommit) throw new InvalidOperationException("commit fault"); if (!ReferenceEquals(p.Inventory.Equipment, old)) throw new InvalidOperationException("source drift"); p.Inventory.Equipment = next; }
    }
    public sealed class KitOpticPreloadGuard : IDisposable
    {
        public static int Active;
        public static KitOpticPreloadGuard Begin(EFT.ObjectsFactory factory, ResourceKey[] keys, bool loadingScreen = false)
        { if (!loadingScreen) throw new InvalidOperationException("required entry optics need loading ownership"); Active++; return new(); }
        public void Dispose() => Active--;
    }
}
public readonly record struct ResourceKey(string Path)
{
    public static ResourceKey EmptyResourceKey => new("");
}
