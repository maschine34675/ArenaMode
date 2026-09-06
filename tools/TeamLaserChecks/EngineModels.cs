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
        public void SetFloat(int id, float value) => Floats[id] = value;
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
        public float GetFloat(int id) => Floats.TryGetValue(id, out var value) ? value : 0f;
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
    public float DrawnIntensity;
    public float DrawnBeamFactor, DrawnPointFactor;
    public bool Hit = true, FailBeforeLight, FailAfterLight;
    public void Awake() { }
    public void LateUpdate()
    {
        if (FailBeforeLight) throw new InvalidOperationException("before native light");
        _beamBlock.SetFloat(3, 20f); _beamBlock.SetFloat(4, IntensityFactor);
        _pointBlock.SetFloat(4, IntensityFactor); _pointBlock.SetFloat(5, 0.07f); _pointBlock.SetFloat(6, 100f);
        _light.intensity = Hit ? IntensityFactor * LightIntensity : 0f;
        if (FailAfterLight) throw new InvalidOperationException("after native light");
        DrawnIntensity = IntensityFactor;
        DrawnBeamFactor = _beamBlock.Floats.TryGetValue(2, out var beamFactor) ? beamFactor : BeamMaterial.GetFloat(2);
        DrawnPointFactor = _pointBlock.Floats.TryGetValue(2, out var pointFactor) ? pointFactor : PointMaterial.GetFloat(2);
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
        public string ProfileId = Guid.NewGuid().ToString();
        public bool IsAI;
        public Health HealthController = new();
        public AIData AIData = new();
        public object HandsController;
        public class FirearmController { public UnityEngine.GameObject ControllerGameObject; }
    }
    public class Health { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public enum EBotState { Active, Inactive }
    public class BotOwner : UnityEngine.Object { public Player GetPlayer; public string ProfileId; public EBotState BotState; public bool IsDead; public Health HealthController = new(); }
    public class GameWorld
    {
        public Player MainPlayer;
        public Dictionary<string, Player> Alive = new(), Ever = new();
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
    }
    public enum GameStatus { Started, Stopped }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class LocalGame : AbstractGame { }
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
        public static Setting<float> VisibleLaserBrightness = new(1f);
        public static Setting<float> VisibleLaserBeamBrightness = new(1f);
        public static Setting<ArenaMode.Effects.ArenaTracerColor> FriendlyTracerColor = new(ArenaMode.Effects.ArenaTracerColor.Cyan), OpponentTracerColor = new(ArenaMode.Effects.ArenaTracerColor.Red);
    }
}
namespace ArenaMode.Modes
{
    public static class ArenaMatchService { public static ArenaMatch Current = new(); }
    public class ArenaMatch { public ModeModel Mode = new TeamDeathmatchMode(); }
    public class ModeModel { public string Id = "endless"; public bool IsComplete; }
    public class TeamDeathmatchMode : ModeModel
    {
        public TeamDeathmatchMode() { Id = "teamdeathmatch"; }
        public readonly Dictionary<EFT.Player, bool> CurrentBodies = new();
        public bool TryClassifyCurrentBody(EFT.Player player, out bool friendly) { friendly = false; return player != null && CurrentBodies.TryGetValue(player, out friendly); }
    }
    // Only state storage is modeled; production TacticalMode.Lasers.cs supplies
    // the actual round/body admission logic compiled into this replay.
    public sealed partial class TacticalMode : ModeModel
    {
        internal enum Phase { Live, Staging, Aftermath, Rebinding }
        internal bool _teamArena = true;
        public bool IsTeamArena => _teamArena;
        internal Phase _phase = Phase.Live;
        internal int _teamRoundEpoch = 1;
        internal const int TeamCompanionCount = 4, TeamEnemyCount = 5;
        internal ArenaMatch _match;
        internal EFT.LocalGame _teamGame;
        internal EFT.Player _boundLocalPlayer;
        internal string _boundLocalProfileId;
        internal EFT.BotOwner[] _boundTeamFollowers = new EFT.BotOwner[4], _boundTeamEnemies = new EFT.BotOwner[5];
        internal string[] _boundTeamFollowerProfileIds = new string[4], _boundTeamEnemyProfileIds = new string[5];
        public TacticalMode() { Id = "tactical"; }
    }
}
namespace ArenaMode.Compat { public static class PitFireTeamCompat { public static bool TeamArenaActive = true; } }
namespace ArenaMode { public static class Plugin { public static Logger ModLogger = new(); public class Logger { public void LogWarning(string message) { } } } }
namespace HarmonyLib
{
    public static class Priority { public const int First = 800, Last = 0; }
    public class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
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
    public class PatchPostfixAttribute : Attribute { }
    public class PatchFinalizerAttribute : Attribute { }
}
