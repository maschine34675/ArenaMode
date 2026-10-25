// Engine and external lifecycle seams only. Spawn decisions, reservations,
// header validation and loading-failure ownership link the production runtime.
using System.Reflection;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero=>new(0,0,0); public static Vector3 up=>new(0,1,0); public static Vector3 down=>new(0,-1,0);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static bool operator ==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.0000001f;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object o)=>o is Vector3 v&&this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
        public override string ToString()=>$"({x},{y},{z})";
    }
    public struct Quaternion
    {
        public float yaw; public static Quaternion Euler(float x,float y,float z)=>new(){yaw=y};
        public static float Angle(Quaternion a,Quaternion b)=>MathF.Abs(a.yaw-b.yaw);
    }
    public class Transform { public Vector3 position; public Quaternion rotation; }
    public class Collider { public bool enabled=true,isTrigger; public Player Owner; public T GetComponentInParent<T>() where T:class=>Owner as T; }
    public class SphereCollider:Collider { public Vector3 center; public float radius; }
    public struct RaycastHit { public Vector3 point,normal; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static Func<Vector3,bool> Floor=p=>true,Clear=p=>true;
        public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float length,int mask,QueryTriggerInteraction query)
        { var p=origin-Vector3.up*.25f;hit=new(){point=p,normal=Vector3.up};return Floor(p); }
        public static Collider[] OverlapCapsule(Vector3 bottom,Vector3 top,float radius,int mask,QueryTriggerInteraction query)
            =>Clear(bottom-Vector3.up*.45f)?Array.Empty<Collider>():new[]{new Collider()};
        public static void SyncTransforms() { }
    }
    public static class Mathf { public static float Abs(float x)=>MathF.Abs(x); public static float Min(float a,float b)=>MathF.Min(a,b); }
    public static class Random { public static int Range(int a,int b)=>a; }
    public static class Time { public static float realtimeSinceStartup, time; }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete,PathPartial,PathInvalid }
    public struct NavMeshHit { public Vector3 position; }
    public class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners; }
    public static class NavMesh
    {
        public const int AllAreas=-1;
        public static Func<Vector3,bool> Sample=p=>true;
        public static Func<Vector3,Vector3,bool> Path=(a,b)=>true;
        public static Func<Vector3,Vector3,NavMeshPathStatus?> Status;
        public static Func<Vector3,Vector3,Vector3[]> Corners;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,int mask) {hit=new(){position=p};return Sample(p);}
        public static bool CalculatePath(Vector3 a,Vector3 b,int mask,NavMeshPath path)
        {bool ok=Path(a,b);path.status=Status?.Invoke(a,b)??(ok?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial);path.corners=Corners?.Invoke(a,b)??new[]{a,b};return true;}
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class {public static T Instance;public static bool Instantiated=>Instance!=null;} }
namespace EFT
{
    public enum EPlayerSide {Usec=1,Bear=2,Savage=4}
    [Flags] public enum EPlayerSideMask {Usec=1,Bear=2,Savage=4,All=7}
    public enum GameStatus {Running,Started,Stopping,Stopped}
    public enum ExitStatus { Left }
    public class Profile { public string Id; public ProfileInfo Info=new(); }
    public class ProfileInfo { public EPlayerSide Side; }
    public class Health {public bool IsAlive=true;}
    public class Player {public Profile Profile=new();public string ProfileId=>Profile.Id; public Vector3 Position; public Health HealthController=new();}
    public class GameWorld
    {
        public string LocationId; public Player MainPlayer; public List<Player> AllAlivePlayersList=new();
        public Player GetAlivePlayerByProfileID(string id)=>AllAlivePlayersList.FirstOrDefault(p=>p.ProfileId==id&&p.HealthController.IsAlive);
    }
    public class AbstractGame { }
    public class EftGamePlayerOwner { }
    public class BaseLocalGame<T>:AbstractGame {public Task Run(BotControllerSettings settings,string backend,EFT.InventoryLogic.InventoryController inventory)=>Task.CompletedTask;}
    public class LocalGame:BaseLocalGame<EftGamePlayerOwner> {public GameWorld GameWorld; public GameStatus Status;public Profile Profile=new(); public RaidSettings Settings=new();public void Stop(string profileId,ExitStatus status,string exitName,float delay){} }
    public class RaidSettings {public string location,serverId;}
    public class BotControllerSettings { }
    public static class InGameStatus {public static bool InRaid;}
}
namespace EFT.InventoryLogic {public class InventoryController {}}
namespace EFT.Interactive
{
    public class ExitTriggerSettings {public string Name;}
    public class ExfiltrationPoint
    {
        public ExitTriggerSettings Settings=new();public List<Player> Entered=new();public int Proceeds;
        public void Proceed(Player player,bool manuallyStarted=false){Proceeds++;}
    }
}
namespace CommonAssets.Scripts.Game
{
    public class ExfiltrationController
    {public static ExfiltrationController Instance;public EFT.Interactive.ExfiltrationPoint[] ExfiltrationPoints;}
}
namespace EFT.Bots {public class NamespaceMarker {}}
public static class LayersMaskController {public static int PlayerStaticCollisionsMask=1;}
public class BotZone
{
    public string name; public List<SpawnPointMarker> SpawnPointMarkers=new();
    public ISpawnPoint[] SpawnPoints=>SpawnPointMarkers.Select(m=>(ISpawnPoint)m.SpawnPoint).ToArray();
}
public static class FindObjectsProxy {public static List<BotZone> Zones=new();public static T[] FindUnityObjectsOfType<T>()=>Zones.OfType<T>().ToArray();}
namespace EFT.Game.Spawning
{
    [Flags] public enum ESpawnCategoryMask {Player=1,Bot=2,BotPmc=64}
    public enum ESpawnCategory {Player=1,Bot=2,Coop=8}
    public class SpawnPointParams { }
    public interface ISpawnPoint
    {
        string Id {get;} string BotZoneName {get;} string Infiltration {get;} int CorePointId {get;} float DelayToCanSpawnSec {get;}
        Vector3 Position {get;} Quaternion Rotation {get;} bool SpawnBlocked {get;}
        ESpawnCategoryMask Categories {get;} EPlayerSideMask Sides {get;}
    }
    public class SpawnPoint:ISpawnPoint
    {
        public string Id {get;set;} public string BotZoneName {get;set;} public string Infiltration {get;set;}
        public int CorePointId {get;set;} public float DelayToCanSpawnSec {get;set;}
        public Vector3 Position {get;set;} public Quaternion Rotation {get;set;} public bool SpawnBlocked {get;set;}
        public ESpawnCategoryMask Categories {get;set;} public EPlayerSideMask Sides {get;set;}
        public SpawnPointMarker Collider;
    }
    public class SpawnPointMarker
    {
        public SpawnPoint SpawnPoint;public BotZone BotZone;public Collider _collider;public SphereCollider Sphere;
        public Transform transform=new(); public T GetComponent<T>() where T:class=>Sphere as T;
    }
    public interface ISpawnPointsCollection { }
    public class SpawnPointsCollection:ISpawnPointsCollection
    {
        public Dictionary<ISpawnPoint,SpawnPointMarker> _dictionary=new();
        public static SpawnPointsCollection CreateFromScene(DateTime? now,SpawnPointParams[] points)=>new();
    }
    public interface ISpawnSystem {ISpawnPoint SelectSpawnPoint(ESpawnCategory category,EPlayerSide side,string profileId);}
    public class SpawnSystem:ISpawnSystem
    {
        public GameWorld _players;public ISpawnPointsCollection _spawnPoints;
        ISpawnPoint ISpawnSystem.SelectSpawnPoint(ESpawnCategory category,EPlayerSide side,string profileId)=>null;
    }
}
namespace HarmonyLib
{
    public static class Priority {public const int Last=0;}
    public class HarmonyPriority:Attribute {public HarmonyPriority(int value){}}
    public class HarmonyAfter:Attribute {public HarmonyAfter(string owner){}}
    public class HarmonyMethod {public HarmonyMethod(Type type,string name){}}
    public class Harmony {public Harmony(string id){} public void Patch(MethodBase target,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null){}public void UnpatchSelf(){}}
    public static class AccessTools {public static MethodInfo Method(Type type,string name,Type[] args=null)=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,args??Type.EmptyTypes,null);public static MethodInfo DeclaredMethod(Type type,string name,Type[] args=null)=>Method(type,name,args);}
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix:Attribute {} public class PatchPostfix:Attribute {}
    public abstract class ModulePatch {public static bool Fail;protected abstract MethodBase GetTargetMethod();public void Enable(){if(Fail||GetTargetMethod()==null)throw new Exception("fixture hook unavailable");}public void Disable(){}}
}
namespace Redeploy.Core.GameAccess {public static class LocalGameAccessor {public static RaidSettings GetLocalRaidSettings(LocalGame game)=>game.Settings;}}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle
    {
        public static event Action<GameWorld> OnRaidStarted;
        public static int Subscribers=>OnRaidStarted?.GetInvocationList().Length??0;
        public static void RaiseStarted(GameWorld world)=>OnRaidStarted?.Invoke(world);
    }
}
namespace Redeploy.Core.Spawning {public interface ISpawnSelector {(Vector3 position,Quaternion rotation) Resolve(LocalGame game);}}
namespace ArenaMode.Respawn
{
    public class ArenaSpawnSelector:Redeploy.Core.Spawning.ISpawnSelector
    { public (Vector3 position,Quaternion rotation) Resolve(LocalGame game)=>ArenaMode.Bots.ArenaTeamBoxRuntime.PickRespawn(game,default); }
}
namespace ArenaMode
{
    public static class Plugin {public const string PluginGuid="com.maschine.ArenaMode"; public static Log ModLogger=new();}
    public class Log {public List<string> Lines=new();public void LogInfo(string s)=>Lines.Add(s); public void LogDebug(string s)=>Lines.Add(s);public void LogError(string s)=>Lines.Add(s);public void LogWarning(string s)=>Lines.Add(s);}
}
namespace BepInEx.Bootstrap
{
    public class PluginInfo {public object Instance;}
    public static class Chainloader {public static Dictionary<string,PluginInfo> PluginInfos=new();}
}
namespace ArenaMode.Modes {public static class ArenaMatchService {public static List<string> Aborts=new();public static void ScheduleFailedStartAbort(string message)=>Aborts.Add(message);}}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static EPlayerSide? SelectedTeam;
        public static EPlayerSide? TeamDeathmatchLocalSide(LocalGame game,GameWorld world)
            => SelectedTeam ?? game?.Profile?.Info?.Side;
        public static EPlayerSide? TeamDeathmatchBodySide(Player player,LocalGame game,GameWorld world)
            => ReferenceEquals(player,world?.MainPlayer) ? TeamDeathmatchLocalSide(game,world) : player?.Profile?.Info?.Side;
        public static bool IsActive,ArmedForNextRaid;public static int ArmEpoch;public static int? Participants;
        public static string ActiveMode,ArmedMode,ArmedMap;public static LocalGame ExactGame;public static GameWorld ExactWorld;
        public static bool IsExactActiveRaid(LocalGame game,GameWorld world)=>ReferenceEquals(game,ExactGame)&&ReferenceEquals(world,ExactWorld);
        public static bool IsExactStartedRaid(LocalGame game,GameWorld world)=>ReferenceEquals(game,ExactGame)&&ReferenceEquals(world,ExactWorld);
        public static int CancelCount;
        public static void ClearArmed(string reason){ArmedForNextRaid=false;ArenaServerClient.PendingArmToken=null;CancelCount++;ArenaMode.Bots.ArenaTeamBoxRuntime.Reset();}
    }
    public static class ArenaServerClient {public static string PendingArmToken;}
    public static class ArenaMenuSelection {public static int ClearCount;public static void ClearCompatibilityState(string reason)=>ClearCount++;}
}
namespace ArenaMode.Shared
{
    public static class Protocol
    {
        public static bool IsLifecycleRequestId(string s)=>Guid.TryParseExact(s,"N",out _);
        public static bool IsNativeStartIdentity(string s,string t)=>s=="start:"+t;
        public static bool HasNativeEndIdentity(string s,string t)=>s=="end:"+t;
    }
}
