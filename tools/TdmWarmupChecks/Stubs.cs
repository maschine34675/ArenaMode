using System.Collections;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public int Starts;
        public void StartCoroutine(IEnumerator routine) => Starts++;
    }
    public static class Time { public static float time; }
}
namespace ArenaMode.Config
{
    public class Setting { public bool Value; }
    public static class Settings { public static Setting RandomKit = new(); }
    public static class SettingsSync { public static void Tick() { } }
}
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool IsActive; public static string ActiveMode; }
    public static class LabyrinthSpawnTraps { public static void Tick() { } }
}
namespace ArenaMode.Compat
{
    public static class PitFireTeamCompat
    {
        public static bool TeamArenaActive, IsFinalizing;
        public static void Tick() { }
    }
}
namespace ArenaMode.Kits
{
    public class KitDefinition
    {
        public string Id;
        public List<KitNode> Items = new();
        public KitFill Fill;
    }
    public class KitNode { public string Tpl, ParentId; }
    public class KitFill { public List<KitStackedItem> RigContents = new(); }
    public class KitStackedItem { public string Tpl; }
    public static class KitCatalog { public static object KitProvider; public static List<KitDefinition> Kits; }
    public static class LadderCatalog { public static List<KitDefinition> Rungs; }
    public static class KitOpticPreloadGuard
    {
        public static bool ThrowOnBegin;
        public static void Reset() { ThrowOnBegin = false; }
        public static IDisposable Begin(EFT.ObjectsFactory factory, ResourceKey[] keys)
        { if (ThrowOnBegin) throw new InvalidOperationException("guard failure"); return new Scope(); }
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
namespace ArenaMode.Modes
{
    public class TeamDeathmatchMode { public bool InitialRosterReady; }
    public class GunGameMode { }
    public class Match { public object Mode; }
    public static class ArenaMatchService { public static Match Current; public static void Tick() { } }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static void Tick() { } } }
namespace ArenaMode.Bots
{
    public static class BurstSpawnService { public static void Tick() { } }
    public static class ArenaHostility { public static void Tick() { } }
    public static class CorpseLightService { public static void Tick() { } }
    public static class CorpseCleanupService { public static void Tick() { } }
}
namespace ArenaMode.Movement { public static class MovementRuntime { public static void Tick() { } } }
namespace ArenaMode.Combat
{
    public static class ArenaSkills { public static void Tick() { } }
    public static class ArenaGiveUp { public static void Tick() { } }
}
namespace ArenaMode.Effects
{
    public static class ArenaTeamLaserColors { public static void Tick() { } }
    public static class ArenaLightingDirector { public static void Tick() { } }
}
namespace ArenaMode.Audio
{
    public static class AnnouncerService { public static void Tick() { } }
    public static class LabyrinthAlarmPatches { public static void Tick() { } }
}
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static void Tick() { } } }
namespace ArenaMode.Hud { public static class ArenaHud { public static void Tick() { } } }

public record struct MongoID(string Value);
public record struct ResourceKey(string Value)
{
    public static ResourceKey EmptyResourceKey = new("");
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace Diz.Jobs { public enum JobYieldPriority { General } }
namespace EFT
{
    public class ItemTemplate { public List<ResourceKey> AllResources = new(); }
    public class ItemFactory { public Dictionary<MongoID, ItemTemplate> ItemTemplates = new(); }
    public class ObjectsFactory
    {
        public enum PoolsCategory { Raid }
        public enum AssemblyType { Local }
        public static System.Threading.CancellationToken DefaultCancellationToken;
        public readonly Queue<TaskCompletionSource> Loads = new();
        public Task LoadBundlesAndCreatePools(PoolsCategory category, AssemblyType assembly,
            ResourceKey[] keys, Diz.Jobs.JobYieldPriority priority, object progress,
            System.Threading.CancellationToken token)
        {
            var task = new TaskCompletionSource();
            Loads.Enqueue(task);
            return task.Task;
        }
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger
    {
        public void LogInfo(object value) { }
        public void LogWarning(object value) { }
    }
}
