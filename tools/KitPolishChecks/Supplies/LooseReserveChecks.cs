using ArenaMode.Kits;
using EFT.InventoryLogic;

internal static partial class Program
{
    private static void LooseReserveChecks(List<KitDefinition> kits, List<KitDefinition> ladder)
    {
        int fixedKits = 0, oddStacks = 0, detachable = 0, multiStack = 0, stockSpares = 0;
        foreach (var kit in kits)
        {
            var built = Build(kit);
            var weapons = built.Descendants().OfType<Weapon>().ToArray();
            var magazine = weapons.SelectMany(w => w.Descendants()).OfType<Magazine>()
                .FirstOrDefault(m => m.Cartridges.Items.Any());
            var inWeapons = weapons.SelectMany(w => w.Descendants()).ToHashSet();
            if (magazine?.ReloadMagType != Weapon.EReloadMode.InternalMagazine)
            {
                var stowed = built.Descendants().OfType<Magazine>().Where(m => !inWeapons.Contains(m)).ToArray();
                stockSpares += stowed.Length;
                SpareMagazinesFull(stowed, "full kit " + kit.Id);
                continue;
            }
            fixedKits++;
            var loaded = magazine.Cartridges.Items.First();
            var loose = built.Descendants().Where(i => i.TemplateId == loaded.TemplateId && !inWeapons.Contains(i)).ToArray();
            var expected = (Math.Max(magazine.MaxCount, loaded.StackMaxSize) * kit.Fill.SpareMagazines + 1) / 2;
            Check(loose.Sum(i => i.StackObjectsCount) == expected, "full kit halves total loose reserve: " + kit.Id);
            Check(loose.All(i => i.StackObjectsCount > 0), "no empty loose ammo stacks");
            LooseStacksWithinStackSize(loose, "full kit " + kit.Id);
        }
        foreach (var rung in ladder)
        {
            var baseline = KitBuilder.BuildPrimaryWeapon(rung, 0, out var none);
            Check(none.Count == 0, "zero reserve remains zero");
            var loadedBefore = baseline.Descendants().OfType<Magazine>().SelectMany(m => m.Cartridges.Items).Sum(i => i.StackObjectsCount);
            var chambersBefore = baseline.Chambers.Select(c => c.ContainedItem?.StackObjectsCount ?? 0).ToArray();
            var weapon = KitBuilder.BuildPrimaryWeapon(rung, 3, out var spares);
            var magazine = weapon.Descendants().OfType<Magazine>().FirstOrDefault(m => m.Cartridges.Items.Any());
            if (magazine == null) continue;
            Check(loadedBefore == weapon.Descendants().OfType<Magazine>().SelectMany(m => m.Cartridges.Items).Sum(i => i.StackObjectsCount), "loaded magazine unchanged");
            Check(chambersBefore.SequenceEqual(weapon.Chambers.Select(c => c.ContainedItem?.StackObjectsCount ?? 0)), "chambers unchanged");
            var loaded = magazine.Cartridges.Items.First();
            if (magazine.ReloadMagType == Weapon.EReloadMode.InternalMagazine)
            {
                int full = Math.Max(magazine.MaxCount, loaded.StackMaxSize);
                Check(spares.All(i => i.TemplateId == loaded.TemplateId), "internal weapon gets loose reload rounds");
                Check(spares.Sum(i => i.StackObjectsCount) == (full * 3 + 1) / 2, "weapon swap halves total loose reserve: " + rung.Id);
                LooseStacksWithinStackSize(spares, "weapon swap " + rung.Id);
                if (full % 2 != 0)
                {
                    oddStacks++;
                    Check(spares.Max(i => i.StackObjectsCount) - spares.Min(i => i.StackObjectsCount) <= 1, "odd total is evenly distributed");
                }
            }
            else
            {
                detachable++;
                Check(spares.Count == 3 && spares.All(i => i is Magazine), "detachable spare magazines unchanged");
                SpareMagazinesFull(spares.Cast<Magazine>(), "weapon swap " + rung.Id);
                if (magazine.MaxCount > loaded.StackMaxSize)
                {
                    // The full-kit fill of the same box (a Gun Game respawn builds the whole kit),
                    // into a backpack with room for two, so a refusal cannot hide a short box.
                    multiStack++;
                    var backpack = Grid(8, 8);
                    RunFill(Equipment(null, null, backpack), new() { Id = rung.Id, Fill = new() { SpareMagazines = 2, RigContents = [] } },
                        new() { { "loaded", magazine } });
                    var stowed = backpack.Items.OfType<Magazine>().ToArray();
                    Check(stowed.Length == 2, "full kit stows both multi-stack spares in a free backpack: " + rung.Id);
                    SpareMagazinesFull(stowed, "full kit " + rung.Id);
                }
            }
        }
        Check(fixedKits >= 3 && oddStacks > 0 && detachable > 0, "real catalog covers fixed, odd-stack and detachable ammo controls");
        // PKM/PKP and M60 boxes (40-stack rounds) and the RPD box (60-stack) are on the ladder.
        Check(multiStack >= 3, "real ladder covers boxes that hold more than one stack of their round");
        Check(stockSpares > 0, "real catalog covers stowed spare magazines of full kits");
        LauncherLooseFixture();
    }

