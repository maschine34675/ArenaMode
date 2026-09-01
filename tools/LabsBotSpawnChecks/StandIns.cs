// The filters and patches are production source. These minimal external types
// model call boundaries and shared mutable inputs, not Unity/Harmony execution.
using System.Reflection;

public sealed class BotZone
{
    public string NameZone;
    public EFT.Game.Spawning.ISpawnPoint[] SpawnPoints;
    public BotZone(string name) => NameZone = name;
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) => (this.x, this.y, this.z) = (x, y, z);
        public static float Distance(Vector3 left, Vector3 right)
        {
            var dx = left.x - right.x;
            var dy = left.y - right.y;
            var dz = left.z - right.z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}

namespace EFT
{
    public sealed class BotOwner { }

    public sealed class BotCreationData
    {
        public int ProfileCount = 3;
        public List<UnityEngine.Vector3> Positions = new();
        public bool Stopped;
    }

    public sealed class BotSpawner
    {
        public int DispatchCalls;
        public int InSpawnProcess;
        public IEnumerable<BotZone> Zones;

        // Signatures checked against local decompiled EFT.BotSpawner.
        public IEnumerable<BotZone> SpawnZones(bool canBeSnipe) => Zones;

        public void SpawnBotsInZoneOnPositions(
            List<Game.Spawning.ISpawnPoint> openedPositions, BotZone botZone,
            BotCreationData data, Action<BotOwner> callback = null)
        {
            DispatchCalls++;
            InSpawnProcess += openedPositions.Count;
            foreach (var point in openedPositions)
                data.Positions.Add(point.Position);
            openedPositions.Clear();
        }
    }
}

namespace EFT.Game.Spawning
{
    [Flags]
    public enum ESpawnCategoryMask { None = 0, Player = 1, Bot = 2 }

    public interface ISpawnPoint
    {
        string BotZoneName { get; }
        UnityEngine.Vector3 Position { get; }
        ESpawnCategoryMask Categories { get; }
        bool SpawnBlocked { get; set; }
        float NextBornTime { get; set; }
        int CorePointId { get; set; }
    }

    public sealed class TestSpawnPoint : ISpawnPoint
    {
        public string BotZoneName { get; set; }
        public UnityEngine.Vector3 Position { get; set; }
        public ESpawnCategoryMask Categories { get; set; }
        public bool SpawnBlocked { get; set; }
        public float NextBornTime { get; set; }
        public int CorePointId { get; set; }
    }

    public enum ActionIfNotEnoughPoints
    {
        ReturnFoundPoints,
        ReturnNothing,
        DuplicateIfAtLeastOne,
        FillWithDiscardedPointsAndDuplicates
    }

    public sealed class SpawnSystem
    {
        public int SelectionCalls;
        public List<ISpawnPoint> Selection;

        // This is the PUBLIC six-argument method. The native interface wrapper
        // has a different signature and applies cooldowns after this returns.
        public List<ISpawnPoint> SelectAISpawnPoints(
            EFT.BotCreationData profileData, BotZone zone, int maxCount, float time,
            UnityEngine.Vector3? closestBossPos = null,
            ActionIfNotEnoughPoints mode = ActionIfNotEnoughPoints.ReturnNothing)
        {
            SelectionCalls++;
            return Selection;
        }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance;
            return parameters == null ? type.GetMethod(name, flags)
                : type.GetMethod(name, flags, null, parameters, null);
        }
    }

    public static class Priority { public const int First = 800, Last = 0; }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriority : Attribute
    {
        public int Value;
        public HarmonyPriority(int value) => Value = value;
    }
}

namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        public static readonly List<MethodBase> EnabledTargets = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => EnabledTargets.Add(GetTargetMethod()
            ?? throw new InvalidOperationException("Patch target did not resolve."));
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPostfixAttribute : Attribute { }
}

namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static bool ArmedForNextRaid;
        public static string ArmedMap;
    }
}

namespace ArenaMode
{
    public static class Plugin { public static readonly TestLogger ModLogger = new(); }
    public sealed class TestLogger
    {
        public readonly List<string> Warnings = new();
        public void LogWarning(object message) => Warnings.Add(message.ToString());
    }
}
