using ArenaMode.Kits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Offline checks of the custom-kit rules (docs/reviews/CUSTOM-KITS-20261110.md): ids, the cap, the
// per-raid hold, the structure check with its price-loophole pins, the tree edits, the kit file and
// the candidate rule. Production sources are linked; Verify.ps1 runs this once as it is and once per
// mutant, each of which must fail its named assertion.
int checks = 0;
void Need(bool ok, string reason) { checks++; if (!ok) throw new Exception("FAIL: " + reason); }

string repo = AppContext.BaseDirectory;
while (repo != null && !File.Exists(Path.Combine(repo, "AGENTS.md"))) repo = Path.GetDirectoryName(repo);
if (repo == null) throw new Exception("repository root not found");
var stock = JsonConvert.DeserializeObject<List<KitDefinition>>(File.ReadAllText(Path.Combine(repo, "ArenaMode.Server", "defaults", "kits.json")));
var envelope = CustomKitEnvelope.FromStock(stock);
int counter = 0;
string Ids() => (++counter).ToString("x24");

// --- ids
Need(ArenaCustomKitId.IsWellFormed(ArenaCustomKitId.NewId(_ => false)), "a new id is well formed");
string fixedId = ArenaCustomKitId.NewId(_ => false, () => new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef });
Need(fixedId == "custom-0123456789abcdef", "id is the prefix and sixteen lower-case hex digits");
Need(ArenaKitFavorites.IsKitId(fixedId), "a custom id is a storable kit id");
int draws = 0;
string second = ArenaCustomKitId.NewId(id => id == fixedId, () => draws++ == 0
    ? new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef } : new byte[] { 0xff, 0, 0, 0, 0, 0, 0, 1 });
Need(second == "custom-ff00000000000001" && draws == 2, "a taken id is drawn again");
bool exhausted = false;
try { ArenaCustomKitId.NewId(_ => true); } catch (InvalidOperationException) { exhausted = true; }
Need(exhausted, "id generation gives up instead of looping");
Need(!ArenaCustomKitId.IsWellFormed("custom-0123456789ABCDEF") && !ArenaCustomKitId.IsWellFormed("custom-0123456789abcde")
     && !ArenaCustomKitId.IsWellFormed("kit-0123456789abcdef0") && !ArenaCustomKitId.IsWellFormed(null), "only the exact id shape is well formed");

// --- the cap
Need(ArenaCustomKitPolicy.StockCap(new[] { 180000, 0, -5, 669000, 251000 }) == 669000, "the cap is the dearest stock price");
Need(ArenaCustomKitPolicy.StockCap(new int[0]) == 0 && ArenaCustomKitPolicy.StockCap(null) == 0, "no stock price, no cap");
Need(ArenaCustomKitPolicy.Eligible(CustomKitState.Ready, 669000, 669000), "a kit at the cap is issued");
Need(!ArenaCustomKitPolicy.Eligible(CustomKitState.Ready, 669001, 669000), "a kit above the cap is not issued");
Need(!ArenaCustomKitPolicy.Eligible(CustomKitState.Ready, 1000, 0) && !ArenaCustomKitPolicy.Eligible(CustomKitState.Ready, 0, 669000),
     "an unknown cap or price refuses");
Need(!ArenaCustomKitPolicy.Eligible(CustomKitState.Unavailable, 1000, 669000) && !ArenaCustomKitPolicy.Eligible(CustomKitState.NoPrice, 1000, 669000),
     "only a ready kit is issued");
Need(ArenaCustomKitPolicy.Sellable(CustomKitState.Ready, 900000) && !ArenaCustomKitPolicy.Sellable(CustomKitState.Ready, 0)
     && !ArenaCustomKitPolicy.Sellable(CustomKitState.Unavailable, 1000), "solo Tactical sells any ready priced kit, cap or not");
Need(ArenaCustomKitPolicy.PickerBlock(CustomKitState.Ready, null, 669000, 669000) == null
     && ArenaCustomKitPolicy.PickerBlock(CustomKitState.Ready, null, 700000, 669000)?.Contains("solo Tactical") == true
     && ArenaCustomKitPolicy.PickerBlock(CustomKitState.Unavailable, "item X missing", 1, 2) == "item X missing"
     && ArenaCustomKitPolicy.PickerBlock(CustomKitState.NoPrice, null, 0, 2)?.Contains("handbook") == true, "the picker says why a kit is blocked");
