using System.Text.Json;

namespace ArenaStart;

public sealed record ManagedPreview(string Source, string Destination, bool Exists, long PrivateBytes, long SharedBytes);

public static class ManagedEnvironment
{
    public static string Destination(string source)
    {
        source = EnvironmentCopy.Root(source);
        return Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileName(source) + "-Arena");
    }

    public static ManagedPreview Inspect(string source, bool diagnostics = false)
    {
        source = EnvironmentCopy.Root(source);
        var destination = Destination(source);
        if (!Directory.Exists(destination) && !File.Exists(destination))
        {
            var plan = EnvironmentCopy.Plan(source, destination, shareAssets: true, diagnostics);
            return new(source, destination, false, plan.PrivateBytes, plan.SharedBytes);
        }
        var manifest = ReadOwnedManifest(source, destination, diagnostics);
        if (manifest.Files.Count == 0 || manifest.Files.Any(f => f.Length < 0))
            throw new InvalidDataException("Invalid managed Arena file catalog.");
        return new(source, destination, true,
            checked(manifest.Files.Where(f => !f.Shared).Sum(f => f.Length)),
            checked(manifest.Files.Where(f => f.Shared).Sum(f => f.Length)));
    }

    public static string Ensure(string source, bool diagnostics = false, Action<string>? report = null)
    {
        source = EnvironmentCopy.Root(source);
        var destination = Destination(source);
        RuntimeHost.RequireIdle();
        if (!Directory.Exists(destination) && !File.Exists(destination))
        {
            var plan = EnvironmentCopy.Plan(source, destination, shareAssets: true, diagnostics);
            report?.Invoke($"Preparing managed Arena environment: {plan.PrivateBytes / 1073741824d:F2} GiB private; " +
                $"{plan.SharedBytes / 1073741824d:F2} GiB reused without copying.");
            EnvironmentCopy.Prepare(plan, report);
        }
        else
        {
            // Never adopt, reset, silently update or merge an arbitrary directory.
            _ = ReadOwnedManifest(source, destination, diagnostics);
            report?.Invoke($"Reusing {destination}. All files will be verified before start.");
        }
        return destination;
    }

    private static Manifest ReadOwnedManifest(string source, string destination, bool diagnostics)
    {
        var path = EnvironmentCopy.Inside(destination, EnvironmentCopy.ManifestPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(EnvironmentCopy.ReadPrivate(path), EnvironmentCopy.Json);
        if (manifest == null || manifest.Schema != 2 || manifest.Diagnostics != diagnostics ||
            !string.Equals(manifest.Root, destination, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Source, source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Existing managed Arena directory belongs to another source/mode. It was left unchanged.");
        return manifest;
    }
}
