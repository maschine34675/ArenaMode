using ArenaMode.Combat.Translocator;
using ArenaMode.Areas;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using N=System.Numerics.Vector3;
using Disc=ArenaMode.Combat.Translocator.ArenaTranslocatorDisc;

static class Checks
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL: "+name);}
    static Disc.Trace Clear(N a,N b)=>new(Disc.TraceKind.Clear,b);
    static bool Inside(N a,N b)=>true;
    static (Player,Item) Setup()
    {
        ArenaTranslocatorRuntime.Reset(); ArenaTranslocatorSettings.Enabled.Value=true;
        ArenaSession.IsActive=ArenaSession.Exact=true;
        ArenaAreaRuntime.IsPending=false; ArenaAreaRuntime.IsReady=true; ArenaAreaRuntime.CurrentArea=null;
        ArenaMode.Respawn.ArenaRespawnDriver.StandDown=ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=false;
        ArenaMode.Spectator.ArenaSpectatorService.IsActive=false; ArenaMode.Hud.ArenaInputBlocker.Blocked=false;
        ArenaMode.Combat.WeaponSwapService.IsSwapping=false;
        var item=new Item(); var player=new Player {Owned=item,HandsController=new Player.ItemHandsController{Item=item}};
        var world=new GameWorld{MainPlayer=player,Alive=player};
        Singleton<GameWorld>.Instance=world; Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world,LocalPlayer=player};
        ArenaMatchService.Current=new ArenaMatch{Mode=new RoundsMode()}; ArenaMatchService.TeamStartAbortPending=false;
        Time.time=10;Time.deltaTime=.016f;Time.timeScale=1;
        Physics.Overlap=(_,_,_)=>0;Physics.Cast=(_,_,_,_,_)=>0;
        ArenaTranslocatorView.ThrowTick=false;ArenaTranslocatorView.Pulses=0;
        ArenaTranslocatorTeleport.Success=true;ArenaTranslocatorTeleport.Calls=0;ArenaTranslocatorTeleport.Callback=null;
        ArenaTranslocatorRuntime.Tick();return(player,item);
    }
    static void Main()
    {
        try {PhysicsChecks(); RuntimeChecks();Console.WriteLine($"TRANSLOCATOR_RUNTIME_CHECKS_PASSED assertions={checks}");}
        catch(Exception e){Console.WriteLine(e.Message);Environment.ExitCode=1;}
    }
    static void PhysicsChecks()
    {
        var disc=new Disc(new N(0,2,0),new N(18,2,0));int sweeps=0;
        for(int i=0;i<120;i++)disc.Advance(1f/120,(a,b)=>{sweeps++;return Clear(a,b);},Inside);
        Check(Math.Abs(disc.Position.X-18)<.001 && disc.Velocity.Y < -7.8f && disc.Velocity.Y > -8,"real ballistic flight");
        Check(sweeps==120,"every motion obtains a sweep");
        disc=new Disc(N.Zero,new N(18,0,0));sweeps=0;
        disc.Advance(10,(a,b)=>{sweeps++;return Clear(a,b);},Inside);
        Check(sweeps<=12 && disc.Position.X<=1.801f,"hitch work is bounded");
        var before=disc.Position;disc.Advance(0,Clear,Inside);disc.Advance(float.NaN,Clear,Inside);
        Check(disc.Position==before,"paused or invalid clock does not move");
        disc=new Disc(N.Zero,new N(18,0,0));sweeps=0;
        disc.Advance(.02f,(a,b)=>{sweeps++;return Clear(a,b);},(a,b)=>false);
        Check(disc.State==Disc.Flight.Lost && sweeps==0,"area crossing retires before motion");
        disc=new Disc(N.Zero,new N(18,0,0));
        disc.Advance(.02f,(a,b)=>new(Disc.TraceKind.Hit,new N(.05f,0,0),-N.UnitX),Inside);
        Check(disc.Position.X<=.05f && disc.Velocity.X<0,"thin wall first contact bounces without penetration");
        disc=new Disc(N.Zero,N.UnitX);sweeps=0;
        disc.Advance(.008f,(a,b)=>++sweeps==1 ? new(Disc.TraceKind.Hit,a,-N.UnitX) : new(Disc.TraceKind.Invalid,a),Inside);
        Check(disc.State==Disc.Flight.Lost,"impact separation cannot enter neighbouring wall");
        disc=new Disc(new N(0,.10f,0),new N(0,-1,0));
        disc.Advance(.008f,(a,b)=>new(Disc.TraceKind.Hit,new N(0,.085f,0),N.UnitY),Inside);
        Check(disc.State==Disc.Flight.Resting && disc.Velocity==N.Zero,"supported slow disc settles");
        before=disc.Position;disc.Advance(10,Clear,Inside);Check(before==disc.Position,"resting disc persists");
        disc.ReleaseSupport();disc.Advance(.008f,Clear,Inside);Check(disc.Position.Y<before.Y,"lost moving support resumes gravity");
        foreach(var kind in new[]{Disc.TraceKind.Invalid,Disc.TraceKind.Clear})
        {
            disc=new Disc(N.Zero,N.UnitX);
            disc.Advance(.01f,(a,b)=>new(kind,new N(100,0,0)),Inside);
            Check(disc.State==Disc.Flight.Lost,"unknown or substituted trace is not movement proof");
        }
        disc=new Disc(N.Zero,N.Zero);for(int i=0;i<310;i++)disc.Advance(.1f,Clear,Inside);
        Check(disc.State==Disc.Flight.Lost,"unbounded abyss flight expires");
        Check(new Disc(new N(float.NaN,0,0),N.Zero).State==Disc.Flight.Lost,"nonfinite launch rejected");
        Setup();Physics.Overlap=(_,_,_)=>64;
        Check(ArenaTranslocatorGeometry.Sweep(N.Zero,N.UnitX,new Player()).Kind==Disc.TraceKind.Invalid,"saturated overlap fails closed");
        Physics.Overlap=(_,_,_)=>0;Physics.Cast=(_,_,_,_,_)=>64;
        Check(ArenaTranslocatorGeometry.Sweep(N.Zero,N.UnitX,new Player()).Kind==Disc.TraceKind.Invalid,"saturated sweep fails closed");
        var bounds=new ArenaAreaBounds(new[]{new ArenaAreaVertex(0,0),new ArenaAreaVertex(4,0),new ArenaAreaVertex(4,1),new ArenaAreaVertex(1,1),new ArenaAreaVertex(1,4),new ArenaAreaVertex(0,4)},-10,10);
        ArenaAreaRuntime.CurrentArea=new ArenaAreaDefinition("test","test","factory4_day",bounds,-10,10,2);
        Check(!ArenaTranslocatorGeometry.Inside(new(.5f,0,3),new(3,0,.5f)),"concave area crossing rejects inside endpoints");
        Check(ArenaTranslocatorGeometry.Inside(new(.5f,0,3),new(.5f,0,1)),"legal area segment remains usable");
    }
    static void RuntimeChecks()
    {
        var(p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);
        Check(ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorRuntime.Launches==1,"primary creates exactly one owned disc");
        ArenaTranslocatorRuntime.Primary(p,item);Check(ArenaTranslocatorRuntime.DiscDeployed,"held input cannot immediately recall");
        Time.time+=.2f;ArenaTranslocatorRuntime.Primary(p,item);
        Check(!ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorRuntime.Recalls==1 && ReferenceEquals(p.Owned,item),"recall keeps reusable inventory device");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);Time.time+=.2f;
        ArenaTranslocatorTeleport.Success=false;ArenaTranslocatorRuntime.Secondary(p,item);
        Check(ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorRuntime.Teleports==0,"failed teleport preserves disc");
        Time.time+=.2f;ArenaTranslocatorTeleport.Success=true;var target=ArenaTranslocatorRuntime.Destination;
        ArenaTranslocatorRuntime.Secondary(p,item);
        Check(!ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorRuntime.Teleports==1 && (p.Position-target).sqrMagnitude<.001,"confirmed teleport consumes only disc");
        (p,item)=Setup();var gun=new GunGameMode();ArenaMatchService.Current.Mode=gun;
        ArenaTranslocatorRuntime.Primary(p,item);gun.AssistScoringOpen=false;ArenaTranslocatorRuntime.Tick();Time.time+=.2f;
        ArenaTranslocatorRuntime.Secondary(p,item);
        Check(ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorTeleport.Calls==0,"promotion preload retains disc but blocks actions");
        gun.AssistScoringOpen=true;ArenaTranslocatorRuntime.Secondary(p,item);
        Check(ArenaTranslocatorRuntime.Teleports==1,"disc usable after promotion preload");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);p.HandsController=new Player.ItemHandsController{Item=new Item()};
        ArenaTranslocatorRuntime.Tick();Time.time+=.2f;ArenaTranslocatorRuntime.Secondary(p,item);
        Check(ArenaTranslocatorRuntime.DiscDeployed && ArenaTranslocatorTeleport.Calls==0,"holster retains disc but cannot teleport from firearm");
        p.Owned=null;ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"dropped device cancels disc");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);p.HealthController.IsAlive=false;Time.timeScale=0;
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"death clears even while paused");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);Singleton<GameWorld>.Instance.MainPlayer=new Player();
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"replacement body cannot inherit disc");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);p.Destroyed=true;
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"destroyed Unity body clears disc");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);((RoundsMode)ArenaMatchService.Current.Mode).Round.Number++;
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"round transition clears old disc");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);((Mode)ArenaMatchService.Current.Mode).Done=true;
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"result state clears old disc");
        (p,item)=Setup();ArenaSession.IsActive=false;ArenaTranslocatorRuntime.Primary(p,item);
        Check(!ArenaTranslocatorRuntime.DiscDeployed,"ordinary raid cannot launch");
        foreach(var block in new Action<Player>[] {q=>q.IsAI=true,q=>q.IsInventoryOpened=true,q=>ArenaMode.Hud.ArenaInputBlocker.Blocked=true,q=>ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=true,q=>Singleton<GameWorld>.Instance.Alive=new Player(),q=>ArenaSession.Exact=false,q=>q.InventoryController.IsChangingWeapon=true})
        {
            (p,item)=Setup();block(p);ArenaTranslocatorRuntime.Primary(p,item);
            Check(!ArenaTranslocatorRuntime.DiscDeployed,"foreign stale paused input or changing hands rejects action");
        }
        (p,item)=Setup();Physics.Overlap=(_,_,hits)=>{hits[0]=new Collider();return 1;};ArenaTranslocatorRuntime.Primary(p,item);
        Check(!ArenaTranslocatorRuntime.DiscDeployed,"launch inside wall is refused");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);Time.time+=.2f;
        ArenaTranslocatorTeleport.Callback=ArenaTranslocatorRuntime.Reset;ArenaTranslocatorRuntime.Secondary(p,item);
        Check(ArenaTranslocatorRuntime.Teleports==0 && ArenaTranslocatorView.Pulses==0,"callback teardown cannot publish stale success");
        (p,item)=Setup();ArenaTranslocatorRuntime.Primary(p,item);ArenaTranslocatorView.ThrowTick=true;p.Owned=null;
        ArenaTranslocatorRuntime.Tick();Check(!ArenaTranslocatorRuntime.DiscDeployed,"visual failure cannot prevent physical cleanup");
        ArenaTranslocatorView.ThrowTick=false;ArenaTranslocatorRuntime.Reset();
    }
}
