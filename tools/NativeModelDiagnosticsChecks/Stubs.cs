using System.Reflection;
using System.Reflection.Emit;

namespace UnityEngine
{
    public class Component
    {
        public static int HierarchyReads;
        public bool FailHierarchy;
        public bool Destroyed;
        public string name = "fixture";
        private static int _next;
        private readonly int _id = ++_next;
        private GameObject _go;
        private Transform _transform;
        public Component ParentComponent;
        public GameObject gameObject
        {
            get { HierarchyReads++; if (FailHierarchy) throw new InvalidOperationException("fixture read failure"); return _go ??= new(); }
        }
        public Transform transform => this is Transform t ? t : _transform ??= new();
        public int GetInstanceID() => _id;
        public static bool operator ==(Component left, Component right)
            => (ReferenceEquals(left, null) || left.Destroyed) && (ReferenceEquals(right, null) || right.Destroyed)
                || ReferenceEquals(left, right);
        public static bool operator !=(Component left, Component right) => !(left == right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => _id;
        public T GetComponentInParent<T>() where T : class
            => this as T ?? ParentComponent?.GetComponentInParent<T>();
    }
    public sealed class Transform : Component { public Transform parent; }
    public sealed class GameObject
    {
        public bool activeSelf = true, activeInHierarchy = true;
        public Scene scene = new();
    }
    public sealed class Scene { public string name = "factory4_day"; }
}
namespace EFT.AssetsManager
{
    public class AssetPoolObject : UnityEngine.Component { public bool IsInPool, IsStub; }
}
namespace EFT.InventoryLogic { public sealed class Weapon { public string TemplateId = "weapon-template"; } }
namespace Diz.Skinning { public sealed class Skeleton { } }
namespace EFT
{
    public interface IPlayer { }
    public class AbstractGame { }
    public sealed class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Started; }
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public sealed class GameWorld
    {
        public string LocationId = "factory4_day";
        public Player MainPlayer;
        public Player RegisteredPlayer;
        public Player GetEverExistedPlayerByID(string id) => RegisteredPlayer;
    }
    public sealed class PlayerBody : UnityEngine.Component
    {
        private bool _active = true;
        #pragma warning disable CS0414 // Production reflection intentionally reads this native-shaped private field.
        private string _playerProfileID = "body-fixture-profile";
        #pragma warning restore CS0414
        public StringState BodyCustomizationId = new() { Value = "fixture-body-template" };
        public int OriginalErrors;
        public void Dispose() => _active = false;
        public void OnDestroy() { if (_active) OriginalErrors++; }
    }
    public sealed class StringState { public string Value; }
    public class Player : UnityEngine.Component, IPlayer
    {
        public PlayerBody PlayerBody;
        public bool IsAI;
        public enum EOperationState { Ready, Executing, Finished }
        public class FirearmController
        {
            public class FirearmOperation
            {
                public EOperationState State;
                public bool _inInventory;
                public float _utilityTime;
                public string _utilityOperationType = "CheckFireMode";
                public Player Player;
                public InventoryLogic.Weapon Weapon;
            }
            public class UtilityOperation : FirearmOperation
            {
                public void Update(float deltaTime) { }
            }
            public class LauncherUtilityOperation : FirearmOperation { public void Update(float deltaTime) { } }
        }
    }
}
public sealed class MuzzleManager : UnityEngine.Component { public object JetMaterial; }
public sealed class FirearmsEffects : UnityEngine.Component
{
    private MuzzleManager _muzzleManager;
    public void SetMuzzle(MuzzleManager value) => _muzzleManager = value;
    public void Init(UnityEngine.Transform hierarchy) { }
}
public sealed class WeaponPrefab : EFT.AssetsManager.AssetPoolObject
{
    private EFT.InventoryLogic.Weapon _weaponData = new();
    public UnityEngine.Transform Parent(EFT.IPlayer player) => transform;
}
public enum EClippingCustoms { None, Hat }
public sealed class CustomItem : UnityEngine.Component
{
    public EClippingCustoms _itemType;
    public static readonly Dictionary<EClippingCustoms, int> InterestSlots = new() { [EClippingCustoms.Hat] = 1 };
    public void Init(EFT.PlayerBody playerBody) { }
}
public sealed class CustomItemSkin : UnityEngine.Component
{
    public EClippingCustoms _itemType;
    public void Init(Diz.Skinning.Skeleton skeleton, EFT.PlayerBody playerBody) { }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace ArenaMode
{
    internal static class Plugin
    {
        internal const string PluginGuid = "diagnostic-fixture";
        internal static readonly Logger ModLogger = new();
    }
    internal sealed class Logger
    {
        internal List<string> Warnings = new();
        internal void LogWarning(string message) => Warnings.Add(message);
    }
}
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled;
        internal static List<string> Lines = new();
        internal static void Developer(string message) { if (DeveloperEnabled) Lines.Add(message); }
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive, ArmedForNextRaid;
        internal static int ArmEpoch;
        internal static string ActiveMode = "deathmatch", ArmedMode = "tactical", ArmedMap;
        internal static EFT.AbstractGame ExactGame;
        internal static EFT.GameWorld ExactWorld;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
    internal static class ArenaServerClient
    {
        internal static bool HasPendingArm;
        internal static string PendingArmToken;
    }
}
namespace HarmonyLib
{
    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public List<Label> labels = new();
        public List<object> blocks = new();
        public CodeInstruction(OpCode opcode, object operand = null) { this.opcode = opcode; this.operand = operand; }
    }
    public static class Priority { public const int Last = 0; }
    public sealed class HarmonyMethod
    {
        public MethodInfo method;
        public int priority;
        public HarmonyMethod(MethodInfo method) => this.method = method;
    }
    public sealed class Harmony
    {
        public static readonly List<(MethodInfo Target, string Kind, MethodInfo Hook)> Hooks = new();
        public Harmony(string id) { }
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null, HarmonyMethod transpiler = null, HarmonyMethod ilmanipulator = null)
        {
            var hook = prefix ?? postfix ?? finalizer ?? transpiler;
            Hooks.Add((target, prefix != null ? "prefix" : postfix != null ? "postfix" : finalizer != null ? "finalizer" : "transpiler", hook.method));
        }
        public void UnpatchSelf() => Hooks.Clear();
    }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static FieldInfo Field(Type type, string name) => type.GetField(name, Flags);
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] arguments = null)
            => arguments == null ? type.GetMethod(name, Flags | BindingFlags.DeclaredOnly)
                : type.GetMethod(name, Flags | BindingFlags.DeclaredOnly, null, arguments, null);
    }
}