Need(ArenaCustomKitPolicy.ShopBlock(CustomKitState.Ready, null, 900000) == null && ArenaCustomKitPolicy.ShopBlock(CustomKitState.NoPrice, null, 0) != null,
     "the shop blocks only unusable or unpriced kits");

// --- the per-raid hold
var hold = new ArenaRaidHold<string>();
int reads = 0;
string Live(string value) { reads++; return value; }
Need(hold.Resolve(false, 1, () => Live("menu")) == "menu" && hold.Resolve(false, 1, () => Live("edited")) == "edited", "the menu reads live");
Need(hold.Resolve(true, 1, () => Live("armed")) == "armed", "arming takes the live value");
reads = 0;
Need(hold.Resolve(true, 1, () => Live("changed")) == "armed" && reads == 0, "an armed raid keeps its value without reading again");
Need(hold.Resolve(true, 2, () => Live("next")) == "next", "a new arm reads again");
Need(hold.Resolve(false, 2, () => Live("menu again")) == "menu again" && hold.Resolve(true, 2, () => Live("re-armed")) == "re-armed",
     "leaving the raid releases the hold");
var picky = new ArenaRaidHold<string>();
Need(picky.Resolve(true, 5, () => "early", value => value != "early") == "early"
     && picky.Resolve(true, 5, () => "loaded", value => value != "early") == "loaded"
     && picky.Resolve(true, 5, () => "late", value => value != "early") == "loaded", "a value the hold refuses is answered but not held");

// --- structure
KitDefinition Copy(KitDefinition source, string name = "My kit") => CustomKitEditing.CopyOf(source, ArenaCustomKitId.NewId(_ => false), name, Ids);
Need(stock.Count == 20 && stock.All(kit => CustomKitStructure.Validate(Copy(kit), envelope).Count == 0), "a copy of every stock kit is sound");
Need(envelope.MaxSpareMagazines == 5 && envelope.MaxRigEntries == 2 && envelope.MaxRigCount == 1 && envelope.SecuredContainers.Count == 1,
     "the envelope comes from the stock kits");
var basis = Copy(stock.First(kit => kit.Fill?.RigContents?.Count > 0));
string rootId = CustomKitStructure.RootId(basis);
KitItemNode Node(KitDefinition kit, string slot) => kit.Items.First(n => n.ParentId == rootId && n.SlotId == slot);
List<string> Broken(Action<KitDefinition> damage) { var kit = CustomKitEditing.Clone(basis); damage(kit); return CustomKitStructure.Validate(kit, envelope); }
bool Says(List<string> problems, string part) => problems.Any(p => p.Contains(part));
Need(Says(Broken(k => k.Items.Add(new KitItemNode { Id = Ids(), Tpl = CustomKitStructure.EquipmentTemplate })), "not exactly one root"), "two roots are refused");
Need(Says(Broken(k => k.Items.Add(new KitItemNode { Id = Ids(), Tpl = stock[0].Items[1].Tpl, ParentId = "ffffffffffffffffffffffff", SlotId = "mod_x" })), "missing parent"), "an orphan is refused");
Need(Says(Broken(k => { var a = Ids(); var b = Ids(); k.Items.Add(new KitItemNode { Id = a, Tpl = stock[0].Items[1].Tpl, ParentId = b, SlotId = "x" }); k.Items.Add(new KitItemNode { Id = b, Tpl = stock[0].Items[1].Tpl, ParentId = a, SlotId = "y" }); }), "own ancestor"),
     "a cycle is refused");
Need(Says(Broken(k => k.Items.Add(new KitItemNode { Id = Ids(), Tpl = Node(k, "Earpiece").Tpl, ParentId = rootId, SlotId = "Earpiece" })), "holds two items"), "a slot with two items is refused");
Need(Broken(k => { var stack = k.Items.First(n => n.SlotId == "cartridges"); k.Items.Add(new KitItemNode { Id = Ids(), Tpl = stack.Tpl, ParentId = stack.ParentId, SlotId = "cartridges", Upd = new JObject { ["StackObjectsCount"] = 20 } }); }).Count == 0,
     "a magazine holds several stacks");
