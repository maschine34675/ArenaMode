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
using System.Reflection.Emit;
using HarmonyLib;
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
            ArenaBackpackReload.Enable(); if(ModulePatch.Enabled.Count!=3) Console.Error.WriteLine(string.Join("; ",ArenaMode.Plugin.ModLogger.Warnings));
            Check(ModulePatch.Enabled.Count==3 && ModulePatch.Enabled.All(p=>!p.Target.IsGenericMethod && !p.Target.DeclaringType.IsGenericType),"never detour shared generic code");
            Check(ModulePatch.Enabled.Select(p=>p.Target.Name).SequenceEqual(new[]{"ReloadExternalMagazine","method_0","FindSuitableMagazine"}),"exact three magazine consumers only");
            VerifyTranspilers();
            postfix=typeof(ArenaBackpackReload).GetMethod("AppendBackpack",BindingFlags.NonPublic|BindingFlags.Static);
            Fresh();Apply();Check(output.SequenceEqual(new[]{rig,packed}),"backpack appended without changing native priority");
            Apply();Check(output.Count==2,"other reach mods cannot duplicate magazine candidates");
            Fresh();Apply(filter:new Predicate<Magazine>(_=>false));Check(output.SequenceEqual(new[]{rig}),"native predicate respected");
            Fresh();bag.Items.Add(new ContainerCollection{Items=[new Magazine()]});bag.Items.Add(new Weapon());Apply();Check(output.Count==2,"no magazines inside nested containers or weapons");
            Fresh();var pocket=new ContainerCollection();var round=new Ammo();var grenade=new ThrowWeap();pocket.Items.AddRange([round,grenade,rig]);controller.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem=pocket;
            var ammo=new List<Ammo>();controller.GetReachableItemsOfTypeNonAlloc(ammo);Check(ammo.SequenceEqual(new[]{round}),"native ammo query retains type");
            var grenades=new List<ThrowWeap>();controller.GetReachableItemsOfTypeNonAlloc(grenades);Check(grenades.SequenceEqual(new[]{grenade}),"native grenade query retains type");
            var extend=typeof(ArenaBackpackReload).GetMethod("AppendBackpackToResult",BindingFlags.Static|BindingFlags.NonPublic);
            var native=controller.GetReachableItemsOfType<Magazine>(null);
            output=((IEnumerable<Magazine>)extend.Invoke(null,new object[]{native,controller,null})).ToList();Check(output.SequenceEqual(new[]{rig,packed}),"helper runs original then extends");
            ArenaSession.IsActive=false;native=controller.GetReachableItemsOfType<Magazine>(null);
            var ordinary=(IEnumerable<Magazine>)extend.Invoke(null,new object[]{native,controller,null});Check(ReferenceEquals(ordinary,native)&&ordinary.SequenceEqual(new[]{rig}),"ordinary helper preserves original scan");
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
    static void VerifyTranspilers()
    {
        foreach(var patch in ModulePatch.Enabled)
        {
            bool nonAlloc=patch.Target.Name=="ReloadExternalMagazine";
            var query=typeof(InventoryController).GetMethod(nonAlloc?"GetReachableItemsOfTypeNonAlloc":"GetReachableItemsOfType");
            var mag=query.MakeGenericMethod(typeof(Magazine));var ammo=query.MakeGenericMethod(typeof(Ammo));var grenade=query.MakeGenericMethod(typeof(ThrowWeap));
            var method=patch.Patch.GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic);
            var generator=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();
            var call=new CodeInstruction(OpCodes.Callvirt,mag);var label=generator.DefineLabel();call.labels.Add(label);call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            var original=new[]{new CodeInstruction(OpCodes.Nop),new CodeInstruction(OpCodes.Callvirt,ammo),call,new CodeInstruction(OpCodes.Callvirt,grenade)};
            var changed=((IEnumerable<CodeInstruction>)method.Invoke(null,new object[]{original,generator})).ToArray();
            Check(changed.Count(i=>Equals(i.operand,ammo))==1&&changed.Count(i=>Equals(i.operand,grenade))==1,"transpiler leaves unrelated typed calls intact");
            var extension=changed.Single(i=>i.operand is MethodInfo m&&m.DeclaringType==typeof(ArenaBackpackReload));
            Check(extension.opcode==OpCodes.Call&&extension.operand is MethodInfo helper&&!helper.IsGenericMethod,"transpiler uses non-generic helper");
            Check(changed[2].labels.Single().Equals(label)&&changed[2].blocks.Count==1,"branch labels and exception boundaries retained");
            Check(changed.Count(i=>Equals(i.operand,mag))==1&&call.opcode==OpCodes.Callvirt,"native magazine query retained for foreign transpilers");
            var append=(MethodInfo)extension.operand;
            var parameters=nonAlloc?new[]{typeof(InventoryController),typeof(IList<Magazine>),typeof(Predicate<Magazine>)}:new[]{typeof(IEnumerable<Magazine>),typeof(InventoryController),typeof(Predicate<Magazine>)};
            Check(append.ReturnType==mag.ReturnType&&append.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters),"extension stack contract preserves original result");
            foreach(var invalid in new[]{new[]{new CodeInstruction(OpCodes.Callvirt,ammo)},new[]{new CodeInstruction(OpCodes.Callvirt,mag),new CodeInstruction(OpCodes.Callvirt,mag)}})
            {
                bool refused=false;try{method.Invoke(null,new object[]{invalid,generator});}catch(TargetInvocationException ex){refused=ex.InnerException is InvalidOperationException;}
                Check(refused&&invalid.All(i=>i.opcode==OpCodes.Callvirt),"missing or duplicate query rejected before editing IL");
            }
        }
    }
}
