using ArenaMode;
using ArenaMode.Combat;
using ArenaMode.Combat.Translocator;
using ArenaMode.Hud;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool result,string name) { _checks++; if(!result) throw new Exception("FAIL: "+name); }
    private sealed record Fixture(GameWorld World,Player Player,Player.SinglePlayerInventoryController Inventory,Grid Rig,Grid Pockets)
    {
        internal Item Item=>Rig.Items.Concat(Pockets.Items).SingleOrDefault();
        internal void Tick()=>ArenaTranslocatorSupply.Tick(World,Player);
        internal UsableItemInputTranslator Hold(Item item=null)
        {
            var hands=new PortableRangeFinderController { _player=Player,Item=item??Item };
            Player.HandsController=hands;return new() { _controller=hands };
        }
    }
    private static Fixture Setup(int capacity=10)
    {
        ArenaTranslocatorModel.Shutdown(); ArenaTranslocatorInput.Shutdown();
        ArenaSession.IsActive=false; ArenaTranslocatorSupply.ForgetOutsideRaid(null);
        ArenaSession.IsActive=true; ArenaSession.ArmedForNextRaid=false; ArenaSession.ArmEpoch++;
        ArenaServerClient.TranslocatorAvailableForConfirmedRaid=true;
        ArenaMatchService.Current=new(); ArenaTranslocatorRuntime.Enabled=true;
        ArenaTranslocatorRuntime.PrimaryCount=ArenaTranslocatorRuntime.SecondaryCount=0;
        ArenaInputBlocker.Blocked=ArenaRespawnDriver.IsRespawnInProgress=WeaponSwapService.IsSwapping=false;
        KitBundlePreloader.Warmed=true; KitBundlePreloader.IsPreloading=false;
        ItemManipulator.RejectSimulation=ItemManipulator.RejectAdd=false; ItemManipulator.Added=ItemManipulator.Simulated=0; ItemManipulator.OnEvent=null;
        BindResult.Applied=0;BindResult.OnSimulation=null;
        ArenaToasts.Messages.Clear(); Plugin.ModLogger.Warnings.Clear(); Shader.Missing=false;
        var equipment=new InventoryEquipment();
        Grid Add(EquipmentSlot slot) { var c=new CompoundItem { Parent=equipment };var g=new Grid { Container=c,Capacity=capacity };c.Grids=new[]{g};equipment.Slots[slot]=new(){ContainedItem=c};return g; }
        var rig=Add(EquipmentSlot.TacticalVest);var pockets=Add(EquipmentSlot.Pockets);
        var inventory=new Player.SinglePlayerInventoryController { Inventory=new() { Equipment=equipment } };
        var player=new Player { InventoryController=inventory, HandsController=new Player.ItemHandsController() };
        var world=new GameWorld { MainPlayer=player };
        Singleton<GameWorld>.Instance=world;
        Singleton<AbstractGame>.Instance=new LocalGame { GameWorld=world };
        Singleton<ObjectsFactory>.Instance=new();
        Singleton<ItemFactory>.Instance=new();
        Singleton<ItemFactory>.Instance.ItemTemplates[ArenaTranslocatorItem.TemplateId]=new PortableRangeFinderTemplate
        { _id=ArenaTranslocatorItem.TemplateId, ParentId=ArenaTranslocatorItem.ParentTemplateId, Prefab=new() { path=ArenaTranslocatorItem.PrefabPath } };
        ArenaTranslocatorInput.Enable(); ArenaTranslocatorModel.Enable(); Time.frameCount++;
        return new(world,player,inventory,rig,pockets);
    }
    private static void SupplyChecks()
    {
        var f=Setup();f.Tick();var item=f.Item;
        Check(item is PortableRangeFinder && item.TemplateId==ArenaTranslocatorItem.TemplateId,"one persistent private device");
        Check(f.Rig.Items.Count==1 && ArenaTranslocatorSupply.Owns(f.Player,item),"grant reachable equipment ownership");
        Check(f.Inventory.Inventory.FastAccess.BoundItems[EBoundItem.Item4]==item,"first free native key bound");
        f.Tick();ArenaTranslocatorRuntime.Enabled=false;f.Tick();ArenaTranslocatorRuntime.Enabled=true;f.Tick();
        Check(ItemManipulator.Added==1 && Singleton<ItemFactory>.Instance.Created==1,"no duplicate across ticks or toggle");
        Check(!ArenaTranslocatorSupply.Owns(f.Player,new PortableRangeFinder {TemplateId=item.TemplateId,Id=item.Id,Parent=item.Parent}),"exact item reference required");
        item.Reachable=false;Check(!ArenaTranslocatorSupply.Owns(f.Player,item),"unreachable device loses ownership");item.Reachable=true;
        item.Parent=null;Check(!ArenaTranslocatorSupply.Owns(f.Player,item),"dropped device loses ownership");f.Tick();
        Check(ItemManipulator.Added==1,"drop is not refill");
        item.Parent=f.Rig.Container;item.ThrowOnGraph=true;
        Check(!ArenaTranslocatorSupply.Owns(f.Player,item),"retiring graph fails closed");item.ThrowOnGraph=false;
        var oldWorld=f.World;Singleton<GameWorld>.Instance=new() { MainPlayer=f.Player };
        Check(!ArenaTranslocatorSupply.Owns(f.Player,item),"world ownership exact");Singleton<GameWorld>.Instance=oldWorld;
        ArenaMatchService.Current=new();Check(!ArenaTranslocatorSupply.Owns(f.Player,item),"match ownership exact");

        f=Setup(0);f.Tick();f.Tick();f.Rig.Capacity=10;f.Tick();
        Check(ItemManipulator.Added==0 && Singleton<ItemFactory>.Instance.Created==1 && ArenaToasts.Messages.Count==1,"full inventory once no eviction retry");
        f=Setup();f.Rig.Capacity=0;f.Tick();Check(f.Pockets.Items.Count==1,"pocket fallback");
        f=Setup();f.Inventory.RejectOperations=true;f.Tick();Check(ItemManipulator.Added==0,"native placement permission");
        f=Setup();ItemManipulator.RejectSimulation=true;f.Tick();Check(ItemManipulator.Added==0,"native simulation required");
        f=Setup();ItemManipulator.OnEvent=s=>throw new Exception("notification failure");f.Tick();f.Tick();
        Check(ItemManipulator.Added==1 && ArenaTranslocatorSupply.Owns(f.Player,f.Item),"notification failure retains ownership without duplicate");

        foreach(var blocked in new[]{"inactive","disabled","respawn","swap","inventory","hands","changing"})
        {
            f=Setup();switch(blocked) {case "inactive":ArenaSession.IsActive=false;break;case "disabled":ArenaTranslocatorRuntime.Enabled=false;break;case "respawn":ArenaRespawnDriver.IsRespawnInProgress=true;break;case "swap":WeaponSwapService.IsSwapping=true;break;case "inventory":f.Player.IsInventoryOpened=true;break;case "hands":f.Player.HandsController=null;break;case "changing":f.Inventory.IsChangingWeapon=true;break;}
            f.Tick();Check(ItemManipulator.Added==0,"blocked supply "+blocked);
        }
        f=Setup();ArenaServerClient.TranslocatorAvailableForConfirmedRaid=false;f.Tick();
        Check(ItemManipulator.Added==0 && !ArenaTranslocatorSupply.HasVerifiedTemplate,"confirmed capability required");
        foreach(var change in new Action<PortableRangeFinderTemplate>[] {x=>x._id="other",x=>x.ParentId="other",x=>x.Width=2,x=>x.Height=2,x=>x.StackMaxSize=2,x=>x.ExaminedByDefault=false,x=>x.CanSellOnRagfair=true,x=>x.CanRequireOnRagfair=true,x=>x.LeftHandItem=true,x=>x.Prefab.path="other",x=>x.UsePrefab.path="other"})
        {
            f=Setup();change((PortableRangeFinderTemplate)Singleton<ItemFactory>.Instance.ItemTemplates[ArenaTranslocatorItem.TemplateId]);f.Tick();
            Check(!ArenaTranslocatorSupply.HasVerifiedTemplate && ItemManipulator.Added==0,"changed template rejected");
        }
        f=Setup();KitBundlePreloader.Warmed=false;f.Tick();
        var routine=((LocalGame)Singleton<AbstractGame>.Instance).Pending.Single();Check(routine.MoveNext(),"native preload started");
        ArenaMatchService.Current=new();ArenaTranslocatorSupply.ForgetOutsideRaid(f.World);
        Check(!routine.MoveNext(),"old preload retired");KitBundlePreloader.Warmed=true;f.Tick();
        Check(ItemManipulator.Added==1,"old preload cannot poison next match");
        var old=f.Item;f.Inventory.Inventory.Equipment=new InventoryEquipment();
        Check(!ArenaTranslocatorSupply.Owns(f.Player,old),"equipment replacement loses ownership");
    }
    private static void BindingChecks()
    {
        var f=Setup();var bound=f.Inventory.Inventory.FastAccess.BoundItems;
        foreach(var key in Enum.GetValues<EBoundItem>()) bound[key]=new Item();var saved=bound.ToArray();f.Tick();
        Check(BindResult.Applied==0 && saved.All(x=>ReferenceEquals(bound[x.Key],x.Value)),"full keybar never evicted");
        f=Setup();bound=f.Inventory.Inventory.FastAccess.BoundItems;var existing=new Item();bound[EBoundItem.Item4]=existing;f.Tick();
        Check(ReferenceEquals(bound[EBoundItem.Item4],existing) && ReferenceEquals(bound[EBoundItem.Item5],f.Item),"occupied key skipped");
        f=Setup();bound=f.Inventory.Inventory.FastAccess.BoundItems;
        BindResult.OnSimulation=(inventory,item,key)=>inventory.Inventory.FastAccess.BoundItems[key]=new Item();f.Tick();
        Check(BindResult.Applied==0 && bound.Count==7 && !bound.Values.Contains(f.Item),"late occupant never evicted");
        f=Setup();bound=f.Inventory.Inventory.FastAccess.BoundItems;
        BindResult.OnSimulation=(inventory,item,key)=>inventory.Inventory.FastAccess.BoundItems[EBoundItem.Item10]=item;f.Tick();
        Check(BindResult.Applied==0 && bound.Count==1 && ReferenceEquals(bound[EBoundItem.Item10],f.Item),"late user binding preserved");
        f=Setup();bound=f.Inventory.Inventory.FastAccess.BoundItems;
        ItemManipulator.OnEvent=s=>{if(s==CommandStatus.Begin)bound[EBoundItem.Item8]=f.Item;};f.Tick();
        Check(BindResult.Applied==0 && ReferenceEquals(bound[EBoundItem.Item8],f.Item),"existing device binding preserved");
    }
    private static void InputChecks()
    {
        var f=Setup();f.Tick();var t=f.Hold();
        bool Handle(ECommand c)=>ArenaTranslocatorInput.TryHandle(t,c);
        Check(Handle(ECommand.ToggleShooting) && ArenaTranslocatorRuntime.PrimaryCount==1,"primary native command admitted");
        Handle(ECommand.ToggleShooting);Check(ArenaTranslocatorRuntime.PrimaryCount==1,"same frame duplicate ignored");
        Check(Handle(ECommand.EndShooting) && ArenaTranslocatorRuntime.PrimaryCount==1,"release never fires");
        Time.frameCount++;Handle(ECommand.ToggleAlternativeShooting);Check(ArenaTranslocatorRuntime.SecondaryCount==1,"secondary suppresses native ADS");
        Time.frameCount++;Handle(ECommand.ToggleAlternativeShooting);Check(ArenaTranslocatorRuntime.SecondaryCount==2,"press binding without release works");
        Check(Handle(ECommand.EndAlternativeShooting) && ArenaTranslocatorRuntime.SecondaryCount==2,"ADS release consumed only");
        Check(!Handle(ECommand.ReloadWeapon),"unrelated command native");
        Check(ItemManipulator.Added==1 && f.Item!=null,"input never consumes device");
        f=Setup();f.Tick();t=f.Hold();t._controller.FirearmsAnimator.SetFire(true);
        Check(!t._controller.CanInteract()&&Handle(ECommand.ToggleShooting)&&ArenaTranslocatorRuntime.PrimaryCount==1,"raised native phase admits primary");
        Time.frameCount++;Check(Handle(ECommand.ToggleAlternativeShooting)&&ArenaTranslocatorRuntime.SecondaryCount==1&&!t._controller.IsAiming,"raised native phase admits secondary without ADS");
        var raised=(TestAnimator)t._controller.FirearmsAnimator.Animator;
        raised.PhaseOverride="Hands.IDLE";raised.NextOverride="Hands.AIMING";raised.Transition=true;
        Time.frameCount++;Check(Handle(ECommand.ToggleShooting)&&ArenaTranslocatorRuntime.PrimaryCount==2,"raised transition admits primary");
        foreach(var blocked in new[]{"disabled","capability","inventory","menu","respawn","swap","changing","interaction","transition","stalehands","inactive","handsaction","draw","finished","look","nextlook","closeinventory","ADS"})
        {
            f=Setup();f.Tick();t=f.Hold();var animation=(TestAnimator)t._controller.FirearmsAnimator.Animator;
            switch(blocked)
            {
                case "disabled":ArenaTranslocatorRuntime.Enabled=false;break;
                case "capability":ArenaServerClient.TranslocatorAvailableForConfirmedRaid=false;break;
                case "inventory":f.Player.IsInventoryOpened=true;break;
                case "menu":ArenaInputBlocker.Blocked=true;break;
                case "respawn":ArenaRespawnDriver.IsRespawnInProgress=true;break;
                case "swap":WeaponSwapService.IsSwapping=true;break;
                case "changing":f.Inventory.IsChangingWeapon=true;break;
                case "interaction":animation.SetBool(AnimationControllerParametersTable.BOOL_LEFT,true);break;
                case "transition":t._controller.Transition=true;break;
                case "stalehands":f.Player.HandsController=new Player.ItemHandsController();break;
                case "inactive":animation.SetBool(AnimationControllerParametersTable.BOOL_ACTIVE,false);break;
                case "handsaction":f.Inventory.HandsAction=true;break;
                case "draw":t._controller.CurrentHandsOperation=new PortableRangeFinderController.DrawOperation();break;
                case "finished":t._controller.CurrentHandsOperation.State=Player.EOperationState.Finished;break;
                case "look":animation.PhaseOverride="Hands.LOOK";break;
                case "closeinventory":animation.PhaseOverride="Hands.CLOSE INVENTORY";break;
                case "nextlook":animation.Transition=true;animation.NextOverride="Hands.LOOK";break;
                case "ADS":t._controller.IsAiming=true;break;
            }
            Check(Handle(ECommand.ToggleAlternativeShooting) && ArenaTranslocatorRuntime.SecondaryCount==0,"blocked owned input "+blocked);
        }
        f=Setup();f.Tick();t=f.Hold(new PortableRangeFinder{TemplateId=ArenaTranslocatorItem.DonorTemplateId});
        Check(!Handle(ECommand.ToggleAlternativeShooting),"ordinary rangefinder native ADS");
        t=f.Hold(f.Item);ArenaSession.IsActive=false;
        Check(!Handle(ECommand.ToggleShooting) && ArenaTranslocatorRuntime.PrimaryCount==0,"normal raid not intercepted");
        f=Setup();f.Tick();t=f.Hold();ArenaTranslocatorInput.Shutdown();
        Check(!Handle(ECommand.ToggleShooting) && ArenaTranslocatorRuntime.PrimaryCount==0,"input shutdown inert");
        ArenaTranslocatorInput.Enable();Check(Handle(ECommand.ToggleShooting),"input reenable works");
    }
    private sealed record ModelFixture(GameObject Root,WeaponPrefab Prefab,MeshFilter[] Bodies,MeshRenderer Lens,Mesh[] Original,Material[][] Materials);
    private static ModelFixture Model(bool pooled=false,bool stub=false)
    {
        var root=new GameObject("native rig");var prefab=root.AddComponent<WeaponPrefab>();prefab.Hierarchy=root.AddComponent<TransformLinks>();prefab.IsInPool=pooled;prefab.IsStub=stub;
        var bodies=new List<MeshFilter>();
        for(int i=0;i<2;i++)
        {
            var part=new GameObject("bodyLOD"+i);part.transform.SetParent(root.transform);
            var filter=part.AddComponent<MeshFilter>();filter.sharedMesh=new Mesh {name="item_ranger_1500_LOD"+i,bounds=new Bounds {center=new Vector3(-.001f,0,.037f)}};
            part.AddComponent<MeshRenderer>().sharedMaterials=new[]{new Material(Shader.Find("native"))};bodies.Add(filter);
        }
        var lens=new GameObject("linza");lens.transform.SetParent(root.transform);var renderer=lens.AddComponent<MeshRenderer>();
        return new(root,prefab,bodies.ToArray(),renderer,bodies.Select(x=>x.sharedMesh).ToArray(),bodies.Select(x=>x.GetComponent<MeshRenderer>().sharedMaterials).ToArray());
    }
    private static async Task ModelChecks()
    {
        Setup();var item=new PortableRangeFinder {TemplateId=ArenaTranslocatorItem.TemplateId};var m=Model();
        await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);
        Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh!=x.Second) && !m.Lens.enabled,"both body LODs and lenses styled");
        Check(m.Root.Components.OfType<WeaponPrefab>().Count()==1 && m.Prefab.Hierarchy!=null && m.Root.transform.Children.Count==3,"native rig preserved");
        foreach(var body in m.Bodies)
        {
            var mesh=body.sharedMesh;
            Check(mesh.subMeshCount==3 && mesh.Vertices.All(v=>float.IsFinite(v.x+v.y+v.z)) && mesh.Triangles.Values.All(t=>t.Count%3==0&&t.All(i=>i>=0&&i<mesh.Vertices.Count)),"original mesh valid topology");
            Check(mesh.bounds.extents.x<.04f && mesh.bounds.extents.y<.07f && mesh.bounds.extents.z<.07f && Math.Abs(mesh.bounds.center.z-.037f)<.01f,"device metric size and native center");
        }
        var ownMesh=m.Bodies.Select(x=>x.sharedMesh).ToArray();var ownMat=m.Bodies.SelectMany(x=>x.GetComponent<MeshRenderer>().sharedMaterials).ToArray();
        ArenaSession.IsActive=false;ArenaTranslocatorModel.Restore(m.Prefab);
        Check(m.Bodies.Zip(m.Original).All(x=>ReferenceEquals(x.First.sharedMesh,x.Second)) && m.Lens.enabled,"pool restores outside Arena");
        Check(ownMesh.All(x=>x.Destroyed)&&ownMat.All(x=>x.Destroyed)&&m.Original.All(x=>!x.Destroyed)&&m.Materials.SelectMany(x=>x).All(x=>!x.Destroyed),"only owned resources destroyed");

        Setup();m=Model();await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);
        var renderer=m.Bodies[0].GetComponent<MeshRenderer>();var own=renderer.sharedMaterials;var foreign=new Material(Shader.Find("foreign"));renderer.sharedMaterials=new[]{own[0],foreign,own[2]};
        var foreignMesh=new Mesh();m.Bodies[0].sharedMesh=foreignMesh;ArenaTranslocatorModel.Restore(m.Prefab);
        Check(ReferenceEquals(m.Bodies[0].sharedMesh,foreignMesh) && ReferenceEquals(renderer.sharedMaterials[1],foreign) && !foreign.Destroyed && own.All(x=>x.Destroyed),"foreign replacements preserved owned materials released");
        Setup();m=Model();await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);ownMesh=m.Bodies.Select(x=>x.sharedMesh).ToArray();
        m.Bodies[0].FailNextAssignment=true;ArenaTranslocatorModel.Restore(m.Prefab);
        Check(!ownMesh[0].Destroyed && ownMesh[1].Destroyed && m.Lens.enabled,"failed restoration retains only bound mesh");
        ArenaTranslocatorModel.Restore(m.Prefab);Check(ownMesh[0].Destroyed && !m.Root.GetComponent<ArenaTranslocatorModelLease>().Active,"pool retry completes restoration");
        Setup();m=Model();m.Bodies[0].GetComponent<MeshRenderer>().FailNextAssignment=true;var count=UnityEngine.Object.Created.Count;
        await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);
        Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second) && UnityEngine.Object.Created.Skip(count).Where(x=>x is Mesh||x is Material).All(x=>x.Destroyed),"partial apply cleans allocated resources");
        foreach(var kind in new[]{"mesh","material"})
        {
            Setup();m=Model();count=UnityEngine.Object.Created.Count;
            if(kind=="mesh")Mesh.FailNextUpload=true;else Material.FailNextProperty=true;
            await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);
            Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second) && UnityEngine.Object.Created.Skip(count).Where(x=>x is Mesh||x is Material).All(x=>x.Destroyed),"construction failure releases "+kind);
        }
        Setup();m=Model();m.Lens.enabled=false;await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);ArenaTranslocatorModel.Restore(m.Prefab);
        Check(!m.Lens.enabled,"originally hidden lens remains hidden");
        Setup();m=Model();Shader.Missing=true;await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);
        Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second),"missing shader retains native model");
        foreach(var kind in new[]{"ordinary","capability","pooled","stub","nohierarchy"})
        {
            Setup();m=Model(kind=="pooled",kind=="stub");if(kind=="nohierarchy")m.Prefab.Hierarchy=null;if(kind=="capability")ArenaServerClient.TranslocatorAvailableForConfirmedRaid=false;
            await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),kind=="ordinary"?new Item{TemplateId=ArenaTranslocatorItem.DonorTemplateId}:item);
            Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second),"model untouched "+kind);
        }
        foreach(var kind in new[]{"epoch","capability","shutdown"})
        {
            Setup();m=Model();var pending=new TaskCompletionSource<GameObject>();var wrapped=ArenaTranslocatorModel.Wrap(pending.Task,item);
            if(kind=="epoch")ArenaSession.ArmEpoch++;if(kind=="capability")ArenaServerClient.TranslocatorAvailableForConfirmedRaid=false;if(kind=="shutdown"){ArenaTranslocatorModel.Shutdown();ArenaTranslocatorModel.Enable();}
            pending.SetResult(m.Root);await wrapped;
            Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second),"late model rejected "+kind);
        }
        Setup();m=Model();await ArenaTranslocatorModel.Wrap(Task.FromResult(m.Root),item);ArenaTranslocatorModel.Shutdown();
        Check(m.Bodies.Zip(m.Original).All(x=>x.First.sharedMesh==x.Second) && m.Lens.enabled,"shutdown restores live device");
        var failure=new InvalidOperationException("native failure");bool propagated=false;
        ArenaTranslocatorModel.Enable();try {await ArenaTranslocatorModel.Wrap(Task.FromException<GameObject>(failure),item);}catch(InvalidOperationException ex){propagated=ReferenceEquals(ex,failure);}
        Check(propagated,"native async failure preserved");
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            SupplyChecks();BindingChecks();InputChecks();await ModelChecks();
            int native=NativeContracts.Verify(args.Length>0?args[0]:@"D:\SPT41\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll");
            Console.WriteLine($"TRANSLOCATOR_DEVICE_PRODUCTION_PASSED behavior={_checks} native={native}");return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
