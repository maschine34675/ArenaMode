using System.Reflection;
using EFT;
using EFT.Vaulting;

namespace UnityEngine
{
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
    }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); public static float Min(float a,float b)=>Math.Min(a,b); }
    public static class Time { public static float unscaledTime, deltaTime = .02f; }
}
namespace HarmonyLib
{
    public static class AccessTools { public static MethodInfo DeclaredMethod(Type type, string name) => type.GetMethod(name); }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { if(GetTargetMethod()==null) throw new Exception("missing target"); } }
    public class PatchPrefixAttribute : Attribute { }
    public class PatchPostfixAttribute : Attribute { }
    public class PatchFinalizerAttribute : Attribute { }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode
{
    internal static class Plugin { public static Logger ModLogger = new(); }
    internal sealed class Logger { public List<string> Info=new(); public bool ThrowInfo; public void LogInfo(object value) { if(ThrowInfo) throw new InvalidOperationException("logger"); Info.Add(value.ToString()); } public void LogDebug(object value) { } public void LogWarning(object value) { } }
}
namespace ArenaMode.Config
{
    internal sealed class Entry<T> { internal T Value; }
    internal static class Settings { internal static Entry<bool> AirVaultEnabled = new() { Value = true }; }
}
namespace ArenaMode.Movement
{
    // Scope itself is exercised by the root's production-linked checks. This
    // fixture deliberately controls its result; it is not live ownership proof.
    internal static class ArenaMovementScope
    {
        internal static Player Local;
        internal static bool Active = true;
        internal static bool IsLocal(Player p, bool requireInput = false) => Active && p != null && ReferenceEquals(Local,p)
            && p.Alive && !p.Ai && (!requireInput || !p.InputBlocked);
    }
}
namespace EFT
{
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public Player LocalPlayer; }
    public class BaseMovementState
    {
        public float NormalizedTime, AnimationAuthority, AuthoritySpeed; public int AnimatorStateHash, Name; public bool CanInteract;
        public int Enters, Exits, Moves; public string AnimatorStateName; public int AdditionalDirectionInfo, Type; public object SmoothLerpAnimCurve; public float RotationSpeedClamp, StateSensitivity; public bool DisableRootMotion; public void AssignAnimatorPose(object pose) { } public Action OnEnter;
        public virtual void Enter(bool same) { Enters++; OnEnter?.Invoke(); }
        public virtual void Exit(bool same) { Exits++; }
        public void ReEnter() { }
        public virtual void OnStateEnter(float nt) { }
        public virtual void OnStateMove(float nt) { }
        public virtual void OnStateExit(float nt) { }
    }
    public partial class MovementState : BaseMovementState
    { public MovementContext MovementContext; public virtual void Vaulting() { }
      public void ManualAnimatorMoveUpdate(float dt) { Moves++; } }
    public class JumpPlayerState : MovementState
    {
        public enum EJumpState { PushingFromTheGround, Jump, Bumbped }
        public EJumpState _jumpState = EJumpState.Jump;
    }
    public class FallDown : JumpPlayerState { }
    public partial class VaultingMovementState : MovementState
    {
        public float vaultingHeight, vaultingLength; public UnityEngine.Vector3? currentMotion;
        public override void Enter(bool same) { base.Enter(same); MovementContext.Vaulting=true;
            var component=(VaultingComponent)MovementContext._player.VaultingComponent;
            vaultingHeight=component.WrittenHeight; vaultingLength=component.WrittenLength; }
        public void ApplyMotion(ref UnityEngine.Vector3 motion,float dt)
        {
            var sample=Harness.MotionBefore(this,motion); Exception error=null;
            try { NativeApplyMotion(ref motion,dt); } catch(Exception ex) { error=ex; }
            error=Harness.MotionAfter(error,sample); if(error!=null)throw error;
        }
    }
    // Collision/Unity seam only: tests choose accepted displacement. Actual
    // native ApplyMotion is extracted, but collision/clip playback is not tested.
    public class MotionController
    {
        public MovementContext Context; public float height=1.6f;
        public UnityEngine.Vector3 AcceptedFraction=new(1,1,1); public Action OnMove;
        public void Move(UnityEngine.Vector3 motion,float dt)
        { Context.TransformPosition+=new UnityEngine.Vector3(motion.x*AcceptedFraction.x,motion.y*AcceptedFraction.y,motion.z*AcceptedFraction.z); OnMove?.Invoke(); }
    }
    public class ClimbState : VaultingMovementState { }
    public class VaultState : VaultingMovementState { }
    public class PlayerAnimator
    {
        public object Animator = new();
        public const int LANDING_PARAM_HASH=11, FALLING_DOWN_PARAM_HASH=12, JUMP_PARAM_HASH=13; public bool Climb, Vault;
        public bool GetDoClimb() => Climb;
        public bool GetDoVault() => Vault;
    }
    public partial class MovementContext
    {
        public MotionController CharacterController;
        public UnityEngine.Vector3 TransformPosition;
        public UnityEngine.Transform VaultingGridRoot=new();
        public UnityEngine.Vector3 PlayerRealForward=new(0,0,1);
        public Player _player;
        public BaseMovementState CurrentState, PreviousState, OverridenState, OverridenControlsState;
        public float LastDeltaTime=.02f; public Holder CanUseProp=new();
        public PlayerAnimator _playerAnimator=>PlayerAnimator;
        public Action<int,int> OnStateChanged;
        public bool IsSameStates(BaseMovementState a,BaseMovementState b)=>ReferenceEquals(a,b);
        public void UpdateCharacterControllerSpeedLimit() { }
        public void PlayerAnimatorEnableJump(bool enabled)=>JumpSet=enabled;
        public class Holder { public bool Value; }
        public bool IsGrounded, IsInPronePose, IsInMountedState, IsStationaryWeaponInHands, Vaulting, Transition, JumpSet = true, IsSprintEnabled;
        public int BlindFire, PoseToInt = 2;
        public UnityEngine.Vector3 MovementDirection = new(0,1,0);
        public PlayerAnimator PlayerAnimator = new();
        public bool PlayerAnimatorGetIsVaulting() => Vaulting;
        public bool IsAnimatorInTransitionState(int layer) => Transition;
        public bool PlayerAnimatorIsJumpSetted() => JumpSet;
        public void PlayerAnimatorSetDoClimb(bool doClimb) => PlayerAnimator.Climb=doClimb;
        public void PlayerAnimatorSetDoVault(bool doVault) => PlayerAnimator.Vault=doVault;
    }
    public class Inventory { public bool HasActiveEvents; }
    public interface IMedsController { }
    public class Meds : IMedsController { }
    public class FirearmsAnimator
    {
        public enum EGrenadeFire { None, Throw, Hold }
        public Animation Animator = new();
        public class Animation { public int Value; public int GetInteger(int key) => Value; }
    }
    public static class AnimationControllerParametersTable { public const int INT_GRENADEFIRE=0; }
    public partial class Player
    {
        public MovementContext MovementContext;
        public IVaultingComponent VaultingComponent;
        public IVaultingParameters VaultingParameters;
        public VaultingGameplayRestrictions VaultingGameplayRestrictions;
        public MovementState CurrentManagedState => (MovementContext.OverridenControlsState ?? MovementContext.CurrentState) as MovementState;
        public bool IsVaultingPressed=true, IsForwardInputDirection=true, Alive=true, Ai, InputBlocked;
        public Inventory InventoryController = new();
        public object HandsController;
        public int LeftHandRemoved, VaultNotifications, NativeToggleCalls;
        public Authority AuthorityBlender=new();
        public class Authority { public float Target, Speed; }
        public void RemoveLeftHandItem(float time) => LeftHandRemoved++;
        public virtual void OnVaulting() => VaultNotifications++;
        public float _vaultingTiming;
        public void ToggleVaulting() { NativeToggleCalls++; NativeToggleVaulting(); Harness.Held(this); }
        public class GrenadeHandsController { public FirearmsAnimator FirearmsAnimator = new(); }
        public class FirearmController
        {
            public HandsOperation CurrentHandsOperation;
            public class HandsOperation { }
            public class UtilityOperation : HandsOperation { }
            public class Idling : HandsOperation { }
            public class LauncherIdling : HandsOperation { }
            public class FireOperation : HandsOperation { }
            public class AutomaticFireOperation : HandsOperation { }
        }
    }
    public class GlobalConfiguration { public IVaultingSettings VaultingSettings; }
}
namespace EFT.Vaulting
{
    public enum EVaultingStrategy { None, Vault, Climb }
    public interface IVaultingGameplayRestrictions { bool CanVaulting(); }
    public interface IVaultingComponent { bool TryVaulting(); }
    public interface IVaultingParameters { float VaultingHeight { get; } EVaultingStrategy GetVaultingStrategy(); EVaultingStrategy GetAutoVaultingStrategy(); }
    public interface IVaultingSettings { bool IsActive { get; } IMovesSettings MovesSettings { get; } }
    public interface IMovesSettings { MoveSetting VaultSettings { get; } MoveSetting ClimbSettings { get; } }
    public class MoveSetting { public float MaxWithoutHandHeight=.5f; }
    public class SettingsModel : IVaultingSettings, IMovesSettings
    {
        public bool Active=true;
        public bool IsActive => Active;
        public IMovesSettings MovesSettings => this;
        public MoveSetting VaultSettings { get; } = new();
        public MoveSetting ClimbSettings { get; } = new();
    }
    public class Rule { public bool Allowed; public int Calls; public bool CanMove() { Calls++; return Allowed; } public bool CanMoveAutomaticly() => Allowed; }
    public class Moves { public Rule VaultState=new(), ClimbState=new(); }
    public class Obstacle { public bool IsTerrain; public float DistanceToMainObstacle=.4f; }
    public class Stairs { public bool Blocked; public bool IsStairsCondition()=>Blocked; }
    public class Automatic { public bool IsEnableAutoVault, IsEnableAutoClimb; }
    public class Model
    {
        public Obstacle ObstacleCalculatorModel=new();
        public Stairs StairsCalculatorModel=new();
        public Moves VaultingStatesModel=new();
        public Automatic AutomaticVaultingModel=new();
    }
    public partial class VaultingComponent : IVaultingParameters, IVaultingComponent
    {
        public MovementContext _vaultingContext;
        public IVaultingSettings _vaultingSettings;
        public Model _vaultingModelDebug=new();
        public Func<bool> _canVaultByStaminaGetter=()=>true, _canClimbByStaminaGetter=()=>true, _isAutoVaultingSettingEnable=()=>false;
        public float BehindObstacleRatio=-1;
        public float VaultingHeight { get; set; }=1.2f;
        public float VaultingLength=.5f, AbsoluteForwardVelocity=2;
        public UnityEngine.Vector3 MaxWeightPointPosition=new(4,2,1);
        public int Ticks, AnimatorParameters;
        public Action OnTick, OnParameters;
        public float WrittenHeight, WrittenLength;
        public void Tick() { Ticks++; OnTick?.Invoke(); }
        public bool IsVaulting()=>_vaultingContext.PlayerAnimatorGetIsVaulting();
        public void SetParametersInAnimator() { AnimatorParameters++; WrittenHeight=VaultingHeight; WrittenLength=VaultingLength; OnParameters?.Invoke(); }
        public EVaultingStrategy GetVaultingStrategy()
        {
            var result=NativeGetVaultingStrategy();
            return Harness.Strategy(this,result);
        }
        public bool DoVaultingByStrategy(EVaultingStrategy strategy)
        {
            bool result=false;
            if(Harness.CanCommit(this,strategy,ref result)) result=NativeDoVaultingByStrategy(strategy);
            Harness.Committed(this,strategy,result);
            return result;
        }
    }
}

