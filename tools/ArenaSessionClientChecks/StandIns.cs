// Minimal stand-ins for the game, BepInEx and the rest of the client. The production sources under
// test (ArenaSessionMode, ArenaWhitelistAdmission, ArenaSessionSwitch and the shared session logic)
// are compiled unchanged; everything they reach outside themselves is replaced here by a double the
// checks can steer.

namespace ArenaMode
{
    public sealed class TestLogger
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Debug = new();
        public void LogInfo(object value) => Info.Add(value?.ToString());
        public void LogWarning(object value) => Warnings.Add(value?.ToString());
        public void LogDebug(object value) => Debug.Add(value?.ToString());
        public void Clear() { Info.Clear(); Warnings.Clear(); Debug.Clear(); }
    }

    public static class Plugin
    {
        public static TestLogger ModLogger = new();
    }
}

namespace BepInEx
{
    public static class Paths
    {
        public static string PluginPath;
        public static string ConfigPath;
        public static string GameRootPath;
        public static string ExecutablePath;
    }

    public sealed class BepInPlugin
    {
        public string GUID;
        public string Name;
        public Version Version;
    }

    public sealed class BepInDependency
    {
        [Flags]
        public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
        public string DependencyGUID;
        public DependencyFlags Flags;
    }

    public sealed class PluginInfo
    {
        public BepInPlugin Metadata;
        public IEnumerable<BepInDependency> Dependencies = Array.Empty<BepInDependency>();
        public string Location;
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, PluginInfo> PluginInfos { get; } = new(StringComparer.Ordinal);
    }
}

namespace UnityEngine
{
    public static class Application
    {
        public static int QuitCalls;
        public static void Quit() => QuitCalls++;
    }
}

namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static bool IsLocal = true;
    }
}

namespace ArenaMode.Session
{
    /// <summary>Same shape as the production type in ArenaServerClient.cs.</summary>
    internal sealed class ArenaServerModInfo
    {
        internal ArenaServerModInfo(string guid, string name, string version, string folder, IReadOnlyList<string> dependencies = null)
        {
            Guid = guid; Name = name; Version = version; Folder = folder;
            Dependencies = dependencies ?? Array.Empty<string>();
        }
        internal string Guid { get; }
        internal string Name { get; }
        internal string Version { get; }
        internal string Folder { get; }
        internal IReadOnlyList<string> Dependencies { get; }
    }

    /// <summary>
    /// The part of ArenaServerClient the session code reads. The real ping parser is covered by the
    /// native menu harness, which compiles the production ArenaServerClient.cs.
    /// </summary>
    internal static class ArenaServerClient
    {
        internal static int PingGeneration;
        internal static bool IsServerModKnownAvailable;
        internal static bool SessionFieldsPresent;
        internal static bool IsArenaInstance;
        internal static IReadOnlyList<ArenaServerModInfo> ServerMods;
        internal static int RefreshCalls;

        /// <summary>What the next ping "learns" from the server.</summary>
        internal static Action OnRefresh;

        /// <summary>The bounded ping at screen open (the real one is covered by NativeMenuChecks).</summary>
        internal static bool TryRefreshWithin(int milliseconds)
        {
            RefreshCalls++;
            PingGeneration++;
            OnRefresh?.Invoke();
            return IsServerModKnownAvailable;
        }

        internal static void Reset()
        {
            // Never back to zero: the production cache is keyed on the generation.
            PingGeneration++;
            IsServerModKnownAvailable = false;
            SessionFieldsPresent = false;
            IsArenaInstance = false;
            ServerMods = null;
            RefreshCalls = 0;
            OnRefresh = null;
        }
    }
}