Need(Says(Broken(k => { for (int i = 0; i < 400; i++) k.Items.Add(new KitItemNode { Id = Ids(), Tpl = Node(k, "Earpiece").Tpl, ParentId = Node(k, "Backpack").Id, SlotId = "main" + i }); }), "more than"),
     "an oversized kit is refused");
Need(Says(Broken(k => k.Id = "spartan"), "not a custom kit id"), "a stock id is refused");
Need(Says(Broken(k => Node(k, "Earpiece").SlotId = " "), "has no slot"), "a blank slot is refused");
Need(Says(Broken(k => Node(k, "Earpiece").SlotId = "Holster"), "not a kit slot"), "a slot no stock kit uses is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Upd = new JObject { ["Sight"] = new JObject() }), "carries upd"), "an upd key the builder ignores is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Upd = new JObject { ["StackObjectsCount"] = 5 }), "a stack outside a magazine"), "a stack outside a magazine is refused");
Need(Says(Broken(k => k.Items.Add(new KitItemNode { Id = Ids(), Tpl = Node(k, "Earpiece").Tpl, ParentId = Node(k, "SecuredContainer").Id, SlotId = "main" })), "not empty"),
     "a filled secure container is refused");
Need(Says(Broken(k => Node(k, "SecuredContainer").Tpl = "5c093ca986f7740a1867ab12"), "not a stock one"), "another secure container is refused");
Need(Says(Broken(k => Node(k, "Pockets").Tpl = "5af99e9186f7747c447120b8"), "pockets are not stock"), "other pockets are refused");
Need(Says(Broken(k => k.Fill.SpareMagazines = 6), "spare magazines"), "more spare magazines than stock are refused");
Need(Says(Broken(k => k.Fill.RigContents = new List<KitStackedItem> { new() { Tpl = "5449016a4bdc2d6f028b456f", Count = 1 } }), "a supply no stock kit carries"),
     "a supply no stock kit carries is refused");
Need(Says(Broken(k => k.Fill.RigContents[0].Count = 2), "a supply no stock kit carries"), "a supply above the stock count is refused");
Need(Says(Broken(k => { var t = envelope.RigTemplates.First(); k.Fill.RigContents = new List<KitStackedItem> { new() { Tpl = t }, new() { Tpl = t }, new() { Tpl = t } }; }), "more supplies"),
     "more supply entries than stock are refused");
Need(Says(Broken(k => k.Name = "<b>loud</b>"), "name") && Says(Broken(k => k.Name = ""), "name"), "a name with markup or none is refused");
Need(Says(Broken(k => k.Items.First(n => n.Id == rootId).Tpl = "5449016a4bdc2d6f028b456f"), "equipment template"), "a root that is not the equipment is refused");
Need(Says(Broken(k => { var gun = CustomKitEditing.Subtree(k.Items, Node(k, "FirstPrimaryWeapon").Id); k.Items.RemoveAll(n => gun.Contains(n.Id)); }), "primary weapon"), "a kit without a weapon is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Tpl = "not-a-template"), "no valid template"), "a malformed template id is refused");
string upper = Node(basis, "Earpiece").Tpl.ToUpperInvariant();
Need(upper != Node(basis, "Earpiece").Tpl && Says(Broken(k => Node(k, "Earpiece").Tpl = upper), "no valid template"), "an upper-case template id is refused");
Need(Says(Broken(k => k.Items.Add(new KitItemNode { Id = Node(k, "Earpiece").Id, Tpl = Node(k, "Earpiece").Tpl, ParentId = rootId, SlotId = "Headwear" })), "used twice"),
     "a node id used twice is refused");

// The upd values: of the type KitBuilder reads, inside what a stock kit sets.
Need(envelope.MaxDurability == 100 && envelope.FireModes.Count == 1 && envelope.FireModes.Contains("single"), "the envelope reads the stock upd values");
KitItemNode Cartridges(KitDefinition kit) => kit.Items.First(n => n.SlotId == "cartridges");
JObject Upd(string key, JToken value) => new JObject { [key] = value };
Need(Says(Broken(k => Cartridges(k).Upd = Upd("StackObjectsCount", "abc")), "whole number") && Says(Broken(k => Cartridges(k).Upd = Upd("StackObjectsCount", 0)), "whole number"),
     "a stack count that is not a whole number is refused");
Need(Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("Repairable", new JObject { ["Durability"] = 1000000, ["MaxDurability"] = 1000000 })), "durability"),
     "a durability above the stock weapons is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Upd = Upd("Repairable", new JObject { ["Durability"] = 50, ["MaxDurability"] = 100 })), "durability"),
     "a durability on another item is refused");
