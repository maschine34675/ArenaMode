using System.Reflection;
namespace UnityEngine { public struct Vector3 {} }
namespace UnityEngine.AI { public enum NavMeshPathStatus {PathComplete,PathPartial,PathInvalid} }
namespace Comfort.Common
{
    public static class Singleton<T> where T:class
    {public static T Instance; public static bool Instantiated=>Instance!=null;}
}
namespace EFT
{
    public enum GameStatus {Running,Started,Stopping,Stopped}
    public class AbstractGame {}
    public interface IBotGame { BotsController BotsController {get;} }
    public class LocalGame:AbstractGame,IBotGame
    {public GameWorld GameWorld; public GameStatus Status=GameStatus.Started;public BotsController BotsController {get;set;}=new();}
    public class GameWorld
    {public string LocationId="arena_bay5";public Player Alive;public Player GetAlivePlayerByProfileID(string id)=>Alive?.ProfileId==id?Alive:null;}
    public class BotsController { public AICoversData CoversData=new(); }
    public class Player
    {public string ProfileId="companion";public bool IsAI=true;public GameWorld GameWorld;public Health HealthController=new();public AI AIData=new();}
    public class Health {public bool IsAlive=true;}
    public class AI {public BotOwner BotOwner;}
    public class BotOwner
    {public Player GetPlayer;public BotsController BotsController;public BotCoverSearchInfo CoverSearchInfo;public BotsGroup BotsGroup=new();}
}
public class AICoversData {public List<object> Points=new();}
public class BotsGroup {public CoverPointMaster CoverPointMaster=new();}
public class BotCoverSearchInfo {public EFT.BotOwner _owner;}
public class CoverSearchData {public object Bot;}
public class CustomNavigationPoint {}
public class CoverPointMaster
{public CustomNavigationPoint GetCoverPointMain2(CoverSearchData data)=>throw new InvalidOperationException("unsafe graph entered");}
public class BotPathFinderCorePoints
{
    public EFT.BotOwner _owner;public AICoversData _covers;
    public UnityEngine.AI.NavMeshPathStatus GoToPosition(UnityEngine.Vector3 target,bool slow,float reach,bool getUp,bool mustHaveWay,bool onlyShortTrie=false,bool force=false,bool slowCalcUsingNativeNavMesh=false)=>UnityEngine.AI.NavMeshPathStatus.PathInvalid;
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {public static bool IsActive=true,Exact=true;public static string ActiveMode="zombie";public static bool IsExactActiveRaid(EFT.AbstractGame g,EFT.GameWorld w)=>Exact;}
}
namespace ArenaMode.Compat
{
    public static class PitFireTeamCompat
    {public static bool TeamArenaActive=true,IsFinalizing,Member=true;public static bool IsTeamMember(EFT.BotOwner bot)=>Member;}
}
namespace ArenaMode
{
    public static class Plugin {public static Logger ModLogger=new();}
    public class Logger {public int Lines;public void LogInfo(string s)=>Lines++;}
}
namespace HarmonyLib
{
    public static class Priority {public const int Last=0;}
    public class HarmonyMethod(Type type,string name)
    {public int priority=400;public MethodInfo Method=type.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);}
    public class Harmony
    {public List<(MethodInfo Target,MethodInfo Prefix,int Priority)> Hooks=new();public void Patch(MethodInfo method,HarmonyMethod prefix=null)=>Hooks.Add((method,prefix.Method,prefix.priority));}
}
