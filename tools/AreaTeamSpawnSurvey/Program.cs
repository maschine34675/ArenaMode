using ArenaMode.Shared;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    if (args.Length < 2) throw new InvalidOperationException("Usage: AreaTeamSpawnSurvey <SPT root> <proposal.json> [fault]");
    var proposal = JsonSerializer.Deserialize<Proposal>(File.ReadAllText(args[1]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var surveys = new List<AreaSurvey>();
    foreach (var (area, expectedPlayers, expectedBots) in new[] {
        (ArenaAreaCatalog.CustomsFactoryCenterPolygon, 17, 11),
        (ArenaAreaCatalog.CustomsFactoryCenter, 15, 7),
        (ArenaAreaCatalog.FactorySouth, 20, 9) })
    {
        string path = Path.Combine(args[0], "SPT_Runtime", "SPT_Data", "database", "locations", area.MapId, "base.json");
        var entries = ReadEntries(path);
        Need(entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() == entries.Length, $"{area.Id}: duplicate source IDs");
        var allowed = entries.Where(e => Allowed(area, e)).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        Need(allowed.Count(e => Player(e, "Usec")) == expectedPlayers
            && allowed.Count(e => Player(e, "Bear")) == expectedPlayers,
            $"{area.Id}: original PMC corpus drifted");
        Need(allowed.Count(Bot) == expectedBots, $"{area.Id}: original bot corpus drifted");
        foreach (var e in allowed)
        {
            Need(float.IsFinite(e.X) && float.IsFinite(e.Y) && float.IsFinite(e.Z), $"nonfinite position: {e.Id}");
            Need(e.Sides.All(v => v is "None" or "Usec" or "Bear" or "Savage" or "Pmc" or "All"), $"unsupported side mask: {e.Id}");
        }
        Need(allowed.Where(e => Player(e, "Usec") || Bot(e)).Select(e => (e.X,e.Y,e.Z)).Distinct().Count()
            == allowed.Count(e => Player(e, "Usec") || Bot(e)), $"{area.Id}: duplicate usable positions");
        if (area.Id == ArenaAreaCatalog.CustomsFactoryCenterPolygonId)
        {
            const string sniperId = "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e";
            var sniper = entries.Single(e => e.Id == sniperId);
            Need(area.Bounds.Contains(sniper.X, sniper.Y, sniper.Z, ArenaAreaCatalog.SpawnInset)
                && area.IsExcludedSpawn(sniper.Id) && !allowed.Any(e => e.Id == sniper.Id), "sniper identity exclusion drifted");
            Need(!area.Bounds.LimitsHeight, "survey contour must retain unrestricted height");
        }
        Console.WriteLine($"\nAREA {area.Id}; accepted={allowed.Length}; USEC={allowed.Count(e => Player(e,"Usec"))}; BEAR={allowed.Count(e => Player(e,"Bear"))}; bots={allowed.Count(Bot)}");
        Console.WriteLine($"Source SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
        string canonical = string.Join("\n", allowed.Select(e => string.Join("|", e.Id,
            e.X.ToString("R"),e.Y.ToString("R"),e.Z.ToString("R"),string.Join(",",e.Categories),string.Join(",",e.Sides),e.Zone)));
        Console.WriteLine($"Allowed-corpus SHA256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}");
        Console.WriteLine("ID | X | Y | Z | categories | native sides | zone | local PMC USEC/BEAR | bot");
        foreach (var e in allowed)
            Console.WriteLine($"{e.Id} | {e.X:R} | {e.Y:R} | {e.Z:R} | {string.Join(',', e.Categories)} | {string.Join(',', e.Sides)} | {e.Zone} | {Player(e,"Usec")}/{Player(e,"Bear")} | {Bot(e)}");
        surveys.Add(new AreaSurvey(area, allowed));
    }
    var contour = surveys.Single(s => s.Area.Id == proposal.AreaId);
    string? fault = args.ElementAtOrDefault(2);
    if (fault == "missing-west-bot") contour.Entries = contour.Entries.Where(e => e.Id != proposal.Sides[0].Bots[0]).ToArray();
    if (fault == "invalid-pmc-mask") contour.Entries = contour.Entries.Select(e => e.Id == proposal.Sides[0].Players[0] ? e with { Sides = ["Savage"] } : e).ToArray();
    if (fault == "collapsed-west-bots")
    {
        var first = contour.Entries.Single(e => e.Id == proposal.Sides[0].Bots[0]);
        contour.Entries = contour.Entries.Select(e => e.Id == proposal.Sides[0].Bots[1] ? e with { X=first.X,Y=first.Y,Z=first.Z } : e).ToArray();
    }
    if (fault == "excluded-sniper") proposal.Sides[0].Bots[0] = "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e";
    Need(fault == null || fault is "missing-west-bot" or "invalid-pmc-mask" or "collapsed-west-bots" or "excluded-sniper", "unknown deliberate fault");
    Need(proposal.Sides.Length == 2 && proposal.Sides[0].Name == "west" && proposal.Sides[1].Name == "east", "expected two explicit opposing start regions");
    var allIds = proposal.Sides.SelectMany(s => s.Players.Concat(s.Bots)).ToArray();
    Need(allIds.Distinct(StringComparer.Ordinal).Count() == allIds.Length, "start pools overlap or repeat identities");
    var lookup = contour.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    Entry Resolve(string id)
    {
        Need(!contour.Area.IsExcludedSpawn(id), "start proposal includes excluded sniper");
        Need(lookup.ContainsKey(id), "start proposal is missing an allowed marker: " + id);
        return lookup[id];
    }
    foreach (var side in proposal.Sides)
    {
        var players = side.Players.Select(Resolve).ToArray();
        var bots = side.Bots.Select(Resolve).ToArray();
        Need(players.Length >= 2 && players.All(e => Player(e, "Usec") && Player(e, "Bear")), "initial PMC pool must permit either faction");
        Need(bots.Length >= proposal.MaxTeamSize && bots.All(Bot), "initial bot pool has insufficient native bot candidates");
        var starts = players.Concat(bots).ToArray();
        Need(starts.Select(e => (e.X,e.Y,e.Z)).Distinct().Count() == starts.Length, "start pool has duplicate physical positions");
        // Player alternatives never coexist: pairwise spacing applies to bots
        // and every possible local player vs bots, not alternative PMC markers.
        double spacing = Math.Min(PairMinimum(bots), players.Min(p => bots.Min(b => Distance(p,b))));
        Need(spacing >= proposal.MinimumSameTeamStartDistance, "same-side occupied starts violate the assumed spacing");
        Console.WriteLine($"\nSTART {side.Name}: PMC choices={players.Length} per faction, bot choices={bots.Length}, minimum simultaneously occupied XZ spacing={spacing:F3}m");
        Console.WriteLine($"Envelope X {starts.Min(e=>e.X):F3}..{starts.Max(e=>e.X):F3}; Z {starts.Min(e=>e.Z):F3}..{starts.Max(e=>e.Z):F3}; maximum same-side span={PairMaximum(starts):F3}m");
        for (int size = 2; size <= proposal.MaxTeamSize; size++)
            foreach (string faction in new[] { "Usec", "Bear" })
            {
                Need(players.Count(e => Player(e, faction)) >= 2 && bots.Length >= size && bots.Length >= size - 1, "team capacity failure");
                Console.WriteLine($"CAPACITY {side.Name} local={faction} size={size}: allied bots {size-1}/{bots.Length}; when opposite local, enemy bots {size}/{bots.Length}; distinct local choices {players.Length}");
            }
    }
    var west = proposal.Sides[0].Players.Concat(proposal.Sides[0].Bots).Select(Resolve).ToArray();
    var east = proposal.Sides[1].Players.Concat(proposal.Sides[1].Bots).Select(Resolve).ToArray();
    double opposingDistance = west.Min(w => east.Min(e => Distance(w,e)));
    Need(opposingDistance >= proposal.MinimumOpposingStartDistance, "opposing start separation is too small");
    Need(west.Max(e=>e.X) < east.Min(e=>e.X), "opposing X ordering drifted");
    Console.WriteLine($"\nOpposing minimum XZ separation={opposingDistance:F3}m; empty X band={east.Min(e=>e.X)-west.Max(e=>e.X):F3}m");
    Console.WriteLine("Legacy areas remain 2v2: their full bot corpora cannot supply two disjoint five-bot pools.");
    Need(surveys.Skip(1).All(s => s.Area.MaxTeamSize == 2 && s.Entries.Count(Bot) < proposal.MaxTeamSize * 2), "legacy capacity assumptions drifted");
    Console.WriteLine("This proves database identity/category/side-mask/geometry capacity only; no NavMesh, visibility, collision, blocked-marker, route length or battle-balance claim.");
    Console.WriteLine("ARENA_TEAM_SPAWN_SURVEY_PASSED");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("SURVEY_REJECTED: " + ex.Message);
    return 1;
}

static bool Allowed(ArenaAreaDefinition area, Entry e) => area.AllowsSpawn(e.X,e.Y,e.Z,e.Id)
    && !ArenaBotSpawnPolicy.IsExcludedPoint(area.MapId,e.Id) && !ArenaBotSpawnPolicy.IsExcludedZone(area.MapId,e.Zone);
static bool Player(Entry e, string faction) => e.Categories.Contains("Player")
    && e.Sides.All(v => v is "None" or "Usec" or "Bear" or "Savage" or "Pmc" or "All")
    && e.Sides.Any(v => v == faction || v is "Pmc" or "All");
static bool Bot(Entry e) => !string.IsNullOrWhiteSpace(e.Zone)
    && (e.Categories.Contains("Bot") || e.Categories.Contains("BotPmc"));
static double Distance(Entry a, Entry b) => Math.Sqrt(Math.Pow((double)a.X-b.X,2)+Math.Pow((double)a.Z-b.Z,2));
static double PairMinimum(Entry[] entries) => entries.SelectMany((a,i) => entries.Skip(i+1).Select(b=>Distance(a,b))).Min();
static double PairMaximum(Entry[] entries) => entries.SelectMany((a,i) => entries.Skip(i+1).Select(b=>Distance(a,b))).Max();
static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
static Entry[] ReadEntries(string path)
{
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    return json.RootElement.GetProperty("SpawnPointParams").EnumerateArray().Select(entry =>
    {
        var p = entry.GetProperty("Position");
        return new Entry(entry.GetProperty("Id").GetString()!,p.GetProperty("x").GetSingle(),p.GetProperty("y").GetSingle(),p.GetProperty("z").GetSingle(),
            entry.GetProperty("Categories").EnumerateArray().Select(v=>v.GetString()!).ToArray(),
            entry.GetProperty("Sides").EnumerateArray().Select(v=>v.GetString()!).ToArray(),entry.GetProperty("BotZoneName").GetString() ?? "");
    }).ToArray();
}
record Entry(string Id, float X, float Y, float Z, string[] Categories, string[] Sides, string Zone);
sealed class AreaSurvey(ArenaAreaDefinition area, Entry[] entries) { public ArenaAreaDefinition Area=area; public Entry[] Entries=entries; }
sealed class Proposal
{
    public string AreaId { get; set; } = "";
    public int MaxTeamSize { get; set; }
    public double MinimumOpposingStartDistance { get; set; }
    public double MinimumSameTeamStartDistance { get; set; }
    public Side[] Sides { get; set; } = [];
}
sealed class Side
{
    public string Name { get; set; } = "";
    public string[] Players { get; set; } = [];
    public string[] Bots { get; set; } = [];
}
