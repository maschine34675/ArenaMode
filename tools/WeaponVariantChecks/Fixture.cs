using System.Text.Json.Nodes;

static class Fixture
{
    public const string Gun = "111111111111111111111111", Barrel = "111111111111111111111112", Mount = "111111111111111111111113";
    public const string Dot = "111111111111111111111114", Scope = "111111111111111111111115", LongScope = "111111111111111111111116";
    public const string Thermal = "111111111111111111111117", Grip = "111111111111111111111118", Laser = "111111111111111111111119";
    public const string Equipment = "55d7217a4bdc2d86028b456d";
    private static JsonObject Slot(string name, bool required, params string[] allowed)
        => new() { ["_name"] = name, ["_required"] = required, ["_props"] = new JsonObject
            { ["filters"] = new JsonArray(new JsonObject { ["Filter"] = new JsonArray(allowed.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["ExcludedFilter"] = new JsonArray() }) } };
    private static JsonObject Item(string parent, double zoom = 1, params JsonObject[] slots)
        => new() { ["_parent"] = parent, ["_type"] = "Item", ["_name"] = "fixture", ["_props"] = new JsonObject
            { ["Slots"] = new JsonArray(slots.Cast<JsonNode?>().ToArray()), ["Weight"] = 0.1, ["QuestItem"] = false,
                ["Prefab"] = new JsonObject { ["path"] = "test.bundle" }, ["Zooms"] = new JsonArray(new JsonArray(zoom)), ["ConflictingItems"] = new JsonArray() } };
    public static JsonObject Database()
    {
        var db = new JsonObject
        {
            [Equipment] = Item(""),
            [Gun] = Item("5422acb9af1c889c16000029", 1, Slot("mod_barrel", true, Barrel), Slot("mod_mount", false, Mount),
                Slot("mod_foregrip", false, Grip), Slot("mod_tactical", false, Laser)),
            [Barrel] = Item("barrel"),
            [Mount] = Item("55818b224bdc2dde698b456f", 1, Slot("mod_scope", true, Dot, Scope, LongScope, Thermal)),
            [Dot] = Item("55818ad54bdc2ddc698b4569"), [Scope] = Item("55818add4bdc2d5b648b456f", 4),
            [LongScope] = Item("55818ae44bdc2dde698b456c", 20), [Thermal] = Item("55818aeb4bdc2ddc698b456a", 2),
            [Grip] = Item("55818af64bdc2d5b648b4570"), [Laser] = Item("55818b164bdc2ddc698b456c"),
        };
        foreach (string id in new[] { "5422acb9af1c889c16000029", "55818b224bdc2dde698b456f", "55818ad54bdc2ddc698b4569", "55818add4bdc2d5b648b456f", "55818ae44bdc2dde698b456c", "55818aeb4bdc2ddc698b456a", "55818af64bdc2d5b648b4570", "55818b164bdc2ddc698b456c" })
            db[id] = new JsonObject { ["_parent"] = "", ["_type"] = "Node", ["_name"] = "category", ["_props"] = new JsonObject() };
        return db;
    }
    public static JsonObject Node(string id, string tpl, string parent, string slot)
        => new() { ["_id"] = id, ["_tpl"] = tpl, ["parentId"] = parent, ["slotId"] = slot };
    public static JsonObject Entry() => new()
    {
        ["id"] = "fixture", ["price"] = 123, ["order"] = 7, ["custom"] = "preserve",
        ["items"] = new JsonArray(new JsonObject { ["_id"] = "equipment", ["_tpl"] = Equipment },
            Node("weapon", Gun, "equipment", "FirstPrimaryWeapon"), Node("barrel", Barrel, "weapon", "mod_barrel")),
    };
}
