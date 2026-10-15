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
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { TacticalVest, Pockets }
    public enum CommandStatus { Begin, Succeed }
    public class Item { public string TemplateId; }
    public class ThrowWeap:Item { public ThrowWeap(){TemplateId=ArenaMode.Shared.ArenaMolotovItem.DonorTemplateId;} }
    public class CompoundItem:Item { public Grid[] Grids={new()}; }
    public class Slot { public Item ContainedItem=new CompoundItem(); }
    public class Location { }
    public class Address { public Grid Grid; }
    public class Grid
    {
        public int Capacity=8;public bool Reject;public List<Item> Items=new();
        public Location FindFreeSpace(Item item)=>Items.Count<Capacity?new():null;
        public Address CreateItemAddress(Location location)=>new(){Grid=this};
    }
    public class InventoryEquipment
    {
        public ThrowWeap TopPriorityGrenade;
        public Dictionary<EquipmentSlot,Slot> Slots=Enum.GetValues<EquipmentSlot>().ToDictionary(x=>x,x=>new Slot());
        public Slot GetSlot(EquipmentSlot slot)=>Slots[slot];
        public IEnumerable<Grid> Grids=>Slots.Values.Select(x=>x.ContainedItem).OfType<CompoundItem>().SelectMany(x=>x.Grids);
        public List<Item> Items=>Grids.SelectMany(x=>x.Items).ToList();
    }
    public class Inventory { public InventoryEquipment Equipment=new(); }
    public class InventoryController
    {
        public Inventory Inventory=new();public bool IsChangingWeapon,BlockActions,Unknown,ThrowEvent;public int Events;
        public bool CanExecute(AddResult result)=>!BlockActions;
        public bool Examined(Item item)=>!Unknown;
        public bool IsAtReachablePlace(Item item)=>Inventory.Equipment.Items.Contains(item);
    }
    public class AddResult
    {
        public void RaiseEvents(InventoryController inventory,CommandStatus status)
        { inventory.Events++; if(inventory.ThrowEvent)throw new InvalidOperationException("foreign inventory listener"); }
    }
    public class Result { public bool Failed;public AddResult Value=new(); }
    public static class ItemManipulator
    {
        public static Result Add(Item item,Address to,InventoryController inventory,bool simulate=false)
        {
            if(to.Grid.Reject||to.Grid.Items.Count>=to.Grid.Capacity)return new(){Failed=true};
            if(!simulate)to.Grid.Items.Add(item);
            return new();
        }
    }
    public class ItemTemplate { }
    public class ThrowWeapTemplate:ItemTemplate
    {
        public float Strength,MinExplosionDistance,MaxExplosionDistance,ExplDelay=3.5f,MinTimeToContactExplode=-1;
        public int FragmentsCount;public Vector3 Contusion,Blindness,ArmorDistanceDistanceDamage;
        public string ExplosionEffectType="";public bool CanPlantOnGround,PlayFuzeSound;
    }
    public class ItemFactory
    {
        public string NextId=>Guid.NewGuid().ToString();public int Created;
        public Dictionary<string,ItemTemplate> ItemTemplates=new() { [ArenaMode.Shared.ArenaMolotovItem.TemplateId]=new ThrowWeapTemplate() };
        public Item CreateItem(string id,string tpl,object upd){Created++;return new ThrowWeap{TemplateId=tpl};}
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo
    { public EDamageType DamageType;public float Damage;public IObserverToPlayerBridge Player;public EFT.InventoryLogic.ThrowWeap Weapon;public string SourceId;public Vector3 HitPoint,Direction,HitNormal; }
}
namespace EFT
{
    public class ObjectsFactory { }
    public enum EDamageType { Flame,Bullet }
    public enum GameStatus { Started,Stopping }
    public interface IPlayer { string ProfileId {get;} }
    public class Player : MonoBehaviour,IPlayer
    {
        public string ProfileId {get;set;} public bool IsAI,IsYourPlayer;public Vector3 Position;
        public Health ActiveHealthController=new(); public Health HealthController=>ActiveHealthController;
        public ItemHandsController HandsController=new();
        public EFT.InventoryLogic.InventoryController InventoryController=new SinglePlayerInventoryController();
        public class SinglePlayerInventoryController:EFT.InventoryLogic.InventoryController { } public AIData AIData;
        public float Taken;public IPlayer LastSource;public bool Scoped;
        public EFT.InventoryLogic.ThrowWeap LastWeapon;public string LastSourceId;public EDamageType LastDamageType;
        public bool Protected;public int AdmissionCalls,Deaths;public Action<DamageInfo> OnDeath;
        public virtual void ApplyDamageInfo(DamageInfo info,EBodyPart part,EBodyPartColliderType collider,float absorbed)
        {
            AdmissionCalls++;
            if(!HealthController.IsAlive)return;
            // Boundary double for the real ApplyDamageInfo prefixes. The common
            // production mutator is linked; team ledgers and protection are not.
            if(Protected||ArenaMode.Combat.ArenaFriendlyFire.ShouldBlockFriendlyDamage(this,info.Player?.iPlayer))return;
            LastSource=info.Player?.iPlayer;
            LastWeapon=info.Weapon;LastSourceId=info.SourceId;LastDamageType=info.DamageType;
            Scoped=ArenaMode.Combat.Fire.ArenaFireRuntime.OwnsDamage(this,LastSource,info.DamageType);
            float amount=info.Damage*Comfort.Common.Singleton<GlobalConfiguration>.Instance.Health.ProfileHealthSettings.BodyPartsSettings[part].EnvironmentDamageMultiplier;
            if(ActiveHealthController.TrackHealth)
            {
                var hp=ActiveHealthController.GetBodyPartHealth(part);amount=Math.Min(amount,hp.Current);hp.Current-=amount;
                ActiveHealthController.Parts[part]=hp;
                if(hp.Current<=0&&(part==EBodyPart.Head||part==EBodyPart.Chest))
                {ActiveHealthController.IsAlive=false;Deaths++;OnDeath?.Invoke(info);}
            }
            Taken+=amount;
        }
        public class ItemHandsController { public Player _player; }
        public class BaseGrenadeHandsController:ItemHandsController { public EFT.InventoryLogic.ThrowWeap Item=new(); }
    }
    public class Health
    {
        public bool IsAlive=true,TrackHealth;
        public Dictionary<EBodyPart,HealthValue> Parts=new();
        public HealthValue GetBodyPartHealth(EBodyPart part)=>Parts.TryGetValue(part,out var hp)?hp:new(){Current=100,Maximum=100};
        public void StandardProfile()
        {
            TrackHealth=true;float[] maxima={35,85,70,60,60,65,65};int i=0;
            foreach(var part in Enum.GetValues<EBodyPart>())Parts[part]=new(){Current=maxima[i],Maximum=maxima[i++]};
        }
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
    public class AbstractGame { public GameStatus Status=GameStatus.Started; }
    public class LocalGame:AbstractGame
    {
        public GameWorld GameWorld;
        private readonly List<Stack<System.Collections.IEnumerator>> Jobs=new();
        public void StartCoroutine(System.Collections.IEnumerator routine)
        { var stack=new Stack<System.Collections.IEnumerator>();stack.Push(routine);Jobs.Add(stack);AdvanceCoroutines(); }
        public void AdvanceCoroutines()
        {
            foreach(var stack in Jobs.ToArray())
            {
                while(stack.Count>0)
                {
                    var top=stack.Peek();if(!top.MoveNext()){stack.Pop();continue;}
                    if(top.Current is System.Collections.IEnumerator child){stack.Push(child);continue;}
                    break;
                }
                if(stack.Count==0)Jobs.Remove(stack);
            }
        }
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger { public void LogInfo(object s){} public void LogDebug(object s){} public void LogWarning(object s){} }
}
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool IsActive,FriendlyFireEnabledSelection; public static bool IsExactActiveRaid(AbstractGame g,GameWorld w)=>IsActive&&g is LocalGame local&&ReferenceEquals(local.GameWorld,w); }
}
namespace ArenaMode.Session { public static class ArenaServerClient { public static bool MolotovAvailableForConfirmedRaid=true; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Combat { public static class WeaponSwapService { public static bool IsSwapping; } }
namespace ArenaMode.Kits
{
    public class KitDefinition { public string Id;public KitFill Fill; }
    public class KitFill { public List<KitStackedItem> RigContents; }
    public class KitStackedItem { public string Tpl;public int Count; }
    public static class KitBundlePreloader
    {
        public static bool IsPreloading,Hold,Fail,Warmed;
        public static bool IsTemplateWarmed(string tpl)=>Warmed;
        public static System.Collections.IEnumerator Preload(KitDefinition kit)
        { while(Hold)yield return null;Warmed=!Fail; }
    }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { bool IsComplete {get;} }
    public class ArenaMatch { public IArenaMode Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current;public static bool TeamStartAbortPending; }
    public class RoundsMode:IArenaMode { public bool Complete;public bool IsComplete=>Complete;public bool AssistScoringOpen=true;public Round Round=new(); }
    public class Round { public int Number=1; }
    public class TacticalMode:IArenaMode
    {
        public bool IsComplete=>false;public bool AssistScoringOpen=true;public int RoundNumber=1;
        public Func<Player,IPlayer,bool> FriendlyDamageRule=(v,a)=>false;
        public bool ShouldBlockFriendlyDamage(Player v,IPlayer p)=>FriendlyDamageRule(v,p);
        public bool IsOpposingAwardKill(Player v,IPlayer p)=>!FriendlyDamageRule(v,p);
    }
    public class TeamDeathmatchMode:IArenaMode { public bool IsComplete=>false;public bool AssistScoringOpen=true; }
    public class GunGameMode:IArenaMode { public bool IsComplete=>false;public bool AssistScoringOpen=true; }
    public class ZombieMode:IArenaMode { public bool IsComplete=>false;public bool ShouldBlockFriendlyDamage(Player v,IPlayer p)=>false; }
}
namespace ArenaMode.Bots
{
    public static class TeamDeathmatchRelations
    {
        public static Func<Player,IPlayer,bool> FriendlyDamageRule=(v,a)=>false;
        public static bool ShouldBlockFriendlyDamage(Player v,IPlayer a)=>FriendlyDamageRule(v,a);
    }
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
    {
        internal static int Alive,ConstructorFailures,TickFailures,DisposeFailures;private bool disposed;
        public ArenaFireView(IReadOnlyList<Vector3> v){if(ConstructorFailures-- > 0)throw new InvalidOperationException("visual constructor failure");Alive++;}
        public void Tick(float t){if(TickFailures-- > 0)throw new InvalidOperationException("visual tick failure");}
        public void Dispose(){if(!disposed){disposed=true;Alive--;}if(DisposeFailures-- > 0)throw new InvalidOperationException("visual disposal failure");}
    }
}

// Explicitly enabled for these diagnostic-observation scenarios; the real
// default-off BepInEx routing is exercised by tools/LoggingChecks.
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled = true;
        internal static void Developer(string message)
        {
            if (DeveloperEnabled) ArenaMode.Plugin.ModLogger?.LogInfo("[dev] " + message);
        }
    }
}
