using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ArenaMode.Config
{
    public sealed class Setting<T>(T value) { public T Value = value; }
    public static class Settings
    {
        public static Setting<bool> MovementEnabled = new(true), AirHipFireEnabled = new(true), SprintToAds = new(true);
    }
}
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode.Movement
{
    public static class ArenaAirVault
    {
        public static bool Pending;
        public static bool HasPendingRequest(EFT.Player player) => Pending;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public sealed class Logger { public List<string> Warnings = new(); public void LogWarning(object value) => Warnings.Add(value.ToString()); }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Started, Stopping, Stopped }
    public enum EPlayerState { Idle, Jump, FallDown, Vaulting, Transition, Sprint }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameStatus Status = GameStatus.Started; }
    public class GameWorld { public Player MainPlayer; }
    public class Health { public bool IsAlive = true; }
    public class MovementContext
    {
        public bool IsGrounded, IsInPronePose, IsInMountedState, BlockFirearms, Vaulting, IsSprintEnabled, AnimatorSprint;
        public int SprintWrites, AnimatorWrites;
        public void EnableSprint(bool value) { IsSprintEnabled=value; SprintWrites++; }
        public void PlayerAnimatorEnableSprint(bool value) { AnimatorSprint=value; AnimatorWrites++; }
        public bool PlayerAnimatorGetIsVaulting() => Vaulting;
    }
    public class MovementState { public EPlayerState Name; }
    public class JumpPlayerState(MovementContext context) : MovementState
    {
        public enum EJumpState { PushingFromTheGround, Jump, Bumbped }
        public MovementContext MovementContext = context;
        public EJumpState _jumpState = EJumpState.Jump;
    }
    public class DerivedJump(MovementContext context) : JumpPlayerState(context) { }
    public static class PlayerAnimator { public static int WEAPON_ROOT_3RD = 1672138505, OTHER_CURVE = 1; }
    public class BodyAnimator
    {
        public int CurrentHash=1332272753, NextHash;
        public bool Transition;
        public record struct State(int fullPathHash);
        public State GetCurrentAnimatorStateInfo(int layer) => new(CurrentHash);
        public State GetNextAnimatorStateInfo(int layer) => new(NextHash);
        public bool IsInTransition(int layer) => Transition;
    }
    public class Player
    {
        public bool IsYourPlayer = true, IsAI, IsInventoryOpened, FirstPersonPointOfView = true;
        public float CurveValue = 1;
        public int CurveReads;
        public BodyAnimator BodyAnimatorCommon=new();
        public float GetCurveValue(int hash) { CurveReads++; return CurveValue; }
        public void VisualPass() { }
        public Health HealthController = new();
        public MovementContext MovementContext = new();
        public MovementState CurrentManagedState, CurrentState;
        public ItemHandsController HandsController;
        public class ItemHandsController
        {
            public Player _player;
            public bool NativeSuitable;
            public int Reads;
            public bool SuitableForHandInput { get { Reads++; return NativeSuitable; } }
        }
        public class FirearmController : ItemHandsController
        {
            public bool IsAiming { get; set; }
            public bool Blindfire, Interaction;
            public float _overlapValue;
            public EFT.InventoryLogic.Weapon Item = new();
            public FirearmsAnimator FirearmsAnimator = new();
            public FirearmOperation CurrentOperation;
            public bool IsInInteractionStrictCheck() => Interaction;
            public void ToggleAim() { }
            public void StateChangedHandler(EPlayerState previousstate,EPlayerState nextstate) { }
            public class FirearmOperation { public object Controller; }
            public class Idling : FirearmOperation { public float _idlingTime=5; public void SetTriggerPressed(bool value) { } public void SetAiming(bool value) { } }
            public class FireOperationBase : FirearmOperation { }
            public class LauncherFire : FireOperationBase { }
            public class BaseReloadOperation : FirearmOperation { }
        }
    }
}
namespace EFT.InventoryLogic
{
    public class Weapon { public enum EReloadMode { External, OnlyBarrel }; public EReloadMode ReloadMode; public bool IsBeltMachineGun; }
    public class RocketLauncher : Weapon { }
}
public sealed class FirearmsAnimator { public bool Idling=true; public bool IsIdling() => Idling; }
public sealed class EFTHardSettings { public static EFTHardSettings Instance=new(); public float STOP_AIMING_AT=0.5f; }
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchTranspilerAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchFinalizerAttribute : Attribute { }
    public abstract class ModulePatch
    {
        public static List<MethodBase> Hooks = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { var method = GetTargetMethod(); if (method == null) throw new Exception("missing hook"); Hooks.Add(method); }
    }
}
// Host-safe reflection/IL carriers only: the Unity/Mono Harmony runtime itself
// cannot initialize its reflection backend in this net10 console process.
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T,F>(T instance);
        public static FieldRef<T,F> FieldRefAccess<T,F>(string name)
        {
            if (!typeof(EFT.Player.ItemHandsController).IsAssignableFrom(typeof(T)) || typeof(F)!=typeof(EFT.Player) || name!="_player")
                throw new Exception("unexpected native owner field");
            return (T instance) => ref Unsafe.As<EFT.Player,F>(ref ((EFT.Player.ItemHandsController)(object)instance)._player);
        }
        public static MethodInfo DeclaredMethod(Type type,string name,Type[] args)
            => type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly,null,args,null);
        public static MethodInfo Method(Type type,string name)
            => type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
        public static MethodInfo PropertyGetter(Type type,string name)
            => type.GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)?.GetMethod;
        public static MethodInfo PropertySetter(Type type,string name)
            => type.GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)?.SetMethod;
        public static FieldInfo Field(Type type,string name)
            => type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
    }
    public sealed class CodeInstruction
    {
        public OpCode opcode; public object operand; public List<Label> labels=new();
        public CodeInstruction(OpCode code,object value=null) { opcode=code; operand=value; }
        public CodeInstruction(CodeInstruction other) { opcode=other.opcode; operand=other.operand; labels=new(other.labels); }
        public bool Calls(MethodInfo method) => (opcode==OpCodes.Call || opcode==OpCodes.Callvirt) && Equals(operand,method);
    }
}
