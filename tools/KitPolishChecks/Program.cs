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
            Check(File.Exists(catalog), "required real kit catalog exists " + catalog);
            KitSummary.Reset(); // The two catalogs deliberately reuse the same IDs.
            var kits = JsonConvert.DeserializeObject<List<KitDefinition>>(File.ReadAllText(catalog));
            foreach (var kit in kits)
            {
                var line = KitSummary.For(kit);
                Check(!string.IsNullOrWhiteSpace(line), "real kit description is visible " + kit.Id);
                foreach (string slot in new[] { "FirstPrimaryWeapon", "Headwear", "Earpiece" })
                {
                    var node = kit.Items.FirstOrDefault(n => n.SlotId == slot);
                    if (node != null)
                        Check(line.Contains(db[node.Tpl]["_name"].ToString()), "real description retains " + slot + " " + kit.Id);
                }
                if (kit.Fill?.SpareMagazines > 0)
                    Check(line.Contains($"{kit.Fill.SpareMagazines} spare mag"), "real description retains spare magazines " + kit.Id);
                // Independent oracle for the shipped formats: an actual node in
                // Front_plate wins, otherwise the fitted Soft_armor_front node.
                int expected = kit.Items.Where(n => n.SlotId == "Front_plate")
                    .Select(n => int.Parse(db[n.Tpl]["_props"]["armorClass"].ToString())).DefaultIfEmpty().Max();
                if (expected == 0) expected = kit.Items.Where(n => n.SlotId == "Soft_armor_front")
                    .Select(n => int.Parse(db[n.Tpl]["_props"]["armorClass"].ToString())).DefaultIfEmpty().Max();
                Check(KitSummary.FrontBodyClass(kit) == expected, "real front armor " + kit.Id);
                if (expected > 0) Check(line.Contains($"armour {expected}"), "real description retains front armor " + kit.Id);
            }
            Console.WriteLine($"Real front armor: {kits.Count} kits from {catalog}.");
        }
        DescriptionFixtures();
        Console.WriteLine($"KIT_POLISH_ASSERTIONS_PASSED: {_checks}");
    }
    private static void DescriptionFixtures()
    {
        var factory = Comfort.Common.Singleton<EFT.ItemFactory>.Instance;
        var vest = new VestTemplate { armorClass=3, armorColliders=[EBodyPartColliderType.RibcageUp] };
        // Positive control for the independently inspected native boundary. The
        // old harness silently converted every vest's missing array into mask 0.
        bool nativeFault = false;
        try { _ = vest.ArmorPlateColliders; } catch (NullReferenceException) { nativeFault = true; }
        Check(nativeFault, "native vest boundary reproduces missing-array failure");
        factory.ItemTemplates[new MongoID("legacyVest")] = vest;
        factory.ItemTemplates[new MongoID("testGun")] = new ItemTemplate { Name="test rifle" };
        factory.ItemTemplates[new MongoID("testHelmet")] = new ArmorTemplate { Name="test helmet", ArmorClass=3 };
        factory.ItemTemplates[new MongoID("testEars")] = new ItemTemplate { Name="test headset" };
        var kit = new KitDefinition { Id="description-legacy-vest", Fill=new KitFill { SpareMagazines=3 }, Items=[
            new() { Id="equipment",Tpl="root" },
            new() { Id="vest",Tpl="legacyVest",ParentId="equipment",SlotId="TacticalVest" },
            new() { Id="gun",Tpl="testGun",ParentId="equipment",SlotId="FirstPrimaryWeapon" },
            new() { Id="helmet",Tpl="testHelmet",ParentId="equipment",SlotId="Headwear" },
            new() { Id="ears",Tpl="testEars",ParentId="equipment",SlotId="Earpiece" }
        ] };
        KitSummary.Reset();
        string line=KitSummary.For(kit);
        Check(line.Contains("armour 3"), "positive legacy soft vest with null plate array remains described");
        Check(line.Contains("test rifle") && line.Contains("test helmet (3)") && line.Contains("test headset") && line.Contains("3 spare mags"), "complete legacy description");
        vest.armorPlateColliders = [EArmorPlateCollider.Plate_Granit_SAPI_back];
        KitSummary.Reset();
        Check(!KitSummary.For(kit).Contains("armour 3"), "nonzero vest plate mask is not invented integral soft armor");

        factory.ItemTemplates[new MongoID("legacyVest")] = new BrokenArmorTemplate();
        KitSummary.Reset();
        int warnings=ArenaMode.Plugin.ModLogger.Warnings.Count;
        line=KitSummary.For(kit);
        Check(line.Contains("test rifle") && line.Contains("test helmet (3)") && line.Contains("test headset") && line.Contains("3 spare mags"), "broken armor preserves all unrelated description sections");
        Check(!line.Contains("armour "), "broken armor does not advertise fabricated class");
        Check(ArenaMode.Plugin.ModLogger.Warnings.Count==warnings+1, "one diagnostic for broken armor");
        Check(KitSummary.For(kit)==line && ArenaMode.Plugin.ModLogger.Warnings.Count==warnings+1, "cached partial description does not spam every draw");
        factory.ItemTemplates[new MongoID("legacyVest")] = vest;
        vest.armorPlateColliders=null;
        KitSummary.Reset();
        Check(KitSummary.For(kit).Contains("armour 3"), "catalog reset clears partial cached descriptions");
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
