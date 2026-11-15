using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;

int assertions=0, serial=0;
ZombieMode mode=null;
GameWorld world=null;
LocalGame game=null;
void Check(bool value,string message) { assertions++; if(!value)throw new InvalidOperationException("FAIL: "+message); }
BotOwner Setup(bool track=true)
{
    Time.time=1000;Time.timeScale=1;Physics.Occluded=true;Physics.Cast=null;Physics.Casts=0;
    NavMesh.Reachable=false;NavMesh.Sample=true;NavMesh.Path=null;NavMesh.Calls=0;
    ArenaSession.Exact=true;ArenaRespawnDriver.IsRespawnInProgress=ArenaRespawnDriver.StandDown=false;
    game=new();Singleton<AbstractGame>.Instance=game;Singleton<IBotGame>.Instance=game;
    world=new(){MainPlayer=new(){IsAI=false,IsYourPlayer=true,Position=new(40,0,0),Profile=new(){Id="pmc"}}};
    world.Live.Add("pmc",world.MainPlayer);Singleton<GameWorld>.Instance=world;
    mode=new();ArenaMatchService.Current=new(){Mode=mode};ZombieSpawnService.Reset();
    return Add(track);
}
BotOwner Add(bool track=true)
{
    var bot=new BotOwner();bot.Profile.Id="infected"+(++serial);bot.GetPlayer.AIData.BotOwner=bot;
    bot.Memory.GoalEnemy=new(){Person=world.MainPlayer};bot.Mover.TargetPoint=world.MainPlayer.Position;
    world.Live.Add(bot.ProfileId,bot.GetPlayer);game.BotsController.Bots.BotOwners.Add(bot);
    if(track)
    {
        Check(ZombieSpawnService.TryRequest(WildSpawnType.infectedAssault,BotDifficulty.normal),"owned dispatch accepted");
        BurstSpawnService.Arrival(bot);
    }
    return bot;
}
int Run(float duration,float timeout=100,int wave=1,int size=50,Action action=null)
{
    int credits=0;
    for(int i=0;i<duration*4;i++)
    {
        Time.time+=.25f;action?.Invoke();int before=NavMesh.Calls;
        credits+=ZombieRecoveryRuntime.Tick(mode,wave,size,timeout);
        Check(NavMesh.Calls-before<=1,"recovery calculates at most one native path per evaluation");
    }
    return credits;
}

var bot=Setup();
Check(ZombiePathBudgetRuntime.Resets>0,"spawn lifecycle resets path planning budget");
Check(ZombieRecoveryRuntime.OwnsCurrentBody(bot),"confirmed owned arrival is retained");
Check(Run(25)==0 && bot.HealthController.IsAlive,"immobility alone cannot retire before repeated fresh route proof");
Check(Run(3)==1 && !bot.HealthController.IsAlive,"repeated unreachable proof returns exactly one confirmed slot");
Check(Run(10)==0,"dead body cannot return a second slot");
Check(bot.BotsGroup.Searches==2,"one pursuit refresh before retirement plus arrival awareness");

bot=Setup();NavMesh.Reachable=true;
Check(Run(33)==0,"complete route requires renewed sustained movement intent");
Check(Run(7)==1,"physical blocked route recovers after fresh sustained input without displacement");

