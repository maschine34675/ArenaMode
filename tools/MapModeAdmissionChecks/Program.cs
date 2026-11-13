using ArenaMode.Shared;
using ArenaMode.Bots;
using EFT.Game.Spawning;
using System.Text.Json;
using UnityEngine;

int assertions = 0;
void Need(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

// Iceberg and Chop Shop join the matrix with ArenaMapPort's spawn rows of 2026-11-12: both carry
// the same Tactical decisions as Equator now (see the TACTICAL-ROWS block and the installed-data
// pins below).
string[] maps = { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId, ArenaMapCatalog.AirPitId, ArenaMapCatalog.CustomsId,
    ArenaMapCatalog.IcebergId, ArenaMapCatalog.AutoServiceId };
string[] modes = { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" };
string[] areas = { ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
    ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId };
foreach (string map in maps)
foreach (string mode in modes)
{
    if (map == ArenaMapCatalog.CustomsId)
    {
        Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out _, out _),
            "CUSTOMS-AREA-ONLY: no full-map admission in " + mode);
        Need(!ArenaAreaCatalog.TryResolve(null, map, mode, false, out _, out _),
            "CUSTOMS-AREA-ONLY: omitted area cannot become a whole-map raid");
        continue;
    }
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out var area, out var reason)
        && area == null && reason == null, "FULL-MAP: " + map + "/" + mode);
    Need(ArenaAreaCatalog.TryResolve(null, map, mode, false, out area, out reason)
        && area == null, "OMITTED-FULL: legacy null area remains full map");
    Need(ArenaAreaCatalog.TryResolveTeamSize(area, null, out int size, out _) && size == 0,
        "FULL-SIZE: full map never invents a surveyed team size");
    Need(!ArenaAreaCatalog.TryResolveTeamSize(area, 5, out _, out _),
        "FULL-SIZE: stale bounded team size cannot become a full-map override");
    bool selectable = mode is "deathmatch" or "gungame" or "teamdeathmatch";
    foreach (int total in new[] { 4, 8, 20, 22, 24 })
    {
        bool expected = selectable && !(mode == "teamdeathmatch" && total > 20
            && (map == ArenaMapCatalog.EquatorId || map == ArenaMapCatalog.Bay5Id || map == ArenaMapCatalog.AirPitId
                || map == ArenaMapCatalog.IcebergId || map == ArenaMapCatalog.AutoServiceId));
        Need(ArenaParticipantPolicy.TryResolve(map, mode, false, false, total, out int? selected, out _)
            == expected && (!expected || selected == total), "PARTICIPANTS: exact frozen total " + map + "/" + mode);
    }
}
Need(!ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.EquatorId, "teamdeathmatch", false, false, 24, out _, out _),
    "EQUATOR-TEAM-CAPACITY: twenty native places cannot promise 24 participants");
foreach (string map in new[] { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.Bay5Id.ToUpperInvariant(), ArenaMapCatalog.AirPitId, ArenaMapCatalog.AirPitId.ToUpperInvariant() })
foreach (string mode in new[] { "teamdeathmatch", "TEAMDEATHMATCH" })
{
    Need(ArenaParticipantPolicy.DefaultTotal(map, mode) == 8 && ArenaParticipantPolicy.MaximumFor(map, mode) == 20,
        "BAY5-TEAM-CAPACITY: keep the eight-player default and twenty original places");
    for (int total = 4; total <= 20; total += 2)
        Need(ArenaParticipantPolicy.TryResolve(map, mode, false, false, total, out var selected, out var reason)
            && selected == total && reason == null, "BAY5-EXACT-TOTAL: acknowledge every even 2v2..10v10 format");
    foreach (int? total in new int?[] { null, int.MinValue, 0, 2, 3, 5, 19, 21, 22, 24, int.MaxValue })
        Need(!ArenaParticipantPolicy.TryResolve(map, mode, false, false, total, out var selected, out var reason)
            && selected == null && !string.IsNullOrWhiteSpace(reason),
            "BAY5-BOUNDARY: no omitted, odd, undersized or oversized count can enter team boxes");
    Need(!ArenaParticipantPolicy.TryResolve(map, mode, true, false, 8, out _, out _)
        && !ArenaParticipantPolicy.TryResolve(map, mode, false, true, 8, out _, out _),
        "BAY5-SCOPE: independent full-map teams cannot become a bounded or PFT override");
}
foreach (string mode in new[] { "deathmatch", "gungame" })
{
    Need(ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.Bay5Id, mode, false, false, 24, out var selected, out _)
        && selected == 24, "BAY5-SOLO-CAPACITY: other continuous modes keep 24 participants");
    Need(ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.Bay5Id, mode, false, false, null, out selected, out _)
        && selected == null, "BAY5-LEGACY-SOLO: other modes keep their omitted-count behavior");
}
Need(!ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.AirPitId, "teamdeathmatch", false, false, 24, out _, out _),
    "AIRPIT-CAPACITY: original boxes cannot promise more than twenty participants");
