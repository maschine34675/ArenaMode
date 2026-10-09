using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

static class Program
{
    static int assertions;
    static GameWorld world;
    static LocalGame game;
    static BotsController controller;
    static AICorePoint near,far;
    static void Check(bool condition,string name) {assertions++;if(!condition)throw new InvalidOperationException(name);}
    static void Fresh()
    {
        world=new();game=new(){GameWorld=world};controller=game.BotsController;
        Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=game;
        near=new(){Id=23,Position=new(2,0,0)};far=new(){Id=5,Position=new(50,0,0)};
        controller.CoversData.AICorePointsHolder.CorePoints.AddRange([far,near]);
        ArenaSession.IsActive=false;ArenaSession.Exact=true;ArenaSession.ArmedForNextRaid=true;
        ArenaSession.ArmedMap=world.LocationId;ArenaSession.ArmedMode=ArenaSession.ActiveMode="tactical";
        PitFireTeamCompat.TeamArenaActive=true;PitFireTeamCompat.IsFinalizing=false;
    }
    static void Exclude(Action change,string name)
    {
        Fresh();change();Check(!PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out var point),name);
        Check(point==null,name+" result untouched");
    }
    static void Main()
    {
        foreach(var map in new[]{"arena_bay5","arena_equator"})
        foreach(var mode in new[]{"tactical","zombie"})
        foreach(bool active in new[]{false,true})
        {
            Fresh();world.LocationId=ArenaSession.ArmedMap=map;
            ArenaSession.ActiveMode=ArenaSession.ArmedMode=mode;ArenaSession.IsActive=active;
            game.Status=active?GameStatus.Started:GameStatus.Running;
            var list=controller.CoversData.AICorePointsHolder.CorePoints;
            Check(PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out var point),$"{map}/{mode}/{active}");
            Check(ReferenceEquals(point,near),"nearest authored core");
            Check(controller.CoversData.Points.Count==0 && list.Count==2,"native geometry unchanged");
        }
        Exclude(()=>PitFireTeamCompat.TeamArenaActive=false,"solo or unauthenticated roster");
        Exclude(()=>PitFireTeamCompat.IsFinalizing=true,"finalization");
        Exclude(()=>Singleton<AbstractGame>.Instance=null,"no game");
        Exclude(()=>Singleton<AbstractGame>.Instance=new AbstractGame(),"not LocalGame");
        Exclude(()=>Singleton<GameWorld>.Instance=null,"no world");
        Exclude(()=>game.GameWorld=new GameWorld(),"wrong world");
        Exclude(()=>game.BotsController=new BotsController(),"foreign controller");
        Exclude(()=>controller=null,"null controller");
        Exclude(()=>world.MainPlayer=null,"no leader");
        Exclude(()=>world.MainPlayer.IsYourPlayer=false,"not local leader");
        Exclude(()=>world.MainPlayer.ProfileId="other","wrong leader profile");
        Exclude(()=>game.Profile=null,"no profile");
        foreach(var map in new[]{"factory4_day","bigmap","laboratory","arena_bay50",null})
            Exclude(()=>world.LocationId=ArenaSession.ArmedMap=map,"ordinary or unknown map");
        Exclude(()=>ArenaSession.ArmedForNextRaid=false,"ordinary loading");
        Exclude(()=>ArenaSession.ArmedMap="arena_equator","arm mismatch");
        foreach(var mode in new[]{"deathmatch","gungame","tdm",null})
            Exclude(()=>ArenaSession.ArmedMode=mode,"not a protected mode");
        foreach(var status in new[]{GameStatus.Stopped,GameStatus.Stopping,GameStatus.SoftStopping,GameStatus.Started})
            Exclude(()=>game.Status=status,"not matching loading phase");
        Exclude(()=>{ArenaSession.IsActive=true;ArenaSession.Exact=false;game.Status=GameStatus.Started;},"stale active raid");
        Exclude(()=>{ArenaSession.IsActive=true;game.Status=GameStatus.Stopping;},"active stopping");
        Exclude(()=>{ArenaSession.IsActive=true;game.Status=GameStatus.Started;ArenaSession.ActiveMode="tdm";},"wrong active mode");
        Exclude(()=>controller.CoversData=null,"no cover container");
        Exclude(()=>controller.CoversData.Points=null,"malformed cover list");
        Exclude(()=>controller.CoversData.Points.Add(new()),"existing covers retain original");
        Exclude(()=>controller.CoversData.AICorePointsHolder=null,"no holder");
        Exclude(()=>controller.CoversData.AICorePointsHolder.CorePoints=null,"no core list");
        Exclude(()=>controller.CoversData.AICorePointsHolder.CorePoints.Clear(),"no real core points");
        foreach(var bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            Fresh();Check(!PitFireTeamPortedMapSpawns.TryResolve(controller,new(bad,0,0),out _),"nonfinite spawn");
            near.Position=new(bad,0,0);Check(PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out var p)&&ReferenceEquals(p,far),"invalid core skipped");
        }
        Fresh();near.Destroyed=true;Check(PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out var picked)&&ReferenceEquals(picked,far),"destroyed Unity core skipped");
        Fresh();near.Id=-1;Check(PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out picked)&&ReferenceEquals(picked,far),"invalid core ID skipped");
        Fresh();near.Id=far.Id;Check(PitFireTeamPortedMapSpawns.TryResolve(controller,new(),out picked)&&ReferenceEquals(picked,far),"duplicate ID cannot reference wrong core");
        Fresh();var harmony=new Harmony();PitFireTeamPortedMapSpawns.Configure(typeof(Program).Assembly,"0.10.3",harmony);
        Check(harmony.Target?.Name=="GetClosestCorePoint"&&harmony.Prefix!=null,"actual binding shape");
        object[] args={controller,new Vector3(),null};
        Check((bool)harmony.Prefix.Invoke(null,args)==false&&ReferenceEquals(args[2],near),"replacement result at native seam");
        controller.CoversData.Points.Add(new());args=[controller,new Vector3(),far];
        Check((bool)harmony.Prefix.Invoke(null,args)==true&&ReferenceEquals(args[2],far),"native sentinel untouched outside zero covers");
        foreach(var version in new[]{"0.10.0","0.10.1","0.10.2","0.10.4",null})
        {
            var rejected=false;try{PitFireTeamPortedMapSpawns.Configure(typeof(Program).Assembly,version,new Harmony());}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"unsupported or mismatched plugin/assembly "+version);
        }
        assertions+=AreaHarness.Run();
        Console.WriteLine($"PFT_PORTED_MAP_CHECKS_PASSED: {assertions} assertions; production resolver and native-hook binding, no game acceptance claimed.");
    }
}
