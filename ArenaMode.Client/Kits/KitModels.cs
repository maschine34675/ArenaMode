// Linked into the test project, which compiles with nullable enabled; these
// are JSON shapes with no constructor, so the annotations are switched off
// here rather than pretending every field is always set.
#nullable disable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// A kit is an SPT preset-style flat item tree (the DefaultEquipmentPreset shape —
/// covers arbitrary mod trees, armor plates, chambered rounds and upd variants
/// natively) plus a small declarative fill section for grid contents, which the
/// preset shape cannot express.
/// </summary>
public class KitDefinition
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("name")] public string Name;

    /// <summary>
    /// What this kit costs in a mode with an economy, in roubles. Filled in
    /// by the server from the handbook sum unless the kit names its own, so a
    /// hand-written kit is priced by its contents like every other.
    /// </summary>
    [JsonProperty("price")] public int Price;
    [JsonProperty("order")] public int Order;
    [JsonProperty("items")] public List<KitItemNode> Items = new List<KitItemNode>();
    [JsonProperty("fill")] public KitFill Fill = new KitFill();
}

public class KitItemNode
{
    /// <summary>Template-local id — NEVER used as a live item id; the builder generates
    /// fresh ids per instantiation and remaps parent references.</summary>
    [JsonProperty("_id")] public string Id;

    [JsonProperty("_tpl")] public string Tpl;
    [JsonProperty("parentId")] public string ParentId;
    [JsonProperty("slotId")] public string SlotId;
    [JsonProperty("upd")] public JObject Upd;
}

public class KitFill
{
    /// <summary>Extra loaded magazines for the primary weapon, placed in rig/pockets.</summary>
    [JsonProperty("spareMagazines")] public int SpareMagazines = 3;

    [JsonProperty("rigContents")] public List<KitStackedItem> RigContents = new List<KitStackedItem>();
}

public class KitStackedItem
{
    [JsonProperty("tpl")] public string Tpl;
    [JsonProperty("count")] public int Count = 1;

    /// <summary>Optional Arena grenade: add after authored supplies, only to
    /// native grenade-throwing containers; lack of space is an expected skip.</summary>
    [JsonProperty("optionalQuickUseOnly")] public bool OptionalQuickUseOnly;
}
