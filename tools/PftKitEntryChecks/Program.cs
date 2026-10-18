using ArenaMode.Kits;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using Redeploy.Core.Inventory;

try
{
int checks=0;
void Check(bool ok,string label) { checks++; if(!ok) throw new Exception("CHECK FAILED: "+label); }
void Reject(Action action,string label) { bool threw=false; try { action(); } catch { threw=true; } Check(threw,label); }
bool RetainsPreparation(InventoryEquipment equipment)
{
    object table=typeof(TeamArenaEntryKit).GetField("Prepared",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
    return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table,[equipment,null]);
}
Item Equipped(InventoryEquipment eq,EquipmentSlot slot)=>eq.GetSlot(slot).ContainedItem;
string Fingerprint(Profile p)=>string.Join("|",p.Inventory.Equipment.GetAllItems().Select(i=>$"{i.Id}/{i.TemplateId}/{i.StackObjectsCount}/{i.CurrentAddress?.GetHashCode()}"))
    +";favorites="+string.Join(",",p.Inventory.FavoriteItemsStorage)+";limits="+string.Join(",",p.Inventory.DiscardLimits)
    +";mags="+string.Join(",",p.CheckedMagazines)+";chambers="+string.Join(",",p.CheckedChambers)
    +";known="+(p.Encyclopedia==null?"NULL":string.Join(",",p.Encyclopedia));

(Profile p,KitDefinition kit,ItemFactory factory) Fixture()
{
    var factory=new ItemFactory(); Singleton<ItemFactory>.Instance=factory;
    factory.ItemTemplates["equipment"]=()=>new InventoryEquipment();
    factory.ItemTemplates["own-pockets"]=()=>new CompoundItem { Slots=[new(){ID="SpecialSlot1",IsSpecial=true},new(){ID="SpecialSlot2",IsSpecial=true}],Grids=[new(){Capacity=20}] };
    factory.ItemTemplates["kit-pockets"]=()=>new CompoundItem{Grids=[new(){Capacity=4}]};
    factory.ItemTemplates["gun"]=()=>new Weapon{ExaminedByDefault=false,Slots=[new(){ID="mod_barrel",Required=true},new(){ID="mod_scope"},new(){ID="mod_magazine"},new(){ID="patron_in_weapon"}]};
    factory.ItemTemplates["barrel"]=()=>new Item();
    factory.ItemTemplates["optic"]=()=>new Item{ExaminedByDefault=false};
    factory.ItemTemplates["magazine"]=()=>new Magazine();
    factory.ItemTemplates["ammo"]=()=>new Item();
    factory.ItemTemplates["rig"]=()=>new CompoundItem{Grids=[new(){Capacity=20}]};
    factory.ItemTemplates["med"]=()=>new Item();
    factory.ItemTemplates["grenade"]=()=>new ThrowWeap();
    var source=new InventoryEquipment{Id="original-root",TemplateId="equipment"};
    var pockets=factory.CreateItem("original-pockets","own-pockets",null);
    source.GetSlot(EquipmentSlot.Pockets).AddWithoutRestrictions(pockets);
    var special=new Item{Id="original-special",TemplateId="secret",ExaminedByDefault=false};
    ((CompoundItem)pockets).Slots[0].AddWithoutRestrictions(special);
    foreach(var slot in new[]{EquipmentSlot.SecuredContainer,EquipmentSlot.Scabbard,EquipmentSlot.ArmBand})
        source.GetSlot(slot).AddWithoutRestrictions(new CompoundItem{Id="original-"+slot,TemplateId="protected-"+slot,Slots=[new(){ID="nested",ContainedItem=new Item{Id="child-"+slot,TemplateId="child-"+slot}}]});
    source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).AddWithoutRestrictions(new Weapon{Id="original-gun",TemplateId="own-gun"});
    var p=new Profile(); p.Inventory.Equipment=source;
    p.Inventory.FastAccess=new(source,new(){{EBoundItem.Four,special.Id},{EBoundItem.Five,"original-gun"}});
    p.Inventory.FavoriteItemsStorage.UnionWith([special.Id,"original-gun"]);
    p.Inventory.DiscardLimits[special.Id]=2;p.Inventory.DiscardLimits["original-gun"]=3;
    p.CheckedMagazines["original-gun"]=3;p.CheckedChambers.Add("original-gun");
    p.Encyclopedia["pre-existing"]=true;
    var kit=new KitDefinition{Id="fixture",Fill=new(){SpareMagazines=1,RigContents=[new(){Tpl="med"},new(){Tpl="grenade",OptionalQuickUseOnly=true}]} };
    KitItemNode N(string id,string tpl,string parent,string slot)=>new(){Id=id,Tpl=tpl,ParentId=parent,SlotId=slot};
    kit.Items=[N("root","equipment",null,null),N("pockets","kit-pockets","root","Pockets"),N("rig","rig","root","TacticalVest"),
        N("weapon","gun","root","FirstPrimaryWeapon"),N("barrel","barrel","weapon","mod_barrel"),N("optic","optic","weapon","mod_scope"),
        N("mag","magazine","weapon","mod_magazine"),N("ammo","ammo","mag","cartridges"),N("chamber","ammo","weapon","patron_in_weapon"),
        N("secure","UNKNOWN-SECURE","root","SecuredContainer"),N("secure-child","UNKNOWN-CHILD","secure","main"),
        N("scabbard","UNKNOWN-SCABBARD","root","Scabbard"),N("armband","UNKNOWN-ARMBAND","root","ArmBand"),
        N("special","UNKNOWN-SPECIAL","pockets","SpecialSlot1"),N("special-child","UNKNOWN-SPECIAL-CHILD","special","main")];
    kit.Items.Single(n=>n.Id=="ammo").Upd=new JObject{{"StackObjectsCount",30}};
    ProfileInventorySanitizer.Fail=false;ProfileInventorySanitizer.Calls=0;EquipmentSurgeon.Calls=0;
    return(p,kit,factory);
}

