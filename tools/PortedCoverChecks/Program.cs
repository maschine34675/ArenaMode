using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;

static class Program
{
    static int assertions;
    static LocalGame game;
    static GameWorld world;
    static BotOwner bot;
    static BotPathFinderCorePoints finder;
    static CoverSearchData data;
    static MethodInfo Cover=>typeof(PitFireTeamPortedMapCombat).GetMethod("CoverPrefix",BindingFlags.Static|BindingFlags.NonPublic);
    static MethodInfo Path=>typeof(PitFireTeamPortedMapCombat).GetMethod("PathPrefix",BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool result,string label){assertions++;if(!result)throw new InvalidOperationException(label);}
    static bool RunCover(CoverPointMaster master,CoverSearchData search,out object result,bool original=true)
    {
        object[] args={original,master,search,new CustomNavigationPoint()};
        bool run=(bool)Cover.Invoke(null,args);result=args[3];return run;
    }
    static (bool Short,bool Must) RunPath(BotPathFinderCorePoints instance,bool original=true)
    {
        object[] args={original,instance,false,true};Path.Invoke(null,args);return ((bool)args[2],(bool)args[3]);
    }
    static void Fresh()
    {
        world=new();game=new(){GameWorld=world};bot=new(){BotsController=game.BotsController};
        bot.GetPlayer=new(){GameWorld=world};bot.GetPlayer.AIData.BotOwner=bot;world.Alive=bot.GetPlayer;
        bot.CoverSearchInfo=new(){_owner=bot};data=new(){Bot=bot.CoverSearchInfo};
        finder=new(){_owner=bot,_covers=game.BotsController.CoversData};
        Singleton<AbstractGame>.Instance=game;Singleton<GameWorld>.Instance=world;
        ArenaSession.IsActive=ArenaSession.Exact=true;ArenaSession.ActiveMode="zombie";
        PitFireTeamCompat.TeamArenaActive=PitFireTeamCompat.Member=true;PitFireTeamCompat.IsFinalizing=false;
    }
    static void Excluded(Action change,string label)
    {
        Fresh();change();var sentinel=new CustomNavigationPoint();object[] args={true,bot.BotsGroup.CoverPointMaster,data,sentinel};
        Check((bool)Cover.Invoke(null,args),"SCOPE-COVER "+label);
        Check(ReferenceEquals(args[3],sentinel),"SCOPE-SENTINEL "+label);
        var path=RunPath(finder);
        Check(!path.Short&&path.Must,"SCOPE-PATH "+label);
    }
    static void Main()
    {
        var harmony=new Harmony();PitFireTeamPortedMapCombat.Configure(harmony);
        Check(harmony.Hooks.Count==2&&harmony.Hooks[0].Target.Name=="GetCoverPointMain2"&&harmony.Hooks[1].Target.Name=="GoToPosition","NATIVE-BINDING");
        Check(harmony.Hooks.All(x=>x.Priority==Priority.Last),"HARMONY-PRIORITY");
        SourceContracts();
        foreach(var map in new[]{"arena_bay5","arena_equator","arena_airpit","ARENA_AIRPIT"})
        foreach(var mode in new[]{"tactical","zombie"})
        {
            Fresh();world.LocationId=map;ArenaSession.ActiveMode=mode;
            Check(!RunCover(bot.BotsGroup.CoverPointMaster,data,out var result)&&result==null,"EMPTY-COVER "+map+mode);
            var path=RunPath(finder);
            Check(path.Short&&!path.Must,"DIRECT-NAV-ONLY "+map+mode);
            Check(game.BotsController.CoversData.Points.Count==0,"GEOMETRY-UNCHANGED");
            // Execute the observed native branch decision: a real complete path
            // remains success; an incomplete path stops before the cover graph
            // and does not demand the separate cover-based teleport fallback.
            Check(NativeBranch(true,path.Short,path.Must)=="complete","NAV-COMPLETE");
            Check(NativeBranch(false,path.Short,path.Must)=="invalid","NAV-INCOMPLETE");
            Check(NativeBranch(false,false,true)=="null-cover-throw","NATIVE-FAULT-REPRODUCED");
        }
        Excluded(()=>ArenaSession.IsActive=false,"ordinary or loading");
        Excluded(()=>PitFireTeamCompat.TeamArenaActive=false,"solo");
        Excluded(()=>PitFireTeamCompat.IsFinalizing=true,"finalizing");
        Excluded(()=>ArenaSession.Exact=false,"wrong exact raid");
        foreach(var mode in new[]{"deathmatch","gungame","teamdeathmatch",null})Excluded(()=>ArenaSession.ActiveMode=mode,"mode "+mode);
        foreach(var map in new[]{"bigmap","factory4_day","arena_bay50","arena_airpit_extra",null})Excluded(()=>world.LocationId=map,"map "+map);
        foreach(var status in new[]{GameStatus.Running,GameStatus.Stopping,GameStatus.Stopped})Excluded(()=>game.Status=status,"status "+status);
        Excluded(()=>Singleton<AbstractGame>.Instance=null,"no game");
        Excluded(()=>Singleton<AbstractGame>.Instance=new AbstractGame(),"not local game");
        Excluded(()=>Singleton<GameWorld>.Instance=null,"no world");
        Excluded(()=>game.GameWorld=new GameWorld(),"game-world mismatch");
        Excluded(()=>bot.BotsController=new BotsController(),"foreign controller");
        Excluded(()=>game.BotsController=null,"missing controller");
        Excluded(()=>bot.GetPlayer=null,"no player");
        Excluded(()=>bot.GetPlayer.IsAI=false,"human");
        Excluded(()=>bot.GetPlayer.HealthController.IsAlive=false,"dead");
        Excluded(()=>bot.GetPlayer.HealthController=null,"no health");
        Excluded(()=>bot.GetPlayer.GameWorld=new GameWorld(),"foreign player world");
        Excluded(()=>bot.GetPlayer.AIData.BotOwner=new BotOwner(),"replaced owner");
        Excluded(()=>bot.GetPlayer.AIData=null,"no AI");
        Excluded(()=>world.Alive=new Player(),"same id wrong physical body");
        Excluded(()=>world.Alive=null,"deregistered");
        Excluded(()=>game.BotsController.CoversData=null,"no cover data");
        Excluded(()=>game.BotsController.CoversData.Points=null,"malformed cover data");
        Excluded(()=>game.BotsController.CoversData.Points.Add(new()),"real covers present");
        Fresh();PitFireTeamCompat.Member=false;
        Check(RunCover(bot.BotsGroup.CoverPointMaster,data,out _),"COMPANION-ONLY-COVER");
        Check(RunPath(finder)==(true,false),"INFECTED-DIRECT-NAV");
        Fresh();Check(RunCover(new CoverPointMaster(),data,out _),"FOREIGN-COVER-MASTER");
        Fresh();Check(RunCover(bot.BotsGroup.CoverPointMaster,new CoverSearchData{Bot=new BotCoverSearchInfo{_owner=bot}},out _),"FOREIGN-COVER-SEARCH");
        Fresh();finder._covers=new();Check(RunPath(finder)==(false,true),"FOREIGN-FINDER-COVERS");
        Fresh();Check(RunCover(bot.BotsGroup.CoverPointMaster,null,out _),"NULL-SEARCH");
        Fresh();data.Bot=null;Check(RunCover(bot.BotsGroup.CoverPointMaster,data,out _),"NULL-SEARCH-BOT");
        Fresh();data.Bot=new object();Check(RunCover(bot.BotsGroup.CoverPointMaster,data,out _),"FOREIGN-SEARCH-BOT");
        Fresh();bot.CoverSearchInfo._owner=null;Check(RunCover(bot.BotsGroup.CoverPointMaster,data,out _),"NULL-SEARCH-OWNER");
        Fresh();var master=bot.BotsGroup.CoverPointMaster;bot.BotsGroup=null;Check(RunCover(master,data,out _),"NO-COVER-GROUP");
        Fresh();Check(RunPath(null)==(false,true),"NULL-FINDER");
        Fresh();finder._owner=null;Check(RunPath(finder)==(false,true),"NULL-FINDER-OWNER");
        Fresh();world.LocationId="ARENA_BAY5";Check(!RunCover(bot.BotsGroup.CoverPointMaster,data,out _),"CASE-INSENSITIVE-MAP");
        Fresh();var sentinel=new CustomNavigationPoint();object[] cancelled={false,bot.BotsGroup.CoverPointMaster,data,sentinel};
        Check((bool)Cover.Invoke(null,cancelled)&&ReferenceEquals(cancelled[3],sentinel),"EARLIER-COVER-OWNER");
        Check(RunPath(finder,false)==(false,true),"EARLIER-PATH-OWNER");
        Fresh();int before=ArenaMode.Plugin.ModLogger.Lines;RunPath(finder);RunPath(finder);
        Check(ArenaMode.Plugin.ModLogger.Lines==before+1,"BOUNDED-DIAGNOSTICS");
        Console.WriteLine($"PORTED_COVER_CHECKS_PASSED: {assertions} assertions; linked production gates/native branch model, not game acceptance.");
    }
    static string NativeBranch(bool complete,bool shortOnly,bool mustHaveWay)
        =>complete?"complete":!shortOnly?"null-cover-throw":mustHaveWay?"null-teleport-throw":"invalid";

