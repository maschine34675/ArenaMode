using System.Reflection;
using System.Runtime.CompilerServices;

// Stand-ins for the game and plugin types ArenaBackupProfileDuplicatePatch touches. Only the members the patch
// reads exist. The stand-in BotProfileClient.LoadedByBackup behaves like the game's callback: it appends what it
// is handed to the pool. It is patched with real Harmony, so the patch's prefix binding is exercised for real.

namespace Comfort.Common
{
    /// <summary>Lets a check prove that something never reads the game singletons.</summary>
    public static class StubProbe { public static bool ThrowOnRead; }

    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated
        {
            get
            {
                if (StubProbe.ThrowOnRead) throw new InvalidOperationException("the game singletons were read");
                return Instance != null;
            }
        }
    }
}

namespace EFT
{
    /// <summary>Equal by Id and hashed by Id: two distinct objects can compare equal, as the check needs.</summary>
    public class Profile
    {
        public string Id;
        public override bool Equals(object other) => other is Profile p && p.Id == Id;
        public override int GetHashCode() => Id?.GetHashCode() ?? 0;
    }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame { public BotsController BotsController { get; set; } = new(); }
    public class GameWorld { }
    public class BotsController { public BotSpawner BotSpawner = new(); }
    public class BotSpawner { public BotCreatorClient _botCreator; }
}

public interface IBotGame { EFT.BotsController BotsController { get; } }
public class BotCreatorClient { public IBotGame _game; public BotProfileClient _creatorProfile; }

public class BotProfileClient
{
    public List<EFT.Profile> Pool = new();
    /// <summary>Makes reading the pool throw, which is how a check makes the prefix fail.</summary>
    public bool ThrowOnPool;
    public List<EFT.Profile> _simpleProfiles
    {
        get { if (ThrowOnPool) throw new InvalidOperationException("pool unreadable"); return Pool; }
        set => Pool = value;
    }

    /// <summary>The list the original received, the pool as it was when the original started, and the call count.</summary>
    public List<EFT.Profile> Received;
    public List<EFT.Profile> PoolAtEntry;
    public int Calls;

    // No inlining: a patched method that the JIT inlined into its caller would run unpatched.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LoadedByBackup(List<EFT.Profile> profiles)
    {
        Calls++;
        Received = profiles;
        PoolAtEntry = new List<EFT.Profile>(Pool);
        if (profiles != null) Pool.AddRange(profiles);
    }
}

namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static object StartedGame, StartedWorld;
        // The shape of the real one: active, and the very game and world the raid started.
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && game != null && world != null && ReferenceEquals(StartedGame, game) && ReferenceEquals(StartedWorld, world);
    }
}

namespace ArenaMode
{
    public static class Plugin { public static Log ModLogger = new(); }
    public class Log
    {
        public static int Warnings;
        public static List<string> Warned = new();
        public static List<string> Debugs = new();
        public void LogWarning(object text) { Warnings++; Warned.Add(text.ToString()); }
        public void LogDebug(object text) => Debugs.Add(text.ToString());
    }
}

namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        public static bool FailEnable; public static int Enabled;
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { if (FailEnable) throw new InvalidOperationException("target"); GetTargetMethod(); Enabled++; }
    }
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefixAttribute : Attribute { }
}
