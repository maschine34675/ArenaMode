using System.Text.Json;

namespace ArenaStart;

public sealed record ManagedPreview(string Source, string Destination, bool Exists, long PrivateBytes, long SharedBytes,
    ModUpdateSummary? Updates = null, bool RecoveryPending = false, string? RebuildReason = null,
    bool RebuildPending = false);

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
        var active = BaseRebuild.Resolve(destination, source, diagnostics);
        var manifest = ReadOwnedManifest(source, active, diagnostics);
        if (manifest.Files.Count == 0 || manifest.Files.Any(f => f.Length < 0))
            throw new InvalidDataException("Invalid managed Arena file catalog.");
        EnvironmentCopy.ValidateProfileFiles(active);
        var rebuilding = BaseRebuild.Pending(destination);
        var reason = rebuilding ? "An interrupted rebuild needs attention. Previous private files are retained." : BaseRebuild.SourceChange(manifest);
        var pending = ModUpdates.Pending(active);
        return new(source, active, true,
            checked(manifest.Files.Where(f => !f.Shared).Sum(f => f.Length)),
            checked(manifest.Files.Where(f => f.Shared).Sum(f => f.Length)),
            pending || reason != null ? null : ModUpdates.Inspect(manifest), pending, reason, rebuilding);
    }

    public static string Ensure(string source, bool diagnostics = false, Action<string>? report = null)
    {
        source = EnvironmentCopy.Root(source);
        var destination = Destination(source);
        RuntimeHost.RequireIdle();
        if (!Directory.Exists(destination) && !File.Exists(destination))
        {
            var plan = EnvironmentCopy.Plan(source, destination, shareAssets: true, diagnostics);
            report?.Invoke($"Preparing managed ArenaMode environment: {plan.PrivateBytes / 1073741824d:F2} GiB private copies; " +
                $"{plan.SharedBytes / 1073741824d:F2} GiB shared through NTFS hardlinks, without copying asset data.");
            EnvironmentCopy.Prepare(plan, report);
        }
        else
        {
            // Never adopt/reset an arbitrary directory. RuntimeHost owns the
            // verified mod update under the exclusive running lease.
            _ = BaseRebuild.Resolve(destination, source, diagnostics);
            BaseRebuild.RequireNoPending(destination);
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
