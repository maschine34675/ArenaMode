using System.Text.Json.Nodes;
using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ArenaMode.Tests;

/// <summary>
/// ArenaPresetGuard: one reason per defect on a small hand-made database, and the proof on the installed
/// vanilla database that it rejects nothing the generator can build. A defect that is not asserted by its exact
/// reason here is not a rule (docs/reviews/WEAPON-VARIANTS-REVIEW.md, "Preset guard").
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaPresetGuardTests
{
    // Templates of the hand-made database. Ids are 24-hex so MongoId accepts them.
    private static readonly MongoId Weapon = new("a00000000000000000000001");
    private static readonly MongoId Barrel = new("a00000000000000000000002");
    private static readonly MongoId Muzzle = new("a00000000000000000000003");
    private static readonly MongoId Magazine = new("a00000000000000000000004");
    private static readonly MongoId Ammo = new("a00000000000000000000005");
    private static readonly MongoId Other = new("a00000000000000000000006");
    private static readonly MongoId Unknown = new("ffffffffffffffffffffffff");

    private const string P1 = "b00000000000000000000001", P2 = "b00000000000000000000002", P3 = "b00000000000000000000003",
        P4 = "b00000000000000000000004", P5 = "b00000000000000000000005", P6 = "b00000000000000000000006",
        P7 = "b00000000000000000000007", P8 = "b00000000000000000000008", P9 = "b00000000000000000000009";

    private static Slot S(string name, bool required = false) => new() { Name = name, Required = required };

    private static TemplateItem T(MongoId id, IEnumerable<Slot>? slots = null, IEnumerable<Slot>? chambers = null, IEnumerable<Slot>? cartridges = null)
        => new()
        {
            Id = id,
            Name = "t",
            Parent = new MongoId("5422acb9af1c889c16000029"),
            Properties = new TemplateItemProperties { Slots = slots ?? [], Chambers = chambers ?? [], Cartridges = cartridges ?? [] },
        };

    /// <summary>
    /// The weapon requires its barrel, the barrel requires its muzzle; the magazine takes a stack of cartridges
    /// and the weapon has a chamber. (A weapon's own slot names are not what matters: the shape is.)
    /// </summary>
    private static Dictionary<MongoId, TemplateItem> Database() => new()
    {
        [Weapon] = T(Weapon, [S("mod_barrel", true), S("mod_stock"), S("mod_magazine")], [S("patron_in_weapon")]),
        [Barrel] = T(Barrel, [S("mod_muzzle", true)]),
        [Muzzle] = T(Muzzle),
        [Magazine] = T(Magazine, cartridges: [S("cartridges")]),
        [Ammo] = T(Ammo),
        [Other] = T(Other),
    };

    private static Item I(string id, MongoId tpl, string? parent, string? slot)
        => new() { Id = new MongoId(id), Template = tpl, ParentId = parent, SlotId = slot };

    private static Preset Preset(MongoId? encyclopedia, params Item[] items)
        => new() { Id = new MongoId("c00000000000000000000001"), Name = "p", Parent = new MongoId("c00000000000000000000002"), Items = [.. items], Encyclopedia = encyclopedia };

    /// <summary>A complete, buildable preset: required parts filled, a chamber round and a repeated stack slot.</summary>
    private static Item[] Complete() =>
    [
        I(P1, Weapon, null, null),
        I(P2, Barrel, P1, "mod_barrel"),
        I(P3, Muzzle, P2, "mod_muzzle"),
        I(P4, Magazine, P1, "mod_magazine"),
        I(P5, Ammo, P1, "patron_in_weapon"),   // a chamber slot, not in Slots
        I(P6, Ammo, P4, "cartridges"),
        I(P7, Ammo, P4, "cartridges"),         // the same stack slot again: legitimate, vanilla saiga12_nerf does it 20 times
    ];

    public static IEnumerable<object?[]> Cases()
    {
        object?[] Row(string name, Func<Preset> preset, string? reason) => [name, preset, reason];

        yield return Row("complete preset with chamber and repeated stack slot", () => Preset(null, Complete()), null);
        yield return Row("encyclopedia equal to the root template", () => Preset(Weapon, Complete()), null);
        yield return Row("upper-case parent ids are accepted", () => Preset(null, Complete().Select(i => i.ParentId == null ? i : I(i.Id.ToString(), i.Template, i.ParentId.ToUpperInvariant(), i.SlotId)).ToArray()), null);

        yield return Row("null item list", () => new Preset { Id = new MongoId("c00000000000000000000001"), Name = "p", Parent = new MongoId("c00000000000000000000002"), Items = null! }, "no-parts");
        yield return Row("empty item list", () => Preset(null), "no-parts");
        yield return Row("null element", () => new Preset { Id = new MongoId("c00000000000000000000001"), Name = "p", Parent = new MongoId("c00000000000000000000002"), Items = [I(P1, Weapon, null, null), null!] }, "no-parts");

        yield return Row("no parentless node", () => Preset(null, I(P1, Weapon, P1, "mod_barrel")), "no-root");
        yield return Row("two parentless nodes", () => Preset(null, [.. Complete(), I(P8, Other, null, null)]), "several-roots");
        yield return Row("an empty parent id is not a root", () => Preset(null, [.. Complete(), I(P8, Other, "", "mod_stock")]), $"bad-parent:{Other}");

        yield return Row("duplicate part id", () => Preset(null, [.. Complete(), I(P2, Other, P1, "mod_stock")]), $"duplicate-id:{P2}");

        yield return Row("parent id that is not 24-hex", () => Preset(null, [.. Complete(), I(P8, Other, "zz", "mod_stock")]), $"bad-parent:{Other}");
        yield return Row("child without a slot id", () => Preset(null, [.. Complete(), I(P8, Other, P1, null)]), $"no-slot:{Other}");
        yield return Row("parent id that names no part", () => Preset(null, [.. Complete(), I(P8, Other, P9, "mod_stock")]), $"orphan:{Other}");
        yield return Row("two parts that only name each other", () => Preset(null, [.. Complete(), I(P8, Other, P9, "mod_stock"), I(P9, Other, P8, "mod_stock")]), $"cycle:{Other}");
        yield return Row("a part that names itself as parent", () => Preset(null, [.. Complete(), I(P8, Other, P8, "mod_stock")]), $"cycle:{Other}");

        yield return Row("encyclopedia names another weapon", () => Preset(Barrel, Complete()), $"root-mismatch:{Barrel}!={Weapon}");

        yield return Row("a part whose template is not in the database", () => Preset(null, [.. Complete(), I(P8, Unknown, P1, "mod_stock")]), $"unknown-template:{Unknown}");

        yield return Row("slot the parent does not have", () => Preset(null, [.. Complete(), I(P8, Other, P1, "mod_gone")]), $"slot-missing:mod_gone on {Weapon}");
        yield return Row("slot id differing from the parent's only in case", () => Preset(null, [.. Complete(), I(P8, Other, P1, "MOD_STOCK")]), $"slot-missing:MOD_STOCK on {Weapon}");
        yield return Row("slot only a chamber offers", () => Preset(null, [.. Complete().Where(i => i.Id.ToString() != P5), I(P8, Ammo, P1, "patron_in_weapon")]), null);

        yield return Row("required slot of the root left empty", () => Preset(null, I(P1, Weapon, null, null)), $"required-empty:mod_barrel on {Weapon}");
        yield return Row("required slot of a deeper part left empty", () => Preset(null, Complete().Where(i => i.Id.ToString() != P3).ToArray()), $"required-empty:mod_muzzle on {Barrel}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ProblemNamesTheDefectOrNothing(string name, Func<Preset> preset, string? reason)
    {
        var actual = ArenaPresetGuard.Problem(preset(), Database());

        // The row name is in the message: a Func does not enumerate into the test name.
        Assert.True(Equals(reason, actual), $"{name}: expected {reason ?? "<accepted>"}, got {actual ?? "<accepted>"}");
    }

    [SkippableFact]
    public void GuardRejectsNoVanillaWeaponPreset()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var items = WeaponPoolFixture.LoadTemplates();
        var presets = WeaponPoolFixture.LoadPresets();

        var weaponPresets = presets.Values.Where(p => WeaponPoolFixture.RootOf(p) is { } root && WeaponPoolFixture.IsWeapon(items, root)).ToList();
        var weapons = weaponPresets.Select(p => WeaponPoolFixture.RootOf(p)!.Value).Distinct().Count();

        // Floors: an empty or half-read database must not pass for "no rejects". Measured on SPT 4.1: 272 / 148.
        Assert.True(weaponPresets.Count >= 250, $"only {weaponPresets.Count} weapon presets were loaded");
        Assert.True(weapons >= 140, $"only {weapons} weapons were loaded");

        var rejected = weaponPresets
            .Select(p => (p.Name, Reason: ArenaPresetGuard.Problem(p, items)))
            .Where(x => x.Reason != null)
            .Select(x => $"{x.Name}: {x.Reason}")
            .ToList();
        // If a future vanilla database fails here the rule is wrong and must be narrowed, not this test relaxed.
        Assert.True(rejected.Count == 0, "vanilla weapon presets rejected: " + string.Join("; ", rejected));
    }

    /// <summary>
    /// Evidence helper, not an assertion about the data: when ARENA_POOL_HASH_OUT names a file, the SHA-256 of
    /// the normalised vanilla pool and of the two database files it was built from are written there. It was
    /// run once on the generator before the preset guard existed and once after, to show the guard changed
    /// nothing on vanilla data. The hash is not pinned: it depends on the SPT data version.
    /// </summary>
    [SkippableFact]
    public async Task RecordTheNormalisedVanillaPoolHash()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var run = await WeaponPoolFixture.GenerateAsync(WeaponPoolFixture.LoadTemplates(), WeaponPoolFixture.LoadPresets());
        var pool = run.Pool;

        // The shipped fallback is a one-entry array with no items: a pool that really was generated is large.
        Assert.True(pool.Count >= 100, $"the generator did not produce a pool ({pool.Count} entries)");

        var normalised = WeaponPoolFixture.Normalise(run.Json);
        var report = $"pool entries: {pool.Count}{Environment.NewLine}"
                     + $"normalised pool sha256: {WeaponPoolFixture.Sha256(normalised)}{Environment.NewLine}"
                     + $"items.json sha256: {WeaponPoolFixture.Sha256File(WeaponPoolFixture.ItemsPath)}{Environment.NewLine}"
                     + $"globals.json sha256: {WeaponPoolFixture.Sha256File(WeaponPoolFixture.GlobalsPath)}{Environment.NewLine}";

        if (Environment.GetEnvironmentVariable("ARENA_POOL_HASH_OUT") is { Length: > 0 } outPath)
            File.WriteAllText(outPath, report);
    }
}
