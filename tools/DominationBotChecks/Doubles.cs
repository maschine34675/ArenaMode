using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x=x,y=y,z=z;
        public float sqrMagnitude=>x*x+y*y+z*z;
        public float magnitude=>MathF.Sqrt(sqrMagnitude);
        public static Vector3 up=>new(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
    }
    public static class Mathf
    {
        public const float Deg2Rad=MathF.PI/180;
        public static float Cos(float x)=>MathF.Cos(x);
        public static float Sin(float x)=>MathF.Sin(x);
        public static float Min(float a,float b)=>MathF.Min(a,b);
    }
    public static class Time { public static float time,timeScale=1; }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete,PathPartial,PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status;public Vector3[] corners=[]; }
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas=-1;
        public static bool Samples=true, Paths=true;
        public static int Calls;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float distance,int areas) { hit=new(){position=p};return Samples; }
        public static bool CalculatePath(Vector3 a,Vector3 b,int areas,NavMeshPath path)
        { Calls++;path.status=Paths?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;path.corners=[a,b];return true; }
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class {public static T Instance;public static bool Instantiated=>Instance!=null;} }
namespace EFT
{
    public enum EPlayerSide { Usec,Bear,Savage }
    public enum EBotState { Active,NonActive }
    public enum GameStatus { Started,Stopped }
    public class AbstractGame {public GameStatus Status=GameStatus.Started;}
    public class LocalGame:AbstractGame {public GameWorld GameWorld;}
    public class GameWorld
    {
        public Dictionary<string,Player> Bodies=[];
        public Player GetAlivePlayerByProfileID(string id)=>Bodies.GetValueOrDefault(id);
        public Player GetEverExistedPlayerByID(string id)=>Bodies.GetValueOrDefault(id);
    }
    public class Health {public bool IsAlive=true;}
    public class AI {public BotOwner BotOwner;}
    public class Player
    {
        public bool IsAI=true,IsYourPlayer;
        public Health HealthController=new();public string ProfileId;
        public AI AIData=new();public EPlayerSide Side;public Vector3 Position;
    }
    public class Memory {public bool IsUnderFire;}
    public class Enemy {public Player Person;public bool IsVisible,CanShoot;public float PersonalLastShootTime;}
    public class Enemies {public Dictionary<Player,Enemy> EnemyInfos=[];}
    public class Medical {public Part FirstAid=new(),SurgicalKit=new(),Stimulators=new();}
    public class Part {public bool Using,Have2Do,HaveWork;}
    public class Weapons {public Reload Reload=new();public Grenades Grenades=new();}
    public class Reload {public bool Reloading;public int BulletCount=30;}
    public class Grenades {public bool ThrowindNow;}
    public class Door {public bool Interacting;}
    public class Danger {public bool Active;public bool ShallRunAway()=>Active;}
    public class Flash {public bool IsFlashed;}
    public class Steering {public void LookToMovingDirection() { }}
    public class Look {public void SetLookPointByHearing() { }}
    public class BotOwner
    {
        public Player GetPlayer;public string ProfileId=>GetPlayer?.ProfileId;public Vector3 Position=>GetPlayer.Position;
        public EBotState BotState=EBotState.Active;public bool IsDead,CanSprintPlayer=true;
        public object BotsGroup=new();public BotMover Mover=new();public Memory Memory=new();
        public Enemies EnemiesController=new();public Medical Medecine=new();public Weapons WeaponManager=new();
        public Door DoorOpener=new();public Danger BewareGrenade=new(),BewareBTR=new(),ArtilleryDangerPlace=new();
        public Flash FlashGrenade=new();public Steering Steering=new();public Look LookData=new();
        public DoorInteractionStatus DoorStatus=DoorInteractionStatus.CanRun;
        public NavMeshPathStatus RouteResult=NavMeshPathStatus.PathComplete;
        public bool NativeDirectPathFails,Sprinting;public int RouteCalls,CoverFallbackCalls,TeleportFallbackCalls;
        public void MovementResume() { }
        public void Sprint(bool value)=>Sprinting=value;
        public void SetPose(float value) { }
        public void SetTargetMoveSpeed(float value) { }
        public NavMeshPathStatus GoToPoint(Vector3 point,bool slowAtTheEnd,float reachDist,bool getUpWithCheck,bool mustHaveWay,bool mustGetUp=true,bool onlyShortTrie=false)
        {
            RouteCalls++;
            if(NativeDirectPathFails)
            {
                if(!onlyShortTrie){CoverFallbackCalls++;throw new NullReferenceException("cover fallback");}
                if(mustHaveWay)TeleportFallbackCalls++;
                return NavMeshPathStatus.PathInvalid;
            }
            var state=Mover._moverStateMachine.DefaultMoverState;
            state.PathController.CurPath=new(){Target=point};state.LastTargetPoint=point;
            Mover.Corners=[Position,point];Mover.DistDestination=(point-Position).magnitude;
            return RouteResult;
        }
    }
}
public enum EBotMoverState {Default,NearDoor}
public class AbstractBotPath {public Vector3 Target;}
public class BotPathController {public AbstractBotPath CurPath;public void Stop()=>CurPath=null;}
public class BotMoverState {public BotPathController PathController=new();public Vector3? LastTargetPoint;}
public class BotMoverStateMachine {public BotMoverState DefaultMoverState=new(),DoorMoverState=new();}
public class BotMover
{
    public BotMoverStateMachine _moverStateMachine=new();public EBotMoverState CurrentState;
    private BotPathController Controller=>CurrentState==EBotMoverState.Default?_moverStateMachine.DefaultMoverState.PathController:_moverStateMachine.DoorMoverState.PathController;
    public Vector3? TargetPoint=>Controller.CurPath?.Target;public bool HasPathAndNoComplete=>Controller.CurPath!=null;
    public float DistDestination=10;public List<Vector3> Corners=[];public int Stops;
    public void Stop(){Stops++;Controller.Stop();}
    public List<Vector3> GetCornersFromIndex(int index)=>Corners;
}
public class CoreActionResultParams { }
public enum DoorInteractionStatus {CanRun,OpeningDoor,CannotRun}
public class RunToEnemy
{
    protected BotOwner _owner;public RunToEnemy(BotOwner bot){_owner=bot;}
    public virtual void UpdateNodeByBrain(CoreActionResultParams data)=>throw new Exception("unmodified native enemy action");
    public DoorInteractionStatus DoorOpen()=>_owner.DoorStatus;
    public void PeriodChangeWeapon() { }
}
namespace ArenaMode.Modes
{
    public class TeamDeathmatchMode
    {
        public bool IsDomination=true,IsSearchActive=true,IsComplete;
        public int ObjectiveCount=3;
        public HashSet<Player> Owned=[];
        public bool TryClassifyCurrentBody(Player body,out bool friendly){friendly=body.Side==EPlayerSide.Usec;return Owned.Contains(body);}
    }
    public class ArenaMatch {public TeamDeathmatchMode Mode=new();}
    public static class ArenaMatchService {public static ArenaMatch Current;}
}
namespace ArenaMode.Session
{
    public static class ArenaSession {public static bool Exact=true;public static bool IsExactActiveRaid(LocalGame game,GameWorld world)=>Exact;}
}
namespace ArenaMode.Bots
{
    public static class TeamDeathmatchRelations
    {
        public static bool Enabled=true;
        public static bool Register(Player body)=>Enabled&&body!=null;
        public static EPlayerSide? Faction(Player body)=>body?.Side;
    }
}
namespace ArenaMode.Movement {internal static class ArenaBotMovementSain {internal static bool Allowed=true;internal static bool AllowsBoost(BotOwner bot,Player body)=>Allowed;}}
namespace ArenaMode.Pickups {internal static class ArenaPickupGeometry {public static bool Clear=true;internal static bool LineClear(Vector3 from,Vector3 to)=>Clear;}}
namespace ArenaMode.Shared {public readonly struct ArenaAreaPoint(float x,float y,float z);}
namespace ArenaMode.Areas
{
    public static class ArenaAreaRuntime {public static Area CurrentArea;}
    public class Area {public Bounds Bounds=new();}
    public class Bounds
    {
        public bool Allowed=true;
        public bool ContainsPath<T>(ArenaMode.Shared.ArenaAreaPoint start,IReadOnlyList<T> corners,bool closed,Func<T,ArenaMode.Shared.ArenaAreaPoint> map,float margin)=>Allowed;
    }
}
namespace DrakiaXYZ.BigBrain.Brains
{
    public abstract class CustomLayer
    {
        protected BotOwner BotOwner;public CustomLayer(BotOwner bot,int priority){BotOwner=bot;}
        public class ActionData { }
        public class Action(Type type,string reason,ActionData data) {public ActionData Data=data;}
        public virtual string GetName()=>"";public virtual bool IsActive()=>false;
        public virtual Action GetNextAction()=>null;public virtual bool IsCurrentActionEnding()=>false;public virtual void Stop() { }
    }
    public abstract class CustomLogic
    {
        protected BotOwner BotOwner;protected CustomLogic(BotOwner bot){BotOwner=bot;}
        public abstract void Update(CustomLayer.ActionData data);public virtual void Stop() { }
    }
}
namespace ArenaMode.BigBrainCompat
{
    internal static class Library {public static bool RuntimeEnabled=true;public static Logger ModLogger=new();}
    internal class Logger {public void LogWarning(string text) { }}
    internal enum ArenaSainHandoffState {Ready,WaitingForDeactivation,Unavailable}
    internal static class ArenaSainMovementHandoff
    {
        public static ArenaSainHandoffState State;
        public static ArenaSainHandoffState EnsureSuspended(BotOwner bot,out string reason){reason="synthetic";return State;}
    }
}
namespace ArenaMode.Diagnostics {public static class ArenaLog {public static bool DeveloperEnabled;public static void Developer(string text) { }}}
