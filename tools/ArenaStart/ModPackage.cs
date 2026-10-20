using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ArenaStart;

// The same finite catalog is used for first preparation and subsequent updates.
internal static class ModPackage
{
    internal const string ClientDirectory = "BepInEx/plugins/maschine-ArenaMode/";
    internal const string Core = "BepInEx/plugins/maschine-Redeploy.Core.dll";
    internal const string Client = ClientDirectory + "maschine-ArenaMode.Client.dll";
    internal const string Compat = ClientDirectory + "maschine-ArenaMode.BigBrainCompat.dll";
    internal const string Server = "SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll";
    internal const string Bridge = "BepInEx/plugins/maschine-UiBridge.dll";
    internal static readonly string[] Required =
        [Core, Client, Compat, Server, "SPT_Runtime/user/mods/ArenaMode/config/kits.json",
            "SPT_Runtime/user/mods/ArenaMode/config/ladder.json"];
    private static readonly string[] Trees = ["announcements", "sounds", "docs"];

    internal static bool Allows(string path, bool diagnostics)
    {
        if (path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p is "" or "." or "..") ||
            path.Contains(".sync-conflict-", StringComparison.OrdinalIgnoreCase)) return false;
        if (Required.Contains(path, StringComparer.Ordinal) || diagnostics && path == Bridge) return true;
        if (!path.StartsWith(ClientDirectory, StringComparison.Ordinal)) return false;
        var suffix = path[ClientDirectory.Length..];
        var extension = Path.GetExtension(suffix).ToLowerInvariant();
        if (suffix.StartsWith("announcements/", StringComparison.Ordinal) && suffix.Count(c => c == '/') == 1)
            return extension == ".ogg";
        if (suffix.StartsWith("sounds/", StringComparison.Ordinal) && suffix.Count(c => c == '/') == 1)
            return extension == ".wav";
        return suffix == "docs/LICENSE" || suffix.StartsWith("docs/", StringComparison.Ordinal) &&
            extension is ".md" or ".txt" or ".json" or ".png" or ".svg";
    }

    internal static List<string> Enumerate(string source, bool diagnostics)
    {
        var result = Required.Concat(diagnostics ? [Bridge] : Array.Empty<string>()).ToList();
        foreach (var path in result)
            if (!File.Exists(EnvironmentCopy.Inside(source, path)))
                throw new FileNotFoundException($"Required installed mod file missing: {path}. Finish updating normal SPT before starting Arena.");
        foreach (var tree in Trees)
        {
            var relative = ClientDirectory + tree;
            if (!Directory.Exists(EnvironmentCopy.Inside(source, relative)))
                throw new DirectoryNotFoundException($"Required mod data directory missing: {relative}.");
            foreach (var path in EnvironmentCopy.Walk(source, relative))
            {
                if (path.Contains(".sync-conflict-", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Executable in mod data directory refused: {path}.");
                if (Allows(path, diagnostics)) result.Add(path);
            }
        }
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidDataException("Duplicate mod package path.");
        return result.Order(StringComparer.Ordinal).ToList();
    }

    internal static void Validate(IReadOnlyDictionary<string, FileStream> files)
    {
        foreach (var path in Required.Where(p => p.EndsWith(".json", StringComparison.Ordinal)))
        {
            var input = files[path];
            input.Position = 0;
            using var json = JsonDocument.Parse(input);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
                throw new InvalidDataException($"Empty/invalid kit data: {path}.");
        }
        var core = ReadAssembly(files[Core], "maschine-Redeploy.Core");
        var client = ReadAssembly(files[Client], "maschine-ArenaMode.Client");
        var compat = ReadAssembly(files[Compat], "maschine-ArenaMode.BigBrainCompat");
        var server = ReadAssembly(files[Server], "maschine-ArenaMode.Server");
        if (client.Version != server.Version || client.Version != compat.Version)
            throw new InvalidDataException("Arena client, server and compatibility library versions differ. Finish updating the normal installation.");
        if (client.MinimumCore == null || core.Version < client.MinimumCore)
            throw new InvalidDataException("The installed Redeploy.Core is older than ArenaMode requires, or its minimum-version metadata is missing.");
        if (files.TryGetValue(Bridge, out var bridge)) ReadAssembly(bridge, "maschine-UiBridge");
    }

    // Inspect PE metadata only. Never load/execute a source mod inside the launcher.
    private static (Version Version, Version? MinimumCore) ReadAssembly(Stream input, string expectedName)
    {
        input.Position = 0;
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) throw new InvalidDataException($"Not a managed mod assembly: {expectedName}.");
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly) throw new InvalidDataException($"Not an assembly: {expectedName}.");
        var definition = metadata.GetAssemblyDefinition();
        if (metadata.GetString(definition.Name) != expectedName)
            throw new InvalidDataException($"Wrong mod assembly at the expected path: {expectedName}.");
        Version? minimum = null;
        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Reflection" ||
                metadata.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
            var value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) throw new InvalidDataException("Invalid assembly metadata.");
            if (value.ReadSerializedString() != "RedeployCoreBuiltAgainst") continue;
            if (minimum != null || !Version.TryParse(value.ReadSerializedString(), out var parsed))
                throw new InvalidDataException("Invalid/duplicate Redeploy.Core minimum version.");
            minimum = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        }
        return (definition.Version, minimum);
    }
}
