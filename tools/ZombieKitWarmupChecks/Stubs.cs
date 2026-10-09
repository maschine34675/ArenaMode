using System.Collections;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public int Starts;
        public void StartCoroutine(IEnumerator routine) => Starts++;
    }
    public static class Time { public static float time; public static float realtimeSinceStartup => time; }
}
namespace ArenaMode.Config
{
    public class Setting { public bool Value; }
    public class ScopeSetting { public ArenaMode.Kits.KitPreloadScope Value; }
    public static class Settings { public static Setting RandomKit = new(); public static ScopeSetting KitPreloadInLoadingScreen = new(); }
    public static class SettingsSync { public static void Tick() { } }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid, ZombieOwnGearArmed;
        public static string ActiveMode, ArmedMode;
        public static int ArmEpoch;
    }
    public static class ArenaMenuSelection { public static bool CompatibilityTeamArenaActive; }
    public static class ArenaRaidExit { public static void Tick() { } }
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
        public List<KitItemNode> Items = new();
        public KitFill Fill;
    }
    public class KitItemNode { public string Tpl, ParentId; }
    public class KitFill { public List<KitStackedItem> RigContents = new(); }
    public class KitStackedItem { public string Tpl; }
    public static class KitCatalog
    {
        public static object KitProvider;
        public static List<KitDefinition> Kits;
        public static KitDefinition Opening, Reserved;
        public static int Draws;
        public static KitDefinition ReserveBaseKit() { Draws++; return Reserved = Opening; }
        public static void ClearReservedBaseKit() => Reserved = null;
    }
    public static class KitMerge { public static KitDefinition WithPrimaryWeapon(KitDefinition kit, KitDefinition weapon) => kit; }
    public static class LadderCatalog { public static List<KitDefinition> Rungs; }
    public static class AutoQuickSlots { public static void Tick() { } }
    public static class ArenaSecureContainer { public static void Tick() { } }

    public static class KitOpticPreloadGuard
    {
        public static bool ThrowOnBegin;
        public static void Reset() { ThrowOnBegin = false; }
        public static IDisposable Begin(EFT.ObjectsFactory factory, ResourceKey[] keys, bool loadingScreen = false)
        { if (ThrowOnBegin) throw new InvalidOperationException("guard failure"); return new Scope(); }
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
namespace ArenaMode.Modes
{
    public class TeamDeathmatchMode { public bool InitialRosterReady; }
    public class GunGameMode
    {
        public static List<ArenaMode.Kits.KitDefinition> BuildLadder(out bool weaponsOnly, bool quiet)
        { weaponsOnly = false; return ArenaMode.Kits.LadderCatalog.Rungs; }
    }
    public static class TacticalMode { public static ArenaMode.Kits.KitDefinition SelectOpeningKit() => ArenaMode.Kits.KitCatalog.Opening; }
    public class Match { public object Mode; }
    public static class ArenaMatchService { public static Match Current; public static void Tick() { } }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static void Tick() { } } }
namespace ArenaMode.Bots
{
    public static class BurstSpawnService { public static void Tick() { } }
    public static class BotLoadBoost { public static void Tick() { } }
    public static class ArenaHostility { public static void Tick() { } }
    public static class ArenaBotPatches { public static void TickIndividualGroups() { } }
    public static class CorpseLightService { public static void Tick() { } }
    public static class CorpseCleanupService { public static void Tick() { } }
}
namespace ArenaMode.Movement { public static class MovementRuntime { public static void Tick() { } } }
namespace ArenaMode.Performance { public static class GcThresholdMonitor { public static void Tick() { } } }
namespace ArenaMode.Pickups { public static class ArenaPickupRuntime { public static void Tick() { } } }
namespace ArenaMode.Combat.Fire { public static class ArenaFireRuntime { public static void Tick() { } } }
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
    public static class HitmarkerAudioService { public static void Tick() { } }
    public static class LabyrinthAlarmPatches { public static void Tick() { } }
}
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static void Tick() { } } }
namespace ArenaMode.Hud
{
    public static class ArenaHud { public static void Tick() { } }
    public static class ArenaExtractionList { public static void Tick() { } }
}

public record struct MongoID(string Value);
public record struct ResourceKey(string Value)
{
    public static ResourceKey EmptyResourceKey = new("");
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Diz.Jobs { public enum JobYieldPriority { General } }
namespace EFT
{
    public class GameWorld { }
    public class ItemTemplate { public List<ResourceKey> AllResources = new(); }
    public class ItemFactory { public Dictionary<MongoID, ItemTemplate> ItemTemplates = new(); }
    public class ObjectsFactory
    {
        public enum PoolsCategory { Raid }
        public enum AssemblyType { Local }
        public static System.Threading.CancellationToken DefaultCancellationToken;
        public readonly Queue<TaskCompletionSource> Loads = new();
        public readonly List<ResourceKey[]> Requested = new();
        public bool PoolReady = true;
        public bool IsPoolReady(PoolsCategory category) => PoolReady;
        public Task LoadBundlesAndCreatePools(PoolsCategory category, AssemblyType assembly,
            ResourceKey[] keys, Diz.Jobs.JobYieldPriority priority, object progress,
            System.Threading.CancellationToken token)
        {
            Requested.Add(keys);
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