foreach (string map in maps)
foreach (string mode in new[] { "tactical", "zombie" })
{
    // Written out per map and mode on purpose: Customs has no whole-map raid; Equator and Chop Shop
    // are too small for the squad's infected wave (space, not rows); Chop Shop's yard centre has
    // too few distant rows for a PitFireTeam Tactical squad (the pinned evidence is below).
    bool squadAdmitted = map != ArenaMapCatalog.CustomsId
        && !(mode == "zombie" && (ArenaMapCatalog.IsEquator(map) || ArenaMapCatalog.IsAutoService(map)))
        && !(mode == "tactical" && ArenaMapCatalog.IsAutoService(map));
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, true, out var area, out _)
        == squadAdmitted && area == null,
        "PFT-FULL: authenticated squad observes exact full-map capacity " + map + "/" + mode);
}

// TACTICAL-ROWS: ArenaMapPort's rows of 2026-11-12 removed the row argument of the Equator, Iceberg
// and Chop Shop Tactical refusals. Equator and Iceberg are admitted solo and with a squad; Chop Shop
// solo only, with its own reason for the squad format.
foreach (string map in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.IcebergId, ArenaMapCatalog.AutoServiceId })
foreach (string spelling in new[] { "tactical", "TACTICAL" })
{
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, spelling, false, out var soloArea, out var soloReason)
        && soloArea == null && soloReason == null, "TACTICAL-ROWS: solo Tactical is admitted on " + map);
    bool squad = ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, spelling, true, out var squadArea, out var squadReason);
    if (map == ArenaMapCatalog.AutoServiceId)
        Need(!squad && squadArea == null && squadReason?.Contains("yard centre") == true
            && squadReason.Contains("PitFireTeam Tactical squad"), "CHOPSHOP-PFT-TACTICAL: explain the yard-centre blocker");
    else
        Need(squad && squadArea == null && squadReason == null, "TACTICAL-ROWS: squad Tactical is admitted on " + map);
}

foreach (string map in new[] { "arena_equator", "ARENA_EQUATOR" })
foreach (string mode in new[] { "zombie", "ZOMBIE" })
foreach (string? id in new[] { null, "", "  ", ArenaAreaCatalog.FullMapId, "stale-area" })
{
    Need(!ArenaAreaCatalog.TryResolve(id, map, mode, true, out var blocked, out var reason)
        && blocked == null && reason?.Contains("PitFireTeam squad") == true,
        "EQUATOR-PFT-ZOMBIES: reject squad before any full-map or stale-area fallback");
    if (id != "stale-area")
        Need(ArenaAreaCatalog.TryResolve(id, map, mode, false, out _, out _),
            "EQUATOR-SOLO-ZOMBIES: retain solo survival");
}

