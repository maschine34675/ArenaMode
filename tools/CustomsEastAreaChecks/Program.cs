using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int assertions = 0;
void Need(bool condition, string code, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(code + ": " + message);
}

try
{
    if (args.Length != 2) throw new ArgumentException("Usage: CustomsEastAreaChecks <draft fixture> <native bigmap/base.json>");
    var draftBytes = File.ReadAllBytes(args[0]);
    Need(Convert.ToHexString(SHA256.HashData(draftBytes)) == "2F5046F1E1C739D292B3B300C1D0686BFDE420718D4600AFB9E2CEE6EC33A7A0",
        "DRAFT-HASH", "Original user draft bytes changed.");
    using var document = JsonDocument.Parse(draftBytes);
    var draft = document.RootElement;
    var geometry = draft.GetProperty("geometry");
    var vertices = geometry.GetProperty("vertices").EnumerateArray()
        .Select(p => new ArenaAreaVertex(p.GetProperty("x").GetSingle(), p.GetProperty("z").GetSingle())).ToArray();
    Need(draft.GetProperty("schemaVersion").GetInt32() == 1 && draft.GetProperty("kind").GetString() == "ArenaMode.AreaDraft"
        && draft.GetProperty("draftOnly").GetBoolean(), "DRAFT-KIND", "Unexpected source contract.");
    const string geometryHash = "bddf25bdaec03bce8a140cbd7fe0dd73ddc58ef58672ca5ce30078f1f1a7ad1e";
    Need(draft.GetProperty("geometrySha256").GetString() == geometryHash
        && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(geometry))))
            .Equals(geometryHash, StringComparison.OrdinalIgnoreCase), "GEOMETRY-HASH", "Captured geometry fingerprint does not match.");

    var area = ArenaAreaCatalog.CustomsFactoryEastPolygon;
    Need(area.Id == "customs-factory-east-polygon-v1" && area.Id == ArenaAreaCatalog.CustomsFactoryEastPolygonId,
        "PROTOCOL-ID", "Use an explicit new protocol ID.");
    Need(area.MapId == "bigmap" && geometry.GetProperty("mapId").GetString() == area.MapId,
        "MAP-ID", "New contour belongs to Customs only.");
    Need(area.MaxTeamSize == 5 && geometry.GetProperty("maxTeamSize").GetInt32() == 2 && area.TeamStarts != null,
        "TEAM-STARTS", "The unchanged original survey is upgraded through explicit surveyed 2v2..5v5 team starts.");
    Need(area.DisplayName == "Customs Factory Side — team starts", "AREA-NAME", "Use the native zone name, not unverified world-X compass orientation.");
    Need(!area.Bounds.LimitsHeight, "UNLIMITED-HEIGHT", "Draft height hints must not become actor/spawn limits.");
    Need(area.Bounds.Outline.Count == vertices.Length && vertices.Length == 11, "EXACT-GEOMETRY", "Measured outline count changed.");
    for (int i = 0; i < vertices.Length; i++)
    {
        var expected = vertices[i]; var actual = area.Bounds.Outline[i];
        Need(actual.X == expected.X && actual.Z == expected.Z, "EXACT-GEOMETRY", "Measured vertex " + i + " changed.");
        Need(area.Bounds.Contains(expected.X, 2000f, expected.Z), "BOUNDARY", "Boundary must belong to unrestricted-height polygon.");
        Need(!area.AllowsSpawn(expected.X, 1f, expected.Z), "SPAWN-INSET", "A boundary vertex cannot be a spawn footpoint.");
    }
    Need(area.Bounds.MinX == 466.07547f && area.Bounds.MaxX == 670.6625f
        && area.Bounds.MinZ == -167.768631f && area.Bounds.MaxZ == -25.097086f,
        "EXTENTS", "Published geographic extents changed.");
    for (int xi = 0; xi <= 18; xi++)
        for (int zi = 0; zi <= 16; zi++)
        {
            float x = 458f + xi * 12f, z = -176f + zi * 10f;
            bool inside = OracleContains(vertices, x, z, 0);
            bool spawn = OracleContains(vertices, x, z, .75);
            foreach (float y in new[] { -2000f, 1f, 2000f })
            {
                Need(area.Bounds.Contains(x, y, z) == inside, "SHAPE-ORACLE", $"Polygon mismatch at ({x},{y},{z}).");
                Need(area.AllowsSpawn(x, y, z) == spawn, "SPAWN-ORACLE", $"Spawn inset or height mismatch at ({x},{y},{z}).");
            }
        }
    Need(!area.Bounds.Contains(580f, float.NaN, -100f) && !area.AllowsSpawn(float.PositiveInfinity, 1, -100),
        "INVALID-COORDINATES", "Unlimited height does not permit nonfinite data.");

    double triangleArea = 0;
    var triangles = area.Bounds.FloorTriangles;
    Need(triangles.Count == (vertices.Length - 2) * 3, "TRIANGULATION", "Expected nondegenerate contour triangulation.");
    for (int i = 0; i < triangles.Count; i += 3)
    {
        Need(triangles.Skip(i).Take(3).All(index => index >= 0 && index < vertices.Length), "TRIANGULATION", "Index outside measured outline.");
        var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
        double cross = ((double)b.X-a.X)*(c.Z-a.Z)-((double)b.Z-a.Z)*(c.X-a.X);
        Need(cross > 0 && OracleContains(vertices, ((double)a.X+b.X+c.X)/3, ((double)a.Z+b.Z+c.Z)/3, 0),
            "TRIANGULATION", "Map-preview face must be positive and lie within the contour.");
        triangleArea += cross / 2;
    }
    double outlineArea = 0;
    for (int i = 0; i < vertices.Length; i++)
    {
        var a = vertices[i]; var b = vertices[(i + 1) % vertices.Length];
        outlineArea += ((double)a.X*b.Z-(double)b.X*a.Z)/2;
    }
    Need(Math.Abs(triangleArea-outlineArea) < .01, "TRIANGULATION", "Map-preview fill must equal the full measured area.");
    var origin = new ArenaAreaPoint(580, 1, -100);
    var insidePath = new[] { new ArenaAreaPoint(560, 1, -120), new ArenaAreaPoint(550, 500, -130) };
    Need(area.Bounds.ContainsPath(origin, insidePath, true, ArenaAreaCatalog.NavigationInset), "PATH-INSIDE", "Complete contained path should remain legal.");
    Need(!area.Bounds.ContainsPath(origin, insidePath, false, ArenaAreaCatalog.NavigationInset), "PATH-INCOMPLETE", "Incomplete native path must remain illegal.");
    Need(!area.Bounds.ContainsPath(origin, new[] { new ArenaAreaPoint(480, 1, -70), insidePath[0] }, true,
        ArenaAreaCatalog.NavigationInset), "PATH-OUTSIDE", "Path leaving the contour must remain illegal.");

    Need(ArenaAreaCatalog.TryResolve(area.Id, "BIGMAP", "TEAMDEATHMATCH", false, out var resolved, out _)
        && ReferenceEquals(area, resolved), "RESOLVE", "The same shared definition must resolve on client and server.");
    foreach (var mode in new[] { "deathmatch", "gungame", "tactical", "endless", "rounds", "unknown", null })
        Need(!ArenaAreaCatalog.TryResolve(area.Id, "bigmap", mode, false, out _, out _), "REJECT-MODES", "Unsupported bounded mode was admitted: " + mode);
    foreach (var map in new[] { "factory4_day", "factory4_night", "laboratory", "labyrinth", "woods", null })
        Need(!ArenaAreaCatalog.TryResolve(area.Id, map, "teamdeathmatch", false, out _, out _), "REJECT-MAPS", "Wrong map was admitted: " + map);
    Need(!ArenaAreaCatalog.TryResolve(area.Id, "bigmap", "teamdeathmatch", true, out _, out _), "REJECT-PFT", "Independent 2v2 cannot arm with PitFireTeam.");
    foreach (var id in new[] { "customs-factory-east-polygon-v2", draft.GetProperty("proposedAreaId").GetString(), ArenaAreaCatalog.FullMapId })
        Need(!ArenaAreaCatalog.TryResolve(id, "bigmap", "teamdeathmatch", false, out _, out _), "REJECT-PROTOCOL", "Unknown, draft or full-map Customs identity was admitted.");
    foreach (int? requested in new int?[] { null, -1, 0, 1, 6, 12 })
        Need(!ArenaAreaCatalog.TryResolveTeamSize(area, requested, out _, out _), "TEAM-CONTRACT", "Unselected, legacy fixed-team or out-of-range request was admitted.");
    for (int size = 2; size <= 5; size++)
        Need(ArenaAreaCatalog.TryResolveTeamSize(area, size, out int selected, out _) && selected == size,
            "TEAM-CONTRACT", "Exact 2v2..5v5 request must be echoed unchanged.");

    var menu = ArenaAreaSelectionState.Create("bigmap", "teamdeathmatch", false, area.Id, 12);
    Need(menu.Available && menu.SelectedId == area.Id && menu.MapId == "bigmap" && ReferenceEquals(menu.SelectedArea, area),
        "MENU-AREA", "Offered selection and preview must refer to the exact new definition.");
    Need(menu.Options.Select(o => o.Id).SequenceEqual(new[] { "customs-factory-center-polygon-v1",
        "customs-factory-center-teams-v1", area.Id }), "MENU-ORDER", "New option must not replace or reorder existing Customs choices.");
    Need(menu.Summary == "2v2 - opposing team starts" && menu.HasTeamSizeChoice
        && menu.TeamSizeOptions.SequenceEqual(new[] { 2, 3, 4, 5 }),
        "MENU-TEAM-SIZE", "Full-map size 12 cannot leak into explicit bounded sizes.");
    foreach (int size in new[] { 2, 3, 4, 5 })
    {
        var selectedMenu = ArenaAreaSelectionState.Create("bigmap", "teamdeathmatch", false, area.Id, 12, size);
        Need(selectedMenu.SelectedTeamSize == size && selectedMenu.Summary == $"{size}v{size} - opposing team starts",
            "MENU-TEAM-SIZE", "Menu must use this area's requested count, not the full-map setting.");
    }
    foreach (var definition in new[] { ArenaAreaCatalog.CustomsFactoryCenterPolygon, ArenaAreaCatalog.CustomsFactoryCenterTeams })
    {
        var old = ArenaAreaSelectionState.Create("bigmap", "teamdeathmatch", false, definition.Id, 8, 4);
        Need(old.SelectedId == definition.Id && ReferenceEquals(old.SelectedArea, definition), "LEGACY-AREA", "Existing saved area or preview changed.");
    }
    Need(ArenaAreaCatalog.FactorySouthId == "factory-south-v1" && ArenaAreaCatalog.CustomsFactoryCenter.MaxTeamSize == 2
        && ArenaAreaCatalog.CustomsFactoryCenterPolygon.MaxTeamSize == 2 && ArenaAreaCatalog.CustomsFactoryCenterTeams.MaxTeamSize == 5,
        "LEGACY-AREA", "Existing protocol identities or capacities changed.");
    foreach (var (id, map, replacement) in new[] {
        (ArenaAreaCatalog.FactorySouthId, "factory4_day", ArenaAreaCatalog.FullMapId),
        (ArenaAreaCatalog.CustomsFactoryCenterId, "bigmap", ArenaAreaCatalog.CustomsFactoryCenterPolygonId) })
    {
        Need(!ArenaAreaCatalog.TryResolve(id, map, "teamdeathmatch", false, out _, out _),
            "RETIRED-AREA", "Retired prototype must be rejected by the shared arm resolver.");
        var retired = ArenaAreaSelectionState.Create(map, "teamdeathmatch", false, id, 5);
        Need(retired.SelectedId == replacement && retired.Options.All(o => o.Id != id),
            "RETIRED-MENU", "Offer a valid native proposal without admitting the retired prototype.");
    }
    var whole = ArenaAreaSelectionState.Create("factory4_day", "deathmatch", false, area.Id, 8);
    Need(whole.SelectedId == ArenaAreaCatalog.FullMapId && whole.SelectedArea == null && whole.MapId == "factory4_day",
        "PREVIEW-FULL-MAP", "An old Customs request must not overlay a full-map Factory proposal.");
    foreach (var unavailable in new[] {
        ArenaAreaSelectionState.Create("bigmap", "tactical", false, area.Id, 8),
        ArenaAreaSelectionState.Create("bigmap", "tactical", true, area.Id, 8),
        ArenaAreaSelectionState.Create("unknown", "teamdeathmatch", false, area.Id, 8) })
        Need(!unavailable.Available && unavailable.SelectedId == null && unavailable.SelectedArea == null,
            "PREVIEW-UNAVAILABLE", "No offered choice means no geometry preview.");
    var invalid = ArenaAreaSelectionState.Create("bigmap", "teamdeathmatch", false, "unpublished-draft", 8);
    Need(invalid.SelectedId == ArenaAreaCatalog.CustomsFactoryCenterPolygonId
        && ReferenceEquals(invalid.SelectedArea, ArenaAreaCatalog.CustomsFactoryCenterPolygon), "PREVIEW-PROPOSAL", "Preview must follow offered proposal, not rejected draft ID.");

    using var nativeDocument = JsonDocument.Parse(File.ReadAllBytes(args[1]));
    var entries = nativeDocument.RootElement.GetProperty("SpawnPointParams").EnumerateArray().Select(ReadEntry).ToArray();
    Need(entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() == entries.Length, "SPAWN-CORPUS", "Native corpus has duplicate IDs.");
    var allowed = entries.Where(e => area.AllowsSpawn(e.X,e.Y,e.Z,e.Id)).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
    foreach (var e in entries)
        Need(area.AllowsSpawn(e.X,e.Y,e.Z,e.Id) == OracleContains(vertices,e.X,e.Z,.75), "NATIVE-SPAWN-ORACLE", "Native marker admission disagrees with measured contour: " + e.Id);
    var players = allowed.Where(e => Player(e,"Usec") && Player(e,"Bear")).ToArray();
    var bots = allowed.Where(e => Bot(e) && !ArenaBotSpawnPolicy.IsExcludedPoint(area.MapId,e.Id)
        && !ArenaBotSpawnPolicy.IsExcludedZone(area.MapId,e.Zone)).ToArray();
    Need(allowed.Length == 35 && players.Length == 20 && bots.Length == 15,
        "SPAWN-CORPUS", "Reviewed Customs candidate counts changed; inspect the new database before rebinding this survey.");
    Need(players.Length >= 2 && bots.Length >= area.MaxTeamSize*2-1, "STATIC-CAPACITY", "Insufficient distinct candidates even before live path validation.");
    Need(allowed.Select(e => (e.X,e.Y,e.Z)).Distinct().Count() == allowed.Length, "SPAWN-POSITIONS", "Allowed markers repeat physical positions.");
    Need(bots.All(e => e.Zone == "ZoneFactorySide"), "SPAWN-ZONES", "Review unexpected bot zones before claiming the Factory Side name.");
    Need(!allowed.Any(e => e.Zone.Contains("Snipe",StringComparison.OrdinalIgnoreCase)
        || e.Zone == "ZoneBlockPost"), "SNIPER-OUTSIDE", "Sniper or checkpoint markers unexpectedly entered contour.");
    var sniper = entries.Single(e => e.Id == "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e");
    Need(!area.Bounds.Contains(sniper.X,sniper.Y,sniper.Z) && !area.IsExcludedSpawn(sniper.Id), "SNIPER-OUTSIDE", "Sniper exclusion comes from horizontal geometry, not a guessed new rule.");
    Need(players.Any(e => e.Y < 0), "UNLIMITED-SPAWN-HEIGHT", "Below-ground native PMC candidates must not be dropped by the obsolete height hint.");

    // Exact native categories/coordinates decide capacity, not the ID count in
    // a copied test array. Every production-selected marker must be eligible.
    var sectors = new List<Entry[]>();
    foreach (bool usec in new[] { true, false })
    {
        var selectedPlayers = entries.Where(e => area.TeamStarts.Allows(usec, true, e.Id)).ToArray();
        var selectedBots = entries.Where(e => area.TeamStarts.Allows(usec, false, e.Id)).ToArray();
        Need(selectedPlayers.Length == 5 && selectedPlayers.All(e => players.Contains(e)),
            "START-PMC", "Every selected PMC identity must exist, permit both factions and lie inside the measured contour.");
        Need(selectedBots.Length == (usec ? 8 : 6) && selectedBots.All(e => bots.Contains(e)),
            "START-BOT", "Every selected bot identity must exist, have Bot/BotPmc and a native zone, and pass all filters.");
        var sector = selectedPlayers.Concat(selectedBots).ToArray();
        Need(sector.Select(e => (e.X,e.Y,e.Z)).Distinct().Count() == sector.Length,
            "START-POSITION", "Start identities must refer to distinct physical positions.");
        double sameMinimum = selectedBots.SelectMany((a,i) => selectedBots.Skip(i+1).Concat(selectedPlayers).Select(b=>Distance(a,b))).Min();
        Need(sameMinimum >= 1.5, "START-SPACING", "Simultaneously occupied native starts require at least 1.5m horizontal separation.");
        for (int size = 2; size <= 5; size++)
            foreach (var player in selectedPlayers)
                Need(selectedBots.Count(b => Distance(player,b) >= 1.5) >= size,
                    "START-CAPACITY", "Either local faction needs a complete opposing team, not just size-1 allies.");
        sectors.Add(sector);
        Console.WriteLine($"START {(usec ? "USEC map-left" : "BEAR map-right")}: PMC={selectedPlayers.Length}, bot={selectedBots.Length}, simultaneous min={sameMinimum:F3}m; X={sector.Min(e=>e.X):F3}..{sector.Max(e=>e.X):F3}, Z={sector.Min(e=>e.Z):F3}..{sector.Max(e=>e.Z):F3}.");
    }
    Need(!sectors[0].Select(e=>e.Id).Intersect(sectors[1].Select(e=>e.Id)).Any(),
        "START-DISJOINT", "Opposing sectors may not share native identities.");
    double opposingMinimum = sectors[0].Min(a => sectors[1].Min(b => Distance(a,b)));
    Need(opposingMinimum >= 25, "START-OPPOSING", "Every possible opposing pair requires at least 25m horizontal separation.");
    Need(sectors[0].Min(e=>e.X) > sectors[1].Max(e=>e.X),
        "START-ORIENTATION", "USEC map-left uses higher world X than BEAR map-right under the actual reversed-U projection.");
    Need(!area.TeamStarts.Allows(true,false,"ae0c5e1d-71cc-44a2-86ae-4c45e224a1ff"),
        "START-NEUTRAL", "Central X588 marker remains a respawn candidate, not an opposing start less than 25m from BEAR.");
    Console.WriteLine($"Opposing native minimum horizontal separation: {opposingMinimum:F3}m.");
    Console.WriteLine($"Native database SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))}");
    Console.WriteLine($"Native geometry candidates: total={allowed.Length}, PMC per faction={players.Length}, Bot/BotPmc={bots.Length}; area={outlineArea:F1} square metres.");
    Console.WriteLine("ID | X | Y | Z | categories | sides | native zone");
    foreach (var e in allowed) Console.WriteLine($"{e.Id} | {e.X:R} | {e.Y:R} | {e.Z:R} | {string.Join(',',e.Categories)} | {string.Join(',',e.Sides)} | {e.Zone}");
    Console.WriteLine("Static capacity only: no assertion about live NavMesh, obstacles, spawn blocking, visibility or balance.");
    Console.WriteLine($"CUSTOMS EAST PRODUCTION CHECKS PASSED ({assertions} assertions)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("CUSTOMS EAST REJECTED: " + ex.Message);
    return 1;
}

