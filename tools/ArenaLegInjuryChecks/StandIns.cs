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
        internal static ConfigEntry<int> LegInjurySlowdownReductionPercent { get; } = new();
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive { get; set; }
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

namespace EFT
{
    public sealed class GameWorld
    {
        public Player MainPlayer { get; set; }
    }

    public class Player
    {
        public enum ESpeedLimit
        {
            HealthCondition,
            Aiming,
            Swamp,
        }

        public bool IsAI { get; set; }
        public MovementContext MovementContext { get; set; }
    }

    public class MovementContext
    {
        public void AddStateSpeedLimit(float speedLimit, Player.ESpeedLimit cause)
        {
        }

        public void AddStateSpeedLimit(float speedLimit, Player.ESpeedLimit cause, float duration)
        {
        }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: parameters, modifiers: null);
    }
}

namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPrefixAttribute : Attribute
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
