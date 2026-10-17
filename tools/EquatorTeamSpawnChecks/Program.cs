using System.Reflection;
using System.Runtime.ExceptionServices;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using Redeploy.Core.Spawning;
using UnityEngine;
using UnityEngine.AI;
using Runtime=ArenaMode.Bots.ArenaTeamBoxRuntime;
using Catalog=ArenaMode.Shared.ArenaTeamBoxCatalog;

internal static class Program
{
    private static int Checks;
    internal static Catalog.Formation Formation = Catalog.Find("arena_equator");
    private static LocalGame Game;
    private static GameWorld World;
    private static BotZone Zone;
    private static SpawnPointsCollection Collection;
    private static void Check(bool condition,string name) {Checks++;if(!condition)throw new Exception(name);}
    private static void Reject(Action action,string name)
    {try{action();}catch(InvalidOperationException){Checks++;return;}throw new Exception(name);}
    private static object Invoke(string name,params object[] args)
    {
        try{return typeof(Runtime).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}
        catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    private static object Initial(ESpawnCategory category,EPlayerSide side,string profile,SpawnSystem system,out ISpawnPoint selected)
    {
        var patch=typeof(Runtime).GetNestedType("InitialPatch",BindingFlags.NonPublic);
        object[] args={system,category,side,profile,null};
        try{var result=patch.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);selected=(ISpawnPoint)args[4];return result;}
        catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    private static Player Body(string id,EPlayerSide side,Vector3 position,bool add=true)
    {var player=new Player{Profile=new(){Id=id,Info=new(){Side=side}},Position=position};if(add)World.AllAlivePlayersList.Add(player);return player;}
    private static void Setup(bool active=true,EPlayerSide side=EPlayerSide.Usec,int total=4,bool restore=true)
    {
        Runtime.Shutdown();SPT.Reflection.Patching.ModulePatch.Fail=false;Runtime.Enable();
        Physics.Floor=p=>true;Physics.Clear=p=>true;NavMesh.Sample=p=>true;NavMesh.Path=(a,b)=>true;
        ArenaMode.Modes.ArenaMatchService.Aborts.Clear();
        World=new(){LocationId=Formation.MapId};
        Game=new(){GameWorld=World,Status=GameStatus.Running,Profile=new(){Id="local",Info=new(){Side=side}}};
        Singleton<AbstractGame>.Instance=Game;Singleton<GameWorld>.Instance=World;
        ArenaSession.ExactGame=Game;ArenaSession.ExactWorld=World;ArenaSession.IsActive=false;
        ArenaSession.ArmedForNextRaid=true;ArenaSession.ArmedMap=Formation.MapId;ArenaSession.ArmedMode=Catalog.ModeId;
        ArenaSession.ActiveMode=Catalog.ModeId;ArenaSession.Participants=total;ArenaSession.ArmEpoch++;
        ArenaServerClient.PendingArmToken=Guid.NewGuid().ToString("N");
        Game.Settings=new(){location=Formation.MapId,serverId="start:"+ArenaServerClient.PendingArmToken};
        Zone=new(){name=Formation.Zone};FindObjectsProxy.Zones=new(){Zone};Collection=new();
        foreach(var spec in Formation.Points)
        {
            var point=new SpawnPoint{Id=spec.Id,Position=new(0,.2f,2),Rotation=Quaternion.Euler(0,spec.Yaw,0),
                BotZoneName=Formation.Zone,Infiltration=Formation.Infiltration,CorePointId=0,DelayToCanSpawnSec=4,
                Sides=spec.Usec?EPlayerSideMask.Usec:EPlayerSideMask.Bear,
                Categories=ESpawnCategoryMask.Player|ESpawnCategoryMask.Bot|ESpawnCategoryMask.BotPmc};
            var marker=new SpawnPointMarker{SpawnPoint=point,Sphere=new(){radius=.8f,isTrigger=true}};
            Zone.SpawnPointMarkers.Add(marker);Collection._dictionary.Add(point,marker);
        }
        if(restore)Invoke("RestoreCollection",Collection);
        var first=Formation.Points.First(p=>p.Usec==(side==EPlayerSide.Usec));
        World.MainPlayer=Body("local",side,new(first.X,first.Y,first.Z));
        if(active){ArenaSession.IsActive=true;ArenaSession.ArmedForNextRaid=false;Game.Status=GameStatus.Started;}
    }
    private static Runtime.Lease Reserve(bool usec=true)
    {Check(Runtime.TryPlanBot(new[]{Zone},usec,true,out var zone,out var point,out var lease),"RESERVE: safe team place available");Check(zone==Zone&&Formation.Find(point.Id).Usec==usec,"FACTION: only canonical own box");return lease;}
    private static void ScopeAndFormation()
    {
        Setup();Check(Runtime.IsReady&&Runtime.IsRequired,"READY: exact active scope");Runtime.Begin(World,Game);
        Check(ArenaMode.Plugin.ModLogger.Lines.Any(x=>x.Contains("USEC 10/10")),"CAPACITY-LOG: all positions exposed");
        Check(Collection._dictionary.All(p=>p.Key.Position.z==(Formation.Find(p.Key.Id).Z)),"RESTORE: moved points restored");
        var point=Collection._dictionary.Keys.First();var old=point.Position;
        ArenaSession.IsActive=false;((SpawnPoint)point).Position=new(123,0,123);Invoke("RestoreCollection",Collection);
        Check(point.Position.x==123&&!Runtime.IsRequired,"ORDINARY: no unarmed mutation");
        ArenaSession.IsActive=true;ArenaSession.ActiveMode="deathmatch";Invoke("RestoreCollection",Collection);
        Check(point.Position.x==123&&!Runtime.IsRequired,"OTHER-MODE: unchanged");
        Setup();Runtime.Reset();Check(Runtime.IsRequired,"FAIL-CLOSED: missing preparation remains required");
        Check(!Runtime.TryPlanBot(new[]{Zone},true,true,out _,out _,out _),"FAIL-CLOSED: missing preparation rejects bots");
        Reject(()=>Runtime.PickRespawn(Game,default),"FAIL-CLOSED: missing preparation rejects player");
        Reject(()=>Runtime.Begin(World,Game),"FAIL-CLOSED: missing preparation rejects match");
        Runtime.Shutdown();SPT.Reflection.Patching.ModulePatch.Fail=true;Runtime.Enable();
        Check(!Runtime.IsReady,"HOOK: failure is unavailable");
        Setup();ArenaSession.Participants=24;Reject(()=>Runtime.Begin(World,Game),"CAPACITY: 12v12 rejected");
        Setup(total:20);var blocked=Formation.Points[1];Physics.Floor=p=>p!=new Vector3(blocked.X,blocked.Y,blocked.Z);Reject(()=>Runtime.Begin(World,Game),"CAPACITY: blocked slot rejects 10v10");
        Setup();NavMesh.Path=(a,b)=>Formation.InsideBooth(a.x,a.y,a.z,false);Reject(()=>Runtime.Begin(World,Game),"PATH: one disconnected box rejects match");
        Setup();World.MainPlayer.Position+=new Vector3(.5f,0,0);var bodyPosition=World.MainPlayer.Position;
        Physics.Floor=p=>p!=bodyPosition;Reject(()=>Runtime.Begin(World,Game),"INITIAL-BODY: actual floor is required");
    }
    private static void HeadersAndInitial()
    {
        foreach(string fault in new[]{"missing","unknown","duplicate","side","category","core","delay","sphere","zone","foreign-map"})
        {
            Setup(active:false,restore:false);var pair=Collection._dictionary.First();var point=(SpawnPoint)pair.Key;
            switch(fault)
            {
                case "foreign-map":point.Id=Catalog.Find(Formation.MapId=="arena_equator"?"arena_bay5":"arena_equator").Points[0].Id;break;
                case "missing":Collection._dictionary.Remove(point);break;
                case "unknown":point.Id=Formation.IdPrefix+"unknown";break;
                case "duplicate":Collection._dictionary.Keys.Skip(1).Cast<SpawnPoint>().First().Id=point.Id;break;
                case "side":point.Sides=EPlayerSideMask.All;break;
                case "category":point.Categories=ESpawnCategoryMask.Player;break;
                case "core":point.CorePointId=9;break;
                case "delay":point.DelayToCanSpawnSec=5;break;
                case "sphere":pair.Value.Sphere.radius=30;break;
                case "zone":Zone.SpawnPointMarkers.Remove(pair.Value);break;
            }
            Reject(()=>Invoke("RestoreCollection",Collection),"HEADERS: "+fault);
            Check(Collection._dictionary.Keys.All(p=>p.Position.z==2),"HEADERS-ATOMIC: no partial restore "+fault);
        }
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        {
            Setup(active:false,side:side);NavMesh.Sample=p=>false;
            var system=new SpawnSystem{_players=World,_spawnPoints=Collection};
            Check(!(bool)Initial(ESpawnCategory.Player,side,"local",system,out var chosen),"INITIAL: replace exact local selector");
            Check(Formation.Find(chosen.Id).Usec==(side==EPlayerSide.Usec),"INITIAL: correct faction without premature NavMesh");
            Check((bool)Initial(ESpawnCategory.Bot,side,"local",system,out _),"INITIAL: other category untouched");
            Check((bool)Initial(ESpawnCategory.Player,side,"other",system,out _),"INITIAL: other profile untouched");
            system._players=new();Reject(()=>Initial(ESpawnCategory.Player,side,"local",system,out _),"INITIAL: wrong world fail closed");
            system._players=World;Physics.Clear=p=>false;
            Reject(()=>Initial(ESpawnCategory.Player,side,"local",system,out _),"INITIAL: no physically clear point fails");
        }
    }
    private static void ReservationsAndArrival()
    {
        Setup();var leases=new List<Runtime.Lease>();
        leases.Add(Reserve());
        Check(Runtime.TryPlanBot(new[]{Zone},true,false,out _,out var next,out _)&&next.Id!=leases[0].PointId,
            "PENDING: promised point is not offered again");
        for(int i=1;i<9;i++)leases.Add(Reserve());
        Check(leases.Select(l=>l.PointId).Distinct().Count()==9,"PENDING: each unresolved bot owns a distinct place");
        Check(!Runtime.TryPlanBot(new[]{Zone},true,true,out _,out _,out _),"PENDING: nine native requests plus local occupy whole box");
        Check(Runtime.TryPlanBot(new[]{Zone},false,false,out _,out _,out _),"FACTION: opposite box independently available");
        var lease=leases[0];var arrival=Body("arrived",EPlayerSide.Usec,lease.Position);
        Check(lease.Accept(arrival),"ARRIVAL: exact registered body accepted");
        var impostor=Body("arrived",EPlayerSide.Usec,lease.Position,false);
        arrival.Position=lease.Position+new Vector3(20,0,0);
        Check(!lease.Accept(impostor),"ARRIVAL: wrong body rejected");
        arrival.Position=lease.Position;
        arrival.Position+=new Vector3(.5f,0,0);var actual=arrival.Position;Physics.Clear=p=>p!=actual;
        Check(!lease.Accept(arrival),"ARRIVAL-BODY: actual capsule required inside tolerance");
        Physics.Clear=p=>true;NavMesh.Sample=p=>p!=actual;
        Check(!lease.Accept(arrival),"ARRIVAL-BODY: actual NavMesh required inside tolerance");
        NavMesh.Sample=p=>true;arrival.Position=lease.Position+new Vector3(1,0,0);
        Check(!lease.Accept(arrival),"ARRIVAL: displaced body rejected");
        arrival.Position=lease.Position;arrival.Profile.Info.Side=EPlayerSide.Bear;
        Check(!lease.Accept(arrival),"ARRIVAL: wrong faction rejected");
        arrival.Profile.Info.Side=EPlayerSide.Usec;ArenaSession.ArmEpoch++;
        Check(!lease.Accept(arrival),"ARRIVAL: changed arm epoch rejected");
        Setup();lease=Reserve();arrival=Body("late",EPlayerSide.Usec,lease.Position);Runtime.Reset();
        Check(!lease.Accept(arrival),"ARRIVAL: reset rejects late callback");
        Setup();lease=Reserve();World.AllAlivePlayersList.Add(Body("near",EPlayerSide.Usec,lease.Position,false));
        Check(!lease.CanReleasePlayer,"OCCUPANCY: living occupant invalidates pending release");
        Setup();lease=Reserve();lease.Dispose();Check(lease.CanReleasePlayer==false,"DISPOSE: released lease loses authority");
    }
    private sealed class FixedSelector:ISpawnSelector
    {public (Vector3 position,Quaternion rotation) Resolve(LocalGame game)=>(new(99,0,99),default);}
    private static void PlayerHandover()
    {
        Setup();var fixedSelector=new FixedSelector();
        Check(Runtime.TryPrepareRespawn(Game,fixedSelector,out var unchanged)&&ReferenceEquals(unchanged,fixedSelector),"FIXED: in-place kit swap untouched");
        World.MainPlayer.Position=new(99,0,99);Check(Runtime.ReleasePlayerReservation(World.MainPlayer),"FIXED: in-place arrival needs no box reservation");
        Setup();Check(Runtime.TryPrepareRespawn(Game,new ArenaMode.Respawn.ArenaSpawnSelector(),out var prepared),"PLAYER: preflight reserves");
        var selected=prepared.Resolve(Game);var old=World.MainPlayer;old.HealthController.IsAlive=false;
        World.MainPlayer=Body("local",EPlayerSide.Usec,selected.position);
        Check(Runtime.ReleasePlayerReservation(World.MainPlayer),"PLAYER: exact new body accepted");
        Setup();Runtime.TryPrepareRespawn(Game,new ArenaMode.Respawn.ArenaSpawnSelector(),out prepared);selected=prepared.Resolve(Game);
        Body("intruder",EPlayerSide.Usec,selected.position);
        Reject(()=>prepared.Resolve(Game),"PLAYER: occupant before handover rejects");
        Check(ArenaMode.Modes.ArenaMatchService.Aborts.Count==0,"PLAYER: caller owns protected Resolve failure");
        Setup();Runtime.TryPrepareRespawn(Game,new ArenaMode.Respawn.ArenaSpawnSelector(),out prepared);selected=prepared.Resolve(Game);
        World.MainPlayer.HealthController.IsAlive=false;World.MainPlayer=Body("local",EPlayerSide.Usec,selected.position+new Vector3(.5f,0,0));
        var actual=World.MainPlayer.Position;Physics.Floor=p=>p!=actual;
        Check(!Runtime.ReleasePlayerReservation(World.MainPlayer)&&ArenaMode.Modes.ArenaMatchService.Aborts.Count==1,"PLAYER-ARRIVAL: unsafe actual body aborts");
        Setup();Physics.Clear=p=>false;
        Check(!Runtime.TryPrepareRespawn(Game,new ArenaMode.Respawn.ArenaSpawnSelector(),out _)&&ArenaMode.Modes.ArenaMatchService.Aborts.Count==1,"PLAYER: unavailable box protects before handover");
    }
    private static async Task LoadingFailures()
    {
        Setup(active:false);object[] stateArgs={Game,null};Invoke("BeforeLoad",stateArgs);var lease=stateArgs[1];
        Check(lease!=null,"LOADING: exact lease captured");int before=ArenaSession.CancelCount;InGameStatus.InRaid=true;
        var failure=new InvalidOperationException("fixture load failed");
        Check(ReferenceEquals(Invoke("FinalizeLoad",failure,lease),failure),"LOADING: native exception retained");
        Check(ArenaSession.CancelCount==before+1&&!ArenaSession.ArmedForNextRaid&&!InGameStatus.InRaid,"LOADING: sync failure cancels exact arm");
        Setup(active:false);stateArgs=new object[]{Game,null};Invoke("BeforeLoad",stateArgs);lease=stateArgs[1];before=ArenaSession.CancelCount;
        object[] asyncArgs={lease,Task.FromException(failure)};Invoke("AfterLoad",asyncArgs);
        try{await(Task)asyncArgs[1];throw new Exception("LOADING: expected async failure");}catch(InvalidOperationException){Checks++;}
        Check(ArenaSession.CancelCount==before+1,"LOADING: asynchronous failure cancels arm");
        Setup(active:false);stateArgs=new object[]{Game,null};Invoke("BeforeLoad",stateArgs);lease=stateArgs[1];
        Setup(active:false);before=ArenaSession.CancelCount;Invoke("FinalizeLoad",failure,lease);
        Check(ArenaSession.CancelCount==before&&ArenaSession.ArmedForNextRaid,"REARM: stale loading failure cannot cancel replacement");
        stateArgs=new object[]{Game,null};Invoke("BeforeLoad",stateArgs);lease=stateArgs[1];
        ArenaSession.ArmEpoch++;ArenaServerClient.PendingArmToken=Guid.NewGuid().ToString("N");
        before=ArenaSession.CancelCount;Invoke("FinalizeLoad",failure,lease);
        Check(ArenaSession.CancelCount==before&&ArenaSession.ArmedForNextRaid,"REARM-TOKEN: reused game cannot cancel another arm");
        Game.Settings.serverId="start:"+ArenaServerClient.PendingArmToken;
        stateArgs=new object[]{Game,null};Invoke("BeforeLoad",stateArgs);lease=stateArgs[1];
        ArenaSession.IsActive=true;before=ArenaSession.CancelCount;Invoke("FinalizeLoad",failure,lease);
        Check(ArenaSession.CancelCount==before,"LOADING: active raid abort belongs to match owner");
        Setup(active:false);Game.Settings.serverId="ordinary";stateArgs=new object[]{Game,null};Invoke("BeforeLoad",stateArgs);
        Check(stateArgs[1]==null,"LOADING: unconfirmed identity cannot own cleanup");
        Reject(()=>Invoke("RestoreCollection",Collection),"LOADING: bad arm identity cannot restore markers");
    }
    private static async Task Main()
    {
        foreach(var map in new[]{"arena_equator","arena_bay5"})
        {
            Formation=Catalog.Find(map);
            ScopeAndFormation();HeadersAndInitial();ReservationsAndArrival();PlayerHandover();await LoadingFailures();
            Setup();World.LocationId=map=="arena_equator"?"arena_bay5":"arena_equator";
            Check(Runtime.IsRequired,"MAP-DRIFT: supported replacement remains required");
            Reject(()=>Runtime.Begin(World,Game),"MAP-DRIFT: old formation cannot authorize replacement map");
            Setup(active:false);ArenaSession.ArmedMap=map=="arena_equator"?"arena_bay5":"arena_equator";
            object[] args={Game,null};Invoke("BeforeLoad",args);
            Check(args[1]==null,"CROSS-MAP: loading cannot capture another armed map");
            var before=Collection._dictionary.Keys.Select(p=>p.Position).ToArray();
            Invoke("RestoreCollection",Collection);
            Check(Collection._dictionary.Keys.Select(p=>p.Position).SequenceEqual(before),"CROSS-MAP: no mismatched collection write");
        }
        Runtime.Shutdown();Console.WriteLine($"EQUATOR_TEAM_SPAWN_CHECKS_PASSED ({Checks} assertions)");
    }
}
