using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ArenaStart;

// Copy locally installed packages only. These foreign mods are not distributed
// with Arena. Their generated settings, presets, logs and profiles are excluded.
internal static class RecommendedModPackage
{
    internal const string BigBrain = "BepInEx/plugins/DrakiaXYZ-BigBrain.dll";
    internal const string WaypointsDirectory = "BepInEx/plugins/DrakiaXYZ-Waypoints/";
    internal const string Waypoints = WaypointsDirectory + "DrakiaXYZ-Waypoints.dll";
    internal const string SainClientDirectory = "BepInEx/plugins/SAIN/";
    internal const string SainServerDirectory = "SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/";
    internal const string ApbsDirectory = "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/";
    internal const string AbpsClientDirectory = "BepInEx/plugins/acidphantasm-botplacementsystem/";
    internal const string AbpsServerDirectory = "SPT_Runtime/user/mods/acidphantasm-botplacementsystem/";

    private sealed record Package(string Name, string[] Dlls, string[] Required, string[] Roots, string[] Notices);

    private static readonly string[] WaypointMaps =
        ["bigmap", "factory4", "interchange", "laboratory", "labyrinth", "lighthouse", "rezervbase",
            "sandbox", "shoreline", "tarkovstreets", "woods"];
    private static readonly string[] TierCategories = ["Ammo", "Appearance", "Chances", "Equipment", "Mods"];
    private static readonly Package[] Packages =
    [
        new("SAIN",
            [SainClientDirectory + "SAIN.dll", SainClientDirectory + "SAIN.Preset.Shared.dll",
                SainServerDirectory + "SAINServerMod.dll", SainServerDirectory + "SAIN.ServerInterop.dll",
                SainServerDirectory + "SAIN.Preset.Shared.dll"],
            [SainClientDirectory + "SAIN.dll", SainClientDirectory + "SAIN.Preset.Shared.dll",
                SainServerDirectory + "SAINServerMod.dll", SainServerDirectory + "SAIN.ServerInterop.dll",
                SainServerDirectory + "SAIN.Preset.Shared.dll", SainServerDirectory + "Data/NicknamePersonalities.json",
                SainServerDirectory + "wwwroot/css/sain.css"],
            [SainClientDirectory, SainServerDirectory],
            [SainClientDirectory + "LICENSE", SainServerDirectory + "LICENSE"]),
        new("APBS", [ApbsDirectory + "acidphantasm-progressivebotsystem.dll"],
            [ApbsDirectory + "acidphantasm-progressivebotsystem.dll",
                .. TierCategories.SelectMany(category => Enumerable.Range(0, 8)
                    .Select(tier => ApbsDirectory + $"Data/{category}/Tier{tier}_{category.ToLowerInvariant()}.json")),
                ApbsDirectory + "Data/DefaultConfigs/config.default.json",
                ApbsDirectory + "Data/DefaultConfigs/blacklists.default.json",
                ApbsDirectory + "Data/Quests/QuestData.json", ApbsDirectory + "Data/Tiers/TierData.json",
                ApbsDirectory + "wwwroot/css/app.css", ApbsDirectory + "wwwroot/files/ReleaseNotes.json",
                ApbsDirectory + "wwwroot/images/background.jpg"],
            [ApbsDirectory], [ApbsDirectory + "LICENSE"]),
        new("ABPS", [AbpsClientDirectory + "acidphantasm-botplacementsystem.dll",
                AbpsServerDirectory + "acidphantasm-botplacementsystem.dll"],
            [AbpsClientDirectory + "acidphantasm-botplacementsystem.dll", AbpsServerDirectory + "acidphantasm-botplacementsystem.dll",
                .. new[] { "Bosses", "Hostility", "MapZones", "PMCs", "Scavs", "config.default" }
                    .Select(name => AbpsServerDirectory + $"Defaults/{name}.json"),
                AbpsServerDirectory + "wwwroot/css/app.css", AbpsServerDirectory + "wwwroot/files/ReleaseNotes.json",
                AbpsServerDirectory + "wwwroot/images/background.jpg"],
            [AbpsClientDirectory, AbpsServerDirectory],
            [AbpsClientDirectory + "LICENSE", AbpsServerDirectory + "LICENSE"]),
        // The plugin root contains other mods: never recursively scan that root
        // or treat a shared LICENSE file there as BigBrain's own notice.
        new("BigBrain", [BigBrain], [BigBrain], [], []),
        new("Waypoints", [Waypoints],
            [Waypoints, .. WaypointMaps.Select(map => WaypointsDirectory + $"navmesh/{map}-navmesh.bundle")],
            [WaypointsDirectory], [WaypointsDirectory + "LICENSE"])
    ];
    private static readonly HashSet<string> Catalog = Packages.SelectMany(p => p.Required.Concat(p.Notices))
        .ToHashSet(StringComparer.Ordinal);

    internal static bool Allows(string path) => Catalog.Contains(path);

    internal static List<string> Enumerate(string source)
    {
        var result = new List<string>();
        var selected = Packages.Where(package => package.Dlls.Any(path => File.Exists(EnvironmentCopy.Inside(source, path)))).ToArray();
        RequireDependencies(selected);
        foreach (var package in selected)
        {
            foreach (var path in package.Required)
            {
                if (!File.Exists(EnvironmentCopy.Inside(source, path)))
                    throw new FileNotFoundException($"Incomplete installed {package.Name} package: {path}. Finish installing both components and package data before starting Arena.");
                result.Add(path);
            }
            foreach (var root in package.Roots)
                foreach (var path in EnvironmentCopy.Walk(source, root.TrimEnd('/')))
                    if (IsExecutable(path) && !package.Dlls.Contains(path, StringComparer.Ordinal))
                        throw new InvalidDataException($"Unexpected executable in installed {package.Name} package: {path}. No additional mods are copied.");
            result.AddRange(package.Notices.Where(path => File.Exists(EnvironmentCopy.Inside(source, path))));
        }
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidDataException("Duplicate recommended mod package path.");
        return result.Order(StringComparer.Ordinal).ToList();
    }

