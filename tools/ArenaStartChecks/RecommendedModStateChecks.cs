using System.Text.Json;
using ArenaStart;

internal static class RecommendedModStateChecks
{
    internal static int Run(string scratch)
    {
        var count = 0;
        var root = Path.Combine(scratch, "recommended-private-state");
        var source = Path.Combine(scratch, "recommended-state-source");
        const string id = "0123456789abcdef01234567";
        void Check(bool value, string why) { count++; if (!value) throw new Exception(why); }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException)
            { count++; return; }
            throw new Exception("Accepted private mod state: " + why);
        }
        void Put(string path, string content = "{}")
        {
            var full = EnvironmentCopy.Inside(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        Directory.CreateDirectory(source);
        Put("native.txt", "fixture");
        Put("BepInEx/config/BepInEx.cfg", EnvironmentCopy.NativeLoaderConfig);
        Put(EnvironmentCopy.AccountPath, JsonSerializer.Serialize(new { username = RuntimeHost.Username, profileId = id }));
        Put($"SPT_Runtime/user/profiles/{id}.json", JsonSerializer.Serialize(new { info = new { id, username = RuntimeHost.Username } }));
        var manifest = new Manifest(1, root, source, [new("native.txt", 7, EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, "native.txt")))]);
        Put(EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(manifest, EnvironmentCopy.Json));
        var persistent = new[] { "BepInEx/config/me.sol.sain.cfg", "BepInEx/plugins/SAIN/EditorDefaults.json",
            RecommendedModState.Sain + "CoreOverrides.json", RecommendedModState.Sain + "ServerConfig.json",
            RecommendedModState.Sain + "Presets/Arena custom.json", RecommendedModState.Apbs + "config.json",
            RecommendedModState.Apbs + "blacklists.json", RecommendedModState.Apbs + "Presets/Arena/manifest.json",
            RecommendedModState.Apbs + "Presets/Arena/Ammo/Tier1_ammo.json",
            RecommendedModState.Abps + "config.json", RecommendedModState.Abps + $"Data/{id}.json" };
        var disposable = new[] { RecommendedModState.Sain + "cache/releases.json", RecommendedModState.Sain + "CoreOverrides.json.bak",
            RecommendedModState.Sain + "Presets/Arena.json.bak", RecommendedModState.Apbs + "logs/errors.txt",
            RecommendedModState.Apbs + "logs/scav_generation.json.tmp",
            RecommendedModState.Apbs + "config.json.bak", RecommendedModState.Apbs + "blacklists.json.bak",
            RecommendedModState.Abps + "config.json.bak",
            RecommendedModState.Apbs + "wwwroot/files/RELEASE_NOTES.txt",
            RecommendedModState.Apbs + "GeneratedVanillaMappings-DO_NOT_TOUCH/manifest.json",
            "BepInEx/plugins/DrakiaXYZ-Waypoints/mesh/factory4.json" };
        foreach (var path in persistent.Concat(disposable)) Put(path);
        EnvironmentCopy.Verify(root);
        var carry = RecommendedModState.PreservedFiles(root).ToArray();
        foreach (var path in persistent) Check(carry.Contains(path), "private preference omitted: " + path);
        foreach (var path in disposable) Check(!carry.Contains(path), "disposable state preserved: " + path);
        foreach (var path in new[] { RecommendedModState.Abps + "Data/ffffffffffffffffffffffff.json",
            RecommendedModState.Sain + "Presets/nested/preset.json", RecommendedModState.Apbs + "Presets/evil.dll",
            RecommendedModState.Apbs + "GeneratedVanillaMappings-DO_NOT_TOUCH/injected.dll",
            "BepInEx/plugins/SAIN/injected.dll", "BepInEx/plugins/DrakiaXYZ-Waypoints/points/foreign.json" })
        {
            Put(path);
            Reject(() => EnvironmentCopy.Verify(root), "unexpected mod file " + path);
            File.Delete(EnvironmentCopy.Inside(root, path));
        }
        foreach (var name in new[] { "../foreign", @"C:\normal\Presets", @"nested\preset", "nested/preset", "foo:bar", "NUL", "trailing." })
        {
            Put(RecommendedModState.Apbs + "config.json", JsonSerializer.Serialize(new { presetName = name, usePreset = false }));
            Reject(() => EnvironmentCopy.Verify(root), "preset escape even when currently disabled");
        }
        Put(RecommendedModState.Apbs + "config.json", "{\"presetName\":\"Arena\",\"usePreset\":false}");
        EnvironmentCopy.Verify(root);
        var victim = EnvironmentCopy.Inside(root, RecommendedModState.Sain + "ServerConfig.json");
        var outside = EnvironmentCopy.Inside(source, "normal-config.json");
        File.WriteAllText(outside, "{}");
        File.Delete(victim);
        Check(NativeLinks.CreateHardLink(victim, outside, IntPtr.Zero), "state hardlink fixture failed");
        Reject(() => EnvironmentCopy.Verify(root), "linked private setting writes outside environment");
        File.Delete(victim);
        Put(RecommendedModState.Sain + "ServerConfig.json");
        Check(File.ReadAllText(outside) == "{}", "normal setting changed");
        var fresh = root + "-fresh";
        Directory.CreateDirectory(fresh);
        RecommendedModState.EnsureDirectories(manifest with { Root = fresh, Files = [new(
            RecommendedModState.Apbs + "acidphantasm-progressivebotsystem.dll", 0, "fixture")] });
        Check(Directory.Exists(EnvironmentCopy.Inside(fresh, RecommendedModState.Apbs + "logs")) &&
            Directory.Exists(EnvironmentCopy.Inside(fresh, RecommendedModState.Apbs + "Presets")),
            "APBS first-start logger or preset UI has no private directory");
        Console.WriteLine($"RECOMMENDED_MOD_STATE_CHECKS_PASS ({count} assertions)");
        return count;
    }
}
