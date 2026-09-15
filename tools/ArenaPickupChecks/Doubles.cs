// Explicit boundaries, not a replacement implementation of pickup policy.
using System.Collections;
using EFT;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x=x,y=y,z=z;
        public static Vector3 up => new(0,1,0);
        public static Vector3 down => new(0,-1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a, Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
        public static Vector3 operator /(Vector3 a,float s)=>new(a.x/s,a.y/s,a.z/s);
        public static Vector3 Lerp(Vector3 a,Vector3 b,float f)=>a+(b-a)*f;
    }
    public struct Quaternion { public static Quaternion Euler(float x,float y,float z)=>new(); }
    public class Transform { public Transform parent; public bool IsChildOf(Transform other)=>ReferenceEquals(this,other)||parent?.IsChildOf(other)==true; }
    public class GameObject { public Transform transform=new(); }
    public class Collider { public Transform transform=new(); public object Owner; public T GetComponentInParent<T>() where T:class=>Owner as T; }
    public struct RaycastHit { public float distance; public Vector3 point,normal; public Collider collider; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Time { public static float time,unscaledTime,timeScale=1; }
    public static class Physics
    {
        public const int DefaultRaycastLayers=-5;
        public static Func<Vector3,Vector3,float,RaycastHit[]> Rays;
        public static Collider[] Overlaps=[];
        public static RaycastHit[] RaycastAll(Vector3 from,Vector3 direction,float distance,int layers,QueryTriggerInteraction trigger)=>Rays?.Invoke(from,direction,distance)??[];
        public static Collider[] OverlapCapsule(Vector3 a,Vector3 b,float radius,int layers,QueryTriggerInteraction trigger)=>Overlaps;
    }
}
namespace UnityEngine.AI
{
    public struct NavMeshHit { public Vector3 position; }
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners=[]; }
    public static class NavMesh
    {
        public const int AllAreas=-1;
        public static bool SampleOk=true,PathOk=true;
        public static NavMeshPathStatus Status=NavMeshPathStatus.PathComplete;
        public static Func<Vector3,Vector3> Sample=p=>p;
        public static Vector3[] Corners;
        public static int Samples,Paths;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float distance,int mask)
        { Samples++; hit=new(){position=Sample(p)}; return SampleOk; }
        public static bool CalculatePath(Vector3 a,Vector3 b,int mask,NavMeshPath path)
        { Paths++;path.status=Status;path.corners=Corners??[a,b];return PathOk; }
    }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace EFT
{
    public enum EBotState { Active, PreActive, NonActive }
    public enum GameStatus { Started, Starting, Stopping }
    public class AbstractGame { }
    public class LocalGame:AbstractGame
    {
        public GameStatus Status=GameStatus.Started;
        public GameWorld GameWorld; public Player LocalPlayer;
        public readonly List<Stack<IEnumerator>> Coroutines=[];
        public void StartCoroutine(IEnumerator routine)=>Coroutines.Add(new([routine]));
        public void Pump()
        {
            foreach(var stack in Coroutines.ToArray())
            {
                int guard=100;
                while(stack.Count>0 && guard-->0)
                {
                    if(!stack.Peek().MoveNext()) {stack.Pop();continue;}
                    if(stack.Peek().Current is IEnumerator nested){stack.Push(nested);continue;}
                    break;
                }
                if(stack.Count==0) Coroutines.Remove(stack);
            }
        }
    }
    public class Health { public bool IsAlive=true; public float DamageCoeff=1; }
    public class MovementContext { public bool IsGrounded=true; }
    public class Inventory { public bool IsChangingWeapon; }
    public class BotOwner { public EBotState BotState=EBotState.Active;public Player GetPlayer; }
    public class AIData { public BotOwner BotOwner; }
    public class Player
    {
        public string ProfileId; public bool IsAI,IsYourPlayer; public Vector3 Position;
        public Health HealthController=new(),ActiveHealthController=new();
        public MovementContext MovementContext=new(); public Inventory InventoryController=new(); public AIData AIData;
    }
    public class GameWorld
    {
        public string LocationId="factory4_day"; public Player MainPlayer;
        public List<Player> AllAlivePlayersList=[];
        public Dictionary<string,Player> Alive=[],Ever=[];
        public Player GetAlivePlayerByProfileID(string id)=>Alive.GetValueOrDefault(id);
        public Player GetEverExistedPlayerByID(string id)=>Ever.GetValueOrDefault(id);
        public void Add(Player p){AllAlivePlayersList.Add(p);Alive[p.ProfileId]=p;Ever[p.ProfileId]=p;}
        public void Remove(Player p){AllAlivePlayersList.Remove(p);if(Alive.GetValueOrDefault(p.ProfileId)==p)Alive.Remove(p.ProfileId);}
    }
}
namespace ArenaMode
{
    public class Plugin
    {
        public static Plugin Instance=new();public InfoType Info=new();public static Logger ModLogger=new();
        public class InfoType {public string Location;}
        public class Logger { public List<string> Lines=[];public void LogWarning(object x)=>Lines.Add("Warning: "+x);public void LogInfo(object x)=>Lines.Add("Info: "+x); }
    }
}
namespace ArenaMode.Shared { public struct ArenaAreaPoint(float x,float y,float z) {public float X=x,Y=y,Z=z;} }
namespace ArenaMode.Areas
{
    public class Bounds
    {
        public bool PointAllowed=true,PathAllowed=true;
        public bool Contains(float x,float y,float z,float inset)=>PointAllowed;
        public bool ContainsPath<T>(ArenaMode.Shared.ArenaAreaPoint anchor,T[] points,bool closed,Func<T,ArenaMode.Shared.ArenaAreaPoint> projection,float inset)=>PathAllowed;
    }
    public class Area {public string Id;public Bounds Bounds=new();}
    public static class ArenaAreaRuntime { public static bool IsPending;public static Area CurrentArea; }
}
namespace ArenaMode.Combat { }
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode; }
    public class RoundData {public int Number=1;}
    public class RoundsMode {public bool AssistScoringOpen=true,IsComplete;public RoundData Round=new();}
    public class TeamDeathmatchMode {public bool AssistScoringOpen=true,IsComplete;}
    public class TacticalMode { }
    public class GunGameMode { }
    public static class ArenaMatchService {public static ArenaMatch Current;public static bool TeamStartAbortPending;}
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive=true,Exact=true;
        public static bool IsExactActiveRaid(LocalGame game,GameWorld world)=>Exact;
    }
}
namespace ArenaMode.Compat { public static class PitFireTeamCompat {public static bool TeamArenaActive;} }
namespace ArenaMode.Hud { public static class ArenaHudVisibility {public static bool IsBattleUiActive=true;} }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver {public static bool IsRespawnInProgress;} }
namespace ArenaMode.Kits
{
    public class KitDefinition {public string Id;}
    public static class KitCatalog {public static List<KitDefinition> Kits=[new(){Id="spartan"}];}
    public static class KitBundlePreloader {public static bool IsPreloading;}
    public static class WeaponSwapService {public static bool IsSwapping;}
}
namespace ArenaMode.Pickups
{
    public class Setting<T>(T value) {public T Value=value;}
    public static class ArenaPickupSettings
    {
        public static Setting<bool> Enabled=new(true),BotsCollectPowerups=new(true);
        public static Setting<string> WeaponKit=new("spartan");
    }
    internal static class ArenaPowerups
    {
        internal static List<(Player Player,string Kind,float Points,float Seconds)> Grants=[];internal static int Resets;internal static bool GrantResult=true;
        internal static void Reset()=>Resets++;
        internal static bool GrantDamage(Player p,float seconds){if(GrantResult)Grants.Add((p,"damage",0,seconds));return GrantResult;}
        internal static bool GrantShield(Player p,float points,float seconds){if(GrantResult)Grants.Add((p,"shield",points,seconds));return GrantResult;}
    }
    internal sealed class ArenaPickupView:IDisposable
    {
        internal static List<ArenaPickupView> Created=[];internal static bool ThrowOnTick;
        internal ArenaPickupPoint Point;internal bool Disposed;internal ArenaPickupPhase Phase;
        internal ArenaPickupView(ArenaPickupPoint point,Vector3 floor){Point=point;Created.Add(this);}
        internal void Tick(ArenaPickupState state,bool preparing,GameObject visual){if(ThrowOnTick)throw new InvalidOperationException("test view failure");Phase=state.Phase;}
        public void Dispose()=>Disposed=true;
    }
    internal static class ArenaWeaponPickup
    {
        internal static List<Handle> Created=[];internal static bool PrepareNull;
        internal static IEnumerator Prepare(GameWorld world,ArenaMode.Kits.KitDefinition kit,Vector3 p,Quaternion q,Func<bool> current,Action<Handle> done)
        {if(PrepareNull)done(null);else {var h=new Handle();Created.Add(h);done(h);}yield break;}
        internal sealed class Handle:IDisposable
        {
            internal GameObject Visual=new();internal bool IsAvailable=true,IsPending,Disposed,StartResult=true;
            internal string LastRefusal="test transfer refusal";
            internal bool? ImmediateResult;internal int Attempts;internal Player Taker;internal Func<bool> Current;internal Action<bool> Callback;
            internal bool TryTake(Player player,Func<bool> current,Action<bool> done)
            {
                Attempts++;Taker=player;Current=current;Callback=done;
                if(!StartResult)return false;
                IsPending=true;if(ImmediateResult.HasValue)Settle(ImmediateResult.Value);return true;
            }
            internal void Settle(bool success,bool ambiguous=false)
            {IsPending=false;if(success||ambiguous)IsAvailable=false;Callback?.Invoke(success);}
            public void Dispose(){Disposed=true;IsAvailable=false;}
        }
    }
}
