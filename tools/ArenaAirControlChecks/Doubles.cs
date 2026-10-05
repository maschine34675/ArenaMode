// Only native inputs, transforms, collision observations and detour registration.
// All eligibility, steering, flight lifetime and scope decisions come from production.
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public readonly Dictionary<Type, Component> Components = new();
        public T GetComponent<T>() where T : Component => Components.GetValueOrDefault(typeof(T)) as T;
    }
    public struct Vector2(float x, float y)
    {
        public float x = x, y = y;
        public float sqrMagnitude => x * x + y * y;
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
    }
    public static class Time { public static int frameCount; }
}

namespace EFT
{
    public static class NativeIo { public static readonly List<string> Calls = new(); }
    public enum GameStatus { Starting, Started, Stopping, Stopped }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Started; public Player LocalPlayer; }
    public class GameWorld : UnityEngine.Object { public Player MainPlayer; }
    public class HealthController { public bool IsAlive = true; }
    public class CharacterController { public bool isGrounded; }
    public class Player : UnityEngine.Component
    {
        public bool IsAI, IsYourPlayer = true, IsInventoryOpened;
        public HealthController HealthController = new();
        public CharacterController CharacterController = new();
        public MovementContext MovementContext;
        private MovementState _currentManagedState;
        public Exception StateReadFailure;
        public int StateReads;
        public MovementState CurrentManagedState
        {
            get { StateReads++; if (StateReadFailure != null) throw StateReadFailure; return _currentManagedState; }
            set => _currentManagedState = value;
        }
        public void Move(UnityEngine.Vector2 input) => NativeIo.Calls.Add("Move");
    }
    public class MovementContext
    {
        public bool IsInPronePose, IsInMountedState, IsStationaryWeaponInHands, IsAxesIgnored, IsGrounded, Vaulting;
        public int BlindFire;
        public MovementState CurrentState, OverridenState, OverridenControlsState;
        public double TransformYawDegrees;
        public UnityEngine.Vector3? TransformOverride;
        public Exception TransformFailure;
        public int TransformCalls;
        public UnityEngine.Vector3 LastTransformInput;
        public bool PlayerAnimatorGetIsVaulting() => Vaulting;
        public UnityEngine.Vector3 TransformVector(UnityEngine.Vector3 input)
        {
            TransformCalls++;
            LastTransformInput = input;
            if (TransformFailure != null) throw TransformFailure;
            if (TransformOverride.HasValue) return TransformOverride.Value;
            double yaw = TransformYawDegrees * Math.PI / 180;
            return new((float)(input.x * Math.Cos(yaw) + input.z * Math.Sin(yaw)), input.y,
                (float)(input.z * Math.Cos(yaw) - input.x * Math.Sin(yaw)));
        }
        public void CheckFlying() => NativeIo.Calls.Add("CheckFlying");
    }
    public class MovementState(MovementContext context)
    {
        public MovementContext MovementContext = context;
        public int LimitCalls;
        public UnityEngine.Vector3 LastLimitInput;
        public float LastLimitDt;
        public float CollisionX = 1, CollisionY = 1, CollisionZ = 1;
        public Exception LimitFailure;
        public void LimitMotion(ref UnityEngine.Vector3 motion, float dt)
        {
            NativeIo.Calls.Add("LimitMotion");
            LimitCalls++;
            LastLimitInput = motion;
            LastLimitDt = dt;
            if (LimitFailure != null) throw LimitFailure;
            motion.x *= CollisionX; motion.y *= CollisionY; motion.z *= CollisionZ;
        }
    }
    public class JumpPlayerState(MovementContext context) : MovementState(context)
    {
        public enum EJumpState { PushingFromTheGround, Jump, Bumbped }
        public EJumpState _jumpState = EJumpState.Jump;
        public UnityEngine.Vector3 _currentLiftforce;
        public void ProcessJumpMovement(float dt) => NativeIo.Calls.Add("ProcessJumpMovement");
    }
    // Native FallDown inherits JumpPlayerState and uses its ProcessJumpMovement.
    public class FallDown(MovementContext context) : JumpPlayerState(context) { }
    public class IdlePlayerState(MovementContext context) : MovementState(context) { }
    public class ForeignJumpState(MovementContext context) : JumpPlayerState(context) { }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace ArenaMode.Config
{
    public class Entry<T>(T value) { public T Value = value; }
    public static class Settings
    {
        public static Entry<bool> MovementEnabled = new(true), AirControlEnabled = new(true);
        public static Entry<float> AirControlDegreesPerSecond = new(60);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && game != null && world != null
                && ReferenceEquals(game, ActiveGame) && ReferenceEquals(world, ActiveWorld);
    }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Movement
{
    internal static class ArenaBunnyhop { internal static void ApplyCarry(EFT.MovementState s, ref UnityEngine.Vector3 m, float dt) { } }
    public static class ArenaAirVault { public static bool Pending; public static bool HasPendingRequest(EFT.Player player) => Pending; }
    public class ArenaSlideController : UnityEngine.Component { public bool IsSliding; }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "air-control-checks"; public static Logger ModLogger = new(); }
    public class Logger
    {
        public readonly List<string> Lines = new();
        public void LogInfo(object value) => Lines.Add(value.ToString());
        public void LogWarning(object value) => Lines.Add(value.ToString());
    }
}

namespace HarmonyLib
{
    public readonly record struct ExceptionBlock(string Kind);
    public class CodeInstruction(OpCode opcode, object operand = null)
    {
        public OpCode opcode = opcode;
        public object operand = operand;
        public readonly List<Label> labels = new();
        public readonly List<ExceptionBlock> blocks = new();
    }
    public class HarmonyMethod(MethodInfo method) { public MethodInfo Method = method; }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }
    public class Harmony(string id)
    {
        public record Entry(string Id, HarmonyMethod Prefix, HarmonyMethod Postfix, HarmonyMethod Transpiler);
        public static readonly Dictionary<MethodInfo, Entry> Hooks = new();
        public static int FailAt, LimitCallCount = 1;
        public static bool WrongLimitOpcode, FailUnpatch;
        public static List<CodeInstruction> LastCode;
        public static CodeInstruction OriginalLimitNode;
        public static Label CollisionLabel;
        public static ExceptionBlock CollisionBlock = new("native collision exception block");

        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
        {
            if (FailAt > 0 && --FailAt == 0) throw new InvalidOperationException("detour rejected");
            if (original == null) throw new MissingMethodException("native target absent");
            if (transpiler != null)
            {
                var limit = AccessTools.DeclaredMethod(typeof(EFT.MovementState), nameof(EFT.MovementState.LimitMotion));
                var instructions = new List<CodeInstruction> { new(OpCodes.Ldarg_0), new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2) };
                CollisionLabel = new DynamicMethod("label", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
                OriginalLimitNode = null;
                for (int i = 0; i < LimitCallCount; i++)
                {
                    var instruction = new CodeInstruction(WrongLimitOpcode ? OpCodes.Call : OpCodes.Callvirt, limit);
                    instruction.labels.Add(CollisionLabel); instruction.blocks.Add(CollisionBlock);
                    OriginalLimitNode ??= instruction;
                    instructions.Add(instruction);
                }
                instructions.Add(new(OpCodes.Ret));
                LastCode = ((IEnumerable<CodeInstruction>)transpiler.Method.Invoke(null, [instructions])).ToList();
            }
            Hooks[original] = new(id, prefix, postfix, transpiler);
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("unpatch rejected");
            foreach (var target in Hooks.Where(x => x.Value.Id == id).Select(x => x.Key).ToArray()) Hooks.Remove(target);
        }
    }
}
