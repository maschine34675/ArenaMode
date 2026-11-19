using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ArenaMode.Server.Services;

/// <summary>
/// Decides whether one weapon preset of the item database can be turned into a
/// working Gun Game rung by <see cref="LadderGenerator"/>, and if not, says why in
/// a short stable reason. Pure: it reads the preset and the template table, logs
/// nothing and keeps no state.
///
/// Why it exists. LadderGenerator builds the whole pool inside one try block, so
/// one preset it cannot build used to cost the whole pool: the server logged
/// "Weapon pool generation failed" and served the 24-entry shipped ladder instead
/// of about 140 weapons. A weapon mod that changes a weapon's slots without
/// repairing the vanilla preset is enough. Worse, some defects do not throw at
/// all: the client builds such a preset as an incomplete gun (an AK without gas
/// tube and handguard) and only logs a warning. A weapon mod author reported
/// both. Every rule below therefore maps to a throw site of LadderGenerator.Build
/// or BuildEntry, or to a build the client demonstrably cannot complete; a rule
/// that does neither does not belong here.
///
/// Why it runs on the RAW preset, before the generator keeps one preset per
/// weapon (Plainer). A check after Plainer would lose a weapon whenever the
/// plainest preset is the broken one although another preset of it is fine.
///
/// The rules, in the order they are applied (the first failing one is returned):
///  1. no-parts          Items is null, empty or holds a null element: Build and BuildEntry would dereference it.
///  2. no-root / several-roots
///                       BuildEntry takes `First(i => i.ParentId == null)` (InvalidOperationException) and
///                       turns EVERY parentless node into a FirstPrimaryWeapon. Only null counts as "no
///                       parent": "" is not a root, it is a parent id that points nowhere (rule 4).
///  3. duplicate-id      BuildEntry's ToDictionary over the part ids throws ArgumentException.
///  4. bad-parent / no-slot / orphan
///                       BuildEntry resolves `remap[node.ParentId]`: a parent id that is not 24-hex
///                       throws ArgumentException, one that names no part of the preset throws
///                       KeyNotFoundException. A part without a SlotId is written without a slot and
///                       the client cannot attach it.
///  5. cycle / orphan    every part must reach the root by walking its parent ids upwards; two parts that
///                       name each other pass rule 4 and are still an island the client never builds.
///  6. root-mismatch     the generator keys the weapon by `preset.Encyclopedia` when it is set (the rung's
///                       name, the calibre, the ammunition, the chamber and the sight rule all follow that
///                       template) but BuildEntry builds the gun from the root NODE. If the two differ the
///                       rung is named and loaded for one weapon and built as another: mislabelled, with
///                       the wrong calibre's rounds. Vanilla has none (every one of its 148 presets with an
///                       Encyclopedia agrees with its root node).
///  7. unknown-template  a part whose template is not in the database: the client drops the whole rung
///                       (GunGameMode.BuildLadder, "left out - unknown templates"), so the weapon would be
///                       counted in the pool and never appear.
///  8. slot-missing      the part's SlotId must exist on its parent's template, in Slots, Chambers or
///                       Cartridges (vanilla puts 28 children into chamber and cartridge slots). The client
///                       skips a part whose slot is gone (KitBuilder: "no slot ... on ...") and builds an
///                       incomplete gun.
///  9. required-empty    every required entry of a part's `Slots` (the root included) must have a child. Only
///                       `Slots`: no vanilla Chambers/Cartridges entry is required, and a magazine is filled
///                       by the generator itself.
///
/// Parent ids are compared as MongoId, not as text: MongoId is case-insensitive, and BuildEntry's own
/// `remap[...]` lookup goes through it, so a preset with an upper-case parent id builds today and must
/// not be rejected. SlotId stays an ordinal string: the client and ArenaWeaponVariants compare slot ids
/// with `==`.
///
/// Deliberately NOT here, each for a reason:
///  - a duplicate-slot rule: stack slots legitimately repeat one slotId (vanilla saiga12_nerf holds 20
///    `cartridges` nodes under one magazine, ArenaWeaponVariants.TryValidate exempts stack slots too);
///  - a slot FILTER rule: the client attaches with AddWithoutRestrictions, and category-style filters need
///    ArenaWeaponVariants' native-category semantics; it stays a `base-invalid` note of that class;
///  - a part-count cap (vanilla maximum 36, the variant layer's cap is 180);
///  - ammunition, calibre, magazine and handbook rules: Consider and IndexAmmo already own them;
///  - leaving a whole mod out, or the Pack-a-Punch profile comparison: another mod's features.
///
/// All rules are O(parts squared) at worst and read only. On the installed vanilla database none of the
/// 272 weapon presets is rejected (ArenaPresetGuardTests, GuardRejectsNoVanillaWeaponPreset).
/// </summary>
internal static class ArenaPresetGuard
{
    /// <summary>Null when the preset can be built from this database; otherwise the reason (see the class comment).</summary>
    internal static string? Problem(Preset preset, IReadOnlyDictionary<MongoId, TemplateItem> templates)
    {
        var items = preset.Items;
        if (items is null || items.Count == 0 || items.Any(i => i is null))
            return "no-parts";

        Item? root = null;
        foreach (var item in items)
        {
            if (item.ParentId != null)
                continue;

            if (root != null)
                return "several-roots";

            root = item;
        }

        if (root == null)
            return "no-root";

        var ids = new Dictionary<MongoId, Item>();
        foreach (var item in items)
        {
            if (!ids.TryAdd(item.Id, item))
                return "duplicate-id:" + item.Id;
        }

        // Parent links by MongoId, the type BuildEntry keys its remap with.
        var parentOf = new Dictionary<MongoId, MongoId>();
        foreach (var item in items)
        {
            if (ReferenceEquals(item, root))
                continue;

            if (item.SlotId == null)
                return "no-slot:" + item.Template;

            if (!MongoId.IsValidMongoId(item.ParentId!))
                return "bad-parent:" + item.Template;

            var parent = new MongoId(item.ParentId!);
            if (!ids.ContainsKey(parent))
                return "orphan:" + item.Template;

            parentOf[item.Id] = parent;
        }

        // Every part must reach the root. `reaching` collects what is known to, so each part is walked once
        // overall; a repeat inside one walk is a cycle.
        var reaching = new HashSet<MongoId> { root.Id };
        foreach (var item in items)
        {
            var visited = new HashSet<MongoId>();
            var current = item.Id;
            while (!reaching.Contains(current))
            {
                if (!visited.Add(current))
                    return "cycle:" + item.Template;

                current = parentOf[current];
            }

            reaching.UnionWith(visited);
        }

        if (preset.Encyclopedia is { } encyclopedia && encyclopedia != root.Template)
            return $"root-mismatch:{encyclopedia}!={root.Template}";

        foreach (var item in items)
        {
            if (!templates.ContainsKey(item.Template))
                return "unknown-template:" + item.Template;
        }

        var slotNames = new Dictionary<MongoId, HashSet<string>>();
        foreach (var item in items)
        {
            if (ReferenceEquals(item, root))
                continue;

            var parentTemplate = ids[parentOf[item.Id]].Template;
            if (!slotNames.TryGetValue(parentTemplate, out var names))
                slotNames[parentTemplate] = names = SlotNames(templates[parentTemplate]);

            if (!names.Contains(item.SlotId!))
                return $"slot-missing:{item.SlotId} on {parentTemplate}";
        }

        var filled = new HashSet<(MongoId Parent, string Slot)>();
        foreach (var item in items)
        {
            if (!ReferenceEquals(item, root))
                filled.Add((parentOf[item.Id], item.SlotId!));
        }

        foreach (var item in items)
        {
            foreach (var slot in templates[item.Template].Properties?.Slots ?? [])
            {
                if (slot.Required == true && slot.Name != null && !filled.Contains((item.Id, slot.Name)))
                    return $"required-empty:{slot.Name} on {item.Template}";
            }
        }

        return null;
    }

    private static HashSet<string> SlotNames(TemplateItem template)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var props = template.Properties;
        foreach (var slot in (props?.Slots ?? []).Concat(props?.Chambers ?? []).Concat(props?.Cartridges ?? []))
        {
            if (slot.Name != null)
                names.Add(slot.Name);
        }

        return names;
    }
}
