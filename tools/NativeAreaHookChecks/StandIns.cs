using System.Reflection;
using System.Runtime.ExceptionServices;
using ArenaMode.Session;
using ArenaMode.Shared;

namespace ArenaMode.Session.Maps
{
    // Renderer seam only. The real preview is exercised by AreaMapPreviewChecks.
    internal sealed class ArenaAreaMapPreview
    {
        internal static ArenaAreaMapPreview Last;
        internal int Shows, Refreshes;
        internal bool Closed = true;
        internal ArenaAreaSelectionState State;
        internal void Show(UnityEngine.Transform screen, UnityEngine.GameObject info, TMPro.TMP_Text text)
        { Last = this; Shows++; Closed = false; }
        internal void Refresh(ArenaAreaSelectionState state) { Refreshes++; State = state; }
        internal void Close() { Closed = true; State = null; }
    }
}

internal static class NativeHooks
{
    internal static readonly List<string> Trace = new();
    internal static object Call(string patch, string method, params object[] args)
    {
        var type = typeof(ArenaLocationSelectionPatch).GetNestedType(patch, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production patch " + patch);
        var target = type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production callback " + patch + "." + method);
        try { return target.Invoke(null, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        // Read-only field lookup is enough for this hook. This does not claim
        // to exercise Harmony's emitted ref-return delegate or patch detours.
        public delegate F FieldRef<T, F>(T value);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            var field = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(T).Name, name);
            return value => (F)field.GetValue(value);
        }
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameters, null);
        public static MethodInfo PropertySetter(Type type, string name) => type.GetProperty(name).SetMethod;
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { if (GetTargetMethod() == null) throw new MissingMethodException(GetType().Name); }
    }
    public class PatchPrefixAttribute : Attribute { }
    public class PatchPostfixAttribute : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => ReferenceEquals(a, null) ? ReferenceEquals(b, null) || b.Destroyed
                : ReferenceEquals(b, null) ? a.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => other is Object value && this == value;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(GameObject target)
        {
            if (target == null) return;
            foreach (var child in target.transform.Children.ToArray()) Destroy(child.gameObject);
            target.transform.SetParent(null, false);
            target.Destroyed = true;
            foreach (var component in target.Components)
            {
                component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
                component.Destroyed = true;
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => gameObject.GetComponentsInChildren<T>(inactive);
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform parent { get; private set; }
        public void SetParent(Transform value, bool worldPositionStays)
        { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta; }
    public class Sprite { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public string name;
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public int SetActiveCalls;
        public GameObject(string name) { this.name = name; transform = AddComponent<Transform>(); }
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = types.Contains(typeof(RectTransform)) ? AddComponent<RectTransform>() : AddComponent<Transform>();
            foreach (var type in types.Where(type => !typeof(Transform).IsAssignableFrom(type)))
                Components.Add((Component)Activator.CreateInstance(type));
            foreach (var component in Components) component.gameObject = this;
        }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public T AddComponent<T>() where T : Component, new()
        { var value = new T { gameObject = this }; Components.Add(value); return value; }
        public void SetActive(bool active)
        { if (Destroyed) throw new InvalidOperationException("Changed destroyed view " + name); activeSelf = active; SetActiveCalls++; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            var result = new List<T>();
            void Visit(GameObject node)
            {
                if (node == null || (!inactive && !node.activeInHierarchy)) return;
                result.AddRange(node.Components.OfType<T>().Where(value => value != null));
                foreach (var child in node.transform.Children) Visit(child.gameObject);
            }
            Visit(this); return result.ToArray();
        }
    }
    public static class Time { public static float unscaledTime; }
}
namespace TMPro
{
    public class TMP_Text : UnityEngine.Component
    {
        public string text;
        public object font, fontSharedMaterial;
        public float fontSize;
        public UnityEngine.Color color;
        public bool enableWordWrapping, richText, raycastTarget;
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
    }
    public class TextMeshProUGUI : TMP_Text { }
}
namespace ArenaMode
{
    public static class Plugin { public static TestLogger ModLogger = new(); }
    public sealed class TestLogger
    {
        public readonly List<string> Warnings = new();
        public void LogInfo(object message) { } public void LogDebug(object message) { }
        public void LogWarning(object message) => Warnings.Add(message?.ToString());
    }
}
namespace ArenaMode.Config
{
    internal static class ArenaParticipantSettings
    {
        internal static readonly Dictionary<string, int> Values = new();
        internal static bool Reject;
        internal static int Writes;
        internal static int Get(string map, string mode)
            => Values.TryGetValue((map ?? "") + ":" + mode, out var value) ? value : ArenaParticipantPolicy.DefaultTotal(map, mode);
        internal static bool TrySelect(string map, string mode, int total)
        {
            if (Reject || Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid
                || !ArenaParticipantPolicy.TryResolve(map, mode, false, false, total, out _, out _)) return false;
            Values[map + ":" + mode] = total; Writes++; return true;
        }
    }
    public sealed class Entry<T> { public T Value; }
    public static class Settings
    {
        public static Entry<string> GameMode = new() { Value = "teamdeathmatch" };
        public static Entry<string> TeamDeathmatchArea = new() { Value = ArenaAreaCatalog.FullMapId };
        public static Entry<int> TeamDeathmatchSize = new() { Value = 5 };
        public static Entry<int> AreaTeamSize = new() { Value = 2 };
        public static bool TrySelectAreaTeamSize(int size)
        { if (RejectAreaWrite || size < 2 || size > 5) return false; AreaTeamSize.Value = size; return true; }
        public static string Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
        public static bool RejectAreaWrite;
        public static int AreaWrites;
        public static string SelectedGameModeId => Modes.ArenaModeCatalog.NormalizeId(GameMode.Value);
        public static bool IsArenaMap(string map) => ArenaMapCatalog.AllowlistContains(Allowlist, map);
        public static bool TrySelectGameMode(string mode) { GameMode.Value = mode; return true; }
        // External BepInEx setting boundary. The real writer is compiled with
        // the client and source-reviewed; fixtures inject its bool contract.
        public static bool TrySelectArea(string area)
        {
            if (TeamDeathmatchArea.Value == area) return true;
            NativeHooks.Trace.Add("area-write:" + area);
            AreaWrites++;
            if (RejectAreaWrite) return false;
            TeamDeathmatchArea.Value = area;
            return true;
        }
    }
}
namespace ArenaMode.Audio { public static class AnnouncerService { public static void EnsureLoaded() { } } }
namespace ArenaMode.Session
{
    // Presentation-only seams; these never decide map or mode admission.
    internal sealed class ArenaCompactMapInfo
    {
        internal UnityEngine.GameObject Root;
        internal void Show(UnityEngine.Transform screen, UnityEngine.GameObject nativeInfo, TMPro.TMP_Text text)
            => Root = nativeInfo;
        internal void Refresh(string name, UnityEngine.Sprite thumbnail) { }
        internal void Close() => Root = null;
    }
    internal sealed class ArenaPortedMapPresentation
    {
        internal static string DisplayName(string id, string fallback) => fallback;
        internal static ArenaPortedMapPresentation Create(string id, UnityEngine.RectTransform rect,
            TMPro.TMP_Text header, TMPro.TMP_Text size, Func<bool> selected) => null;
        internal void Refresh() { }
        internal void Close() { }
    }
    internal static class ArenaSession
    {
        internal static bool IsActive, ArmedForNextRaid;
        internal static int? Participants;
    }
    public static class ArenaClientConfig
    {
        public static Func<string, bool> Enabled = map => true;
        public static bool IsMapEnabledOnServer(string map) => Enabled(map);
    }
    internal static class ArenaLabyrinthAccess
    {
        public static bool IsLabyrinth(JsonType.LocationSettings.Location location) => location?.Id == ArenaMapCatalog.LabyrinthId;
        public static bool CanProject(EFT.RaidSettings owner, JsonType.LocationSettings.Location location) => false;
        public static JsonType.LocationSettings.Location Project(EFT.RaidSettings owner, JsonType.LocationSettings.Location location) => location;
    }
    // Renderer doubles expose arguments and owned callbacks, not visual layout.
    internal sealed class ArenaModeSelectionView
    {
        public static ArenaModeSelectionView Last;
        internal ArenaModeSelectionState State;
        internal bool Closed = true;
        public void Show(UnityEngine.Transform screen, TMPro.TMP_Text text, string mode, bool team, Func<string, bool> callback)
        { Last = this; Closed = false; State = new(mode, team, callback); State.CommitInitialSelection(); }
        public void Synchronize(string mode, bool team) => State?.Synchronize(mode, team);
        public void Close() { Closed = true; State = null; }
    }
    internal sealed class ArenaAreaSelectionView
    {
        public static ArenaAreaSelectionView Last;
        internal Func<string, bool> Callback;
        internal Func<string, int, bool> SizeCallback;
        internal Func<ArenaParticipantSelectionState, int, bool> ParticipantCallback;
        internal ArenaAreaSelectionState State;
        internal ArenaParticipantSelectionState Participants;
        internal bool Closed = true;
        internal bool Team;
        public void Show(UnityEngine.Transform screen, TMPro.TMP_Text text, Func<string, bool> callback,
            Func<string, int, bool> sizeCallback = null, Func<ArenaParticipantSelectionState, int, bool> participantCallback = null)
        { Last = this; Callback = callback; SizeCallback = sizeCallback; ParticipantCallback = participantCallback; Closed = false; }
        public void Refresh(ArenaAreaSelectionState state, bool team, ArenaParticipantSelectionState participants = null)
        { State = state; Team = team; Participants = participants; }
        public void Close() { Closed = true; Callback = null; SizeCallback = null; ParticipantCallback = null; State = null; Participants = null; }
    }
}