    // A spare is loaded the way the game loads a magazine: full, in stacks of at most the
    // round's stack size. One stack of min(capacity, stack size) once handed every 100-round
    // box of a 40- or 60-stack round out short ("60/100" on an RPD kit's spare, 2026-11-10).
    private static void SpareMagazinesFull(IEnumerable<Magazine> spares, string where)
    {
        foreach (var spare in spares)
        {
            Check(spare.Cartridges.Count == spare.MaxCount, "spare magazines are loaded to capacity: " + where);
            Check(spare.Cartridges.Items.All(i => i.StackObjectsCount >= 1 && i.StackObjectsCount <= Math.Max(1, i.StackMaxSize)),
                "spare magazine stacks never exceed the round's stack size: " + where);
        }
    }

    private static void LooseStacksWithinStackSize(IEnumerable<Item> loose, string where)
        => Check(loose.All(i => i.StackObjectsCount <= Math.Max(1, i.StackMaxSize)),
            "loose reserve stacks never exceed the round's stack size: " + where);

    // The MSGL's cylinder holds six 40 mm grenades of a round that stacks to 1, the one
    // primary weapon whose loose share (half of six per requested stack) is more than a stack.
    // The share is cut into single grenades, for a weapon swap and for a full kit alike, and
    // the total stays the halved reserve.
    private static void LauncherLooseFixture()
    {
        const string launcher = "6275303a9f372d6ea97f9ec7", cylinderTpl = "627bce33f21bc425b06ab967", grenade = "5ede474b0c226a66f5402622";
        var nodes = new List<KitItemNode>
        {
            new() { Id = "equipment", Tpl = "55d7217a4bdc2d86028b456d" },
            new() { Id = "launcher", Tpl = launcher, ParentId = "equipment", SlotId = "FirstPrimaryWeapon" },
            new() { Id = "cylinder", Tpl = cylinderTpl, ParentId = "launcher", SlotId = "mod_magazine" },
        };
        for (int c = 0; c < 6; c++)
            nodes.Add(new() { Id = "round" + c, Tpl = grenade, ParentId = "cylinder", SlotId = $"camora_{c:000}" });
        var kit = new KitDefinition { Id = "msgl-fixture", Items = nodes, Fill = new() { SpareMagazines = 3, RigContents = [] } };

        var weapon = KitBuilder.BuildPrimaryWeapon(kit, 3, out var spares);
        var cylinder = weapon.Descendants().OfType<Magazine>().Single();
        Check(cylinder.MaxCount == 6 && cylinder.ReloadMagType == Weapon.EReloadMode.InternalMagazine
              && cylinder.Slots.Count(s => s.ContainedItem?.TemplateId == grenade) == 6, "MSGL fixture is a loaded six-camora internal cylinder");
        Check(spares.Count > 0 && spares.All(i => i.TemplateId == grenade) && spares.Sum(i => i.StackObjectsCount) == (6 * 3 + 1) / 2,
            "launcher weapon swap halves total loose reserve");
        LooseStacksWithinStackSize(spares, "weapon swap MSGL");

        var backpack = Grid(6, 6);
        RunFill(Equipment(null, null, backpack), kit, new() { { "cylinder", cylinder } });
        Check(backpack.Items.Count > 0 && backpack.Items.All(i => i.TemplateId == grenade) && backpack.Items.Sum(i => i.StackObjectsCount) == (6 * 3 + 1) / 2,
            "full launcher kit halves total loose reserve");
        LooseStacksWithinStackSize(backpack.Items, "full kit MSGL");
    }
}
