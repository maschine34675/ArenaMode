using System.Reflection;
using System.Text.Json.Nodes;
using ArenaMode.Server.Services;

/// <summary>Explicit regression examples, independent of the generator's socket classifier.</summary>
internal static class OpticMountChecks
{
    private const string MountClass = "55818b224bdc2dde698b456f";
    private const string ScopeClass = "55818ae44bdc2dde698b456c";
    private const string Ring = "222222222222222222222201", Cap = "222222222222222222222202";
    private const string Scope4 = "222222222222222222222203", Offset = "222222222222222222222204";
    // Verified actual DB contracts: these main slots are optional but contain
    // the physical tube optic. This expected-data oracle does not call the
    // production IsPrimaryScopeSocket / CanUseOpticBranch implementation.
    internal static readonly (string Tpl, string Socket)[] Rings =
    [
        ("5a37ca54c4a282000d72296a", "mod_scope_000"), // JP 30 mm / DeltaPoint top mount
        ("5b2389515acfc4771e1be0c0", "mod_scope_000"), // Burris PEPR 30 mm
        ("5aa66c72e5b5b00016327c93", "mod_scope_000"), // Nightforce 34 mm multimount
        ("61713cc4d8e3106d9806c109", "mod_scope_000"), // ERA-TAC 34 mm
        ("6171407e50224f204c1da3c5", "mod_scope_000"), // ERA-TAC 30 mm
        ("618b9643526131765025ab35", "mod_scope"),     // Geissele black
        ("618bab21526131765025ab3f", "mod_scope"),     // Geissele DDC
    ];

    internal static void Run(JsonObject realDb, Action<bool, string> check)
    {
        Synthetic(check);
        RealMountContracts(realDb, check);
    }

    private static JsonObject RingDatabase()
    {
        var db = Fixture.Database();
        db[Fixture.Gun]!["_props"]!["Slots"] = new JsonArray(Fixture.Slot("mod_barrel", true, Fixture.Barrel), Fixture.Slot("mod_mount", false, Ring));
        db[Ring] = Fixture.Item(MountClass, 1, Fixture.Slot("mod_scope_000", false, Fixture.LongScope), Fixture.Slot("mod_mount", false, Cap));
        db[Cap] = Fixture.Item(MountClass, 1, Fixture.Slot("mod_scope", false, Fixture.Dot));
        db[Scope4] = Fixture.Item(ScopeClass, 4);
        db[Offset] = Fixture.Item(MountClass, 1, Fixture.Slot("mod_scope", false, Fixture.Dot));
        return db;
    }