    static void SourceContracts()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!Directory.Exists(System.IO.Path.Combine(directory.FullName,"ArenaMode.Client")))directory=directory.Parent;
        if(directory==null)throw new InvalidOperationException("Cannot locate repository for mandatory native contract checks.");
        string native=System.IO.Path.GetFullPath(System.IO.Path.Combine(directory.FullName,"../_decompiled"));
        string text=File.ReadAllText(System.IO.Path.Combine(native,"BotPathFinderCorePoints.cs"));
        string calculate=Method(text,"public static EPathCalcResult CalculatePathFull(int connectionGroup, Vector3 from, Vector3 to,");
        int real=calculate.IndexOf("if (FindPath(from, to, out path))",StringComparison.Ordinal);
        int shortGate=calculate.IndexOf("if (onlyShortTrie)",StringComparison.Ordinal);
        int covers=calculate.IndexOf("GetClosestPointWithWay(covers, to, connectionGroup",StringComparison.Ordinal);
        Check(real>=0&&shortGate>real&&covers>shortGate,"NATIVE-FULL-PATH-BEFORE-SHORT-CIRCUIT");
        Check(calculate.Substring(shortGate,covers-shortGate).Contains("return EPathCalcResult.NeedTeleport;"),"NATIVE-SHORT-CIRCUIT-FAILURE");
        string find=Method(text,"public static bool FindPath(Vector3 f, Vector3 t,");
        Check(find.Contains("NavMesh.CalculatePath(f, t, -1, navMeshPath) && navMeshPath.status == NavMeshPathStatus.PathComplete"),"NATIVE-FINDPATH-REQUIRES-COMPLETE");
        string go=Method(text,"public NavMeshPathStatus GoToPosition(Vector3 target,");
        Check(go.Contains("out var path, onlyShortTrie, method_1)"),"NATIVE-FORWARDS-SHORT-MODE");
        int required=go.IndexOf("if (mustHaveWay)",StringComparison.Ordinal);
        int teleport=go.IndexOf("_owner.GetPlayer.Teleport(closestPoint.Position);",StringComparison.Ordinal);
        Check(required>=0&&teleport>required&&go.Substring(required,teleport-required).Contains("GetClosestPoint(_covers,"),"NATIVE-COVER-TELEPORT-GATED");
        Check(go.Contains("if (slowCalcUsingNativeNavMesh)")&&go.Contains("navMeshPath.status != NavMeshPathStatus.PathInvalid"),"NATIVE-OPTIONAL-PARTIAL-BRANCH-PRESERVED");
        text=File.ReadAllText(System.IO.Path.Combine(native,"CoverPointMaster.cs"));
        Check(text.Contains("[CanBeNull]\n\tpublic CustomNavigationPoint GetCoverPointMain2".Replace("\n",Environment.NewLine))||text.Contains("[CanBeNull]\n\tpublic CustomNavigationPoint GetCoverPointMain2"),"NATIVE-COVER-NULL-CONTRACT");
        string cover=Method(text,"public CustomNavigationPoint GetCoverPointMain2(");
        Check(cover.Contains("_finderByGraph.GetCover(data)")&&cover.Contains("voxelSafe.PointStartSearch.GetById(data.Bot.Id)"),"NATIVE-NODE-DEPENDENCY");
        string pft=System.IO.Path.Combine(directory.FullName,"artifacts/zombie-voice-pft-20261009/sain-analysis/pft-installed/pitTeam.Utils/Covers.cs");
        string candidates=Method(File.ReadAllText(pft),"private static List<CustomNavigationPoint> GetVanillaBackedCandidates(");
        Check(candidates.Contains("GetCoverPointMain(data, checkCurrent: false)")&&candidates.Contains("if (primary != null"),"INSTALLED-PFT-ACCEPTS-EMPTY-COVER");
    }
    static string Method(string source,string signature)
    {
        int start=source.IndexOf(signature,StringComparison.Ordinal);
        if(start<0)throw new InvalidOperationException("Native source signature drift: "+signature);
        int open=source.IndexOf('{',start),depth=0;
        for(int i=open;i<source.Length;i++)
        {
            if(source[i]=='{')depth++;
            if(source[i]=='}'&&--depth==0)return source.Substring(start,i-start+1);
        }
        throw new InvalidOperationException("Unclosed native method "+signature);
    }
}