Need(Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("Repairable", new JObject { ["Durability"] = "x", ["MaxDurability"] = 100 })), "durability")
     && Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("Repairable", new JObject { ["Durability"] = 100, ["MaxDurability"] = 100, ["Spare"] = 1 })), "durability")
     && Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("Repairable", new JObject { ["Durability"] = 100, ["MaxDurability"] = 90 })), "durability"),
     "a durability that is not two fitting numbers is refused");
Need(Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("FireMode", new JObject { ["FireMode"] = "fullauto" })), "fire mode")
     && Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = Upd("FireMode", new JObject { ["FireMode"] = 3 })), "fire mode"), "a fire mode no stock kit sets is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Upd = Upd("Foldable", new JObject { ["Folded"] = "yes" })), "fold state"), "a fold state that is not true or false is refused");
Need(Says(Broken(k => Node(k, "Earpiece").Upd = Upd("MedKit", new JObject { ["HpResource"] = 1000000 })), "carries upd MedKit"), "a medkit resource is refused");
Need(!Says(Broken(k => Node(k, "FirstPrimaryWeapon").Upd = new JObject { ["Repairable"] = new JObject { ["Durability"] = 80, ["MaxDurability"] = 100.0 }, ["FireMode"] = new JObject { ["FireMode"] = "single" } }), "item"),
     "a stock durability and fire mode pass");
Need(CustomKitStructure.SanitizeName("  My\t <b>Best</b>\u0007 Kit  ") == "My bBest/b Kit", "a name is cleaned, not refused, when it is stored");
Need(CustomKitStructure.SanitizeName(new string('x', 50)).Length == CustomKitStructure.MaxNameLength, "a name is cut to 32 characters");

// --- editing
var source = stock.First(k => k.Id == "spartan");
string before = JsonConvert.SerializeObject(source);
var copy = Copy(source, "Spartan (2)");
Need(JsonConvert.SerializeObject(source) == before, "a copy leaves its source untouched");
Need(copy.Items.Count == source.Items.Count && copy.Items.All(n => source.Items.All(s => s.Id != n.Id)) && copy.Price == 0 && copy.Order == 0,
     "a copy has fresh ids and no price or order");
Need(copy.Items.Select(n => n.Tpl + "/" + n.SlotId).OrderBy(x => x).SequenceEqual(source.Items.Select(n => n.Tpl + "/" + n.SlotId).OrderBy(x => x)),
     "a copy keeps the tree");
copy.Items.First(n => n.Upd != null).Upd["probe"] = 1;
Need(JsonConvert.SerializeObject(source) == before, "a copy shares no upd with its source");
copy.Items.First(n => n.Upd != null && n.Upd["probe"] != null).Upd.Remove("probe");
var weapon = CustomKitEditing.RootSlot(copy, "FirstPrimaryWeapon");
var weaponTree = CustomKitEditing.Subtree(copy.Items, weapon.Id);
Need(weaponTree.Count > 5 && weaponTree.Contains(copy.Items.First(n => n.SlotId == "cartridges").Id), "a subtree reaches the cartridges of the magazine");
var otherWeapon = stock.First(k => k.Id == "titan");
var otherRoot = CustomKitStructure.RootId(otherWeapon);
var titanGun = otherWeapon.Items.First(n => n.ParentId == otherRoot && n.SlotId == "FirstPrimaryWeapon");
var gunRows = otherWeapon.Items.Where(n => CustomKitEditing.Subtree(otherWeapon.Items, titanGun.Id).Contains(n.Id))
    .Select(n => new KitItemNode { Id = n.Id, Tpl = n.Tpl, ParentId = n.Id == titanGun.Id ? null : n.ParentId, SlotId = n.SlotId, Upd = n.Upd }).ToList();