foreach (string id in areas)
foreach (string mode in modes)
foreach (bool pft in new[] { false, true })
{
    bool admitted = !pft || mode is "tactical" or "zombie";
    Need(ArenaAreaCatalog.TryResolve(id, ArenaMapCatalog.CustomsId, mode, pft, out var area, out var reason)
        == admitted, "BOUNDED-ADAPTER: " + id + "/" + mode + "/" + pft);
    if (!admitted) Need(area == null && !string.IsNullOrWhiteSpace(reason), "BLOCKER: explain unsupported bounded adapter");
    else
    {
        Need(area?.TeamStarts != null, "STARTS: bounded selection retains surveyed faction identities");
        if (mode == "teamdeathmatch")
        {
            foreach (int size in new[] { 2, 3, 4, 5 })
                Need(ArenaAreaCatalog.TryResolveTeamSize(area, mode, pft, size, out int selected, out _) && selected == size,
                    "SIZE-PRESERVED: exact 2v2..5v5 choice");
            Need(!ArenaAreaCatalog.TryResolveTeamSize(area, mode, pft, 6, out _, out _), "SIZE-CEILING: no invented starts");
        }
        else
            Need(ArenaAreaCatalog.TryResolveTeamSize(area, mode, pft, null, out var size, out _) && size == 0,
                "MODE-OWNED-ROSTER: no hidden TDM override");
    }
    Need(ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.CustomsId, mode, true, pft, 8, out _, out _)
        == (!pft && mode is "deathmatch" or "gungame"),
        "BOUNDED-POPULATION: only solo continuous modes negotiate participant totals");
}
foreach (string id in areas)
foreach (string map in new[] { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId, ArenaMapCatalog.FactoryDayId })
    Need(!ArenaAreaCatalog.TryResolve(id, map, "teamdeathmatch", false, out _, out _),
        "MAP-IDENTITY: contour is never borrowed by another map");
foreach (string retired in new[] { "customs-factory-center-v1", "customs-factory-center-polygon-v1",
    "customs-factory-center-teams-v1", "customs-factory-east-polygon-v1", "factory-south-v1", "future-area" })
    Need(!ArenaAreaCatalog.TryResolve(retired, ArenaMapCatalog.CustomsId, "teamdeathmatch", false, out _, out _),
        "RETIRED-ID: old geometry is never silently reopened");

foreach (string map in maps)
foreach (string? legacy in new[] { null, "endless", "rounds", "future-mode" })
    Need(!ArenaAreaCatalog.TryResolve(null, map, legacy, false, out _, out _),
        "CURRENT-MODES: opening new full maps must not enable unidentified/legacy populations");

// SPAWN-ROWS-DATA: the Tactical admissions of 2026-11-13 (Equator, Iceberg, Chop Shop) were decided on
// the Bot rows ArenaMapPort added on 2026-11-12. Older installed data cannot verify them; the pins that
// follow would be red for that reason and not because of an ArenaMode defect, so say so.
string SpawnRowsData(string map, string what)
    => "SPAWN-ROWS-DATA: " + what + " for " + map + ". The installed ArenaMapPort data predates the spawn rows of "
        + "2026-11-12 (build 0.5.0+160523d or a later release is needed; the published 0.5.0 has 10 and 9 rows and no "
        + "Chop Shop). The Tactical admissions of 2026-11-13 were decided on those rows, so they cannot be verified on "
        + "this data, and a red pin on older data is not an ArenaMode defect.";
var minimumRows = new Dictionary<string, int>
{
    [ArenaMapCatalog.EquatorId] = 16, [ArenaMapCatalog.IcebergId] = 17, [ArenaMapCatalog.AutoServiceId] = 16,
};

