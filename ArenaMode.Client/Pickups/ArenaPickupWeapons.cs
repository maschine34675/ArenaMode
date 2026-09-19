using ArenaMode.Kits;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace ArenaMode.Pickups;

/// <summary>Pickup-only copies; never inserted into the normal kit catalogue.</summary>
internal static class ArenaPickupWeapons
{
    internal const string M60Id = "pickup-m60-retzrestdfg";

    internal static KitDefinition Find(string id)
        => string.Equals(id, M60Id, StringComparison.Ordinal) ? CreateM60()
            : KitCatalog.Kits?.FirstOrDefault(kit => string.Equals(kit.Id, id, StringComparison.Ordinal));

    // Copied from spt41dev's stash assembly matching preset "retzrestdfg".
    // Only templates/relationships and pristine weapon/ammo state are retained.
    // These local labels are remapped by KitBuilder to fresh native item IDs.
    internal static KitDefinition CreateM60()
    {
        var kit = new KitDefinition { Id = M60Id, Name = "M60E6 — retzrestdfg", Fill = new KitFill { SpareMagazines = 0 } };
        void Add(string id, string tpl, string parent = null, string slot = null, JObject upd = null)
            => kit.Items.Add(new KitItemNode { Id = id, Tpl = tpl, ParentId = parent, SlotId = slot, Upd = upd });
        // The equipment parent is declarative only, but lets the existing preload
        // planner include the weapon root; no equipment item is handed out.
        Add("equipment", "55d7217a4bdc2d86028b456d");
        Add("weapon", "661cec09b2c6356b4d0c7a36", "equipment", "FirstPrimaryWeapon",
            JObject.Parse("{\"Repairable\":{\"Durability\":100,\"MaxDurability\":100},\"FireMode\":{\"FireMode\":\"fullauto\"}}"));
        Add("magazine", "660ea4453786cc0af808a1be", "weapon", "mod_magazine");
        Add("stock", "661520fb6f8e1a96340afaa6", "weapon", "mod_stock");
        Add("barrel", "6601279cc752a02bbe05e692", "weapon", "mod_barrel");
        Add("handguard", "661fbe066751ee51930b01f2", "weapon", "mod_handguard");
        Add("trigger", "66152153a031cbb5570e346f", "weapon", "mod_pistolgrip_000");
        Add("sight", "570fd721d2720bc5458b4596", "weapon", "mod_scope");
        Add("muzzle", "5d026791d7ad1a04a067ea63", "barrel", "mod_muzzle");
        Add("foregrip", "5c1cd46f2e22164bef5cfedb", "handguard", "mod_foregrip");
        Add("laser", "5cc9c20cd7f00c001336c65d", "handguard", "mod_tactical_000");
        Add("grip", "6615211ca031cbb5570e346d", "trigger", "mod_pistolgrip_001");
        for (int i = 0; i < 3; i++)
            Add("ammo" + i, "5efb0c1bd79ff02a1f5e68d9", "magazine", "cartridges",
                new JObject { ["StackObjectsCount"] = i < 2 ? 40 : 20 });
        return kit;
    }
}
