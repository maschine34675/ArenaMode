using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;

int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.000001f;
BotOwner Setup(bool bounded=false)
{
    ZombieCrowdRuntime.Reset();Settings.ZombieCrowdEnabled.Value=true;ZombieNativeEventBridge.Active=true;
    ArenaRespawnDriver.IsRespawnInProgress=false;Time.time=100;
    var world=new GameWorld{MainPlayer=new Player()};world.AllAlivePlayersList.Add(world.MainPlayer);
    Singleton<GameWorld>.Instance=world;Singleton<IBotGame>.Instance=new();
    var body=new Player{IsAI=true,Position=new(50,0,0)};
    var bot=new BotOwner{GetPlayer=body,Mover=new(body)};body.AIData.BotOwner=bot;
    world.AllAlivePlayersList.Add(body);Singleton<IBotGame>.Instance.BotsController.Bots.BotOwners.Add(bot);
    ArenaAreaRuntime.CurrentArea=bounded?new object():null;
    ArenaAreaRuntime.IsReady=ArenaAreaRuntime.OwnerValid=ArenaAreaRuntime.Inside=ArenaAreaRuntime.Connected=ArenaAreaRuntime.Unoccupied=true;
    NavMesh.SampleOk=NavMesh.PathOk=NavMesh.Complete=true;NavMesh.Delta=new(.3f,0,.2f);NavMesh.Samples=0;
    Physics.Floor=true;Physics.HeightError=0;Physics.NormalY=1;Physics.Contacts=[];Physics.Calls=0;
    Plugin.ModLogger.Warnings.Clear();return bot;
}
foreach(bool bounded in new[]{false,true})
{
    var bot=Setup(bounded);ZombieCrowdRuntime.Tick();
    Check(bot.Mover.Teleports==1,"ON recalls on bounded/full map");
    Check(Same(bot.Mover.LastTeleport,NavMesh.LastSample)&&!Same(bot.Mover.LastTeleport,NavMesh.LastCandidate),"teleports corrected sample, never raw candidate");
    Check(Same(bot.Mover.LastTeleport,Physics.Checked),"physical clearance checked for exact placed point");
    if(bounded)Check(Same(bot.Mover.LastTeleport,ArenaAreaRuntime.Validated),"bounded path verified for actual sample");
    Settings.ZombieCrowdEnabled.Value=false;bot.GetPlayer.Position=new(60,0,0);Time.time+=3;
    ZombieCrowdRuntime.Tick();
    Check(bot.Mover.Teleports==1&&bot.Mover.Stops==1&&bot.BotsGroup.Searches==1,"OFF never moves/stops/searches or returns bots to old positions");
    Check(Same(bot.Position,new(60,0,0)),"OFF preserves current location");
}
void NoRecall(string reason,Action<BotOwner> arrange,bool bounded=false)
{
    var bot=Setup(bounded);arrange(bot);ZombieCrowdRuntime.Tick();
    Check(bot.Mover.Teleports==0&&bot.Mover.Stops==0&&bot.BotsGroup.Searches==0,reason);
    Check(Plugin.ModLogger.Warnings.Count==0,reason+" is normal rejection, not exception fallback");
}
NoRecall("OFF",_=>Settings.ZombieCrowdEnabled.Value=false);
NoRecall("ordinary raid",_=>ZombieNativeEventBridge.Active=false);
NoRecall("respawning",_=>ArenaRespawnDriver.IsRespawnInProgress=true);
NoRecall("dead local/spectator",_=>Singleton<GameWorld>.Instance.MainPlayer.HealthController.IsAlive=false);
NoRecall("follower",b=>b.Follower=true);
NoRecall("boss follower",b=>b.BotFollower.HaveBoss=true);
NoRecall("noninfected",b=>b.Profile.Info.Settings.Role=WildSpawnType.pmcBEAR);
NoRecall("dead infected",b=>b.GetPlayer.HealthController.IsAlive=false);
NoRecall("stale body",b=>Singleton<GameWorld>.Instance.AllAlivePlayersList.Remove(b.GetPlayer));
NoRecall("preactive",b=>b.BotState=EBotState.PreActive);
NoRecall("no sample",_=>NavMesh.SampleOk=false);
NoRecall("wrong floor",_=>NavMesh.Delta=new(0,3,0));
NoRecall("no path",_=>NavMesh.PathOk=false);
NoRecall("partial path",_=>NavMesh.Complete=false);
NoRecall("no physical floor",_=>Physics.Floor=false);
NoRecall("floor too far",_=>Physics.HeightError=.2f);
NoRecall("steep floor",_=>Physics.NormalY=.4f);
NoRecall("solid obstruction",_=>Physics.Contacts=[new()]);
NoRecall("full contact buffer",_=>Physics.Contacts=Enumerable.Range(0,16).Select(_=>new Collider()).ToArray());
NoRecall("bounded pending",_=>ArenaAreaRuntime.IsReady=false,true);
NoRecall("bounded wrong owner",_=>ArenaAreaRuntime.OwnerValid=false,true);
NoRecall("bounded outside",_=>ArenaAreaRuntime.Inside=false,true);
NoRecall("bounded disconnected",_=>ArenaAreaRuntime.Connected=false,true);
NoRecall("bounded reserved/occupied",_=>ArenaAreaRuntime.Unoccupied=false,true);
NoRecall("all ring candidates occupied, no unsafe fallback",bot=>
{
    foreach(float radius in ZombieCrowdPolicy.RingRadii)
    {
        ZombieCrowdPolicy.RingOffset(0,1,ZombieCrowdPolicy.SlotYaw(bot.GetPlayer.Id),radius,out float x,out float z);
        Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(new Player{Position=new Vector3(x,0,z)+NavMesh.Delta});
    }
});
foreach(bool bounded in new[]{false,true})
{
    var bot=Setup(bounded);bot.Profile.Info.Settings.BotDifficulty=BotDifficulty.hard;ZombieCrowdRuntime.Tick();
    Check(bot.Mover.Teleports==1,"shooters also recall");
    Check(ZombieCrowdRuntime.ShouldRunSight(new(),bot,new()),"shooter keeps native sight");
}
foreach(string excluded in new[]{"eligible","off","ordinary","follower","dead","preactive","noninfected"})
{
    var bot=Setup();
    if(excluded=="off")Settings.ZombieCrowdEnabled.Value=false;
    if(excluded=="ordinary")ZombieNativeEventBridge.Active=false;
    if(excluded=="follower")bot.Follower=true;
    if(excluded=="dead")bot.IsDead=true;
    if(excluded=="preactive")bot.BotState=EBotState.PreActive;
    if(excluded=="noninfected")bot.Profile.Info.Settings.Role=WildSpawnType.pmcBEAR;
    var avoidance=new BotLocalAvoidance{_owner=bot,_totalOffset=new(.2f,0,0),_notLinkedOffset=new(0,0,.3f)};
    bool native=ZombieCrowdRuntime.ShouldRunAvoidance(avoidance),eligible=excluded=="eligible";
    Check(native!=eligible,"avoidance scope "+excluded);
    Check(eligible?Same(avoidance._totalOffset,default)&&Same(avoidance._notLinkedOffset,default):avoidance.Drops==0,"both offsets cleared only for eligible ON "+excluded);
    var sight=new EnemyPartVision();bool nativeSight=ZombieCrowdRuntime.ShouldRunSight(sight,bot,new(){Position=bot.Position});
    Check(nativeSight!=eligible&&sight.HasLineOfSight==eligible,"sight scope "+excluded);
    if(eligible)
    {
        avoidance._notLinkedOffset=new(.1f,0,0);ZombieCrowdRuntime.ShouldRunAvoidance(avoidance);
        Check(Same(avoidance._notLinkedOffset,default),"new follower AddPower is cleared on next update too");
        Check(ZombieCrowdRuntime.ShouldRunSight(new(),bot,new(){Position=new(1000,0,0)}),"far target native sight");
    }
}
Console.WriteLine($"ZOMBIE_CROWD_CHECKS_PASSED ({checks} assertions)");
