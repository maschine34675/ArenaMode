using System.Reflection;

// Contract doubles. No Unity physics, animator graph or Mono/Harmony dispatch is simulated.
namespace UnityEngine
{
    public class MonoBehaviour { public bool enabled = true; }
    public struct Vector3(float x, float y, float z)
    {
        public float x=x,y=y,z=z;
        public static Vector3 zero => new(0,0,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude>0?this/magnitude:zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object o)=>o is Vector3 v&&this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
        public static Vector3 ClampMagnitude(Vector3 a,float max)=>a.magnitude>max?a.normalized*max:a;
        public static Vector3 MoveTowards(Vector3 a,Vector3 b,float max)=>(b-a).magnitude<=max?b:a+(b-a).normalized*max;
    }
    public static class Time { public static float time=10, deltaTime=.02f; }
    public static class Mathf
    {
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp(float a,float min,float max)=>Math.Clamp(a,min,max);
        public static float Clamp01(float a)=>Clamp(a,0,1);
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid="slide-checks"; public static Logger ModLogger=new(); }
    public class Logger { public void LogWarning(string s) { } }
}
namespace ArenaMode.Config
{
    public class Setting<T>(T v)
    {
        private T value=v;
        public event EventHandler SettingChanged;
        public int Subscribers=>SettingChanged?.GetInvocationList().Length??0;
        public T Value { get=>value; set { if(EqualityComparer<T>.Default.Equals(this.value,value))return; this.value=value; SettingChanged?.Invoke(this,EventArgs.Empty); } }
    }
    public static class Settings
    {
        public static Setting<bool> MovementEnabled=new(true),SlideEnabled=new(true);
        public static Setting<float> WalkSpeedMult=new(1),SprintSpeedMult=new(1),ExtraMotionScale=new(1);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.GameWorld ActiveWorld;
        public static EFT.AbstractGame ActiveGame;
        public static bool IsExactActiveRaid(EFT.AbstractGame g,EFT.GameWorld w)=>ReferenceEquals(g,ActiveGame)&&ReferenceEquals(w,ActiveWorld);
    }
}
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown,IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode.Movement
{
    internal static class ArenaAirVault { internal static bool Pending; internal static bool HasPendingRequest(EFT.Player p)=>Pending; }
    internal static class ArenaMovementPatches { internal static float Scale=2; internal static float MotionScale(EFT.MovementContext c)=>Scale; }
    internal static class ArenaAirControl { internal static EFT.MovementContext ResetContext; internal static void ResetHeading(EFT.MovementContext c)=>ResetContext=c; }
}
namespace Redeploy.Core.Engine { public static class RaidLifecycle { public static event Action OnRaidEnded; public static void End()=>OnRaidEnded?.Invoke(); } }
namespace HarmonyLib
{
    public class HarmonyMethod(MethodInfo method) { public MethodInfo Method=method; }
    public class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodInfo original,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null)
        { if(original==null)throw new Exception("missing hook target"); }
        public void UnpatchSelf() { }
    }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
    }
}
namespace EFT
{
    public class AbstractGame { }
    public enum GameStatus { Started,Stopped }
    public class LocalGame:AbstractGame { public GameStatus Status=GameStatus.Started; public Player LocalPlayer; }
    public class GameWorld { public Player MainPlayer; }
    public class HealthController { public bool IsAlive=true; }
    public class Physical { public bool CanJump=true; public int Debits; }
    public class Player
    {
        public bool IsAI,IsInventoryOpened,IsSprintEnabled;
        public bool IsYourPlayer=true;
        public HealthController HealthController=new(); public Physical Physical=new();
        public SimpleCharacterController CharacterController=new();
        public MovementContext MovementContext; public MovementState CurrentManagedState;
    }
    public class PlayerOwner { public Player Player; public void CreateInputTranslators() { } }
    public class MovementContext
    {
        public Player Player;
        public BaseMovementState CurrentState,PreviousState,OverridenState,OverridenControlsState;
        public bool IsGrounded=true,CanWalk=true,CanJump=true,CanStand=true;
        public bool IsInPronePose,IsInMountedState,IsStationaryWeaponInHands,IsAxesIgnored,Vaulting;
        public int BlindFire,PoseCalls,PoseGeometryChecks,PushUpSkills,MoveCalls;
        public Action OnPose;
        public float PoseLevel=1,SmoothedPoseLevel=1,MaxPoseLevel=1,_smoothedCharacterMovementSpeed=1;
        public object PlayerAnimator=new(),Platform;
        public AsyncController ImpostorCC;
        public UnityEngine.Vector3 PlatformMotion,TransformPosition,InputMotion;
        public GroundData GroundHit=new();
        public bool PlayerAnimatorGetIsVaulting()=>Vaulting;
        public bool CanStandAt(float pose) { PoseGeometryChecks++; return pose<PoseLevel || CanStand; }
        public bool SetPoseLevel(float pose,bool force=false)
        {
            PoseCalls++;
            // Native equality return is before both headroom and force sync.
            if(Math.Abs(PoseLevel-pose)<float.Epsilon)return true;
            if(!CanStandAt(pose))return false;
            PoseLevel=Math.Clamp(pose,0,MaxPoseLevel);
            if(!force&&PoseLevel>=MaxPoseLevel)PushUpSkills++; // Recent native straighten timer in this fixture.
            OnPose?.Invoke();
            if(force)SmoothedPoseLevel=PoseLevel;
            return true;
        }
        public void EnableSprint(bool value)=>Player.IsSprintEnabled=value;
        public void DirectApplyMotion(UnityEngine.Vector3 motion,float dt) { MoveCalls++; InputMotion=motion/dt; TransformPosition+=motion; }
    }
    public class AsyncController { public bool UseAsyncMoveValidation; }
    public class GroundData { public bool hasHit=true; public UnityEngine.Vector3 normal=new(0,1,0); }
    public class BaseMovementState { }
    public class MovementState(MovementContext context):BaseMovementState { public MovementContext MovementContext=context; }
    public class IdlePlayerState(MovementContext c):MovementState(c) { }
    public class MovePlayerState(MovementContext c):MovementState(c) { }
    public class SprintPlayerState(MovementContext c):MovePlayerState(c) { }
    public class JumpLandingState(MovementContext c):MovementState(c) { }
    public class SprintToIdleTransitionState(MovementContext c):MovementState(c) { }
    public class OtherTransitionState(MovementContext c):MovementState(c) { }
    public class DerivedSprintExit(MovementContext c):SprintToIdleTransitionState(c) { }
    public class JumpPlayerState(MovementContext c):MovementState(c)
    {
        public enum EJumpState { PushingFromTheGround,Jump,Bumbped }
        public EJumpState _jumpState=EJumpState.Jump;
        public UnityEngine.Vector3 _currentLiftforce=new(0,-1,0),_liftForce=new(0,8,0),_initialNormalizedDirection=new(0,0,1);
        public float _initialMagnitude=1,_currentMagnitude=1;
        public virtual void Enter(bool same) { MovementContext.Player.Physical.Debits++; }
    }
    public class FallDown(MovementContext c):JumpPlayerState(c) { }
    public class MoveInputTranslator(Player p) { public Player _controller=p; public void TranslateCommand(InputSystem.ECommand c) { } }
}
namespace EFT.InputSystem { public enum ECommand { ToggleDuck,PreviousWalkPose,RestorePose,NextWalkPose,Jump,ToggleProne,Vaulting,VaultingEnd,ReloadWeapon } }
public class SimpleCharacterController { public bool isGrounded=true; public void SingleMove(UnityEngine.Vector3 motion) { } }
