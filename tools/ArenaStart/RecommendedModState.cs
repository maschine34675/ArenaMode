using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArenaStart;

// Mutable files are never copied from normal SPT or made part of the package
// hash catalog. These narrow paths also remain valid after an optional package
// is removed, so uninstall/reinstall does not discard private preferences.
internal static class RecommendedModState
{
    internal const string Sain = "SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/";
    internal const string Apbs = "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/";
    internal const string Abps = "SPT_Runtime/user/mods/acidphantasm-botplacementsystem/";
    private static readonly string[] Settings =
    [
        "BepInEx/config/me.sol.sain.cfg", "BepInEx/config/xyz.drakia.bigbrain.cfg",
        "BepInEx/config/xyz.drakia.waypoints.cfg", "BepInEx/config/com.acidphantasm.botplacementsystem.cfg",
        "BepInEx/plugins/SAIN/EditorDefaults.json", Sain + "CoreOverrides.json", Sain + "ServerConfig.json",
        Apbs + "config.json", Apbs + "blacklists.json", Abps + "config.json"
    ];
    private static readonly string[] Locations = ["bigmap", "factory4", "factory4_day", "factory4_night",
        "interchange", "laboratory", "labyrinth", "lighthouse", "rezervbase", "sandbox", "sandbox_high",
        "shoreline", "tarkovstreets", "woods"];

    private static bool SafeName(string name) => name.Length is > 0 and <= 120 &&
        name.Trim() == name && !name.EndsWith('.') && name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\') &&
        !name.Contains(".sync-conflict-", StringComparison.OrdinalIgnoreCase) &&
        !Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool DirectJson(string path, string prefix) => path.StartsWith(prefix, StringComparison.Ordinal) &&
        path.EndsWith(".json", StringComparison.Ordinal) && SafeName(path[prefix.Length..^5]);

    private static bool ApbsPreset(string path)
    {
        if (!path.StartsWith(Apbs + "Presets/", StringComparison.Ordinal)) return false;
        var parts = path[(Apbs.Length + "Presets/".Length)..].Split('/');
        if (parts.Length < 2 || !SafeName(parts[0])) return false;
        if (parts.Length == 2) return parts[1] == "manifest.json";
        if (parts.Length != 3) return false;
        return new[] { "Ammo", "Appearance", "Chances", "Equipment", "Mods" }.Any(category =>
            parts[1] == category && Enumerable.Range(1, 7).Any(tier => parts[2] == $"Tier{tier}_{category.ToLowerInvariant()}.json"));
    }

    private static bool OwnBossProgress(string path, string? accountId) => accountId is { Length: 24 } &&
        accountId.All(Uri.IsHexDigit) && path == Abps + "Data/" + accountId + ".json";

    internal static bool Preserve(string path, string? accountId) =>
        Settings.Contains(path, StringComparer.Ordinal) || DirectJson(path, Sain + "Presets/") ||
        ApbsPreset(path) || OwnBossProgress(path, accountId);

    internal static bool Allows(string path, string? accountId)
    {
        if (Preserve(path, accountId)) return true;
        // SPT's atomic writer can leave its exact .bak after interruption. It is
        // allowed on disk but never imported/restored as the active setting.
        if (path.EndsWith(".bak", StringComparison.Ordinal))
        {
            var active = path[..^4];
            return active == Sain + "CoreOverrides.json" || active == Sain + "ServerConfig.json" ||
                active == Apbs + "config.json" || active == Apbs + "blacklists.json" || active == Abps + "config.json" ||
                DirectJson(active, Sain + "Presets/");
        }
        if (path == Sain + "cache/releases.json" || path == Apbs + "wwwroot/files/RELEASE_NOTES.txt") return true;
        if (Locations.Any(id => path == "BepInEx/plugins/DrakiaXYZ-Waypoints/mesh/" + id + ".json")) return true;
        if (path.StartsWith(Apbs, StringComparison.Ordinal))
        {
            var suffix = path[Apbs.Length..];
            return Regex.IsMatch(suffix, @"^GeneratedVanillaMappings-DO_NOT_TOUCH/[A-Za-z0-9_-]+\.json$", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(suffix, @"^logs/(debug|warnings|errors|success)\.txt$", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(suffix, @"^logs/(boss|event|pmc|scav|special|unhandled)_generation\.json(\.tmp)?$", RegexOptions.CultureInvariant);
        }
        return false;
    }

    internal static string? AccountId(string root)
    {
        var file = EnvironmentCopy.Inside(root, EnvironmentCopy.AccountPath);
        if (!File.Exists(file)) return null;
        using var json = JsonDocument.Parse(EnvironmentCopy.ReadPrivate(file));
        return json.RootElement.GetProperty("profileId").GetString();
    }

    internal static IEnumerable<string> PreservedFiles(string root)
    {
        var accountId = AccountId(root);
        // Include absent fixed paths so rebuild's readback notices their creation.
        var paths = new HashSet<string>(Settings, StringComparer.Ordinal);
        foreach (var prefix in new[] { Sain + "Presets", Apbs + "Presets", Abps + "Data" })
        {
            if (!Directory.Exists(EnvironmentCopy.Inside(root, prefix))) continue;
            foreach (var path in EnvironmentCopy.Walk(root, prefix))
                if (Preserve(path, accountId)) paths.Add(path);
        }
        return paths.Order(StringComparer.Ordinal);
    }

    internal static void Validate(string root)
    {
        var config = EnvironmentCopy.Inside(root, Apbs + "config.json");
        if (!File.Exists(config)) return;
        using var json = JsonDocument.Parse(EnvironmentCopy.ReadPrivate(config));
        ValidatePresetConfiguration(json.RootElement);
    }

    internal static void ValidatePresetConfiguration(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid APBS configuration.");
        foreach (var property in config.EnumerateObject())
        {
            if (!property.Name.Equals("presetName", StringComparison.OrdinalIgnoreCase)) continue;
            var name = property.Value.GetString();
            if (name != null && name.Length != 0 && !SafeName(name))
                throw new InvalidDataException("Private APBS presetName must be one local directory name, without path components.");
        }
    }

    internal static void EnsureDirectories(Manifest manifest)
    {
        if (manifest.Files.Any(f => f.Path == Apbs + "acidphantasm-progressivebotsystem.dll"))
        {
            // APBS's startup logger writes warnings before it creates any
            // bot-generation logs; a fresh install must already have logs/.
            foreach (var folder in new[] { "Presets", "logs" })
            {
                var path = EnvironmentCopy.Inside(manifest.Root, Apbs + folder);
                EnvironmentCopy.AssertAncestors(path);
                Directory.CreateDirectory(path);
            }
        }
    }
}
