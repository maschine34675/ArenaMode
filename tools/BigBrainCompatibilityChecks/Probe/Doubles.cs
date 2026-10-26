namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    { public string GUID = guid; public string Name = name; public Version Version = new(version); }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class BepInDependency : Attribute
    {
        public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
        public string Guid; public DependencyFlags Flags;
        public BepInDependency(string guid, string version) { Guid = guid; Flags = DependencyFlags.HardDependency; }
        public BepInDependency(string guid, DependencyFlags flags) { Guid = guid; Flags = flags; }
    }
    public sealed class PluginInfo { public BepInPlugin Metadata; public object Instance; }
    public class BaseUnityPlugin
    { public bool enabled = true; public Logging.ManualLogSource Logger = new(); }
}
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Lines = new();
        public void LogDebug(string value) => Lines.Add("DEBUG " + value);
        public void LogInfo(string value) => Lines.Add("INFO " + value);
        public void LogWarning(string value) => Lines.Add("WARN " + value);
        public void LogError(string value) => Lines.Add("ERROR " + value);
    }
}
namespace BepInEx.Bootstrap
{ public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace UnityEngine { public static class Time { public static float time; } }
namespace ArenaMode
// Deliberately unlike the release version: proves the companion uses the
// owning client's compiled constant rather than a stale hard-coded version.
{ public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public const string PluginVersion = "9.8.7"; } }
namespace ArenaMode.Bots
{
    public static class DominationObjectiveBridge
    {
        public static bool Available;
        public static void Stop() { }
    }
    public static class TeamArenaPursuitLeaseBridge
    {
        public static string Consumer;
        public static bool RegisterConsumer(string id) { if (Consumer != null) return false; Consumer = id; return true; }
        public static void UnregisterConsumer(string id) { if (Consumer == id) Consumer = null; }
    }
}
namespace ArenaMode.Combat.Fire { public static class ArenaFireRuntime { public static bool BotEscapeAvailable; } }
namespace ArenaMode.Pickups { public static class ArenaPickupRuntime { public static bool BotObjectivesAvailable; } }
namespace ArenaMode.BigBrainCompat
{
    public sealed class ArenaPursuitLayer : DrakiaXYZ.BigBrain.Brains.CustomLayer { }
    public sealed class ArenaPickupLayer : DrakiaXYZ.BigBrain.Brains.CustomLayer { }
    public sealed class ArenaFireEscapeLayer : DrakiaXYZ.BigBrain.Brains.CustomLayer { }
    public sealed class ArenaDominationLayer : DrakiaXYZ.BigBrain.Brains.CustomLayer { }
    internal static class ArenaPickupObjective
    {
        internal static int Prunes, Resets;
        internal static void Prune() => Prunes++;
        internal static void ResetClaims() => Resets++;
    }
}
