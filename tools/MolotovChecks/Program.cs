using ArenaMode.Combat.Fire;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using System.Reflection;

internal static class Program
{
    private static int assertions;
    private static void Check(bool yes,string name) { assertions++;if(!yes)throw new Exception("FAILED: "+name); }
    private static void Near(float a,float b,string name)=>Check(Math.Abs(a-b)<.002f,name);
    internal static bool Explode(Grenade grenade)=>(bool)typeof(ArenaFireRuntime).GetMethod("Explosion",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{grenade});
    private static void Bind(Player.BaseGrenadeHandsController hands,Grenade grenade)=>typeof(ArenaFireRuntime).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{hands,grenade});
    private static Grenade Throw(Player owner,string tpl=ArenaFirePolicy.DonorTemplate,bool supplied=true)
    {
        var item=supplied&&tpl==ArenaFirePolicy.DonorTemplate
            ? owner.InventoryController.Inventory.Equipment.Items.OfType<ThrowWeap>().FirstOrDefault(x=>ArenaFireSupply.Owns(owner,x)) : null;
        var hands=new Player.BaseGrenadeHandsController{_player=owner,Item=item??new(){TemplateId=tpl}}; owner.HandsController=hands;
        var g=new Grenade {WeaponSource=hands.Item,Player=new Bridge{iPlayer=owner}};Bind(hands,g);return g;
    }
    private static void Main()
    {
        var savedOff = new BepInEx.Configuration.ConfigFile();
        savedOff.Stored["Arena incendiary prototype/Enabled"] = false;
        ArenaFireRuntime.Enable(savedOff);
        Check(!ArenaFireRuntime.Enabled.Value, "saved Molotov opt-out survives default change");
        ArenaFireRuntime.Enable(new());
        Singleton<GlobalConfiguration>.Instance=new();
        Singleton<ItemFactory>.Instance=new();
        Singleton<ObjectsFactory>.Instance=new();
        var world=new GameWorld();Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world};
        var local=new Player{ProfileId="local",IsYourPlayer=true,Position=new(20,0,20)};
        var bot=new Player{ProfileId="bot",IsAI=true};world.MainPlayer=local;world.AllAlivePlayersList.AddRange(new[]{local,bot});
        var mode=new RoundsMode();ArenaMatchService.Current=new(){Mode=mode};
        Check(ArenaFireRuntime.Enabled.Value,"Molotov defaults on for fresh settings");
        ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.BotEscapeAvailable=true;ArenaFireRuntime.Tick();
        Check(Explode(Throw(local)),"ordinary raid untouched");
        Check(local.InventoryController.Inventory.Equipment.Items.Count==0,"ordinary raid supplies nothing");
        ArenaSession.IsActive=true;ArenaFireRuntime.Tick();
        Check(local.InventoryController.Inventory.Equipment.Items.Count==1,"one additional local Molotov supplied");
        Check(local.InventoryController.Events==2,"native add begin and succeed events raised");
        Check(Explode(Throw(local,ArenaFirePolicy.DonorTemplate,false)),"ordinary local RGD-5 remains explosive");
        ArenaMatchService.Current.Mode=new ZombieMode();ArenaFireRuntime.Tick();
        Check(Explode(Throw(local)),"Zombie grenades untouched even with default on");
        Check(ArenaFireView.Alive==0,"Zombie mode cannot create fire");
        ArenaMatchService.Current.Mode=mode;ArenaFireRuntime.Tick();
        Check(Explode(Throw(bot)),"bot grenades untouched");
        Check(Explode(Throw(local,"other")),"other template untouched");
        ArenaFireRuntime.BotEscapeAvailable=false;Check(Explode(Throw(local)),"companion required");ArenaFireRuntime.BotEscapeAvailable=true;
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
        mode.AssistScoringOpen=false;ArenaFireRuntime.Tick();Check(ArenaFireView.Alive==0,"intermission extinguishes");Check(Explode(Throw(replacement)),"intermission no conversion");
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
        SupplyChecks();
        Console.WriteLine($"ARENA_MOLOTOV_PRODUCTION_PASSED: {assertions} assertions");
    }

    private static void SupplyChecks()
    {
        (GameWorld world,Player player) Start(bool enabled=true,bool companion=true,bool full=false,bool busy=false)
        {
            ArenaSession.IsActive=false;ArenaFireRuntime.Tick();
            ArenaMode.Kits.KitBundlePreloader.Warmed=false;
            var player=new Player{ProfileId=Guid.NewGuid().ToString(),IsYourPlayer=true};
            var world=new GameWorld{MainPlayer=player};world.AllAlivePlayersList.Add(player);
            Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world};
            ArenaMatchService.Current=new(){Mode=new RoundsMode()};ArenaSession.IsActive=true;
            ArenaFireRuntime.Enabled.Value=enabled;ArenaFireRuntime.BotEscapeAvailable=companion;
            var equipment=player.InventoryController.Inventory.Equipment;
            var grid=equipment.Grids.First();grid.Items.Add(new ThrowWeap());grid.Items.Add(new ThrowWeap());
            equipment.TopPriorityGrenade=(ThrowWeap)grid.Items[0];
            if(full)foreach(var g in equipment.Grids)g.Capacity=g.Items.Count;
            player.InventoryController.IsChangingWeapon=busy;
            return(world,player);
        }
        var (world,player)=Start();var equipment=player.InventoryController.Inventory.Equipment;
        var originals=equipment.Items.ToArray();ArenaFireRuntime.Tick();
        Check(equipment.Items.Count==3&&originals.All(x=>equipment.Items.Contains(x)),"authored grenade identities preserved plus one");
        var bonus=equipment.Items.OfType<ThrowWeap>().Single(x=>ArenaFireSupply.Owns(player,x));
        Check(ReferenceEquals(equipment.TopPriorityGrenade,bonus),"native G preference is supplied Molotov");
        equipment.TopPriorityGrenade=(ThrowWeap)originals[0];ArenaFireRuntime.Tick();
        Check(ReferenceEquals(equipment.TopPriorityGrenade,originals[0]),"manual grenade selection preserved on subsequent ticks");
        ArenaFireRuntime.Enabled.Value=false;ArenaFireRuntime.Tick();ArenaFireRuntime.Enabled.Value=true;ArenaFireRuntime.Tick();
        Check(equipment.Items.Count==3,"off on cannot duplicate bonus");
        foreach(var g in equipment.Grids)g.Items.Remove(bonus);ArenaFireRuntime.Tick();
        Check(equipment.Items.Count==2,"spent or discarded Molotov not replenished");
        ((RoundsMode)ArenaMatchService.Current.Mode).Round.Number++;ArenaFireRuntime.Tick();
        Check(equipment.Items.Count==2,"round number alone cannot replenish same loadout");
        player.InventoryController.Inventory.Equipment=new();ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==1,"fresh own gear equipment receives one bonus");
        Check(!ArenaFireSupply.Owns(player,bonus),"old equipment token cannot convert on replacement body");

        (world,player)=Start(enabled:false);ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"disabled mutator adds nothing");
        (world,player)=Start(companion:false);ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"missing companion adds nothing");
        (world,player)=Start(full:true);equipment=player.InventoryController.Inventory.Equipment;originals=equipment.Items.ToArray();ArenaFireRuntime.Tick();
        Check(equipment.Items.SequenceEqual(originals),"full inventory preserves every existing item");
        int created=Singleton<ItemFactory>.Instance.Created;ArenaFireRuntime.Tick();
        Check(created==Singleton<ItemFactory>.Instance.Created,"full inventory attempts once without frame retry spam");
        (world,player)=Start(busy:true);ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"busy native inventory defers supply");
        player.InventoryController.IsChangingWeapon=false;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==3,"supply resumes once inventory ready");
        (world,player)=Start();player.InventoryController.BlockActions=true;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"native CanExecute refusal preserved");
        (world,player)=Start();player.InventoryController.ThrowEvent=true;ArenaFireRuntime.Tick();ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==3,"foreign event exception cannot duplicate physical grant");
        Check(player.InventoryController.Inventory.Equipment.Items.OfType<ThrowWeap>().Count(x=>ArenaFireSupply.Owns(player,x))==1,"identity retained after event exception");
        (world,player)=Start();Singleton<ObjectsFactory>.Instance=null;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"missing native pool factory cannot grant unpooled item");
        Singleton<ObjectsFactory>.Instance=new();ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==3,"pool factory arrival resumes supply");
        (world,player)=Start();ArenaMode.Kits.KitBundlePreloader.Warmed=true;ArenaMode.Kits.KitBundlePreloader.IsPreloading=true;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==3,"warm donor does not wait for unrelated catalog preload");
        ArenaMode.Kits.KitBundlePreloader.IsPreloading=false;
        (world,player)=Start();ArenaMode.Kits.KitBundlePreloader.Fail=true;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"failed resource preload cannot grant unpooled item");
        ArenaMode.Kits.KitBundlePreloader.Fail=false;
        (world,player)=Start();ArenaMode.Kits.KitBundlePreloader.Hold=true;ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"pending resource preload cannot grant item");
        var oldGame=(LocalGame)Singleton<AbstractGame>.Instance;
        (world,player)=Start(enabled:false);ArenaFireRuntime.Tick();
        ArenaMode.Kits.KitBundlePreloader.Hold=false;oldGame.AdvanceCoroutines();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"old world preload completion cannot grant into next raid");
        (world,player)=Start();ArenaMatchService.Current.Mode=new ZombieMode();ArenaFireRuntime.Tick();
        Check(player.InventoryController.Inventory.Equipment.Items.Count==2,"Zombie loadout not given unsupported Molotov");
        ArenaSession.IsActive=false;ArenaFireRuntime.Tick();
        Check(!player.InventoryController.Inventory.Equipment.Items.OfType<ThrowWeap>().Any(x=>ArenaFireSupply.Owns(player,x)),"raid exit clears ephemeral item identities");
    }
}