string copyBefore = JsonConvert.SerializeObject(copy);
var swapped = CustomKitEditing.ReplaceRootSlot(copy, "FirstPrimaryWeapon", gunRows, Ids);
Need(JsonConvert.SerializeObject(copy) == copyBefore, "a replace leaves its input untouched");
Need(swapped.Items.All(n => !weaponTree.Contains(n.Id)), "a replace removes the whole old subtree");
var newGun = CustomKitEditing.RootSlot(swapped, "FirstPrimaryWeapon");
Need(newGun != null && newGun.Tpl == titanGun.Tpl && CustomKitEditing.Subtree(swapped.Items, newGun.Id).Count == gunRows.Count
     && swapped.Items.All(n => gunRows.All(g => g.Id != n.Id)), "a replace grafts the new subtree with fresh ids under the root");
Need(swapped.Items.Where(n => !CustomKitEditing.Subtree(swapped.Items, newGun.Id).Contains(n.Id)).Select(n => n.Id).OrderBy(x => x)
     .SequenceEqual(copy.Items.Where(n => !weaponTree.Contains(n.Id)).Select(n => n.Id).OrderBy(x => x)), "a replace touches no other slot");
Need(CustomKitStructure.Validate(swapped, envelope).Count == 0, "a swapped kit stays sound");
var cleared = CustomKitEditing.ReplaceRootSlot(copy, "Backpack", null, Ids);
Need(CustomKitEditing.RootSlot(cleared, "Backpack") == null && CustomKitEditing.RootSlot(copy, "Backpack") != null, "an empty subtree clears the slot");
var renamed = CustomKitEditing.Renamed(copy, "Other");
Need(renamed.Name == "Other" && copy.Name == "Spartan (2)" && !ReferenceEquals(renamed.Items, copy.Items), "a rename is a new kit");
var supplyTpl = copy.Fill.RigContents[0].Tpl;
var lean = CustomKitEditing.WithoutSupplies(copy, tpl => tpl == supplyTpl);
Need(lean.Fill.RigContents.Count == copy.Fill.RigContents.Count - 1 && copy.Fill.RigContents.Any(e => e.Tpl == supplyTpl), "dropping supplies is a new kit");
var prices = copy.Items.Select(n => n.Tpl).Distinct().ToDictionary(t => t, _ => 1000d);
int expected = (int)(Math.Round(copy.Items.Count(n => n.SlotId != "SecuredContainer") * 1000d / 1000d) * 1000d);
Need(CustomKitEditing.Price(copy, tpl => prices.TryGetValue(tpl, out var p) ? p : null) == expected, "a kit is priced by the shared rule without its secure container");
Need(CustomKitEditing.Price(copy, _ => null) == 0, "an unpriced kit costs 0, which the policy refuses");

// Every item the kit hands out needs a price; the plumbing and the game's own locked inserts do not.
double? Listed(string tpl, Dictionary<string, double> table) => table.TryGetValue(tpl, out var p) ? p : null;
var listed = copy.Items.Select(n => n.Tpl).Distinct().ToDictionary(t => t, _ => 1000d);
string copyRoot = CustomKitStructure.RootId(copy);
var part = copy.Items.First(n => weaponTree.Contains(n.Id) && n.Id != weapon.Id && n.SlotId != "cartridges");
Need(CustomKitEditing.FirstUnpriced(copy, t => Listed(t, listed), null) == null, "a fully priced kit has no unpriced item");
var withoutPart = new Dictionary<string, double>(listed);
withoutPart.Remove(part.Tpl);
Need(CustomKitEditing.FirstUnpriced(copy, t => Listed(t, withoutPart), null)?.Tpl == part.Tpl, "an unpriced item is found");
Need(CustomKitEditing.FirstUnpriced(copy, t => Listed(t, new Dictionary<string, double>(listed) { [part.Tpl] = 0 }), null)?.Tpl == part.Tpl, "a price of 0 is no price");
Need(CustomKitEditing.FirstUnpriced(copy, t => Listed(t, withoutPart), id => copy.Items.Any(n => n.Id == id && n.Tpl == part.Tpl)) == null,
     "an insert of a locked slot needs no price");
