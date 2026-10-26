// External Unity/TMP, match-state, score-anchor and pickup-state boundaries.
// The three layout/view files are compiled unchanged; no rendering is claimed.
namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }
    public class GameObject
    {
        public static readonly List<GameObject> All = new();
        public readonly string name;
        public readonly List<Component> Components = new();
        public readonly RectTransform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            Components.Add(transform);
            foreach (var type in types)
                if (type != typeof(RectTransform))
                {
                    var component = (Component)Activator.CreateInstance(type);
                    component.gameObject = this; Components.Add(component);
                }
            All.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this }; Components.Add(component); return component; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public void SetActive(bool active) => activeSelf = active;
    }
    public class Transform : Component
    {
        public Transform parent;
        public void SetParent(Transform target, bool preserveWorld) => parent = target;
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition, offsetMin, offsetMax;
    }
    public readonly record struct Vector2(float x, float y)
    {
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public readonly record struct Color(float r, float g, float b, float a = 1);
    public static class Mathf { public static int CeilToInt(float value) => (int)Math.Ceiling(value); }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.UI
{
    public sealed class Image : UnityEngine.Component { public UnityEngine.Color color; public bool raycastTarget; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public string text;
        public bool enableWordWrapping;
        public UnityEngine.RectTransform rectTransform => gameObject.transform;
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public sealed class Player { }
    public sealed class GameWorld { public Player MainPlayer = new(); }
}
namespace ArenaMode.Modes
{
    internal sealed class ArenaMatch { internal object Mode; }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
    internal sealed class TeamDeathmatchMode
    {
        internal bool ObjectiveHudVisible, IsSearchActive;
        internal string ObjectiveStatusLine = "A USEC | B CONTESTED | C BEAR";
    }
}
namespace ArenaMode.Pickups
{
    internal static class ArenaPickupRuntime
    {
        internal static bool Live = true;
        internal static float NoticeUntil;
        internal static string LastNotice = "Picked up shield";
        internal static bool IsLiveActor(EFT.Player player) => Live && player != null;
    }
    internal sealed class PowerupState
    { internal float DamageSeconds, ShieldSeconds, ShieldPoints, JumpSeconds, AmmoSeconds; }
    internal static class ArenaPowerups
    {
        internal static PowerupState State = new();
        internal static PowerupState Read(EFT.Player player) => State;
    }
}
namespace ArenaMode.Hud
{
    // Score offsets are supplied at the public layout anchor; the production
    // ScorePanel/config implementation is intentionally not replicated here.
    internal static class ScorePanel
    {
        internal const float PanelHeight = 84f;
        internal static UnityEngine.Vector2 Position = new(0, -100);
    }
    internal static class ArenaUiTheme
    {
        internal static readonly UnityEngine.Color Surface = new(0, 0, 0);
        internal static UnityEngine.Color WithAlpha(UnityEngine.Color color, float alpha) => new(color.r, color.g, color.b, alpha);
        internal static void Decorate(UnityEngine.RectTransform parent) { }
    }
    internal static class HudStyle
    {
        internal static TMPro.TextMeshProUGUI CreateText(UnityEngine.RectTransform parent, string name, float size,
            TMPro.TextAlignmentOptions alignment)
        {
            var go = new UnityEngine.GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<TMPro.TextMeshProUGUI>();
        }
    }
}
