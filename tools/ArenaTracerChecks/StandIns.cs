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
    using ArenaMode.Effects;
    using BepInEx.Configuration;

    internal static class Settings
    {
        internal static ConfigEntry<bool> TeamTracers { get; } = new();
        internal static string RequestedMode;
        internal static bool RequestedTeam;
        internal static ConfigEntry<bool> TracerEntry(string mode, bool protectedTeam)
        { RequestedMode = mode; RequestedTeam = protectedTeam; return TeamTracers; }
        internal static ConfigEntry<ArenaTracerColor> FriendlyTracerColor { get; } = new();
        internal static ConfigEntry<ArenaTracerColor> OpponentTracerColor { get; } = new();
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive { get; set; }
        internal static string ActiveMode { get; set; } = "tactical";
    }
}

namespace ArenaMode
{
    internal static class Plugin
    {
        internal static TestLogger ModLogger { get; } = new();
    }

    internal sealed class TestLogger
    {
        internal List<string> Warnings { get; } = new();
        public void LogWarning(object message) => Warnings.Add(message?.ToString());
    }
}

namespace UnityEngine
{
    public struct Color
    {
        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public float r;
        public float g;
        public float b;
        public float a;
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
        public float sqrMagnitude => x * x + y * y + z * z;

        public static Vector3 operator -(Vector3 left, Vector3 right)
            => new(left.x - right.x, left.y - right.y, left.z - right.z);
    }
}

namespace JsonType
{
    public enum TaxonomyColor
    {
        tracerYellow,
        tracerGreen,
        tracerRed,
        @default,
    }
}

namespace EFT.InventoryLogic
{
    using JsonType;

    public class Item
    {
    }

    public sealed class Ammo : Item
    {
        public bool Tracer { get; set; }
        public TaxonomyColor TracerColor { get; set; }
    }
}

namespace EFT
{
    public interface IPlayer
    {
        string ProfileId { get; }
    }

    public interface IObserverToPlayerBridge
    {
        IPlayer iPlayer { get; }
    }

    public sealed class TestPlayer : IPlayer
    {
        public string ProfileId { get; set; }
    }

    public sealed class TestBridge : IObserverToPlayerBridge
    {
        public IPlayer iPlayer { get; set; }
    }
}

namespace EFT.Ballistics
{
    using EFT.InventoryLogic;
    using UnityEngine;

    public sealed class Shot
    {
        public Item Ammo { get; set; }
        public List<Vector3> PositionHistory { get; } = new();
        public EFT.IObserverToPlayerBridge Player { get; set; }
        public string PlayerProfileID { get; set; }
        public int FragmentIndex { get; set; }
    }
}

public sealed class TracersLight
{
    public UnityEngine.Color Color;
    public int ShotTracerCalls { get; private set; }
    public int GrenadeTracerCalls { get; private set; }
    public bool ThrowOnCreate { get; set; }

    public void CreateTracer(EFT.Ballistics.Shot shot)
    {
    }

    public void CreateShotTracer(
        EFT.Ballistics.Shot shot,
        UnityEngine.Vector3 p0,
        UnityEngine.Vector3 p1)
    {
        if (ThrowOnCreate)
            throw new InvalidOperationException("test renderer failure");
        ShotTracerCalls++;
    }

    public void CreateGrenadeTracer(
        EFT.Ballistics.Shot shot,
        UnityEngine.Vector3 p0,
        UnityEngine.Vector3 p1)
    {
        if (ThrowOnCreate)
            throw new InvalidOperationException("test renderer failure");
        GrenadeTracerCalls++;
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