// Preparation is detached, preserves original pockets and excludes whole private subtrees.
{
    var(p,k,f)=Fixture(); string before=Fingerprint(p);var old=p.Inventory.Equipment;
    var fresh=TeamArenaEntryKit.Prepare(p,k);
    Check(ReferenceEquals(p.Inventory.Equipment,old)&&Fingerprint(p)==before,"prepare is read-only on profile tree and metadata");
    Check(fresh.CurrentAddress==null && !ReferenceEquals(fresh,old),"new detached equipment root");
    Check(Equipped(fresh,EquipmentSlot.Pockets).TemplateId=="own-pockets","preserve original pocket template");
    Check(!f.Created.Any(t=>t.StartsWith("UNKNOWN")),"protected roots and descendants never instantiated");
    Check(((CompoundItem)Equipped(fresh,EquipmentSlot.Pockets)).Slots.All(s=>s.ContainedItem==null),"special slots left for identity transfer");
    Check(fresh.GetAllItems().All(i=>!old.GetAllItems().Any(o=>o.Id==i.Id)),"fresh IDs disjoint from source");
    Check(EquipmentSurgeon.Calls==0&&ProfileInventorySanitizer.Calls==0,"no live-body surgeon or sanitizer before commit");
    var carried=new[]{EquipmentSlot.SecuredContainer,EquipmentSlot.Scabbard,EquipmentSlot.ArmBand}.ToDictionary(s=>s,s=>Equipped(old,s));
    Item special=((CompoundItem)Equipped(old,EquipmentSlot.Pockets)).Slots[0].ContainedItem;
    TeamArenaEntryKit.Commit(p,old,fresh);
    Check(ReferenceEquals(p.Inventory.Equipment,fresh),"actual equipment swapped");
    foreach(var pair in carried) Check(ReferenceEquals(Equipped(fresh,pair.Key),pair.Value)&&Equipped(old,pair.Key)==null,"protected identity transferred: "+pair.Key);
    Check(ReferenceEquals(((CompoundItem)Equipped(fresh,EquipmentSlot.Pockets)).Slots[0].ContainedItem,special),"special item identity transferred");
    Check(Equipped(old,EquipmentSlot.FirstPrimaryWeapon).Id=="original-gun","old ordinary gear unchanged");
    Check(p.Inventory.FavoriteItemsStorage.SetEquals([special.Id])&&p.Inventory.DiscardLimits.Count==1,"stale favorites/discard limits pruned");
    Check(p.Inventory.FastAccess.BoundItems.Count==1&&ReferenceEquals(p.Inventory.FastAccess.BoundItems[EBoundItem.Four],special),"only retained quickslot preserved");
    Check(fresh.GetAllItems().OfType<Magazine>().All(m=>p.CheckedMagazines.TryGetValue(m.Id,out int v)&&v==60),"issued magazines checked");
    Check(p.CheckedChambers.Contains(Equipped(fresh,EquipmentSlot.FirstPrimaryWeapon).Id),"issued chamber checked");
    Check(p.Encyclopedia.ContainsKey("gun")&&p.Encyclopedia.ContainsKey("optic"),"unknown issued weapon and attachment learned");
    Check(!p.Encyclopedia.ContainsKey("secret")&&p.Encyclopedia["pre-existing"],"private unexamined template and prior knowledge unchanged");
    Check(p.Inventory.TotalWeight.Calls==1&&p.Inventory.TotalWeightEliteSkill.Calls==1,"both weight caches invalidated");
    Check(!RetainsPreparation(fresh),"successful issuance releases source preparation graph");
    Reject(()=>TeamArenaEntryKit.Commit(p,fresh,fresh),"prepared issuance cannot commit twice");
}
{
    var(p,k,f)=Fixture();p.Encyclopedia=null;var old=p.Inventory.Equipment;var fresh=TeamArenaEntryKit.Prepare(p,k);
    TeamArenaEntryKit.Commit(p,old,fresh);Check(p.Encyclopedia==null,"null encyclopedia stays knows-all");
}

