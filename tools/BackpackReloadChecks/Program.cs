using ArenaMode.Kits;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Respawn;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using System.Reflection;
static class Program
{
    static int n;static MethodInfo postfix;static Player player;static InventoryController controller;static ContainerCollection bag;static Magazine rig,packed;static List<Magazine> output;
    static void Check(bool ok,string message){n++;if(!ok)throw new Exception("ASSERT: "+message);}
    static void Fresh()
    {
        ArenaSession.IsActive=ArenaSession.Exact=Settings.UseItemsAnywhere.Value=true;ArenaSession.ArmEpoch++;
        ArenaRespawnDriver.StandDown=ArenaRespawnDriver.IsRespawnInProgress=ArenaSpectatorService.IsActive=false;
        player=new();controller=player.InventoryController;bag=new();rig=new(){Id="rig"};packed=new(){Id="backpack"};bag.Items.Add(packed);
        controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem=bag;output=[rig];
        var world=new GameWorld{MainPlayer=player};Singleton<GameWorld>.Instance=world;
        Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=world,LocalPlayer=player};
    }
    static void Apply(object list=null,object filter=null)=>postfix.Invoke(null,new object[]{controller,list??output,filter});
    sealed class FilteredController:InventoryController
    {
        public int Calls;public bool Allow;
        public override void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] equipmentSlots,IList<T> preAllocatedList,Predicate<T> predicate=null,Predicate<ContainerCollection> goDeeperPredicate=null)
        {Calls++;if(Allow)base.GetAcceptableItemsNonAlloc(equipmentSlots,preAllocatedList,predicate,goDeeperPredicate);}
    }
    static int Main()
    {
        try
        {
            ArenaBackpackReload.Enable();var patch=ModulePatch.Enabled.Single();Check(patch.Target.IsGenericMethod&&patch.Target.GetGenericArguments().Single()==typeof(Magazine),"only concrete magazine generic target");
            postfix=patch.Patch.GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);
            Fresh();Apply();Check(output.SequenceEqual(new[]{rig,packed}),"backpack appended without changing native priority");
            Apply();Check(output.Count==2,"other reach mods cannot duplicate magazine candidates");
            Fresh();Apply(filter:new Predicate<Magazine>(_=>false));Check(output.SequenceEqual(new[]{rig}),"native predicate respected");
            Fresh();bag.Items.Add(new ContainerCollection{Items=[new Magazine()]});bag.Items.Add(new Weapon());Apply();Check(output.Count==2,"no magazines inside nested containers or weapons");
            Fresh();var ammo=new List<Ammo>();Apply(ammo);Check(ammo.Count==0,"generic shared ammo query unchanged");
            Fresh();Apply(filter:new Predicate<Ammo>(_=>true));Check(output.Count==1,"foreign generic predicate rejected");
            Fresh();Apply(output.AsReadOnly());Check(output.Count==1,"read-only candidate list unchanged");
            foreach(Action deny in new Action[]{()=>ArenaSession.IsActive=false,()=>ArenaSession.Exact=false,()=>Settings.UseItemsAnywhere.Value=false,()=>player.IsAI=true,()=>player.IsYourPlayer=false,()=>player.HealthController.IsAlive=false,()=>player.InventoryController=new(),()=>Singleton<GameWorld>.Instance.MainPlayer=new(),()=>((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new(),()=>((LocalGame)Singleton<AbstractGame>.Instance).Status=GameStatus.Stopped,()=>((LocalGame)Singleton<AbstractGame>.Instance).GameWorld=new(),()=>ArenaRespawnDriver.IsRespawnInProgress=true,()=>ArenaRespawnDriver.StandDown=true,()=>ArenaSpectatorService.IsActive=true,()=>Singleton<GameWorld>.Instance=null,()=>Singleton<AbstractGame>.Instance=null,()=>controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem=null})
            {Fresh();deny();Apply();Check(output.Count==1,"ordinary foreign or inactive inventory untouched");}
            Fresh();Apply(filter:new Predicate<Magazine>(_=>{player.InventoryController=new();return true;}));Check(output.Count==1,"predicate body replacement prevents publication");
            Fresh();Apply(filter:new Predicate<Magazine>(_=>{controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem=new ContainerCollection();return true;}));Check(output.Count==1,"dropped backpack cannot contribute stale candidates");
            Fresh();Apply(filter:new Predicate<Magazine>(_=>throw new Exception("predicate failed")));Check(output.Count==1,"predicate error keeps native candidates");
            Fresh();var screened=new FilteredController{Inventory=controller.Inventory};controller=screened;player.InventoryController=screened;
            Apply();Check(screened.Calls==1&&output.Count==1,"virtual native search and examination filter preserved");
            screened.Allow=true;Apply();Check(screened.Calls==2&&output.Count==2,"searched examined magazines may pass native override");
            Console.WriteLine($"ARENA_BACKPACK_RELOAD_BEHAVIOR_PASSED: {n} assertions; source-linked query extension, explicit native boundaries");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException());return 1;}
    }
}
