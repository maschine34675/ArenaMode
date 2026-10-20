using System.Text.Json;
using ArenaStart;

internal static class NativeModUpdateChecks
{
    // Opt-in integration check. Mutates only the explicitly supplied private
    // environment using the real production update path; starts no process.
    internal static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--native-mod-update");
        if (index < 0) return 0;
        if (index != 0 || args.Length != 2) throw new ArgumentException("Expected --native-mod-update <private Arena root>.");
        RuntimeHost.RequireIdle();
        var root = EnvironmentCopy.Root(args[index + 1]);
        using var running = new FileStream(EnvironmentCopy.Inside(root, ".arena-start/running"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        EnvironmentCopy.RequireSingleLink(running.SafeFileHandle);
        ModUpdates.Recover(root, Console.WriteLine);
        using var assets = new AssetLease();
        var manifest = EnvironmentCopy.Verify(root, Console.WriteLine, running.SafeFileHandle, assets);
        var protectedPaths = new[] { EnvironmentCopy.AccountPath }
            .Concat(new[] { "SPT_Runtime/user/profiles", "SPT_Runtime/user/sptSettings", "BepInEx/config",
                "SPT_Runtime/user/mods/ArenaMode/config" }.Where(p => Directory.Exists(EnvironmentCopy.Inside(root, p)))
                .SelectMany(p => EnvironmentCopy.Walk(root, p)))
            .Where(p => !ModPackage.Allows(p, manifest.Diagnostics) && File.Exists(EnvironmentCopy.Inside(root, p)))
            .Distinct().ToArray();
        var before = protectedPaths.ToDictionary(p => p, p => EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, p)));
        var sourcePaths = ModPackage.Enumerate(manifest.Source, manifest.Diagnostics);
        var sourceBefore = sourcePaths.ToDictionary(p => p, p => EnvironmentCopy.Hash(EnvironmentCopy.Inside(manifest.Source, p)));
        var delta = ModUpdates.Inspect(manifest);
        var updated = ModUpdates.Apply(manifest, Console.WriteLine);
        if (ModUpdates.Inspect(updated).Total != 0) throw new Exception("Native update did not converge.");
        foreach (var pair in before)
            if (EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, pair.Key)) != pair.Value)
                throw new Exception("Protected native file changed: " + pair.Key);
        if (!sourcePaths.SequenceEqual(ModPackage.Enumerate(manifest.Source, manifest.Diagnostics)) ||
            sourceBefore.Any(p => EnvironmentCopy.Hash(EnvironmentCopy.Inside(manifest.Source, p.Key)) != p.Value))
            throw new Exception("Native source package changed.");
        foreach (var file in updated.Files.Where(f => ModPackage.Allows(f.Path, updated.Diagnostics)))
            if (EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, file.Path)) != file.Sha256)
                throw new Exception("Native target hash mismatch.");
        EnvironmentCopy.ValidateProfileFiles(root);
        // No raw profile/config contents or IDs in the evidence.
        Console.WriteLine(JsonSerializer.Serialize(new {
            delta.Added, delta.Changed, delta.Removed, ProtectedFiles = before.Count,
            SourceFiles = sourceBefore.Count, ManifestSha256 = EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, EnvironmentCopy.ManifestPath))
        }));
        Console.WriteLine("NATIVE_MOD_UPDATE_CHECKS_PASS");
        return 4 + before.Count + sourceBefore.Count;
    }
}