    private static void Synthetic(Action<bool, string> check)
    {
        var db = RingDatabase();
        string input = new JsonArray(Fixture.Entry()).ToJsonString();
        var engine = new ArenaWeaponVariants(db);
        for (int seed = 0; seed < 16; seed++)
        {
            string output = engine.CreateCatalog(input, seed, out var report);
            check(!report.Single().HasOptic, "new empty scope rings reject reflex-only chain");
            check(!output.Contains(Ring), "unsuitable new ring is never committed without a scope");
            check(output == engine.CreateCatalog(input, seed, out _), "ring rejection remains deterministic");
        }

        var existing = Fixture.Entry();
        var items = existing["items"]!.AsArray();
        items.Add(Fixture.Node("ring", Ring, "weapon", "mod_mount"));
        items.Add(Fixture.Node("cap", Cap, "ring", "mod_mount"));
        string authored = new JsonArray(existing.DeepClone()).ToJsonString();
        string result = engine.CreateCatalog(authored, 11, out var existingReport);
        check(!existingReport.Single().HasOptic, "existing ring cap cannot bypass empty ancestor scope socket");
        check(JsonNode.DeepEquals(items, JsonNode.Parse(result)![0]!["items"]), "authored empty ring and cap preserved");

        // Existing quirky authored builds stay valid and are not retroactively
        // stripped. The new rule restricts additions, not native acceptance.
        items.Add(Fixture.Node("authored-dot", Fixture.Dot, "cap", "mod_scope"));
        check(engine.TryValidate(items, out _), "native-valid authored empty-ring reflex stays valid");
        result = engine.CreateCatalog(new JsonArray(existing.DeepClone()).ToJsonString(), 3, out _);
        check(JsonNode.DeepEquals(items, JsonNode.Parse(result)![0]!["items"]), "authored empty-ring reflex remains unchanged");
        items.RemoveAt(items.Count - 1);

        // A real scope in THIS primary socket permits the otherwise valid
        // backup branch. Public generation currently respects any existing
        // optic; invoke the actual private candidate builder to test the guard.
        db[Ring]!["_props"]!["Slots"]![0]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(Scope4);
        engine = new ArenaWeaponVariants(db);
        items.Add(Fixture.Node("scope", Scope4, "ring", "mod_scope_000"));
        check(TryOptic(engine, items, out var withBackup), "filled ring primary permits valid backup path");
        check(withBackup.Any(n => T(n, "_tpl") == Fixture.Dot), "filled ring receives the actual backup reflex");
        check(engine.TryValidate(withBackup, out _), "filled ring backup output is natively valid");

        // An optic elsewhere on the weapon is not evidence the ring is filled.
        items.RemoveAt(items.Count - 1);
        db[Fixture.Gun]!["_props"]!["Slots"]!.AsArray().Add(Fixture.Slot("mod_scope", false, Scope4));
        items.Add(Fixture.Node("unrelated-scope", Scope4, "weapon", "mod_scope"));
        db[Ring]!["_props"]!["Slots"]![0]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(Fixture.LongScope);
        engine = new ArenaWeaponVariants(db);
        check(!TryOptic(engine, items, out _), "scope elsewhere cannot authorize empty ring backup");

        foreach (string validRoute in new[] { Fixture.Dot, Offset })
        {
            var validDb = RingDatabase();
            validDb[Fixture.Gun]!["_props"]!["Slots"]![1]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(validRoute);
            var output = new ArenaWeaponVariants(validDb).CreateCatalog(input, 9, out var report);
            check(report.Single().HasOptic && output.Contains(Fixture.Dot), "direct and offset reflex routes retained " + validRoute);
        }
        // Only native OpticScope sockets identify this defect, not zoom>1 or
        // the generic mod_scope label on a prism/AssaultScope adapter.
        var prism = RingDatabase();
        prism[Fixture.LongScope]!["_parent"] = "55818add4bdc2d5b648b456f";
        new ArenaWeaponVariants(prism).CreateCatalog(input, 5, out var prismReport);
        check(prismReport.Single().HasOptic, "prism adapter is not blanket-classified as a scope ring");

        foreach (string filtering in new[] { "intersection", "exclusion", "mixed", "empty" })
        {
            var filtered = RingDatabase();
            var groups = filtered[Ring]!["_props"]!["Slots"]![0]!["_props"]!["filters"]!.AsArray();
            groups[0]!["Filter"] = new JsonArray(Fixture.LongScope, Fixture.Dot);
            if (filtering == "intersection") groups.Add(new JsonObject { ["Filter"] = new JsonArray(Fixture.LongScope) });
            if (filtering == "exclusion") groups[0]!["ExcludedFilter"] = new JsonArray(Fixture.Dot);
            if (filtering == "empty") groups[0]!["ExcludedFilter"] = new JsonArray(Fixture.LongScope, Fixture.Dot);
            new ArenaWeaponVariants(filtered).CreateCatalog(input, 7, out var report);
            check(report.Single().HasOptic == (filtering is "mixed" or "empty"), "socket classification follows actual nonempty native filter intersection " + filtering);
        }

        // A usable low-power tube scope remains available as the primary path.
        var primary = RingDatabase();
        primary[Ring]!["_props"]!["Slots"]![0]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(Scope4);
        string primaryOutput = new ArenaWeaponVariants(primary).CreateCatalog(input, 22, out var primaryReport);
        check(primaryReport.Single().HasOptic && primaryOutput.Contains(Scope4) && !primaryOutput.Contains(Fixture.Dot), "valid primary scope path retained");
    }

