using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed { get; internal set; }

        public static void Destroy(Object value)
        {
            if (value != null)
                value.Destroyed = true;
        }
    }

    public class Component : Object
    {
        internal GameObject Owner;

        public GameObject gameObject
        {
            get
            {
                if (Owner == null)
                    Owner = new GameObject(GetType().Name);
                return Owner;
            }
        }

        public Transform transform => gameObject.transform;
    }

    public class MonoBehaviour : Component
    {
    }

    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new();

        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform();
            Created.Add(this);
        }

        public static List<GameObject> Created { get; } = new();
        public string name { get; }
        public HideFlags hideFlags { get; set; }
        public Transform transform { get; }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { Owner = this };
            _components[typeof(T)] = component;
            return component;
        }

        public T GetComponent<T>() where T : Component
            => _components.TryGetValue(typeof(T), out Component value) ? (T)value : null;
    }

    public sealed class Transform
    {
        public Transform parent { get; private set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }

        public void SetParent(Transform value, bool worldPositionStays)
            => parent = value;
    }

    public sealed class Light : Component
    {
        public bool enabled { get; set; }
        public float intensity { get; set; }
        public float bounceIntensity { get; set; }
        public int cullingMask { get; set; }
        public Color color { get; set; }
        public LightType type { get; set; }
        public LightShadows shadows { get; set; }
        public LightRenderMode renderMode { get; set; }
    }

    public enum HideFlags
    {
        None,
        HideAndDontSave,
    }

    public enum LightType
    {
        Spot,
        Directional,
        Point,
    }

    public enum LightShadows
    {
        None,
        Hard,
        Soft,
    }

    public enum LightRenderMode
    {
        Auto,
        ForcePixel,
        ForceVertex,
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x { get; }
        public float y { get; }
        public float z { get; }
        public static Vector3 zero => new(0f, 0f, 0f);
    }

    public readonly struct Quaternion
    {
        public Quaternion(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static Quaternion Euler(float x, float y, float z)
            => new(x, y, z);
    }

    public readonly struct Color
    {
        public Color(float red, float green, float blue, float alpha = 1f)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }

        public float r { get; }
        public float g { get; }
        public float b { get; }
        public float a { get; }
    }

    public static class Time
    {
        public static float time { get; set; }
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        private static IDictionary<string, object> _pluginInfos =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static bool ThrowOnRead { get; set; }

        public static IDictionary<string, object> PluginInfos
        {
            get
            {
                if (ThrowOnRead)
                    throw new InvalidOperationException("plugin registry unavailable");
                return _pluginInfos;
            }
            set => _pluginInfos = value;
        }
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
    public sealed class GameWorld : UnityEngine.MonoBehaviour
    {
        public string LocationId { get; set; }
    }

    public sealed class GlobalConfiguration
    {
        public int TimeBeforeDeployLocal;
    }

    public sealed class LocalGame
    {
        public void vmethod_2() { }
    }

    public sealed class BaseLocalGame<T>
    {
        public void vmethod_6() { }
    }

    public sealed class EftGamePlayerOwner { }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static System.Reflection.MethodBase Method(Type type, string name)
            => type.GetMethod(name);
    }
}

namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        protected abstract System.Reflection.MethodBase GetTargetMethod();
        public void Enable() { }
    }

    public sealed class PatchPrefixAttribute : Attribute { }
    public sealed class PatchPostfixAttribute : Attribute { }
}

namespace ArenaMode.Config
{
    internal sealed class FakeConfigEntry<T>
    {
        internal FakeConfigEntry(T value) => Value = value;
        internal T Value { get; set; }
    }

    internal static class Settings
    {
        internal static FakeConfigEntry<bool> FastDeploy { get; } = new(true);
        internal static FakeConfigEntry<bool> ArenaLightingEnabled { get; } = new(true);
        internal static FakeConfigEntry<float> ArenaLightingStrength { get; } = new(1f);
        internal static FakeConfigEntry<int> ArenaLightingPressureSeconds { get; } = new(30);
        internal static FakeConfigEntry<float> ArenaLightingOutcomeSeconds { get; } = new(3f);
        internal static FakeConfigEntry<bool> ArenaLightingRespectExternalMods { get; } = new(true);
        internal static FakeConfigEntry<ArenaMode.Effects.ArenaTracerColor> FriendlyTracerColor { get; }
            = new(ArenaMode.Effects.ArenaTracerColor.Cyan);
        internal static FakeConfigEntry<ArenaMode.Effects.ArenaTracerColor> OpponentTracerColor { get; }
            = new(ArenaMode.Effects.ArenaTracerColor.Red);
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive { get; set; }
        internal static bool ArmedForNextRaid { get; set; }
        internal static string ActiveMode { get; set; }
        internal static object ExactGame { get; set; }
        internal static object ExactWorld { get; set; }
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
}

namespace ArenaMode.Modes
{
    internal interface IArenaMode
    {
    }

    internal sealed class ArenaMatch
    {
        private Action<ArenaAnnouncerCue> _announcerCue;

        internal ArenaMatch(IArenaMode mode) => Mode = mode;

        internal IArenaMode Mode { get; }
        internal int AnnouncerSubscriberCount => _announcerCue?.GetInvocationList().Length ?? 0;

        internal event Action<ArenaAnnouncerCue> OnAnnouncerCue
        {
            add => _announcerCue += value;
            remove => _announcerCue -= value;
        }

        internal void Emit(ArenaAnnouncerCue cue) => _announcerCue?.Invoke(cue);
    }

    internal sealed class EndlessMode : IArenaMode
    {
    }

    internal sealed class RoundsMode : IArenaMode
    {
        internal sealed class RoundState
        {
            internal int Number { get; set; } = 1;
        }

        internal RoundState Round { get; } = new();
        internal bool InIntermission { get; set; }
        internal float IntermissionRemaining { get; set; }
        internal float TimeRemaining { get; set; } = -1f;
    }

    internal sealed class TacticalMode : IArenaMode
    {
        internal enum Phase
        {
            Staging,
            Buy,
            Live,
            Aftermath,
            Rebinding,
            Ended,
        }

        internal Phase CurrentPhase { get; set; }
        internal float PhaseRemaining { get; set; }
    }
}

namespace ArenaMode
{
    internal sealed class FakeLogger
    {
        internal List<string> Infos { get; } = new();
        internal List<string> Warnings { get; } = new();
        internal List<string> Debugs { get; } = new();

        internal void LogInfo(object value) => Infos.Add(value?.ToString() ?? string.Empty);
        internal void LogWarning(object value) => Warnings.Add(value?.ToString() ?? string.Empty);
        internal void LogDebug(object value) => Debugs.Add(value?.ToString() ?? string.Empty);
    }

    internal static class Plugin
    {
        internal static FakeLogger ModLogger { get; } = new();
    }
}
