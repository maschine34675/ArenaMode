using EFT;
namespace UnityEngine
{
    public struct Vector3 { public float sqrMagnitude; public static Vector3 operator -(Vector3 a, Vector3 b) => a; }
    public static class Time { public static float time; }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum WildSpawnType { infectedAssault, infectedPmc, infectedCivil, infectedLaborant, pmcUSEC }
    public enum BotDifficulty { normal, hard }
    public enum EDamageType { Undefined }
    public static class Roles { public static bool IsInfected(this WildSpawnType v) => v != WildSpawnType.pmcUSEC; }
    public class Profile { public string Id = "bot"; public Info Info = new(); }
    public class Info { public Settings Settings = new(); }
    public class Settings { public WildSpawnType Role; }
    public class Health { public bool IsAlive = true; public void Kill(EDamageType type) => IsAlive = false; }
    public class Player { public Health HealthController = new(); public Health ActiveHealthController => HealthController; public UnityEngine.Vector3 Position; }
    public class Memory { public bool HaveEnemy; }
    public class Group { public int Searches; public void AddPointToSearch(UnityEngine.Vector3 p, float n, BotOwner bot) => Searches++; }
    public class BotOwner
    {
        public Player GetPlayer = new(); public Profile Profile = new();
        public string ProfileId => Profile.Id; public Health HealthController => GetPlayer?.HealthController;
        public UnityEngine.Vector3 Position; public Memory Memory = new(); public Group BotsGroup = new();
    }
    public interface IBotGame { BotsController BotsController { get; } }
    public class BotGame : IBotGame { public BotsController BotsController { get; } = new(); }
    public class BotsController { public Bots Bots = new(); public Spawner BotSpawner = new(); }
    public class Bots { public List<BotOwner> BotOwners = new(); }
    public class Spawner { public void AddToTargetBackup(BotDifficulty d, WildSpawnType r, int n) { } }
    public class GameWorld { public Player MainPlayer = new(); }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger { public void LogInfo(object s) { } public void LogWarning(object s) { } public void LogError(object s) { } }
}
namespace ArenaMode.Combat
{
    public sealed class ArenaAdministrativeDeathGate : IDisposable
    { public static ArenaAdministrativeDeathGate Enter(string s) => new(); public void Dispose() { } }
}
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    public class ZombieMode { public bool IsTeamArena; public IReadOnlyList<Player> LiveTeamMembers = Array.Empty<Player>(); }
}
namespace ArenaMode.Bots
{
    public static class ZombieRoleMix
    {
        public const string InfectedAssault = "infectedAssault", InfectedPmc = "infectedPmc", InfectedCivil = "infectedCivil", InfectedLaborant = "infectedLaborant", Normal = "normal", Hard = "hard";
    }
    public static class ZombieProfilePlan { public class Entry { public string Role, Difficulty; public int Count; } }
    public static class ArenaHostility { public static void ReportPlayerTo(Group g, Player p) { } }
    public static class ZombieMeleeDiagnostics { public static int Arrivals; public static void OnZombieArrived(BotOwner b) => Arrivals++; }
    public static class BurstSpawnService
    {
        public sealed class Request
        {
            public Action<BotOwner> Arrival; public Func<bool> Current, Begin; public Action<bool> Finish; public Action Rejected;
        }
        public static readonly List<Request> Requests = new();
        public static Action<Request> Inline;
        public static bool Preflight = true;
        public static bool SpawnInfected(WildSpawnType r, BotDifficulty d, Action<BotOwner> arrival,
            Func<bool> requestCurrent = null, Func<bool> beginNativeDispatch = null,
            Action<bool> dispatchFinished = null, Action arrivalRejected = null)
        {
            if (!Preflight) return false;
            var request = new Request { Arrival = arrival, Current = requestCurrent, Begin = beginNativeDispatch, Finish = dispatchFinished, Rejected = arrivalRejected };
            Requests.Add(request); Inline?.Invoke(request); return true;
        }
    }
}
