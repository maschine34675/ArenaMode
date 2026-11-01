using System.Reflection;

// Only external boundaries are doubled; the complete production adapter is linked.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static readonly List<Object> Scene = new();
        public static int Scans;
        public static Object[] FindObjectsOfType(Type type, bool includeInactive)
        { Scans++; return Scene.Where(o => type.IsInstanceOfType(o) && !o.Destroyed && (includeInactive || ((Component)o).gameObject.activeSelf)).ToArray(); }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b)
            || ReferenceEquals(b, null) && !ReferenceEquals(a, null) && a.Destroyed
            || ReferenceEquals(a, null) && !ReferenceEquals(b, null) && b.Destroyed;
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object { public GameObject gameObject = new(); }
    public class GameObject
    { public bool activeSelf = true, ThrowSetActive;
      public void SetActive(bool value) { if (ThrowSetActive) throw new Exception("hide failed"); activeSelf = value; } }
    public class RectTransform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta; public Quaternion localRotation; }
    public struct Quaternion { public float z; public static Quaternion Euler(float x, float y, float z) => new() { z = z }; }
    public struct Vector2(float x, float y) { public float x = x, y = y; public static Vector2 zero => new(0, 0); }
    public struct Vector3(float x, float y, float z) { public float x = x, y = y, z = z; }
    public struct Color(float r, float g, float b, float a = 1) { public float r = r, g = g, b = b, a = a; }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component { public object sprite = new(); public UnityEngine.RectTransform rectTransform = new(); }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Top, Center }
    public enum FontStyles { Normal, Bold }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public bool richText, enableAutoSizing, enableWordWrapping;
        public float fontSize;
        public TextAlignmentOptions alignment;
        public FontStyles fontStyle;
        public UnityEngine.RectTransform rectTransform = new();
        public UnityEngine.Color color;
    }
}
namespace HarmonyLib
{
    public class HarmonyMethod(Type type, string name) { internal MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic); }
    public class Harmony(string id)
    {
        internal static readonly Dictionary<string, List<(MethodInfo Target, HarmonyMethod Hook)>> Hooks = new();
        internal static int FailAt = -1, Count;
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (Count++ == FailAt) throw new Exception("patch failed");
            if (!Hooks.ContainsKey(id)) Hooks.Add(id, new());
            Hooks[id].Add((method, prefix ?? postfix));
        }
        public void UnpatchSelf() => Hooks.Remove(id);
        internal static void Call(string method, object target)
        {
            foreach (var (_, hook) in Hooks.Values.SelectMany(v => v).Where(p => p.Target.Name == method).ToArray())
                hook.Method.Invoke(null, new[] { target });
        }
    }
}
namespace BepInEx.Bootstrap
{
    public class Metadata { public Version Version = new(1, 2, 1); }
    public class Info { public Metadata Metadata = new(); public object Instance = new FakeDynamicMapsPlugin(); }
    public class FakeDynamicMapsPlugin { }
    public static class Chainloader { public static readonly Dictionary<string, Info> PluginInfos = new(); }
}
namespace ArenaMode.Bots
{
    public readonly struct DominationObjectivePoint(string id, UnityEngine.Vector3 position, bool? owner, bool contested)
    { public string Id { get; } = id; public UnityEngine.Vector3 Position { get; } = position;
      public bool? OwnerUsec { get; } = owner; public bool Contested { get; } = contested; }
}
namespace ArenaMode.Compat { internal static class DynamicMapsCorpseMarkers { internal const string PluginGuid = "com.mpstark.dynamicmaps"; } }
namespace ArenaMode.Effects
{
    internal static class ArenaTeamPalette
    { internal static UnityEngine.Color UsecUi => new(127f/255, 167f/255, 1); internal static UnityEngine.Color BearUi => new(1, 107f/255, 107f/255); }
}
namespace ArenaMode
{
    internal static class Plugin { internal static readonly Logger ModLogger = new(); }
    internal sealed class Logger
    { internal int Warnings; internal void LogDebug(string text) { } internal void LogWarning(string text) { Warnings++; } }
}
namespace DynamicMaps.Data
{
    public class MapDef { public List<string> MapInternalNames { get; set; } = new(); }
    public class MapMarkerDef
    {
        public string Text { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
        public UnityEngine.Vector3 Position { get; set; }
    }
}
namespace DynamicMaps.UI.Components
{
    public enum LayerStatus { Hidden, Underneath, OnTop, FullReveal }
    public class MapMarker : UnityEngine.Component
    {
        public string Id, Category, Icon;
        public TMPro.TextMeshProUGUI Label { get; } = new();
        public UnityEngine.UI.Image Image { get; } = new();
        public Dictionary<LayerStatus, float> LabelAlphaLayerStatus { get; } = new() { [LayerStatus.OnTop] = 0 };
        public UnityEngine.Vector2 Size { get; set; }
        private UnityEngine.Vector3 _position;
        private UnityEngine.Color _color;
        public bool Hidden;
        public int Moves;
        public UnityEngine.Vector3 Position
        {
            get => _position;
            set { _position = value; Moves++; _color.a = Hidden ? 0 : 1; Label.color.a = Hidden ? 0 : LabelAlphaLayerStatus[LayerStatus.OnTop]; }
        }
        public UnityEngine.Color Color { get => _color; set { _color = value; Label.color = value; } }
    }
    public class MapView : UnityEngine.Component
    {
        public Data.MapDef CurrentMapDef { get; private set; }
        public readonly List<MapMarker> Markers = new();
        public int Adds, Removes;
        public bool ThrowAdd, ThrowRemove;
        public MapView() { UnityEngine.Object.Scene.Add(this); }
        public void LoadMap(Data.MapDef def, UnityEngine.RectTransform mask)
        { if (CurrentMapDef != null) UnloadMap(); CurrentMapDef = def; HarmonyLib.Harmony.Call(nameof(LoadMap), this); }
        public void UnloadMap()
        { HarmonyLib.Harmony.Call(nameof(UnloadMap), this); foreach (var m in Markers) m.Destroyed = true; Markers.Clear(); CurrentMapDef = null; }
        public MapMarker AddMapMarker(Data.MapMarkerDef def)
        {
            if (ThrowAdd) throw new Exception("add failed");
            var marker = new MapMarker { Id = def.Text, Category = def.Category, Icon = def.ImagePath, Position = def.Position };
            Markers.Add(marker); Adds++; return marker;
        }
        public void RemoveMapMarker(MapMarker marker)
        { if (ThrowRemove) throw new Exception("remove failed"); Markers.Remove(marker); Removes++; marker.gameObject.SetActive(false); marker.Destroyed = true; }
    }
}