// Minimum of the production selector's usable-row count over the bounding box of the zone's rows,
// in 1 m steps; the cell's y is the y of the nearest row (x/z distance). Returns the cell of the minimum.
(int Minimum, Vector3 Cell) GridMinimum(BotZone zone)
{
    var rows = zone.SpawnPoints.Select(point => point.Position).ToArray();
    float minX = rows.Min(r => r.x), maxX = rows.Max(r => r.x), minZ = rows.Min(r => r.z), maxZ = rows.Max(r => r.z);
    int best = int.MaxValue; var bestCell = default(Vector3);
    for (float x = minX; x <= maxX; x += 1f)
        for (float z = minZ; z <= maxZ; z += 1f)
        {
            var nearest = rows.OrderBy(r => (r.x - x) * (r.x - x) + (r.z - z) * (r.z - z)).First();
            var cell = new Vector3(x, nearest.y, z);
            int usable = BurstSpawnPointSelector.CountUsable(zone, cell);
            if (usable < best) { best = usable; bestCell = cell; }
        }
    return (best, bestCell);
}

string sptRoot = args.FirstOrDefault() ?? @"D:\SPT41";
foreach (string map in maps)
{
    string file = map == ArenaMapCatalog.CustomsId
        ? Path.Combine(sptRoot, "SPT_Runtime", "SPT_Data", "database", "locations", "bigmap", "base.json")
        : Path.Combine(sptRoot, "SPT_Runtime", "user", "mods", "ArenaMapPort", "db",
            map, "base.json");
    if (minimumRows.ContainsKey(map))
        Need(File.Exists(file), SpawnRowsData(map, "the file " + file + " is missing"));
    Need(File.Exists(file), "DATA-FILE: the installed file " + file + " is missing; this harness reads the installed maps");
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    Need(document.RootElement.GetProperty("Id").GetString() == map, "DATA-ID: inspect the exact installed map");
    // ArenaMapPort's tag v0.5.0 holds a stub arena_autoservice/base.json without any spawn rows; name the
    // reason before the property access below turns it into a raw KeyNotFoundException.
    if (minimumRows.ContainsKey(map))
        Need(document.RootElement.TryGetProperty("SpawnPointParams", out var declared) && declared.GetArrayLength() > 0,
            SpawnRowsData(map, "the file carries no spawn rows (a stub location)"));
    var markers = document.RootElement.GetProperty("SpawnPointParams").EnumerateArray().ToArray();
    Need(markers.All(row => !ArenaTeamBoxCatalog.Owns(row.GetProperty("Id").GetString())),
        "PRIVATE-CLONE-ONLY: installed shared locations contain no per-raid team-box markers");
    Vector3 Position(JsonElement row)
    {
        var p = row.GetProperty("Position");
        return new Vector3(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle(), p.GetProperty("z").GetSingle());
    }
    bool Category(JsonElement row, string value)
        => row.GetProperty("Categories").EnumerateArray().Any(c => c.GetString() == value);
    if (map == ArenaMapCatalog.Bay5Id)
    {
        var donors = markers.Where(row => row.GetProperty("Id").GetString() == "arena_bay5-bot-west-centre").ToArray();
        Need(donors.Length == 1, "BAY5-DONOR: exactly one original west-centre donor");
        var donor = donors.Single();
        var position = Position(donor);
        Need(position.x == 63.497f && position.y == -3.8574f && position.z == 42.2217f,
            "BAY5-DONOR: installed anchor retains its exact authored position");
        Need(donor.GetProperty("BotZoneName").GetString() == "BotZone_Main"
            && donor.GetProperty("CorePointId").GetInt32() == 0
            && donor.GetProperty("DelayToCanSpawnSec").GetInt32() == 4
            && donor.GetProperty("Categories").GetArrayLength() == 2
            && Category(donor, "Bot") && Category(donor, "BotPmc"),
            "BAY5-DONOR: installed native header matches the server admission contract");
    }
    if (map == ArenaMapCatalog.AirPitId)
    {
        var donors = markers.Where(row => row.GetProperty("Id").GetString() == "arena_airpit-bot-center-east").ToArray();
        Need(donors.Length == 1, "AIRPIT-DONOR: exactly one original center-east marker");
        var donor = donors.Single(); var position = Position(donor);
        Need(position.x == -3.21f && position.y == .1f && position.z == -12.09f,
            "AIRPIT-DONOR: installed position matches the admission contract");
        Need(donor.GetProperty("BotZoneName").GetString() == "BotZone_Main"
            && donor.GetProperty("CorePointId").GetInt32() == 0
            && donor.GetProperty("DelayToCanSpawnSec").GetInt32() == 4
            && donor.GetProperty("Categories").GetArrayLength() == 2
            && Category(donor, "Bot") && Category(donor, "BotPmc"),
            "AIRPIT-DONOR: installed header matches the admission contract");
    }
    var zones = markers.Where(row => (Category(row, "Bot") || Category(row, "BotPmc"))
            && row.TryGetProperty("BotZoneName", out var z) && !string.IsNullOrWhiteSpace(z.GetString()))
        .GroupBy(row => row.GetProperty("BotZoneName").GetString())
        .Select(rows => new BotZone { NameZone = rows.Key!, SpawnPoints = rows
            .Select(row => (ISpawnPoint)new Point(row.GetProperty("Id").GetString()!, Position(row))).ToArray() }).ToArray();
    ArenaBotSpawnFilter.CurrentMap = map;
    // Before the squad pins: on older data one of them could fail first with a message that does not name the data.
    if (minimumRows.TryGetValue(map, out int rowsNeeded))
    {
        int rows = zones.SelectMany(z => z.SpawnPoints).Select(point => point.Id).Distinct().Count();
        Need(rows >= rowsNeeded, SpawnRowsData(map, "the file has " + rows + " distinct Bot/BotPmc rows, at least " + rowsNeeded + " are required"));
    }
    var players = markers.Where(row => Category(row, "Player")
        && row.GetProperty("Sides").EnumerateArray().Any(s => s.GetString() is "All" or "Pmc" or "Usec" or "Bear")).ToArray();
    Need(players.Length > 0 && zones.Length > 0, "NATIVE-STARTS: real player and bot markers are required");
    foreach (var player in players)
    {
        int cursor = 0;
        var p = Position(player);
        var zone = BurstSpawnPointSelector.PickZone(zones, p, 5, ref cursor);
        var points = BurstSpawnPointSelector.PickPoints(zone, p, 5, ref cursor, out _, out _, grouped: true);
        Need(points?.Count == 5, "OPENING-SQUAD: native entry has five distinct distant markers: " + map);
        Need(points.All(point => Vector3.Distance(point.Position, p) >= 25f), "DISTANCE: never reduce safety floor");
    }
    string gridLine = "";
    if (minimumRows.ContainsKey(map))
    {
        // The grid and the evidence pins below read the one zone these maps have (BotZone_Main); rows spread
        // over several zones would otherwise end in a raw "Sequence contains more than one element".
        Need(zones.Length == 1, "SPAWN-ROWS-ZONE: the Bot rows of " + map + " are spread over " + zones.Length
            + " BotZoneName values; the Tactical pins were decided on a single zone and are to be re-decided");
        var (gridMinimum, gridCell) = GridMinimum(zones.Single());
        string cellText = FormattableString.Invariant($"({gridCell.x:0.0}, {gridCell.z:0.0})");
        gridLine = $" Grid minimum {gridMinimum} near {cellText}.";
        if (map == ArenaMapCatalog.AutoServiceId)
            Need(gridMinimum >= 1 && gridMinimum < 5, "CHOPSHOP-GRID: the minimum over the bounding box is " + gridMinimum + " near " + cellText
                + "; the PitFireTeam Tactical refusal rests on 1..4 (the yard centre is the pin beside it)");
        else
            Need(gridMinimum >= 5, (map == ArenaMapCatalog.EquatorId ? "EQUATOR-GRID" : "ICEBERG-GRID") + ": the minimum over the bounding box is "
                + gridMinimum + " near " + cellText + "; Tactical with a squad needs five distant rows from every cell");
    }
    // Installed-data evidence of the Tactical decisions, computed with the production selector (distinct
    // coordinates, 3-D distance, the unchanged 25 m floor). A MapPort data change that moves one of
    // these fails here and the decision is to be re-made, not the number silently re-pinned.
    if (map == ArenaMapCatalog.EquatorId)
    {
        // The atrium had four such rows before the rows of 2026-11-12 (nine now); MapPort's mesh minimum is six.
        foreach (var spot in new[] { new Vector3(0f, .2f, 2f), new Vector3(-4f, .2f, 15f) })
            Need(BurstSpawnPointSelector.CountUsable(zones.Single(), spot) >= 5,
                "EQUATOR-CENTER: a surviving leader at " + spot + " must still have five distant rows for Tactical");
        Need(ArenaAreaCatalog.TryResolve(null, map, "tactical", true, out _, out _),
            "EQUATOR-CENTER: with the rows present Tactical is admitted with a squad");
    }
    if (map == ArenaMapCatalog.IcebergId)
    {
        // The old centre rectangle (x -12.6..12.5, z -71.9..-58.8) had four before; MapPort's mesh minimum is ten.
        var near = markers.Where(row => Category(row, "Bot")).Select(Position)
            .OrderBy(p => (p.x - 10f) * (p.x - 10f) + (p.z + 68f) * (p.z + 68f)).First();
        Need(BurstSpawnPointSelector.CountUsable(zones.Single(), new Vector3(10f, near.y, -68f)) >= 5,
            "ICEBERG-CENTER: a surviving leader in the old centre must have five distant rows for Tactical");
    }
    if (map == ArenaMapCatalog.AutoServiceId)
    {
        foreach (var player in players)
            Need(BurstSpawnPointSelector.CountUsable(zones.Single(), Position(player)) >= 5,
                "CHOPSHOP-PLAYER-ROWS: a solo survivor moved to a Player row has five distant rows for Tactical");
        // The yard centre is the evidence of the PitFireTeam refusal: no dead patch any more (the 25 m floor
        // leaves rows) but fewer than the five a squad needs. When MapPort closes this patch the
        // assertion fails and the PitFireTeam Tactical rule in ArenaAreaCatalog is to be re-decided.
        int yard = BurstSpawnPointSelector.CountUsable(zones.Single(), new Vector3(-375f, -3.8f, 487f));
        Need(yard >= 1 && yard < 5, "CHOPSHOP-YARD-CENTER: the yard centre has " + yard + " distant rows; the PitFireTeam Tactical refusal rests on 1..4");
    }
    Console.WriteLine($"Installed {map}: {players.Length} player markers, {zones.Sum(z => z.SpawnPoints.Length)} bot markers; opening squad capacity verified." + gridLine);
}

