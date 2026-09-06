using ArenaMode.Kits;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int _checks;
    private static void Check(bool okay, string message)
    {
        _checks++;
        if (!okay) throw new InvalidOperationException("ASSERT: " + message);
    }
    private static void Main(string[] args)
    {
        string root = args[0], database = args[1];
        var db = JObject.Parse(File.ReadAllText(database));
        DatabaseBoundary.Install(db);
        ArmorFixtures();
        DatabaseBoundary.Install(db);
        foreach (string catalog in new[] { Path.Combine(root, "ArenaMode.Server/defaults/kits.json"), Path.Combine(Path.GetDirectoryName(database)!, "../../../user/mods/ArenaMode/config/kits.json") })
        {
            if (!File.Exists(catalog)) continue;
            var kits = JsonConvert.DeserializeObject<List<KitDefinition>>(File.ReadAllText(catalog));
            foreach (var kit in kits)
            {
                // Independent oracle for the shipped formats: an actual node in
                // Front_plate wins, otherwise the fitted Soft_armor_front node.
                int expected = kit.Items.Where(n => n.SlotId == "Front_plate")
                    .Select(n => int.Parse(db[n.Tpl]["_props"]["armorClass"].ToString())).DefaultIfEmpty().Max();
                if (expected == 0) expected = kit.Items.Where(n => n.SlotId == "Soft_armor_front")
                    .Select(n => int.Parse(db[n.Tpl]["_props"]["armorClass"].ToString())).DefaultIfEmpty().Max();
                Check(KitSummary.FrontBodyClass(kit) == expected, "real front armor " + kit.Id);
            }
            Console.WriteLine($"Real front armor: {kits.Count} kits from {catalog}.");
        }
        Console.WriteLine($"KIT_POLISH_ASSERTIONS_PASSED: {_checks}");
    }
    private static void ArmorFixtures()
    {
        var factory = Comfort.Common.Singleton<EFT.ItemFactory>.Instance;
        void Put(string id, ArmorTemplate template) => factory.ItemTemplates[new MongoID(id)] = template;
        var chest = EArmorPlateCollider.Plate_Granit_SAPI_chest;
        var back = EArmorPlateCollider.Plate_Granit_SAPI_back;
        var side = EArmorPlateCollider.Plate_Granit_SSAPI_side_left_high;
        Put("carrier", new ArmorTemplate { Slots = [
            new ArmorSlot { ID="Front_plate", ArmorPlateColliderMask=chest },
            new ArmorSlot { ID="Back_plate", ArmorPlateColliderMask=back },
            new ArmorSlot { ID="Left_side_plate", ArmorPlateColliderMask=side },
            new ArmorSlot { ID="Soft_armor_front", ArmorColliders=[EBodyPartColliderType.RibcageUp] },
            new ArmorSlot { ID="Soft_armor_back", ArmorColliders=[EBodyPartColliderType.SpineTop] },
            new Slot { ID="grid" }] });
        Put("front4", new ArmorTemplate { ArmorClass=4, ArmorPlateColliders=chest|back|side });
        Put("side6", new ArmorTemplate { ArmorClass=6, ArmorPlateColliders=chest|back|side });
        Put("soft2", new ArmorTemplate { ArmorClass=2, ArmorColliders=[EBodyPartColliderType.RibcageUp] });
        Put("backsoft6", new ArmorTemplate { ArmorClass=6, ArmorColliders=[EBodyPartColliderType.SpineTop] });
        KitDefinition Build(params (string tpl, string slot)[] children) => new()
        {
            Id=Guid.NewGuid().ToString(), Items = new List<KitItemNode> {
                new() { Id="eq",Tpl="equipment" }, new() { Id="body",Tpl="carrier",ParentId="eq",SlotId="ArmorVest" }
            }.Concat(children.Select((c,i) => new KitItemNode { Id="n"+i,Tpl=c.tpl,ParentId="body",SlotId=c.slot })).ToList()
        };
        var kit = Build(("front4","Front_plate"),("side6","Left_side_plate"),("side6","Back_plate"),("soft2","Soft_armor_front"));
        Check(KitSummary.FrontBodyClass(kit)==4,"front 4 wins over side 6");
        Check(KitSummary.For(kit).Contains("armour 4") && !KitSummary.For(kit).Contains("armour 6"),"actual buy summary uses front class");
        Check(KitSummary.FrontBodyClass(Build(("side6","Back_plate"),("side6","Left_side_plate"),("soft2","Soft_armor_front")))==2,"missing front uses physical front soft armor");
        Check(KitSummary.FrontBodyClass(Build(("side6","Left_side_plate")))==0,"side-only carrier has no front armor");
        Check(KitSummary.FrontBodyClass(Build(("side6","grid")))==0,"stored spare plate is not protection");
        Check(KitSummary.FrontBodyClass(Build(("backsoft6","Soft_armor_front")))==0,"part and slot soft coverage must intersect");
        Check(KitSummary.FrontBodyClass(Build(("soft2","Soft_armor_back")))==0,"front soft part in back slot is not front protection");
        Put("integral", new ArmorTemplate { ArmorClass=3,ArmorColliders=[EBodyPartColliderType.RibcageUp] });
        var integral=Build(); integral.Items[1].Tpl="integral";
        Check(KitSummary.FrontBodyClass(integral)==3,"integral thorax armor fallback");
        Put("integral", new ArmorTemplate { ArmorClass=6,ArmorColliders=[EBodyPartColliderType.LeftSideChestUp] });
        Check(KitSummary.FrontBodyClass(integral)==0,"integral side-only does not become thorax front");
        kit.Items[1].ParentId="missing";
        Check(KitSummary.FrontBodyClass(kit)==0,"unequipped orphan armor ignored");
        Check(KitSummary.FrontBodyClass(null)==0,"null kit safe");
        Check(KitSummary.FrontBodyClass(Build())==0,"bare carrier not its slot filter class");
    }
}