bot=Setup();Check(Run(45,action:()=>bot.GetPlayer.Position=new(Time.time-1000,0,0))==0,"real movement prevents recovery");
bot=Setup(false);Check(!ZombieRecoveryRuntime.OwnsCurrentBody(bot)&&Run(50)==0,"ambient unowned infected never recovers");
bot=Setup();Check(Run(50,timeout:0)==0,"disabled watchdog also disables individual recovery");
bot=Setup();world.MainPlayer.Position=new(7,0,0);Check(Run(50)==0,"near survivor prevents recovery");
bot=Setup();Physics.Occluded=false;Check(Run(50)==0,"visible survivor prevents recovery");
bot=Setup();Physics.Cast=(a,b)=>a.y<1;Check(Run(50)==0,"clear head sight alone prevents recovery");
bot=Setup();world.MainPlayer.MovementContext.IsGrounded=false;Check(Run(50)==0,"airborne target is not unreachable proof");
bot=Setup();NavMesh.Sample=false;Check(Run(50)==0,"unknown target mesh sample cannot prove failure");
bot=Setup();bot.ShootData.Shooting=true;Check(Run(50)==0,"shooting zombie cannot vanish");
bot=Setup();Check(Run(50,action:()=>bot.ShootData.LastTriggerPressd=Time.time)==0,"recent trigger resets stationary proof");
bot=Setup();Check(Run(50,action:()=>bot.Memory.LastTimeHit=Time.time)==0,"recent received hit resets stationary proof");
bot=Setup();Check(Run(50,action:()=>bot.WeaponManager.Melee.LastTimeEnemyHit=Time.time)==0,"recent melee hit resets stationary proof");
bot=Setup();bot.Memory.GoalEnemy.IsVisible=true;Check(Run(50)==0,"native visible combat blocks recovery even with physical occlusion");
foreach(var disableIntent in new Action<BotOwner>[] {
    b=>b.GetPlayer.MovementContext.MovementDirection=default,
    b=>b.Mover.Pause=true,
    b=>b.Mover.TargetPoint=new(0,0,0),
    b=>b.DoorOpener.Interacting=true,
    b=>b.DoorOpener._enteringDoorSequence=true,
    b=>b.DoorOpener._shallStartInteract=true,
    b=>b.Mover.CurrentState=EBotMoverState.NearDoor,
    b=>b.StandBy.StandByType=BotStandByType.paused,
    b=>b.StandBy.StandByType=BotStandByType.goToSave
})
{
    bot=Setup();NavMesh.Reachable=true;disableIntent(bot);
    Check(Run(55)==0,"stale moving flag door sequence or unrelated path is not sustained pursuit");
}

foreach(var mutate in new Action<BotOwner>[] {
    b=>world.Live[b.ProfileId]=new(){Profile=b.Profile},
    b=>b.GetPlayer.AIData.BotOwner=new(),
    b=>b.Follows=true,
    b=>b.BotFollower.HaveBoss=true,
    b=>b.Profile.Info.Settings.Role=WildSpawnType.infectedTagilla,
    b=>b.GetPlayer.IsAI=false,
    b=>b.GetPlayer.IsYourPlayer=true,
    b=>b.BotState=EBotState.NonActive,
    b=>game.Status=GameStatus.Stopping,
    b=>ArenaSession.Exact=false,
    b=>ArenaRespawnDriver.StandDown=true,
    b=>ArenaRespawnDriver.IsRespawnInProgress=true,
    b=>Time.timeScale=0,
    b=>mode.IsRecoveryAdmissionOpen=false,
    b=>ArenaMatchService.Current=new(){Mode=new ZombieMode()},
    b=>Singleton<GameWorld>.Instance=new(){MainPlayer=world.MainPlayer}
})
{
    bot=Setup();mutate(bot);Check(Run(50)==0 && bot.HealthController.IsAlive,"foreign stale inactive follower terminal paused or rebinding state rejects recovery");
}
bot=Setup();bot.BotState=EBotState.PreActive;Check(Run(30)==0,"preactive owned body waits for activation");
bot.BotState=EBotState.Active;Check(Run(30)==1,"preactive arrival retains ownership after activation");

bot=Setup();Check(Run(23)==0,"probe starts without retirement");
world.MainPlayer.Position=new(60,0,0);Check(Run(5)==0,"moving survivor discards previous unreachable evidence");
Check(Run(25)==1,"fresh stable survivor observation may recover later");
bot=Setup();Run(23);mode.IsRecoveryAdmissionOpen=false;Run(30);mode.IsRecoveryAdmissionOpen=true;
Check(Run(20)==0,"paused proof cannot age into immediate retirement");Check(Run(10)==1,"fresh post pause evidence permits recovery");

bot=Setup();var friend=new Player(){Position=new(50,0,0),Profile=new(){Id="friend"}};world.Live.Add(friend.ProfileId,friend);
mode.IsTeamArena=true;mode.LiveTeamMembers=new[]{world.MainPlayer,friend};bot.Mover.IsMoving=false;
NavMesh.Path=(a,b)=>b.x==50;
Check(Run(65)==0,"one reachable companion defeats all unreachable proof");
bot=Setup();friend=new(){Position=new(3,0,0),Profile=new(){Id="friend"}};world.Live.Add(friend.ProfileId,friend);
mode.IsTeamArena=true;mode.LiveTeamMembers=new[]{world.MainPlayer,friend};
Check(Run(45)==0,"near companion protects zombie even while local survivor is far");

