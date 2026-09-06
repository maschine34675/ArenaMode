namespace UnityEngine
{
    public class Object { }
    public class Component : Object { public GameObject gameObject; }
    public class Transform : Component { public void SetParent(Transform parent, bool keep) { } }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta; }
    public struct Vector2(float x, float y) { public float x = x, y = y; }
    public struct Color(float r, float g, float b, float a) { public float r = r, g = g, b = b, a = a; }
    public class GameObject : Object
    {
        public static readonly Dictionary<string, GameObject> All = new();
        public readonly Dictionary<Type, Component> Components = new();
        public Transform transform;
        public bool activeSelf = true;
        public GameObject(string name, params Type[] types)
        { All[name] = this; transform = new RectTransform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components[typeof(T)] = c; return c; }
        public void SetActive(bool active) => activeSelf = active;
    }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component { public UnityEngine.Color color; public bool raycastTarget = true; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public enum FontStyles { Bold }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public UnityEngine.RectTransform rectTransform = new();
        public string text; public float fontSize; public bool raycastTarget;
        public FontStyles fontStyle; public UnityEngine.Color color;
    }
}
namespace ArenaMode.Hud
{
    internal static class HudStyle
    {
        public static readonly Dictionary<string, TMPro.TextMeshProUGUI> Texts = new();
        public static TMPro.TextMeshProUGUI CreateText(UnityEngine.Transform parent, string name, float size, TMPro.TextAlignmentOptions align)
        { var t = new TMPro.TextMeshProUGUI { fontSize = size, raycastTarget = false }; Texts[name] = t; return t; }
    }
}
namespace EFT { public class GameWorld { } public class AbstractGame { } }
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive = true, Exact = true;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => Exact;
    }
}
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    // Only the state boundary is modeled. The production partial contains all
    // notice decisions and formatting; no copied implementation in this fixture.
    public sealed partial class TacticalMode
    {
        public enum Phase { Staging, Buy, Live, Aftermath, Rebinding, Ended }
        internal const int TeamEnemyCount = 5;
        internal bool _teamArena, _teamEnemiesRequested, _playerDown;
        internal Phase _phase;
        internal List<object> _squad = new();
        internal int _squadRequested;
        public bool IsComplete;
        public int RoundNumber = 1;
    }
}
