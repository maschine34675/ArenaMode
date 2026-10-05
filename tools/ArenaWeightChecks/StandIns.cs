using System.Reflection;

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public T Value { get; set; }
    }
}

namespace ArenaMode.Config
{
    using BepInEx.Configuration;

    internal static class Settings
    {
        internal static ConfigEntry<bool> MovementEnabled { get; } = new();
        internal static ConfigEntry<float> CarryWeightLimitMultiplier { get; } = new();
        internal static ConfigEntry<bool> NoInertia { get; } = new();
        internal static ConfigEntry<bool> SlideEnabled { get; } = new();
        internal static ConfigEntry<bool> InfiniteStamina { get; } = new();
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive { get; set; }
        internal static bool ArmedForNextRaid { get; set; }
    }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance { get; set; }
        public static bool Instantiated => Instance != null;
    }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float x;
        public float y;
    }

    public struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x;
        public float y;
        public float z;
    }

    public sealed class GameObject
    {
        private readonly Dictionary<Type, object> _components = new();

        public T AddComponent<T>() where T : new()
        {
            var component = new T();
            _components[typeof(T)] = component;
            return component;
        }

        public T GetComponent<T>() where T : class
            => _components.TryGetValue(typeof(T), out object value) ? (T)value : null;
    }
}

public class PhysicalBase
{
    public UnityEngine.Vector2 WalkOverweightLimits;
    public UnityEngine.Vector2 BaseOverweightLimits;
    public UnityEngine.Vector2 SprintOverweightLimits;
    public UnityEngine.Vector2 WalkSpeedOverweightLimits;
    public UnityEngine.Vector3 BaseInertiaLimits;
    public bool EncumberDisabled { get; set; }
    public int ThrowWeightLimitsUpdatesRemaining { get; set; }
    public int WeightLimitsUpdateAttemptCount { get; private set; }
    public int WeightLimitsUpdateCount { get; private set; }
    public int WeightUpdateCount { get; private set; }

    public void UpdateWeightLimits()
    {
    }

    public void OnWeightLimitsUpdated()
    {
        WeightLimitsUpdateAttemptCount++;
        if (ThrowWeightLimitsUpdatesRemaining > 0)
        {
            ThrowWeightLimitsUpdatesRemaining--;
            throw new InvalidOperationException("transient test failure");
        }

        WeightLimitsUpdateCount++;
        UpdateWeightLimits();
        OnWeightUpdated();
    }

    public virtual void OnWeightUpdated() => WeightUpdateCount++;
}

namespace EFT
{
    public sealed class Stamina
    {
        public float Current { get; set; }
        public float TotalCapacity { get; set; } = 100f;
    }

    public sealed class Physical : global::PhysicalBase
    {
        public Stamina Stamina { get; } = new();
        public Stamina HandsStamina { get; } = new();
        public Stamina Oxygen { get; } = new();
    }

    public sealed class HealthController
    {
        public bool IsAlive { get; set; } = true;
    }

    public sealed class MovementContext
    {
    }

    public sealed class Player
    {
        public bool IsAI { get; set; }
        public Physical Physical { get; set; }
        public HealthController HealthController { get; set; } = new();
        public MovementContext MovementContext { get; set; } = new();
        public UnityEngine.GameObject gameObject { get; } = new();

        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public sealed class GameWorld
    {
        public Player MainPlayer { get; set; }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}

namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPostfixAttribute : Attribute
    {
    }

    public abstract class ModulePatch
    {
        internal static List<MethodBase> EnabledTargets { get; } = new();

        protected abstract MethodBase GetTargetMethod();

        public void Enable()
        {
            MethodBase target = GetTargetMethod()
                ?? throw new MissingMethodException("production patch target did not resolve");
            EnabledTargets.Add(target);
        }
    }
}

namespace ArenaMode.Movement
{
    using EFT;

    internal static class SprintAdsPatches
    {
        internal static void ClearBypassSafety()
        {
        }
    }

    internal static class ArenaAirVault
    {
        internal static void Tick()
        {
        }
    }

    internal static class VaultingTuning
    {
        internal static int RestoreCount { get; private set; }
        internal static void Restore() => RestoreCount++;
    }

    internal static class ArenaBunnyhop { internal static void TickOwner() { } }
    internal static class ArenaMovementPatches
    {
        internal static void ResetPerRaidState()
        {
        }

        internal static void RememberSlide(Player player, ArenaSlideController slide)
        {
        }
    }

    internal sealed class ArenaSlideController
    {
        // Mirrors the MonoBehaviour flag the production runtime now reads: the
        // controller disables itself on the body's death and must be adopted
        // again, not re-added, if that same body is ever live once more.
        internal bool enabled = true;
        internal int Inits;

        internal void Init(Player player, MovementContext context) => Inits++;
    }
}
