using ArenaMode.Config;
using ArenaMode.Movement;
using Comfort.Common;
using EFT;
using EFT.Vaulting;
using UnityEngine;

internal static class UnityChecks
{
    internal static void Run(Action<bool,string> check,Func<(Player p,MovementContext c,VaultingComponent v)> setup)
    {
        (Player p,MovementContext c,VaultingComponent v,Animator a) Ready()
        { var(p,c,v)=setup(); return(p,c,v,UnityFixture.Create(c)); }
        foreach(bool manual in new[]{false,true})
        foreach(bool vault in new[]{false,true})
        foreach(int source in new[]{232800732,1056621489,1332272753,1889265959})
        {
            var(p,c,v,a)=Ready(); var old=c.CurrentState;
            p.BodyUpdateMode=manual?EUpdateMode.Manual:EUpdateMode.Auto;
            a.Current=source;
            a.Falling=true; // Real retest: stale MovePlayerState flag during a genuine Jump.
            v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=vault;
            p.ToggleVaulting();
            check(ArenaAirVault.HasPendingRequest(p),"Unity native moving and sprinting jump sources are admitted");
            check(ArenaAirVault.HasPendingRequest(p) && a.CrossFades==0,"Unity native held request queues before animation dispatch");
            v.VaultingHeight+=.05f;
            p.BodyUpdate(.02f);
            check(a.CrossFades==1,"Unity Auto and Manual body updates both issue one transition");
            check(a.Falling,"Unity mantle leaves the stale native falling flag untouched");
            check(a.Blend==(vault?.1f:.15f) && a.Offset==(vault?.1f:0),"Unity transition retains native entry blend and offset");
            if(!manual)
            {
                check(a.Evaluations==0 && old.Exits==0 && c.CurrentState==old,"Unity dispatch never manually updates animation or enters movement");
                // The native move is committed. Later input/settings/geometry no longer revoke it.
                p.IsVaultingPressed=false; Settings.AirVaultEnabled.Value=false; ArenaMovementScope.Active=false;
                v._vaultingModelDebug.VaultingStatesModel.ClimbState.Allowed=false;
                v.VaultingHeight=99;
                ArenaAirVault.Tick();
                a.Update(.02f);
            }
            check(c.CurrentState.GetType()==(vault?typeof(VaultState):typeof(ClimbState)) && old.Exits==1
                && c.CurrentState.Enters==1,"Unity extracted container callback performs native full state entry once");
            check(!c.JumpSet && !ArenaAirVault.HasPendingRequest(p),"Unity committed entry consumes jump even after settings disable or release");
            check(a.Evaluations==1 && c.CurrentState.Moves==1 && old.Moves==0,"Unity native motion runs once on destination without extra zero update");
            check(p.VaultNotifications==1 && p.LeftHandRemoved==1,"Unity dispatch preserves one-shot native hands notifications");
            p.BodyUpdate(.02f);
            check(a.CrossFades==1,"Unity accepted entry is never issued twice");
        }
        foreach(int source in new[]{1056621489,1332272753})
        foreach(bool missing in new[]{false,true})
        {
            var(p,c,v,a)=Ready(); a.Current=source;
            var own=a.Containers.Single(x=>x.StateFullNameHash==source);
            if(missing) a.Containers=a.Containers.Where(x=>!ReferenceEquals(x,own)).ToArray();
            else own.EncapsulatedState=new JumpPlayerState {MovementContext=c};
            p.ToggleVaulting(); p.BodyUpdate(.02f);
            check(a.CrossFades==0 && !ArenaAirVault.HasPendingRequest(p),"Unity moving source requires its own exact container despite valid Idle owner");
        }
        // An already-queued idle request may switch to the native moving jump
        // before the next BodyUpdate, while retaining its exact managed owner.
        foreach(int next in new[]{1056621489,1332272753})
        {
            var(p,c,v,a)=Ready(); p.ToggleVaulting(); a.Current=next; p.BodyUpdate(.02f); a.Update(.02f);
            check(c.CurrentState is ClimbState && !c.JumpSet,"Unity source changes within native Jump family retain valid pending request");
        }
        void Reject(Action<Player,MovementContext,VaultingComponent,Animator> alter,string label)
        {
            var(p,c,v,a)=Ready(); p.ToggleVaulting(); alter(p,c,v,a); p.BodyUpdate(.02f);
            check(a.CrossFades==0 && c.CurrentState is JumpPlayerState,label);
        }
        Reject((p,c,v,a)=>p.IsVaultingPressed=false,"Unity release before dispatch cancels mantle");
        Reject((p,c,v,a)=>ArenaMovementScope.Active=false,"Unity ordinary raid cannot dispatch pending mantle");
        Reject((p,c,v,a)=>p.InputBlocked=true,"Unity blocked input before dispatch cancels mantle");
        Reject((p,c,v,a)=>Settings.AirVaultEnabled.Value=false,"Unity disabled feature before dispatch cancels mantle");
        Reject((p,c,v,a)=>a.Landing=true,"Unity native landing always wins");
        var falling=Ready(); falling.c.CurrentState=new FallDown {MovementContext=falling.c};
        falling.a.Containers[0].EncapsulatedState=falling.c.CurrentState;
        falling.p.ToggleVaulting(); falling.p.BodyUpdate(.02f);
        check(falling.a.CrossFades==0 && !ArenaAirVault.HasPendingRequest(falling.p),"Unity actual FallDown subtype cannot borrow a Jump leaf");
        Reject((p,c,v,a)=>c.JumpSet=false,"Unity missing native jump flag cannot dispatch");
        Reject((p,c,v,a)=>c.Transition=true,"Unity existing native transition always wins");
        Reject((p,c,v,a)=>a.Current=12345,"Unity unrelated animator state cannot borrow Jump owner");
        Reject((p,c,v,a)=>a.isInitialized=false,"Unity uninitialized animator cannot dispatch");
        Reject((p,c,v,a)=>a.isActiveAndEnabled=false,"Unity disabled animator cannot dispatch");
        Reject((p,c,v,a)=>a.speed=float.NaN,"Unity invalid animator speed cannot dispatch");
        Reject((p,c,v,a)=>a.runtimeAnimatorController=new(),"Unity controller replacement revokes pending ownership");
        Reject((p,c,v,a)=>((AnimationSystem.UnityAnimatorWrapper)c.PlayerAnimator.Animator).Animator=UnityFixture.Create(c),"Unity native handle replacement revokes pending ownership");
        Reject((p,c,v,a)=>a.Containers[0].EncapsulatedState=new JumpPlayerState {MovementContext=c},"Unity source container must own exact Jump instance");
        Reject((p,c,v,a)=>a.Containers[1].EncapsulatedState=new ClimbState {MovementContext=new()},"Unity foreign destination context cannot receive mantle");
        Reject((p,c,v,a)=>a.States.Remove(-285230529),"Unity missing destination state cannot receive mantle");
        Reject((p,c,v,a)=>a.Containers[1].DisableRootMotion=true,"Unity root motion disabled destination cannot receive mantle");
        Reject((p,c,v,a)=>a.Containers=a.Containers.Concat(new[]{a.Containers[1]}).ToArray(),"Unity duplicate destination container cannot receive mantle");
        Reject((p,c,v,a)=>v.OnParameters=()=>p.HandsController=new Meds(),"Unity parameter callback cannot bypass hands restriction");
        Reject((p,c,v,a)=>v._canClimbByStaminaGetter=()=>false,"Unity fresh dispatch requires native stamina");
        Reject((p,c,v,a)=>v.OnTick=()=>v.VaultingHeight=float.NaN,"Unity fresh dispatch rejects nonfinite geometry");
        Reject((p,c,v,a)=>Time.unscaledTime=2,"Unity queued request expiry cannot dispatch");
        var fresh=Ready(); fresh.p.ToggleVaulting(); fresh.v.VaultingHeight+=.05f; fresh.p.BodyUpdate(.02f);
        check(fresh.v.WrittenHeight==fresh.v.VaultingHeight,"Unity last geometry is refreshed before commit");

        foreach(int variant in new[]{0,1,2,3,4})
        {
            var(p,c,v,a)=Ready(); p.ToggleVaulting(); p.BodyUpdate(.02f);
            if(variant==0)Singleton<GameWorld>.Instance=new();
            if(variant==1)Singleton<AbstractGame>.Instance=new LocalGame();
            if(variant==2)a.runtimeAnimatorController=new();
            if(variant==3)Singleton<GameWorld>.Instance.MainPlayer=new();
            if(variant==4)((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new();
            a.Update(.02f);
            check(c.JumpSet,"Unity committed old epoch never clears replacement body jump");
        }
        var rebound=Ready(); rebound.p.ToggleVaulting(); rebound.p.BodyUpdate(.02f);
        rebound.a.Containers[1].EncapsulatedState=new ClimbState {MovementContext=rebound.c}; rebound.a.Update(.02f);
        check(rebound.c.JumpSet,"Unity reinitialized container cannot impersonate captured destination movement");

        var errorCase=Ready(); errorCase.a.OnCrossFade=()=>throw new InvalidOperationException("accepted then failed");
        errorCase.p.ToggleVaulting(); errorCase.p.BodyUpdate(.02f);
        check(ArenaAirVault.HasPendingRequest(errorCase.p),"Unity queue acceptance followed by exception retains observer");
        errorCase.a.Update(.02f);
        check(!errorCase.c.JumpSet,"Unity accepted throwing dispatch still consumes jump at native entry");

        var nativeError=Ready(); nativeError.p.ToggleVaulting(); nativeError.p.BodyUpdate(.02f);
        var expected=new InvalidOperationException("native initialization failure");
        nativeError.a.Containers[1].EncapsulatedState.OnEnter=()=>throw expected;
        Exception actual=null; try {nativeError.a.Update(.02f);}catch(Exception ex){actual=ex;}
        check(ReferenceEquals(actual,expected) && !nativeError.c.JumpSet,"Unity preserves native entry exception and releases only completed ownership");

        var delayed=Ready(); delayed.p.ToggleVaulting(); delayed.p.BodyUpdate(.02f);
        delayed.p.IsVaultingPressed=false; ArenaAirVault.Tick(); delayed.p.IsVaultingPressed=true;
        delayed.p.BodyUpdate(.02f);
        check(delayed.a.CrossFades==1 && ArenaAirVault.HasPendingRequest(delayed.p),"Unity committed observer survives release without re-dispatch");
        var impostor=Ready(); impostor.p.ToggleVaulting(); impostor.p.BodyUpdate(.02f);
        impostor.c.CurrentState=impostor.a.Containers[1].EncapsulatedState; ArenaAirVault.Tick();
        check(impostor.c.JumpSet && ArenaAirVault.HasPendingRequest(impostor.p),"Unity polling cannot prove entry from managed state alone");
        foreach(bool controls in new[]{false,true})
        {
            var over=Ready(); over.p.ToggleVaulting(); over.p.BodyUpdate(.02f);
            if(controls)over.c.OverridenControlsState=new MovementState(); else over.c.OverridenState=new MovementState();
            // Bypass native override handling deliberately to test only observer rejection.
            over.c.CurrentState=over.a.Containers[1].EncapsulatedState; over.a.Current=-285230529;
            ArenaAirVault.Tick(); check(over.c.JumpSet,"Unity polling cannot accept overridden movement ownership");
        }
        var diagnostic=Ready(); diagnostic.a.Current=999; ArenaMode.Plugin.ModLogger.Info.Clear();
        for(int frame=0;frame<20;frame++) {diagnostic.a.Current++; diagnostic.p.ToggleVaulting();}
        var rejected=ArenaMode.Plugin.ModLogger.Info.Where(x=>x.Contains("Unity rejected held: unsupported-jump-leaf")).ToArray();
        check(rejected.Length==1 && rejected[0].Contains("state=1000") && rejected[0].Contains("update=Auto"),"Unity rejection logs explain source and throttle volatile details by stable reason");
        var loggedCancel=Ready(); loggedCancel.p.ToggleVaulting(); loggedCancel.a.Landing=true;
        ArenaMode.Plugin.ModLogger.Info.Clear(); loggedCancel.p.BodyUpdate(.02f);
        check(ArenaMode.Plugin.ModLogger.Info.Any(x=>x.Contains("Unity rejected dispatch: native-jump-flags-or-transition") && x.Contains("landing=True")),"Unity queued cancellation logs its native dispatch reason");
        var alternating=Ready(); ArenaMode.Plugin.ModLogger.Info.Clear();
        for(int frame=0;frame<20;frame++)
        {
            alternating.a.Landing=false; alternating.p.ToggleVaulting();
            alternating.a.Landing=true; alternating.p.BodyUpdate(.02f);
        }
        check(ArenaMode.Plugin.ModLogger.Info.Count==2,"alternating queued and rejected reasons are independently throttled");
        Time.unscaledTime+=2.1f; alternating.a.Landing=false; alternating.p.ToggleVaulting();
        alternating.a.Landing=true; alternating.p.BodyUpdate(.02f);
        check(ArenaMode.Plugin.ModLogger.Info.Count==4,"mantle diagnostic reason budgets reopen after interval");
        var newWorld=Ready(); newWorld.p.ToggleVaulting(); newWorld.a.Landing=true; newWorld.p.BodyUpdate(.02f);
        check(ArenaMode.Plugin.ModLogger.Info.Count==6,"new world resets mantle diagnostic reason budgets");
        var brokenLogger=Ready(); ArenaMode.Plugin.ModLogger.ThrowInfo=true;
        try {brokenLogger.p.ToggleVaulting(); brokenLogger.p.BodyUpdate(.02f); brokenLogger.a.Update(.02f);}
        finally {ArenaMode.Plugin.ModLogger.ThrowInfo=false;}
        check(brokenLogger.c.CurrentState is ClimbState && !brokenLogger.c.JumpSet,"Unity diagnostics cannot break successful native mantle");
        var deadline=Ready(); deadline.p.ToggleVaulting(); deadline.p.BodyUpdate(.02f); Time.unscaledTime=3; ArenaAirVault.Tick();
        check(deadline.c.PlayerAnimator.Climb && deadline.a.CrossFades==1 && ArenaAirVault.HasPendingRequest(deadline.p),"Unity missing entry retains observer without rewriting native committed move");
        deadline.a.Update(.02f);
        check(!deadline.c.JumpSet && !ArenaAirVault.HasPendingRequest(deadline.p),"Unity late native entry still consumes jump after diagnostic timeout");
    }
}
