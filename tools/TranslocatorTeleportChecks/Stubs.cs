using ArenaMode.Areas;
using ArenaMode.Combat.Translocator;
using System.Runtime.CompilerServices;

// Native boundaries are doubles. These checks exercise linked admission,
// authorization, readback and extracted cleanup code, not Unity geometry.
namespace UnityEngine
{
    public struct Vector2 { public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;} public float sqrMagnitude=>x*x+y*y; }
    public static class Time {public static int frameCount=1;}
    public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
    public struct Ray {public Vector3 origin,direction;public Ray(Vector3 o,Vector3 d){origin=o;direction=d;}}
    public struct RaycastHit {public Collider collider;public float distance;}
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>default;
        public static Vector3 one=>new(1,1,1);
        public static Vector3 up=>new(0,1,0);
        public static Vector3 down=>new(0,-1,0);
        public static Vector3 right=>new(1,0,0);
        public static Vector3 forward=>new(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public Vector3 normalized=>sqrMagnitude>0?this/MathF.Sqrt(sqrMagnitude):zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object value)=>value is Vector3 v&&this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
        public string ToString(string format)=>$"({x},{y},{z})";
    }
    public struct Quaternion { public static Quaternion identity=>default; }
    public struct Bounds
    {
        public Vector3 center,extents;
        public Bounds(Vector3 c,Vector3 size){center=c;extents=size*.5f;}
        public bool Intersects(Bounds b)=>Math.Abs(center.x-b.center.x)<=extents.x+b.extents.x
            &&Math.Abs(center.y-b.center.y)<=extents.y+b.extents.y&&Math.Abs(center.z-b.center.z)<=extents.z+b.extents.z;
    }
    public class GameObject { public bool activeInHierarchy=true; }
    public class Transform
    {
        public Vector3 position,lossyScale=Vector3.one;
        public Quaternion rotation;
        public Transform parent;
        public Vector3 TransformPoint(Vector3 p)=>position+new Vector3(p.x*lossyScale.x,p.y*lossyScale.y,p.z*lossyScale.z);
        public Vector3 TransformDirection(Vector3 p)=>p;
        public bool IsChildOf(Transform p){for(var t=this;t!=null;t=t.parent)if(ReferenceEquals(t,p))return true;return false;}
    }
    public class Collider
    {
        public bool enabled=true,isTrigger;
        public GameObject gameObject=new();
        public Transform transform=new();
        public Vector3 localCenter,half=new(.3f,.9f,.3f);
        public virtual Bounds bounds=>new(transform.position+localCenter,half*2);
    }
    public class CapsuleCollider:Collider
    {
        public Vector3 center=new(0,.9f,0);
        public float radius=.3f,height=1.8f;
        public int direction=1;
        public override Bounds bounds=>new(transform.TransformPoint(center),new(radius*2,height,radius*2));
    }
    public class SphereCollider:Collider{}
    public class BoxCollider:Collider{}
    public struct LayerMask {public int value;public static implicit operator LayerMask(int i)=>new(){value=i};}
    public enum QueryTriggerInteraction {Ignore}
    public static class Physics
    {
        public static List<Collider> Obstacles=new();
        public static bool Saturated,ThrowQuery;
        public static Action OnSync,OnQuery;
        public static int Queries,Syncs;
        public static Vector3 SyncedBodyPosition;
        public static void Reset(){Obstacles.Clear();Saturated=ThrowQuery=false;OnSync=OnQuery=null;Queries=Syncs=0;SyncedBodyPosition=default;}
        public static void SyncTransforms(){Syncs++;SyncedBodyPosition=ArenaTranslocatorRuntime.Owner?.Position??Vector3.zero;var action=OnSync;OnSync=null;action?.Invoke();}
        public static int OverlapBoxNonAlloc(Vector3 c,Vector3 e,Collider[] results,Quaternion rotation,int mask,QueryTriggerInteraction trigger)
        {
            Queries++;var action=OnQuery;OnQuery=null;action?.Invoke();if(ThrowQuery)throw new InvalidOperationException("injected native query failure");
            if(Saturated)return results.Length;
            var bounds=new Bounds(c,e*2);int count=0;
            foreach(var o in Obstacles)if(bounds.Intersects(o.bounds)&&count<results.Length)results[count++]=o;
            return count;
        }
        public static bool ComputePenetration(Collider a,Vector3 ap,Quaternion ar,Collider b,Vector3 bp,Quaternion br,out Vector3 direction,out float distance)
        {
            var aa=a.bounds;aa.center+=ap-a.transform.position;var bb=b.bounds;bb.center+=bp-b.transform.position;
            direction=Vector3.up;distance=Math.Min(aa.extents.x+bb.extents.x-Math.Abs(aa.center.x-bb.center.x),
                Math.Min(aa.extents.y+bb.extents.y-Math.Abs(aa.center.y-bb.center.y),aa.extents.z+bb.extents.z-Math.Abs(aa.center.z-bb.center.z)));
            return distance>0;
        }
    }
}
public interface ICharacterController {bool isGrounded {get;set;} bool ShouldStickToGround {get;}UnityEngine.Vector3 center{get;}float height{get;}float radius{get;}float skinWidth{get;}}
public class UnknownController:ICharacterController {public bool isGrounded{get;set;}public bool ShouldStickToGround=>true;public UnityEngine.Vector3 center=>default;public float height=>1.8f;public float radius=>.3f;public float skinWidth=>.01f;}
public class SimpleCharacterController:ICharacterController
{
    public UnityEngine.CapsuleCollider _mainCollider;
    public UnityEngine.Collider[] _colliders;
    public UnityEngine.LayerMask _collisionMask=1;
    public bool isGrounded {get;set;}
    public UnityEngine.Vector3 velocity;
    public bool ShouldStickToGround=>true;
    public UnityEngine.Vector3 center=>_mainCollider.center;
    public float height=>_mainCollider.height;
    public float radius=>_mainCollider.radius;
    public float skinWidth=>.01f;
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance;public static bool Instantiated=>Instance!=null;} }
namespace EFT
{
    using UnityEngine;
    public class AbstractGame{}
    public class GameWorld {public Player MainPlayer;public List<Player> AllAlivePlayersList=new();}
    public class Health {public bool IsAlive=true;}
    public class PlayerBones {public HashSet<Collider> BodyPartCollidersHashSet=new();}
    public class AIData {public BotOwner BotOwner;}
    public class BotOwner {public object Mover;}
    public class Impostor {public bool UseAsyncMoveValidation;}
    public partial class MovementContext
    {
        public Player Player;
        private Player _player=>Player;
        public ICharacterController CharacterController=>Player.CharacterController;
        public Transform PlayerTransform=>Player.Transform.Original;
        public Vector3 TransformPosition=>Player.Position;
        public Vector3 Slope;
        public float PoseLevel=1;
        public int JumpRequests;
        public void TryJump(){JumpRequests++;}
        public float CheckGroundedRayDistance=.07f;
        public RaycastHit _groundHit;
        public int ContactUpdates;
        public Func<Vector3,bool> SupportedAt=_=>false;
        public Action AfterContact,BeforeGroundQuery;
        public bool GroundQueryThrows;
        public Vector3 LastProbeOrigin;
        public bool GroundCollisionRaycast(Ray ray,float ground,float additional,float dt,out RaycastHit hit){LastProbeOrigin=ray.origin;BeforeGroundQuery?.Invoke();if(GroundQueryThrows)throw new Exception("injected ground query failure");bool support=SupportedAt(Physics.SyncedBodyPosition);hit=support?new(){collider=new BoxCollider(),distance=ground-.01f}:default;return support;}
        public Vector3 TransformVector(Vector3 v)=>v;
        public MovementState CurrentState;
        public object OverridenState,OverridenControlsState,Platform;
        public bool IsInPronePose,IsInMountedState,IsStationaryWeaponInHands,IsAxesIgnored,Vaulting,IsGrounded,_hasLastHeight;
        public int BlindFire;
        public Vector3 PlatformMotion;
        public Impostor ImpostorCC;
        public float FreefallTime,_lastPlayerHeight;
    }
    public static class ContextExtensions {public static bool PlayerAnimatorGetIsVaulting(this MovementContext c)=>c.Vaulting;}
    public class MovementState {public static float G_y=9.81f;public static Vector3 G=new(0,-9.81f,0);public Vector3? Destination;public MovementContext MovementContext;public virtual void Jump(){}protected void ChangePose(float change){MovementContext.PoseLevel+=change;}}
    public partial class IdlePlayerState:MovementState{}
    public class MovePlayerState:MovementState{}
    public class SprintPlayerState:MovementState{}
    public class JumpPlayerState:MovementState
    {
        public enum EJumpState {Jump,Bumbped,PushingFromTheGround}
        public EJumpState _jumpState=EJumpState.Jump;
        public float _potentialH,_lastPlayerHeight,_stateTime,_currentMagnitude;
        public bool _hasLastHeight;
        public Vector3 _currentLiftforce;
    }
    public class FallDown:JumpPlayerState{}
    public class UnknownState:MovementState{}
    public class Player
    {
        public Transform transform=new();
        public NativeTransform Transform=>new(){Original=transform};
        public class NativeTransform {public Transform Original;}
        public MovementContext MovementContext=new();
        public MovementState CurrentManagedState;
        public ICharacterController CharacterController;
        public Health HealthController=new();
        public PlayerBones PlayerBones=new();
        public AIData AIData;
        public ArenaMode.Movement.ArenaSlideController Slide;
        public Vector3 Position=>transform.position;
        public int Teleports;
        public bool Refuse,SkipPrefix,ThrowBefore,ThrowAfter;
        public Vector3? NativeTarget;
        public Action BeforePrefix,AfterTeleport;
        public T GetComponent<T>() where T:class=>Slide as T;
        public void Teleport(Vector3 p,bool server=false)
        {
            if(NativeTarget.HasValue)p=NativeTarget.Value;
            BeforePrefix?.Invoke();
            if(ThrowBefore)throw new Exception("injected native pre-move failure");
            bool admitted=SkipPrefix||ArenaAreaNavigationPatches.RunPrefix(this,p);
            if(!Refuse&&admitted){Teleports++;transform.position=p;}
            AfterTeleport?.Invoke();
            if(ThrowAfter)throw new Exception("injected native post-move failure");
        }
    }
}
namespace ArenaMode
{
    public static class Plugin {public static Log ModLogger=new();public class Log{public int Warnings;public void LogWarning(string value){Warnings++;}}}
}
namespace ArenaMode.Areas
{
    using EFT;using UnityEngine;using ArenaMode.Shared;
    internal static class ArenaAreaRuntime
    {
        internal static ArenaAreaDefinition CurrentArea;
        internal static bool IsReady=true,OrdinaryTeleportAllowed;
        internal static bool TryGetForPlayer(Player p,out object area){area=CurrentArea;return area!=null;}
        internal static bool AllowTeleport(Player p,Vector3 t)=>OrdinaryTeleportAllowed;
        internal static void NoteRejectedTeleport(Player p,string why){}
    }
    internal static partial class ArenaAreaNavigationPatches
    {
        internal static bool IsReady=true;
        internal static bool RunPrefix(Player p,Vector3 t)=>BeforePlayerTeleport(p,t);
        private static bool Scoped(BotOwner owner)=>false;
        private static void StopUnsafePaths(BotOwner owner,object mover){}
    }
}
namespace ArenaMode.Combat.Translocator
{
    using EFT;using UnityEngine;using Comfort.Common;
    internal static class ArenaTranslocatorRuntime
    {
        internal static Player Owner;
        internal static GameWorld World;
        internal static AbstractGame Game;
        internal static Vector3 Destination;
        internal static bool Enabled=true,Owns=true;
        internal static bool IsLivePlayer(Player p)=>Enabled&&p!=null&&ReferenceEquals(p,Owner)&&p.HealthController.IsAlive
            &&ReferenceEquals(World,Singleton<GameWorld>.Instance)&&ReferenceEquals(Game,Singleton<AbstractGame>.Instance)
            &&ReferenceEquals(World?.MainPlayer,p);
        internal static bool IsOwnedDestination(Player p,Vector3 t)=>Owns&&IsLivePlayer(p)&&(t-Destination).sqrMagnitude<=.0001f;
    }
}
namespace ArenaMode.Movement
{
    using EFT;using UnityEngine;using Comfort.Common;
    internal static partial class ArenaJumpReliability
    {
        private class Contact {internal Player Player;}
        private class Sample {internal Contact Owner;}
        private static Contact _contact;private static Sample _sample;
        internal static int Epoch;
        private static void Reset(){_contact=null;_sample=null;Epoch++;}
        internal static void Seed(Player p){_contact=new(){Player=p};_sample=new(){Owner=_contact};}
        internal static bool Pending=>_contact!=null||_sample!=null;
    }
    internal static partial class ArenaBunnyhop
    {
        private class Flight {
            internal Player Player;internal GameWorld World;internal AbstractGame Game;internal JumpPlayerState Jump;
            internal Vector3 Velocity,Carry;internal float BaseSpeed,Began,SampleAt,LaunchY,LandedAt=-1,BufferedAt=-1,DispatchAt=-1,ContactAt=-1;
            internal MovementState SignalOwner;internal bool LeftGround,HasCarry,Sampling,SawContact;internal int Hops;
        }
        private static Flight _flight;
        private static void Reset(){_flight=null;}
        private static void ReleaseSignal(Flight f){f.SignalOwner=null;}
        private static bool Owns(Flight f,MovementContext c)=>f!=null&&ReferenceEquals(f.Player.MovementContext,c)&&ReferenceEquals(c.CurrentState,f.Jump);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        internal static void Seed(Player p){_flight=new(){Player=p,World=Singleton<GameWorld>.Instance,Game=Singleton<AbstractGame>.Instance,
            Jump=p.CurrentManagedState as JumpPlayerState,Carry=new(8,0,0),Velocity=new(8,-2,0),HasCarry=true,BaseSpeed=7,
            LaunchY=1,LeftGround=true,Hops=2,BufferedAt=1,DispatchAt=1,ContactAt=1,Sampling=true,SawContact=true};}
        internal static bool Pending=>_flight!=null&&(_flight.BufferedAt>=0||_flight.DispatchAt>=0||_flight.ContactAt>=0||_flight.Sampling||_flight.SawContact);
        internal static float LaunchY=>_flight?.LaunchY??float.NaN;
        internal static object Witness=>_flight;
    }
    internal static class ArenaMovementPatches {internal static float MotionScale(MovementContext c)=>1;}
    internal static class ArenaMovementScope {internal static bool IsLocal(Player p,bool requireInput=false)=>ArenaTranslocatorRuntime.IsLivePlayer(p);}
    internal static class Settings {internal class Entry{internal float Value=90;}internal static Entry AirControlDegreesPerSecond=new();}
    internal static partial class ArenaDoubleJump
    {
        private class Credit {internal Player Player;internal MovementContext Context;internal int StateRevision;internal bool Spent,Airborne,NativeRequest=true;}
        private class Request {internal Player Player;}
        private static Credit _credit;private static Request _slideRequest,_command;private static int _inputEpoch;
        private static ConditionalWeakTable<MovementContext,Credit> _credits=new();
        private static void CancelNativeRequest(Credit c){c.NativeRequest=false;}
        internal static void Seed(Player p,bool spent){_credit=new(){Player=p,Context=p.MovementContext,Spent=spent,Airborne=true};_credits.Remove(p.MovementContext);_credits.Add(p.MovementContext,_credit);_slideRequest=new(){Player=p};_command=new(){Player=p};}
        internal static bool Spent=>_credit.Spent;
        internal static bool Airborne=>_credit.Airborne;
        internal static bool Pending=>_credit.NativeRequest||_slideRequest!=null||_command!=null;
    }
    public class ArenaSlideController {public Player Player;public MovementContext Context;public bool IsSliding,Pending=true,enabled=true;public void Clear(){Pending=false;IsSliding=false;}}
    internal static partial class ArenaSlideTransitions
    {
        private static ArenaSlideController _slide;private static long _epoch;private static bool _sampling,_contact;
        internal static void Seed(Player p){_slide=p.Slide=new(){Player=p,Context=p.MovementContext};_sampling=_contact=true;}
        internal static bool Pending=>_slide.Pending||_sampling||_contact;
    }
    internal static partial class ArenaAirControl {
        private class Flight{internal Player Player;internal Vector3 Heading;internal bool HasHeading;internal Vector2 Input;internal MovementState InputState;internal int InputFrame;}
        private static ConditionalWeakTable<MovementContext,Flight> _flights=new();
        internal static void Seed(Player p){_flights.Remove(p.MovementContext);_flights.Add(p.MovementContext,new(){Player=p,Heading=new(.6f,0,.8f),HasHeading=true,Input=new(1,0),InputState=p.CurrentManagedState,InputFrame=1});}
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static bool Eligible(MovementState state,out Player player){player=state.MovementContext.Player;return state is JumpPlayerState&&ArenaTranslocatorRuntime.IsLivePlayer(player);}
        private static Flight Observe(Player p){if(!_flights.TryGetValue(p.MovementContext,out var f)){f=new(){Player=p};_flights.Add(p.MovementContext,f);}return f;}
        internal static bool Pending(Player p)=>_flights.TryGetValue(p.MovementContext,out var f)&&f.InputFrame>=0;
        internal static Vector3 Applied(Player p){int old=Time.frameCount;Time.frameCount=2;try{var motion=new Vector3(.05f,-.1f,0);ArenaBunnyhop.ApplyCarry(p.CurrentManagedState,ref motion,.05f);Steer(p.CurrentManagedState,ref motion,.05f);return motion;}finally{Time.frameCount=old;}}
    }
    internal static partial class ArenaAirVault
    {
        private class Request{internal Player Player;}
        private class Pending{internal Request Request;}
        private class AnimatorFrame{internal Pending Pending;}
        private class Observation{internal Request Request;}
        private static Request _request;private static Pending _pending;private static AnimatorFrame _animatorFrame;private static Observation _motionObservation;private static Player _blockedUntilRelease;
        private static void CancelPending(){_pending=null;}
        internal static void Seed(Player p,bool active=false){_request=active?new(){Player=p}:null;_pending=null;_animatorFrame=new(){Pending=new(){Request=new(){Player=p}}};_motionObservation=new(){Request=new(){Player=p}};_blockedUntilRelease=p;}
        internal static bool Observing=>_animatorFrame!=null||_motionObservation!=null||_blockedUntilRelease!=null;
    }
}
