using System.Reflection;
using System.Runtime.CompilerServices;

// Stand-ins for the game and plugin types ArenaAudioContainerReturnGuardPatch touches. Only the members the patch reads
// exist, with the game's shapes (decompile, ../_decompiled): PoolWithoutAlloc<T> hands out the lowest occupied slot and
// fills the lowest empty one without looking for the item; AudioSourceContainerData.ReturnContainer clears the container
// and puts it into that pool. The stand-in ReturnContainer is patched with real Harmony, so the prefix binding and its
// skip are exercised for real.

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
    public class AbstractGame { }
    public class GameWorld { }
}

/// <summary>The game's pool, member for member in what matters here (PoolWithoutAlloc.cs:25-66).</summary>
public class PoolWithoutAlloc<T> where T : class, new()
{
    public T[] gparam_0;
    public int _increaseBufferStep = 5;

    public PoolWithoutAlloc(int poolSize)
    {
        gparam_0 = new T[poolSize];
        for (int i = 0; i < gparam_0.Length; i++) gparam_0[i] = new T();
    }

    public T GetObject()
    {
        for (int i = 0; i < gparam_0.Length; i++)
        {
            if (gparam_0[i] != null) { var result = gparam_0[i]; gparam_0[i] = null; return result; }
        }
        ResizeBufferIfRequired(gparam_0.Length + 1);
        var last = gparam_0[gparam_0.Length - 1];
        gparam_0[gparam_0.Length - 1] = null;
        return last;
    }

    public void ResizeBufferIfRequired(int volume)
    {
        if (volume <= gparam_0.Length) return;
        int old = gparam_0.Length;
        Array.Resize(ref gparam_0, Math.Max(old + _increaseBufferStep, volume));
        for (int i = old; i < gparam_0.Length; i++) gparam_0[i] = new T();
    }

    public void PutObject(T item)
    {
        for (int i = 0; i < gparam_0.Length; i++)
        {
            if (gparam_0[i] == null) { gparam_0[i] = item; return; }
        }
    }

    public int Count(T item) => gparam_0.Count(slot => ReferenceEquals(slot, item));
}

namespace Audio.SpatialSystem
{
    /// <summary>Equal by ID on purpose: two distinct containers can compare equal, as the identity check needs.</summary>
    public class AudioSourceContainer
    {
        public int ID { get; set; }
        public int Clears;
        public bool Initialized;
        public void Clear() { Clears++; Initialized = false; }
        public override bool Equals(object other) => other is AudioSourceContainer c && c.ID == ID;
        public override int GetHashCode() => ID;
    }
}

namespace Audio.SpatialSystem.AudioSourceContainers
{
    using Audio.SpatialSystem;

    public class AudioSourceContainerData
    {
        private PoolWithoutAlloc<AudioSourceContainer> pool = new(8);
        /// <summary>Makes reading the pool throw, which is how a check makes the prefix fail.</summary>
        public bool ThrowOnPool;
        public PoolWithoutAlloc<AudioSourceContainer> _audioContainersPool
        {
            get { if (ThrowOnPool) throw new InvalidOperationException("pool unreadable"); return pool; }
            set => pool = value;
        }
        public PoolWithoutAlloc<AudioSourceContainer> Pool => pool;
        public int _nextID;
        public int Calls;

        public AudioSourceContainer GetContainer()
        {
            var container = pool.GetObject();
            container.Clear();
            container.ID = _nextID++;
            container.Initialized = true;
            return container;
        }

        // No inlining: a patched method that the JIT inlined into its caller would run unpatched.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ReturnContainer(AudioSourceContainer container)
        {
            Calls++;
            container.Clear();
            pool?.PutObject(container);
        }
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
