using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Checks the current asset/provenance record, not legal rights or audible speech.
// Source authority remains the reviewed intake; do not infer a CC grant from a
// historical license file left beside a replacement pack.
internal static class AnnouncerAssetContract
{
    private static readonly HashSet<string> ActiveKeys = new(
        ("double_kill triple_kill multi_kill monster_kill killing_spree rampage " +
         "dominating unstoppable godlike first_blood headshot payback last_man_standing " +
         "round_start round_won round_lost match_won match_lost").Split(' '), StringComparer.Ordinal);

    private static readonly Dictionary<string, string> ReviewedLifecycle = new()
    {
        ["round_start"] = "ROUND START", ["round_won"] = "ROUND WINNER",
        ["round_lost"] = "MISSION FAILED", ["match_won"] = "MISSION ACCOMPLISHED",
        ["match_lost"] = "GAME OVER", ["multi_kill"] = "ULTRA KILL"
    };

    // Independent reviewed contract. Do not derive this allowlist from the
    // production catalog: a catalog expansion must fail until reviewed here.
    internal static readonly IReadOnlyDictionary<string, string> ReserveRuntimeBindings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prepare_to_fight"] = "PREPARE TO FIGHT", ["three"] = "THREE", ["two"] = "TWO", ["one"] = "ONE",
            ["taken_the_lead"] = "TAKEN THE LEAD", ["lost_the_lead"] = "LOST THE LEAD", ["tied_for_the_lead"] = "TIED FOR THE LEAD",
            ["one_minute_remaining"] = "ONE MINUTE REMAINING", ["thirty_seconds_remaining"] = "THIRTY SECONDS REMAINING",
            ["ten_seconds_remaining"] = "TEN SECONDS REMAINING", ["five_kills_remaining"] = "FIVE KILLS REMAINING",
            ["one_kill_remaining"] = "ONE KILL REMAINING", ["match_point"] = "MATCH POINT", ["final_round"] = "FINAL ROUND",
            ["final_weapon"] = "FINAL WEAPON", ["ladder_complete"] = "LADDER COMPLETE", ["draw"] = "DRAW",
            ["shutdown"] = "SHUTDOWN", ["clutch"] = "CLUTCH"
        };

    private static readonly HashSet<string> UnboundReserveKeys = new(
        ("round_won round_lost victory defeat your_flag_has_been_taken enemy_flag_taken " +
         "flag_dropped flag_returned flag_captured").Split(' '), StringComparer.Ordinal);

    internal const string AdditionsManifest = "ANNOUNCER-ADDITIONS-20260917.json";

    // The 2026-09-17 supplement, reviewed here independently of its own Python
    // verifier and of the production catalog. It is a SEPARATE manifest because
    // the original 46 recordings must stay byte-identical. Their record now uses
    // the promoted package paths; coverage below remains the union of both records.
    private static readonly HashSet<string> AdditionActiveKeys = new(
        "no_scope airborne no_contest embarrassing".Split(' '), StringComparer.Ordinal);

    // Recorded but deliberately not wired: absent from the production reserve
    // allowlist, so the runtime loader must never pick them up.
    private static readonly HashSet<string> AdditionReserveKeys = new(
        "trickshot air_superiority".Split(' '), StringComparer.Ordinal);

    internal static JsonObject Read(string assets)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "docs", "licenses", "audio", "ANNOUNCER-SOURCES.json")))!.AsObject();

    internal static JsonObject ReadAdditions(string assets)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "docs", "licenses", "audio", AdditionsManifest)))!.AsObject();

    /// <summary>
    /// The supplement's own record: provenance, exactly the six reviewed
    /// destinations, and a hash per file. Deliberately not a re-implementation
    /// of verify_additions.py's audio decoding — this layer answers "is the
    /// installed set the reviewed set", which is what the coverage check leans on.
    /// </summary>
    internal static void ValidateAdditions(string assets, JsonObject manifest)
    {
        foreach (string field in new[] { "source", "generator", "rights", "licenseChecked", "licenseSource", "termsSource" })
            Require(!string.IsNullOrWhiteSpace(manifest[field]?.GetValue<string>()), "missing addition provenance: " + field);
        Require(Regex.IsMatch(manifest["sourceSha256"]?.GetValue<string>() ?? "", @"\A[0-9A-Fa-f]{64}\z"),
            "invalid addition source fingerprint");
        foreach (string field in new[] { "licenseSource", "termsSource" })
            Require(Uri.TryCreate(manifest[field]!.GetValue<string>(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps, "invalid addition provenance URL: " + field);

        var active = new HashSet<string>(StringComparer.Ordinal);
        var reserve = new HashSet<string>(StringComparer.Ordinal);
        var rows = manifest["files"]!.AsArray();
        foreach (var node in rows)
        {
            var row = node!.AsObject();
            string path = row["file"]!.GetValue<string>();
            Require(Regex.IsMatch(path, @"\Aannouncements/(?:reserve/)?[a-z0-9_]+\.ogg\z"), "unsafe addition path");
            string basename = Path.GetFileNameWithoutExtension(path);
            bool isActive = row["usage"]!.GetValue<string>() == "active";
            Require(isActive == !path.StartsWith("announcements/reserve/", StringComparison.Ordinal),
                "wrong addition loading scope");
            Require(!string.IsNullOrWhiteSpace(row["spoken"]?.GetValue<string>()), "missing addition spoken label");
            if (isActive)
            {
                Require(row["eventKey"]?.GetValue<string>() == basename, "addition event/file mapping differs");
                Require(active.Add(basename), "duplicate addition cue");
            }
            else
            {
                Require(row["eventKey"] == null, "reserve addition has an event");
                Require(reserve.Add(basename), "duplicate addition reserve cue");
            }

            string file = Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(file) && new FileInfo(file).Length > 0, "missing or empty addition: " + path);
            using var stream = File.OpenRead(file);
            Require(string.Equals(Convert.ToHexString(SHA256.HashData(stream)), row["sha256"]?.GetValue<string>(),
                StringComparison.OrdinalIgnoreCase), "addition checksum differs: " + path);
        }

        Require(rows.Count == 6 && active.SetEquals(AdditionActiveKeys) && reserve.SetEquals(AdditionReserveKeys),
            "addition cue set differs");
        // An addition may never shadow or replace one of the original 46.
        Require(!active.Overlaps(ActiveKeys) && !reserve.Overlaps(ReserveRuntimeBindings.Keys),
            "addition collides with the original pack");
    }

    private static IEnumerable<string> AdditionPaths()
        => AdditionActiveKeys.Select(key => "announcements/" + key + ".ogg")
            .Concat(AdditionReserveKeys.Select(key => "announcements/reserve/" + key + ".ogg"));

    internal static void Validate(string assets, JsonObject manifest)
    {
        foreach (string field in new[] { "source", "generator", "rights", "licenseChecked", "licenseSource", "termsSource" })
            Require(!string.IsNullOrWhiteSpace(manifest[field]?.GetValue<string>()), "missing provenance: " + field);
        Require(Regex.IsMatch(manifest["sourceSha256"]?.GetValue<string>() ?? "", @"\A[0-9A-Fa-f]{64}\z"),
            "invalid source fingerprint");
        foreach (string field in new[] { "licenseSource", "termsSource" })
            Require(Uri.TryCreate(manifest[field]!.GetValue<string>(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps, "invalid provenance URL: " + field);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var runtimeKeys = new HashSet<string>(StringComparer.Ordinal);
        int reserved = 0;
        int unbound = 0;
        var rows = manifest["files"]!.AsArray();
        foreach (var node in rows)
        {
            var row = node!.AsObject();
            string path = row["file"]!.GetValue<string>();
            Require(Regex.IsMatch(path, @"\Aannouncements/(?:reserve/)?[a-z0-9_]+\.ogg\z"), "unsafe asset path");
            Require(paths.Add(path), "duplicate asset path");
            string usage = row["usage"]!.GetValue<string>();
            bool active = usage == "active";
            Require(active || usage == "reserve", "unknown loading scope");
            // Keep the historical intake classification while the 19 subsequently
            // enabled cues now ship at the root alongside the original active set.
            bool promoted = ReserveRuntimeBindings.ContainsKey(Path.GetFileNameWithoutExtension(path));
            Require((active || promoted) == !path.StartsWith("announcements/reserve/", StringComparison.Ordinal), "wrong loading scope");
            string? key = row["eventKey"]?.GetValue<string>();
            string? runtimeKey = row["runtimeEventKey"]?.GetValue<string>();
            Require(!string.IsNullOrWhiteSpace(row["spoken"]?.GetValue<string>()), "missing spoken label");
            if (active)
            {
                Require(runtimeKey == null, "active cue has a reserve runtime binding");
                Require(key == Path.GetFileNameWithoutExtension(path), "event/file mapping differs");
                Require(keys.Add(key!), "duplicate active cue");
                if (ReviewedLifecycle.TryGetValue(key!, out var spoken))
                    Require(row["spoken"]!.GetValue<string>() == spoken, "reviewed lifecycle mapping differs: " + key);
            }
            else
            {
                reserved++;
                Require(key == null, "reserve cue has an event");
                string basename = Path.GetFileNameWithoutExtension(path);
                if (runtimeKey != null)
                {
                    Require(runtimeKeys.Add(runtimeKey), "duplicate reserve runtime cue");
                    Require(ReserveRuntimeBindings.TryGetValue(basename, out var spoken), "unsupported reserve runtime binding");
                    Require(runtimeKey == basename, "reserve runtime event/file mapping differs");
                    Require(row["spoken"]!.GetValue<string>() == spoken, "reviewed reserve label differs");
                }
                else
                {
                    Require(!ReserveRuntimeBindings.ContainsKey(basename), "missing reserve runtime binding");
                    Require(UnboundReserveKeys.Contains(basename), "unreviewed unbound reserve cue");
                    unbound++;
                }
            }
            string file = Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(file) && new FileInfo(file).Length > 0, "missing or empty cue: " + path);
            using var stream = File.OpenRead(file);
            Require(string.Equals(Convert.ToHexString(SHA256.HashData(stream)), row["sha256"]?.GetValue<string>(),
                StringComparison.OrdinalIgnoreCase), "cue checksum differs: " + path);
        }
        Require(keys.SetEquals(ActiveKeys) && reserved == 28 && rows.Count == 46, "active/reserve cue set differs");
        Require(runtimeKeys.SetEquals(ReserveRuntimeBindings.Keys) && unbound == 9, "runtime/unbound reserve cue set differs");
        var actual = Directory.GetFiles(Path.Combine(assets, "announcements"), "*.ogg", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/'));
        // Coverage spans both records: an installed file recorded in neither is
        // still an unrecorded cue, which is the property this protects.
        var recorded = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        recorded.UnionWith(AdditionPaths());
        Require(recorded.SetEquals(actual), "missing or unrecorded cue files");
    }

    internal static void ValidateLoadedPaths(string assets, IReadOnlyList<string> files)
    {
        var paths = files.Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/')).ToArray();
        Require(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == paths.Length, "duplicate runtime load path");
        var expected = new HashSet<string>(ActiveKeys.Select(key => "announcements/" + key + ".ogg"), StringComparer.Ordinal);
        expected.UnionWith(AdditionActiveKeys.Select(key => "announcements/" + key + ".ogg"));
        expected.UnionWith(ReserveRuntimeBindings.Keys.Select(key => "announcements/" + key + ".ogg"));
        // 18 original active + 4 reviewed additions + 19 bound reserve. The two
        // reserve additions are absent from the allowlist and must stay unloaded.
        Require(expected.SetEquals(paths) && paths.Length == 41, "runtime loaded cue set differs");
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition) throw new InvalidDataException(problem);
    }
}
