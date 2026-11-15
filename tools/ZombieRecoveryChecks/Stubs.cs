using EFT;
using UnityEngine;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 up => new(0,1,0);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float f) => new(a.x*f,a.y*f,a.z*f);
    }
    public static class Time { public static float time, timeScale=1; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static bool Occluded=true; public static int Casts;
        public static Func<Vector3,Vector3,bool> Cast;
        public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction trigger)
        { Casts++; return Cast?.Invoke(a,b) ?? Occluded; }
    }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status; }
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas=-1; public static int Calls; public static bool Reachable, Sample=true;
        public static Func<Vector3,Vector3,bool> Path;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float r,int areas) { hit=new(){position=p};return Sample; }
        public static bool CalculatePath(Vector3 a,Vector3 b,int areas,NavMeshPath path)
        { Calls++;bool result=Path?.Invoke(a,b) ?? Reachable;path.status=result?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return result; }
    }
}
public static class LayersMaskController { public static int PlayerStaticCollisionsMask=17; }
public enum EBotState { PreActive, Active, NonActive }
public enum EBotMoverState { Default, NearDoor }
public enum BotStandByType { none, paused, goToSave, active }
namespace Comfort.Common
{
    public static class Singleton<T>
    {
        private static T _instance;
        public static T Instance { get => _instance ?? throw new InvalidOperationException("uninstantiated singleton"); set => _instance=value; }
        public static bool Instantiated => _instance != null;
    }
}
namespace EFT
{
    public enum WildSpawnType { infectedAssault, infectedPmc, infectedCivil, infectedLaborant, pmcUSEC, infectedTagilla }
    public enum BotDifficulty { easy, normal, hard }
    public enum EDamageType { Undefined }
    public enum GameStatus { Started, Stopping, Stopped }
    public static class Roles { public static bool IsInfected(this WildSpawnType v) => v != WildSpawnType.pmcUSEC; }
    public class Profile { public string Id="bot"; public Info Info=new(); }
    public class Info { public Settings Settings=new(); }
    public class Settings { public WildSpawnType Role; }
    public class Health
    {
        public bool IsAlive=true, RefuseKill, ThrowAfterKill; public int Kills; public Action AfterKill;
        public void Kill(EDamageType type)
        { Kills++; if (!ArenaMode.Combat.ArenaAdministrativeDeathGate.Active) throw new Exception("missing administrative gate");
          if (!RefuseKill) IsAlive=false; AfterKill?.Invoke(); if(ThrowAfterKill)throw new Exception("death observer threw"); }
    }
    public class Movement { public bool IsGrounded=true; public Vector3 MovementDirection=new(1,0,0); }
    public class AI { public BotOwner BotOwner; }
    public class Player
    {
        public Profile Profile=new(); public string ProfileId => Profile.Id;
        public Health HealthController=new(); public Health ActiveHealthController => HealthController;
        public Vector3 Position; public bool IsAI=true, IsYourPlayer; public AI AIData=new(); public Movement MovementContext=new();
    }
    public class Enemy
    {
        public Player Person; public bool IsVisible, CanShoot;
        public float PersonalLastSeenTime=-1000, PersonalLastShootTime=-1000;
    }
    public class Memory
    {
        public bool HaveEnemy, IsDamaged, LastDamageDataActive; public float LastTimeHit=-1000; public Enemy GoalEnemy;
    }
    public class Shoot { public bool Shooting; public float LastTriggerPressd=-1000; }
    public class Melee { public float LastTimeEnemyHit=-1000; }
    public class Weapon { public Melee Melee=new(); }
    public class Mover
    {
        public bool IsMoving=true, HasPathAndNoComplete=true, Pause; public float RemainPause;
        public EBotMoverState CurrentState; public Vector3? TargetPoint;
    }
    public class Door { public bool Interacting, _enteringDoorSequence, _shallStartInteract; }
    public class Follower { public bool HaveBoss; }
    public class Standby { public BotStandByType StandByType; }
    public class Group { public int Searches; public void AddPointToSearch(Vector3 p,float n,BotOwner bot) => Searches++; }
    public class BotOwner
    {
        public Player GetPlayer=new(); public Profile Profile => GetPlayer.Profile; public string ProfileId => Profile.Id;
        public Health HealthController => GetPlayer?.HealthController; public Vector3 Position => GetPlayer.Position;
        public Memory Memory=new(); public Group BotsGroup=new(); public EBotState BotState=EBotState.Active;
        public bool IsDead, Follows; public bool IsFollower()=>Follows; public Follower BotFollower=new();
        public Shoot ShootData=new(); public Weapon WeaponManager=new(); public Mover Mover=new(); public Door DoorOpener=new();
        public Standby StandBy=new();
    }
    public interface IBotGame { BotsController BotsController { get; } }
    public class AbstractGame { }
    public class LocalGame : AbstractGame,IBotGame
    { public GameStatus Status=GameStatus.Started; public BotsController BotsController { get; }=new(); }
    public class BotsController { public Bots Bots=new(); public Spawner BotSpawner=new(); }
    public class Bots { public List<BotOwner> BotOwners=new(); }
    public class Spawner { public void AddToTargetBackup(BotDifficulty d,WildSpawnType r,int n) { } }
    public class GameWorld
    {
        public Player MainPlayer; public Dictionary<string,Player> Live=new();
        public Player GetAlivePlayerByProfileID(string id) => Live.TryGetValue(id,out var p) ? p:null;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger { public void LogDebug(object s) { } public void LogWarning(object s) { } }
}
namespace ArenaMode.Combat
{
    public sealed class ArenaAdministrativeDeathGate : IDisposable
    { public static bool Active; public static ArenaAdministrativeDeathGate Enter(string s) {Active=true;return new();} public void Dispose()=>Active=false; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool Exact=true; public static bool IsExactActiveRaid(LocalGame g,GameWorld w)=>Exact; }
}
namespace ArenaMode.Respawn
{
    public static class ArenaRespawnDriver { public static bool IsRespawnInProgress, StandDown; }
}
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    public class ZombieMode
    {
        public bool IsTeamArena, IsSpawnAdmissionOpen=true, IsRecoveryAdmissionOpen=true;
        public IReadOnlyList<Player> LiveTeamMembers=Array.Empty<Player>();
    }
}
namespace ArenaMode.Bots
{
    public static class ZombiePathBudgetRuntime { public static int Resets; public static void Reset()=>Resets++; }
    public static class ZombieRoleMix
    {
        public const string InfectedAssault="infectedAssault", InfectedPmc="infectedPmc", InfectedCivil="infectedCivil", InfectedLaborant="infectedLaborant", Easy="easy", Normal="normal", Hard="hard";
    }
    public static class ZombieProfilePlan { public class Entry { public string Role,Difficulty; public int Count; } }
    public static class ArenaHostility { public static void ReportPlayerTo(Group g,Player p) { } }
    public static class ZombieMeleeDiagnostics { public static void OnZombieArrived(BotOwner b) { } }
    public static class BurstSpawnService
    {
        public static Action<BotOwner> Arrival;
        public static bool SpawnInfected(WildSpawnType r,BotDifficulty d,Action<BotOwner> arrival,
            Func<bool> requestCurrent=null,Func<bool> beginNativeDispatch=null,Action<bool> dispatchFinished=null,Action arrivalRejected=null)
        { Arrival=arrival; return true; }
    }
}