// Independent ray-crossing and Euclidean segment-distance oracle over the user
// fixture. No call into production containment or triangulation helpers.
static bool OracleContains(ArenaAreaVertex[] polygon, double x, double z, double inset)
{
    bool inside = false, boundary = false;
    double clearance = double.PositiveInfinity;
    for (int i = 0, j = polygon.Length-1; i < polygon.Length; j = i++)
    {
        var a = polygon[j]; var b = polygon[i];
        double dx = (double)b.X-a.X, dz = (double)b.Z-a.Z;
        double t = Math.Clamp(((x-a.X)*dx+(z-a.Z)*dz)/(dx*dx+dz*dz),0,1);
        double distance = Math.Sqrt(Math.Pow(x-a.X-t*dx,2)+Math.Pow(z-a.Z-t*dz,2));
        clearance = Math.Min(clearance,distance);
        if (distance < 1e-9) boundary = true;
        if ((a.Z > z) != (b.Z > z) && x < a.X+(z-a.Z)*dx/dz) inside = !inside;
    }
    return (inside || boundary) && clearance >= inset;
}
static bool Player(Entry e,string side) => e.Categories.Contains("Player") && e.Sides.Any(s => s == side || s is "Pmc" or "All");
static bool Bot(Entry e) => !string.IsNullOrWhiteSpace(e.Zone) && e.Categories.Any(c => c is "Bot" or "BotPmc");
static double Distance(Entry a, Entry b) => Math.Sqrt(Math.Pow((double)a.X-b.X,2)+Math.Pow((double)a.Z-b.Z,2));
static Entry ReadEntry(JsonElement element)
{
    var position = element.GetProperty("Position");
    return new Entry(element.GetProperty("Id").GetString(),position.GetProperty("x").GetSingle(),position.GetProperty("y").GetSingle(),position.GetProperty("z").GetSingle(),
        element.GetProperty("Categories").EnumerateArray().Select(c=>c.GetString()).ToArray(),
        element.GetProperty("Sides").EnumerateArray().Select(s=>s.GetString()).ToArray(),element.GetProperty("BotZoneName").GetString() ?? "");
}
record Entry(string Id,float X,float Y,float Z,string[] Categories,string[] Sides,string Zone);
