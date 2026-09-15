// Native state and external admission boundaries only. Production owns all bot
// eligibility, motion scaling, corner damping, leases and exception cleanup.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector3 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }
    public static class Time { public static float timeScale = 1; }
}

public interface ICharacterController { float SpeedLimit { get; set; } }
public class Controller : ICharacterController
{
    public float Value = 4;
    public int Writes, ThrowBeforeWrites, ThrowAfterWrites;
    public float SpeedLimit
    {
        get => Value;
        set
        {
            Writes++;
            if (ThrowBeforeWrites-- > 0) throw new InvalidOperationException("controller write rejected");
            Value = value;
            if (ThrowAfterWrites-- > 0) throw new InvalidOperationException("controller wrote then failed");
        }
    }
}
public enum EBotMoverState { None, NearDoor }
public class BotMover
{
    public bool Pause, HasPathAndNoComplete;
    public EBotMoverState CurrentState;
    public UnityEngine.Vector3 CurrentCornerPoint = new(10, 0, 0);
}
public class BotDoorOpener { public bool Interacting, NearDoor; }

namespace EFT
{
    public enum GameStatus { Starting, Started, Stopping, Stopped }
    public enum EBotState { PreActive, Active, NonActive, Dead }
    public enum EPlayerState { None, Idle, Run, Sprint, Jump, FallDown, Transition, ProneMove, Pickup, Vaulting }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Ever = new(), Alive = new();
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
    }
    public class HealthController { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : UnityEngine.Object
    {
        public bool IsAI = true, IsYourPlayer;
        public string ProfileId = "bot";
        public HealthController HealthController = new();
        public MovementContext MovementContext;
        public ICharacterController CharacterController = new Controller();
        public AIData AIData = new();
        public UnityEngine.Vector3 Position;
    }
    public class BotOwner : UnityEngine.Object
    {
        public EBotState BotState = EBotState.Active;
        public Player GetPlayer;
        public BotDoorOpener DoorOpener = new();
        public BotMover Mover = new();
    }
    public class BaseMovementState { public EPlayerState Name = EPlayerState.Run; }
    public class MovementContext
    {
        public Player _player;
        public BaseMovementState CurrentState = new();
        public bool IsGrounded = true, IsInPronePose, IsInMountedState;
        public UnityEngine.Vector3 PlatformMotion;
        public object StationaryWeapon;
        public ICharacterController CharacterController;
        public int NativeCalls;
        public bool FailNative;
        public UnityEngine.Vector3 NativeMotion;
        public void DirectApplyMotion(UnityEngine.Vector3 motion, float dt)
        { NativeCalls++; NativeMotion = motion; if (FailNative) throw new InvalidOperationException("native collision failed"); }
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Config
{
    public class Entry<T>(T value) { public T Value = value; }
    public static class Settings
    {
        public static Entry<bool> BotMovementEnabled = new(true);
        public static Entry<float> BotWalkSpeedMult = new(1.25f), BotSprintSpeedMult = new(1.3f);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive = true;
        public static EFT.GameWorld World;
        public static EFT.AbstractGame Game;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { bool IsComplete { get; } }
    public class Mode : IArenaMode { public bool Complete; public bool IsComplete => Complete; public bool AssistScoringOpen = true; }
    public class RoundsMode : Mode { }
    public class TacticalMode : Mode { }
    public class TeamDeathmatchMode : Mode { }
    public class GunGameMode : Mode { }
    public class ArenaMatch { public IArenaMode Mode = new RoundsMode(); }
    public static class ArenaMatchService { public static ArenaMatch Current = new(); public static bool TeamStartAbortPending; }
}
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static bool IsPending; } }
namespace ArenaMode.Movement
{
    public static class ArenaBotMovementSain
    {
        public static bool Allowed = true, Throw;
        public static void Enable() { }
        public static bool AllowsBoost(EFT.BotOwner owner, EFT.Player player)
        { if (Throw) throw new InvalidOperationException("SAIN boundary failed"); return Allowed; }
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger { public readonly List<string> Lines = new(); public void LogInfo(object x) => Lines.Add(x.ToString()); public void LogWarning(object x) => Lines.Add(x.ToString()); }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority(int priority) : Attribute { public int Priority = priority; }
    public static class Priority { public const int Last = 0; }
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            if (typeof(T) != typeof(EFT.MovementContext) || typeof(F) != typeof(EFT.Player) || name != "_player") throw new MissingFieldException(name);
            return (T instance) => ref Unsafe.As<EFT.Player, F>(ref ((EFT.MovementContext)(object)instance)._player);
        }
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class PatchFinalizer : Attribute { }
    public abstract class ModulePatch
    {
        public static MethodBase Target;
        public static MethodInfo PrefixHook, FinalizerHook;
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            Target = GetTargetMethod();
            PrefixHook = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(m => m.IsDefined(typeof(PatchPrefix)));
            FinalizerHook = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(m => m.IsDefined(typeof(PatchFinalizer)));
        }
    }
}