var plumbing = new Dictionary<string, double>(listed);
plumbing.Remove(CustomKitStructure.EquipmentTemplate);
plumbing.Remove(copy.Items.First(n => n.ParentId == copyRoot && n.SlotId == "Pockets").Tpl);
plumbing.Remove(copy.Items.First(n => n.ParentId == copyRoot && n.SlotId == "SecuredContainer").Tpl);
Need(CustomKitEditing.FirstUnpriced(copy, t => Listed(t, plumbing), null) == null, "the root, the pockets and the secure container need no price");

// An item Arena does not hand out is found in a stored kit.
Need(CustomKitEditing.FirstExcluded(copy, _ => "ordinary", null) == null, "a kit of ordinary items has no excluded item");
var scoped = CustomKitEditing.Clone(copy);
scoped.Items.First(n => n.Id == part.Id).Tpl = "544a3d0a4bdc2d1b388b4567";
Need(CustomKitEditing.FirstExcluded(scoped, _ => "ordinary", null)?.Id == part.Id, "a seeded broken item is found");
Need(CustomKitEditing.FirstExcluded(copy, t => t == part.Tpl ? "!!!DO_NOT_USE!!!x" : "ordinary", null)?.Id == part.Id, "a placeholder item is found");
Need(CustomKitEditing.FirstExcluded(copy, _ => "ordinary", new[] { part.Tpl })?.Tpl == part.Tpl, "a configured exclusion is found");

