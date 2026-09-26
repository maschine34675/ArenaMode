using ArenaMode.BigBrainCompat;
using ArenaMode.Combat;
using ArenaMode.Pickups;
using EFT;
using EFT.HealthSystem;
using UnityEngine;
using UnityEngine.AI;

internal static partial class Program
{
    private static void Damage(EBodyPart part,float current,float maximum=100)
        => _local.ActiveHealthController.Parts[part]=new(){Current=current,Maximum=maximum};
    private static void HealthReady()
    {
        ArenaAdministrativeDeathGate.Active=false;
        Ready(ArenaPickupKind.Health);
    }
    private static void HealthCases()
    {
        HealthReady();Damage(EBodyPart.Head,10,35);Damage(EBodyPart.Chest,10,85);Damage(EBodyPart.Stomach,5);
        Check(ArenaHealthPickup.Apply(_local,out var healed)==ArenaHealingResult.Applied&&healed==150,"health budget is 150 total");
        Check(_local.ActiveHealthController.GetBodyPartHealth(EBodyPart.Head).Current==35&&_local.ActiveHealthController.GetBodyPartHealth(EBodyPart.Chest).Current==85&&_local.ActiveHealthController.GetBodyPartHealth(EBodyPart.Stomach).Current==55,"vital parts heal first within native maxima");
        Check(_local.ActiveHealthController.Effects.SetEquals(["Bleeding","Fracture"]),"health pickup does not cure wounds");
        HealthReady();Damage(EBodyPart.LeftArm,0);Damage(EBodyPart.RightArm,1);_local.ActiveHealthController.Destroyed.Add(EBodyPart.RightArm);
        Check(ArenaHealthPickup.Missing(_local)==0&&ArenaHealthPickup.Apply(_local,out healed)==ArenaHealingResult.Declined,"destroyed and zero HP parts are not restored");
        Move(_local,0);Check(State().Phase==ArenaPickupPhase.Available,"no healable health leaves appearance available");
        HealthReady();Damage(EBodyPart.LeftLeg,17,20);Move(_local,0);
        Check(State().Phase==ArenaPickupPhase.Cooldown&&_local.ActiveHealthController.Parts[EBodyPart.LeftLeg].Current==20,"partial health collection clamps and consumes once");
        Check(ArenaPickupRuntime.LastNotice.Contains("+3 HP"),"health notice reports actual confirmed gain");
        foreach(var reject in new Action[]{()=>_local.ActiveHealthController.Player=new(),()=>ArenaAdministrativeDeathGate.Active=true,
            ()=>_local.ActiveHealthController.DamageCoeff=float.NaN,()=>_local.ActiveHealthController.DamageCoeff=0,
            ()=>_world.Ever["local"]=new(),()=>Dm.Round.Number++,()=>ArenaMode.Session.ArenaSession.Exact=false})
        {
            HealthReady();Damage(EBodyPart.Chest,10);reject();
            Check(ArenaHealthPickup.Apply(_local,out healed)==ArenaHealingResult.Declined&&_local.ActiveHealthController.Calls==0,"foreign or excluded health cannot mutate");
        }
        HealthReady();Damage(EBodyPart.Head,10,35);Damage(EBodyPart.Chest,10);
        _local.ActiveHealthController.Changed=(_,_)=>throw new Exception("after health changed");Move(_local,0);
        Check(State().Phase==ArenaPickupPhase.Cooldown&&_local.ActiveHealthController.Calls==1,"callback failure after positive gain consumes partial healing once");
        HealthReady();Damage(EBodyPart.Chest,10);
        _local.ActiveHealthController.Changed=(part,delta)=>{var v=_local.ActiveHealthController.Parts[part];v.Current-=delta;_local.ActiveHealthController.Parts[part]=v;};Move(_local,0);
        Check(State().Phase==ArenaPickupPhase.Retired,"unconfirmed health mutation never rearms");
        HealthReady();Damage(EBodyPart.Head,10,35);Damage(EBodyPart.Chest,10);
        var oldHealth=_local.ActiveHealthController;
        oldHealth.Changed=(_,_)=>_local.ActiveHealthController=new(){Player=_local};
        Check(ArenaHealthPickup.Apply(_local,out healed)==ArenaHealingResult.Applied&&oldHealth.Calls==1&&_local.ActiveHealthController.Calls==0,"health owner replacement stops remaining body parts");
        HealthReady();Damage(EBodyPart.Head,10,35);Damage(EBodyPart.Chest,10);
        _local.ActiveHealthController.Changed=(_,_)=>{ArenaPickupRuntime.Reset();ArenaPickupRuntime.Tick();};Move(_local,0);
        Check(ArenaPickupRuntime.IsLiveActor(_local)&&ArenaPickupView.Created.Last().Disposed==false,"old healing scan preserves replacement generation");
        HealthReady();Damage(EBodyPart.Head,10,35);Damage(EBodyPart.Chest,10);Damage(EBodyPart.Stomach,10);
        _local.ActiveHealthController.Changed=(part,delta)=>{var v=_local.ActiveHealthController.Parts[part];v.Current-=delta/2;_local.ActiveHealthController.Parts[part]=v;};
        ArenaHealthPickup.Apply(_local,out healed);
        Check(_local.ActiveHealthController.Requested==150&&healed==75f,"callback damage cannot increase requested heal budget");
    }

