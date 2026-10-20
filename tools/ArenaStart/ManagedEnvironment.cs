using System.Text.Json;

namespace ArenaStart;

public static class ManagedEnvironment
{
    public static string Destination(string source)
    {
        source = EnvironmentCopy.Root(source);
        return Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileName(source) + "-Arena");
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
            var path = EnvironmentCopy.Inside(destination, EnvironmentCopy.ManifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(EnvironmentCopy.ReadPrivate(path), EnvironmentCopy.Json);
            if (manifest == null || manifest.Schema != 2 || manifest.Diagnostics != diagnostics ||
                !manifest.Root.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
                !manifest.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Existing managed Arena directory belongs to another source/mode. It was left unchanged.");
            report?.Invoke($"Reusing {destination}. All files will be verified before start.");
        }
        return destination;
    }
}
