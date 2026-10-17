using System.Reflection;
using System.Runtime.ExceptionServices;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using Runtime = ArenaMode.Bots.ArenaEquatorTeamSpawns;
using Gates = ArenaMapPort.Client.SpawnGates;

internal sealed class FixturePlugin { }
internal static class GateProgram
{
    private static int _checks;
    private static LocalGame Game => Singleton<AbstractGame>.Instance as LocalGame;
    private static GameWorld World => Singleton<GameWorld>.Instance;
    private static void Check(bool ok, string label) { _checks++; if (!ok) throw new Exception(label); }
    private static object Call(Type type, string method, params object[] args)
    {
        try { return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Call(string method, params object[] args) => Call(typeof(Runtime), method, args);
    private static void Reject(Action action, string label)
    { try { action(); } catch (InvalidOperationException) { _checks++; return; } throw new Exception(label); }
    private static object Collection => typeof(Program).GetField("Collection", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static void Setup(bool active = true, bool installed = true, bool enabled = true, bool preheld = false)
    {
        NavMesh.Status = null; NavMesh.Corners = null;
        Call(typeof(Program), "Setup", false, EPlayerSide.Usec, 4, false);
        BepInEx.Bootstrap.Chainloader.PluginInfos.Clear(); Gates.Reset();
        Gates.Enabled = enabled; Gates.Held = preheld;
        if (installed) BepInEx.Bootstrap.Chainloader.PluginInfos["com.maschine.ArenaMapPort"] = new() { Instance = new FixturePlugin() };
        Call("RestoreCollection", Collection);
        if (active) { ArenaSession.IsActive = true; ArenaSession.ArmedForNextRaid = false; Game.Status = GameStatus.Started; }
        Time.time = Time.realtimeSinceStartup = 0f;
    }
    private static void SealedPaths()
    {
        // Physical gate carve splits the atrium path, booth's interior remains
        // navigable. This seam reports actual path statuses, never changes them.
        NavMesh.Path = (a, b) => Math.Abs(b.z - 2f) > .001f;
    }
    private static ArenaMapPortSpawnGates.Lease Hold(Type type)
        => ArenaMapPortSpawnGates.TryHoldContract(type, Game, World, ArenaSession.ArmEpoch, ArenaServerClient.PendingArmToken);
    private static bool Connected(Vector3 p) => (bool)Call("Connected", p);
    private static void OptionalContracts()
    {
        Setup(installed:false); Check(Gates.Holds == 0, "ABSENT: optional MapPort untouched"); Runtime.Begin(World, Game);
        Setup(enabled:false); Check(Gates.Holds == 1 && !Gates.Held, "DISABLED: false hold leaves original openings");
        Runtime.Reset(); Check(Gates.Releases == 0, "DISABLED: no unowned release");
        Setup(preheld:true); Check(Gates.Holds == 0, "FOREIGN: preexisting hold not borrowed");
        SealedPaths(); Reject(() => Runtime.Begin(World,Game), "FOREIGN-PATH: preexisting hold cannot license disconnected starts");
        Runtime.Reset(); Check(Gates.Releases == 0 && Gates.Held, "FOREIGN: cleanup preserves foreign hold");
        foreach (var type in new[] { typeof(MissingContract), typeof(WrongContract), typeof(AmbiguousContract) })
        { Setup(active:false,installed:false); Check(Hold(type) == null, "MISMATCH: incompatible optional contract skipped"); }
        Setup(active:false,installed:false); Gates.Parts=0; Check(Hold(typeof(Gates))==null && Gates.Holds==0,"NO-PARTS: no menu/future-scene hold");
        Setup(active:false,installed:false); ArenaSession.ArmedMode="deathmatch"; Check(Hold(typeof(Gates))==null && Gates.Holds==0,"NON-TDM: ordinary hold denied");
        Setup(active:false,installed:false); ArenaSession.ArmedForNextRaid=false; Check(Hold(typeof(Gates))==null,"UNARMED: no ordinary hold");
        Setup(active:false,installed:false); Game.Settings.serverId="ordinary"; Check(Hold(typeof(Gates))==null,"TOKEN: unconfirmed arm cannot hold");
        Setup(active:false,installed:false); Gates.ThrowHold=true; Check(Hold(typeof(Gates))==null&&!Gates.Held&&Gates.Releases==1,"THROW: changed request unwinds");
    }
    private static void ScopedNavigation()
    {
        Setup(); Check(Gates.Held&&Gates.Holds==1,"HOLD: exact loading acquires once");
        SealedPaths(); Runtime.Begin(World, Game);
        Check(Connected(World.MainPlayer.Position),"BOOTH: exact owned point keeps local full path");
        Check(!Connected(new Vector3(0,.08f,30)),"BOOTH: outside original interior rejected");
        Check(!Connected(new Vector3(9,.08f,48)),"CANONICAL: unsurveyed booth location rejected");
        NavMesh.Status=(a,b)=>NavMeshPathStatus.PathInvalid;
        Check(!Connected(World.MainPlayer.Position),"INVALID: not excused by held gate");
        NavMesh.Status=null; NavMesh.Path=(a,b)=>false;
        Check(!Connected(World.MainPlayer.Position),"LOCAL-PATH: disconnected booth anchor rejected");
        SealedPaths(); NavMesh.Corners=(a,b)=>new[]{a,new Vector3(0,.08f,2),b};
        Check(!Connected(World.MainPlayer.Position),"CORNERS: interior path cannot leave own booth");
        NavMesh.Corners=null; Physics.Clear=p=>Math.Abs(p.x-.299996f)>.0001f;
        Check(!Connected(World.MainPlayer.Position),"ANCHOR: actual standing clearance required");
        Physics.Clear=p=>true;
        ArenaSession.ArmEpoch++;
        Check(!Connected(World.MainPlayer.Position),"STALE-PATH: old arm grants no exception");
        Runtime.Reset(); Check(Gates.Releases==0,"STALE-RELEASE: old arm cannot release new authority");
        Setup(); var old=World; Singleton<GameWorld>.Instance=new GameWorld();
        Runtime.Reset(); Check(Gates.Releases==0,"WORLD: old world cannot release successor");
        Setup(); Runtime.Reset(); Runtime.Reset(); Check(Gates.Releases==1&&!Gates.Held,"RESET: own hold released exactly once");
        Setup(); var oldGame=Game;var oldWorld=World;var oldEpoch=ArenaSession.ArmEpoch;
        Setup();Runtime.ReleaseGates(oldGame,oldWorld,oldEpoch,"stale match ending");
        Check(Gates.Held&&Gates.Releases==0,"CALLER: stale mode cannot release new raid hold");
        Runtime.ReleaseGates(Game,World,ArenaSession.ArmEpoch-1,"stale epoch");
        Check(Gates.Held&&Gates.Releases==0,"CALLER-EPOCH: reused object cannot release new arm");
        Setup(active:false); Call("RestoreCollection",Collection); Check(Gates.Holds==1,"COLLECTION: repeated collection does not rehold");
        Setup();SealedPaths();var lease=(Runtime.Lease)Call(typeof(Program),"Reserve",true);
        var zone=(BotZone)typeof(Program).GetField("Zone",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(Runtime.TryPlanBot(new[]{zone},true,false,out _,out var next,out _)&&next.Id!=lease.PointId,
            "RESERVATION: closed gates retain promised places");
        var arrival=new Player{Profile=new(){Id="arrived",Info=new(){Side=EPlayerSide.Usec}},Position=lease.Position};
        World.AllAlivePlayersList.Add(arrival);
        Check(lease.Accept(arrival),"ARRIVAL: real own-box body accepted while sealed");
        Check(!Runtime.TryOpenForCombat(),"RESERVATION: opening waits but does not clear leases");
        NavMesh.Path=(a,b)=>true;
        Check(lease.Accept(arrival)&&Runtime.TryOpenForCombat(),"RESERVATION: pending lease survives opening/full-path revalidation");
    }
    private static async Task Lifecycle()
    {
        Setup(active:false); object[] args={Game,null};Call("BeforeLoad",args);
        var failure=new InvalidOperationException("failed load");Call("FinalizeLoad",failure,args[1]);
        Check(!Gates.Held&&Gates.Releases==1,"LOAD-FAIL: synchronous failed load releases hold");
        Setup(active:false);args=new object[]{Game,null};Call("BeforeLoad",args);
        var task=(Task)Call("ObserveLoad",Task.FromException(failure),args[1]);
        try{await task;}catch(InvalidOperationException){ }
        Check(!Gates.Held&&Gates.Releases==1,"ASYNC-FAIL: async failed load releases hold");
        Setup(active:false);args=new object[]{Game,null};Call("BeforeLoad",args);
        ArenaSession.ArmedForNextRaid=false; // consumed, but ConfirmRaidStarted rejected it
        await (Task)Call("ObserveLoad",Task.CompletedTask,args[1]);
        Check(!Gates.Held&&Gates.Releases==1,"REJECTED-START: successful native loading cannot leak rejected hold");
        Setup();Call("AfterStop",Game,Game.Profile.Id);Check(Gates.Releases==0&&Gates.Held,"STOP-SUPPRESSED: life-only Give Up leaves gates held");
        Game.Status=GameStatus.Stopping;Call("AfterStop",Game,"other-profile");Check(Gates.Releases==0&&Gates.Held,"STOP-PROFILE: unrelated extraction cannot release local hold");
        Call("AfterStop",Game,Game.Profile.Id);Runtime.Reset();Check(Gates.Releases==1,"STOP: committed native shutdown releases once");
        Setup();Runtime.Shutdown();Check(Gates.Releases==1,"SHUTDOWN: plugin shutdown releases");
    }
    private static void OpeningAndCountdown()
    {
        Setup(); SealedPaths(); Runtime.Begin(World,Game);
        var flow=new ArenaMode.Modes.GateCountdownFixture();
        flow.Tick(false);Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Assembling&&Gates.Releases==0,"ASSEMBLY: hold while roster incomplete");
        flow.Tick(true);Time.time=2.99f;flow.Tick(true);
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown&&Gates.Releases==0,"COUNTDOWN: keep hold for full preparation");
        Time.time=3;flow.Tick(true);
        Check(Gates.Releases==1&&!Gates.Held&&Runtime.GatesOpening,"RELEASE: countdown releases owned gates");
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown,"OPEN-WAIT: slide cannot start scoring/time");
        Check(!Connected(World.MainPlayer.Position),"POST-RELEASE: partial never valid during slide");
        Time.realtimeSinceStartup=2;Time.time=5;NavMesh.Path=(a,b)=>true;flow.Tick(true);
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Live&&flow.StartedAt==5,"LIVE: real full paths precede live clock");
        Check(Gates.Releases==1&&!Runtime.GatesOpening,"ONCE: release complete");
        Runtime.PickRespawn(Game,default); Runtime.TryOpenForCombat();
        Check(Gates.Holds==1&&Gates.Releases==1&&!Gates.Held,"RESPAWN: no reclose between lives");
        Setup();SealedPaths();Check(!Runtime.TryOpenForCombat(),"OPEN: wait for real navigation");
        Time.realtimeSinceStartup=100;bool pausedWait=false;try{pausedWait=!Runtime.TryOpenForCombat();}catch(InvalidOperationException){}
        Check(pausedWait,"PAUSE: animation pause cannot expire the opening watchdog");
        Time.time=10;Reject(()=>Runtime.TryOpenForCombat(),"TIMEOUT: permanently disconnected opening fails closed");
        Setup(enabled:false);var optional=new ArenaMode.Modes.GateCountdownFixture(); optional.Tick(true);Time.time=3;optional.Tick(true);
        Check(optional.Phase==ArenaMode.Modes.TdmMatchPhase.Live,"FALLBACK: disabled feature retains original match start");
        Setup();SealedPaths();var reset=new ArenaMode.Modes.GateCountdownFixture();reset.Tick(true);Time.time=2;reset.Tick(false);
        Time.time=8;reset.Tick(true);Time.time=10.99f;reset.Tick(true);Check(Gates.Releases==0,"READINESS: cancelled countdown restarts full duration");
        Time.time=11;reset.Tick(true);Check(Gates.Releases==1,"READINESS: eventual ready countdown opens");
    }
    private static EFT.Interactive.ExfiltrationPoint Exit(string name="Equator North Gate", bool registered=true)
    {
        var point=new EFT.Interactive.ExfiltrationPoint{Settings=new(){Name=name}};point.Entered.Add(World.MainPlayer);
        CommonAssets.Scripts.Game.ExfiltrationController.Instance=new(){ExfiltrationPoints=registered?new[]{point}:Array.Empty<EFT.Interactive.ExfiltrationPoint>()};
        return point;
    }
    private static bool Proceed(EFT.Interactive.ExfiltrationPoint point,Player player=null)
    {
        player??=World.MainPlayer;
        bool run=(bool)Call("BeforeExtraction",point,player);
        if(run)point.Proceed(player);
        return run;
    }
    private static void ExtractionPreparation()
    {
        foreach(var name in new[]{"Equator North Gate","Equator South Gate"})
        {
            Setup();SealedPaths();var exit=Exit(name);
            Check(!Proceed(exit)&&exit.Proceeds==0,"EXFIL: original booth starts cannot extract during assembly");
            Check(!Runtime.TryOpenForCombat()&&!Proceed(exit)&&exit.Proceeds==0,"EXFIL-SLIDE: no native timer during opening");
            NavMesh.Path=(a,b)=>true;Check(Runtime.TryOpenForCombat()&&exit.Proceeds==1,"EXFIL-RESUME: still-entered own body gets fresh native timer after opening");
            Runtime.TryOpenForCombat();Check(exit.Proceeds==1,"EXFIL-ONCE: deferred timer replay occurs once");
            Check(Proceed(exit)&&exit.Proceeds==2,"EXFIL-LIVE: ordinary native extraction restored in combat");
        }
        Setup(active:false);var loading=Exit();Check(!Proceed(loading),"EXFIL-LOADING: exact armed local body protected before active latch");
        foreach(var fault in new[]{"ordinary","other-mode","other-map","other-body","unregistered","unknown-exit","not-entered","foreign-hold","disabled"})
        {
            Setup(preheld:fault=="foreign-hold",enabled:fault!="disabled");var exit=Exit(fault=="unknown-exit"?"Other exit":"Equator North Gate",fault!="unregistered");
            Player player=World.MainPlayer;
            switch(fault)
            {
                case "ordinary":ArenaSession.IsActive=false;break;
                case "other-mode":ArenaSession.ActiveMode="deathmatch";break;
                case "other-map":World.LocationId="factory4_day";break;
                case "other-body":player=new Player{Profile=player.Profile};exit.Entered.Add(player);break;
                case "not-entered":exit.Entered.Clear();break;
            }
            Check(Proceed(exit,player),"EXFIL-SCOPE: unchanged "+fault);
        }
        foreach(var fault in new[]{"left","replacement","dead","old-trigger","reset","failure","wrong-world"})
        {
            Setup();var exit=Exit();Check(!Proceed(exit),"EXFIL-PENDING: captured before "+fault);
            switch(fault)
            {
                case "left":exit.Entered.Clear();break;
                case "replacement":World.MainPlayer=new Player{Profile=World.MainPlayer.Profile};World.AllAlivePlayersList.Add(World.MainPlayer);break;
                case "dead":World.MainPlayer.HealthController.IsAlive=false;break;
                case "old-trigger":CommonAssets.Scripts.Game.ExfiltrationController.Instance.ExfiltrationPoints=Array.Empty<EFT.Interactive.ExfiltrationPoint>();break;
                case "reset":Runtime.Reset();break;
                case "failure":Runtime.ReleaseGates(Game,World,ArenaSession.ArmEpoch,"fixture failure");break;
                case "wrong-world":Singleton<GameWorld>.Instance=new GameWorld();break;
            }
            if(fault=="reset"||fault=="wrong-world")Call("ResumeExtractions");else Runtime.TryOpenForCombat();
            Check(exit.Proceeds==0,"EXFIL-STALE: no deferred extraction for "+fault);
        }
    }
    public static async Task Main()
    {
        OptionalContracts();ScopedNavigation();await Lifecycle();OpeningAndCountdown();ExtractionPreparation();Runtime.Shutdown();
        Console.WriteLine($"EQUATOR_SPAWN_GATE_CHECKS_PASSED ({_checks} assertions)");
    }
    public static class MissingContract { }
    public static class WrongContract
    { public static int Hold(string reason)=>1;public static void Release(string reason){}public static bool IsHeld=>false;public static int ClosedParts=>15; }
    public static class AmbiguousContract
    { public static bool Hold(string reason)=>true;public static bool Hold<T>(string reason)=>true;public static void Release(string reason){}public static bool IsHeld=>false;public static int ClosedParts=>15; }
}

namespace ArenaMapPort.Client
{
    public static class SpawnGates
    {
        public static bool Enabled,Held,ThrowHold;public static int Holds,Releases,Parts;
        public static bool IsHeld=>Held;
        public static int ClosedParts=>Parts;
        public static bool Hold(string reason){Holds++;if(!Enabled)return false;Held=true;if(ThrowHold)throw new InvalidOperationException("fixture hold failed");return true;}
        public static void Release(string reason){Releases++;Held=false;}
        public static void Reset(){Enabled=true;Held=ThrowHold=false;Holds=Releases=0;Parts=15;}
    }
}
