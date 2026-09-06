using System.Text.Json.Nodes;
using ArenaMode.Server.Services;

static class Checks
{
    private static int _assertions;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("ASSERT: " + message); _assertions++; }

    public static int Main(string[] args)
    {
        try
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("../..");
            string database = args.Length > 1 ? args[1] : @"D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json";
            Synthetic();
            if (!File.Exists(database)) throw new InvalidOperationException("Real item database required: " + database);
            var db = JsonNode.Parse(File.ReadAllText(database))!.AsObject();
            var engine = new ArenaWeaponVariants(db);
            string beforeDb = db.ToJsonString();
            RealCatalog(engine, db, File.ReadAllText(Path.Combine(root, "ArenaMode.Server/defaults/kits.json")), "kits", 20);
            RealCatalog(engine, db, File.ReadAllText(Path.Combine(root, "ArenaMode.Server/defaults/ladder.json")), "ladder", 20);
            string liveKits = Path.Combine(root, "../../SPT_Runtime/user/mods/ArenaMode/config/kits.json");
            if (File.Exists(liveKits)) RealCatalog(engine, db, File.ReadAllText(liveKits), "installed-kits", 20);
            Check(beforeDb == db.ToJsonString(), "real template database remains byte-equivalent");
            string session = File.ReadAllText(Path.Combine(root, "ArenaMode.Server/Services/ArenaSessionService.cs"));
            string router = File.ReadAllText(Path.Combine(root, "ArenaMode.Server/Routers/ArenaModeRouter.cs"));
            Check(session.Contains("entry.KitsJson = variants.CreateCatalog") && session.Contains("entry.LadderPoolJson = ArenaContext.LadderJson"), "raid snapshots source and result");
            Check(router.Contains("Sessions?.GetKits(sessionId)") && router.Contains("Sessions?.GetLadder(sessionId, redraw: true)"), "routes use exact session catalogue");
            Check(!session.Contains("ArenaContext.KitsJson = variants"), "no shared source rewrite");
            CoverException(engine, db, File.ReadAllText(Path.Combine(root, "ArenaMode.Server/defaults/kits.json")));
            Console.WriteLine($"ARENA_WEAPON_VARIANTS_PASSED assertions={_assertions}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Synthetic()
    {
        var db = Fixture.Database();
        var engine = new ArenaWeaponVariants(db);
        var entry = Fixture.Entry();
        var items = entry["items"]!.AsArray();
        Check(engine.TryValidate(items, out _), "positive base control");
        var wrong = (JsonArray)items.DeepClone();
        wrong.Add(Fixture.Node("bad", Fixture.Grip, "weapon", "mod_mount"));
        Check(!engine.TryValidate(wrong, out _), "explicit incompatible slot rejected");
        var missing = (JsonArray)items.DeepClone();
        missing.Add(Fixture.Node("baremount", Fixture.Mount, "weapon", "mod_mount"));
        Check(!engine.TryValidate(missing, out var why) && why.StartsWith("required-slot:"), "late mount required child checked");
        var duplicate = (JsonArray)items.DeepClone(); duplicate.Add(items[1]!.DeepClone());
        Check(!engine.TryValidate(duplicate, out _), "duplicate identity rejected");
        var orphan = (JsonArray)items.DeepClone(); orphan[2]!["parentId"] = "missing";
        Check(!engine.TryValidate(orphan, out _), "orphan rejected");
        var cycle = (JsonArray)items.DeepClone(); cycle[1]!["parentId"] = "barrel";
        Check(!engine.TryValidate(cycle, out _), "parent cycle rejected");

        string input = new JsonArray(entry).ToJsonString();
        string first = engine.CreateCatalog(input, 7, out var report);
        Check(report.Single().HasOptic && report.Single().Added >= 2, "mount plus CQB optic generated");
        Check(first == engine.CreateCatalog(input, 7, out _), "same seed deterministic including IDs");
        Check(engine.CreateCatalog(first, 7, out _).Length >= first.Length, "owned existing nodes remain present");
        Check(input == new JsonArray(Fixture.Entry()).ToJsonString(), "base input unchanged");
        Check(!first.Contains(Fixture.LongScope) && !first.Contains(Fixture.Thermal), "long and special optics excluded");
        var output = JsonNode.Parse(first)!.AsArray()[0]!["items"]!.AsArray();
        Preserve(items, output);

        // Native filters are conjunctions, including subtree exclusions.
        var filterDb = Fixture.Database();
        filterDb[Fixture.Gun]!["_props"]!["Slots"]![1]!["_props"]!["filters"]!.AsArray()
            .Add(new JsonObject { ["Filter"] = new JsonArray(Fixture.Grip) });
        Check(!new ArenaWeaponVariants(filterDb).TryValidate(output, out _), "second inclusion record cannot be ignored");
        filterDb = Fixture.Database();
        const string customGroup = "eeeeeeeeeeeeeeeeeeeeeeee";
        filterDb[customGroup] = new JsonObject { ["_parent"] = "55818ad54bdc2ddc698b4569", ["_type"] = "Node", ["_props"] = new JsonObject() };
        filterDb[Fixture.Dot]!["_parent"] = customGroup;
        filterDb[Fixture.Mount]!["_props"]!["Slots"]![0]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(customGroup);
        var customFiltered = (JsonArray)items.DeepClone();
        customFiltered.Add(Fixture.Node("mount", Fixture.Mount, "weapon", "mod_mount"));
        customFiltered.Add(Fixture.Node("dot", Fixture.Dot, "mount", "mod_scope"));
        Check(!new ArenaWeaponVariants(filterDb).TryValidate(customFiltered, out _), "unknown Node is not a native filter category");
        filterDb[Fixture.Mount]!["_props"]!["Slots"]![0]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray(Fixture.Dot);
        Check(new ArenaWeaponVariants(filterDb).TryValidate(customFiltered, out _), "exact modded template remains accepted");
        filterDb = Fixture.Database();
        filterDb[Fixture.Gun]!["_props"]!["Slots"]![1]!["_props"]!["filters"]![0]!["ExcludedFilter"] = new JsonArray(Fixture.Dot);
        var onlyDot = (JsonArray)items.DeepClone();
        onlyDot.Add(Fixture.Node("mount", Fixture.Mount, "weapon", "mod_mount"));
        onlyDot.Add(Fixture.Node("dot", Fixture.Dot, "mount", "mod_scope"));
        Check(!new ArenaWeaponVariants(filterDb).TryValidate(onlyDot, out _), "ancestor slot excludes descendant optic");
        foreach (bool reverse in new[] { false, true })
        {
            var conflictDb = Fixture.Database();
            conflictDb[reverse ? Fixture.Gun : Fixture.Dot]!["_props"]!["ConflictingItems"] = new JsonArray(reverse ? Fixture.Dot : Fixture.Gun);
            Check(!new ArenaWeaponVariants(conflictDb).TryValidate(onlyDot, out _), "conflict both directions includes weapon root");
        }
        var noRoute = Fixture.Database();
        noRoute[Fixture.Gun]!["_props"]!["Slots"]![1]!["_props"]!["filters"]![0]!["Filter"] = new JsonArray();
        var noEngine = new ArenaWeaponVariants(noRoute);
        var fallback = noEngine.CreateCatalog(input, 9, out var fallbackReport);
        Check(!fallbackReport.Single().HasOptic && fallbackReport.Single().Detail.Contains("optic:"), "no compatible optic diagnosed");
        Preserve(items, JsonNode.Parse(fallback)![0]!["items"]!.AsArray());
        string malformed = "{broken";
        Check(engine.CreateCatalog(malformed, 1, out _) == malformed, "unparseable source exact fallback");
        var badEntry = Fixture.Entry(); badEntry["items"]!.AsArray().RemoveAt(2);
        string badCatalog = new JsonArray(badEntry).ToJsonString();
        Check(engine.CreateCatalog(badCatalog, 1, out var badReport) == badCatalog && badReport[0].Detail.StartsWith("base-invalid"), "invalid base left exactly unchanged");

        int dots = 0, scopes = 0, grips = 0, lasers = 0;
        var variants = new HashSet<string>();
        for (int seed = 0; seed < 300; seed++)
        {
            var built = JsonNode.Parse(engine.CreateCatalog(input, seed, out _))![0]!["items"]!.AsArray();
            Check(engine.TryValidate(built, out _), "weighted output always valid");
            var tpls = built.Select(x => x!["_tpl"]!.GetValue<string>()).ToHashSet();
            if (tpls.Contains(Fixture.Dot)) dots++;
            if (tpls.Contains(Fixture.Scope)) scopes++;
            if (tpls.Contains(Fixture.Grip)) grips++;
            if (tpls.Contains(Fixture.Laser)) lasers++;
            variants.Add(string.Join(",", tpls.Order()));
        }
        Check(dots > scopes * 3 && scopes > 5, "CQB is preferred but lower weight compact optics still occur");
        Check(grips > 170 && grips < 265 && lasers > 120 && lasers < 230, "optional slot probabilities retain variation");
        Check(variants.Count > 5, "draws produce genuinely different attachment combinations");
        Console.WriteLine($"Weighted fixture: CQB={dots}, compact scope={scopes}, grip={grips}, laser={lasers}, combinations={variants.Count}");
    }

    private static void RealCatalog(ArenaWeaponVariants engine, JsonObject db, string input, string label, int minimumCount)
    {
        var original = JsonNode.Parse(input)!.AsArray();
        Check(original.Count >= minimumCount, label + " meaningful fixture size");
        int improvements = 0;
        var improved = new HashSet<string>();
        for (int seed = 0; seed < 4; seed++)
        {
            string generated = engine.CreateCatalog(input, seed, out var report);
            var catalog = JsonNode.Parse(generated)!.AsArray();
            Check(catalog.Count == original.Count, label + " count/progression preserved");
            Check(generated == engine.CreateCatalog(input, seed, out _), label + " stable repeated draw");
            for (int i = 0; i < catalog.Count; i++)
            {
                var before = original[i]!.AsObject(); var after = catalog[i]!.AsObject();
                foreach (var prop in before.Where(x => x.Key != "items"))
                    Check(JsonNode.DeepEquals(prop.Value, after[prop.Key]), label + " authored metadata " + prop.Key);
                var baseItems = before["items"]!.AsArray(); var variantItems = after["items"]!.AsArray();
                Preserve(baseItems, variantItems);
                bool validBase = engine.TryValidate(baseItems, out _);
                if (validBase)
                {
                    Check(engine.TryValidate(variantItems, out var reason), label + " full real validation " + reason);
                    AuditNewNodes(db, baseItems, variantItems);
                }
                else Check(JsonNode.DeepEquals(baseItems, variantItems), label + " invalid authored base exact fallback");
                if (!report[i].HadOptic && report[i].HasOptic) { improvements++; improved.Add(report[i].Id); }
            }
            if (seed == 0) Console.WriteLine(ArenaWeaponVariants.Summarize(label, report));
            else if (label.Contains("kits"))
                Console.WriteLine($"{label} seed={seed}: optics={report.Count(x => x.HasOptic)}/{report.Count}; "
                    + string.Join("; ", report.Where(x => !x.HasOptic).Select(x => x.Id + "=" + x.Detail)));
            if (label == "kits") Check(report.All(x => x.HasOptic), "all twenty shipped kit concepts receive an optic at seed " + seed);
        }
        Check(improvements > 0 && improved.Count >= (label.Contains("kits") ? 8 : 6), label + " measurable optic coverage across models");
        Console.WriteLine($"Real coverage {label}: improved models={improved.Count}/{original.Count}; additions across seeds={improvements}");
        string drawn = LadderPicker.Redraw(input, 12, null, engine);
        Check(drawn == LadderPicker.Current, "selected ladder stays installed without redrawing on access");
    }

    private static void Preserve(JsonArray before, JsonArray after)
    {
        foreach (var node in before)
        {
            if (after.Any(x => JsonNode.DeepEquals(x, node))) { Check(true, "original node retained"); continue; }
            string tpl = node!["_tpl"]!.GetValue<string>();
            string id = node["_id"]!.GetValue<string>();
            Check(new[] { "59d6507c86f7741b846413a2", "5649af094bdc2df8348b4586", "59e6449086f7746c9f75e822", "59985a8086f77414ec448d1a" }.Contains(tpl)
                  && node["upd"] == null && !before.Any(x => x!["parentId"]?.GetValue<string>() == id), "only approved childless plain cover may differ");
            var replaced = after.Single(x => x!["_id"]!.GetValue<string>() == id)!.DeepClone();
            Check(new[] { "5d2c76ed48f03532f2136169", "5d2c770c48f0354b4a07c100", "5d2c772c48f0355d95672c25", "59985a6c86f77414ec448d17" }.Contains(replaced["_tpl"]!.GetValue<string>()), "known compatible railed cover chosen");
            replaced["_tpl"] = tpl;
            Check(JsonNode.DeepEquals(node, replaced), "cover replacement preserves every other authored field");
            Check(after.Any(x => x!["parentId"]?.GetValue<string>() == id && x["slotId"]?.GetValue<string>() == "mod_scope"), "cover committed only with complete optic branch");
        }
        Check(after.Count(x => x!["slotId"]?.GetValue<string>() == "FirstPrimaryWeapon") == 1, "one original primary");
    }

    private static void CoverException(ArenaWeaponVariants engine, JsonObject db, string json)
    {
        var kit = JsonNode.Parse(json)!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "loyalist")!.AsObject();
        var source = new JsonArray(kit.DeepClone()).ToJsonString();
        var result = engine.CreateCatalog(source, 6, out var reports);
        Check(reports.Single().HasOptic && reports.Single().Detail.Contains("stock-cover:"), "AKM stock cover receives atomic optic upgrade");
        var custom = (JsonObject)kit.DeepClone();
        var cover = custom["items"]!.AsArray().Single(x => x!["slotId"]?.GetValue<string>() == "mod_reciever")!;
        string coverId = cover["_id"]!.GetValue<string>();
        cover["upd"] = new JsonObject { ["Custom"] = 42 };
        var protectedKit = JsonNode.Parse(engine.CreateCatalog(new JsonArray(custom).ToJsonString(), 6, out _))![0]!;
        Check(JsonNode.DeepEquals(cover, protectedKit["items"]!.AsArray().Single(x => x!["_id"]!.GetValue<string>() == coverId)), "custom Upd blocks cover replacement");
        var child = (JsonObject)kit.DeepClone();
        child["items"]!.AsArray().Add(Fixture.Node("authored-child", Fixture.Dot, coverId, "mod_mount"));
        var withChild = JsonNode.Parse(engine.CreateCatalog(new JsonArray(child).ToJsonString(), 6, out _))![0]!;
        Check(JsonNode.DeepEquals(child["items"], withChild["items"]), "authored child prevents cover removal even when invalid");
        var deniedDb = (JsonObject)db.DeepClone();
        string gunTpl = kit["items"]!.AsArray().Single(x => x!["slotId"]?.GetValue<string>() == "FirstPrimaryWeapon")!["_tpl"]!.GetValue<string>();
        var receiverSlot = deniedDb[gunTpl]!["_props"]!["Slots"]!.AsArray().Single(x => x!["_name"]!.GetValue<string>() == "mod_reciever")!;
        receiverSlot["_props"]!["filters"]![0]!["Filter"] = new JsonArray("59d6507c86f7741b846413a2");
        var denied = JsonNode.Parse(new ArenaWeaponVariants(deniedDb).CreateCatalog(source, 6, out var deniedReport))![0]!;
        Check(!deniedReport.Single().HasOptic, "incompatible replacement cover never forced");
        Check(JsonNode.DeepEquals(kit["items"]!.AsArray().Single(x => x!["_id"]!.GetValue<string>() == coverId), denied["items"]!.AsArray().Single(x => x!["_id"]!.GetValue<string>() == coverId)), "failed cover+optic transaction preserves original cover");
        // Exercise the fallback branch directly: an unrelated free rail must
        // never justify replacing a cover whose own optic socket cannot work.
        var unrelatedDb = (JsonObject)db.DeepClone();
        const string compatibleDot = "58491f3324597764bc48fa02";
        unrelatedDb[gunTpl]!["_props"]!["Slots"]!.AsArray().Add(new JsonObject
        {
            ["_name"] = "mod_scope", ["_required"] = false,
            ["_props"] = new JsonObject { ["filters"] = new JsonArray(new JsonObject { ["Filter"] = new JsonArray(compatibleDot) }) },
        });
        foreach (string railed in new[] { "5d2c76ed48f03532f2136169", "5d2c770c48f0354b4a07c100", "5d2c772c48f0355d95672c25" })
            foreach (var s in unrelatedDb[railed]!["_props"]!["Slots"]!.AsArray())
                s!["_props"]!["filters"] = new JsonArray(new JsonObject { ["Filter"] = new JsonArray() });
        var unrelatedEngine = new ArenaWeaponVariants(unrelatedDb);
        unrelatedEngine.CreateCatalog(source, 6, out var unrelatedReport);
        Check(unrelatedReport.Single().HasOptic && !unrelatedReport.Single().Detail.Contains("stock-cover:"), "unrelated free rail positive control is usable without cover replacement");
        object?[] invocation = { kit["items"]!.AsArray(), new Random(6), 6, "cover-branch", 100.0, null, null };
        var method = typeof(ArenaWeaponVariants).GetMethod("TryReplacePlainCover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Check(!(bool)method.Invoke(unrelatedEngine, invocation)!, "optic elsewhere cannot authorize useless cover replacement");
    }

    private static void AuditNewNodes(JsonObject db, JsonArray before, JsonArray after)
    {
        var originalIds = before.Select(x => x!["_id"]!.GetValue<string>()).ToHashSet();
        foreach (var item in after.Where(x => !originalIds.Contains(x!["_id"]!.GetValue<string>())))
        {
            string tpl = item!["_tpl"]!.GetValue<string>();
            string parent = item["parentId"]!.GetValue<string>();
            var parentNode = after.Single(x => x!["_id"]!.GetValue<string>() == parent)!;
            var slots = db[parentNode["_tpl"]!.GetValue<string>()]!["_props"]!["Slots"]!.AsArray();
            var slot = slots.Single(x => x!["_name"]!.GetValue<string>() == item["slotId"]!.GetValue<string>())!;
            foreach (var filter in slot["_props"]!["filters"]!.AsArray())
                Check(filter!["Filter"]!.AsArray().Any(x => x!.GetValue<string>() == tpl), "independent real DB exact attachment whitelist");
            Check(!db[tpl]!["_props"]!["QuestItem"]!.GetValue<bool>(), "no quest attachments added");
            Check((db[tpl]!["_props"]!["Prefab"]?["path"]?.GetValue<string>() ?? "").Length > 0, "every new template has a preloadable prefab");
        }
    }
}
