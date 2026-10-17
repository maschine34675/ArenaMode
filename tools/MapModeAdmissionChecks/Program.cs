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

string[] maps = { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId, ArenaMapCatalog.AirPitId, ArenaMapCatalog.CustomsId };
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
    if (ArenaMapCatalog.IsEquator(map) && mode == "tactical")
    {
        Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out var blocked, out var blocker)
            && blocked == null && blocker?.Contains("five-opponent") == true, "EQUATOR-SQUAD: explain the exact capacity blocker");
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
            && (map == ArenaMapCatalog.EquatorId || map == ArenaMapCatalog.Bay5Id || map == ArenaMapCatalog.AirPitId));
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
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, true, out var area, out _)
        == !(map == ArenaMapCatalog.CustomsId || ArenaMapCatalog.IsEquator(map)) && area == null,
        "PFT-FULL: authenticated squad observes exact full-map capacity " + map + "/" + mode);

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

string sptRoot = args.FirstOrDefault() ?? @"D:\SPT41";
foreach (string map in maps)
{
    string file = map == ArenaMapCatalog.CustomsId
        ? Path.Combine(sptRoot, "SPT_Runtime", "SPT_Data", "database", "locations", "bigmap", "base.json")
        : Path.Combine(sptRoot, "SPT_Runtime", "user", "mods", "ArenaMapPort", "db",
            map, "base.json");
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    Need(document.RootElement.GetProperty("Id").GetString() == map, "DATA-ID: inspect the exact installed map");
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
    if (map == ArenaMapCatalog.EquatorId)
    {
        var center = new Vector3(0f, .2f, 2f);
        Need(BurstSpawnPointSelector.CountUsable(zones.Single(), center) == 4,
            "EQUATOR-CENTER: real mid-map survivor cannot receive five Tactical opponents");
        Need(!ArenaAreaCatalog.TryResolve(null, map, "tactical", true, out _, out _),
            "EQUATOR-CENTER: admission must retain actual capacity blocker");
    }
    Console.WriteLine($"Installed {map}: {players.Length} player markers, {zones.Sum(z => z.SpawnPoints.Length)} bot markers; opening squad capacity verified.");
}

Console.WriteLine($"MAP_MODE_ADMISSION_CHECKS_PASSED ({assertions} assertions)");
