using ArenaMode.Combat.Translocator;
using ArenaMode.Shared;
namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x=x,y=y,z=z;
        public static Vector3 zero=>new(0,0,0); public static Vector3 up=>new(0,1,0);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude==0?zero:this/magnitude;
        public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator*(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
        public static Vector3 operator/(Vector3 a,float f)=>new(a.x/f,a.y/f,a.z/f);
    }
    public class Transform { public Vector3 position; }
    public static class Time { public static float time,deltaTime=.016f,timeScale=1f; }
    public class Collider { public bool enabled=true,isTrigger; public object Parent; public T GetComponentInParent<T>() where T:class=>Parent as T; }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 normal; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public const int DefaultRaycastLayers=~4;
        public static Func<Vector3,float,Collider[],int> Overlap=(_,_,_)=>0;
        public static Func<Vector3,float,Vector3,RaycastHit[],float,int> Cast=(_,_,_,_,_)=>0;
        public static int OverlapSphereNonAlloc(Vector3 p,float r,Collider[] hits,int mask,QueryTriggerInteraction q)=>Overlap(p,r,hits);
        public static int SphereCastNonAlloc(Vector3 p,float r,Vector3 d,RaycastHit[] hits,float distance,int mask,QueryTriggerInteraction q)=>Cast(p,r,d,hits,distance);
    }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace EFT.InventoryLogic { public class Item { } }
namespace EFT
{
    public enum GameStatus { Started,Stopped }
    public class AbstractGame { }
    public class LocalGame:AbstractGame { public GameStatus Status=GameStatus.Started; public GameWorld GameWorld; public Player LocalPlayer; }
    public class MovementContext { }
    public class Health { public bool IsAlive=true; }
    public class Inventory { public bool IsChangingWeapon; }
    public class Player
    {
        public bool Destroyed;
        public static bool operator==(Player a,Player b) => (ReferenceEquals(a,null)||a.Destroyed)
            ? ReferenceEquals(b,null)||b.Destroyed : !(ReferenceEquals(b,null)||b.Destroyed)&&ReferenceEquals(a,b);
        public static bool operator!=(Player a,Player b)=>!(a==b);
        public override bool Equals(object other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public class ItemHandsController { public EFT.InventoryLogic.Item Item; }
        public bool IsAI,IsInventoryOpened,IsYourPlayer=true;
        public string ProfileId="local"; public Health HealthController=new(); public MovementContext MovementContext=new();
        public Inventory InventoryController=new(); public object HandsController; public EFT.InventoryLogic.Item Owned;
        public UnityEngine.Vector3 Position,LookDirection=new(1,0,0);
        public UnityEngine.Transform CameraPosition=new(){position=new(0,1.6f,0)};
    }
    public class GameWorld { public Player MainPlayer,Alive; public Player GetAlivePlayerByProfileID(string id)=>Alive?.ProfileId==id?Alive:null; }
}
namespace ArenaMode.Modes
{
    public interface IMode { bool IsComplete {get;} }
    public class Mode:IMode { public bool Done; public bool IsComplete=>Done; public bool AssistScoringOpen=true; }
    public class ArenaMatch { public IMode Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; public static bool TeamStartAbortPending; }
    public class RoundsMode:Mode { public class RoundState {public int Number=1;} public RoundState Round=new(); }
    public class TeamDeathmatchMode:Mode { }
    public class TacticalMode:Mode { public int RoundNumber=1; }
    public class GunGameMode:Mode { public bool HasActiveLadder=true; }
    public class ZombieMode:Mode { public bool IsRecoveryAdmissionOpen=true; }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive=true,Exact=true; public static bool IsExactActiveRaid(EFT.LocalGame g,EFT.GameWorld w)=>Exact; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver {public static bool StandDown,IsRespawnInProgress;} }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService {public static bool IsActive;} }
namespace ArenaMode.Hud { public static class ArenaInputBlocker {public static bool Blocked;} }
namespace ArenaMode.Combat { public static class WeaponSwapService {public static bool IsSwapping;} }
namespace ArenaMode.Pickups { public static class ArenaPickupRuntime {public static bool OwnsCollider(UnityEngine.Collider c)=>false;} }
namespace ArenaMode.Areas { public static class ArenaAreaRuntime {public static bool IsPending,IsReady=true; public static ArenaAreaDefinition CurrentArea;} }
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger {public void LogDebug(string s){} public void LogWarning(string s){} }
}
namespace ArenaMode.Combat.Translocator
{
    internal class Entry {public bool Value=true;}
    internal static class ArenaTranslocatorSettings {internal static Entry Enabled=new();}
    internal static class ArenaTranslocatorSupply
    {
        internal static int Ticks; internal static void Tick(EFT.GameWorld w,EFT.Player p){Ticks++;}
        internal static void ForgetOutsideRaid(EFT.GameWorld w){}
        internal static bool Owns(EFT.Player p,EFT.InventoryLogic.Item i)=>i!=null && ReferenceEquals(p?.Owned,i);
    }
    internal sealed class ArenaTranslocatorView:IDisposable
    {
        internal static bool ThrowTick; internal static int Disposals,Pulses;
        internal void Tick(ArenaTranslocatorDisc d){if(ThrowTick)throw new Exception("view failure");}
        internal void Cue(bool teleport){} internal void Pulse(UnityEngine.Vector3 p){Pulses++;}
        public void Dispose(){Disposals++;}
    }
    internal static class ArenaTranslocatorTeleport
    {
        internal static bool Success=true; internal static int Calls; internal static Action Callback;
        internal static bool TryTeleport(EFT.Player p,UnityEngine.Vector3 target,out string reason)
        {Calls++;reason="blocked";if(!Success)return false;p.Position=target;Callback?.Invoke();return true;}
    }
}
