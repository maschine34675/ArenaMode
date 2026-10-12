using EFT;
using EFT.Ballistics;
using UnityEngine;
namespace UnityEngine
{
    public class Object { }
    public class MonoBehaviour : Object { }
    public class Transform { public Vector3 position; }
    public struct Quaternion { }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero=>default; public static Vector3 up=>new(0,1,0); public static Vector3 down=>new(0,-1,0);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
        public static Vector3 ClampMagnitude(Vector3 a,float length)=>a.magnitude>length?a/a.magnitude*length:a;
    }
    public static class Mathf { public const float PI=MathF.PI; public static float Cos(float x)=>MathF.Cos(x); public static float Sin(float x)=>MathF.Sin(x); public static int CeilToInt(float x)=>(int)Math.Ceiling(x); }
    public static class Time { public static float time,timeScale=1; }
    public enum QueryTriggerInteraction { Ignore }
    public class Collider { public T GetComponentInParent<T>() where T:class =>null; }
    public struct RaycastHit { public Collider collider; public Vector3 point,normal;public float distance; }
    public struct ContactPoint { public Vector3 point,normal; }
    public class Collision { public List<ContactPoint> contacts=new(); public int contactCount=>contacts.Count; public ContactPoint GetContact(int i)=>contacts[i]; }
    public static class Physics
    {
        public const int DefaultRaycastLayers=-1;
        public static bool Saturated; public static float? WallX;
        public static int RaycastNonAlloc(Vector3 start,Vector3 dir,RaycastHit[] hits,float length,int mask,QueryTriggerInteraction ignore)
        {
            if(Saturated)return hits.Length;
            int n=0;
            if(dir.y < -.0001f) { float d=-start.y/dir.y; if(d>=0&&d<=length)hits[n++]=new(){distance=d,point=start+dir*d,normal=Vector3.up,collider=new()}; }
            if(WallX.HasValue&&Math.Abs(dir.x)>.0001f) { float d=(WallX.Value-start.x)/dir.x; if(d>=0&&d<=length)hits[n++]=new(){distance=d,point=start+dir*d,normal=new(-1,0,0),collider=new()}; }
            return n;
        }
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }
    public class ConfigFile { public readonly Dictionary<string,object> Stored=new(); public ConfigEntry<T> Bind<T>(string a,string b,T value,string c)=>new(){Value=Stored.TryGetValue(a+"/"+b,out var saved)?(T)saved:value}; }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T,F>(T obj);
        public static FieldRef<T,F> FieldRefAccess<T,F>(string name)=>Get<T,F>;
        private static ref F Get<T,F>(T obj)=>ref System.Runtime.CompilerServices.Unsafe.As<Player,F>(ref ((Player.ItemHandsController)(object)obj)._player);
        public static System.Reflection.MethodBase DeclaredMethod(Type t,string name,Type[] p=null)=>null;
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract System.Reflection.MethodBase GetTargetMethod(); public void Enable() { } }
    public class PatchPostfix:Attribute { } public class PatchPrefix:Attribute { }
}
public enum EBodyPart { Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg }
public enum EBodyPartColliderType { None }
public interface IObserverToPlayerBridge { IPlayer iPlayer {get;} }
public sealed class Bridge : IObserverToPlayerBridge { public IPlayer iPlayer {get;set;} }
public class GlobalConfiguration
{
    public HealthConfig Health=new();
    public class HealthConfig { public ProfileConfig ProfileHealthSettings=new(); }
    public class ProfileConfig { public Dictionary<EBodyPart,PartConfig> BodyPartsSettings=Enum.GetValues<EBodyPart>().ToDictionary(p=>p,p=>new PartConfig()); }
    public class PartConfig { public float EnvironmentDamageMultiplier=.5f; }
}
namespace EFT.InventoryLogic { public class ThrowWeap { public string TemplateId=ArenaMode.Combat.Fire.ArenaFirePolicy.DonorTemplate; } }
namespace EFT.Ballistics
{
    public struct DamageInfo
    { public EDamageType DamageType;public float Damage;public IObserverToPlayerBridge Player;public EFT.InventoryLogic.ThrowWeap Weapon;public string SourceId;public Vector3 HitPoint,Direction,HitNormal; }
}
namespace EFT
{
    public enum EDamageType { Flame,Bullet }
    public enum GameStatus { Started,Stopping }
    public interface IPlayer { string ProfileId {get;} }
    public class Player : MonoBehaviour,IPlayer
    {
        public string ProfileId {get;set;} public bool IsAI,IsYourPlayer;public Vector3 Position;
        public Health ActiveHealthController=new(); public Health HealthController=>ActiveHealthController;
        public ItemHandsController HandsController; public AIData AIData;
        public float Taken;public IPlayer LastSource;public bool Scoped;
        public virtual void ApplyDamageInfo(DamageInfo info,EBodyPart part,EBodyPartColliderType collider,float absorbed)
        {
            LastSource=info.Player?.iPlayer;
            Scoped=ArenaMode.Combat.Fire.ArenaFireRuntime.OwnsDamage(this,LastSource,info.DamageType);
            float amount=info.Damage*Comfort.Common.Singleton<GlobalConfiguration>.Instance.Health.ProfileHealthSettings.BodyPartsSettings[part].EnvironmentDamageMultiplier;
            Taken+=amount;
        }
        public class ItemHandsController { public Player _player; }
        public class BaseGrenadeHandsController:ItemHandsController { public EFT.InventoryLogic.ThrowWeap Item=new(); }
    }
    public class Health
    {
        public bool IsAlive=true;
        public HealthValue GetBodyPartHealth(EBodyPart part)=>new(){Current=100,Maximum=100};
    }
    public struct HealthValue { public float Current,Maximum; }
    public class Grenade : MonoBehaviour
    {
        public int Id;public EFT.InventoryLogic.ThrowWeap WeaponSource;public IObserverToPlayerBridge Player;public Transform transform=new();
        public bool Retired,TimerStopped;
        public int RetireFailures;
        public void StopTimer()=>TimerStopped=true;
        public void OnExplosion(){ if(RetireFailures-- > 0)throw new InvalidOperationException("native teardown failure");Retired=true; }
        public void InvokeBlowUpEvent(){ Program.Explode(this);OnExplosion(); }
        public void Explosion() { }
    }
    public class GameWorld
    {
        public Player MainPlayer;public List<Player> AllAlivePlayersList=new();
        public Player GetAlivePlayerByProfileID(string id)=>AllAlivePlayersList.FirstOrDefault(p=>p.ProfileId==id);
        public Player GetEverExistedPlayerByID(string id)=>GetAlivePlayerByProfileID(id);
    }
    public class AbstractGame { }
    public class LocalGame:AbstractGame { public GameStatus Status=GameStatus.Started;public GameWorld GameWorld; }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger { public void LogInfo(object s){} public void LogWarning(object s){} }
}
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool IsActive; public static bool IsExactActiveRaid(LocalGame g,GameWorld w)=>IsActive&&ReferenceEquals(g.GameWorld,w); }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { bool IsComplete {get;} }
    public class ArenaMatch { public IArenaMode Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current;public static bool TeamStartAbortPending; }
    public class RoundsMode:IArenaMode { public bool Complete;public bool IsComplete=>Complete;public bool AssistScoringOpen=true;public Round Round=new(); }
    public class Round { public int Number=1; }
    public class TacticalMode:IArenaMode { public bool IsComplete=>false;public bool AssistScoringOpen=true;public int RoundNumber=1;public bool IsOpposingAwardKill(Player v,IPlayer p)=>v.IsAI; }
    public class TeamDeathmatchMode:IArenaMode { public bool IsComplete=>false;public bool AssistScoringOpen=true; }
    public class GunGameMode:IArenaMode { public bool IsComplete=>false;public bool AssistScoringOpen=true; }
    public class ZombieMode:IArenaMode { public bool IsComplete=>false;public bool IsTeamArena;public bool IsOpposingAwardKill(Player v,IPlayer p)=>v.IsAI; }
}
namespace ArenaMode.Areas
{
    public static class ArenaAreaRuntime { public static bool IsPending;public static Area CurrentArea; }
    public class Area { public Bounds Bounds=new(); }
    public class Bounds
    {
        public bool Contains(float x,float y,float z,float p)=> x>=-2&&x<=2&&z>=-2&&z<=2;
        public bool ContainsPath<T>(ArenaMode.Shared.ArenaAreaPoint p,IReadOnlyList<T> c,bool v,Func<T,ArenaMode.Shared.ArenaAreaPoint> map,float margin)=>c.Select(map).All(p=>Contains(p.X,p.Y,p.Z,margin));
    }
}
namespace ArenaMode.Shared { public record struct ArenaAreaPoint(float X,float Y,float Z); }
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete,PathPartial }
    public class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners; }
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas=-1;public static int Calls;public static Func<Vector3,bool> Allow=p=>true;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,int mask){hit=new(){position=p};return true;}
        public static bool CalculatePath(Vector3 start,Vector3 end,int mask,NavMeshPath path)
        {Calls++;path.corners=new[]{end};path.status=Allow(end)?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return true;}
    }
}
public class CoreActionResultParams { }
public class AbstractBotPath { public int CurIndex; }
public enum EBotMoverState { Default,NearDoor }
public class PathController { public AbstractBotPath CurPath; public void Stop()=>CurPath=null; }
public class BotMoverState { public PathController PathController=new();public Vector3? LastTargetPoint; }
public class BotMoverStateMachine { public BotMoverState DefaultMoverState=new(); }
public class BotMover
{
    public EBotMoverState CurrentState;public Vector3? TargetPoint;public float DistDestination=4;
    public BotMoverStateMachine _moverStateMachine=new();public int Stops;
    public void Stop(){Stops++;_moverStateMachine.DefaultMoverState.PathController.Stop();}
    public List<Vector3> GetCornersFromIndex(int index)=>new(){TargetPoint??default};
}
public class RunToEnemy
{
    protected EFT.BotOwner _owner;public RunToEnemy(EFT.BotOwner b){_owner=b;}
    public virtual void UpdateNodeByBrain(CoreActionResultParams d){}
}
namespace EFT
{
    public enum EBotState { Active,NonActive }
    public class AIData { public BotOwner BotOwner; }
    public class Hazard { public bool Active,IsFlashed,Interacting;public bool ShallRunAway()=>Active; }
    public class Steering { public void LookToMovingDirection(){} }
    public class BotOwner
    {
        public Player GetPlayer;public string ProfileId=>GetPlayer.ProfileId;public Vector3 Position=>GetPlayer.Position;
        public bool IsDead;public EBotState BotState;public BotMover Mover=new();public bool Sprinting;
        public Hazard DoorOpener=new(),BewareGrenade=new(),BewareBTR=new(),ArtilleryDangerPlace=new(),FlashGrenade=new();public Steering Steering=new();
        public void MovementResume(){}public void Sprint(bool b)=>Sprinting=b;public void SetPose(float p){}public void SetTargetMoveSpeed(float p){}
        public UnityEngine.AI.NavMeshPathStatus GoToPoint(Vector3 p,bool slowAtTheEnd,float reachDist,bool getUpWithCheck,bool mustHaveWay)
        {Mover.TargetPoint=p;Mover._moverStateMachine.DefaultMoverState.LastTargetPoint=p;Mover._moverStateMachine.DefaultMoverState.PathController.CurPath=new();return UnityEngine.AI.NavMeshPathStatus.PathComplete;}
    }
}
namespace DrakiaXYZ.BigBrain.Brains
{
    public abstract class CustomLayer
    {
        protected EFT.BotOwner BotOwner;public Action CurrentAction;public CustomLayer(EFT.BotOwner b,int p){BotOwner=b;}
        public abstract string GetName();public abstract bool IsActive();public abstract Action GetNextAction();public abstract bool IsCurrentActionEnding();public virtual void Stop(){}
        public class Action {public ActionData Data;public Action(Type t,string r,ActionData data){Data=data;} }
        public class ActionData:CoreActionResultParams{}
    }
    public abstract class CustomLogic
    {
        protected EFT.BotOwner BotOwner;public CustomLogic(EFT.BotOwner bot){BotOwner=bot;}public abstract void Update(CustomLayer.ActionData data);public virtual void Stop(){}
    }
}
namespace ArenaMode.Movement { public static class ArenaBotMovementSain { public static bool Safe=true;public static bool AllowsBoost(EFT.BotOwner b,EFT.Player p)=>Safe; } }
namespace ArenaMode.BigBrainCompat
{
    public static class Plugin { public static bool RuntimeEnabled=true;public static ArenaMode.Logger ModLogger=new(); }
    public enum ArenaSainHandoffState { Unavailable,Ready,WaitingForDeactivation }
    public static class ArenaSainMovementHandoff { public static ArenaSainHandoffState EnsureSuspended(EFT.BotOwner b,out string r){r=null;return ArenaSainHandoffState.Ready;} }
}
namespace ArenaMode.Pickups { public static class ArenaPickupRuntime { public static bool OwnsCollider(Collider c)=>false; } }
namespace ArenaMode.Combat.Fire
{
    internal class ArenaFireView : IDisposable
    { internal static int Alive;public ArenaFireView(IReadOnlyList<Vector3> v){Alive++;} public void Tick(float t){} public void Dispose(){Alive--;} }
}
