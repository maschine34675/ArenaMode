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
    static void Check(bool ok,string name) { count++; if(!ok)throw new Exception("FAIL: "+name); }
    static bool Near(float a,float b)=>MathF.Abs(a-b)<.003f;
    static object Hook(string name,params object[] args)=>typeof(ArenaSlideTransitions).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static T Field<T>(ArenaSlideController slide,string name)=>(T)typeof(ArenaSlideController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slide);
    sealed class F
    {
        internal Player P=new(); internal MovementContext C=new(); internal ArenaSlideController S=new();
        internal MoveInputTranslator Input; internal JumpPlayerState J;
        internal F()
        {
            ArenaSlideTransitions.Shutdown();
            Settings.MovementEnabled.Value=Settings.SlideEnabled.Value=true;
            ArenaSession.IsActive=true;
            ArenaMode.Hud.ArenaInputBlocker.Blocked=false;
            ArenaMode.Respawn.ArenaRespawnDriver.StandDown=ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=false;
            ArenaMode.Spectator.ArenaSpectatorService.IsActive=false;
            ArenaAirVault.Pending=false;
            P.MovementContext=C; C.Player=P;
            Singleton<GameWorld>.Instance=ArenaSession.ActiveWorld=new(){MainPlayer=P};
            Singleton<AbstractGame>.Instance=ArenaSession.ActiveGame=new LocalGame{LocalPlayer=P};
            Time.time=10; Time.deltaTime=.02f; ArenaMovementPatches.Scale=2;
            Input=new(P); J=new(C);
            State(new MovePlayerState(C));
            ArenaSlideTransitions.Enable(); S.Init(P,C);
        }
        internal void State(MovementState state) { C.PreviousState=C.CurrentState; C.CurrentState=P.CurrentManagedState=state; }
        internal void Ground(bool value) { C.IsGrounded=P.CharacterController.isGrounded=value; }
        internal Vector3 Motion(float speed=6,float fraction=1,bool pulse=false,bool run=true,float dt=.02f)
        {
            object[] a=[C,dt,null]; Hook("BeforeMotion",a);
            var m=new Vector3(speed*dt,-.2f,0);
            ArenaSlideTransitions.TryApplyMotion(C,ref m,dt);
            if(pulse) { P.CharacterController.isGrounded=true; Hook("AfterSubstep",P.CharacterController); P.CharacterController.isGrounded=false; }
            if(run) { C.DirectApplyMotion(m,dt); C.TransformPosition+=new Vector3(m.x*(fraction-1),0,m.z*(fraction-1)); }
            Hook("AfterMotion",C,run,a[2]); Hook("MotionFailed",null,a[2]);
            return m;
        }
        internal void Tick(float elapsed=.02f) { Time.time+=elapsed; S.Tick(); }
        internal void Start()
        {
            P.IsSprintEnabled=true; Motion(); Tick();
            P.IsSprintEnabled=false; C.PoseLevel=C.SmoothedPoseLevel=0; Tick();
            Check(S.IsSliding,"sprint crouch starts slide");
        }
        internal SprintPlayerState NativeSprint()
        {
            var sprint=new SprintPlayerState(C); State(sprint); P.IsSprintEnabled=true; Motion(6.83f); Tick(); return sprint;
        }
        internal void NativeSprintExit(bool crouch=true)
        {
            NativeSprint(); P.IsSprintEnabled=false; C.PoseLevel=crouch?0:1;
            State(new SprintToIdleTransitionState(C)); Motion(7.07f); Tick();
        }
        internal void EarlySprintSlide()
        {
            NativeSprint(); P.IsSprintEnabled=false; C.PoseLevel=0; Tick();
            Check(S.IsSliding,"crouch starts slide before native sprint exit callback");
        }
        internal void Air()
        {
            State(J); Ground(false); Tick(); Motion();
        }
        internal void Crouch()
        {
            C.PoseLevel=0; Hook("AfterCommand",Input,ECommand.ToggleDuck,true);
        }
        internal void Landing(bool physical=true)
        {
            Motion(pulse:physical); C.IsGrounded=true; P.CharacterController.isGrounded=false;
            State(new JumpLandingState(C)); Tick();
        }
        internal void Jump()
        {
            State(J); J.Enter(false); Hook("AfterJump",J,true);
        }
    }

    static void Main()
    {
        var f=new F(); f.Start(); float speed=f.S.SlideVelocity.magnitude;
        int calls=f.C.MoveCalls; var m=f.Motion(20);
        Check(Near(m.x/.02f,speed),"slide replaces root motion without speed addition");
        Check(m.y==-.2f,"slide preserves native vertical motion");
        Check(f.C.MoveCalls==calls+1,"one native movement call per sample");
        f.Motion(fraction:.4f); float reduced=f.S.SlideVelocity.magnitude;
        Check(reduced<speed*.41f,"collision permanently reduces slide momentum");
        var after=f.Motion(20); Check(Near(after.x/.02f,reduced),"wall speed cannot return next frame");
        ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseLevel==1,"slide jump requests native standing pose");
        Check(f.C.SmoothedPoseLevel>.6f,"slide jump satisfies native Move animator pose gate before dispatch");
        Check(f.C.PushUpSkills==0,"slide immediate standing retains native force skill semantics");
        f.Jump(); Check(!f.S.IsSliding,"confirmed native jump retires slide");
        Check(Near(f.J._initialMagnitude*2,reduced),"jump seed divides motion scale exactly once");
        Check(Near(f.J._currentMagnitude,f.J._initialMagnitude),"jump current speed agrees with seeded initial speed");
        Check(f.J._initialNormalizedDirection==new Vector3(1,0,0),"jump heading follows applied slide motion");
        Check(ReferenceEquals(ArenaAirControl.ResetContext,f.C),"slide jump retires previous AirControl heading");
        Check(f.J._liftForce==new Vector3(0,8,0)&&f.P.Physical.Debits==1,"native lift and stamina remain untouched");
        var airMotion=new Vector3(.04f,-.2f,.03f); var original=airMotion;
        Check(!ArenaSlideTransitions.TryApplyMotion(f.C,ref airMotion,.02f)&&airMotion==original,"native airborne motion owns slide jump after entry");

        f=new F(); f.Start(); f.C.CanStand=false; ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseLevel==0&&f.C.SmoothedPoseLevel==0,"low ceiling cannot force standing"); f.Jump();
        Check(f.J._initialMagnitude==1,"unprepared jump receives no slide seed");
        f=new F(); f.Start(); f.P.Physical.CanJump=false; int poses=f.C.PoseCalls; ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseCalls==poses,"exhausted jump does not force stand");
        f=new F(); f.Air(); f.Crouch(); f.Motion(pulse:true); f.C.IsGrounded=true;
        f.State(new SprintPlayerState(f.C)); f.C.PoseLevel=1;
        f.C.OnPose=()=>Settings.MovementEnabled.Value=false; f.Tick();
        Check(!f.S.IsSliding,"pose callback cannot commit stale slide");
        f=new F(); f.Start(); f.C.PoseLevel=1; f.C.SmoothedPoseLevel=.1f;
        ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.SmoothedPoseLevel==1,"already logical standing slide synchronizes native smoothed pose after headroom");
        f=new F(); f.Start(); f.C.PoseLevel=1; f.C.SmoothedPoseLevel=.1f; f.C.CanStand=false;
        ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.SmoothedPoseLevel==.1f,"equal logical pose cannot bypass current headroom for jump sync");
        f.Jump(); Check(f.J._initialMagnitude==1,"headroom blocked equal pose receives no slide seed");
        f=new F(); f.Start(); f.C.MaxPoseLevel=.5f; ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseLevel==.5f&&f.C.SmoothedPoseLevel==.5f,"instant stand retains native physical maximum pose clamp");
        f.Jump(); Check(f.J._initialMagnitude==1,"clamped low pose cannot prepare slide jump");
        f=new F(); f.Start(); f.C.OnPose=()=>Settings.MovementEnabled.Value=false;
        ArenaSlideTransitions.PrepareJump(f.P);
        Check(!f.S.IsSliding,"standing callback cancellation cannot retain slide jump preparation");
        f=new F(); f.Start(); ArenaSlideTransitions.PrepareJump(f.P); Time.time+=.3f; f.Motion(); f.Jump();
        Check(f.J._initialMagnitude==1,"expired jump preparation cannot seed later entry");
        f=new F(); f.Start(); ArenaSlideTransitions.PrepareJump(f.P); f.State(f.J); f.State(f.J); Hook("AfterJump",f.J,true);
        Check(f.J._initialMagnitude==1,"airborne reentry never reuses slide jump momentum");

        f=new F(); f.Air(); f.Crouch(); f.Landing();
        Check(f.S.IsSliding,"physical landing consumes air crouch intent");
        Check(f.C.CurrentState is JumpLandingState,"landing animation state remains native");
        Check(f.C.PoseLevel==0,"landing slide stays crouched");
        f.Motion(); Check(f.S.IsSliding,"slide carries through native landing state");
        Check(ArenaSlideTransitions.NeedsBufferedJump(f.P),"landing slide is reservable by sole jump owner");
        int beforePose=f.C.PoseCalls; ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseCalls==beforePose,"landing jump reservation does not force native stand early");
        f.State(new SprintPlayerState(f.C)); f.C.PoseLevel=1; f.P.IsSprintEnabled=true; f.Tick(.04f);
        Check(ArenaSlideTransitions.HasActiveSlide(f.P),"reserved jump survives native sprint entry before owner tick");
        f.State(new MovePlayerState(f.C)); ArenaSlideTransitions.PrepareJump(f.P); f.Jump();
        Check(f.J._initialMagnitude>2,"landing slide jump preserves applied speed");
        f=new F(); f.Air(); f.Crouch(); f.Landing(); ArenaSlideTransitions.PrepareJump(f.P);
        f.C.PoseLevel=1; Hook("AfterCommand",f.Input,ECommand.RestorePose,true);
        Check(!ArenaSlideTransitions.HasActiveSlide(f.P),"explicit stand cancels reserved slide jump before component tick");
        f=new F(); f.Air(); f.Crouch(); f.Landing(); ArenaSlideTransitions.PrepareJump(f.P);
        f.C.PoseLevel=.1f; Hook("AfterCommand",f.Input,ECommand.NextWalkPose,true);
        Check(!ArenaSlideTransitions.HasActiveSlide(f.P),"partial pose release cancels reserved slide jump immediately");

        f=new F(); f.Air(); f.Crouch(); f.Landing(false);
        Check(!f.S.IsSliding,"ground proximity alone cannot start landing slide");
        f=new F(); f.Air(); f.Crouch(); f.Motion(pulse:true); f.C.IsGrounded=true;
        f.State(new SprintPlayerState(f.C)); f.C.PoseLevel=1; f.P.IsSprintEnabled=true; f.Tick();
        Check(f.S.IsSliding&&f.C.PoseLevel==0&&!f.P.IsSprintEnabled,"native sprint landing pose reset preserves queued crouch");
        f=new F(); f.Air(); f.Crouch(); f.Motion(pulse:true); f.C.IsGrounded=true;
        f.State(new SprintPlayerState(f.C)); f.C.PoseLevel=1; f.P.IsSprintEnabled=true;
        f.Motion(0); f.Tick();
        Check(f.S.IsSliding&&f.S.SlideVelocity.magnitude>5.8f,"native zero sprint entry preserves physical landing momentum");
        f.State(new SprintToIdleTransitionState(f.C)); f.Motion(0);
        Check(f.S.IsSliding,"landing sprint exit retains admitted contact slide before Tick");
        f=new F(); f.Air(); f.Crouch(); f.J._currentLiftforce=new(0,2,0); f.Landing();
        Check(!f.S.IsSliding,"upward physical pulse cannot start landing slide");
        f=new F(); f.Air(); f.Crouch(); Time.time+=.4f; f.Landing();
        Check(!f.S.IsSliding,"expired air crouch intent cannot slide");
        f=new F(); f.Air(); f.Crouch(); f.C.PoseLevel=1; Hook("AfterCommand",f.Input,ECommand.RestorePose,true); f.Landing();
        Check(!f.S.IsSliding,"crouch release cancels landing intent");
        f=new F(); f.Air(); f.Crouch(); Hook("AfterCommand",f.Input,ECommand.Jump,true); f.Landing();
        Check(!f.S.IsSliding,"jump command cancels queued landing slide");
        f=new F(); f.Air(); f.Crouch(); f.Motion(fraction:.1f,pulse:true); f.C.IsGrounded=true; f.State(new JumpLandingState(f.C)); f.Tick();
        Check(!f.S.IsSliding,"wall blocked landing cannot bank airborne speed");
        f=new F(); f.Air(); f.Crouch(); f.Motion(fraction:3,pulse:true); f.C.IsGrounded=true; f.State(new JumpLandingState(f.C)); f.Tick();
        Check(f.S.IsSliding&&f.S.SlideVelocity.magnitude<=6,"depenetration cannot create landing slide speed");
        f=new F(); f.Air(); f.Crouch(); f.Motion(pulse:true); f.C.IsGrounded=true; f.C.GroundHit.normal=new(0,.3f,0); f.State(new MovePlayerState(f.C)); f.Tick();
        Check(!f.S.IsSliding,"steep support cannot start slide");
        f=new F(); f.Air(); f.Crouch(); f.Motion(pulse:true); f.C.IsGrounded=true; f.Tick(.55f); f.State(new MovePlayerState(f.C)); f.Tick();
        Check(!f.S.IsSliding,"native landing wait remains bounded");

        f=new F(); f.Start(); speed=f.S.SlideVelocity.magnitude; f.C.PoseLevel=1; f.Tick();
        var native=new Vector3(.02f,-.2f,0); ArenaSlideTransitions.TryApplyMotion(f.C,ref native,.02f);
        Check(Near(native.x/.02f,speed),"slide end starts continuous motion fade");
        Time.time+=.1f; native=new(.02f,-.2f,0); ArenaSlideTransitions.TryApplyMotion(f.C,ref native,.02f);
        Check(native.x/.02f<speed&&native.x/.02f>1,"slide fade blends toward native motion");
        Time.time+=.11f; native=new(.02f,-.2f,0);
        Check(!ArenaSlideTransitions.TryApplyMotion(f.C,ref native,.02f)&&native.x==.02f,"slide fade returns fully to native motion");
        f=new F(); f.Start(); f.C.PoseLevel=1; f.Tick(); f.Motion(fraction:.1f);
        native=new(.02f,-.2f,0); ArenaSlideTransitions.TryApplyMotion(f.C,ref native,.02f);
        Check(native.x/.02f<1,"fade cannot restore collision lost momentum");

        Isolation();
        f=new F(); f.Start(); object[] sample=[f.C,.02f,null]; Hook("BeforeMotion",sample);
        var error=new InvalidOperationException("native failure"); var returned=Hook("MotionFailed",error,sample[2]);
        Check(ReferenceEquals(error,returned)&&!f.S.IsSliding,"native motion exception is preserved and clears slide");
        f=new F(); f.Start(); sample=[f.C,.02f,null]; Hook("BeforeMotion",sample); f.S.Init(f.P,f.C);
        f.C.DirectApplyMotion(new(.12f,0,0),.02f); Hook("AfterMotion",f.C,true,sample[2]);
        f.P.IsSprintEnabled=true; f.Tick(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0; f.Tick();
        Check(!f.S.IsSliding,"old motion cannot commit after same component rebind");
        f=new F(); f.Start(); sample=[f.C,.02f,null]; Hook("BeforeMotion",sample); f.S.Init(f.P,f.C); f.Start();
        Hook("MotionFailed",error,sample[2]); Check(f.S.IsSliding,"old motion finalizer cannot clear rebound slide");
        f=new F(); f.Start(); f.Motion(run:false); Check(!f.S.IsSliding,"suppressed native move clears transition");
        f=new F(); f.Start(); f.Motion(dt:.2f); Check(!f.S.IsSliding,"long frame discards momentum");
        f=new F(); f.Start(); Time.deltaTime=float.NaN; f.Tick(); Check(!f.S.IsSliding,"nonfinite tick discards momentum");
        f=new F(); f.Start(); f.S.AppliedMotion(new(float.NaN,0,0),false,0); Check(!f.S.IsSliding,"nonfinite sample discards momentum");
        f=new F(); f.Start(); speed=f.S.SlideVelocity.magnitude; f.Motion(fraction:3); ArenaSlideTransitions.PrepareJump(f.P); f.Jump();
        Check(f.J._initialMagnitude*2<=speed+.003f,"external displacement cannot grant transition momentum");
        f=new F(); f.P.IsSprintEnabled=true; f.Motion(24); f.Tick(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0; f.Tick();
        Check(f.S.IsSliding&&f.S.SlideVelocity.magnitude>23,"configured native speed does not hit an extra slide cap");
        f=new F(); f.Start(); f.Ground(false); f.Tick(); Check(!f.S.IsSliding,"leaving support ends ground slide");
        LandingReserve();
        SprintExit();
        ArenaSlideTransitions.Shutdown(); Check(Settings.SlideEnabled.Subscribers==0,"shutdown removes setting subscription");
        Console.WriteLine($"SLIDE_CHECKS_PASSED ({count} assertions)");
    }

    static void LandingReserve()
    {
        F Pending(float speed=6,float fraction=1)
        {
            var f=new F(); f.Air(); f.Crouch(); f.Motion(speed,fraction,pulse:true); f.C.IsGrounded=true;
            f.State(new SprintPlayerState(f.C)); f.C.PoseLevel=1; f.P.IsSprintEnabled=true; return f;
        }
        var f=Pending(8,.75f); float accepted=Field<Vector3>(f.S,"_landingVelocity").magnitude;
        Check(Near(accepted,6),"landing reserve uses collision reduced contact speed");
        f.Motion(0); f.Motion(12,fraction:2); f.Tick();
        Check(f.S.IsSliding&&f.S.SlideVelocity.magnitude<=accepted,"landing reserve never exceeds accepted contact speed");

        f=Pending(); f.Motion(0); f.Motion(1,fraction:.8f); f.Motion(2); f.Tick();
        Check(f.S.IsSliding&&f.S.SlideVelocity.magnitude<4.81f,"wall after zero request permanently reduces landing reserve");
        f=Pending(); f.Motion(0); f.Motion(1,fraction:0); f.Motion(10); f.Tick();
        Check(!f.S.IsSliding,"fully blocked landing reserve cannot recover from later free movement");

        f=Pending(); float contact=Field<float>(f.S,"_contactAt");
        Time.time+=.1f; f.Motion(10,pulse:true);
        Check(Field<float>(f.S,"_contactAt")==contact&&Field<Vector3>(f.S,"_landingVelocity").magnitude<=6.001f,
            "repeated physical contact cannot renew deadline or raise landing reserve");
        Time.time+=.41f; f.Motion(0); f.Tick();
        Check(!f.S.IsSliding&&Field<Vector3>(f.S,"_landingVelocity")==Vector3.zero,"expired contact removes landing reserve");

        f=Pending(); f.S.CancelLanding();
        Check(Field<Vector3>(f.S,"_landingVelocity")==Vector3.zero,"explicit cancellation removes landing reserve");
        f.Motion(0); f.Tick(); Check(!f.S.IsSliding,"cancelled reserve cannot start later slide");
        f=Pending(); f.S.Clear();
        Check(Field<Vector3>(f.S,"_landingVelocity")==Vector3.zero,"clear removes landing reserve");
        f=Pending(); f.P.IsInventoryOpened=true; ArenaSlideTransitions.TickOwner();
        Check(Field<Vector3>(f.S,"_landingVelocity")==Vector3.zero&&!f.S.enabled,"invalid owner removes landing reserve");
        f=Pending(); f.S.AppliedMotion(new(1,0,0),false,float.NaN);
        Check(Field<Vector3>(f.S,"_landingVelocity")==Vector3.zero,"nonfinite request removes landing reserve");
    }

    static void SprintExit()
    {
        // Native F standing route: release sprint, a standing Tick, then the
        // animator exit, and only later the user's crouch command.
        var released=new F(); released.NativeSprint(); released.P.IsSprintEnabled=false; released.Tick();
        float releasedAt=Field<float>(released.S,"_sprintEnded");
        released.State(new SprintToIdleTransitionState(released.C)); released.Motion(6.8f); released.Tick();
        released.C.PoseLevel=.1667f; released.Tick(.04f);
        Check(released.S.IsSliding,"recent witnessed sprint release admits later transition crouch");

        released=new F(); released.NativeSprint(); released.P.IsSprintEnabled=false; released.Tick();
        releasedAt=Field<float>(released.S,"_sprintEnded");
        released.State(new SprintToIdleTransitionState(released.C)); released.Motion(6.8f); released.Tick();
        Check(Field<float>(released.S,"_sprintEnded")==releasedAt,"transition binding never renews witnessed sprint end");
        Time.time=releasedAt+.31f; released.Motion(6.8f); released.C.PoseLevel=0; released.Tick();
        Check(!released.S.IsSliding,"delayed crouch cannot extend original sprint release window");

        released=new F(); released.NativeSprint(); released.P.IsSprintEnabled=false; released.Tick();
        Time.time=Field<float>(released.S,"_sprintEnded")-.01f; released.Motion(6.8f);
        released.State(new SprintToIdleTransitionState(released.C)); released.C.PoseLevel=0; released.Tick(0);
        Check(!released.S.IsSliding,"backward clock cannot admit future sprint release");
        released=new F(); released.NativeSprint(); released.P.IsSprintEnabled=false; released.Ground(false); released.Tick();
        released.Ground(true); released.State(new SprintToIdleTransitionState(released.C)); released.C.PoseLevel=0; released.Tick();
        Check(!released.S.IsSliding,"unwitnessed sprint release cannot bind transition");
        released=new F(); released.NativeSprint(); released.P.IsSprintEnabled=false; released.Tick();
        released.State(new OtherTransitionState(released.C)); released.C.PoseLevel=0; released.Tick();
        Check(!released.S.IsSliding,"witnessed sprint release cannot admit unrelated transition");

        // Native C: our Update observes crouch while CurrentState is still Sprint;
        // Animator state entry and the first Transition move occur before the next Update.
        var early=new F(); early.EarlySprintSlide();
        Check(!(bool)typeof(ArenaSlideController).GetField("_wasSprinting",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(early.S),
            "early slide has consumed sprint flag before transition motion");
        float carry=early.S.SlideVelocity.magnitude; int calls=early.C.MoveCalls;
        early.State(new SprintToIdleTransitionState(early.C)); var first=early.Motion(1);
        Check(early.S.IsSliding&&Near(first.x/.02f,carry),"first transition motion before Tick retains admitted sprint slide");
        Check(first.y==-.2f&&early.C.MoveCalls==calls+1,"early transition preserves one native move and vertical motion");
        Check(ArenaSlideTransitions.NeedsBufferedJump(early.P)&&ArenaSlideTransitions.BufferedJumpWindow(early.P)==1f,
            "first transition motion binds jump buffer before next Tick");
        float boundAt=(float)typeof(ArenaSlideController).GetField("_sprintExitAt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(early.S);
        early.Tick(); Check(early.S.IsSliding,"transition Tick retains motion bound sprint slide");
        early.Motion(); early.Tick();
        Check((float)typeof(ArenaSlideController).GetField("_sprintExitAt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(early.S)==boundAt,
            "repeated transition observers never renew binding time");

        early=new F(); early.EarlySprintSlide(); Time.time+=.31f; early.Motion();
        early.State(new SprintToIdleTransitionState(early.C)); early.Motion();
        Check(!early.S.IsSliding,"fresh motion cannot renew stale sprint exit witness");
        early=new F(); early.EarlySprintSlide(); Time.time-=.03f; early.Motion();
        early.State(new SprintToIdleTransitionState(early.C)); early.Motion();
        Check(!early.S.IsSliding,"backward clock cannot bind sprint exit witness");
        early=new F(); early.EarlySprintSlide(); early.State(new SprintToIdleTransitionState(early.C));
        early.C.PreviousState=new SprintPlayerState(early.C); early.Motion();
        Check(!early.S.IsSliding,"first transition motion rejects foreign prior sprint");
        early=new F(); early.Start(); early.State(new SprintToIdleTransitionState(early.C)); early.Motion();
        Check(!early.S.IsSliding,"active Move slide cannot witness native sprint exit");
        early=new F(); early.EarlySprintSlide(); early.State(new OtherTransitionState(early.C)); early.Motion();
        Check(!early.S.IsSliding,"early slide cannot admit arbitrary transition motion");
        early=new F(); early.EarlySprintSlide(); Time.time+=.21f;
        early.State(new SprintToIdleTransitionState(early.C)); early.Motion();
        Check(!early.S.IsSliding,"first transition motion needs fresh physical sample");
        early=new F(); early.EarlySprintSlide(); early.State(new SprintToIdleTransitionState(early.C));
        early.Ground(false); early.Motion();
        Check(!early.S.IsSliding,"first transition motion cannot retain unsupported slide");

        var f=new F(); f.NativeSprintExit();
        Check(f.S.IsSliding,"native sprint transition begins crouch slide");
        Check(f.S.SlideVelocity.magnitude<=6.83f,"native transition cannot gain beyond measured sprint speed");
        Check(ArenaSlideTransitions.HasActiveSlide(f.P),"bound native sprint transition remains an active slide");
        Check(ArenaSlideTransitions.NeedsBufferedJump(f.P)&&ArenaSlideTransitions.BufferedJumpWindow(f.P)==1f,"bound sprint exit requests one second native jump buffer");
        float speed=f.S.SlideVelocity.magnitude;
        var motion=f.Motion(.64f); Check(Near(motion.x/.02f,speed),"native transition motion receives single slide override");
        int poses=f.C.PoseCalls; ArenaSlideTransitions.PrepareJump(f.P);
        Check(f.C.PoseCalls==poses,"sprint exit jump waits for native jump capable state");
        for(int i=0;i<7;i++) { f.Motion(.1f); f.Tick(.1f); }
        f.State(new MovePlayerState(f.C)); f.C.PoseLevel=1; f.Tick(.02f);
        Check(f.S.IsSliding,"reserved sprint exit slide survives native transition longer than half second");
        ArenaSlideTransitions.PrepareJump(f.P); f.Jump();
        Check(!f.S.IsSliding&&f.J._initialMagnitude>1,"native sprint transition slide jump reaches ordinary native jump");

        f=new F(); f.NativeSprintExit(); f.State(new MovePlayerState(f.C)); f.C.PoseLevel=0; f.Tick();
        Check(f.S.IsSliding&&!ArenaSlideTransitions.NeedsBufferedJump(f.P),"crouched Move continues slide after native sprint exit");
        f=new F(); f.NativeSprintExit(false); Time.time+=.35f; f.Motion(6.8f); f.C.PoseLevel=0; f.Tick();
        Check(!f.S.IsSliding,"late transition crouch cannot reopen sprint slide window");

        f=new F(); f.NativeSprint(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0; f.State(new OtherTransitionState(f.C)); f.Tick();
        Check(!f.S.IsSliding,"arbitrary transition cannot acquire slide");
        f=new F(); f.NativeSprint(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0; f.State(new DerivedSprintExit(f.C)); f.Tick();
        Check(!f.S.IsSliding,"derived transition cannot acquire slide");
        f=new F(); var sprint=f.NativeSprint(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0;
        f.State(new SprintToIdleTransitionState(f.C)); f.C.PreviousState=new SprintPlayerState(f.C); f.Tick();
        Check(!f.S.IsSliding,"foreign prior sprint cannot acquire transition slide");
        f=new F(); f.NativeSprintExit(); f.Ground(false); f.Tick(); Check(!f.S.IsSliding,"falling sprint exit cannot retain slide");
        f=new F(); f.NativeSprintExit(); f.C.Vaulting=true; ArenaSlideTransitions.TickOwner(); Check(!f.S.IsSliding,"vault from sprint exit clears slide");
        f=new F(); f.NativeSprint(); f.P.IsSprintEnabled=false; f.C.PoseLevel=0;
        f.State(new SprintToIdleTransitionState(f.C)); f.Motion(7,fraction:.1f); f.Tick();
        Check(!f.S.IsSliding,"blocked sprint transition cannot bank prior sprint speed");
        f=new F(); f.NativeSprintExit(); var oldSprint=f.C.PreviousState;
        f.State(new SprintToIdleTransitionState(f.C)); f.C.PreviousState=oldSprint;
        motion=new(.01f,-.2f,0); Check(!ArenaSlideTransitions.TryApplyMotion(f.C,ref motion,.02f),"replacement transition cannot borrow bound slide");
        f=new F(); f.NativeSprintExit(); Time.time+=2.1f;
        Check(!ArenaSlideTransitions.HasActiveSlide(f.P),"stale native transition binding expires");
        f=new F(); f.Air(); f.Crouch(); f.Landing();
        Check(ArenaSlideTransitions.BufferedJumpWindow(f.P)==.5f,"ordinary landing buffer retains half second bound");
    }

    static void Isolation()
    {
        void Case(Action<F> change,string name) { var f=new F(); f.Start(); change(f); ArenaSlideTransitions.TickOwner(); Check(!f.S.IsSliding&&!f.S.enabled,name); }
        Case(f=>f.P.HealthController.IsAlive=false,"death retires slide and component");
        Case(f=>ArenaSession.IsActive=false,"ordinary raid clears slide");
        Case(f=>Settings.MovementEnabled.Value=false,"master off clears slide");
        Case(f=>Settings.SlideEnabled.Value=false,"slide off clears slide");
        Case(f=>Singleton<GameWorld>.Instance=ArenaSession.ActiveWorld=new(){MainPlayer=f.P},"world replacement clears slide");
        Case(f=>Singleton<AbstractGame>.Instance=ArenaSession.ActiveGame=new LocalGame{LocalPlayer=f.P},"game replacement clears slide");
        Case(f=>Singleton<GameWorld>.Instance.MainPlayer=new(),"body replacement clears slide");
        Case(f=>((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new(),"local game body mismatch clears slide");
        Case(f=>f.P.IsAI=true,"bot cannot retain slide");
        Case(f=>f.P.IsInventoryOpened=true,"inventory cancellation clears slide");
        Case(f=>ArenaMode.Hud.ArenaInputBlocker.Blocked=true,"input block clears slide");
        Case(f=>ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress=true,"respawn clears slide");
        Case(f=>ArenaMode.Spectator.ArenaSpectatorService.IsActive=true,"spectator clears slide");
        Case(f=>f.C.IsInPronePose=true,"prone clears slide");
        Case(f=>f.C.OverridenControlsState=new MovePlayerState(f.C),"native override clears slide");
        Case(f=>ArenaAirVault.Pending=true,"pending mantle retains precedence");
        Case(f=>f.C.Platform=new(),"platform motion is excluded");
        Case(f=>f.C.ImpostorCC=new(){UseAsyncMoveValidation=true},"asynchronous controller is excluded");
        Case(f=>f.P.CharacterController=new(),"character controller replacement clears slide");
        Case(f=>Hook("InputReplaced",new PlayerOwner{Player=f.P}),"input owner replacement clears slide");
        Case(f=>{ Hook("AfterCommand",f.Input,ECommand.ReloadWeapon,true); Hook("AfterCommand",new MoveInputTranslator(f.P),ECommand.ReloadWeapon,true); },"translator replacement clears slide");
        Case(f=>Redeploy.Core.Engine.RaidLifecycle.End(),"raid end clears slide");
    }
}