// Structural and live-factory failures fail before any source transfer.
var badDefinitions=new (string label,Action<KitDefinition> mutate)[] {
    ("missing root",k=>k.Items.RemoveAll(n=>n.ParentId==null)),
    ("duplicate ID",k=>k.Items.Add(k.Items[1])),
    ("orphan",k=>k.Items[4].ParentId="absent"),
    ("cycle",k=>k.Items[4].ParentId="barrel"),
    ("duplicate equipment slot",k=>k.Items.Add(new(){Id="second",Tpl="gun",ParentId="root",SlotId="FirstPrimaryWeapon"})),
    ("duplicate nested slot",k=>k.Items.Add(new(){Id="second",Tpl="optic",ParentId="weapon",SlotId="mod_scope"})),
    ("missing primary weapon",k=>k.Items.RemoveAll(n=>n.Id=="weapon"||n.ParentId=="weapon"||n.ParentId=="mag")),
    ("missing vital part",k=>k.Items.RemoveAll(n=>n.Id=="barrel")),
    ("unknown optional weapon component",k=>k.Items.Single(n=>n.Id=="optic").Tpl="unknown"),
    ("missing destination",k=>k.Items.Single(n=>n.Id=="optic").SlotId="absent"),
    ("bad upd",k=>k.Items.Single(n=>n.Id=="optic").Upd=new JObject{{"StackObjectsCount","not-an-int"}}),
    ("zero stack",k=>k.Items.Single(n=>n.Id=="ammo").Upd=new JObject{{"StackObjectsCount",0}}),
    ("malformed supply",k=>k.Fill.RigContents.Add(null))
};
foreach(var bad in badDefinitions) {
    var(p,k,f)=Fixture(); string before=Fingerprint(p);bad.mutate(k);
    Reject(()=>TeamArenaEntryKit.Prepare(p,k),bad.label+" rejected");Check(Fingerprint(p)==before,bad.label+" leaves source untouched");
}
foreach(string template in new[]{"equipment","own-pockets","gun","optic","med"}) {
    var(p,k,f)=Fixture();string before=Fingerprint(p);f.FailTemplate=template;
    Reject(()=>TeamArenaEntryKit.Prepare(p,k),"factory failure "+template);Check(Fingerprint(p)==before,"factory failure retains all source objects");
}
{
    var(p,k,f)=Fixture();f.ReuseIds=true;Reject(()=>TeamArenaEntryKit.Prepare(p,k),"factory ID reuse rejected");
}
{
    var(p,k,f)=Fixture();f.ItemTemplates["rig"]=()=>new CompoundItem{Grids=[new(){Capacity=0}]};
    f.ItemTemplates["own-pockets"]=()=>new CompoundItem{Slots=[new(){ID="SpecialSlot1",IsSpecial=true},new(){ID="SpecialSlot2",IsSpecial=true}],Grids=[new(){Capacity=0}]};
    Reject(()=>TeamArenaEntryKit.Prepare(p,k),"required med cannot silently disappear");
    k.Fill.RigContents.Clear();var fresh=TeamArenaEntryKit.Prepare(p,k);
    Check(fresh!=null,"spare magazines remain best-effort under real capacity limits");
}

