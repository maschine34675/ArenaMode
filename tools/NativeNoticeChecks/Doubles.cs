// Model component ownership and destroyed Unity objects. Pixel rendering belongs to the game test.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed { get; private set; }
        public static void Destroy(Object value) => value.Destroyed = true;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) ||
            (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public class GameObject(string name) : Object
    {
        public string name { get; } = name;
        public bool activeSelf = true;
        public void SetActive(bool value) => activeSelf = value;
        public List<CanvasGroup> Groups { get; } = [];
        public T AddComponent<T>() where T : CanvasGroup, new()
        {
            var value = new T { gameObject = this };
            Groups.Add(value);
            return value;
        }
    }

    public class CanvasGroup : Object
    {
        public GameObject gameObject;
        public float alpha = 1f;
        public bool enabled = true, interactable = true, blocksRaycasts = true;
    }

    public static class Canvas
    {
        public static event Action willRenderCanvases;
        public static int Subscribers => willRenderCanvases?.GetInvocationList().Length ?? 0;
        public static void Render() => willRenderCanvases?.Invoke();
    }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : UnityEngine.Object
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}

namespace EFT
{
    public class GameWorld : UnityEngine.Object { }
    public class AbstractGame : UnityEngine.Object { }
}

namespace EFT.UI
{
    public class MenuScreen
    {
        public UnityEngine.GameObject _alphaWarningGameObject = new("BetaWarningPanel");
        public class MainMenuBaseScreenController
        {
            public event Action OnClose;
            public void Close() => OnClose?.Invoke();
        }
        public class MainMenuScreenController : MainMenuBaseScreenController { }
        public class MainMenuReconnectScreenController : MainMenuBaseScreenController { }
    }
    public class PreloaderUI
    {
        public static PreloaderUI Instance;
        public static bool Instantiated => Instance != null;
        public LocalizedText _alphaVersionLabel;
    }
    public class LocalizedText : UnityEngine.Object
    {
        public UnityEngine.GameObject gameObject = new("AlphaLabel");
        public string LocalizationKey;
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
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSessionMode { public static bool GateActive; }
    internal static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.GameWorld ConfirmedWorld;
        public static EFT.AbstractGame ConfirmedGame;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) =>
            IsActive && ReferenceEquals(game, ConfirmedGame) && ReferenceEquals(world, ConfirmedWorld);
    }
    internal static class Plugin
    {
        public static NoticeLogger ModLogger { get; } = new();
        public class NoticeLogger { public void LogWarning(string message) => throw new Exception(message); }
    }
}
