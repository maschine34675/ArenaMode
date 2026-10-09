using ArenaMode.Areas;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;

static class AreaHarness
{
    static int assertions,native;
    static LocalGame game;
    static GameWorld world;
    static BotsController controller;
    static BotCreatorClient creator;
    static Profile profile;
    static MethodInfo Method(string name)=>typeof(PitFireTeamAreaSpawns).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);
    static void Check(bool condition,string name){assertions++;if(!condition)throw new InvalidOperationException(name);}
    static void Fresh(bool loading=false,bool reset=true)
    {
        if(reset)PitFireTeamAreaSpawns.Reset();
        world=new(){LocationId="bigmap"};game=new(){GameWorld=world,Status=loading?GameStatus.Running:GameStatus.Started};
        controller=game.BotsController;creator=new(){_game=game};controller.BotSpawner._botCreator=creator;
        world.MainPlayer.GameWorld=world;
        Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=game;
        ArenaSession.IsActive=!loading;ArenaSession.Exact=true;ArenaSession.ArmedForNextRaid=true;
        ArenaSession.ArmedMap="bigmap";ArenaSession.ArmedMode=ArenaSession.ActiveMode="zombie";ArenaSession.ArmEpoch++;
        PitFireTeamCompat.TeamArenaActive=true;PitFireTeamCompat.IsFinalizing=false;PitFireTeamCompat.RoundTransitionActive=false;
        profile=new(){Id="companion-1",AccountId="account-1"};
        PitFireTeamCompat.CanonicalCompanionProfilesByAccountId=new(){[profile.AccountId]=profile.Id};
        var boss=new pitTeam.Components.pitAIBossPlayer{realPlayer=world.MainPlayer,_botsController=controller};
        pitTeam.Modules.BossPlayers.Boss=boss;pitTeam.Patches.BotsControllerPatch.Controller=controller;
        pitTeam.Patches.BotsControllerPatch.spawnedPlayers=[boss];pitTeam.Patches.BotsEventsControllerSpawnPatch.Calls=0;
        PitFireTeamRoundLifecycle.Runtime=null;PitFireTeamRoundLifecycle.Rejected=0;
        ArenaAreaRuntime.Block=loading;ArenaAreaRuntime.IsReady=!loading;ArenaAreaRuntime.CurrentArea=loading?null:new();
        ArenaAreaRuntime.HavePoint=ArenaAreaRuntime.Accept=true;ArenaAreaRuntime.Reservations=ArenaAreaRuntime.Disposals=0;native=0;
    }
    static (bool proceed,object[] args) Begin(Action<BotOwner> callback=null,bool run=true)
    {
        callback??=bot=>{native++;controller.BotSpawner._inSpawnProcess--;};
        object[] args={run,creator,profile,new PositionNote(new(),0,false),new BotZone(),callback,null,null};
        controller.BotSpawner._inSpawnProcess++;
        bool proceed=(bool)Method("ActivatePrefix").Invoke(null,args);
        return(proceed,args);
    }
    static BotOwner Body(Profile p=null,GameWorld w=null)=>new(){Profile=p??profile,GetPlayer=new(){GameWorld=w??world}};
    static Task<bool> Observe(object[] args,Task<bool> nativeTask)
    {
        object[] p={nativeTask,args[7]};Method("ActivatePostfix").Invoke(null,p);return(Task<bool>)p[0];
    }
    static void Arrive(object[] args,BotOwner b)=>((Action<BotOwner>)args[5])(b);
    static void Start()
    {ArenaSession.IsActive=true;game.Status=GameStatus.Started;ArenaAreaRuntime.Block=false;ArenaAreaRuntime.IsReady=true;ArenaAreaRuntime.CurrentArea=new();}
    static void RepeatedRaids()
    {
        // The normal protected end keeps PFT in finalization. The next freeze
        // now resets pending placements, but creator identity must also remain
        // safe without that cleanup and with a reused Profile object.
        foreach(bool reset in new[]{false,true})
        foreach(bool reuseProfile in new[]{false,true})
        foreach(bool arrived in new[]{false,true})
        {
            Fresh();
            var oldCreator=creator;var oldController=controller;var oldWorld=world;var oldProfile=profile;
            int oldCallbacks=0;
            var(first,oldArgs)=Begin(_=>{oldCallbacks++;oldController.BotSpawner._inSpawnProcess--;});
            Check(first,"first raid admitted before repeated raid check");
            if(arrived)Arrive(oldArgs,Body());
            game.Status=GameStatus.Stopped;
            Fresh(reset:reset);
            if(reuseProfile)profile=oldProfile;
            var(second,newArgs)=Begin();
            Check(second&&ArenaAreaRuntime.Reservations==1,
                "same companion ID/profile accepted by new native creator with or without compatibility clear");
            object[] newRaw={creator,profile,null};
            Check((bool)Method("CreatePrefix").Invoke(null,newRaw),"second raid raw creation remains admitted");
            int newCharge=controller.BotSpawner._inSpawnProcess;
            object[] oldRaw={oldCreator,oldProfile,null};
            Check(!(bool)Method("CreatePrefix").Invoke(null,oldRaw)&&((Task)oldRaw[2]).IsCompleted,
                "old creator's deferred raw creation remains blocked after new raid admitted same profile");
            Check(controller.BotSpawner._inSpawnProcess==newCharge&&oldController.BotSpawner._inSpawnProcess==0,
                "old raw rejection settles only its own native spawner charge");
            Arrive(newArgs,Body());
            Check(native==1&&controller.BotSpawner._inSpawnProcess==0,"new native callback publishes and settles normally");
            var(duplicate,duplicateArgs)=Begin();
            Check(!duplicate&&ArenaAreaRuntime.Reservations==1&&controller.BotSpawner._inSpawnProcess==0,
                "same creator duplicate identity rejected before another marker reservation");
            profile=new(){Id=profile.Id,AccountId=profile.AccountId};
            Check(!Begin().proceed&&ArenaAreaRuntime.Reservations==1&&controller.BotSpawner._inSpawnProcess==0,
                "new profile object cannot repeat the same identity within one creator");
            if(!arrived)
            {
                var foreign=Body(oldProfile,world);Arrive(oldArgs,foreign);
                Check(oldCallbacks==0&&foreign.Retired==0&&controller.BotSpawner._inSpawnProcess==0,
                    "old callback cannot retire or adopt a body from the next world sharing its profile");
                var late=Body(oldProfile,oldWorld);Arrive(oldArgs,late);Arrive(oldArgs,late);
                Check(oldCallbacks==0&&late.Retired==1&&controller.BotSpawner._inSpawnProcess==0,
                    "old physical arrival cannot publish or disturb the next raid");
            }
        }
        Fresh();var(firstSameCreator,sameArgs)=Begin();
        Check(firstSameCreator,"same creator reset control initially admitted");
        PitFireTeamAreaSpawns.Reset();
        Check(!Begin().proceed&&ArenaAreaRuntime.Reservations==1&&controller.BotSpawner._inSpawnProcess==0,
            "reset retains duplicate identity tombstone within the same native creator");
        object[] resetRaw={creator,profile,null};
        Check(!(bool)Method("CreatePrefix").Invoke(null,resetRaw),"reset still blocks same creator delayed raw call");
    }
    public static int Run()
    {
        PitFireTeamAreaSpawns.Configure(typeof(AreaHarness).Assembly,new Harmony());
        Fresh(true);
        Check(!(bool)Method("DeferPrefix").Invoke(null,null),"initial loading dispatch deferred");
        PitFireTeamAreaSpawns.Tick();Check(pitTeam.Patches.BotsEventsControllerSpawnPatch.Calls==0,"pending cannot fire early");
        Check(!(bool)Method("DeferPrefix").Invoke(null,null),"duplicate loading event coalesced");
        Start();PitFireTeamAreaSpawns.Tick();PitFireTeamAreaSpawns.Tick();
        Check(pitTeam.Patches.BotsEventsControllerSpawnPatch.Calls==1,"ready native dispatch exactly once");
        foreach(Action stale in new Action[]{()=>ArenaSession.ArmEpoch++,()=>world.MainPlayer=new(),()=>PitFireTeamCompat.IsFinalizing=true,
            ()=>pitTeam.Modules.BossPlayers.Boss=new(),()=>game.Status=GameStatus.Stopping,()=>ArenaSession.Exact=false})
        {
            Fresh(true);Method("DeferPrefix").Invoke(null,null);Start();stale();PitFireTeamAreaSpawns.Tick();
            Check(pitTeam.Patches.BotsEventsControllerSpawnPatch.Calls==0,"stale pending dispatch never replays");
        }
        Fresh(true);PitFireTeamCompat.TeamArenaActive=false;
        Check((bool)Method("DeferPrefix").Invoke(null,null),"ordinary PFT untouched");
        Fresh();var(ok,args)=Begin();Check(ok,"initial companion allowed");
        Check(((PositionNote)args[3]).CorePointId==77&&ArenaAreaRuntime.Reservations==1,"real marker replaces shared leader point");
        var result=Observe(args,Task.FromResult(true));Check(result.Result&&ArenaAreaRuntime.Disposals==0,"completed task does not free physical lease");
        var body=Body();Arrive(args,body);Check(native==1&&ArenaAreaRuntime.Disposals==1&&controller.BotSpawner._inSpawnProcess==0,"accepted callback releases once");
        Arrive(args,body);Check(native==1&&body.Retired==0,"duplicate same callback no-op");
        Fresh();(_,args)=Begin();ArenaSession.ArmEpoch++;body=Body();Arrive(args,body);Arrive(args,body);
        Check(native==0&&body.Retired==1&&controller.BotSpawner._inSpawnProcess==0,"stale arrival retired and counter settled once");
        Fresh();(_,args)=Begin();ArenaAreaRuntime.Accept=false;body=Body();Arrive(args,body);
        Check(native==0&&body.Retired==1,"outside area rejected before PFT adoption");
        Fresh();(_,args)=Begin();body=Body(new(){Id=profile.Id,AccountId=profile.AccountId});Arrive(args,body);
        Check(native==0&&body.Retired==0,"foreign profile object never retired");
        Fresh();(_,args)=Begin();PitFireTeamAreaSpawns.Reset();body=Body();Arrive(args,body);
        Check(native==0&&body.Retired==1&&controller.BotSpawner._inSpawnProcess==0,"reset revokes late callback only old charge");
        Fresh();ArenaAreaRuntime.HavePoint=false;(ok,args)=Begin();Check(!ok&&controller.BotSpawner._inSpawnProcess==0,"missing marker fails closed before native");
        Fresh();(_,args)=Begin();Check(!Observe(args,Task.FromResult(false)).Result&&ArenaAreaRuntime.Disposals==1&&controller.BotSpawner._inSpawnProcess==0,"failed native task releases lease and charge");
        Fresh();(_,args)=Begin(_=>throw new InvalidOperationException("injected callback"));body=Body();try{Arrive(args,body);}catch(InvalidOperationException){}
        Check(body.Retired==1&&controller.BotSpawner._inSpawnProcess==0,"callback throw before native decrement settles charge");
        Fresh();(_,args)=Begin(_=>{controller.BotSpawner._inSpawnProcess--;throw new InvalidOperationException("injected after decrement");});body=Body();try{Arrive(args,body);}catch(InvalidOperationException){}
        Check(body.Retired==1&&controller.BotSpawner._inSpawnProcess==0,"callback throw after decrement does not double settle");
        Fresh();(_,args)=Begin(_=>{});body=Body();Arrive(args,body);
        Check(body.Retired==1&&controller.BotSpawner._inSpawnProcess==0,"missing native decrement fails closed");
        Fresh();PitFireTeamRoundLifecycle.Runtime=profile;PitFireTeamCompat.RoundTransitionActive=true;(_,args)=Begin();body=Body();Arrive(args,body);
        Check(native==1&&body.Retired==0,"runtime exact lifecycle lease retains success");
        Fresh();PitFireTeamRoundLifecycle.Runtime=profile;PitFireTeamCompat.RoundTransitionActive=true;(_,args)=Begin();ArenaAreaRuntime.Accept=false;body=Body();Arrive(args,body);
        Check(native==0&&body.Retired==1&&PitFireTeamRoundLifecycle.Rejected==1,"runtime rejection uses existing lifecycle and never double retires");
        Fresh();PitFireTeamRoundLifecycle.Runtime=profile;PitFireTeamCompat.RoundTransitionActive=true;(_,args)=Begin();
        PitFireTeamAreaSpawns.Reset();PitFireTeamRoundLifecycle.Runtime=null;body=Body();Arrive(args,body);Arrive(args,body);
        Check(native==0&&body.Retired==1,"runtime late arrival after lifecycle reset retired exactly once");
        Fresh();(_,args)=Begin();object[] raw={creator,profile,null};
        Check((bool)Method("CreatePrefix").Invoke(null,raw),"current raw creation allowed");
        PitFireTeamAreaSpawns.Reset();raw=[creator,profile,null];
        Check(!(bool)Method("CreatePrefix").Invoke(null,raw)&&((Task)raw[2]).IsCompleted,"reset before native yield continuation blocks raw creation");
        raw=[new BotCreatorClient{_game=new()},profile,null];
        Check((bool)Method("CreatePrefix").Invoke(null,raw),"same profile on different creator is not captured by old entry");
        Fresh();(ok,args)=Begin(run:false);Check(ok&&ArenaAreaRuntime.Reservations==0&&args[7]==null,"earlier Harmony veto preserved");
        Fresh();profile.AccountId="foreign";(ok,args)=Begin();Check(ok&&ArenaAreaRuntime.Reservations==0,"unrelated profile untouched");
        Fresh();ArenaAreaRuntime.CurrentArea=null;(ok,args)=Begin();Check(ok&&ArenaAreaRuntime.Reservations==0,"full map untouched");
        RepeatedRaids();
        Fresh();PitFireTeamAreaSpawns.Reset();
        Console.WriteLine($"PFT bounded native-hook harness: {assertions} assertions.");
        return assertions;
    }
}