// --- the kit file
string folder = Path.Combine(Path.GetTempPath(), "custom-kit-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string file = Path.Combine(folder, "com.maschine.ArenaMode.custom-kits.json");
try
{
    var empty = CustomKitStore.Load(file);
    Need(empty.Records.Count == 0 && !empty.ReadOnly && empty.Banner == null, "no file is an empty, writable list");
    var saved = new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
    Need(CustomKitStore.Mutate(file, list => { list.Add(CustomKitStore.ToRecord(copy, saved)); return null; }) == null && File.Exists(file),
         "a first save writes the file");
    Need(!File.Exists(CustomKitStore.BackupPath(file)) && !File.Exists(CustomKitStore.TempPath(file)), "a first save leaves no backup or temp file");
    string first = File.ReadAllText(file);
    var loaded = CustomKitStore.Load(file);
    Need(loaded.Records.Count == 1 && loaded.Records[0].Kit?.Id == copy.Id && loaded.Records[0].Kit.Items.Count == copy.Items.Count
         && loaded.Records[0].SavedUtc == saved, "a saved kit reads back");
    Need((int)JObject.Parse(first)["version"] == 1 && JObject.Parse(first)["kits"][0]["price"] == null, "the file has a version and no price");

    // An unreadable record and an unknown field survive saving another kit.
    var raw = JObject.Parse(first);
    raw["kits"][0]["future"] = "kept";
    ((JArray)raw["kits"]).Add("not a kit");
    ((JArray)raw["kits"]).Add(new JObject { ["id"] = "custom-1111111111111111", ["items"] = 5 });
    raw["kits"][0]["price"] = 1;
    File.WriteAllText(file, raw.ToString());
    var mixed = CustomKitStore.Load(file);
    Need(mixed.Records.Count == 3 && mixed.Records[0].Kit != null && mixed.Records[0].Kit.Price == 0
         && mixed.Records[1].Kit == null && mixed.Records[2].Kit == null && mixed.Records[2].Problem != null, "bad records load as problems, a stored price is ignored");
    var other = Copy(stock[1], "Second");
    Need(CustomKitStore.Mutate(file, list => { list.Add(CustomKitStore.ToRecord(other, saved)); return null; }) == null, "a second kit saves next to bad records");
    var after = JObject.Parse(File.ReadAllText(file));
    Need(((JArray)after["kits"]).Count == 4 && (string)after["kits"][1] == "not a kit" && (int)after["kits"][2]["items"] == 5
         && (string)after["kits"][0]["future"] == "kept", "bad records and unknown fields are written back as they were");
    Need(File.Exists(CustomKitStore.BackupPath(file)) && JObject.Parse(File.ReadAllText(CustomKitStore.BackupPath(file)))["kits"].Count() == 3,
         "a save keeps the previous file as the backup");
    var edited = CustomKitStore.ToRecord(CustomKitEditing.Renamed(copy, "Renamed"), saved, after["kits"][0]);
    Need((string)edited["future"] == "kept" && (string)edited["name"] == "Renamed" && edited["price"] == null, "an edited record keeps its unknown fields");

    // A refused change writes nothing.
    string untouched = File.ReadAllText(file);
    Need(CustomKitStore.Mutate(file, list => { list.Clear(); return "no"; }) == "no" && File.ReadAllText(file) == untouched, "a refused change writes nothing");
    File.WriteAllText(CustomKitStore.TempPath(file), "stale");
    Need(CustomKitStore.Mutate(file, list => null) == null && !File.Exists(CustomKitStore.TempPath(file)), "a stale temp file does not block a save");

    // Another writer between the read and the swap: the save is refused rather than lost over it.
    string theirs = new JObject { ["version"] = 1, ["kits"] = new JArray() }.ToString();
    string clash = CustomKitStore.Mutate(file, list => { File.WriteAllText(file, theirs); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1)); list.Add(CustomKitStore.ToRecord(other, saved)); return null; });
    Need(clash != null && clash.Contains("another program") && File.ReadAllText(file) == theirs, "a file changed since the read is not written over");

    // A file that cannot be read is set aside and the last good copy restored.
    Need(CustomKitStore.Mutate(file, list => { list.Add(CustomKitStore.ToRecord(copy, saved)); return null; }) == null, "a save after the clash works");
    Need(CustomKitStore.Mutate(file, list => { list.Add(CustomKitStore.ToRecord(other, saved)); return null; }) == null, "a second save makes a backup with one kit");

    // A file another program holds open (here: no reading, renaming allowed) is not corrupt: left alone, not written.
    string heldText = File.ReadAllText(file);
    using (var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Delete))
    {
        var busy = CustomKitStore.Load(file);
        Need(busy.ReadOnly && busy.Banner != null && busy.Banner.Contains("could not be opened") && File.Exists(file)
             && Directory.GetFiles(folder, "com.maschine.ArenaMode.custom-kits.corrupt-*.json").Length == 0, "a file held by another program is left alone");
        Need(CustomKitStore.Mutate(file, list => { list.Clear(); return null; }) != null, "a file held by another program is not written");
    }

    Need(File.ReadAllText(file) == heldText, "a held file is unchanged");

    // A missing file with a backup gets the backup back, instead of an empty list the next save would write over the backup with.
    string backupText = File.ReadAllText(CustomKitStore.BackupPath(file));
    File.Delete(file);
    var back = CustomKitStore.Load(file);
    Need(back.Records.Count == JObject.Parse(backupText)["kits"].Count() && back.Banner != null && back.Banner.Contains("missing")
         && File.Exists(file) && File.ReadAllText(file) == backupText, "a missing file is restored from its backup");

    var index = new List<JToken>
    {
        "not a kit", new JObject { ["id"] = "custom-aaaaaaaaaaaaaaaa" }, new JObject { ["id"] = new JObject { ["x"] = 1 } },
        new JObject { ["id"] = new JArray(1) }, new JObject { ["id"] = "custom-bbbbbbbbbbbbbbbb" },
    };
    int Index(string id) { try { return CustomKitStore.IndexOf(index, id); } catch (Exception) { return -2; } }
    Need(Index("custom-bbbbbbbbbbbbbbbb") == 4 && Index("CUSTOM-AAAAAAAAAAAAAAAA") == 1 && Index("custom-cccccccccccccccc") == -1,
         "the index finds the record by id past records whose id is not text");

    File.WriteAllText(file, "{ not json");
    var recovered = CustomKitStore.Load(file);
    var corrupt = Directory.GetFiles(folder, "com.maschine.ArenaMode.custom-kits.corrupt-*.json");
    Need(corrupt.Length == 1 && File.ReadAllText(corrupt[0]) == "{ not json", "an unreadable file is kept aside as it was");
    Need(recovered.Records.Count == 1 && recovered.Banner != null && recovered.Banner.Contains("restored") && !recovered.ReadOnly && File.Exists(file),
         "the last good copy is restored and the window is told");
    File.Delete(CustomKitStore.BackupPath(file));
    for (int round = 0; round < 4; round++)
    {
        File.WriteAllText(file, "[]");
        var lost = CustomKitStore.Load(file);
        Need(lost.Records.Count == 0 && lost.Banner != null && !File.Exists(file), "without a backup the list starts empty and says so");
        Thread.Sleep(5);
    }

    Need(Directory.GetFiles(folder, "com.maschine.ArenaMode.custom-kits.corrupt-*.json").Length == CustomKitStore.KeptQuarantines,
         "only the newest three unreadable files are kept");

    // A newer schema and an oversized list are shown but never written.
    string newer = new JObject { ["version"] = 2, ["kits"] = new JArray(CustomKitStore.ToRecord(copy, saved)) }.ToString();
    File.WriteAllText(file, newer);
    var future = CustomKitStore.Load(file);
    Need(future.ReadOnly && future.Records.Count == 1 && future.Banner.Contains("newer"), "a newer schema loads read-only");
    Need(CustomKitStore.Mutate(file, list => { list.Clear(); return null; }) != null && File.ReadAllText(file) == newer, "a newer schema is never written");
    string farFuture = "{\"version\":99999999999999999999,\"kits\":[]}";
    File.WriteAllText(file, farFuture);
    CustomKitFile far = null;
    try { far = CustomKitStore.Load(file); } catch (Exception) { far = null; }
    Need(far != null && far.ReadOnly && far.Banner != null && far.Banner.Contains("newer") && File.ReadAllText(file) == farFuture,
         "a version past the range of a number loads read-only instead of throwing");
    var many = new JArray();
    for (int i = 0; i < CustomKitStore.MaxKits + 1; i++) many.Add(CustomKitStore.ToRecord(copy, saved));
    string crowded = new JObject { ["version"] = 1, ["kits"] = many }.ToString();
    File.WriteAllText(file, crowded);
    Need(CustomKitStore.Load(file).ReadOnly && CustomKitStore.Mutate(file, list => { list.RemoveAt(0); return null; }) != null
         && File.ReadAllText(file) == crowded, "more kits than the limit load read-only");
    File.WriteAllText(file, new JObject { ["version"] = 1, ["kits"] = new JArray() }.ToString());
    Need(CustomKitStore.Mutate(file, list => { for (int i = 0; i < CustomKitStore.MaxKits + 1; i++) list.Add(CustomKitStore.ToRecord(copy, saved)); return null; }) != null,
         "a save cannot grow past the limit");
    File.WriteAllText(file, new string(' ', (int)CustomKitStore.MaxFileBytes + 1));
    var huge = CustomKitStore.Load(file);
    Need(huge.Records.Count == 0 && huge.Banner != null && huge.Banner.Contains("1 MiB"), "an oversized file is set aside");
}
finally
{
    try { Directory.Delete(folder, true); } catch { }
}

