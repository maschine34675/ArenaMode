using ArenaMode.BigBrainCompat;
using ArenaMode.Bots;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;

static class Program
{
    private static int _checks;
    private static GameWorld _world;
    private static LocalGame _game;
    private static ArenaMatch _match;
    private static List<BotOwner> _bots;
    private static DominationObjectivePoint[] _points;
    static void Assert(bool value,string name){_checks++;if(!value)throw new Exception("FAIL: "+name);}
    static void Setup(int count=6,int pointCount=3)
    {
        Time.time=10;Time.timeScale=1;NavMesh.Paths=NavMesh.Samples=true;NavMesh.Calls=0;
        ArenaSainMovementHandoff.State=ArenaSainHandoffState.Ready;
        Library.RuntimeEnabled=true;ArenaMode.Session.ArenaSession.Exact=true;TeamDeathmatchRelations.Enabled=true;
        ArenaMode.Movement.ArenaBotMovementSain.Allowed=true;ArenaMode.Areas.ArenaAreaRuntime.CurrentArea=null;
        ArenaMode.Pickups.ArenaPickupGeometry.Clear=true;
        _world=new();_game=new(){GameWorld=_world};_match=new();_bots=[];
        Singleton<GameWorld>.Instance=_world;Singleton<AbstractGame>.Instance=_game;ArenaMatchService.Current=_match;
        _match.Mode.ObjectiveCount=pointCount;
        _points=Enumerable.Range(0,pointCount).Select(i=>new DominationObjectivePoint(
            ((char)('A'+i)).ToString(),new(i*25,0,0),null,false)).ToArray();
        for(int i=0;i<count;i++)
        {
            var body=new Player(){ProfileId="bot"+i,Side=i%2==0?EPlayerSide.Usec:EPlayerSide.Bear,Position=new(-20,0,-20)};
            var bot=new BotOwner(){GetPlayer=body};body.AIData.BotOwner=bot;_world.Bodies[body.ProfileId]=body;_bots.Add(bot);_match.Mode.Owned.Add(body);
        }
        DominationObjectiveBridge.Available=true;DominationObjectiveBridge.Begin(_match,_game,_world);Tick();
    }
    static void Tick()=>DominationObjectiveBridge.Update(_points,_bots,true);
    static DominationObjectiveLease Lease(BotOwner bot){Assert(DominationObjectiveBridge.TryGet(bot,out var lease),"assignment exists");return lease;}
    static (ArenaDominationLayer,ArenaDominationLogic,ArenaDominationAction) Action(BotOwner bot)
    {
        var layer=new ArenaDominationLayer(bot,75);Assert(layer.IsActive(),"layer active");
        var data=(ArenaDominationAction)layer.GetNextAction().Data;var logic=new ArenaDominationLogic(bot);return(layer,logic,data);
    }
    static void Main()
    {
        Setup();
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
            Assert(_bots.Where(b=>b.GetPlayer.Side==side).Select(b=>Lease(b).PointId).Distinct().Count()==3,"distribute each team across three points");
        var first=Lease(_bots[0]);Time.time+=1;Tick();Assert(ReferenceEquals(first,Lease(_bots[0])),"stable assignment");
        Assert(first.Inside(first.Center+new Vector3(3.9f,.9f,0)),"capture volume inside");
        Assert(!first.Inside(first.Center+new Vector3(0,1.1f,0)),"capture wrong floor rejected");
        Assert(!first.Inside(first.Center+new Vector3(4.1f,0,0)),"capture outside radius rejected");
        Assert(first.Inside(first.Destination),"slot offset remains inside capture");
        ArenaMode.Pickups.ArenaPickupGeometry.Clear=false;Assert(!first.Inside(first.Center),"wall prevents false holding");ArenaMode.Pickups.ArenaPickupGeometry.Clear=true;
        _bots[0].Memory.IsUnderFire=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield under fire");
        _bots[0].Memory.IsUnderFire=false;Assert(ReferenceEquals(first,Lease(_bots[0])),"combat preserves assignment");
        _bots[0].WeaponManager.Reload.Reloading=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield reload");
        _bots[0].WeaponManager.Reload.Reloading=false;_bots[0].Medecine.SurgicalKit.Using=true;
        Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield surgery");_bots[0].Medecine.SurgicalKit.Using=false;
        _bots[0].Medecine.FirstAid.Have2Do=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield pending first aid before SAIN starts");_bots[0].Medecine.FirstAid.Have2Do=false;
        _bots[0].Medecine.SurgicalKit.HaveWork=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield pending surgery before SAIN starts");_bots[0].Medecine.SurgicalKit.HaveWork=false;
        _bots[0].WeaponManager.Reload.BulletCount=0;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield empty magazine before reload starts");_bots[0].WeaponManager.Reload.BulletCount=30;
        _bots[0].BewareGrenade.Active=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield grenade");_bots[0].BewareGrenade.Active=false;
        var enemy=new Enemy(){Person=_bots[1].GetPlayer,IsVisible=true,CanShoot=true};_bots[0].EnemiesController.EnemyInfos[enemy.Person]=enemy;
        Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"yield visible shootable enemy");enemy.IsVisible=false;
        Assert(ArenaDominationRuntime.CanAct(_bots[0],first),"unseen remembered enemy does not block objective");
        _bots[0].DoorOpener.Interacting=true;Assert(!ArenaDominationRuntime.CanAct(_bots[0],first),"foreign door yields");_bots[0].DoorOpener.Interacting=false;
        _bots[0].GetPlayer.HealthController.IsAlive=false;Assert(!first.Current,"dead body revoked");
        Setup();first=Lease(_bots[0]);_world.Bodies[first.Body.ProfileId]=new(){ProfileId=first.Body.ProfileId};Assert(!first.Current,"same profile replacement rejected");
        Setup();first=Lease(_bots[0]);_bots[0].Mover=new();Assert(!first.Current,"mover replacement rejected");
        Setup();first=Lease(_bots[0]);_bots[0].BotsGroup=new();Assert(!first.Current,"group replacement rejected");
        Setup();first=Lease(_bots[0]);_match.Mode.Owned.Remove(first.Body);Assert(!first.Current,"foreign live body cannot claim objective");
        Setup();first=Lease(_bots[0]);Singleton<GameWorld>.Instance=new();Assert(!first.Current,"world replacement rejected");
        Setup();first=Lease(_bots[0]);ArenaMatchService.Current=new();Assert(!first.Current,"match replacement rejected");
        Setup();first=Lease(_bots[0]);_match.Mode.IsDomination=false;Assert(!first.Current,"ordinary TDM excluded");
        Setup();first=Lease(_bots[0]);_match.Mode.IsSearchActive=false;Assert(!first.Current,"countdown excluded");
        Setup();first=Lease(_bots[0]);Time.time+=4;Assert(!first.Current,"lost publisher heartbeat revoked");
        Setup();first=Lease(_bots[0]);DominationObjectiveBridge.Available=false;Assert(!first.Current,"library loss revokes objectives");
        Setup();first=Lease(_bots[0]);DominationObjectiveBridge.Begin(_match,_game,_world);Tick();Assert(!first.Current,"generation reset revokes old lease");
        Setup();first=Lease(_bots[0]);DominationObjectiveBridge.Reject(first);Assert(!first.Current,"path rejection revokes trip");
        Time.time++;Tick();var replacement=Lease(_bots[0]);Assert(replacement.PointId!=first.PointId,"unreachable target cooldown chooses alternative");
        DominationObjectiveBridge.Reject(first);Assert(replacement.Current,"stale rejection cannot revoke replacement");
        Setup();var bot=_bots[0];var (layer,logic,data)=Action(bot);ArenaSainMovementHandoff.State=ArenaSainHandoffState.WaitingForDeactivation;
        logic.Update(data);Assert(bot.RouteCalls==0,"wait for native SAIN handoff");
        ArenaSainMovementHandoff.State=ArenaSainHandoffState.Ready;logic.Update(data);Assert(bot.RouteCalls==1,"native coordinate route acquired");
        Assert(data.OwnsMovement,"action owns movement only after handoff");
        var owned=bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath;
        var newer=new AbstractBotPath(){Target=new(90,0,90)};bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath=newer;
        logic.Stop();Assert(ReferenceEquals(newer,bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath),"cleanup preserves newer path");
        Assert(!data.OwnsMovement,"stop clears action ownership");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);logic.Update(data);
        bot.Mover.CurrentState=EBotMoverState.NearDoor;var door=new AbstractBotPath(){Target=new(2,0,2)};bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath=door;
        logic.Stop();Assert(ReferenceEquals(door,bot.Mover._moverStateMachine.DoorMoverState.PathController.CurPath),"cleanup preserves door path");
        Assert(bot.Mover._moverStateMachine.DefaultMoverState.PathController.CurPath==null,"cleanup clears suspended own path");
        Assert(bot.Mover._moverStateMachine.DefaultMoverState.LastTargetPoint==null,"cleanup clears exact native return target");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);bot.NativeDirectPathFails=true;logic.Update(data);
        Assert(bot.CoverFallbackCalls==0&&bot.TeleportFallbackCalls==0,"no cover reconstruction or teleport fallback");Assert(bot.RouteCalls==2,"bounded two native attempts");
        Assert(!data.Lease.Current,"failed native path rejects assignment");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);NavMesh.Paths=false;logic.Update(data);
        Assert(bot.RouteCalls==0&&NavMesh.Calls==2,"partial preflight denied with bounded attempts");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);logic.Update(data);
        for(int i=0;i<6;i++){Time.time++;Tick();logic.Update(data);}
        Assert(!data.Lease.Current,"stuck travel rejects goal");
        Setup();bot=_bots[0];first=Lease(bot);bot.GetPlayer.Position=first.Center;(layer,logic,data)=Action(bot);logic.Update(data);
        for(int i=0;i<30;i++){Time.time++;Tick();logic.Update(data);}
        Assert(data.Lease.Current&&layer.IsActive()&&!layer.IsCurrentActionEnding(),"stationary defender keeps hold beyond pickup timeout");
        Assert(bot.RouteCalls==0,"holding does not churn routes");
        bot.Memory.IsUnderFire=true;Assert(layer.IsCurrentActionEnding(),"combat ends holding action");logic.Stop();layer.Stop();
        bot.Memory.IsUnderFire=false;Assert(layer.IsActive(),"holding assignment resumes after combat");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);logic.Update(data);
        DominationObjectiveBridge.Reject(data.Lease);Time.time++;Tick();
        Assert(layer.IsActive() && layer.IsCurrentActionEnding(),"planner reassignment ends previous BigBrain action");
        logic.Stop();
        var nextData=(ArenaDominationAction)layer.GetNextAction().Data;
        Assert(!ReferenceEquals(nextData.Lease,data.Lease) && nextData.Lease.Current,"new action owns replacement planner lease");
        logic.Update(nextData);
        Assert(nextData.OwnsMovement && !layer.IsCurrentActionEnding(),"replacement action resumes actual movement");
        Setup();bot=_bots[0];(layer,logic,data)=Action(bot);ArenaSainMovementHandoff.State=ArenaSainHandoffState.WaitingForDeactivation;
        logic.Update(data);for(int i=0;i<5;i++){Time.time++;Tick();logic.Update(data);}
        Assert(!data.Lease.Current&&bot.RouteCalls==0,"handoff wait bounded and no route stolen");
        PointCountContract();
        Console.WriteLine($"Domination bot checks passed: {_checks} assertions.");
    }

    static void PointCountContract()
    {
        foreach(int count in new[]{1,2,3})
        {
            Setup(pointCount:count);
            Assert(_bots.All(bot=>DominationObjectiveBridge.TryGet(bot,out _)),
                "authored point count supports current bot assignments: "+count);
            foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
                Assert(_bots.Where(b=>b.GetPlayer.Side==side).Select(b=>Lease(b).PointId).Distinct().Count()==count,
                    "each team distributes over the authored point count: "+count);
            var original=Lease(_bots[0]);
            Time.time++;Tick();
            Assert(ReferenceEquals(original,Lease(_bots[0])),"variable-count assignments remain stable: "+count);
            _match.Mode.ObjectiveCount=count+1;
            Tick();
            Assert(!original.Current&&!DominationObjectiveBridge.TryGet(_bots[0],out _),
                "publisher count mismatch revokes existing leases");
            _match.Mode.ObjectiveCount=count;
            Time.time++;Tick();
            Assert(DominationObjectiveBridge.TryGet(_bots[0],out _),"matching current publisher can resume after mismatch");
            original=Lease(_bots[0]);
            var stalePoints=_points.Take(count-1).ToArray();
            DominationObjectiveBridge.Update(stalePoints,_bots,true);
            Assert(!original.Current&&!DominationObjectiveBridge.TryGet(_bots[0],out _),
                "stale shorter point snapshot cannot retain movement authority");
        }
        Setup(pointCount:16);
        Assert(_bots.All(bot=>DominationObjectiveBridge.TryGet(bot,out _)),"maximum supported count admits current assignments");
        Setup(pointCount:17);
        Assert(_bots.All(bot=>!DominationObjectiveBridge.TryGet(bot,out _)),"more than sixteen points rejected even when mode agrees");
        Setup(pointCount:0);
        Assert(_bots.All(bot=>!DominationObjectiveBridge.TryGet(bot,out _)),"zero point snapshot grants no assignment");
        foreach(string defect in new[]{"null","duplicate","empty-id","nonfinite"})
        {
            Setup(pointCount:2);var prior=Lease(_bots[0]);
            var malformed=_points.ToArray();
            if(defect=="null")malformed=null;
            else if(defect=="duplicate")malformed[1]=new(malformed[0].Id,malformed[1].Position,null,false);
            else if(defect=="empty-id")malformed[0]=new("",malformed[0].Position,null,false);
            else malformed[0]=new("A",new(float.NaN,0,0),null,false);
            DominationObjectiveBridge.Update(malformed,_bots,true);
            Assert(!prior.Current&&!DominationObjectiveBridge.TryGet(_bots[0],out _),
                "malformed publisher revokes previous assignment: "+defect);
        }
    }
}
