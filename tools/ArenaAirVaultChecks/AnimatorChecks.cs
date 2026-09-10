using ArenaMode.Movement;
using ArenaMode.Config;
using Comfort.Common;
using EFT;
using EFT.Vaulting;
using FastAnimatorSystem;

internal static class AnimatorChecks
{
    internal static void Run(Action<bool,string> check,Func<(Player p,MovementContext c,VaultingComponent v)> setup)
    {
        FastAnimatorProcessor Processor(MovementContext c)=>(FastAnimatorProcessor)c.PlayerAnimator.Animator;
        void Step(MovementContext c)=>Processor(c).ProcessState(Processor(c).FastControllerInfo.Layer,.02f,0);
        foreach(bool vault in new[]{false,true})
        {
            var(p,c,v)=setup(); var fast=Processor(c); var layer=fast.FastControllerInfo.Layer;
            var jump=layer.CurrentState; var original=c.CurrentState;
            v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=vault;
            p.ToggleVaulting(); v.VaultingHeight+=.05f; Step(c);
            var edge=layer.Transition;
            check(c.CurrentState is VaultingMovementState,"missing graph edge enters native mantle through actual ProcessState");
            check(original.Exits==1 && original.Moves==0 && c.CurrentState.Enters==1 && c.CurrentState.Moves==1,
                "native target entry precedes old jump movement with one full lifecycle handoff");
            check(!c.JumpSet,"successful mantle consumes jump flag to prevent graph reentry");
            check(!ArenaAirVault.HasPendingRequest(p) && p.VaultNotifications==1 && p.LeftHandRemoved==1,
                "native entry releases pending double-jump precedence without repeating request effects");
            check(v.WrittenHeight==v.VaultingHeight,"animator entry uses fresh obstacle parameters");
            check(ReferenceEquals(edge.SourceState,jump) && edge.Conditions.Length==0,
                "temporary edge has exact jump source and no shared listeners");
            check(Math.Abs(layer.TransitionAbsDuration-(vault?.1f:.15f))<.0001f && edge.NormalizedDestinationStateOffset==(vault?.1f:0),
                "native template blend seconds and destination offset are preserved");
            check(jump.Edges.Count==0 && ((AbstractAnimatorControllerState)fast.FastAnimatorController.AllStates[1]).Edges.Count==2,
                "synthetic transition never mutates stored graph lists");
            for(int i=0;i<15;i++) Step(c);
            check(c.CurrentState.Enters==1 && original.Exits==1 && original.Moves==0,"blend completion never repeats native entry or old jump movement");
        }
        void Rejected(Action<Player,MovementContext,VaultingComponent,FastAnimatorProcessor> alter,string label)
        {
            var(p,c,v)=setup(); p.ToggleVaulting(); var fast=Processor(c); alter(p,c,v,fast);
            Exception error=null;
            try { fast.ProcessState(fast.FastControllerInfo.Layer,.02f,0); } catch(Exception ex) { error=ex; }
            check(error==null && fast.FastControllerInfo.Layer.Transition==null && c.CurrentState is JumpPlayerState && c.JumpSet,label);
        }
        Rejected((p,c,v,f)=>p.IsVaultingPressed=false,"released input cannot synthesize animator transition");
        Rejected((p,c,v,f)=>Settings.AirVaultEnabled.Value=false,"disabled setting cannot synthesize animator transition");
        Rejected((p,c,v,f)=>ArenaMovementScope.Active=false,"ordinary raid cannot synthesize animator transition");
        Rejected((p,c,v,f)=>p.InputBlocked=true,"blocked input cannot synthesize animator transition");
        Rejected((p,c,v,f)=>v._canClimbByStaminaGetter=()=>false,"native animator admission retains stamina gate");
        Rejected((p,c,v,f)=>v.OnParameters=()=>p.HandsController=new Meds(),"parameter callbacks cannot bypass final hands restriction");
        Rejected((p,c,v,f)=>v.OnParameters=()=>v.VaultingHeight+=1,"parameter callbacks cannot leave stale geometry");
        Rejected((p,c,v,f)=>v.OnParameters=()=>v._canClimbByStaminaGetter=()=>false,"parameter callbacks cannot bypass final stamina strategy");
        Rejected((p,c,v,f)=>v.OnParameters=()=>v._vaultingModelDebug.VaultingStatesModel.ClimbState.Allowed=false,"parameter callbacks cannot bypass final geometry strategy");
        Rejected((p,c,v,f)=>f.FastControllerInfo.Layer.CurrentState.MotionDuration=float.Epsilon,"overflowed normalized blend cannot enter animator");
        Rejected((p,c,v,f)=>f.FastAnimatorController.AllStates[2].Behavior.EncapsulatedState=new ClimbState {MovementContext=new()},
            "foreign native destination context cannot receive mantle");
        Rejected((p,c,v,f)=>((AbstractAnimatorControllerState)f.FastAnimatorController.AllStates[1]).Edges[0].ConditionCheck=()=>false,
            "native entry conditions remain authoritative");

        var foreign=setup(); foreign.p.ToggleVaulting(); var own=Processor(foreign.c); var other=AnimatorFixture.Create(foreign.c);
        other.ProcessState(other.FastControllerInfo.Layer,.02f,0);
        check(foreign.c.CurrentState is JumpPlayerState,"foreign processor cannot borrow local pending lease");
        own.ProcessState(own.FastControllerInfo.Layer,.02f,1);
        check(foreign.c.CurrentState is JumpPlayerState,"non-base layer cannot borrow pending lease");
        own.ProcessState(own.FastControllerInfo.Layer,.02f,0,1);
        check(foreign.c.CurrentState is JumpPlayerState,"recursive state pass cannot borrow pending lease");

        var native=setup(); native.p.ToggleVaulting(); var nativeFast=Processor(native.c); var nl=nativeFast.FastControllerInfo.Layer;
        var landing=new AbstractAnimatorControllerState {Name="native landing",MotionDuration=1};
        landing.Behavior.EncapsulatedState=new MovementState {MovementContext=native.c};
        var nativeEdge=new AnimatorControllerTransition("landing",3,nl.CurrentState,landing,false,false,false,0,.1f,false,0,
            Array.Empty<TransitionCondition>(),ETransitionInterruptionSource.None,true);
        nl.CurrentState.NativeResult=nativeEdge; Step(native.c);
        check(ReferenceEquals(nl.Transition,nativeEdge) && native.c.JumpSet,"existing native transition always wins unchanged");

        foreach(bool afterEntry in new[]{false,true})
        {
            var(p,c,v)=setup(); p.ToggleVaulting(); var fast=Processor(c); var layer=fast.FastControllerInfo.Layer;
            var destination=(AbstractAnimatorControllerState)fast.FastAnimatorController.AllStates[2];
            var expected=new InvalidOperationException("native callback");
            if(afterEntry) destination.AfterEnter=()=>throw expected; else destination.BeforeEnter=()=>throw expected;
            Exception caught=null; try { Step(c); } catch(Exception ex) { caught=ex; }
            check(ReferenceEquals(caught,expected),"native callback exception is preserved transparently");
            if(afterEntry)
                check(c.CurrentState is ClimbState && layer.Transition!=null && !c.JumpSet,"entered native state retains ownership after callback failure");
            else
            {
                check(c.CurrentState is JumpPlayerState && layer.Transition==null && !c.PlayerAnimator.Climb && c.JumpSet,
                    "pre-entry native failure retires only own transition and pending flag");
                p.ToggleVaulting(); check(p.VaultNotifications==1,"failed entry requires release before retry");
            }
            check(Harness.Transition(layer.CurrentState,null)==null,"animator lease cannot escape native exception");
        }
        var reset=setup(); reset.p.ToggleVaulting(); var rf=Processor(reset.c); var captured=rf.FastControllerInfo.Layer;
        var rd=(AbstractAnimatorControllerState)rf.FastAnimatorController.AllStates[2];
        rd.AfterEnter=()=>rf.FastControllerInfo=new FastControllerInfo {Layer=new(){CurrentState=captured.CurrentState}};
        rf.ProcessState(captured,.02f,0);
        check(reset.c.JumpSet && !ArenaAirVault.HasPendingRequest(reset.p),"animator reset cannot clear replacement layer jump flag");

        var denied=setup(); denied.p.ToggleVaulting(); denied.v.OnParameters=()=>denied.p.HandsController=new Meds(); Step(denied.c);
        check(!denied.c.PlayerAnimator.Climb,"failed animator admission cancels pending flag immediately");
        var unsupported=setup(); unsupported.c.PlayerAnimator.Animator=new(); unsupported.p.ToggleVaulting();
        check(unsupported.p.VaultNotifications==0 && unsupported.c.JumpSet,"unsupported animator preserves native jump without request effects");
        var invalid=setup(); invalid.p.ToggleVaulting(); invalid.v.VaultingHeight=float.NaN; ArenaAirVault.Tick();
        check(!invalid.c.PlayerAnimator.Climb,"nonfinite pending geometry cannot enter animator");

        foreach(int variant in new[]{0,1,2})
        {
            var(p,c,v)=setup(); var f=Processor(c); var idle=f.FastControllerInfo.Layer.CurrentState;
            var loop=new AbstractAnimatorControllerState {Name="Base Layer.JUMP.Jump_Loop",Hash=1889265959,
                Behavior=null,MotionDuration=.433333f,ParentStateMachine=idle.ParentStateMachine};
            idle.Edges.Add(new AnimatorControllerTransition("native idle to loop",9,idle,loop,false,false,true,.9f,.1f,true,0,
                Array.Empty<TransitionCondition>(),ETransitionInterruptionSource.None,true));
            f.FastAnimatorController.AllStates.Add(loop); f.FastControllerInfo.Layer.CurrentState=loop;
            if(variant==1) idle.Behavior.EncapsulatedState=new JumpPlayerState {MovementContext=c};
            if(variant==2) loop.Name="arbitrary behaviorless node";
            p.ToggleVaulting(); Step(c);
            check(variant==0 ? c.CurrentState is ClimbState : c.CurrentState is JumpPlayerState,
                variant==0 ? "real behaviorless jump loop can mantle through verified incoming owner"
                    : "behaviorless leaf cannot borrow foreign or unknown jump ownership");
        }
        var forced=setup(); forced.p.ToggleVaulting(); var ff=Processor(forced.c); var fl=ff.FastControllerInfo.Layer;
        var forcedTarget=(AbstractAnimatorControllerState)ff.FastAnimatorController.AllStates[1];
        forcedTarget.Behavior.EncapsulatedState=new MovementState {MovementContext=forced.c};
        fl.ForcePlayState=forcedTarget; Step(forced.c);
        check(ReferenceEquals(forced.c.CurrentState,forcedTarget.Behavior.EncapsulatedState) && forced.c.JumpSet,
            "foreign force-play remains native without synthetic jump consumption");
        var changedOwner=setup(); changedOwner.p.ToggleVaulting(); var cf=Processor(changedOwner.c);
        ((AbstractAnimatorControllerState)cf.FastAnimatorController.AllStates[2]).AfterEnter=()=>Singleton<GameWorld>.Instance.MainPlayer=new();
        Step(changedOwner.c);
        check(changedOwner.c.JumpSet,"native entry owner drift cannot consume stale body jump flag");

        var interrupted=setup(); interrupted.v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=true;
        interrupted.p.ToggleVaulting(); Step(interrupted.c);
        var ip=Processor(interrupted.c); var il=ip.FastControllerInfo.Layer; var iv=il.DestinationState;
        var fall=new AbstractAnimatorControllerState {Name="Vault_fall",MotionDuration=1};
        fall.Behavior.EncapsulatedState=new MovementState {MovementContext=interrupted.c};
        var fallEdge=new AnimatorControllerTransition("native vault fall",12,iv,fall,false,false,false,0,.1f,false,0,
            Array.Empty<TransitionCondition>(),ETransitionInterruptionSource.None,true);
        iv.InterruptedResult=fallEdge;
        ip.ProcessState(il,.175f,0);
        check(ReferenceEquals(il.Transition,fallEdge) && ReferenceEquals(interrupted.c.CurrentState,fall.Behavior.EncapsulatedState),
            "native destination interruption remains available during vault blend");
    }
}