bot=Setup();bot.HealthController.RefuseKill=true;
Check(Run(50)==0 && bot.HealthController.IsAlive && bot.HealthController.Kills>0,"failed native death never returns quota");
bot=Setup();var originalHealth=bot.HealthController;originalHealth.AfterKill=()=>bot.GetPlayer=null;
Check(Run(30)==1 && !originalHealth.IsAlive,"synchronous owner teardown still credits confirmed captured death");
bot=Setup();bot.HealthController.ThrowAfterKill=true;Check(Run(30)==1,"throwing death subscriber still credits actual dead controller");
bot=Setup();bot.HealthController.AfterKill=()=>{mode=new();ArenaMatchService.Current=new(){Mode=mode};};
Check(Run(30)==0,"death callback cannot credit a different match");
bot=Setup();Run(23);ZombieSpawnService.Reset();Check(Run(40)==0,"raid reset invalidates old recovery entries");
bot=Setup();Run(23);Singleton<GameWorld>.Instance=null;ZombieRecoveryRuntime.Suspend();
Check(Retained()==0,"lost world suspension safely releases ownership");

bot=Setup();NavMesh.Path=(a,b)=>a.x==0;bot.Mover.IsMoving=false;
var other=Add();other.GetPlayer.Position=new(0,0,20);
NavMesh.Path=(a,b)=>a.z==0;
Check(Run(75)==1 && bot.HealthController.IsAlive && !other.HealthController.IsAlive,"bounded no intent probe cannot starve another stuck body");
bot=Setup();other=Add();Check(Run(65,size:5)==1,"per wave replacement cap bounds recycling");
for(int i=0;i<64;i++)Add().HealthController.IsAlive=false;
Run(5,size:5);
int Retained() => ((System.Collections.ICollection)typeof(ZombieRecoveryRuntime).GetField("Entries",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null)).Count;
Check(Retained()==1,"exhausted allowance still prunes dead owned bodies");
Check(Run(35,wave:2,size:5)==1,"new wave receives fresh bounded recovery allowance");
bot=Setup();for(int i=0;i<64;i++)Add().HealthController.IsAlive=false;
Run(1,timeout:0);Check(Retained()==1,"disabled recovery never retains historical dead bodies");

// Execute actual pure policy: administrative death returns owed work and must
// not manufacture watchdog progress, even when an arrival masks another death.
var config=new ZombieWavePolicy.Config { MaxAlive=3,WaveStartSize=5,SpawnIntervalSeconds=.2f,IntermissionSeconds=10,StuckWaveTimeoutSeconds=100 };
var state=ZombieWavePolicy.Initial();state.SpawnedThisWave=5;state.LastAlive=2;state.LastArrivedTotal=4;state.StuckAliveSince=1;
ZombieWavePolicy.ReportRecoveredSpawn(ref state);
var result=ZombieWavePolicy.Evaluate(ref state,config,20,1,1,4);
Check(state.SpawnedThisWave==4 && result.Phase==ZombieWavePolicy.Phase.Wave && result.Remaining==3,"confirmed recovery owes replacement without clearing pending tail");
Check(state.StuckAliveSince==1,"administrative death is excluded from combat progress");
// Prior cap5: alive3+pending2, four arrived and one previously died. One
// pending arrival coincides with admin+combat deaths, leaving alive2+pending1.
config.MaxAlive=5;config.WaveStartSize=6;
state=ZombieWavePolicy.Initial();state.SpawnedThisWave=6;state.LastAlive=3;state.LastArrivedTotal=4;state.StuckAliveSince=1;
ZombieWavePolicy.ReportRecoveredSpawn(ref state);
result=ZombieWavePolicy.Evaluate(ref state,config,20,2,1,5);
Check(state.StuckAliveSince==20,"genuine simultaneous combat death still resets progress");
state.Phase=ZombieWavePolicy.Phase.Intermission;bool threw=false;
try{ZombieWavePolicy.ReportRecoveredSpawn(ref state);}catch(InvalidOperationException){threw=true;}
Check(threw,"recovery cannot return another wave slot");
Console.WriteLine($"ZOMBIE_RECOVERY_CHECKS_PASSED {assertions} assertions");
