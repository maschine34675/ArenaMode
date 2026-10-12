using ArenaMode.Combat.Fire;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using UnityEngine;
using System.Reflection;

internal static class Program
{
    private static int assertions;
    private static void Check(bool yes,string name) { assertions++;if(!yes)throw new Exception("FAILED: "+name); }
    private static void Near(float a,float b,string name)=>Check(Math.Abs(a-b)<.002f,name);
    internal static bool Explode(Grenade grenade)=>(bool)typeof(ArenaFireRuntime).GetMethod("Explosion",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{grenade});
    private static void Bind(Player.BaseGrenadeHandsController hands,Grenade grenade)=>typeof(ArenaFireRuntime).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{hands,grenade});
    private static Grenade Throw(Player owner,string tpl=ArenaMolotovItem.TemplateId)
    {
        var hands=new Player.BaseGrenadeHandsController{_player=owner,Item=new(){TemplateId=tpl}}; owner.HandsController=hands;
        var g=new Grenade {WeaponSource=hands.Item,Player=new Bridge{iPlayer=owner}};Bind(hands,g);return g;
    }
    private static void Main()
    {
        var config=new BepInEx.Configuration.ConfigFile();ArenaFireRuntime.Enable(config);
        Singleton<GlobalConfiguration>.Instance=new();
        var world=new GameWorld();Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world};
        var local=new Player{ProfileId="local",IsYourPlayer=true,Position=new(20,0,20)};
        var bot=new Player{ProfileId="bot",IsAI=true};world.MainPlayer=local;world.AllAlivePlayersList.AddRange(new[]{local,bot});
        var mode=new RoundsMode();ArenaMatchService.Current=new(){Mode=mode};
        Check(ArenaFireRuntime.Enabled.Value,"dedicated item enabled by default");
        Check(config.Bound.SequenceEqual(new[]{"Arena incendiary/Enabled"}),"legacy prototype config unused");
        ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.BotEscapeAvailable=true;ArenaFireRuntime.Tick();
        Check(Explode(Throw(local,ArenaMolotovItem.DonorTemplateId)),"ordinary donor untouched");
        Check(!Explode(Throw(local))&&!ArenaFireRuntime.HasFire,"ordinary custom cannot explode");
        ArenaSession.IsActive=true;ArenaFireRuntime.Tick();
        Check(Explode(Throw(local,ArenaMolotovItem.DonorTemplateId))&&!ArenaFireRuntime.HasFire,"arena donor untouched");
        Check(Explode(Throw(bot,ArenaMolotovItem.DonorTemplateId)),"bot donor untouched");
        Check(!Explode(Throw(bot))&&!ArenaFireRuntime.HasFire,"foreign custom cannot explode");
        Check(Explode(Throw(local,"other")),"other template untouched");
        ArenaFireRuntime.BotEscapeAvailable=false;Check(!Explode(Throw(local))&&!ArenaFireRuntime.HasFire,"companion required");ArenaFireRuntime.BotEscapeAvailable=true;
        ArenaFireRuntime.Enabled.Value=false;ArenaFireRuntime.Tick();Check(!Explode(Throw(local))&&!ArenaFireRuntime.HasFire,"disabled custom cannot explode");
        ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.Tick();
        Check(!ArenaFireRuntime.OwnsDamage(bot,local,EDamageType.Flame),"unowned flame rejected");
        var first=Throw(local);Check(!Explode(first),"owned blast suppressed");Check(ArenaFireView.Alive==1,"fire visual created");
        ArenaFireRuntime.Tick();Near(bot.Taken,25,"normalized first damage tick");Check(bot.Scoped&&ReferenceEquals(bot.LastSource,local),"attributed exact fire scope");
        Check(!ArenaFireRuntime.OwnsDamage(bot,local,EDamageType.Flame),"scope restored");
        Time.time=.1f;Check(!Explode(Throw(local)),"second owned blast suppressed");ArenaFireRuntime.Tick();Near(bot.Taken,25,"staggered fire cannot add off-cycle damage");
        Time.time=.26f;ArenaFireRuntime.Tick();Near(bot.Taken,50,"overlapping fire does not stack");
        Time.time=2;ArenaFireRuntime.Tick();Near(bot.Taken,75,"hitch does not burst catch up");
        Check(ArenaFireRuntime.HoldsBody(local,world),"exact corpse owner held");
        local.HealthController.IsAlive=false;world.AllAlivePlayersList.Remove(local);
        var replacement=new Player{ProfileId="local",IsYourPlayer=true,Position=new(20,0,20)};world.MainPlayer=replacement;world.AllAlivePlayersList.Add(replacement);
        Time.time=2.3f;ArenaFireRuntime.Tick();Check(ReferenceEquals(bot.LastSource,local),"posthumous source not rebound");
        Check(!ArenaFireRuntime.HoldsBody(replacement,world),"successor not owner");
        var airborne=Throw(replacement);ArenaFireRuntime.Enabled.Value=false;ArenaFireRuntime.Tick();
        Check(airborne.Retired&&airborne.TimerStopped,"disable retires pending native projectile");Check(ArenaFireView.Alive==0,"disable clears visuals");
        Check(((HashSet<Player>)typeof(ArenaFireRuntime).GetField("Ticked",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Count==0,"disable clears transient victim references");
        float before=bot.Taken;Time.time=3;ArenaFireRuntime.Tick();Near(bot.Taken,before,"disable clears damage");
        ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.Tick();Check(!Explode(Throw(replacement)),"new throw after enable");
        mode.AssistScoringOpen=false;ArenaFireRuntime.Tick();Check(ArenaFireView.Alive==0,"intermission extinguishes");Check(!Explode(Throw(replacement))&&!ArenaFireRuntime.HasFire,"intermission custom harmless");
        mode.AssistScoringOpen=true;ArenaFireRuntime.Tick();Check(!Explode(Throw(replacement)),"next round fire");
        Time.time=10;ArenaFireRuntime.Tick();Check(ArenaFireView.Alive==0,"six second expiry");
        var cells=ArenaFireGeometry.Build(Vector3.zero);Check(cells.Count>20,"flat floor spread");
        Check(!ArenaFireGeometry.Contains(cells,new(0,3,0)),"upper floor safe");Check(!ArenaFireGeometry.Contains(cells,new(0,-3,0)),"lower floor safe");
        Physics.WallX=.5f;cells=ArenaFireGeometry.Build(Vector3.zero);Check(cells.All(p=>p.x<.5f),"spread cannot cross wall");
        Check(!ArenaFireGeometry.Contains(cells,new(.65f,0,0)),"cell edge cannot damage through wall");
        Physics.WallX=null;Physics.Saturated=true;Check(ArenaFireGeometry.Build(Vector3.zero).Count==0,"saturated geometry fails closed");Physics.Saturated=false;
        Check(ArenaFireGeometry.Build(new(0,5,0)).Count==0,"airburst cannot project to distant floor");
        Check(!ArenaFirePolicy.Contact(0,float.NaN,0),"invalid height rejected");
        Near(ArenaFirePolicy.PartDamage(20,85,440,.25f)*.25f,20*85/440f,"environment normalization");
        Near(ArenaFirePolicy.PartDamage(20,85,440,0),0,"zero environment immunity preserved");
        Near(ArenaFirePolicy.TickDamage(-1),0,"negative time no damage");
        // The real fire layer must roll over an action's immutable lease, not
        // continue an old ActionData after IsActive selected a replacement.
        Check(!Explode(Throw(replacement)),"bot test fire");
        var owner=new BotOwner{GetPlayer=bot};bot.AIData=new(){BotOwner=owner};
        var layer=new ArenaMode.BigBrainCompat.ArenaFireEscapeLayer(owner,79);
        Check(layer.GetName().Contains("Combat"),"PFT recognizes combat layer");
        Check(layer.IsActive(),"burning bot acquires escape");layer.CurrentAction=layer.GetNextAction();
        Check(!layer.IsCurrentActionEnding(),"current escape action valid");
        var oldLease=((ArenaMode.BigBrainCompat.FireAction)layer.CurrentAction.Data).Lease;oldLease.Released=true;
        Time.time+=.2f;Check(layer.IsActive(),"replacement escape lease acquired");
        Check(layer.IsCurrentActionEnding(),"new lease terminates stale action data");
        owner.DoorOpener.Interacting=true;Check(!ArenaMode.BigBrainCompat.ArenaFireEscape.Eligible(owner),"native door ownership preserved");owner.DoorOpener.Interacting=false;
        ArenaMode.Movement.ArenaBotMovementSain.Safe=false;Check(!ArenaMode.BigBrainCompat.ArenaFireEscape.Eligible(owner),"SAIN door ownership preserved");ArenaMode.Movement.ArenaBotMovementSain.Safe=true;
        Check(!ArenaMode.BigBrainCompat.ArenaFireEscape.RouteAllowed(new(4,0,0),new[]{new Vector3(-4,0,0)}),"route cannot cross fire from outside");
        UnityEngine.AI.NavMesh.Allow=p=>p.x< -3&&p.z< -1;int cursor=0;bool found=false;
        for(int retry=0;retry<8&&!found;retry++)
        {
            int calls=UnityEngine.AI.NavMesh.Calls;
            found=ArenaMode.BigBrainCompat.ArenaFireEscape.TryCreate(owner,ref cursor,out _);
            Check(UnityEngine.AI.NavMesh.Calls-calls<=8,"bounded native path budget");
        }
        Check(found,"rotating search eventually finds southwest exit");UnityEngine.AI.NavMesh.Allow=p=>true;
        var cancelled=Throw(replacement);cancelled.RetireFailures=3;mode.Round.Number++;Time.time+=9;
        ArenaFireRuntime.Tick();Check(!cancelled.Retired,"repeated native teardown failure retained");
        ArenaFireRuntime.Tick();Check(cancelled.Retired,"failed teardown retried natively");
        Check(ArenaFireView.Alive==0,"cancelled projectile cannot ignite in new round");
        var nativeFailure=Throw(replacement);nativeFailure.RetireFailures=1;
        try {nativeFailure.InvokeBlowUpEvent();}catch(InvalidOperationException){}
        int burning=ArenaFireView.Alive;Check(!Explode(nativeFailure),"failed native explosion retains suppression");
        Check(ArenaFireView.Alive==burning,"cancelled projectile cannot ignite in new round");
        ArenaFireRuntime.Tick();Check(nativeFailure.Retired,"failed native destruction retried");
        ArenaFireRuntime.Reset();
        Time.time=100;ArenaSession.IsActive=false;ArenaFireRuntime.Tick();
        Check(typeof(ArenaFireRuntime).GetField("_world",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)==null,
            "raid world released after last quiet period");
        DamageDelegation();
        ViewIsolation();
        LifetimeBoundaries();
        Console.WriteLine($"ARENA_MOLOTOV_PRODUCTION_PASSED: {assertions} assertions");
    }

    private static (Player local,Player bot,GameWorld world) Fresh(IArenaMode mode)
    {
        ArenaSession.IsActive=false;ArenaFireRuntime.Reset();Time.time+=20;
        ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.BotEscapeAvailable=true;
        ArenaSession.FriendlyFireEnabledSelection=false;
        var local=new Player{ProfileId="local",IsYourPlayer=true,Position=new(20,0,20)};
        var bot=new Player{ProfileId="bot",IsAI=true};
        var world=new GameWorld{MainPlayer=local};world.AllAlivePlayersList.AddRange(new[]{local,bot});
        Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world};
        ArenaMatchService.Current=new(){Mode=mode};ArenaSession.IsActive=true;ArenaFireRuntime.Tick();
        return(local,bot,world);
    }

    private static void DamageDelegation()
    {
        // Real common FF policy, with explicit membership/protection doubles at
        // the native damage boundary. This is not a native kill/assist proof.
        var tactical=new TacticalMode();var (local,ally,world)=Fresh(tactical);
        tactical.FriendlyDamageRule=(v,a)=>ReferenceEquals(v,ally)&&ReferenceEquals(a,local);
        var grenade=Throw(local);Check(!Explode(grenade),"tactical custom blast suppressed");ArenaFireRuntime.Tick();
        Check(ally.AdmissionCalls==7&&ally.Taken==0,"friendly damage reaches common mutator");
        ArenaSession.FriendlyFireEnabledSelection=true;Time.time+=.26f;ArenaFireRuntime.Tick();
        Near(ally.Taken,25,"tactical friendly fire enabled damages ally");
        ArenaSession.FriendlyFireEnabledSelection=false;local.Position=Vector3.zero;Time.time+=.26f;ArenaFireRuntime.Tick();
        Near(local.Taken,25,"self damage remains native with friendly fire off");
        local.HealthController.IsAlive=false;world.AllAlivePlayersList.Remove(local);
        var successor=new Player{ProfileId=local.ProfileId,IsYourPlayer=true,Protected=true};world.MainPlayer=successor;world.AllAlivePlayersList.Add(successor);
        Time.time+=.26f;ArenaFireRuntime.Tick();Check(successor.AdmissionCalls==7&&successor.Taken==0,"respawn protection receives old fire");
        successor.Protected=false;Time.time+=.26f;ArenaFireRuntime.Tick();Near(successor.Taken,25,"respawn body has no bespoke fire immunity");
        Check(ReferenceEquals(successor.LastSource,local)&&ReferenceEquals(successor.LastWeapon,grenade.WeaponSource),"old fire preserves physical source and weapon");
        Check(successor.LastSourceId==ArenaMolotovItem.TemplateId&&successor.LastDamageType==EDamageType.Flame,"native damage carries dedicated flame identity");

        var tdm=Fresh(new TeamDeathmatchMode());
        ArenaMode.Bots.TeamDeathmatchRelations.FriendlyDamageRule=(v,a)=>ReferenceEquals(v,tdm.bot)&&ReferenceEquals(a,tdm.local);
        Explode(Throw(tdm.local));ArenaFireRuntime.Tick();Check(tdm.bot.AdmissionCalls==7&&tdm.bot.Taken==0,"TDM common friendly-fire delegation");
        ArenaSession.FriendlyFireEnabledSelection=true;Time.time+=.26f;ArenaFireRuntime.Tick();Near(tdm.bot.Taken,25,"TDM friendly fire enabled damages ally");
        ArenaMode.Bots.TeamDeathmatchRelations.FriendlyDamageRule=(v,a)=>false;

        var lethal=Fresh(new RoundsMode());lethal.bot.ActiveHealthController.StandardProfile();
        var head=lethal.bot.ActiveHealthController.Parts[EBodyPart.Head];head.Current=.1f;lethal.bot.ActiveHealthController.Parts[EBodyPart.Head]=head;
        bool deathScoped=false;lethal.bot.OnDeath=info=>deathScoped=ArenaFireRuntime.OwnsDamage(lethal.bot,info.Player?.iPlayer,info.DamageType)&&ReferenceEquals(info.Player?.iPlayer,lethal.local);
        var killing=Throw(lethal.local);Explode(killing);ArenaFireRuntime.Tick();
        Check(deathScoped&&lethal.bot.Deaths==1,"lethal native callback retains exact scoped owner");
        Near(lethal.bot.Taken,.1f,"damage after lethal head callback stops");
        Check(lethal.bot.AdmissionCalls==1,"dead actor receives no later part dispatch");
        Check(!ArenaFireRuntime.OwnsDamage(lethal.bot,lethal.local,EDamageType.Flame),"lethal scope restored");
        Check(!ArenaFireRuntime.OwnsDamage(lethal.bot,lethal.local,EDamageType.Bullet),"scope cannot claim unrelated bullet");
    }

    private static void ViewIsolation()
    {
        var test=Fresh(new RoundsMode());ArenaFireView.ConstructorFailures=1;
        Explode(Throw(test.local));ArenaFireRuntime.Tick();Near(test.bot.Taken,25,"view constructor failure preserves damage");
        Check(ArenaFireRuntime.HasFire&&ArenaFireView.Alive==0,"viewless fire remains bot-visible hazard");
        Time.time+=.26f;ArenaFireRuntime.Tick();Near(test.bot.Taken,50,"nullable view ticks preserve damage");
        ArenaFireRuntime.Reset();Check(ArenaFireView.Alive==0,"viewless fire cleanup");
        test=Fresh(new RoundsMode());Explode(Throw(test.local));ArenaFireView.TickFailures=1;ArenaFireView.DisposeFailures=1;
        ArenaFireRuntime.Tick();Near(test.bot.Taken,25,"view tick failure preserves damage");
        Check(ArenaFireRuntime.HasFire&&ArenaFireView.Alive==0,"failed view disposed without deleting hazard");
        Time.time+=.26f;ArenaFireRuntime.Tick();Near(test.bot.Taken,50,"failed view does not poison next tick");
        Explode(Throw(test.local));ArenaFireView.DisposeFailures=1;ArenaFireRuntime.Reset();
        Check(ArenaFireView.Alive==0&&!ArenaFireRuntime.HasFire,"disposal failure cannot abort global cleanup");
    }

    private static void LifetimeBoundaries()
    {
        var mode=new RoundsMode();var test=Fresh(mode);
        var pending=Throw(test.local);mode.Round.Number++;
        Check(!Explode(pending)&&!ArenaFireRuntime.HasFire,"previous round projectile cannot create fire before tick");
        ArenaFireRuntime.Tick();Check(pending.Retired,"round replacement retires pending grenade");
        ArenaMode.Areas.ArenaAreaRuntime.IsPending=true;Check(!Explode(Throw(test.local))&&!ArenaFireRuntime.HasFire,"area staging custom harmless");ArenaMode.Areas.ArenaAreaRuntime.IsPending=false;
        ArenaMatchService.TeamStartAbortPending=true;Check(!Explode(Throw(test.local))&&!ArenaFireRuntime.HasFire,"team abort custom harmless");ArenaMatchService.TeamStartAbortPending=false;
        mode.Complete=true;Check(!Explode(Throw(test.local))&&!ArenaFireRuntime.HasFire,"completed match custom harmless");mode.Complete=false;
        ArenaFireRuntime.Tick();
        var hands=new Player.BaseGrenadeHandsController{_player=test.local,Item=new()};test.local.HandsController=hands;
        var mismatched=new Grenade{Player=new Bridge{iPlayer=test.bot},WeaponSource=hands.Item};Bind(hands,mismatched);
        Check(!Explode(mismatched)&&!ArenaFireRuntime.HasFire,"foreign physical bridge cannot own fire");
        mismatched=new Grenade{Player=new Bridge{iPlayer=test.local},WeaponSource=new()};Bind(hands,mismatched);
        Check(!Explode(mismatched)&&!ArenaFireRuntime.HasFire,"substitute weapon instance cannot own fire");
        for(int i=0;i<ArenaFirePolicy.MaxFires+2;i++)Check(!Explode(Throw(test.local)),"capped fire never falls back to explosion");
        Check(ArenaFireView.Alive==ArenaFirePolicy.MaxFires,"active fire count bounded");
        var oldWorld=test.world;
        test=Fresh(new GunGameMode());Check(!ArenaFireRuntime.HoldsBody(oldWorld.MainPlayer,test.world),"source retention cannot cross worlds");
        Explode(Throw(test.local));ArenaFireRuntime.Tick();Near(test.bot.Taken,25,"Gun Game retains supported fire");
        test=Fresh(new ZombieMode());Check(!Explode(Throw(test.local))&&!ArenaFireRuntime.HasFire,"unsupported zombie mode remains harmless");
        ArenaFireRuntime.Reset();ArenaSession.IsActive=false;Time.time+=20;ArenaFireRuntime.Tick();
        Check(ArenaFireView.Alive==0,"all fire views released at final cleanup");
    }
}
