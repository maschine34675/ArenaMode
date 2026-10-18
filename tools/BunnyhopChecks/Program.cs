using System.Reflection;
using ArenaMode.Movement;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using UnityEngine;

static class Program
{
    static int count;
    static void Check(bool value,string message) { count++; if(!value) throw new Exception("ASSERT: "+message); }
    static bool Near(float a,float b) => MathF.Abs(a-b)<.002f;
    const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    static object Call(string name,params object[] args) => typeof(ArenaBunnyhop).GetMethod(name,Flags).Invoke(null,args);
    sealed class F
    {
        internal MovementContext C=new(); internal Player P; internal JumpPlayerState J;
        internal F() {
            ArenaBunnyhop.Shutdown(); ArenaDoubleJump.Shutdown();
            Settings.MovementEnabled.Value=Settings.BunnyhopEnabled.Value=Settings.DoubleJumpEnabled.Value=true;
            ArenaAirControl.Ready=true; ArenaAirVault.Pending=false;
            ArenaMode.Respawn.ArenaRespawnDriver.StandDown=ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=false;
            ArenaMode.Spectator.ArenaSpectatorService.IsActive=ArenaMode.Hud.ArenaInputBlocker.Blocked=false;
            ArenaSession.IsActive=true;
            P=new(){MovementContext=C}; J=new(C);
            Singleton<GameWorld>.Instance=ArenaSession.ActiveWorld=new(){MainPlayer=P};
            Singleton<AbstractGame>.Instance=ArenaSession.ActiveGame=new LocalGame{LocalPlayer=P};
            Singleton<GlobalConfiguration>.Instance=new(); EFTHardSettings.Instance=new();
            ArenaMode.Pickups.ArenaPowerups.State=new(); ArenaMode.Pickups.ArenaPowerups.Owner=P;
            Time.time=Time.realtimeSinceStartup=10;
            State(new MovePlayerState(C)); Ground(true);
            ArenaDoubleJump.Enable(); ArenaBunnyhop.Enable();
            Enter(); Air(); Motion(6); Time.time+=.2f; Tick();
        }
        internal void State(MovementState s) { C.PreviousState=C.CurrentState; C.CurrentState=P.CurrentManagedState=s; }
        internal void Ground(bool g) { C.IsGrounded=P.CharacterController.isGrounded=g; }
        internal void Air() { Ground(false); J._jumpState=JumpPlayerState.EJumpState.Jump; J._currentLiftforce=new(0,-2,0); Tick(); }
        internal void Tick() { Call("Tick",C); Dj("ArenaDoubleJumpLandingPatch","Postfix",C); }
        internal object Dj(string patch,string method,params object[] args)
            => typeof(ArenaDoubleJump).GetNestedType(patch,BindingFlags.NonPublic).GetMethod(method,Flags).Invoke(null,args);
        internal void Motion(float speed,float dt=.02f) {
            object[] args=[C,dt,null]; Call("BeforeMotion",args);
            C.DirectApplyMotion(new(speed*dt,0,0),dt);
            Call("AfterMotion",C,true,args[2]);
        }
        internal void Enter() {
            State(J); object[] args=[J,null]; Call("BeforeEntry",args);
            object[] dj=[J,null]; Dj("ArenaDoubleJumpJumpEnterPatch","Prefix",dj);
            P.Physical.OnJump(true); J._jumpState=JumpPlayerState.EJumpState.PushingFromTheGround;
            J._liftForce=new(0,8,0); ArenaJumpBoots.ApplyEntry(J);
            Dj("ArenaDoubleJumpJumpEnterPatch","Postfix",J,dj[1]);
            Call("AfterEntry",J,args[1]);
        }
        internal bool Press() => ArenaBunnyhop.TryBufferJump(P);
        internal void Land(float delay=.08f) { Time.time+=delay; Ground(true); Tick(); }
        internal void Dispatch() { State(new MovePlayerState(C)); C.PlayerAnimator.Jump=false; Tick(); }
        internal void Hop() { Press(); Land(); Dispatch(); Enter(); Air(); }
        internal Vector3 Carry(float dt=.02f) { var m=new Vector3(.01f,-.3f,.01f); ArenaBunnyhop.ApplyCarry(J,ref m,dt); return m; }
    }
    static void Main()
    {
        var f=new F(); Check(f.Press(),"near landing reserves input"); Check(f.C.NativeRequests==0,"buffer does not jump in air");
        f.Land(); f.State(new JumpLandingState(f.C)); f.Tick(); Check(f.C.NativeRequests==0,"landing animation does not dispatch");
        f.Dispatch(); Check(f.C.NativeRequests==1,"ground transition dispatches once"); f.Tick(); Check(f.C.NativeRequests==1,"duplicate ticks cannot dispatch twice");
        f.Enter(); f.Air(); var v=f.Carry(); Check(Near(v.x/.02f*2,6*1.06f),"carry compensates scale exactly once"); Check(v.y==-.3f,"carry preserves vertical motion");
        Check(f.P.Physical.Debits==2,"two real entries pay exactly twice");
        for(int i=0;i<15;i++) { f.Motion(v.x/.02f*2); Time.time+=.2f; f.Hop(); v=f.Carry(); }
        Check(Near(v.x/.02f*2,7.5f),"chain cap cannot compound beyond base");
        f=new F(); f.Motion(11.8f); f.Hop(); Check(f.Carry().x/.02f*2<=12.001f,"absolute cap applies");
        f=new F(); f.C.GroundHit.normal=new(0,.3f,0); Check(!f.Press(),"steep surface cannot reserve");
        f=new F(); f.C.GroundHit.point=new(0,-.7f,0); Check(!f.Press(),"high airborne input remains doublejump");
        f=new F(); f.J._currentLiftforce.y=1; Check(!f.Press(),"ascending input remains doublejump");
        f=new F(); f.C.GroundHit.hasHit=false; Check(!f.Press(),"missing ground ray cannot reserve");
        f=new F(); f.Press(); f.C.IsGrounded=true; f.P.CharacterController.isGrounded=false; f.State(new MovePlayerState(f.C)); f.Tick(); Check(f.C.NativeRequests==0,"proximity alone never hops");
        f=new F(); f.Press(); f.Land(); f.C.IsGrounded=false; f.Tick(); f.Ground(true); f.Dispatch();
        Check(f.C.NativeRequests==0,"lost support before dispatch discards chain");
        f=new F(); f.Press(); f.Land(); f.P.CharacterController.isGrounded=false; f.Dispatch();
        Check(f.C.NativeRequests==1,"latched landing survives transient contact bit on supported floor");
        f=new F(); f.Press(); f.Land(); f.C.GroundHit.normal=new(0,.3f,0); f.Dispatch();
        Check(f.C.NativeRequests==0,"unwalkable support discards pending hop");
        f=new F(); f.Press(); object[] contactSample=[f.C,.02f,null]; Call("BeforeMotion",contactSample);
        f.P.CharacterController.isGrounded=true; Call("AfterSubstep",f.P.CharacterController);
        f.P.CharacterController.isGrounded=false; f.C.DirectApplyMotion(new(.12f,0,0),.02f);
        Call("AfterMotion",f.C,true,contactSample[2]); f.C.IsGrounded=true; f.Tick(); f.Dispatch();
        Check(f.C.NativeRequests==1,"physical substep contact survives final substep clearing");
        f=new F(); f.Press(); contactSample=[f.C,.02f,null]; Call("BeforeMotion",contactSample);
        Call("AfterSubstep",new SimpleCharacterController{isGrounded=true});
        f.C.DirectApplyMotion(new(.12f,0,0),.02f); Call("AfterMotion",f.C,true,contactSample[2]);
        f.C.IsGrounded=true; f.Tick(); f.Dispatch(); Check(f.C.NativeRequests==0,"another controller cannot grant contact");
        f=new F(); f.Press(); Time.time+=.14f; contactSample=[f.C,.02f,null]; Call("BeforeMotion",contactSample);
        f.P.CharacterController.isGrounded=true; Call("AfterSubstep",f.P.CharacterController);
        f.P.CharacterController.isGrounded=false; f.C.DirectApplyMotion(new(.12f,0,0),.02f);
        Call("AfterMotion",f.C,true,contactSample[2]); Time.time+=.03f; f.C.IsGrounded=true;
        f.Tick(); f.Dispatch(); Check(f.C.NativeRequests==1,"input window uses impact time not delayed observer");
        f=new F(); f.Press(); f.Ground(true); f.C.GroundHit.hasHit=false; f.State(new MovePlayerState(f.C)); f.Tick();
        Check(f.C.NativeRequests==0,"first contact also requires supported dispatch");
        f=new F(); f.Press(); f.Land(.2f); f.Dispatch(); Check(f.C.NativeRequests==0,"expired prelanding input cannot dispatch");
        f=new F(); f.Press(); Time.time+=.1f; f.Press(); f.Land(.1f); f.Dispatch(); Check(f.C.NativeRequests==0,"repeated input cannot extend buffer");
        f=new F(); f.Motion(6); f.Land(0); Time.time+=.1f; f.State(new JumpLandingState(f.C)); Check(f.Press(),"fresh ground press may chain"); f.Dispatch(); Check(f.C.NativeRequests==1,"fresh ground press dispatches");
        f=new F(); f.Motion(6); f.Land(0); Time.time+=.13f; f.State(new JumpLandingState(f.C)); Check(!f.Press(),"late ground press stays vanilla");
        f=new F(); f.Press(); f.Land(); Time.time+=.6f; f.Dispatch(); Check(f.C.NativeRequests==0,"slow landing expires buffer");
        f=new F(); f.Press(); f.Land(); f.Dispatch(); Time.time+=.26f; f.Tick(); Check(!f.C.PlayerAnimator.Jump,"failed native entry releases owned bool");
        f=new F(); f.Press(); f.Land(); f.Dispatch(); f.Ground(false); f.Tick(); Check(!f.C.PlayerAnimator.Jump,"walking off after dispatch releases owned bool");
        f=new F(); f.Press(); f.Land(); f.Dispatch(); Settings.BunnyhopEnabled.Value=false; Check(!f.C.PlayerAnimator.Jump,"disable releases request");
        f=new F(); f.Press(); f.Land(); f.C.OnNoise=()=>Settings.BunnyhopEnabled.Value=false; f.Dispatch();
        Check(!f.C.PlayerAnimator.Jump,"noise callback reset cannot leak native signal");
        f=new F(); f.Hop(); ((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new(); ArenaBunnyhop.TickOwner();
        Check(typeof(ArenaBunnyhop).GetField("_flight",Flags).GetValue(null)==null,"body replacement retires old flight without old-body ticks");
        f=new F(); f.Press(); f.Land(); f.P._vaultingTiming=1; f.Dispatch(); Check(f.C.NativeRequests==0,"native vault timing remains authoritative");
        f=new F(); f.Press(); f.C.TransformPosition.y-=1; ArenaBunnyhop.HardLanding(f.C); f.Land(); f.Dispatch(); Check(f.C.NativeRequests==0,"hard landing cancels chain");
        f=new F(); f.Press(); ArenaBunnyhop.HardLanding(f.C); f.Land(); f.Dispatch(); Check(f.C.NativeRequests==1,"ordinary native fall slowdown does not cancel same-floor hop");
        f=new F(); f.Hop(); f.Motion(0); f.C.InputMotion=new(6,0,0);
        object[] sample=[f.C,.02f,null]; Call("BeforeMotion",sample); Call("AfterMotion",f.C,true,sample[2]);
        Check(Near(f.Carry().x,.01f),"wall contact discards banked speed");
        f=new F(); f.Hop(); sample=[f.C,.02f,null]; Call("BeforeMotion",sample); var error=new Exception("native");
        Check(ReferenceEquals(Call("MotionFailed",error,sample[2]),error),"native motion exception is preserved");
        Check(Near(f.Carry().x,.01f),"failed native move cannot retain carry");
        f=new F(); f.Hop(); f.Motion(3); Check(Near(f.Carry().x/.02f*2,3),"collision loss is not restored next frame");
        f=new F(); f.Hop(); var before=f.Carry(.2f); Check(Near(before.x,.01f),"invalid dt cannot inject motion");
        foreach(var change in new Action<F>[] {
            x=>Settings.BunnyhopEnabled.Value=false, x=>Settings.MovementEnabled.Value=false,
            x=>ArenaSession.IsActive=false, x=>x.P.IsAI=true,x=>x.P.IsInventoryOpened=true,
            x=>x.P.HealthController.IsAlive=false,x=>ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=true,
            x=>ArenaMode.Spectator.ArenaSpectatorService.IsActive=true,x=>x.C.OverridenState=x.J,
            x=>x.C.OverridenControlsState=x.J,x=>x.C.CanJump=false,x=>x.C.CanWalk=false,
            x=>x.C.IsInPronePose=true,x=>x.C.IsInMountedState=true,x=>x.C.Vaulting=true,
            x=>ArenaAirVault.Pending=true,x=>x.P.Slide=new(){IsSliding=true},x=>x.C.Platform=new(),
            x=>x.C.ImpostorCC=new(){UseAsyncMoveValidation=true},x=>ArenaSession.ActiveWorld=new(),
            x=>((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new(),x=>ArenaAirControl.Ready=false
        }) { f=new F(); f.Hop(); change(f); Check(Near(f.Carry().x,.01f),"invalid scope cannot inject carry"); }
        f=new F();
        var translator=new MoveInputTranslator(f.P);
        var patch=typeof(ArenaDoubleJump).GetNestedType("ArenaDoubleJumpJumpCommandPatch",BindingFlags.NonPublic);
        object[] input=[translator,ECommand.Jump,null]; patch.GetMethod("Prefix",Flags).Invoke(null,input);
        patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==1,"near landing consumed before doublejump");
        f=new F(); f.C.GroundHit.hasHit=false; translator=new(f.P); input=[translator,ECommand.Jump,null];
        patch.GetMethod("Prefix",Flags).Invoke(null,input); patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==2,"ordinary airborne command still doublejumps");
        f=new F(); ArenaMode.Pickups.ArenaPowerups.State.GrantJump(Time.time,30); f.Hop();
        Check(Near(f.J._liftForce.y,8*MathF.Sqrt(2)),"boots applied once on chained entry");
        f.C.GroundHit.hasHit=false; translator=new(f.P); input=[translator,ECommand.Jump,null];
        patch.GetMethod("Prefix",Flags).Invoke(null,input); patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==3 && Near(f.J._liftForce.y,8*MathF.Sqrt(2*.8f)),"chained hop doublejump pays once and does not compound boots");
        input=[translator,ECommand.Jump,null]; patch.GetMethod("Prefix",Flags).Invoke(null,input); patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==3,"no second doublejump within hop");
        // Spend that flight's DJ, then land on a physical substep that the last
        // substep clears. The next native hop must have exactly one fresh DJ.
        f.C.GroundHit.hasHit=true; f.J._currentLiftforce.y=-2; Time.time+=.2f; f.Motion(6); f.Press();
        contactSample=[f.C,.02f,null]; Call("BeforeMotion",contactSample);
        f.P.CharacterController.isGrounded=true; Call("AfterSubstep",f.P.CharacterController);
        f.P.CharacterController.isGrounded=false; f.C.DirectApplyMotion(new(.12f,0,0),.02f);
        f.C.IsGrounded=true; Check(!ArenaBunnyhop.HasRecentPhysicalContact(f.C),"uncommitted contact cannot renew credit");
        Call("AfterMotion",f.C,true,contactSample[2]);
        Check(ArenaBunnyhop.HasRecentPhysicalContact(f.C),"committed physical pulse is shared");
        f.Tick(); f.Dispatch(); f.Enter(); f.Air(); f.C.GroundHit.hasHit=false;
        input=[translator,ECommand.Jump,null]; patch.GetMethod("Prefix",Flags).Invoke(null,input); patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==5,"pulse-only landing renews doublejump for next hop");
        input=[translator,ECommand.Jump,null]; patch.GetMethod("Prefix",Flags).Invoke(null,input); patch.GetMethod("Postfix",Flags).Invoke(null,[translator,input[2]]);
        Check(f.P.Physical.Debits==5,"shared pulse cannot grant repeated doublejump");
        f.C.GroundHit.hasHit=true; f.J._currentLiftforce.y=-2; Time.time+=.2f; f.Motion(6); f.Hop();
        Check(f.P.Physical.Debits==6 && Near(f.J._liftForce.y,8*MathF.Sqrt(2)),"next ground hop restores fresh native boots lift and one debit");
        f=new F(); f.Hop(); Redeploy.Core.Engine.RaidLifecycle.End(); Check(Near(f.Carry().x,.01f),"raid end clears carry");
        f=new F(); f.State(new MovePlayerState(f.C)); f.C.IsGrounded=true; f.P.CharacterController.isGrounded=false;
        f.Enter(); Check(typeof(ArenaBunnyhop).GetField("_flight",Flags).GetValue(null)!=null,"native ground takeoff seeds without transient depenetration bit");
        f.Air(); f.Motion(6); Time.time+=.2f; f.Press(); f.C.IsGrounded=true; f.P.CharacterController.isGrounded=false; f.State(new MovePlayerState(f.C)); f.Tick();
        Check(f.C.NativeRequests==0,"seed permission cannot substitute for physical re-landing");
        ArenaBunnyhop.Shutdown(); ArenaDoubleJump.Shutdown();
        Console.WriteLine($"ARENA_BUNNYHOP_CHECKS_PASSED ({count} assertions)");
    }
}
