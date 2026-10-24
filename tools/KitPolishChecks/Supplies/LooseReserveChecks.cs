using ArenaMode.Kits;
using EFT.InventoryLogic;

internal static partial class Program
{
    private static void LooseReserveChecks(List<KitDefinition> kits, List<KitDefinition> ladder)
    {
        int fixedKits = 0, oddStacks = 0, detachable = 0;
        foreach (var kit in kits)
        {
            var built = Build(kit);
            var weapons = built.Descendants().OfType<Weapon>().ToArray();
            var magazine = weapons.SelectMany(w => w.Descendants()).OfType<Magazine>()
                .FirstOrDefault(m => m.Cartridges.Items.Any());
            if (magazine?.ReloadMagType != Weapon.EReloadMode.InternalMagazine) continue;
            fixedKits++;
            var loaded = magazine.Cartridges.Items.First();
            var inWeapons = weapons.SelectMany(w => w.Descendants()).ToHashSet();
            var loose = built.Descendants().Where(i => i.TemplateId == loaded.TemplateId && !inWeapons.Contains(i)).ToArray();
            var expected = (Math.Max(magazine.MaxCount, loaded.StackMaxSize) * kit.Fill.SpareMagazines + 1) / 2;
            Check(loose.Sum(i => i.StackObjectsCount) == expected, "full kit halves total loose reserve: " + kit.Id);
            Check(loose.All(i => i.StackObjectsCount > 0), "no empty loose ammo stacks");
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
                Check(spares.Cast<Magazine>().All(m => m.Cartridges.Items.Sum(i => i.StackObjectsCount) == Math.Max(1, Math.Min(m.MaxCount, loaded.StackMaxSize))), "detachable magazines stay full");
            }
        }
        Check(fixedKits >= 3 && oddStacks > 0 && detachable > 0, "real catalog covers fixed, odd-stack and detachable ammo controls");
    }
}
