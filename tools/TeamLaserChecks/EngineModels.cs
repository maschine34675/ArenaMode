// Deliberately small modeled engine boundary. Production policy and render/restore
// code are linked unchanged; this is not a Unity renderer or a Harmony runtime test.
using System.Reflection;

namespace UnityEngine
{
    public class Object { public string name; public bool Destroyed; public static bool operator ==(Object x, Object y) => (ReferenceEquals(x, null) || x.Destroyed) ? ReferenceEquals(y, null) || y.Destroyed : ReferenceEquals(x, y); public static bool operator !=(Object x, Object y) => !(x == y); public override bool Equals(object obj) => ReferenceEquals(this, obj); public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    public class GameObject : Object
    {
        public GameObject Parent;
        public List<Component> Components = new();
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponentInParent<T>() where T : Component { for (var node = gameObject; node != null; node = node.Parent) { var c = node.GetComponent<T>(); if (c != null) return c; } return null; }
    }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
    public readonly record struct Color(float r, float g, float b, float a = 1f);
    public class Shader : Object { public static int PropertyToID(string name) => name switch { "_Color" => 1, "_Factor" => 2, "_Distance" => 3, "_Intensity" => 4, "_Size" => 5, "_MaxDist" => 6, _ => 20 }; }
    public class Material : Object
    {
        public Shader shader;
        public Dictionary<int, Color> Colors = new();
        public Dictionary<int, float> Floats = new();
        public bool HasProperty(int id) => Colors.ContainsKey(id) || Floats.ContainsKey(id);
        public Color GetColor(int id) => Colors[id];
        public float GetFloat(int id) => Floats[id];
    }
    public class MaterialPropertyBlock
    {
        public static int Created;
        public readonly Dictionary<int, Color> Colors = new();
        public readonly Dictionary<int, float> Floats = new();
        public MaterialPropertyBlock() { Created++; }
        public void Clear() { Colors.Clear(); Floats.Clear(); }
        public void SetColor(int id, Color value) => Colors[id] = value;
        public void SetFloat(int id, float value) => Floats[id] = value;
    }
    public class Light : Component { public Color color; public float intensity = 3f; public bool enabled = true; }
    public static class Time { public static float unscaledTime; }
}

public class LaserBeam : UnityEngine.MonoBehaviour
{
    public UnityEngine.Material BeamMaterial, PointMaterial;
    private UnityEngine.MaterialPropertyBlock _beamBlock = new(), _pointBlock = new();
    private UnityEngine.Light _light = new();
    public UnityEngine.MaterialPropertyBlock NativeBeam => _beamBlock;
    public UnityEngine.MaterialPropertyBlock NativePoint => _pointBlock;
    public UnityEngine.Light NativeLight => _light;
    public float IntensityFactor = 4f, LightIntensity = 8f, BeamSize = 0.004f;
    public UnityEngine.Color DrawnBeam, DrawnPoint;
    public void LateUpdate()
    {
        _beamBlock.SetFloat(3, 20f); _beamBlock.SetFloat(4, IntensityFactor);
        _pointBlock.SetFloat(4, IntensityFactor); _pointBlock.SetFloat(5, 0.07f); _pointBlock.SetFloat(6, 100f);
        DrawnBeam = _beamBlock.Colors.TryGetValue(1, out var b) ? b : BeamMaterial.GetColor(1);
        DrawnPoint = _pointBlock.Colors.TryGetValue(1, out var p) ? p : PointMaterial.GetColor(1);
    }
    public void ReplaceBlocks(UnityEngine.MaterialPropertyBlock beam, UnityEngine.MaterialPropertyBlock point) { _beamBlock = beam; _pointBlock = point; }
    public void ReplaceLight(UnityEngine.Light light) => _light = light;
}
public class TacticalComboVisualController : UnityEngine.MonoBehaviour { public EFT.InventoryLogic.LightComponent LightMod = new(); }
public class WeaponPrefab : UnityEngine.MonoBehaviour { private EFT.IPlayer _player; public void Init(EFT.IPlayer player) => _player = player; }
namespace EFT.InventoryLogic { public class LightComponent { public bool IsActive = true; public int SelectedMode; } }
namespace EFT
{
    public interface IPlayer { }
    public class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public object HandsController;
        public class FirearmController { public UnityEngine.GameObject ControllerGameObject; }
    }
    public class GameWorld { }
    public class AbstractGame { }
}
namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive = true, Exact = true;
        public static string ActiveMode = "teamdeathmatch";
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => Exact;
    }
}
namespace ArenaMode.Config
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public static class Settings
    {
        public static Setting<bool> TeamLasers = new(true);
        public static Setting<ArenaMode.Effects.ArenaTracerColor> FriendlyTracerColor = new(ArenaMode.Effects.ArenaTracerColor.Cyan), OpponentTracerColor = new(ArenaMode.Effects.ArenaTracerColor.Red);
    }
}
namespace ArenaMode.Modes
{
    public static class ArenaMatchService { public static ArenaMatch Current = new(); }
    public class ArenaMatch { public object Mode = new TeamDeathmatchMode(); }
    public class TeamDeathmatchMode
    {
        public readonly Dictionary<EFT.Player, bool> CurrentBodies = new();
        public bool TryClassifyCurrentBody(EFT.Player player, out bool friendly) { friendly = false; return player != null && CurrentBodies.TryGetValue(player, out friendly); }
    }
}
namespace ArenaMode { public static class Plugin { public static Logger ModLogger = new(); public class Logger { public void LogWarning(string message) { } } } }
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } }
    public class PatchPrefixAttribute : Attribute { }
    public class PatchFinalizerAttribute : Attribute { }
}
