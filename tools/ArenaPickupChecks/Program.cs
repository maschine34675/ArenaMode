using System.Collections;
using System.Reflection;
using ArenaMode;
using ArenaMode.Areas;
using ArenaMode.Hud;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;

internal static partial class Program
{
    private static int _checks;
    private static readonly string Scratch=Path.Combine(Path.GetTempPath(),"ArenaPickupChecks-"+Guid.NewGuid().ToString("N"));
    private static LocalGame _game;
    private static GameWorld _world;
    private static Player _local;
    private static RoundsMode Dm=>(RoundsMode)ArenaMatchService.Current.Mode;
    private static string LayoutPath=>Path.Combine(Scratch,"ArenaMode","Pickups.json");
    private static ArenaPickupState State(int index=0)
    {
        var entries=(IList)typeof(ArenaPickupRuntime).GetField("Entries",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        return (ArenaPickupState)entries[index].GetType().GetField("State",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entries[index]);
    }
    private static void Check(bool value,string message){_checks++;if(!value)throw new Exception("ASSERT: "+message);}
    private static void Throws(Action action,string message)
    {bool thrown=false;try{action();}catch{thrown=true;}Check(thrown,message);}
    private static ArenaPickupPoint Point(ArenaPickupKind kind=ArenaPickupKind.DoubleDamage)=>new(){Id="one",Kind=kind,KitId=kind==ArenaPickupKind.Weapon?"spartan":null,X=0,Y=0,Z=0,Yaw=45,RespawnSeconds=10};
    private static ArenaPickupLayoutFile FileFor(ArenaPickupPoint point=null)=>new(){Layouts=[new(){MapId="factory4_day",Points=[point??Point()]}]};
    private static void Fixture(ArenaPickupKind kind=ArenaPickupKind.DoubleDamage)
    {
        ArenaMatchService.Current=null;ArenaPickupRuntime.Tick();
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(FileFor(Point(kind))));
        Plugin.Instance.Info.Location=Path.Combine(Scratch,"client.dll");Plugin.ModLogger.Lines.Clear();
        ArenaSession.IsActive=true;ArenaSession.Exact=true;
        ArenaMatchService.TeamStartAbortPending=false;ArenaMode.Compat.PitFireTeamCompat.TeamArenaActive=false;
        ArenaAreaRuntime.IsPending=false;ArenaAreaRuntime.CurrentArea=null;
        ArenaPickupSettings.Enabled=new(true);ArenaPickupSettings.BotsCollectPowerups.Value=true;ArenaPickupSettings.WeaponKit.Value="spartan";
        ArenaRespawnDriver.IsRespawnInProgress=false;WeaponSwapService.IsSwapping=false;KitBundlePreloader.IsPreloading=false;
        ArenaMode.Movement.ArenaMovementScope.Enabled=true;
        ArenaHudVisibility.IsBattleUiActive=true;KitCatalog.Kits=[new(){Id="spartan"}];
        Time.time=0;Time.unscaledTime=0;Time.timeScale=1;
        NavMesh.SampleOk=true;NavMesh.PathOk=true;NavMesh.Status=NavMeshPathStatus.PathComplete;
        NavMesh.AllowPath=null;NavMesh.LastPathStart=default;
        NavMesh.Sample=p=>p;NavMesh.Samples=NavMesh.Paths=0;NavMesh.Corners=null;Physics.Overlaps=[];Physics.Capsule=null;
        Physics.Rays=(from,dir,distance)=>dir.y<-.9f?[new(){collider=new Collider(),point=from-Vector3.up*.65f,normal=Vector3.up,distance=.65f}]:[];
        ArenaPickupView.Created.Clear();ArenaPickupView.ThrowOnTick=false;ArenaWeaponPickup.Created.Clear();ArenaWeaponPickup.PrepareNull=false;
        ArenaPowerups.Grants.Clear();ArenaPowerups.Resets=0;ArenaPowerups.GrantResult=true;
        _local=new(){ProfileId="local",IsYourPlayer=true,Position=new(-3,0,0)};
        _world=new(){MainPlayer=_local};_world.Add(_local);
        _game=new(){GameWorld=_world,LocalPlayer=_local};
        Singleton<GameWorld>.Instance=_world;Singleton<AbstractGame>.Instance=_game;
        ArenaMatchService.Current=new(){Mode=new RoundsMode()};
    }
    private static void Step(float seconds=.1f)
    {Time.time+=seconds;Time.unscaledTime+=seconds;_game?.Pump();ArenaPickupRuntime.Tick();_game?.Pump();}
    private static void Ready(ArenaPickupKind kind=ArenaPickupKind.DoubleDamage){Fixture(kind);Step();Step();}
    private static void Move(Player actor,float x,float y=0,float z=0,float seconds=.1f){actor.Position=new(x,y,z);Step(seconds);}
    private static Player Bot(string id,float x)
    {var p=new Player(){ProfileId=id,IsAI=true,Position=new(x,0,0)};p.AIData=new(){BotOwner=new(){GetPlayer=p}};_world.Add(p);return p;}
    private static void Policy()
    {
        var s=new ArenaPickupState();Check(s.Reserve(float.NaN)==0,"nonfinite reservation rejected");
        int token=s.Reserve(1);Check(token!=0&&s.Phase==ArenaPickupPhase.Pending,"reservation holds appearance");
        Check(s.Reserve(2)==0,"second claimant denied");
        Check(!s.Complete(token+1,true,2,10),"foreign token cannot settle");
        Check(!s.Complete(token,true,float.NaN,10),"nonfinite completion rejected");
        foreach(float cd in new[]{float.NaN,float.PositiveInfinity,9f,601f})Check(!s.Complete(token,true,2,cd),"invalid cooldown rejected");
        Check(s.Complete(token,true,2,10)&&s.ReadyAt==12,"confirmed transfer starts cooldown");
        Check(!s.Complete(token,false,3,10),"duplicate callback ignored");
        Check(!s.Tick(11.999f),"cooldown not premature");Check(s.Tick(12)&&s.Phase==ArenaPickupPhase.Available,"cooldown exact deadline");
        int second=s.Reserve(12);Check(second!=token,"appearance tokens change");
        Check(!s.Complete(token,false,12,10),"old token cannot cancel new appearance");
        Check(!s.Complete(second,true,22,10),"deadline completion rejected");s.Tick(22);Check(s.Phase==ArenaPickupPhase.Retired,"timeout retires instead of duplicating");
        Check(s.Reserve(23)==0,"retired point cannot rearm");
        s=new();token=s.Reserve(0);Check(s.Complete(token,false,1,10)&&s.Phase==ArenaPickupPhase.Available,"confirmed unchanged failure releases reservation");
        s.Tick(float.PositiveInfinity);Check(s.Phase==ArenaPickupPhase.Retired,"nonfinite clock fails closed");
        Check(ArenaPickupState.InReach(-2,0,0,2,0,0,0,0,0,true,out var f)&&Math.Abs(f-.5)<.001,"short movement sweep catches point");
        Check(!ArenaPickupState.InReach(-2,0,0,2,0,0,0,0,0,false,out _),"fresh or stale sample uses endpoint");
        Check(!ArenaPickupState.InReach(-3,0,0,3,0,0,0,0,0,true,out _),"teleport does not sweep pickup");
        Check(!ArenaPickupState.InReach(-2,1,0,2,1,0,0,0,0,true,out _),"sweep excludes next floor");
        Check(ArenaPickupState.InReach(1,.8f,0,1,.8f,0,0,0,0,false,out _),"radius includes edge on same floor");
        Check(!ArenaPickupState.InReach(1.01f,0,0,1.01f,0,0,0,0,0,false,out _),"outside radius excluded");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(!ArenaPickupState.InReach(invalid,0,0,0,0,0,0,0,0,true,out _),"invalid positions cannot contact");
    }
    private static void Layouts()
    {
        var factory=ArenaPickupLayouts.Factory("spartan");Check(factory.Layouts.Count==2,"Factory day and night supplied");
        Check(factory.Layouts.All(x=>x.Points.Count==6&&x.Points.Select(p=>p.Kind).Distinct().Count()==6),"each factory prototype has all pickup kinds");
        Check(ArenaPickupLayouts.Select(factory,"FACTORY4_DAY",null)!=null,"map identity case insensitive");
        Check(ArenaPickupLayouts.Select(factory,"factory4_day","new-area")==null,"area never falls back to full map");
        Check(ArenaPickupLayouts.Select(factory,"bigmap",null)==null,"unsupported map has no implicit pickups");
        var decoded=ArenaPickupLayouts.Parse(ArenaPickupLayouts.Serialize(factory));
        Check(decoded.Layouts[1].Points[2].KitId=="spartan"&&decoded.Layouts[1].Points[2].RespawnSeconds==45,"layout roundtrip preserves weapon config");
        Check(decoded.Layouts[1].Points[3].Kind==ArenaPickupKind.JumpBoots,"jump boots survive authoring roundtrip");
        Check((int)ArenaPickupKind.DoubleDamage==0&&(int)ArenaPickupKind.Shield==1&&(int)ArenaPickupKind.Weapon==2,"new pickup preserves legacy enum values");
        Check(factory.Layouts.All(l=>l.Points.All(p=>p.Alternatives.Count==7)),"every Factory pickup retains original plus seven alternatives");
        var source=factory.Layouts[0].Points[0];
        var orders=Enumerable.Range(0,24).Select(seed=>ArenaPickupLayouts.Candidates(source,new Random(seed))).ToList();
        Check(orders.All(p=>p.Count==8&&p.Select(x=>(x.X,x.Y,x.Z)).Distinct().Count()==8),"candidate shuffle retains each possible point exactly once");
        Check(orders.Select(p=>(p[0].X,p[0].Y,p[0].Z)).Distinct().Count()>4,"seeded rounds can choose different positions");
        Check(orders.SelectMany(p=>p).All(p=>p.Id==source.Id&&p.Kind==source.Kind&&p.RespawnSeconds==source.RespawnSeconds),"position selection preserves pickup identity and rules");
        orders[0][0].X=999;
        Check(source.X!=999&&source.Alternatives.All(p=>p.X!=999),"selection never changes frozen layout positions");
        Check(decoded.Layouts[1].Points[0].Alternatives.Count==7,"alternatives survive JSON roundtrip");
        foreach(string json in new[]{"null","{}","{\"Version\":2}","{\"Version\":1,\"Unknown\":true}","{\"Layouts\":null}"})
        {
            // An empty v1 file is deliberately valid; the default fields make {} equivalent.
            if(json=="{}")Check(ArenaPickupLayouts.Parse(json).Layouts.Count==0,"empty config disables all builtin points");
            else Throws(()=>ArenaPickupLayouts.Parse(json),"malformed or unsupported layout rejected");
        }
        Throws(()=>ArenaPickupLayouts.Parse(new string('x',512*1024+1)),"oversized file rejected");
        void Invalid(Action<ArenaPickupLayoutFile> mutate,string name){var file=FileFor();mutate(file);Throws(()=>ArenaPickupLayouts.Validate(file),name);}
        Invalid(x=>x.Layouts.Add(x.Layouts[0]),"duplicate layout rejected");
        Invalid(x=>x.Layouts[0].Points.Add(x.Layouts[0].Points[0]),"duplicate pickup id rejected");
        Invalid(x=>x.Layouts[0].Points[0].X=float.NaN,"nonfinite layout position rejected");
        Invalid(x=>x.Layouts[0].Points[0].Y=100001,"outlandish position rejected");
        Invalid(x=>x.Layouts[0].Points[0].RespawnSeconds=0,"cooldown under ten rejected");
        Invalid(x=>x.Layouts[0].Points[0].Kind=(ArenaPickupKind)99,"undefined pickup type rejected");
        Invalid(x=>x.Layouts[0].Points[0].Alternatives=[new(){X=float.NaN}],"nonfinite alternative rejected");
        Invalid(x=>x.Layouts[0].Points[0].Alternatives=Enumerable.Range(0,16).Select(i=>new ArenaPickupPosition()).ToList(),"alternative count bounded");
        Invalid(x=>x.Layouts[0].Points[0].Kind=ArenaPickupKind.Weapon,"weapon without kit rejected");
        Invalid(x=>x.Layouts[0].AreaId=" ","blank area rejected");
        Invalid(x=>x.Layouts[0].Points=Enumerable.Range(0,65).Select(i=>new ArenaPickupPoint{Id=i.ToString()}).ToList(),"point count bounded");
        var areaFile=FileFor();areaFile.Layouts[0].AreaId="Zone";
        Check(ArenaPickupLayouts.Select(areaFile,"factory4_day","zone")==null,"area identity remains ordinal");
        string exportDir=Path.Combine(Scratch,"exports");string one=ArenaPickupLayouts.Export(exportDir,factory),two=ArenaPickupLayouts.Export(exportDir,factory);
        Check(one!=two&&System.IO.File.Exists(one)&&System.IO.File.Exists(two),"draft export never overwrites prior file");
        Check(ArenaPickupLayouts.Parse(System.IO.File.ReadAllText(one)).Layouts.Count==2,"export is loadable layout");
    }
    private static void Admission()
    {
        foreach(var change in new Action[]{()=>ArenaSession.IsActive=false,()=>ArenaSession.Exact=false,()=>_game.Status=GameStatus.Starting,
            ()=>_game.GameWorld=new(),()=>ArenaMatchService.TeamStartAbortPending=true,()=>ArenaAreaRuntime.IsPending=true,
            ()=>ArenaMode.Compat.PitFireTeamCompat.TeamArenaActive=true,()=>ArenaMatchService.Current.Mode=new TacticalMode(),
            ()=>ArenaMatchService.Current.Mode=new GunGameMode(),()=>ArenaMatchService.Current=null})
        {Fixture();change();Step();Check(ArenaPickupView.Created.Count==0&&ArenaPowerups.Grants.Count==0,"ineligible raid or mode creates no pickups");}
        Fixture();Dm.AssistScoringOpen=false;Step();Check(ArenaPickupView.Created.Count==0,"assembly and countdown have no pickups");
        Dm.AssistScoringOpen=true;Step();Check(ArenaPickupView.Created.Count==1,"live phase creates configured point");
        Fixture();Dm.IsComplete=true;Step();Check(ArenaPickupView.Created.Count==0,"completed match has no pickups");
        Fixture();ArenaMatchService.Current.Mode=new TeamDeathmatchMode();Step();Move(_local,0);Check(ArenaPowerups.Grants.Count==1,"independent TDM receives pickups");
        Ready();Check(ArenaPickupRuntime.IsLiveActor(_local),"exact local body admitted");
        foreach(var change in new Action[]{()=>_local.HealthController.IsAlive=false,()=>_local.IsYourPlayer=false,()=>_game.LocalPlayer=new(),
            ()=>_world.MainPlayer=new(),()=>_world.Alive["local"]=new(),()=>_world.Ever["local"]=new(),()=>_local.ProfileId="",()=>Dm.Round.Number++})
        {Ready();change();Check(!ArenaPickupRuntime.IsLiveActor(_local),"stale or dead local identity rejected before tick");}
        Ready();var bot=Bot("bot",-4);Check(ArenaPickupRuntime.IsLiveActor(bot),"exact active bot admitted");
        bot.AIData.BotOwner.BotState=EBotState.PreActive;Check(!ArenaPickupRuntime.IsLiveActor(bot),"preactive bot excluded");
        bot.AIData.BotOwner.BotState=EBotState.Active;bot.AIData.BotOwner.GetPlayer=new();Check(!ArenaPickupRuntime.IsLiveActor(bot),"bot owner wrapper must match physical body");
        Ready();Step();Singleton<GameWorld>.Instance=new();Check(!ArenaPickupRuntime.IsLiveActor(_local),"foreign world rejected immediately");
    }
    private static void Collection()
    {
        Ready();Move(_local,0);Check(ArenaPowerups.Grants.Count==1,"crossing consumes available powerup");
        Check(ArenaPowerups.Grants.Single().Seconds==20,"damage pickup still grants twenty seconds");
        Check(State().Phase==ArenaPickupPhase.Cooldown,"successful effect enters cooldown");Step();Check(ArenaPowerups.Grants.Count==1,"standing cannot consume twice");
        Step(10);Check(State().Phase==ArenaPickupPhase.Available&&ArenaPowerups.Grants.Count==1,"cooldown appearance requires leave before recollect");
        Move(_local,-3);Move(_local,0);Check(ArenaPowerups.Grants.Count==2,"reentering refreshed point collects once");
        Fixture();_local.Position=new(0,0,0);Step();Check(ArenaPowerups.Grants.Count==0,"fresh spawn inside point never collects");
        Step();Move(_local,-3);Move(_local,0);Check(ArenaPowerups.Grants.Count==1,"fresh body must leave and reenter");
        Ready();_world.Remove(_local);var replaced=new Player{ProfileId="local",IsYourPlayer=true,Position=new(0,0,0)};
        _world.Add(replaced);_world.MainPlayer=replaced;_game.LocalPlayer=replaced;_local=replaced;Step();Check(ArenaPowerups.Grants.Count==0,"same profile fresh respawn does not inherit approach");
        Ready();Move(_local,2);Check(ArenaPowerups.Grants.Count==1,"continuous high speed crossing is collected");
        Ready();Move(_local,3);Check(ArenaPowerups.Grants.Count==0,"runtime rejects teleport sweep");
        Ready();Move(_local,2,seconds:.5f);Check(ArenaPowerups.Grants.Count==0,"stale movement sample does not sweep");
        Ready();Move(_local,0,2);Check(ArenaPowerups.Grants.Count==0,"different floor cannot collect");
        Ready();Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),distance=.1f}];Move(_local,.2f,0,.5f);Check(ArenaPowerups.Grants.Count==0,"wall blocks pickup contact");
        Ready();Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),distance=.1f}];Move(_local,.2f);Check(ArenaPowerups.Grants.Count==0,"projected zero length ray cannot bypass current actor wall");
        Ready();var bot=Bot("nearest",-2);Step();_local.Position=new(.8f,0,0);bot.Position=new(.1f,0,0);Step();
        Check(ArenaPowerups.Grants.Count==1&&ArenaPowerups.Grants[0].Player==bot,"only closest eligible actor wins one appearance");
        Ready();bot=Bot("bot",0);Step();Check(ArenaPowerups.Grants.Count==0,"bot spawned on point must leave first");
        Move(bot,-3);Move(bot,0);Check(ArenaPowerups.Grants.Count==1,"bot entering powerup receives it");
        Ready();bot=Bot("bot",-3);ArenaPickupSettings.BotsCollectPowerups.Value=false;Step();Move(bot,0);Check(ArenaPowerups.Grants.Count==0,"bot powerups switch respected");
        foreach(var change in new Action[]{()=>_local.ActiveHealthController.DamageCoeff=0,()=>ArenaRespawnDriver.IsRespawnInProgress=true,
            ()=>WeaponSwapService.IsSwapping=true,()=>_local.InventoryController.IsChangingWeapon=true,()=>ArenaHudVisibility.IsBattleUiActive=false})
        {Ready();change();Move(_local,0);Check(ArenaPowerups.Grants.Count==0,"unsafe local collection deferred");}
        Ready();Time.timeScale=0;Move(_local,0);Check(ArenaPowerups.Grants.Count==0,"paused game cannot collect");
        Ready();ArenaPowerups.GrantResult=false;Move(_local,0);Check(State().Phase==ArenaPickupPhase.Available,"failed effect grant does not consume point");
        ArenaPowerups.GrantResult=true;Step();Check(ArenaPowerups.Grants.Count==0,"failed effect requires new approach");Move(_local,-3);Move(_local,0);Check(ArenaPowerups.Grants.Count==1,"failed effect may retry on reentry");
        Ready(ArenaPickupKind.Shield);Move(_local,0);Check(ArenaPowerups.Grants.Single().Kind=="shield","shield point dispatches shield grant");
        Check(ArenaPowerups.Grants.Single().Points==220&&ArenaPowerups.Grants.Single().Seconds==30,"shield pickup grants 220 points for thirty seconds");
        Ready(ArenaPickupKind.JumpBoots);Move(_local,0);Check(ArenaPowerups.Grants.Single().Kind=="boots"&&ArenaPowerups.Grants.Single().Seconds==30,"boots dispatch thirty-second jump grant");
        Ready(ArenaPickupKind.JumpBoots);bot=Bot("bot",-3);Step();Move(bot,0);Check(ArenaPowerups.Grants.Count==0&&State().Phase==ArenaPickupPhase.Available,"bots leave boots available");
        Ready(ArenaPickupKind.JumpBoots);ArenaMode.Movement.ArenaMovementScope.Enabled=false;Move(_local,0);Check(ArenaPowerups.Grants.Count==0,"disabled movement cannot consume boots");
        Ready(ArenaPickupKind.InfiniteAmmo);Move(_local,0);Check(ArenaPowerups.Grants.Single().Kind=="ammo"&&ArenaPowerups.Grants.Single().Seconds==10,"ammo dispatches ten-second grant");
        Ready(ArenaPickupKind.InfiniteAmmo);bot=Bot("bot",-3);Step();Move(bot,0);Check(ArenaPowerups.Grants.Count==0&&State().Phase==ArenaPickupPhase.Available,"bots leave ammo powerup available");
    }
    private static void Weapons()
    {
        Ready(ArenaPickupKind.Weapon);var weapon=ArenaWeaponPickup.Created.Single();weapon.StartResult=false;Move(_local,0);
        Check(weapon.Attempts==1&&State().Phase==ArenaPickupPhase.Available,"failed free slot leaves weapon available");Step();Check(weapon.Attempts==1,"failed free slot cannot spam while standing");
        Move(_local,-3);Check(weapon.Attempts==1,"leaving after failed take must not retry outward sweep");weapon.StartResult=true;Move(_local,0);Check(weapon.Attempts==2&&State().Phase==ArenaPickupPhase.Pending,"reentry starts one native transaction");
        weapon.Settle(true);Check(State().Phase==ArenaPickupPhase.Cooldown,"only confirmed weapon success starts cooldown");
        weapon.Callback(false);Check(State().Phase==ArenaPickupPhase.Cooldown,"duplicate false callback cannot retire confirmed success");
        Step(10);Check(ArenaWeaponPickup.Created.Count==2,"cooldown builds one replacement weapon");Step();var next=ArenaWeaponPickup.Created.Last();
        Move(_local,-3);Move(_local,0);Check(next.Attempts==1&&State().Phase==ArenaPickupPhase.Pending,"replacement has independent pending transaction");
        weapon.Callback(false);Check(State().Phase==ArenaPickupPhase.Pending&&!next.Disposed,"old callback cannot retire new appearance");next.Settle(true);
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();Move(_local,0);Step(10);
        Check(State().Phase==ArenaPickupPhase.Retired&&weapon.Disposed,"native transfer timeout retires and disposes source");weapon.Settle(true);
        Check(State().Phase==ArenaPickupPhase.Retired,"late timeout success cannot rearm retired point");
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();Move(_local,0);weapon.Settle(false,true);
        Check(State().Phase==ArenaPickupPhase.Retired&&weapon.Disposed,"ambiguous native ownership retires appearance");
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();Move(_local,0);var old=weapon.Callback;Dm.Round.Number++;Step();Step();var newRound=ArenaWeaponPickup.Created.Last();
        old(false);Check(!newRound.Disposed&&State().Phase==ArenaPickupPhase.Available,"old round callback cannot touch new source");
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();Move(_local,0);ArenaSession.Exact=false;
        Check(!weapon.Current(),"native transaction current callback rejects lost exact raid before tick");
        Step();weapon.Settle(true);Check(ArenaPickupView.Created.All(v=>v.Disposed),"lost raid disposes views even with late callback");
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();var bot=Bot("bot",-2);Step();Move(bot,0);Check(weapon.Attempts==0,"bots never auto-take weapon");
        Fixture(ArenaPickupKind.Weapon);KitCatalog.Kits=[];Step();Check(State().Phase==ArenaPickupPhase.Retired&&ArenaWeaponPickup.Created.Count==0,"missing kit disables just weapon point");
        Fixture();System.IO.File.Delete(LayoutPath);KitCatalog.Kits=[];ArenaPickupSettings.WeaponKit.Value=ArenaPickupWeapons.M60Id;Step();Step();
        Check(ArenaWeaponPickup.Created.Single().Kit.Id==ArenaPickupWeapons.M60Id&&KitCatalog.Kits.Count==0,"runtime prepares built-in M60 without normal catalog insertion");
        Fixture(ArenaPickupKind.Weapon);ArenaWeaponPickup.PrepareNull=true;Step();Check(State().Phase==ArenaPickupPhase.Retired,"preparation failure retires weapon source");
        Fixture(ArenaPickupKind.Weapon);KitBundlePreloader.IsPreloading=true;Step();Check(ArenaWeaponPickup.Created.Count==0,"optional weapon waits for required preload");
        KitBundlePreloader.IsPreloading=false;Step();Check(ArenaWeaponPickup.Created.Count==1,"optional weapon resumes after required preload");
        Ready(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();weapon.ImmediateResult=true;Move(_local,0);
        Check(State().Phase==ArenaPickupPhase.Cooldown&&weapon.Attempts==1,"synchronous success callback is consumed exactly once");
    }
    private static void LifecycleAndGeometry()
    {
        Fixture();_local.MovementContext.IsGrounded=false;Step();Check(ArenaPickupView.Created.Count==0,"airborne live start waits for grounded navigation anchor");
        Step(4);_local.MovementContext.IsGrounded=true;Step(.3f);Check(ArenaPickupView.Created.Count==1,"bounded anchor retry recovers after landing");
        Fixture();_local.MovementContext.IsGrounded=false;Step();Step(16);_local.MovementContext.IsGrounded=true;Step();
        Check(ArenaPickupView.Created.Count==0&&Plugin.ModLogger.Lines.Count(x=>x.Contains("15 seconds"))==1,"anchor timeout skips round without repeated warning");
        Dm.Round.Number++;Step();Check(ArenaPickupView.Created.Count==1,"new round can retry previously unavailable anchor");
        Ready();Move(_local,0);int resets=ArenaPowerups.Resets,gen=ArenaPickupRuntime.Generation;Dm.AssistScoringOpen=false;Step();
        Check(ArenaPickupView.Created.All(v=>v.Disposed)&&ArenaPowerups.Resets>resets&&ArenaPickupRuntime.Generation!=gen,"intermission clears scene effects and generation");
        Dm.Round.Number++;Dm.AssistScoringOpen=true;Step();Check(ArenaPickupView.Created.Count==2&&ArenaPowerups.Grants.Count==1,"next round new point cannot award spawn occupant");
        Ready();ArenaPickupSettings.Enabled.Value=false;Step();Check(ArenaPickupView.Created.All(v=>v.Disposed),"disable destroys current round pickups");
        ArenaPickupSettings.Enabled.Value=true;Step();Check(ArenaPickupView.Created.Count==1&&!ArenaPickupRuntime.IsLiveActor(_local),"disable latch prevents same match recreation");
        ArenaMatchService.Current=new(){Mode=new RoundsMode()};Step();Check(ArenaPickupView.Created.Count==2,"new match clears disable latch");
        Ready();ArenaPickupView.ThrowOnTick=true;Step();ArenaPickupView.ThrowOnTick=false;Step();Check(ArenaPickupView.Created.Count==1&&ArenaPickupView.Created[0].Disposed,"runtime fault is latched for current match");
        Ready();System.IO.File.WriteAllText(LayoutPath,"malformed");Dm.Round.Number++;Step();Check(ArenaPickupView.Created.Count==2,"layout frozen across rounds despite disk edits");
        Fixture();System.IO.File.WriteAllText(LayoutPath,"malformed");Step();Step();Check(ArenaPickupView.Created.Count==0&&Plugin.ModLogger.Lines.Count(x=>x.Contains("stopped for this match"))==1,"invalid config fails closed once per match");
        Ready();Check(ArenaPickupGeometry.TryAnchor(_world,out _),"exact grounded local is valid anchor");
        NavMesh.Sample=p=>p+Vector3.up;Check(!ArenaPickupGeometry.TryAnchor(_world,out _),"anchor cannot snap between floors");NavMesh.Sample=p=>p;
        Check(ArenaPickupGeometry.TryPlace(new(0,0,0),_local.Position,out _,out _),"clear native connected floor admitted");
        NavMesh.SampleOk=false;Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"no nav sample excludes point");NavMesh.SampleOk=true;
        NavMesh.Sample=p=>p+Vector3.up;Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"pickup nav cannot snap to another floor");NavMesh.Sample=p=>p;
        Physics.Rays=(a,b,c)=>[];Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"missing support floor excludes point");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),point=new(0,0,0),normal=new(1,0,0)}];Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"steep support excluded");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),point=new(0,0,0),normal=Vector3.up}];Physics.Overlaps=[new Collider()];Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"obstructed pedestal excluded");
        Physics.Overlaps=[];NavMesh.Status=NavMeshPathStatus.PathPartial;Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"partial native path excluded");NavMesh.Status=NavMeshPathStatus.PathComplete;
        ArenaAreaRuntime.CurrentArea=new(){Id="zone"};ArenaAreaRuntime.CurrentArea.Bounds.PointAllowed=false;Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"outside contour point excluded");
        ArenaAreaRuntime.CurrentArea.Bounds.PointAllowed=true;ArenaAreaRuntime.CurrentArea.Bounds.PathAllowed=false;Check(!ArenaPickupGeometry.TryPlace(new(),_local.Position,out _,out _),"native path leaving contour excluded");
        Ready(ArenaPickupKind.Weapon);var handle=ArenaWeaponPickup.Created.Single();var own=new Collider();own.transform.parent=handle.Visual.transform;
        Check(ArenaPickupRuntime.OwnsCollider(own),"owned loot child collider recognized");
        Physics.Rays=(a,b,c)=>[new(){collider=own},new(){collider=new Collider{Owner=_local}}];
        Check(ArenaPickupGeometry.LineClear(new(0,0,0),new(1,0,0)),"self visual and actors do not block contact ray");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider()}];Check(!ArenaPickupGeometry.LineClear(new(),new(1,0,0)),"world wall remains solid to contact ray");
    }
    private static void NearbyPlacement()
    {
        var origin=new Vector3(0,0,0);var anchor=new Vector3(-3,0,0);
        Fixture();
        Check(ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out var floor,out _,out var moved)&&!moved&&NavMesh.Samples==1,"safe original point needs no relocation search");
        Physics.Capsule=(a,b,r)=>a.x*a.x+a.z*a.z<.16f?[new Collider()]:[];
        NavMesh.Samples=0;
        Check(ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out floor,out var reason,out moved)&&moved&&floor.x==.5f&&floor.z==0,"obstructed prototype finds deterministic nearby clear floor");
        Check(reason=="pedestal clearance obstructed"&&NavMesh.Samples==2,"relocation retains original reason and stops at first safe floor");
        Check(ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out var repeat,out _,out _)&&(floor-repeat).sqrMagnitude==0,"identical scene produces identical fallback");
        NavMesh.Samples=0;
        Check(!ArenaPickupGeometry.TryPlacePoint(origin,anchor,false,out _,out _,out moved)&&!moved&&NavMesh.Samples==1,"custom positions never try nearby alternatives");
        Step();Check(ArenaPickupView.Created.Count==0,"runtime custom layout remains exact when obstructed");

        foreach(var fail in new Action[]{()=>Physics.Overlaps=[new Collider()],()=>NavMesh.SampleOk=false,
            ()=>Physics.Rays=(a,b,c)=>[],()=>Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),point=a-Vector3.up*.65f,normal=new(1,0,0)}],
            ()=>NavMesh.Status=NavMeshPathStatus.PathPartial,()=>NavMesh.PathOk=false,
            ()=>ArenaAreaRuntime.CurrentArea=new(){Bounds=new(){PointAllowed=false}},
            ()=>ArenaAreaRuntime.CurrentArea=new(){Bounds=new(){PathAllowed=false}}})
        {
            Fixture();fail();
            Check(!ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out _,out reason,out moved)&&!moved,"unsafe alternatives keep every original geometry guard");
            Check(NavMesh.Samples==33&&reason.Contains("32 candidates"),"failed search is bounded and explains exhaustion");
        }
        Fixture();int samples=0;
        NavMesh.Sample=p=>++samples==1?origin:new Vector3(2.1f,0,p.z);
        Physics.Capsule=(a,b,r)=>a.x==0?[new Collider()]:[];
        Check(!ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out _,out _,out _),"final navigation snap outside two metre radius rejected");
        Fixture();samples=0;
        NavMesh.Sample=p=>++samples==1?origin:p+Vector3.up*.4f;
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),point=a-Vector3.up*.45f,normal=Vector3.up}];
        Physics.Capsule=(a,b,r)=>a.x==0&&a.z==0?[new Collider()]:[];
        Check(!ArenaPickupGeometry.TryPlacePoint(origin,anchor,true,out _,out _,out _),"combined floor and nav height drift cannot change level");

        Fixture();System.IO.File.Delete(LayoutPath);
        var definition=ArenaPickupLayouts.Factory("spartan").Layouts[0].Points[0];
        var originals=ArenaPickupLayouts.Candidates(definition,new Random(1));
        Physics.Capsule=(a,b,r)=>originals.Any(p=>(a.x-p.X)*(a.x-p.X)+(a.z-p.Z)*(a.z-p.Z)<.16f)?[new Collider()]:[];
        Step();var damage=ArenaPickupView.Created.Single(v=>v.Point.Kind==ArenaPickupKind.DoubleDamage);
        Check(ArenaPickupView.Created.Count==6&&damage.Floor.x>damage.Point.X,"built-in runtime opts into safe fallback");
        Check(originals.Any(p=>p.X==damage.Point.X&&p.Y==damage.Point.Y&&p.Z==damage.Point.Z),"relocation never mutates layout definitions");
        Check(Plugin.ModLogger.Lines.Any(s=>s.Contains("factory-damage' relocated")&&s.Contains("pedestal clearance obstructed")),"relocation logs requested and chosen position plus original obstruction");
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(FileFor()));Dm.Round.Number++;Step();
        Check(ArenaPickupView.Created.Count==12,"built-in fallback choice remains frozen across rounds");

        Fixture();var overlap=FileFor();var second=Point(ArenaPickupKind.Shield);second.Id="two";overlap.Layouts[0].Points.Add(second);
        second.X=2.5f;
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(overlap));Step();
        Check(ArenaPickupView.Created.Count==2,"legacy fixed custom points keep authored spacing");
        Fixture();second.X=0;second.Alternatives=[new(){X=1}];
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(overlap));Step();
        Check(ArenaPickupView.Created.Count==1,"final pickup floors cannot overlap within three metres");
        Fixture();second.Alternatives=[new(){X=10}];
        overlap.Layouts[0].Points.Reverse();
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(overlap));Step();
        Check(ArenaPickupView.Created.Count==2&&ArenaPickupView.Created.Any(v=>v.Floor.x==10),"available alternative avoids occupied floor without moving exact custom point");
        Check(ArenaPickupView.Created[0].Point.Id!="two","fixed custom points reserve their place before earlier rotating points");
        var fixedPositions=ArenaPickupView.Created.Select(v=>v.Floor.x).ToArray();Step(80);
        Check(ArenaPickupView.Created.Select(v=>v.Floor.x).SequenceEqual(fixedPositions),"placement stays fixed during a live round");

        Fixture();Check(ArenaPickupGeometry.BeamLength(origin)==4,"open sky beacon height capped at four metres");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),distance=3},new(){collider=new Collider(),distance=1}];
        Check(Math.Abs(ArenaPickupGeometry.BeamLength(origin)-.9f)<.001,"nearest ceiling clips beam with clearance");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider{Owner=_local},distance=.2f}];
        Check(ArenaPickupGeometry.BeamLength(origin)==4,"actor overhead does not permanently shorten beam");
        Physics.Rays=(a,b,c)=>[new(){collider=new Collider(),distance=.05f}];
        Check(ArenaPickupGeometry.BeamLength(origin)==0,"very low ceiling yields no negative beam");
    }
    private static void NativeCandidates(string root)
    {
        var file=ArenaPickupLayouts.Factory("spartan");
        foreach(var layout in file.Layouts)
        {
            var db=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Path.Combine(root,"SPT_Runtime/SPT_Data/database/locations",layout.MapId,"base.json")));
            var bots=db["SpawnPointParams"].Where(p=>p["Categories"].Count()==1&&(string)p["Categories"][0]=="Bot").ToList();
            var candidates=layout.Points.SelectMany(p=>ArenaPickupLayouts.Candidates(p,new Random(1))).GroupBy(p=>(p.X,p.Y,p.Z)).Select(g=>g.First());
            foreach(var p in candidates) Check(bots.Any(b=>Math.Abs((float)b["Position"]["x"]-p.X)<.0001f&&Math.Abs((float)b["Position"]["y"]-p.Y)<.0001f&&Math.Abs((float)b["Position"]["z"]-p.Z)<.0001f),"Factory candidate matches installed native Bot-only marker");
        }
    }
    private static void PlacementRetries()
    {
        Fixture();NavMesh.AllowPath=(from,to)=>from.x < -5;Step();
        Check(ArenaPickupView.Created.Count==0&&NavMesh.Paths==1,"unreachable live anchor leaves placement pending");
        int paths=NavMesh.Paths;
        Move(_local,-8,seconds:.2f);Step(.2f);
        Check(NavMesh.Paths==paths,"full placement retries are throttled to one second");
        Step(.7f);
        Check(ArenaPickupView.Created.Count==1&&NavMesh.LastPathStart.x==-8,"failed placement recovers using fresh moved anchor");
        Check(Plugin.ModLogger.Lines.Any(s=>s.Contains("attempt 1:")&&s.Contains("anchor=(-3.000")&&s.Contains("geometryProbes=1")&&s.Contains("no complete native path from the local actor=1")),"attempt diagnostics include anchor and actual refusal counts");
        Step(2);Check(Plugin.ModLogger.Lines.Count(s=>s.Contains("points placed"))==1,"successful retry reports completion only once");

        Fixture(ArenaPickupKind.Weapon);var mixed=FileFor(Point(ArenaPickupKind.Weapon));
        var later=Point();later.Id="later";later.X=10;mixed.Layouts[0].Points.Add(later);
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(mixed));
        NavMesh.AllowPath=(from,to)=>to.x<5;Step();Step();
        var view=ArenaPickupView.Created.Single();var state=State();var weapon=ArenaWeaponPickup.Created.Single();
        Move(_local,0);weapon.Settle(true);float cooldown=state.ReadyAt;
        Step(1.1f);
        Check(ArenaPickupView.Created.Count==1&&ReferenceEquals(State(),state)&&state.Phase==ArenaPickupPhase.Cooldown
            &&state.ReadyAt==cooldown&&!view.Disposed&&ArenaWeaponPickup.Created.Count==1,"partial retries preserve successful entry state and weapon load");
        NavMesh.AllowPath=null;Move(_local,10,seconds:1.1f);
        Check(ArenaPickupView.Created.Count==2&&ReferenceEquals(State(),state)&&state.ReadyAt==cooldown,"partial recovery adds only the missing point");
        Check(ArenaPowerups.Grants.Count==0,"delayed point under actor requires leave and reenter");
        Move(_local,12);Move(_local,10);Check(ArenaPowerups.Grants.Count==1,"delayed point works after deliberate reentry");

        Fixture();var lateFixed=FileFor();var rotating=Point(ArenaPickupKind.Shield);rotating.Id="rotating";rotating.X=1;
        rotating.Alternatives=[new(){X=1.5f}];lateFixed.Layouts[0].Points.Add(rotating);
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(lateFixed));
        NavMesh.AllowPath=(from,to)=>to.x!=0;Step();var early=ArenaPickupView.Created.Single();
        NavMesh.AllowPath=null;Step(1.1f);
        Check(ArenaPickupView.Created.Count==1&&early.Point.Id=="rotating"&&!early.Disposed,"late fixed point cannot overlap an already placed rotating point");

        Fixture();NavMesh.PathOk=false;Step();
        for(int i=0;i<14;i++)Step(1);
        int attempts=Plugin.ModLogger.Lines.Count(s=>s.Contains("placement attempt "));
        paths=NavMesh.Paths;Step(2);NavMesh.PathOk=true;Step(2);
        Check(attempts==15&&NavMesh.Paths==paths&&ArenaPickupView.Created.Count==0,"unreachable placement has bounded attempts and cannot revive after deadline");
        Check(Plugin.ModLogger.Lines.Count(s=>s.Contains("skipped after"))==1&&Plugin.ModLogger.Lines.Count(s=>s.Contains("points placed"))==1,"exhausted placement reports final refusal once");
        Check(!Plugin.ModLogger.Lines.Any(s=>s.Contains("navigation anchor within")),"failed paths are not misreported as missing anchor");

        Fixture();_local.MovementContext.IsGrounded=false;Step();_local.MovementContext.IsGrounded=true;Step(16);
        Check(NavMesh.Paths==0&&ArenaPickupView.Created.Count==0,"anchor first available after deadline cannot place pickups");
        Fixture();NavMesh.PathOk=false;Step();paths=NavMesh.Paths;Time.timeScale=0;Step(1);
        Check(NavMesh.Paths==paths,"paused game never performs new placement probes");
        Time.timeScale=1;NavMesh.PathOk=true;Step(.1f);Check(ArenaPickupView.Created.Count==1,"unpaused retry uses remaining gameplay window");
        Fixture();ArenaRespawnDriver.IsRespawnInProgress=true;Step();Check(NavMesh.Paths==0,"replacement in progress postpones placement");
        ArenaRespawnDriver.IsRespawnInProgress=false;WeaponSwapService.IsSwapping=true;Step(.3f);Check(NavMesh.Paths==0,"weapon swap postpones placement");
        WeaponSwapService.IsSwapping=false;Step(.3f);Check(ArenaPickupView.Created.Count==1,"ready body resumes pending placement");

        foreach(string change in new[]{"phase","disable","inactive","world","match","stopping","round"})
        {
            Fixture();NavMesh.PathOk=false;Step();NavMesh.PathOk=true;
            switch(change)
            {
                case "phase":Dm.AssistScoringOpen=false;break;
                case "disable":ArenaPickupSettings.Enabled.Value=false;break;
                case "inactive":ArenaSession.IsActive=false;break;
                case "world":Singleton<GameWorld>.Instance=new();break;
                case "match":ArenaMatchService.Current=null;break;
                case "stopping":_game.Status=GameStatus.Stopping;break;
                case "round":Dm.Round.Number++;Dm.AssistScoringOpen=false;break;
            }
            Step(2);Check(ArenaPickupView.Created.Count==0,"pending placement canceled by "+change);
        }
        Fixture();NavMesh.PathOk=false;Step();Step(16);Dm.AssistScoringOpen=false;Step();Dm.Round.Number++;
        Dm.AssistScoringOpen=true;NavMesh.PathOk=true;Step();
        Check(ArenaPickupView.Created.Count==1&&Plugin.ModLogger.Lines.Any(s=>s.Contains("attempt 1: round 2")),"new round receives fresh retry window and attempt counter");

        Fixture();var diagnostics=new ArenaPickupGeometry.PlacementDiagnostics();
        Physics.Capsule=(p,b,r)=>p.x==0&&p.z==0?[new Collider()]:[];NavMesh.PathOk=false;
        Check(!ArenaPickupGeometry.TryPlacePoint(new(),_local.Position,true,out _,out _,out _,diagnostics),"diagnostic probes retain unsafe placement rejection");
        Check(diagnostics.Probes==33&&diagnostics.ToString().Contains("pedestal clearance obstructed=1")
            &&diagnostics.ToString().Contains("no complete native path from the local actor=32"),"geometry diagnostics count every actual fallback failure not just the last candidate");
    }

    public static int Main(string[] args)
    {
        try {
            Policy();Layouts();Admission();Collection();Weapons();LifecycleAndGeometry();NearbyPlacement();PlacementRetries();
#if PICKUP_OBJECTIVES
            ObjectiveCases();
#endif
            if(args.Length==1)NativeCandidates(args[0]);Console.WriteLine($"ARENA_PICKUP_BEHAVIOR_PASSED: {_checks} assertions; source-linked runtime/state/layout/geometry; scratch {Scratch}");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally {ArenaMatchService.Current=null;ArenaPickupRuntime.Tick();}
    }
}