// EXIT-NAMES: Formation.BoothExits is data without a reader, so only this pin notices a rename on MapPort's
// side. It has its own loop over the six formations: Bowl has a formation and no row in the mode matrix above.
foreach (string map in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.AirPitId,
    ArenaMapCatalog.BowlId, ArenaMapCatalog.IcebergId, ArenaMapCatalog.AutoServiceId })
{
    string file = Path.Combine(sptRoot, "SPT_Runtime", "user", "mods", "ArenaMapPort", "db", map, "base.json");
    Need(File.Exists(file), "EXIT-NAMES: the installed file " + file + " is missing; install an ArenaMapPort build that ships " + map);
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    var booth = ArenaTeamBoxCatalog.Find(map) ?? throw new InvalidOperationException("EXIT-NAMES: no team-box formation for " + map);
    string[] installedExits = document.RootElement.TryGetProperty("exits", out var exits)
        ? exits.EnumerateArray().Select(exit => exit.GetProperty("Name").GetString()!).ToArray()
        : Array.Empty<string>();
    Need(installedExits.Length == 2 && booth.BoothExits.Count == 2
        && new HashSet<string>(installedExits).SetEquals(booth.BoothExits),
        "EXIT-NAMES: " + map + " BoothExits [" + string.Join(", ", booth.BoothExits) + "] must equal the installed exits ["
        + string.Join(", ", installedExits) + "]");
}

Console.WriteLine($"MAP_MODE_ADMISSION_CHECKS_PASSED ({assertions} assertions)");