// Wrong owners, detached-tree edits and replay attempts never get a transfer.
foreach(string mutation in new[]{"profile","inventory","equipment","provided-old","missing-optic","protected-occupant","attached-root"}) {
    var(p,k,f)=Fixture();var old=p.Inventory.Equipment;var fresh=TeamArenaEntryKit.Prepare(p,k);var actual=p;var supplied=old;
    if(mutation=="profile") actual=new Profile{Inventory=p.Inventory};
    if(mutation=="inventory") p.Inventory=new(){Equipment=old};
    if(mutation=="equipment") p.Inventory.Equipment=new();
    if(mutation=="provided-old") supplied=new();
    if(mutation=="missing-optic") ((Weapon)Equipped(fresh,EquipmentSlot.FirstPrimaryWeapon)).Slots.Single(s=>s.ID=="mod_scope").RemoveItemWithoutRestrictions();
    if(mutation=="protected-occupant") fresh.GetSlot(EquipmentSlot.Scabbard).AddWithoutRestrictions(new(){Id="injected"});
    if(mutation=="attached-root") fresh.CurrentAddress=new object();
    Reject(()=>TeamArenaEntryKit.Commit(actual,supplied,fresh),mutation+" rejected before transfer");
    Check(Equipped(old,EquipmentSlot.SecuredContainer)!=null,mutation+" retained original protected container");
}

// Exercise the actual transaction's reverse transfers and sanitizer restoration.
foreach(string fault in new[]{"first-add","later-add","special-add","remove","sanitize","weight"}) {
    var(p,k,f)=Fixture();var old=p.Inventory.Equipment;var fresh=TeamArenaEntryKit.Prepare(p,k);string before=Fingerprint(p);var fast=p.Inventory.FastAccess;
    if(fault=="first-add") fresh.GetSlot(EquipmentSlot.SecuredContainer).FailAdd=true;
    if(fault=="later-add") fresh.GetSlot(EquipmentSlot.ArmBand).FailAdd=true;
    if(fault=="special-add") ((CompoundItem)Equipped(fresh,EquipmentSlot.Pockets)).Slots[0].FailAdd=true;
    if(fault=="remove") old.GetSlot(EquipmentSlot.Scabbard).FailRemove=true;
    if(fault=="sanitize") ProfileInventorySanitizer.Fail=true;
    if(fault=="weight") p.Inventory.TotalWeight.FailAt=1;
    Reject(()=>TeamArenaEntryKit.Commit(p,old,fresh),fault+" aborts");
    Check(ReferenceEquals(p.Inventory.Equipment,old)&&Fingerprint(p)==before,fault+" rolls back equipment/items/metadata/knowledge");
    Check(ReferenceEquals(p.Inventory.FastAccess,fast),fault+" restores original fast access instance");
    Check(!RetainsPreparation(fresh),fault+" releases source preparation graph");
    Reject(()=>TeamArenaEntryKit.Commit(p,old,fresh),fault+" poisoned prepared handle rejects retries");
}
Console.WriteLine($"PFT_ENTRY_CHECKS_PASS assertions={checks}");
return 0;
}
catch(Exception error)
{
    Console.Error.WriteLine("CHECK FAILED: expected successful fixture or explicit rejection assertion: " + error);
    return 1;
}