    internal static string Summary(string source)
    {
        var paths = Enumerate(source).ToHashSet(StringComparer.Ordinal);
        var installed = Packages.Where(p => p.Dlls.Any(paths.Contains)).Select(p => p.Name).ToArray();
        var absent = Packages.Where(p => !p.Dlls.Any(paths.Contains)).Select(p => p.Name).ToArray();
        return "Installed: " + (installed.Length == 0 ? "none" : string.Join(", ", installed)) +
            " · Not installed: " + (absent.Length == 0 ? "none" : string.Join(", ", absent));
    }

    internal static void Validate(IReadOnlyDictionary<string, FileStream> files)
    {
        var paths = files.Keys.ToHashSet(StringComparer.Ordinal);
        var selected = Packages.Where(package => package.Required.Concat(package.Notices).Any(paths.Contains)).ToArray();
        RequireDependencies(selected);
        foreach (var package in selected)
        {
            if (package.Required.Any(path => !paths.Contains(path)))
                throw new InvalidDataException($"Incomplete leased {package.Name} package.");
            foreach (var path in package.Required.Where(p => p.EndsWith(".json", StringComparison.Ordinal)))
            {
                var input = files[path];
                input.Position = 0;
                using var json = JsonDocument.Parse(input);
                if (json.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    throw new InvalidDataException($"Invalid recommended mod data: {path}.");
                if (path == ApbsDirectory + "Data/DefaultConfigs/config.default.json")
                {
                    RecommendedModState.ValidatePresetConfiguration(json.RootElement);
                    foreach (var property in json.RootElement.EnumerateObject())
                        if (property.Name.Equals("usePreset", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind != JsonValueKind.False)
                            throw new InvalidDataException("Installed APBS default configuration must not enable a custom preset. Source presets are not imported.");
                }
            }
            foreach (var path in package.Dlls)
                ModPackage.ReadAssembly(files[path], Path.GetFileNameWithoutExtension(path));
        }
        if (paths.Contains(BigBrain)) Minimum(files[BigBrain], "DrakiaXYZ-BigBrain", new(1, 5, 0, 0));
        if (paths.Contains(Waypoints)) Minimum(files[Waypoints], "DrakiaXYZ-Waypoints", new(1, 9, 0, 0));
        if (paths.Contains(AbpsClientDirectory + "acidphantasm-botplacementsystem.dll"))
        {
            RequireType(files[AbpsClientDirectory + "acidphantasm-botplacementsystem.dll"], "BotPlacementSystemClient", "Plugin");
            RequireType(files[AbpsServerDirectory + "acidphantasm-botplacementsystem.dll"], "BotPlacementSystemServer", "ModMetadata");
        }
        if (paths.Contains(SainClientDirectory + "SAIN.dll"))
        {
            var sain = Packages.Single(p => p.Name == "SAIN");
            var versions = sain.Dlls.Select(path => ModPackage.ReadAssembly(files[path], Path.GetFileNameWithoutExtension(path)).Version).ToArray();
            if (versions.Any(version => version != versions[0]) || versions[0] < new Version(4, 5, 1, 0))
                throw new InvalidDataException("SAIN requires matching client/server/shared versions 4.5.1 or newer. Finish updating normal SPT.");
            if (SharedAssets.Hash(files[SainClientDirectory + "SAIN.Preset.Shared.dll"]) !=
                SharedAssets.Hash(files[SainServerDirectory + "SAIN.Preset.Shared.dll"]))
                throw new InvalidDataException("SAIN shared libraries differ. Finish updating normal SPT.");
        }
        // APBS/ABPS intentionally use different assembly and advertised plugin
        // versions. Their own SPT/EFT gates remain authoritative; do not infer a
        // fictitious minimum or equality check from those assembly numbers.
    }

    private static void Minimum(Stream input, string name, Version minimum)
    {
        if (ModPackage.ReadAssembly(input, name).Version < minimum)
            throw new InvalidDataException($"{name} requires version {minimum} or newer for this Arena setup.");
    }

    private static void RequireType(Stream input, string expectedNamespace, string expectedName)
    {
        input.Position = 0;
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        if (!metadata.TypeDefinitions.Any(handle =>
            {
                var type = metadata.GetTypeDefinition(handle);
                return metadata.GetString(type.Namespace) == expectedNamespace && metadata.GetString(type.Name) == expectedName;
            }))
            throw new InvalidDataException($"ABPS component has the wrong role: expected {expectedNamespace}.{expectedName}.");
    }

    private static void RequireDependencies(IReadOnlyCollection<Package> selected)
    {
        if (selected.Any(p => p.Name == "SAIN") &&
            (!selected.Any(p => p.Name == "BigBrain") || !selected.Any(p => p.Name == "Waypoints")))
            throw new InvalidDataException("The recommended SAIN setup requires installed BigBrain and Waypoints. No dependency is downloaded automatically.");
    }

    private static bool IsExecutable(string path) => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
