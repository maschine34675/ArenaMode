using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a; y=b; z=c; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static float Distance(Vector3 a,Vector3 b) => MathF.Sqrt((a-b).sqrMagnitude);
        public override string ToString() => $"({x},{y},{z})";
    }
    public struct Quaternion { }
    public static class Time { public static float time; }
    public static class Mathf
    {
        public static float Abs(float value)=>MathF.Abs(value);
        public static float Min(float a,float b)=>MathF.Min(a,b);
        public static float Max(float a,float b)=>MathF.Max(a,b);
        public static float Sqrt(float a)=>MathF.Sqrt(a);
    }
    public static class Random { public static int Range(int min,int max)=>min; }
}
namespace EFT
{
    public enum EPlayerSide { Usec=1,Bear=2,Savage=4 }
    [Flags] public enum EPlayerSideMask { None=0,Usec=1,Bear=2,Pmc=3,Savage=4,All=7 }
    public interface IPlayer { }
    public interface IPlayersCollection { }
    public class Profile { public string Id; public ProfileInfo Info=new(); }
    public class ProfileInfo { public EPlayerSide Side; }
    public class Health { public bool IsAlive=true; }
    public class Player : IPlayer
    {
        public Vector3 Position; public Profile Profile=new(); public Health HealthController=new();
        public string ProfileId=>Profile.Id;
    }
    public class GameWorld : IPlayersCollection
    {
        public List<IPlayer> AllAlivePlayersList=new(); public Player MainPlayer;
    }
    public class BotSpawner { public List<BotZone> Zones=new(); public IEnumerable<BotZone> SpawnZones(bool snipe)=>Zones?.Where(z=>snipe||!z.SnipeZone); }
    public class BotsController { public BotSpawner BotSpawner=new(); }
    public interface IBotGame { BotsController BotsController {get;} }
    public class LocalGame : IBotGame { public Profile Profile=new(); public GameWorld GameWorld; public Player LocalPlayer=>GameWorld.MainPlayer; public BotsController BotsController {get;}=new(); }
}
namespace EFT.Game.Spawning
{
    [Flags] public enum ESpawnCategoryMask { None=0,Player=1,Bot=2,Boss=4,Coop=8,Group=16,Opposite=32,BotPmc=64,All=71 }
    public enum ESpawnCategory { Player=1,Bot=2 }
    public interface ISpawnPoint
    {
        string Id {get;} Vector3 Position {get;} Quaternion Rotation {get;}
        string BotZoneName {get;} bool SpawnBlocked {get;}
        ESpawnCategoryMask Categories {get;} EPlayerSideMask Sides {get;}
    }
    public class Point : ISpawnPoint
    {
        public string Id {get;set;} public Vector3 Position {get;set;} public Quaternion Rotation {get;set;}
        public string BotZoneName {get;set;} public bool SpawnBlocked {get;set;}
        public ESpawnCategoryMask Categories {get;set;} public EPlayerSideMask Sides {get;set;}
    }
    public interface ISpawnPointsCollection : IEnumerable<ISpawnPoint> { }
    public class PointsCollection : List<ISpawnPoint>, ISpawnPointsCollection { }
    public interface ISpawnSystem
    {
        ISpawnPoint SelectSpawnPoint(ESpawnCategory category,EPlayerSide side,string groupId=null,
            string teamId=null,IPlayer person=null,string infiltration=null,string profileId=null);
    }
    public class SpawnSystem : ISpawnSystem
    {
        public IPlayersCollection _players; public ISpawnPointsCollection _spawnPoints; public int NativeSelections;
        ISpawnPoint ISpawnSystem.SelectSpawnPoint(ESpawnCategory category,EPlayerSide side,string groupId,string teamId,IPlayer person,string infiltration,string profileId)
        {
            ISpawnPoint result=null;
            if (SPT.Reflection.Patching.ModulePatch.Target?.DeclaringType==typeof(SpawnSystem))
            {
                object[] args={this,category,side,profileId,result};
                bool original;
                try { original=(bool)SPT.Reflection.Patching.ModulePatch.Prefix.Invoke(null,args); }
                catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
                if (!original) return (ISpawnPoint)args[4];
            }
            NativeSelections++; return _spawnPoints.FirstOrDefault();
        }
    }
    public static class Extensions
    {
        public static bool ContainBotPmcCategory(this ESpawnCategoryMask value)=>(value&ESpawnCategoryMask.BotPmc)!=0;
    }
}
public class BotZone { public string NameZone; public bool SnipeZone; public ISpawnPoint[] SpawnPoints; }
namespace HarmonyLib
{
    public static class Priority { public const int Last=0; }
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority){} }
}
namespace SPT.Reflection.Patching
{
    public sealed class PatchPrefix : Attribute { }
    public abstract class ModulePatch
    {
        public static MethodBase Target; public static MethodInfo Prefix;
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            Target=GetTargetMethod();
            Prefix=GetType().GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.GetCustomAttribute<PatchPrefix>()!=null);
        }
        public void Disable() { Target=null; Prefix=null; }
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Log ModLogger=new(); }
    public class Log { public List<string> Lines=new(); public void LogInfo(string value)=>Lines.Add(value); public void LogWarning(string value)=>Lines.Add(value); }
}
namespace ArenaMode.Bots
{
    public static class ArenaBotSpawnFilter
    {
        public static HashSet<string> ExcludedIds=new(); public static HashSet<string> ExcludedZones=new();
        public static bool IsExcludedPoint(ISpawnPoint p)=>ExcludedIds.Contains(p.Id);
        public static bool IsExcludedZone(BotZone z)=>z.SnipeZone||ExcludedZones.Contains(z.NameZone);
    }
}
namespace Redeploy.Core.Spawning
{
    public interface ISpawnSelector { (Vector3 position,Quaternion rotation) Resolve(LocalGame game); }
    public static class SpawnSystemCache { public static ISpawnSystem GetOrCreate(LocalGame game)=>null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsExactActiveRaid(LocalGame game,GameWorld world)
            =>ArenaMode.Areas.ArenaAreaRuntime.Active&&ReferenceEquals(game,ArenaMode.Areas.ArenaAreaRuntime.TestGame)
                &&ReferenceEquals(world,ArenaMode.Areas.ArenaAreaRuntime.TestWorld);
    }
}
namespace ArenaMode.Areas
{
    // Unchanged outer lifecycle/navigation are a controlled seam. All new team
    // decisions and initial prefix are linked production; NavMesh is not faked
    // into an acceptance claim. Scenario checks explicitly control connectivity.
    internal static partial class ArenaAreaRuntime
    {
        private static ArenaAreaDefinition _area;
        private static LocalGame _game;
        private static GameWorld _world;
        private static string _token;
        private static readonly List<ISpawnPoint> Candidates=new();
        private static readonly HashSet<ISpawnPoint> Connected=new();
        private static Vector3 _anchor;
        internal static bool Active, Loading, Ready;
        internal static Func<Vector3,bool> Sample=position=>true;
        internal static Func<Vector3,Vector3,bool> Path=(a,b)=>true;
        internal static GameWorld TestWorld=>_world;
        internal static LocalGame TestGame=>_game;
        internal static string TestToken {get=>_token;set=>_token=value;}
        internal static IEnumerable<TeamSpawnLease> TestLeases=>TeamReservations.Values;
        internal static bool TestStarting=>_teamStarting;
        internal static ArenaAreaDefinition TestArea=>_area;
        internal static ArenaAreaDefinition CurrentArea=>IsCurrentActive()?_area:null;
        internal static bool IsReady=>IsCurrentActive()&&Ready;
        private static bool IsCurrentActive()=>Active&&_area!=null&&_world!=null;
        private static bool IsCurrentLoading()=>Loading&&_area!=null&&_world!=null;
        private static bool IsPlayerPoint(ISpawnPoint p)=>ArenaAreaSpawnPolicy.IsPlayerPoint(p,_game?.Profile?.Info?.Side);
        private static bool IsBotPoint(ISpawnPoint p)=>p!=null&&!string.IsNullOrEmpty(p.BotZoneName)
            && ((p.Categories&ESpawnCategoryMask.Bot)!=0||p.Categories.ContainBotPmcCategory());
        private static bool Contains(Vector3 p,float inset=0)=>_area!=null&&_area.Bounds.Contains(p.x,p.y,p.z,inset);
        private static bool TrySample(Vector3 p,out Vector3 sample) {sample=p;return Sample(p)&&Contains(p,ArenaAreaCatalog.NavigationInset);}
        private static bool CompleteInsidePath(Vector3 a,Vector3 b)=>Contains(a,ArenaAreaCatalog.NavigationInset)&&Contains(b,ArenaAreaCatalog.NavigationInset)&&Path(a,b);
        // Real navigation composition is covered by AreaConnectivityChecks;
        // this seam controls only the success/failure seen by team lifetimes.
        private static bool CompleteConnectedPath(Vector3 sample)=>CompleteInsidePath(_anchor,sample);
        internal static bool AllowsSpawn(ISpawnPoint p)=>IsReady&&p!=null&&_area.AllowsSpawn(p.Position.x,p.Position.y,p.Position.z,p.Id)
            &&Connected.Any(c=>c.Id==p.Id&&(c.Position-p.Position).sqrMagnitude<.01f)
            &&TrySample(p.Position,out var sample)&&CompleteInsidePath(_anchor,sample);
        internal static SpawnSystem TestSetup(ArenaAreaDefinition area,EPlayerSide side,int size,IEnumerable<ISpawnPoint> points)
        {
            ResetTeamStarts(); _area=area; _token=Guid.NewGuid().ToString("N"); _teamSize=size;
            _teamStarting=area?.TeamStarts!=null; Active=false;Loading=true;Ready=false;
            Sample=p=>true;Path=(a,b)=>true;Time.time=0;
            _world=new GameWorld();_game=new LocalGame {GameWorld=_world,Profile=new Profile {Id="local",Info=new ProfileInfo {Side=side}}};
            var collection=new PointsCollection();collection.AddRange(points);_loadingCollection=collection;
            Candidates.Clear();Candidates.AddRange(collection);Connected.Clear();foreach(var p in collection)Connected.Add(p);
            _anchor=collection.First().Position;
            _game.BotsController.BotSpawner.Zones=collection.Where(p=>!string.IsNullOrEmpty(p.BotZoneName)).GroupBy(p=>p.BotZoneName)
                .Select(g=>new BotZone {NameZone=g.Key,SpawnPoints=g.ToArray()}).ToList();
            return new SpawnSystem {_players=_world,_spawnPoints=collection};
        }
        internal static void TestActivate(ISpawnPoint local=null)
        {
            Loading=false;Active=true;Ready=true;
            if(local!=null)
            {
                _initialTeamPoint=local;
                _world.MainPlayer=new Player {Profile=_game.Profile,Position=local.Position};
                _world.AllAlivePlayersList.Add(_world.MainPlayer);_anchor=local.Position;
            }
        }
        internal static bool TestValidate(out string problem)=>ValidateTeamStarts(out problem);
        internal static void TestDisconnect(ISpawnPoint point)=>Connected.Remove(point);
        internal static void TestReset()=>ResetTeamStarts();
        internal static bool TestUnoccupied(Vector3 p,Player ignore=null)=>IsUnoccupied(p,ignore,null);
        internal static void RequirePosition(Vector3 p)
        {
            if(!IsReady||!TrySample(p,out var sample)||!CompleteInsidePath(_anchor,sample))throw new InvalidOperationException("invalid geometry");
        }
        internal static bool Aborted;
        internal static void Abort(string problem) {Aborted=true;Active=false;}
    }
}
