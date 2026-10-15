using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a,b)
            || ReferenceEquals(a,null) && !ReferenceEquals(b,null) && b.Destroyed
            || ReferenceEquals(b,null) && !ReferenceEquals(a,null) && a.Destroyed;
        public static bool operator !=(Object a,Object b) => !(a==b);
        public override bool Equals(object o) => ReferenceEquals(this,o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public struct Vector3(float x,float y,float z)
    {
        public float x=x,y=y,z=z;
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T:class
    { public static T Instance; public static bool Instantiated=>Instance!=null; }
}
namespace EFT
{
    public enum GameStatus { Stopped, Running, Started, Stopping, SoftStopping }
    public class AbstractGame { }
    public interface IBotGame { BotsController BotsController {get;} }
    public class LocalGame:AbstractGame,IBotGame
    {
        public GameWorld GameWorld;
        public Profile Profile=new(){Id="local"};
        public GameStatus Status=GameStatus.Running;
        public BotsController BotsController {get;set;} =new();
    }
    public class Player : UnityEngine.Object { public string ProfileId="local"; public bool IsYourPlayer=true; public GameWorld GameWorld; }
    public class Profile {public string Id; public string AccountId; public string ProfileId=>Id;}
    public class GameWorld { public string LocationId="arena_bay5"; public Player MainPlayer=new(); }
    public class BotOwner { public Profile Profile;public Player GetPlayer;public int Retired;}
}
namespace ArenaMode.Modes { internal static class ArenaMatchService { internal static object Current; } }
public class BotsController {public AICoversData CoversData=new();public BotSpawner BotSpawner=new();}
public class BotSpawner {public BotCreatorClient _botCreator;public int _inSpawnProcess;}
public class BotCreatorClient
{
    public EFT.LocalGame _game;
    public Task CreateBot(EFT.Profile profile,PositionNote position,Action<EFT.BotOwner> callback,bool local,CancellationToken token)=>Task.CompletedTask;
}
public class BotZone {}
public class BotsGroup {}
public class PositionNote(UnityEngine.Vector3 position,int core,bool used)
{public UnityEngine.Vector3 Position=position;public int CorePointId=core;}
public class AICoversData
{
    public List<GroupPoint> Points=new();
    public AICorePointHolder AICorePointsHolder=new();
}
public class GroupPoint { }
public class AICorePointHolder
{
    public List<AICorePoint> CorePoints=new();
    public AICorePoint GetCorePoint(int id)=>CorePoints.FirstOrDefault(x=>x.Id==id);
}
public class AICorePoint:UnityEngine.Object
{public int Id; public UnityEngine.Vector3 Position;}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive,Exact=true,ArmedForNextRaid=true;
        public static int ArmEpoch=5;
        public static string ArmedMap="arena_bay5",ArmedMode="tactical",ActiveMode="tactical";
        public static bool IsExactActiveRaid(EFT.AbstractGame g,EFT.GameWorld w)=>Exact;
    }
}
namespace ArenaMode.Compat
{
    internal static partial class PitFireTeamCompat
    {
        internal static readonly object StateLock=new();
        internal static EFT.Player _currentLocalPlayer;
        internal static string _currentLocalProfileId;
        internal static Dictionary<string,EFT.BotOwner> CurrentBodiesByAccountId=new();
        public static bool TeamArenaActive=true,IsFinalizing,RoundTransitionActive;
        public static Dictionary<string,string> CanonicalCompanionProfilesByAccountId=new();
    }
    public static class PitFireTeamRoundLifecycle
    {
        public static EFT.Profile Runtime;
        public static int Rejected;
        public static bool IsCurrentBoundedActivation(EFT.Profile p,BotCreatorClient c)=>ReferenceEquals(p,Runtime);
        public static bool RejectBoundedActivation(EFT.Profile p,EFT.BotOwner body,string reason)
        {
            if(!ReferenceEquals(p,Runtime))return false;
            Rejected++;if(body!=null)body.Retired++;return true;
        }
        public static void RetireBoundedFollower(EFT.BotOwner body,BotsController c,EFT.GameWorld w)=>body.Retired++;
    }
}
namespace ArenaMode.Areas
{
    public static class ArenaAreaRuntime
    {
        public static bool Block,IsReady,HavePoint=true,Accept=true;
        public static object CurrentArea;
        public static int Reservations,Disposals;
        public static bool ShouldBlockLoadingBotDispatch()=>Block;
        public class TeamSpawnLease
        {
            bool disposed;
            public bool IsCurrent=>!disposed&&ArenaAreaRuntime.IsReady;
            public bool Accept(EFT.Player p)=>!disposed&&ArenaAreaRuntime.Accept&&IsReady;
            public void Dispose(){if(!disposed){disposed=true;Disposals++;}}
        }
        public class Point {public UnityEngine.Vector3 Position=new(10,0,3);public int CorePointId=77;}
        public static bool TryReserveCompanionSpawn(out BotZone zone,out Point point,out TeamSpawnLease lease)
        {
            zone=null;point=null;lease=null;
            if(!IsReady||!HavePoint)return false;
            Reservations++;zone=new();point=new();lease=new();return true;
        }
    }
}
namespace ArenaMode
{
    public static class Plugin {public static Logger ModLogger=new();}
    public class Logger {public void LogInfo(string message) { } public void LogDebug(string message) { } public void LogWarning(string s){} public void LogError(string s){} }
}
namespace HarmonyLib
{
    public static class Priority {public const int Last=0;}
    public class HarmonyMethod(Type t,string n) {public MethodInfo Method=t.GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static);public int priority;}
    public class Harmony
    {
        public MethodInfo Target,Prefix;
        public void Patch(MethodInfo target,HarmonyMethod prefix=null,HarmonyMethod postfix=null) {Target=target;Prefix=prefix?.Method;}
    }
}
namespace pitTeam.Patches
{
    public class BotsControllerPatch
    {
        public static BotsController Controller;
        public static List<pitTeam.Components.pitAIBossPlayer> spawnedPlayers=new();
        private AICorePoint GetClosestCorePoint(BotsController controller,UnityEngine.Vector3 position)=>throw new InvalidOperationException("native sentinel");
        private Task<bool> ActivateBotAtPosition(BotCreatorClient c,EFT.Profile p,PositionNote position,BotZone zone,
            bool group,Func<EFT.BotOwner,BotZone,BotsGroup> action,Action<EFT.BotOwner> callback,CancellationToken token)=>Task.FromResult(true);
    }
    public static class BotsEventsControllerSpawnPatch
    {public static int Calls; public static void SpawnFollowers()=>Calls++;}
}
namespace pitTeam.Components
{public class pitAIBossPlayer {public EFT.Player realPlayer; public BotsController _botsController;}}
namespace pitTeam.Modules
{public static class BossPlayers {public static pitTeam.Components.pitAIBossPlayer Boss;public static pitTeam.Components.pitAIBossPlayer GetBoss(string id)=>Boss;}}