    private static BotOwner AiReady(ArenaPickupKind kind=ArenaPickupKind.DoubleDamage)
    {
        ArenaAdministrativeDeathGate.Active=false;ArenaPowerups.Snapshot=default;
        ArenaMode.BigBrainCompat.Plugin.RuntimeEnabled=true;ArenaMode.Movement.ArenaBotMovementSain.Allowed=true;
        ArenaSainMovementHandoff.State=ArenaSainHandoffState.Ready;ArenaSainMovementHandoff.Calls=0;
        Ready(kind);ArenaPickupObjective.ResetClaims();
        var bot=Bot("seeker",-10).AIData.BotOwner;Step();Step();return bot;
    }
    private static ArenaPickupObjective Acquire(BotOwner bot)
    {Check(ArenaPickupObjective.TryAcquire(bot,new(),out var lease),"eligible bot acquires useful available target");return lease;}
    private static void AiCases()
    {
        var bot=AiReady();var lease=Acquire(bot);Check(lease.Target.Id=="one"&&lease.Current(),"lease points to actual appearance");
        Move(_local,0);Check(!lease.Current(),"local collection revokes bot lease immediately");
        bot=AiReady();lease=Acquire(bot);Step(4.1f);Check(!lease.Current(),"stationary objective expires after four seconds");
        bot=AiReady();lease=Acquire(bot);var replacement=Bot("seeker",-10);Check(!lease.Current(),"profile ID reuse never transfers objective life");
        foreach(var reject in new Action<BotOwner>[] {b=>b.Memory.IsUnderFire=true,b=>b.Medecine.FirstAid.Using=true,b=>b.Medecine.SurgicalKit.Using=true,
            b=>b.WeaponManager.Reload.Reloading=true,b=>b.WeaponManager.Grenades.ThrowindNow=true,b=>b.BewareGrenade.Active=true,b=>b.BewareBTR.Active=true,
            b=>b.ArtilleryDangerPlace.Active=true,b=>b.FlashGrenade.IsFlashed=true,b=>b.DoorOpener.Interacting=true,b=>b.Mover.CurrentState=EBotMoverState.NearDoor,
            b=>ArenaMode.Movement.ArenaBotMovementSain.Allowed=false,b=>ArenaMode.BigBrainCompat.Plugin.RuntimeEnabled=false,
            b=>ArenaPickupSettings.BotsCollectPowerups.Value=false,b=>b.GetPlayer.ActiveHealthController=new(){Player=b.GetPlayer}})
        {
            bot=AiReady();lease=Acquire(bot);reject(bot);Check(!lease.Current(),"combat danger busy state or owner change yields objective");
        }
        bot=AiReady();bot.EnemiesController.EnemyInfos[_local]=new(){Person=_local,IsVisible=true};
        Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"visible enemy blocks pickup travel");
        bot.EnemiesController.EnemyInfos[_local].IsVisible=false;bot.EnemiesController.EnemyInfos[_local].PersonalLastSeenTime=Time.time;
        Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"recent enemy preserves combat priority");
        Step(3.1f);Acquire(bot);
        bot=AiReady();lease=Acquire(bot);var ally=Bot("ally",-8).AIData.BotOwner;ally.BotsGroup=bot.BotsGroup;Step();
        Check(!ArenaPickupObjective.TryAcquire(ally,new(),out _),"same native group does not pile onto one pickup");
        var rival=Bot("rival",-9).AIData.BotOwner;Step();Acquire(rival);var third=Bot("third",-8).AIData.BotOwner;Step();
        Check(!ArenaPickupObjective.TryAcquire(third,new(),out _),"at most two competing bots reserve a pickup");
        bot=AiReady();ArenaPowerups.Snapshot=new(10,30,220);
        Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"healthy active damage buff not sought again");
        bot=AiReady(ArenaPickupKind.Shield);ArenaPowerups.Snapshot=new(0,20,200);
        Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"healthy shield not sought again");
        foreach(var kind in new[]{ArenaPickupKind.Weapon,ArenaPickupKind.JumpBoots,ArenaPickupKind.InfiniteAmmo,ArenaPickupKind.Health})
        {bot=AiReady(kind);Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"local only or unneeded health never targeted");}
        bot=AiReady(ArenaPickupKind.Health);bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Chest]=new(){Current=40,Maximum=100};Acquire(bot);
        Move(bot.GetPlayer,0);Check(State().Phase==ArenaPickupPhase.Cooldown,"injured bot can collect health");
        bot=AiReady(ArenaPickupKind.Health);
        bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Chest]=new(){Current=10,Maximum=100};
        bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Stomach]=new(){Current=10,Maximum=100};
        var healthTargets=new List<ArenaPickupTarget>();ArenaPickupRuntime.GetBotTargets(bot.GetPlayer,healthTargets);
        Check(healthTargets.Count==1&&healthTargets[0].Utility==225f,"bot values the full 150 HP benefit");
        bot=AiReady(ArenaPickupKind.Health);bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Chest]=new(){Current=40,Maximum=100};lease=Acquire(bot);
        bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Chest]=new(){Current=100,Maximum=100};ArenaPickupObjective.Prune();
        bot.GetPlayer.ActiveHealthController.Parts[EBodyPart.Chest]=new(){Current=40,Maximum=100};
        Check(!lease.Current(),"pruned claim can never regain execution rights");
        bot=AiReady();NavMesh.Status=NavMeshPathStatus.PathPartial;Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"partial route never assigned");
        bot=AiReady();ArenaMode.Areas.ArenaAreaRuntime.CurrentArea=new(){Bounds=new(){PathAllowed=false}};
        Check(!ArenaPickupObjective.TryAcquire(bot,new(),out _),"bot route must stay within active area");
        bot=AiReady();var layer=new ArenaPickupLayer(bot,35);Check(layer.IsActive(),"BigBrain layer admits objective");
        var action=layer.GetNextAction();var logic=new ArenaPickupLogic(bot);logic.Update(action.Data);
        Check(bot.RouteCalls==1&&bot.Mover.HasPathAndNoComplete&&bot.Sprinting,"owned action starts exact native route");
        bot.Memory.IsUnderFire=true;logic.Update(action.Data);Check(!bot.Mover.HasPathAndNoComplete&&!bot.Sprinting,"combat interruption stops owned motion");
        layer.Stop();bot.Memory.IsUnderFire=false;Check(!layer.IsActive(),"interruption applies retry backoff");
        bot=AiReady();lease=Acquire(bot);bot.RouteResult=NavMeshPathStatus.PathPartial;logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        Check(lease.Released&&!bot.Mover.HasPathAndNoComplete,"failed native route is stopped and released");
        bot=AiReady();lease=Acquire(bot);ArenaSainMovementHandoff.State=ArenaSainHandoffState.WaitingForDeactivation;logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        Check(bot.RouteCalls==0,"SAIN must release action before native movement");
        ArenaSainMovementHandoff.State=ArenaSainHandoffState.Ready;logic.Update(new ArenaPickupAction(lease));Check(bot.RouteCalls==1,"native route starts after confirmed SAIN handoff");
        var oldMover=bot.Mover;bot.Mover=new();bot.GetPlayer=new();logic.Stop();Check(bot.Mover.Stops==0,"cleanup does not stop replacement body mover");
        bot=AiReady();lease=Acquire(bot);logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        bot.Mover._moverStateMachine.DefaultMoverState.LastTargetPoint=lease.Target.Position;
        bot.Mover.CurrentState=EBotMoverState.NearDoor;bot.Mover.TargetPoint=new Vector3(-5,0,0);
        var doorPath=bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath;
        logic.Stop();
        Check(bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath==null&&bot.Mover._moverStateMachine.DefaultMoverState.LastTargetPoint==null,"cancelled pickup path cannot revive after a door");
        Check(ReferenceEquals(doorPath,bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath),"cleanup preserves native door interaction path");
        bot=AiReady();lease=Acquire(bot);logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        bot.GetPlayer.Position=new(-9,0,0);bot.Mover.DistDestination=9;lease.Progress(false);
        Time.time+=2;bot.GetPlayer.Position=new(-8.5f,0,0);bot.Mover.DistDestination=.2f;lease.Progress(true);
        Time.time+=2;bot.Mover.DistDestination=8;lease.Progress(false);
        Time.time+=3;bot.GetPlayer.Position=new(-8,0,0);bot.Mover.DistDestination=7;lease.Progress(false);
        Check(lease.Current(),"door distance cannot poison resumed route progress");
        Time.time+=21;Check(!lease.Current(),"door progress never extends absolute trip limit");
        bot=AiReady();bot.GetPlayer.Position=new(-30,0,0);lease=Acquire(bot);
        bot.Mover.TargetPoint=lease.Target.Position;
        for(int i=1;i<=20;i++)
        {
            Time.time+=1;bot.GetPlayer.Position=new(-30+i*.75f,0,0);bot.Mover.DistDestination=30-i*.75f;lease.Progress(false);
            Check(i<20?lease.Current():!lease.Current(),"continuous progress cannot exceed twenty-second trip cap");
        }
        bot=AiReady();lease=Acquire(bot);logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        bot.Mover._moverStateMachine.DefaultMoverState.LastTargetPoint=lease.Target.Position;
        bot.Mover.CurrentState=EBotMoverState.NearDoor;bot.Mover.TargetPoint=new Vector3(-5,0,0);bot.DoorStatus=DoorInteractionStatus.OpeningDoor;
        doorPath=bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath;
        logic.Update(new ArenaPickupAction(lease));
        Check(bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath==null&&bot.Mover._moverStateMachine.DefaultMoverState.LastTargetPoint==null,"door entry detaches default route before delayed native return");
        Check(ReferenceEquals(doorPath,bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath),"door entry preserves temporary native path");
        bot.Mover.CurrentState=EBotMoverState.Default;bot.DoorStatus=DoorInteractionStatus.CanRun;bot.Memory.IsUnderFire=true;
        logic.Update(new ArenaPickupAction(lease));Check(!bot.Mover.HasPathAndNoComplete&&bot.RouteCalls==1,"delayed door return with combat cannot revive expired objective");
        bot=AiReady();lease=Acquire(bot);logic=new(bot);logic.Update(new ArenaPickupAction(lease));
        bot.Mover.TargetPoint=new Vector3(50,0,0);var foreignPath=bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath;
        logic.Stop();Check(ReferenceEquals(foreignPath,bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath),"cleanup preserves newer foreign default path");
        bot=AiReady();NavMesh.PathOk=false;layer=new(bot,35);layer.IsActive();int calls=NavMesh.Paths;
        for(int i=0;i<100;i++)layer.IsActive();Check(NavMesh.Paths==calls,"failed objective scans are throttled between brain ticks");
    }
    private static void ObjectiveCases(){HealthCases();AiCases();Console.WriteLine("PICKUP_OBJECTIVE_BEHAVIOR_PASSED");}
}
