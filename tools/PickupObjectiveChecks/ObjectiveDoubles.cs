using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace DrakiaXYZ.BigBrain.Brains
{
    public abstract class CustomLayer
    {
        protected BotOwner BotOwner;
        public CustomLayer(BotOwner owner,int priority){BotOwner=owner;}
        public class ActionData { }
        public class Action(Type type,string reason,ActionData data) { public ActionData Data=data; }
        public virtual string GetName()=>"";
        public virtual bool IsActive()=>false;
        public virtual Action GetNextAction()=>null;
        public virtual bool IsCurrentActionEnding()=>false;
        public virtual void Stop() { }
    }
    public abstract class CustomLogic
    {
        protected BotOwner BotOwner;
        protected CustomLogic(BotOwner owner){BotOwner=owner;}
        public abstract void Update(CustomLayer.ActionData data);
        public virtual void Stop() { }
    }
}
namespace ArenaMode.BigBrainCompat
{
    internal static class Plugin { internal static bool RuntimeEnabled=true; internal static ArenaMode.Plugin.Logger ModLogger=>ArenaMode.Plugin.ModLogger; }
    internal enum ArenaSainHandoffState { Ready, WaitingForDeactivation, Unavailable }
    internal static class ArenaSainMovementHandoff
    {
        internal static ArenaSainHandoffState State;
        internal static int Calls;
        internal static ArenaSainHandoffState EnsureSuspended(BotOwner bot,out string reason){Calls++;reason="synthetic";return State;}
    }
}
namespace ArenaMode.Movement
{
    internal static class ArenaBotMovementSain { internal static bool Allowed=true;internal static bool AllowsBoost(BotOwner bot,Player player)=>Allowed; }
}
public enum EBotMoverState { Default, NearDoor }
public class AbstractBotPath { public Vector3 Target; }
public class BotPathController
{
    public AbstractBotPath CurPath;
    public void Stop()=>CurPath=null;
}
public class BotMoverState
{
    public BotPathController PathController=new();
    public Vector3? LastTargetPoint;
}
public class BotMoverStateMachine
{
    public BotMoverState DefaultMoverState=new(),DoorMoverState=new();
}
public class BotMover
{
    public BotMoverStateMachine _moverStateMachine=new();
    private BotPathController Controller=>CurrentState==EBotMoverState.Default?_moverStateMachine.DefaultMoverState.PathController:_moverStateMachine.DoorMoverState.PathController;
    public Vector3? TargetPoint { get=>Controller.CurPath?.Target; set=>Controller.CurPath=value.HasValue?new(){Target=value.Value}:null; }
    public bool HasPathAndNoComplete { get=>Controller.CurPath!=null;set{if(!value)Controller.CurPath=null;} }
    public EBotMoverState CurrentState;
    public float DistDestination=10;
    public List<Vector3> Corners=[];
    public int Stops;
    public void Stop(){Stops++;HasPathAndNoComplete=false;TargetPoint=null;}
    public bool IsComeTo(float dist,bool onCover)=>HasPathAndNoComplete&&DistDestination<=dist;
    public List<Vector3> GetCornersFromIndex(int index)=>Corners;
}
public class CoreActionResultParams { }
public enum DoorInteractionStatus { CanRun, OpeningDoor, CannotRun }
public class RunToEnemy
{
    protected BotOwner _owner;
    public RunToEnemy(BotOwner owner){_owner=owner;}
    public virtual void UpdateNodeByBrain(CoreActionResultParams data)=>throw new Exception("Stock enemy action must not execute");
    public DoorInteractionStatus DoorOpen()
    {
        if (_owner.MoveFailure != null) throw _owner.MoveFailure;
        return _owner.DoorStatus;
    }
    public void PeriodChangeWeapon() { }
}
namespace EFT
{
    public class Memory { public bool IsUnderFire; }
    public class Enemy { public Player Person; public bool IsVisible;public float PersonalLastSeenTime=-1,PersonalLastShootTime=-1; }
    public class Enemies { public Dictionary<Player,Enemy> EnemyInfos=[]; }
    public class Medical { public MedicalPart FirstAid=new(),SurgicalKit=new(),Stimulators=new(); }
    public class MedicalPart { public bool Using; }
    public class Weapons { public Reload Reload=new();public Grenades Grenades=new(); }
    public class Reload { public bool Reloading; }
    public class Grenades { public bool ThrowindNow; }
    public class Door { public bool Interacting; }
    public class Danger { public bool Active;public bool ShallRunAway()=>Active; }
    public class Flash { public bool IsFlashed; }
    public class Steering { public int Calls;public void LookToMovingDirection()=>Calls++; }
    public partial class BotOwner
    {
        public string ProfileId=>GetPlayer?.ProfileId;
        public Vector3 Position=>GetPlayer.Position;
        public bool IsDead;
        public object BotsGroup=new();public BotMover Mover=new();public Memory Memory=new();
        public Enemies EnemiesController=new();public Medical Medecine=new();public Weapons WeaponManager=new();
        public Door DoorOpener=new();public Danger BewareGrenade=new(),BewareBTR=new(),ArtilleryDangerPlace=new();
        public Flash FlashGrenade=new();public Steering Steering=new();
        public DoorInteractionStatus DoorStatus=DoorInteractionStatus.CanRun;
        public NavMeshPathStatus RouteResult=NavMeshPathStatus.PathComplete;
        public Vector3? WrongTarget;
        public int RouteCalls;public bool Sprinting;
        public bool NativeDirectPathFails;
        public int CoverFallbackCalls, TeleportFallbackCalls;
        public Exception MoveFailure;
        public void MovementResume() { }
        public void StopMove()=>Mover.Stop();
        public void Sprint(bool val)=>Sprinting=val;
        public void SetPose(float value) { }
        public void SetTargetMoveSpeed(float value) { }
        public NavMeshPathStatus GoToPoint(Vector3 point,bool slowAtTheEnd,float reachDist,bool getUpWithCheck,bool mustHaveWay,bool mustGetUp=true,bool onlyShortTrie=false)
        {
            RouteCalls++;
            // Model the inspected native ordering: the direct calculation is
            // independent of the sampled preflight and can fail later. Short
            // failure returns before a cover dereference; mustHaveWay owns the
            // separate teleport branch. This is not native NavMesh execution.
            if (NativeDirectPathFails)
            {
                if (!onlyShortTrie) { CoverFallbackCalls++; throw new NullReferenceException("missing native cover node"); }
                if (mustHaveWay) TeleportFallbackCalls++;
                return NavMeshPathStatus.PathInvalid;
            }
            Mover.TargetPoint=WrongTarget??point;Mover.HasPathAndNoComplete=true;
            Mover.Corners=[Position,point];Mover.DistDestination=(point-Position).magnitude;return RouteResult;
        }
    }
}

internal sealed class PickupProbeException : Exception
{
    internal int DetailReads;
    internal PickupProbeException() : base("synthetic native movement failure") { }
    public override string ToString() { DetailReads++; return base.ToString(); }
}
