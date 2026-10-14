using EFT;
using UnityEngine;
using UnityEngine.AI;

// No renderer, animator or pooled-model API: Crowd must never acquire those owners.
namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static Vector3 zero => default;
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 down => new(0, -1, 0);
        public float sqrMagnitude => x*x + y*y + z*z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
    }
    public static class Mathf { public static float Abs(float x) => MathF.Abs(x); }
    public static class Time { public static float time = 100; }
    public enum QueryTriggerInteraction { Ignore }
    public class Collider { public object Owner; public T GetComponentInParent<T>() where T:class => Owner as T; }
    public struct RaycastHit { public Vector3 point, normal; }
    public static class Physics
    {
        public static bool Floor = true; public static float HeightError, NormalY = 1;
        public static int Calls; public static Vector3 Checked;
        public static Collider[] Contacts = [];
        public static bool Raycast(Vector3 origin, Vector3 down, out RaycastHit hit, float length, int mask, QueryTriggerInteraction triggers)
        { Calls++; Checked = origin - Vector3.up*.25f; hit=new(){ point=Checked+Vector3.up*HeightError,normal=new(0,NormalY,0)};return Floor; }
        public static int OverlapCapsuleNonAlloc(Vector3 bottom, Vector3 top, float radius, Collider[] result,int mask,QueryTriggerInteraction triggers)
        { int count=Math.Min(result.Length,Contacts.Length);Array.Copy(Contacts,result,count);return count; }
    }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial }
    public class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners; }
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas=-1;
        public static bool SampleOk=true, PathOk=true, Complete=true;
        public static Vector3 Delta, LastCandidate, LastSample;
        public static int Samples;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float distance,int mask)
        { Samples++;LastCandidate=p;LastSample=p+Delta;hit=new(){position=LastSample};return SampleOk; }
        public static bool CalculatePath(Vector3 from,Vector3 to,int mask,NavMeshPath path)
        {path.status=Complete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;path.corners=[from,to];return PathOk;}
    }
}
namespace EFT
{
    public enum EBotState { Active, PreActive }
    public enum WildSpawnType { infectedAssault, infectedCivil, infectedLaborant, infectedPmc, pmcBEAR }
    public enum BotDifficulty { easy, normal, hard }
    public class Health { public bool IsAlive=true; }
    public class BotSettings { public WildSpawnType Role=WildSpawnType.infectedAssault; public BotDifficulty BotDifficulty=BotDifficulty.normal; }
    public class Info { public BotSettings Settings=new(); }
    public class Profile { public Info Info=new(); }
    public class Movement { public Vector3 LookDirection=new(0,0,1); }
    public class AIData { public BotOwner BotOwner; }
    public class Player
    {
        static int next; public readonly int Id=++next;
        public string ProfileId => Id.ToString();
        public bool IsAI; public Vector3 Position;
        public Health HealthController=new();public Movement MovementContext=new();public Profile Profile=new();public AIData AIData=new();
        public int GetInstanceID()=>Id;
    }
    public class GameWorld
    {
        public Player MainPlayer; public List<Player> AllAlivePlayersList=[];
        public Player GetAlivePlayerByProfileID(string id)=>AllAlivePlayersList.FirstOrDefault(p=>p.ProfileId==id);
    }
    public class Follower { public bool HaveBoss; }
    public class Group { public int Searches; public void AddPointToSearch(Vector3 point,float power,BotOwner owner)=>Searches++; }
    public class BotOwner
    {
        public Player GetPlayer; public BotMover Mover;public bool IsDead, Follower;
        public EBotState BotState=EBotState.Active; public Follower BotFollower=new();public Group BotsGroup=new();
        public Vector3 Position=>GetPlayer.Position;public Profile Profile=>GetPlayer.Profile;public bool IsFollower()=>Follower;
    }
    public class Bots { public List<BotOwner> BotOwners=[]; }
    public class BotsController { public Bots Bots=new(); }
    public class IBotGame { public BotsController BotsController=new(); }
}
public class BotMover(Player body)
{
    public int Stops,Teleports;public Vector3 LastTeleport;
    public void Stop()=>Stops++;
    public void Teleport(Vector3 point){Teleports++;LastTeleport=point;body.Position=point;}
}
public class BotLocalAvoidance
{
    public BotOwner _owner;public Vector3 _totalOffset,_notLinkedOffset;public int Drops;
    public void DropOffset(){Drops++;_totalOffset=_notLinkedOffset=Vector3.zero;}
    public void ManualUpdate(){}
}
public enum ELookObstacleType { None, Wall }
public class EnemyPart { public Vector3 Position; }
public class EnemyPartVision
{
    public bool HasLineOfSight;public RaycastHit BotToTargetHit;public ELookObstacleType BotToTargetObstacleType=ELookObstacleType.Wall;
    public void CheckLineOfSight(){}
}
public static class LayersMaskController { public const int PlayerStaticCollisionsMask=17; }
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance;public static bool Instantiated=>Instance!=null; } }
namespace ArenaMode.Config { public class Entry { public bool Value; } public static class Settings { public static Entry ZombieCrowdEnabled=new(); } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Bots { internal static class ZombieNativeEventBridge { public static bool Active=true;public static bool IsActiveZombieRaid()=>Active; } }
namespace ArenaMode.Shared { public static class ArenaAreaCatalog { public const float NavigationInset=.35f; } }
namespace ArenaMode.Areas
{
    internal static partial class ArenaAreaRuntime
    {
        internal static object CurrentArea;internal static bool IsReady=true,OwnerValid=true,Inside=true,Connected=true,Unoccupied=true;
        internal static Vector3 Validated;
        private static bool TryGetForBot(BotOwner bot,out object area){area=CurrentArea;return area!=null&&OwnerValid;}
        private static bool Contains(Vector3 p,float inset)=>Inside;
        private static bool TrySample(Vector3 p,out Vector3 sample)
        {bool ok=NavMesh.SamplePosition(p,out var hit,.75f,-1);sample=hit.position;return ok;}
        private static bool CompleteConnectedPath(Vector3 sample){Validated=sample;return Connected;}
        private static bool IsUnoccupied(Vector3 sample,Player ignore,object reservation)=>Unoccupied;
    }
}
namespace ArenaMode
{
    public class Log {public List<string> Warnings=[];public void LogInfo(string s){} public void LogWarning(string s)=>Warnings.Add(s);}
    public static class Plugin {public static Log ModLogger=new();}
}
namespace HarmonyLib { public static class AccessTools { public static System.Reflection.MethodInfo DeclaredMethod(Type t,string n)=>t.GetMethod(n); } }
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract System.Reflection.MethodBase GetTargetMethod();public void Enable(){} }
    public class PatchPrefix:Attribute {}
}
