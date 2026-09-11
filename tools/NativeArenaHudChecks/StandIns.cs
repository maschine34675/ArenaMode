// External contracts, not a Unity renderer or a native Harmony execution test.
using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value) => value.Destroyed = true;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => this as Transform ?? gameObject.Components.OfType<Transform>().First();
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MissingComponentException : Exception { public MissingComponentException(string message) : base(message) { } }
    public class Transform : Component
    {
        public Transform Parent;
        public bool IsChildOf(Transform parent)
        { for (Transform node = this; node != null; node = node.Parent) if (ReferenceEquals(node, parent)) return true; return false; }
    }
    public class RectTransform : Transform { }
    public sealed class CanvasGroup : Component
    {
        public float alpha = 1;
        public bool enabled = true, interactable = true, blocksRaycasts = true, ignoreParentGroups;
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public bool activeSelf = true;
        public void SetActive(bool active) => activeSelf = active;
        public bool ThrowOnGet;
        public int AddCalls, GetCalls, DuplicateCanvasErrors;
        public T GetComponent<T>() where T : Component
        {
            GetCalls++;
            if (ThrowOnGet) throw new InvalidOperationException("injected native lookup failure");
            return Components.OfType<T>().FirstOrDefault(c => c != null);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            AddCalls++;
            // Observed engine contract: logs and returns null, not a managed
            // exception or a second CanvasGroup as the old fake allowed.
            if (typeof(T) == typeof(CanvasGroup) && Components.OfType<CanvasGroup>().Any(c => c != null))
            { DuplicateCanvasErrors++; return null; }
            var value = new T { gameObject = this }; Components.Add(value); return value;
        }
    }
}
namespace TMPro { public class TextMeshProUGUI : UnityEngine.Component { } }
namespace EFT.UI.BattleTimer
{
    public class MainTimerPanel : UnityEngine.Object
    {
        public TMPro.TextMeshProUGUI _currentState, TimerText;
    }
}
namespace EFT
{
    public class GameWorld : UnityEngine.Object { }
    public class AbstractGame : UnityEngine.Object { public UI.GameUI GameUi = new(); }
}
namespace EFT.UI
{
    public class GameUI : UnityEngine.Object { public ExtractionTimersPanel TimerPanel = new(); }
    public class ExtractionTimersPanel : UnityEngine.Object
    {
        public UnityEngine.RectTransform _timersPanel = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
        public UnityEngine.RectTransform _mainDescription = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
        public BattleTimer.MainTimerPanel _mainTimerPanel = new();
        public ExtractionTimersPanel()
        {
            _timersPanel.gameObject.AddComponent<UnityEngine.CanvasGroup>();
            var state = new UnityEngine.GameObject();
            state.AddComponent<UnityEngine.RectTransform>().Parent = _mainDescription;
            _mainTimerPanel._currentState = state.AddComponent<TMPro.TextMeshProUGUI>();
            var clock = new UnityEngine.GameObject();
            clock.AddComponent<UnityEngine.RectTransform>();
            _mainTimerPanel.TimerText = clock.AddComponent<TMPro.TextMeshProUGUI>();
        }
        public void ShowTimer(bool showExits, bool updateExits = false) { }
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static readonly Logger ModLogger = new(); }
    public sealed class Logger
    {
        public readonly List<string> Warnings = new(), Infos = new();
        public void LogWarning(string text) => Warnings.Add(text);
        public void LogInfo(string text) => Infos.Add(text);
    }
}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle
    {
        public static event Action<EFT.GameWorld> OnRaidStarted;
        public static event Action OnRaidEnded;
        public static void Start(EFT.GameWorld world) => OnRaidStarted?.Invoke(world);
        public static void End() => OnRaidEnded?.Invoke();
        public static int Starts => OnRaidStarted?.GetInvocationList().Length ?? 0;
        public static int Ends => OnRaidEnded?.GetInvocationList().Length ?? 0;
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] arguments)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, arguments, null);
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public sealed class Harmony
    {
        public static int Patches, Unpatches;
        public static bool ThrowOnUnpatch, ThrowOnPatch;
        public Harmony(string id) { }
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        { Patches++; if (ThrowOnPatch) throw new InvalidOperationException("injected patch failure"); }
        public void UnpatchSelf()
        { Unpatches++; if (ThrowOnUnpatch) throw new InvalidOperationException("injected unpatch failure"); }
    }
}