// --- candidates
CustomKitCandidateFacts Facts() => new() { Tpl = "5447a9cd4bdc2dbd208b4567", InternalName = "weapon_izhmash_ak74n_545x39", DisplayName = "AK-74N", HasPrefab = true, IsWeapon = true, HasPreset = true, HandbookPrice = 28000 };
Need(CustomKitCandidates.Accept(Facts(), null), "a finished priced weapon with a build is offered");
CustomKitCandidateFacts With(Action<CustomKitCandidateFacts> change) { var facts = Facts(); change(facts); return facts; }
Need(!CustomKitCandidates.Accept(With(f => f.DisplayName = "!!!DO_NOT_USE!!!AK"), null), "a placeholder is not offered");
Need(!CustomKitCandidates.Accept(Facts(), new[] { "5447a9cd4bdc2dbd208b4567" }), "a configured exclusion is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.Tpl = "544a3d0a4bdc2d1b388b4567"), null), "a seeded broken template is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.HasPrefab = false), null), "an item without a model is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.IsStationary = true), null), "a stationary weapon is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.InternalName = "weapon_test_consumable_items"), null), "the test weapon is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.HandbookPrice = null), null) && !CustomKitCandidates.Accept(With(f => f.HandbookPrice = 0), null),
     "an unpriced item is not offered");
Need(!CustomKitCandidates.Accept(With(f => f.HasPreset = false), null), "a weapon without a default build is not offered");
Need(CustomKitCandidates.Accept(With(f => { f.IsWeapon = false; f.HasPreset = false; }), null), "gear without a preset is offered");

Console.WriteLine($"CUSTOM_KIT_CHECKS_PASSED ({checks} assertions)");
