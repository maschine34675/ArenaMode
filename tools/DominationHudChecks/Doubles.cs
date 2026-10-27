// External Unity/TMP, config, match-state and pickup-state boundaries.
// Actual score, objective, powerup and shared-layout sources are linked.
// Geometry is modeled; native font rendering is not claimed.
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
        public Rect rect => new(sizeDelta.x + (parent is RectTransform p ? p.rect.width * (anchorMax.x - anchorMin.x) : 0), sizeDelta.y);
    }
    public readonly record struct Rect(float width, float height);
    public readonly record struct Vector2(float x, float y)
    {
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public readonly record struct Color(float r, float g, float b, float a = 1)
    { public static Color clear => new(0, 0, 0, 0); }
    public readonly record struct Color32(byte r, byte g, byte b, byte a)
    { public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }
    public static class Mathf
    {
        public const float Infinity = float.PositiveInfinity;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
    }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.UI
{
    public sealed class Image : UnityEngine.Component { public UnityEngine.Color color; public bool raycastTarget; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center, Top, TopLeft }
    public enum TextOverflowModes { Ellipsis }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public string text;
        public bool enableWordWrapping, enableAutoSizing, richText;
        public float fontSizeMin, fontSizeMax;
        public UnityEngine.Color color;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public UnityEngine.RectTransform rectTransform => gameObject.transform;
        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height)
            => new((value?.Length ?? 0) * 8f, 19f);
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
    internal sealed class ArenaMatch
    {
        internal ModeBase Mode;
        internal int Kills, Deaths, Assists, Score, Streak, BestStreak;
        internal float Elapsed;
        internal ArenaScoreboard Scoreboard;
    }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
    internal class ModeBase { internal string HudStatusLine; }
    internal sealed class TeamDeathmatchMode : ModeBase
    {
        internal bool ObjectiveHudVisible, IsSearchActive;
        internal float PlayingElapsed;
        internal string ObjectiveStatusLine = "A USEC | B CONTESTED | C BEAR";
    }
    internal sealed class ZombieMode : ModeBase { }
    internal sealed class TacticalMode : ModeBase { internal bool IsTeamArena; }
    internal enum ArenaScoreboardSide { Friendly, Opponent }
    internal sealed class ArenaScoreboardEntry { internal int Score; internal string DisplayName; }
    internal sealed class ArenaScoreboard
    {
        internal ArenaScoreboardEntry Totals(ArenaScoreboardSide side) => new();
        internal ArenaScoreboardEntry Leader(ArenaScoreboardSide side) => null;
    }
}
namespace ArenaMode.Config
{
    internal sealed class Entry<T> { internal T Value; }
    internal static class Settings
    {
        internal static Entry<float> ScorePanelOffsetX = new(), ScorePanelOffsetY = new();
    }
}
namespace ArenaMode.Effects
{
    internal static class ArenaTeamPalette { internal static UnityEngine.Color FriendlyUi = new(0, 0, 1); }
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
    internal static class ArenaUiTheme
    {
        internal static readonly UnityEngine.Color Surface = new(0, 0, 0);
        internal static readonly UnityEngine.Color Muted = new(.5f, .5f, .5f);
        internal static UnityEngine.Color WithAlpha(UnityEngine.Color color, float alpha) => new(color.r, color.g, color.b, alpha);
        internal static void Decorate(UnityEngine.RectTransform parent) { }
        internal static UnityEngine.UI.Image Fill(UnityEngine.RectTransform parent, string name, UnityEngine.Color color)
        {
            var go = new UnityEngine.GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.anchorMax = UnityEngine.Vector2.one;
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color; return image;
        }
        internal static UnityEngine.UI.Image Rule(UnityEngine.RectTransform parent, string name, UnityEngine.Color color,
            float top = 0, float inset = 0, float thickness = 1) => Fill(parent, name, color);
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
