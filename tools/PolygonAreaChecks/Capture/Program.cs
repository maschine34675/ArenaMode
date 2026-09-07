using ArenaMode.Areas;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using System.Globalization;

int checks = 0;
void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
void Throws(Action action, string message)
{
    try { action(); } catch (ArgumentException) { checks++; return; }
    throw new Exception(message);
}
Throws(() => new ArenaAreaDraft("../outside", null), "Unsupported path-like map was accepted");
var draft = new ArenaAreaDraft(ArenaMapCatalog.CustomsId, ArenaAreaCatalog.CustomsFactoryCenterId);
Check(!draft.TryExport(0, 12, .5f, 3, out var json, out _) && json == null, "Empty draft must not export");
foreach (var p in new[] { new ArenaAreaPoint(0, 1, 0), new ArenaAreaPoint(8, 1, 0), new ArenaAreaPoint(8, 1, 2),
    new ArenaAreaPoint(2, 1, 2), new ArenaAreaPoint(2, 1, 8), new ArenaAreaPoint(0, 1, 8) })
    Check(draft.Add(p, out _), "Simple concave outline point was refused");
Check(!draft.Add(new ArenaAreaPoint(0, 99, 0), out _) && draft.Points.Count == 6,
    "Repeating the first X/Z point must not create a duplicate closing edge");
Check(!draft.Add(new ArenaAreaPoint(float.NaN, 1, 0), out _), "Nonfinite point must be refused");
Check(!draft.Add(new ArenaAreaPoint(10, float.PositiveInfinity, 0), out _), "Nonfinite captured height must be refused");
Check(draft.Validate(0, 12, .5f, 3, out var shape, out _), "Concave draft did not validate");
Check(!shape.Contains(6, 1, 6), "The L-shaped void must not become allowed by bounding extents");
Check(!shape.ContainsPath(new ArenaAreaPoint(1, 1, 7), new[] { new ArenaAreaPoint(7, 1, 1) }, true, .4f),
    "An all-inside-endpoints path must not cut across the concave void");
Check(shape.ContainsPath(new ArenaAreaPoint(1, 1, 7), new[] { new ArenaAreaPoint(1, 1, 1), new ArenaAreaPoint(7, 1, 1) }, true, .4f),
    "A complete route around the concavity should stay valid");
Check(!draft.TryExport(12, 0, .5f, 3, out _, out _), "Reversed enclosing heights must fail");
Check(!draft.TryExport(0, 12, -.1f, 3, out _, out _), "Spawn band below the floor must fail");
Check(!draft.TryExport(0, 12, .5f, 13, out _, out _), "Spawn band above ceiling must fail");
Check(!draft.TryExport(0, float.NaN, .5f, 3, out _, out _), "Nonfinite ceiling must fail");
Check(!draft.TryExport(0, 999, .5f, 3, out _, out _), "Excessive capture height must fail");
var originalCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    Check(draft.TryExport(0, 12, .5f, 3, out json, out _), "Valid draft could not export under German culture");
    var body = JObject.Parse(json);
    Check(body["draftOnly"].Value<bool>() && body["kind"].Value<string>() == "ArenaMode.AreaDraft", "Export must identify itself as a draft");
    Check(body["geometry"]["vertices"].Count() == 6 && body["capturedPoints"].Count() == 6, "Export lost contour or observed world points");
    Check(body["geometry"]["spawnMinY"].Value<float>() == .5f, "Export decimals became culture dependent");
    Check(body["geometry"]["maxTeamSize"].Value<int>() == 2, "Export changed the fixed 2v2 rule");
    Check(body["sourceAreaId"].Value<string>() == ArenaAreaCatalog.CustomsFactoryCenterId, "Capture lost its current area context");
    Check(!ArenaAreaCatalog.TryResolve(body["proposedAreaId"].Value<string>(), "bigmap", "teamdeathmatch", false, out _, out _),
        "A draft ID must not silently become an accepted active catalog entry");
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    Check(draft.TryExport(0, 12, .5f, 3, out var repeat, out _) && json == repeat, "Export identity changed with locale");
    Check(draft.TryExport(-1, 12, .5f, 3, out var changed, out _)
        && JObject.Parse(changed)["geometrySha256"].Value<string>() != body["geometrySha256"].Value<string>(),
        "A geometry change must receive a different proposed identity");
}
finally { CultureInfo.CurrentCulture = originalCulture; }

var output = Path.Combine(Path.GetTempPath(), "arena-capture-check-" + Guid.NewGuid().ToString("N"));
var first = ArenaAreaDraftFile.Write(output, json);
var second = ArenaAreaDraftFile.Write(output, json);
Check(first != second && File.ReadAllText(first) == json && File.ReadAllText(second) == json,
    "Repeated captures must preserve previous files exactly");
Check(Directory.GetFiles(output).Length == 2 && Directory.GetFiles(output, "*.tmp").Length == 0,
    "Published captures left temporary or unexpected files");
Throws(() => ArenaAreaDraftFile.Write(output, ""), "Empty capture file was allowed");
Check(File.ReadAllText(first) == json, "Failed export damaged an existing capture");
Check(draft.Undo() && draft.Points.Count == 5, "Undo did not remove exactly one point");
draft.Clear();
Check(draft.Points.Count == 0 && !draft.Undo(), "Clear/empty undo altered retained points");
foreach (var p in new[] { new ArenaAreaPoint(0, 1, 0), new ArenaAreaPoint(5, 1, 5),
    new ArenaAreaPoint(0, 1, 5), new ArenaAreaPoint(5, 1, 0) }) draft.Add(p, out _);
Check(!draft.TryExport(0, 12, .5f, 3, out _, out _), "Self-crossing draft exported as a playable polygon");
draft.Clear();
for (int i = 0; i < 64; i++)
    Check(draft.Add(new ArenaAreaPoint(i * 2, 1, i % 2), out _), "Point capacity truncated early");
Check(!draft.Add(new ArenaAreaPoint(200, 1, 3), out _) && draft.Points.Count == 64, "Capture capacity was not bounded");
Console.WriteLine($"ARENA_POLYGON_CAPTURE_PASSED ({checks} assertions)");