    private static bool TryOptic(ArenaWeaponVariants engine, JsonArray input, out JsonArray result)
    {
        var method = typeof(ArenaWeaponVariants).GetMethod("TryAdd", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = [input, "optic", new Random(5), 5, "guard-contract", 0.5d, null, null, null];
        bool okay = (bool)method.Invoke(engine, args)!;
        result = (JsonArray)args[6]!;
        return okay;
    }

    private static void RealMountContracts(JsonObject db, Action<bool, string> check)
    {
        // Synthetic weapon anchors isolate each unmodified REAL mount/filter
        // subtree; whole real kits and Gun Game outputs are audited separately.
        var controlled = (JsonObject)db.DeepClone();
        var catalog = new JsonArray();
        int ordinal = 0;
        string[] positive = ["58d39d3d86f77445bb794ae7", "5a33b2c9c4a282000c5a9511", "5649a2464bdc2d91118b45a8", "5c1cdd302e221602b3137250"];
        foreach (var (mount, primarySlot) in Rings)
        {
            var slot = db[mount]!["_props"]!["Slots"]!.AsArray().Single(x => T(x, "_name") == primarySlot)!;
            check(slot["_required"]!.GetValue<bool>() == false, "real ring primary is native-optional " + mount);
            var listed = slot["_props"]!["filters"]![0]!["Filter"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            check(listed.Length > 0 && listed.All(id => IsA(db, id, ScopeClass)), "real ring primary accepts tube scopes " + mount);
        }
        foreach (string mount in Rings.Select(x => x.Tpl).Concat(positive))
        {
            string gun = "eeeeeeeeeeeeeeee" + (++ordinal).ToString("x8");
            controlled[gun] = Fixture.Item("5422acb9af1c889c16000029", 1, Fixture.Slot("mod_scope", false, mount));
            catalog.Add(new JsonObject { ["id"] = mount, ["items"] = new JsonArray(
                new JsonObject { ["_id"] = "eq", ["_tpl"] = Fixture.Equipment }, Fixture.Node("gun", gun, "eq", "FirstPrimaryWeapon")) });
        }
        var engine = new ArenaWeaponVariants(controlled);
        string input = catalog.ToJsonString();
        for (int seed = 0; seed < 12; seed++)
        {
            var output = JsonNode.Parse(engine.CreateCatalog(input, seed, out var report))!.AsArray();
            foreach (var entry in output)
            {
                var source = catalog.Single(x => T(x, "id") == T(entry, "id"))!["items"]!.AsArray();
                var generated = entry!["items"]!.AsArray();
                check(engine.TryValidate(generated, out _), "real mount candidate fully validates " + T(entry, "id"));
                AuditNewReflexPaths(controlled, source, generated, check);
            }
            foreach (string direct in positive) check(report.Single(r => r.Id == direct).HasOptic, "real direct/offset/prism mount remains usable " + direct);
        }
        Console.WriteLine("Optic ring contract: 7 actual ring mounts plus T1, RM33, MPR45 and Kiba positive routes; 12 seeds.");
    }

    internal static void AuditNewReflexPaths(JsonObject db, JsonArray before, JsonArray after, Action<bool, string> check)
    {
        var originalIds = before.Select(x => T(x, "_id")).ToHashSet();
        var nodes = after.ToDictionary(x => T(x, "_id"), x => x!);
        foreach (var optic in after.Where(x => !originalIds.Contains(T(x, "_id"))
                     && (IsA(db, T(x, "_tpl"), "55818ad54bdc2ddc698b4569") || IsA(db, T(x, "_tpl"), "55818acf4bdc2dde698b456b"))))
        {
            string parent = T(optic, "parentId");
            var seen = new HashSet<string>();
            while (nodes.TryGetValue(parent, out var ancestor) && seen.Add(parent))
            {
                foreach (var ring in Rings.Where(r => r.Tpl == T(ancestor, "_tpl")))
                    check(after.Any(x => T(x, "parentId") == parent && T(x, "slotId") == ring.Socket
                        && IsA(db, T(x, "_tpl"), ScopeClass)), "new real reflex does not sit above an empty scope ring " + ring.Tpl);
                parent = T(ancestor, "parentId");
            }
        }
    }
    private static string T(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";
    private static bool IsA(JsonObject db, string item, string category)
    {
        var seen = new HashSet<string>();
        while (item.Length > 0 && seen.Add(item) && seen.Count < 32)
        { if (item == category) return true; item = T(db[item], "_parent"); }
        return false;
    }
}
