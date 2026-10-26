using System.Reflection;
using System.Runtime.ExceptionServices;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using Runtime = ArenaMode.Bots.ArenaTeamBoxRuntime;
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
        // The shared spawn fixture now accepts an optional virtual team. This
        // gate suite deliberately retains its profile-side USEC baseline.
        Call(typeof(Program), "Setup", false, EPlayerSide.Usec, 4, false, null);
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
        var destination=Program.Formation.Destination;
        NavMesh.Path = (a, b) => Math.Abs(b.x-destination.X)>.001f || Math.Abs(b.z-destination.Z)>.001f;
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
        // Native TarkovApplication awaits Run before calling OnGameStarted.
        // GameStatus already says Started during this still-armed gap.
        Setup(active:false);args=new object[]{Game,null};Call("BeforeLoad",args);Game.Status=GameStatus.Started;SealedPaths();
        await (Task)Call("ObserveLoad",Task.CompletedTask,args[1]);
        Check(Gates.Held&&Gates.Releases==0,"RUN-BEFORE-EVENT: completed native load must retain the armed hold");
        Redeploy.Core.Engine.RaidLifecycle.RaiseStarted(World); // defensive: arm not consumed yet
        Check(Gates.Held&&Gates.Releases==0,"UNCONSUMED: no early event may release an armed hold");
        ArenaSession.ArmedForNextRaid=false;ArenaSession.IsActive=true;
        Runtime.Begin(World,Game);Redeploy.Core.Engine.RaidLifecycle.RaiseStarted(World);
        Check(Gates.Held&&Gates.Releases==0,"CONFIRMED-START: native local-path admission retains preparation hold");
        Setup(active:false);args=new object[]{Game,null};Call("BeforeLoad",args);Game.Status=GameStatus.Started;
        await (Task)Call("ObserveLoad",Task.CompletedTask,args[1]);
        ArenaSession.ArmedForNextRaid=false; // consumed, but ConfirmRaidStarted rejected it
        Redeploy.Core.Engine.RaidLifecycle.RaiseStarted(World);
        Check(!Gates.Held&&Gates.Releases==1,"REJECTED-START: later native start event releases rejected hold");
        Redeploy.Core.Engine.RaidLifecycle.RaiseStarted(World);
        Check(Gates.Releases==1,"REJECTED-ONCE: duplicate native event does not release again");
        foreach(var stale in new[]{"world","game","epoch","token","started-pair"})
        {
            Setup(active:false);ArenaSession.ArmedForNextRaid=false;var eventWorld=World;
            switch(stale)
            {
                case "world":eventWorld=new GameWorld();break;
                case "game":Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=World};break;
                case "epoch":ArenaSession.ArmEpoch++;break;
                case "token":Game.Settings.serverId="ordinary";break;
                case "started-pair":ArenaSession.ExactWorld=new GameWorld();break;
            }
            Redeploy.Core.Engine.RaidLifecycle.RaiseStarted(eventWorld);
            Check(Gates.Held&&Gates.Releases==0,"STALE-START: callback cannot release different "+stale);
        }
        Setup();Call("AfterStop",Game,Game.Profile.Id);Check(Gates.Releases==0&&Gates.Held,"STOP-SUPPRESSED: life-only Give Up leaves gates held");
        Game.Status=GameStatus.Stopping;Call("AfterStop",Game,"other-profile");Check(Gates.Releases==0&&Gates.Held,"STOP-PROFILE: unrelated extraction cannot release local hold");
        Call("AfterStop",Game,Game.Profile.Id);Runtime.Reset();Check(Gates.Releases==1,"STOP: committed native shutdown releases once");
        Setup();Check(Redeploy.Core.Engine.RaidLifecycle.Subscribers==1,"SUBSCRIBE: exactly one native-start observer");
        Runtime.Enable();Check(Redeploy.Core.Engine.RaidLifecycle.Subscribers==1,"SUBSCRIBE: repeated enable remains singular");
        Runtime.Shutdown();Check(Gates.Releases==1&&Redeploy.Core.Engine.RaidLifecycle.Subscribers==0,"SHUTDOWN: plugin shutdown releases and unsubscribes");
        SPT.Reflection.Patching.ModulePatch.Fail=true;Runtime.Enable();
        Check(!Runtime.IsReady&&Redeploy.Core.Engine.RaidLifecycle.Subscribers==0,"HOOK-FAIL: no native-start subscription survives failed enable");
        SPT.Reflection.Patching.ModulePatch.Fail=false;
    }
    private static void OpeningAndCountdown()
    {
        foreach (bool domination in new[] { false, true })
        {
        Setup(); SealedPaths(); Runtime.Begin(World,Game);
        var flow=new ArenaMode.Modes.GateCountdownFixture { IsDomination = domination };
        flow.Tick(false);Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Assembling&&Gates.Releases==0,"ASSEMBLY: hold while roster incomplete");
        flow.Tick(true);Time.time=2.99f;flow.Tick(true);
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown&&Gates.Releases==0,"COUNTDOWN: keep hold for full preparation");
        Time.time=3;flow.Tick(true);
        Check(Gates.Releases==1&&!Gates.Held&&Runtime.GatesOpening,"RELEASE: countdown releases owned gates");
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown,"OPEN-WAIT: slide cannot start scoring/time");
        Check(flow.ObjectiveRouteChecks==0,"OBJECTIVE-WAIT: closed gates cannot validate objective routes");
        Check(!Connected(World.MainPlayer.Position),"POST-RELEASE: partial never valid during slide");
        Time.realtimeSinceStartup=2;Time.time=5;NavMesh.Path=(a,b)=>true;flow.Tick(true);
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Live&&flow.StartedAt==5,"LIVE: real full paths precede live clock");
        Check(flow.ObjectiveRouteChecks==(domination?1:0),"OBJECTIVE-PATH: Domination validates both-team routes before live");
        Check(Gates.Releases==1&&!Runtime.GatesOpening,"ONCE: release complete");
        Runtime.PickRespawn(Game,default); Runtime.TryOpenForCombat();
        Check(Gates.Holds==1&&Gates.Releases==1&&!Gates.Held,"RESPAWN: no reclose between lives");
        }
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
    public static async Task Main()
    {
        OptionalContracts();ScopedNavigation();await Lifecycle();OpeningAndCountdown();
        OriginalBoxes(ArenaMode.Shared.ArenaTeamBoxCatalog.Bay5);
        OriginalBoxes(ArenaMode.Shared.ArenaTeamBoxCatalog.AirPit);
        OriginalBoxes(ArenaMode.Shared.ArenaTeamBoxCatalog.Bowl);
        BowlOpening();CompletionSignal();Runtime.Shutdown();
        Console.WriteLine($"EQUATOR_SPAWN_GATE_CHECKS_PASSED ({_checks} assertions)");
    }
    private static void OriginalBoxes(ArenaMode.Shared.ArenaTeamBoxCatalog.Formation selected)
    {
        Program.Formation=selected;
        if (selected == ArenaMode.Shared.ArenaTeamBoxCatalog.AirPit)
        {
            Setup(active:false,installed:false);
            Gates.Parts=42; Gates.HeldGatesClosed=true;
            Check(!Gates.IsHeld,"AIRPIT-NATIVE: closed passport gate is not a foreign hold");
            Runtime.Reset();
            Check(Gates.Releases==0 && Gates.HeldGatesClosed,"AIRPIT-UNOWNED: cleanup cannot open native passport gate");
            Gates.Release("unowned test");
            Check(Gates.HeldGatesClosed,"AIRPIT-CONTRACT: release without hold leaves native gate shut");
            var lease=Hold(typeof(Gates));
            Check(lease != null && Gates.Held,"AIRPIT-OWNED: native gate can acquire an exact raid hold");
            lease.Release("test complete");
            Check(!Gates.Held && !Gates.HeldGatesClosed,"AIRPIT-OPEN: owned release opens passport gate");
        }
        OptionalContracts();
        Setup();
        var formation=Program.Formation;
        void Sealed() => NavMesh.Path=(a,b)=>Math.Abs(b.x-formation.Destination.X)>.001f;
        Sealed();Runtime.Begin(World,Game);
        foreach(var spec in formation.Points)
            Check(Connected(new Vector3(spec.X,spec.Y,spec.Z)),"BOX-SEALED: original point including authored elevations "+spec.Id);
        var first=formation.Points[0];
        Check(!Connected(new Vector3(first.X+.76f,first.Y,first.Z+.76f)),"BOX-CANONICAL: unsurveyed interior denied");
        var other=formation.BoothAnchor(false);
        NavMesh.Corners=(a,b)=>new[]{a,new Vector3(other.X,other.Y,other.Z),b};
        Check(!Connected(World.MainPlayer.Position),"BOX-CROSS-BOX: path cannot cross into other faction interior");
        NavMesh.Corners=null;
        var flow=new ArenaMode.Modes.GateCountdownFixture();flow.Tick(true);Time.time=3;flow.Tick(true);
        Check(!Gates.Held&&flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown,"BOX-SLIDE: wait for all real paths");
        NavMesh.Path=(a,b)=>true;flow.Tick(true);
        if (selected == ArenaMode.Shared.ArenaTeamBoxCatalog.Bowl)
        {
            Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Countdown,"BOWL-DRIVE: alternate exit path cannot skip unfinished gate pose");
            Time.time=16.5f;flow.Tick(true);
        }
        Check(flow.Phase==ArenaMode.Modes.TdmMatchPhase.Live,"BOX-LIVE: full paths precede score/time");
        Runtime.PickRespawn(Game,default);Call("RestoreCollection",Collection);
        Check(Gates.Holds==1&&Gates.Releases==1,"BOX-RESPAWN: no reclose after release");
        Setup(active:false,installed:false);ArenaSession.ArmedMap="arena_equator";
        Check(Hold(typeof(Gates))==null&&Gates.Holds==0,"CROSS-MAP-HOLD: supported different armed map denied");
        Setup(active:false,installed:false);Game.Settings.location="arena_equator";
        Check(Hold(typeof(Gates))==null&&Gates.Holds==0,"CROSS-MAP-SETTINGS: native settings must match world");
        Program.Formation=ArenaMode.Shared.ArenaTeamBoxCatalog.Equator;
    }
    private static void BowlOpening()
    {
        Program.Formation=ArenaMode.Shared.ArenaTeamBoxCatalog.Bowl;
        Setup();SealedPaths();
        Check(!Runtime.TryOpenForCombat(),"BOWL-OPEN: own release starts once");
        NavMesh.Path=(a,b)=>true;
        Time.time=10f;
        Check(!Runtime.TryOpenForCombat(),"BOWL-DRIVE: old ten-second watchdog cannot abort the longer drive");
        Time.time=13.49f;Time.realtimeSinceStartup=300f;
        Check(!Runtime.TryOpenForCombat(),"BOWL-PAUSE: real time and early connected exit cannot finish authored pose");
        Time.time=13.5f;SealedPaths();
        Check(!Runtime.TryOpenForCombat(),"BOWL-NAV: elapsed drive never manufactures an outside path");
        NavMesh.Path=(a,b)=>true;
        Check(Runtime.TryOpenForCombat(),"BOWL-LIVE: elapsed drive and all real paths allow combat");
        Check(Gates.Releases==1,"BOWL-ONCE: no repeated release during long drive");
        Setup();SealedPaths();Runtime.TryOpenForCombat();Time.time=19.99f;
        Check(!Runtime.TryOpenForCombat(),"BOWL-TIMEOUT: navigation receives the remaining settle budget");
        Time.time=20f;Reject(()=>Runtime.TryOpenForCombat(),"BOWL-TIMEOUT: disconnected formation fails closed");
        Setup();Runtime.TryOpenForCombat();Runtime.Reset();
        Check(Gates.Releases==1&&!Gates.Held&&!Runtime.GatesOpening,"BOWL-CANCEL: opening cleanup leaves no hold or pose timer");
        Program.Formation=ArenaMode.Shared.ArenaTeamBoxCatalog.Equator;
    }
    private static ArenaMapPortSpawnGates.Lease SetupCompletion(Type type = null)
    {
        Setup(active:false,installed:false);
        var lease=Hold(type ?? typeof(CompletionContract));
        Check(lease != null && Gates.Held,"SIGNAL-HOLD: exact owned request accepted");
        typeof(Runtime).GetField("_gates",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,lease);
        ArenaSession.IsActive=true;ArenaSession.ArmedForNextRaid=false;Game.Status=GameStatus.Started;
        return lease;
    }
    private static void CompletionSignal()
    {
        foreach (var formation in new[] { ArenaMode.Shared.ArenaTeamBoxCatalog.Equator,
                     ArenaMode.Shared.ArenaTeamBoxCatalog.Bay5, ArenaMode.Shared.ArenaTeamBoxCatalog.AirPit,
                     ArenaMode.Shared.ArenaTeamBoxCatalog.Bowl })
        {
            Program.Formation=formation;
            var lease=SetupCompletion();NavMesh.Path=(a,b)=>true;
            Check(!lease.OpeningComplete(100f,0f) && Gates.CompletionReads==0,"SIGNAL-BEFORE-RELEASE: never query before owned release");
            Check(!Runtime.TryOpenForCombat(),"SIGNAL-WAIT: complete paths cannot replace unfinished gate signal");
            Time.time=formation.MapId=="arena_bowl" ? 6.75f : formation.MapId=="arena_airpit" ? 1.5f : 3.5f;
            Gates.Ready=true;
            var first=formation.Points[0];
            NavMesh.Status=(a,b)=>Math.Abs(a.x-first.X)<.001f ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete;
            Check(!Runtime.TryOpenForCombat(),"SIGNAL-PATH: completion never replaces all20 complete paths");
            NavMesh.Status=null;Physics.Clear=p=>Math.Abs(p.x-first.X)>.001f;
            Check(!Runtime.TryOpenForCombat(),"SIGNAL-CLEARANCE: completion never replaces physical clearance");
            Physics.Clear=p=>true;
            Check(Runtime.TryOpenForCombat(),"SIGNAL-LIVE: actual completion and navigation bypass legacy minimum");
            Runtime.PickRespawn(Game,default);Runtime.TryOpenForCombat();
            Check(Gates.Holds==1&&Gates.Releases==1,"SIGNAL-RESPAWN: no reclose after completion");
            SetupCompletion();Gates.Ready=true;
            Check(Runtime.TryOpenForCombat(),"SIGNAL-SNAP: instantaneous gate completion has no artificial delay");
        }
        Program.Formation=ArenaMode.Shared.ArenaTeamBoxCatalog.Bowl;
        SetupCompletion();Check(!Runtime.TryOpenForCombat(),"SIGNAL-START: wait for drive");
        Time.time=19.99f;Time.realtimeSinceStartup=1000f;
        Check(!Runtime.TryOpenForCombat(),"SIGNAL-NO-FALLBACK: false signal stays authoritative beyond legacy minimum");
        Time.time=20f;Reject(()=>Runtime.TryOpenForCombat(),"SIGNAL-TIMEOUT: false signal still has bounded simulation-clock watchdog");
        SetupCompletion();Gates.ThrowCompletion=true;
        Reject(()=>Runtime.TryOpenForCombat(),"SIGNAL-THROW: failing getter aborts instead of reverting to time");
        Check(Gates.Releases==1,"SIGNAL-THROW-RELEASE: throwing query did not leave an owned hold");
        foreach (var fault in new[] { "epoch", "world", "game", "token" })
        {
            var lease=SetupCompletion();lease.Release("fixture");int reads=Gates.CompletionReads;
            switch(fault)
            {
                case "epoch":ArenaSession.ArmEpoch++;break;
                case "world":Singleton<GameWorld>.Instance=new GameWorld();break;
                case "game":Singleton<AbstractGame>.Instance=new LocalGame();break;
                case "token":Game.Settings.serverId="successor";break;
            }
            Check(!lease.OpeningComplete(100f,0f)&&Gates.CompletionReads==reads,"SIGNAL-OWNER: stale identity cannot query completion "+fault);
        }
        foreach(var type in new[]{typeof(WrongCompletion),typeof(InstanceCompletion),typeof(PrivateCompletion),
                     typeof(FieldCompletion),typeof(InheritedCompletion),typeof(IndexedCompletion)})
        {
            var lease=SetupCompletion(type);
            Check(lease.HasCompletionSignal,"SIGNAL-MALFORMED: advertised incompatible signal cannot become legacy fallback");
            lease.Release("fixture");
            Reject(()=>lease.OpeningComplete(100f,0f),"SIGNAL-MALFORMED: invalid contract fails protected opening");
        }
        SetupCompletion();Runtime.TryOpenForCombat();Gates.Ready=true;Gates.Held=true;
        Check(!Runtime.TryOpenForCombat(),"SIGNAL-REHOLD: contradictory new hold cannot start combat");
        Program.Formation=ArenaMode.Shared.ArenaTeamBoxCatalog.Equator;
    }
    public static class CompletionContract
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;
        public static bool GatesOpen { get { Gates.CompletionReads++;if(Gates.ThrowCompletion)throw new InvalidOperationException("fixture completion failed");return Gates.Ready; } }
    }
    public static class WrongCompletion
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;public static int GatesOpen=>1;
    }
    public class InstanceCompletion
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;public bool GatesOpen=>true;
    }
    public static class PrivateCompletion
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;private static bool GatesOpen=>true;
    }
    public static class FieldCompletion
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;public static bool GatesOpen=true;
    }
    public class CompletionBase { public static bool GatesOpen=>true; }
    public class InheritedCompletion : CompletionBase
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;
    }
    public class IndexedCompletion
    {
        public static bool Hold(string reason)=>Gates.Hold(reason);public static void Release(string reason)=>Gates.Release(reason);
        public static bool IsHeld=>Gates.IsHeld;public static int ClosedParts=>Gates.ClosedParts;
        [System.Runtime.CompilerServices.IndexerName("GatesOpen")] public bool this[int index]=>true;
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
        public static bool Enabled,Held,ThrowHold,HeldGatesClosed,Ready,ThrowCompletion;public static int Holds,Releases,Parts,CompletionReads;
        public static bool IsHeld=>Held;
        public static int ClosedParts=>Parts;
        public static bool Hold(string reason){Holds++;if(!Enabled)return false;Held=true;if(ThrowHold)throw new InvalidOperationException("fixture hold failed");return true;}
        public static void Release(string reason){Releases++;if(Held)HeldGatesClosed=false;Held=false;}
        public static void Reset(){Enabled=true;Held=ThrowHold=HeldGatesClosed=Ready=ThrowCompletion=false;Holds=Releases=CompletionReads=0;Parts=15;}
    }
}
