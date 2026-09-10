using ArenaMode.Movement;
using Comfort.Common;
using EFT;
using EFT.Vaulting;
using UnityEngine;

internal static class ObservationChecks
{
    internal static void Run(Action<bool,string> check,Func<(Player p,MovementContext c,VaultingComponent v)> setup)
    {
        var high=setup(); high.v.VaultingHeight=1.7f; high.p.ToggleVaulting();
        check(!ArenaAirVault.HasPendingRequest(high.p) && !high.c.PlayerAnimator.Climb && high.p.VaultNotifications==0,
            "airborne climb above native clip reach waits without committing hands or flags");
        high.v.VaultingHeight=1.31f; high.p.ToggleVaulting();
        check(ArenaAirVault.HasPendingRequest(high.p),"held input retries as rising jump brings ledge into native reach");
        var refresh=setup(); var ra=UnityFixture.Create(refresh.c); refresh.p.ToggleVaulting();
        refresh.v.VaultingHeight=1.311f; refresh.p.BodyUpdate(.02f);
        check(ra.CrossFades==0 && !refresh.c.PlayerAnimator.Climb,"falling away before dispatch cannot retain earlier reachable geometry");
        var vault=setup(); vault.v.VaultingHeight=1.7f; vault.v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=true; vault.p.ToggleVaulting();
        check(vault.c.PlayerAnimator.Vault,"climb clip bound does not change native vault preference");
        var standing=setup(); standing.c.IsGrounded=true; standing.c.JumpSet=false; standing.v.VaultingHeight=1.7f;
        check(standing.v.TryVaulting(),"standing Arena climb is not narrowed by airborne clip bound");

        (Player p,MovementContext c,VaultingComponent v,Animator a) Enter()
        {
            var(p,c,v)=setup(); var a=UnityFixture.Create(c); ArenaMode.Plugin.ModLogger.Info.Clear();
            p.ToggleVaulting(); p.BodyUpdate(.02f); a.Update(.02f); return(p,c,v,a);
        }
        string[] Logs()=>ArenaMode.Plugin.ModLogger.Info.Where(x=>x.StartsWith("Arena mantle motion #")).ToArray();
        var observed=Enter();
        check(Logs().Length==1 && Logs()[0].Contains("height=1.2") && Logs()[0].Contains("distance=0.4"),"motion entry records accepted native geometry");
        var movement=(VaultingMovementState)observed.c.CurrentState;
        observed.c.CharacterController.AcceptedFraction=new(1,.2f,1);
        for(int i=0;i<2;i++) { var delta=new Vector3(0,.5f,.2f); movement.ApplyMotion(ref delta,.02f); }
        observed.c.CharacterController.height=1.1f; ArenaAirVault.Tick();
        observed.c.CurrentState=new MovementState(); Time.unscaledTime+=1; ArenaAirVault.Tick();
        var exit=Logs().Single(x=>x.Contains(" exit:"));
        check(exit.Contains("requested=(0,1,0.4)") && exit.Contains("applied=(0,0.2,0.4)") && exit.Contains("steps=2"),
            "motion evidence separates requested root motion from accepted controller movement");
        check(exit.Contains("peakRise=0.2") && exit.Contains("minCapsule=1.1"),"motion evidence records peak rise and capsule minimum");
        observed.c.TransformPosition=new(0,-.3f,.4f); Time.unscaledTime+=.8f; ArenaAirVault.Tick();
        check(Logs().Length==3 && Logs()[2].Contains("after-exit") && Logs()[2].Contains("displacement=(0,-0.3,0.4)") && !Harness.HasObservation,
            "motion observer records post-exit fall then releases its body");

        for(int variant=0;variant<7;variant++)
        {
            var drift=Enter(); var original=(VaultingMovementState)drift.c.CurrentState;
            if(variant==0)Singleton<GameWorld>.Instance=new();
            if(variant==1)Singleton<AbstractGame>.Instance=new LocalGame();
            if(variant==2)drift.c.CharacterController=new(){Context=drift.c};
            if(variant==3)drift.p.Alive=false;
            if(variant==4)ArenaMovementScope.Active=false;
            if(variant==5)drift.a.runtimeAnimatorController=new();
            if(variant==6)Singleton<GameWorld>.Instance.MainPlayer=new();
            var before=Logs().Length; var sample=Harness.MotionBefore(original,new(0,1,1));
            Harness.MotionAfter(null,sample); ArenaAirVault.Tick();
            check(Logs().Length==before && !Harness.HasObservation && drift.c.PlayerAnimator.Climb,
                "motion observer retires stale scope without touching native flags");
        }
        var controllerDrift=Enter(); var oldController=controllerDrift.c.CharacterController;
        controllerDrift.c.CharacterController=new(){Context=controllerDrift.c}; ArenaAirVault.Tick();
        check(!Harness.HasObservation,"motion observer binds the exact character controller");

        var nativeError=Enter(); var intended=new InvalidOperationException("physics");
        nativeError.c.CharacterController.OnMove=()=>throw intended;
        Exception actual=null; var motion=new Vector3(0,.1f,0);
        try {((VaultingMovementState)nativeError.c.CurrentState).ApplyMotion(ref motion,.02f);} catch(Exception ex){actual=ex;}
        check(ReferenceEquals(actual,intended),"motion diagnostics preserve original physics exception");
        nativeError.c.CurrentState=new MovementState(); ArenaAirVault.Tick();
        check(Logs().Any(x=>x.Contains("nativeError=True")),"motion diagnostics identify native motion exceptions");

        var timeout=Enter(); Time.unscaledTime+=8.1f; ArenaAirVault.Tick();
        check(!Harness.HasObservation && timeout.c.PlayerAnimator.Climb && Logs().Any(x=>x.Contains("timeout")),"motion observation timeout never cancels native mantle");
        var shut=Enter(); ArenaAirVault.Shutdown();
        check(!Harness.HasObservation && shut.c.PlayerAnimator.Climb,"shutdown releases physical observer without changing native mantle");

        var budget=Enter();
        for(int i=0;i<15;i++)
        {
            budget.c.CurrentState=new MovementState(); ArenaAirVault.Tick(); Time.unscaledTime+=1; ArenaAirVault.Tick();
            budget.c.CurrentState=new JumpPlayerState(){MovementContext=budget.c}; budget.c.Vaulting=false;
            budget.c.PlayerAnimator.Climb=false; budget.c.JumpSet=true;
            budget.a=UnityFixture.Create(budget.c); budget.p.ToggleVaulting(); budget.p.BodyUpdate(.02f); budget.a.Update(.02f);
        }
        check(Logs().Count(x=>x.Contains(" entry:"))==12 && Logs().Length<=36,"physical diagnostics have a separate finite per-world attempt budget");
        var reset=Enter(); check(Logs().Length==1,"new world resets physical diagnostic budget");
        ArenaMode.Plugin.ModLogger.ThrowInfo=true;
        try
        {
            var delta=new Vector3(0,.4f,.2f); ((VaultingMovementState)reset.c.CurrentState).ApplyMotion(ref delta,.02f);
            reset.c.CurrentState=new MovementState(); ArenaAirVault.Tick(); Time.unscaledTime+=1; ArenaAirVault.Tick();
        }
        finally {ArenaMode.Plugin.ModLogger.ThrowInfo=false;}
        check(!Harness.HasObservation && reset.c.TransformPosition.y==.4f,"broken diagnostic logger cannot prevent motion or observer cleanup");
    }
}