internal static partial class Harness
{
    public static object MotionBefore(VaultingMovementState movement,UnityEngine.Vector3 motion)
    { object[] args={movement,motion,null}; Method("ObservedMotionPatch","Prefix").Invoke(null,args); return args[2]; }
    public static Exception MotionAfter(Exception error,object sample)
        =>(Exception)Method("ObservedMotionPatch","Finalizer").Invoke(null,new[]{error,sample});
    public static bool HasObservation=>typeof(ArenaMode.Movement.ArenaAirVault).GetField("_motionObservation",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)!=null;
    private static MethodInfo Method(string patch,string name)=>typeof(ArenaMode.Movement.ArenaAirVault)
        .GetNestedType(patch,BindingFlags.NonPublic).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
    public static void Held(Player p)=>Method("HeldVaultPatch","Postfix").Invoke(null,new object[]{p});
    public static EVaultingStrategy Strategy(VaultingComponent c,EVaultingStrategy result)
    { object[] args={c,result}; Method("StrategyPatch","Postfix").Invoke(null,args); return (EVaultingStrategy)args[1]; }
    public static bool CanCommit(VaultingComponent c,EVaultingStrategy s,ref bool result)
    { object[] args={c,s,result}; bool allow=(bool)Method("CommitPatch","Prefix").Invoke(null,args); result=(bool)args[2]; return allow; }
    public static void Committed(VaultingComponent c,EVaultingStrategy s,bool result)=>Method("CommitPatch","Postfix").Invoke(null,new object[]{c,s,result});
}
