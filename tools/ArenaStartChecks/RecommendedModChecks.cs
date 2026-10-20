using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using ArenaStart;

internal static class RecommendedModChecks
{
    internal static int Run(string scratch)
    {
        var count = 0;
        var sequence = 0;
        void Check(bool value, string reason)
        {
            count++;
            if (!value) throw new Exception("Recommended mods: " + reason);
        }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or
                UnauthorizedAccessException or BadImageFormatException or JsonException) { count++; return; }
            throw new Exception("Accepted recommended mod negative control: " + reason);
        }
        string NewCase(params string[] packages)
        {
            var root = Path.Combine(scratch, "recommended-mod-" + ++sequence);
            Directory.CreateDirectory(root);
            foreach (var package in packages) Install(root, package);
            return root;
        }
        void Validate(string source)
        {
            var paths = RecommendedModPackage.Enumerate(source);
            var files = new Dictionary<string, FileStream>(StringComparer.Ordinal);
            try
            {
                foreach (var path in paths) files.Add(path, SharedAssets.OpenReadLease(EnvironmentCopy.Inside(source, path)));
                RecommendedModPackage.Validate(files);
            }
            finally { foreach (var stream in files.Values) stream.Dispose(); }
        }

        var empty = NewCase();
        Check(RecommendedModPackage.Enumerate(empty).Count == 0, "absent optional mods are required");
        Check(RecommendedModPackage.Summary(empty) == "Installed: none · Not installed: SAIN, APBS, ABPS, BigBrain, Waypoints",
            "empty summary is misleading");
        RecommendedModPackage.Validate(new Dictionary<string, FileStream>());
        count++;
        foreach (var package in new[] { "APBS", "ABPS", "BigBrain", "Waypoints" })
        {
            var single = NewCase(package);
            Validate(single);
            count++;
            Check(RecommendedModPackage.Summary(single).StartsWith("Installed: " + package + " ·", StringComparison.Ordinal),
                "independent package summary: " + package);
        }
        var complete = NewCase("SAIN", "APBS", "ABPS", "BigBrain", "Waypoints");
        var initial = RecommendedModPackage.Enumerate(complete);
        Check(initial.Count == 84, "unexpected complete fixed package inventory");
        Check(initial.SequenceEqual(initial.Order(StringComparer.Ordinal)) && initial.All(RecommendedModPackage.Allows),
            "package catalog is not canonical");
        Validate(complete);
        count++;
        Check(RecommendedModPackage.Summary(complete) == "Installed: SAIN, APBS, ABPS, BigBrain, Waypoints · Not installed: none",
            "complete summary is misleading");
        var sourceBefore = Snapshot(complete);
        RecommendedModPackage.Enumerate(complete);
        RecommendedModPackage.Summary(complete);
        Validate(complete);
        Check(Same(sourceBefore, complete), "read-only inventory or metadata checks wrote source files");

        foreach (var packages in new[] { new[] { "SAIN" }, new[] { "SAIN", "BigBrain" }, new[] { "SAIN", "Waypoints" } })
        {
            var missing = NewCase(packages);
            Reject(() => RecommendedModPackage.Enumerate(missing), "SAIN dependency absent");
            Reject(() => RecommendedModPackage.Summary(missing), "summary hides partial dependency setup");
        }
        foreach (var missing in new[]
        {
            RecommendedModPackage.SainClientDirectory + "SAIN.dll",
            RecommendedModPackage.SainServerDirectory + "SAINServerMod.dll",
            RecommendedModPackage.SainServerDirectory + "SAIN.ServerInterop.dll",
            RecommendedModPackage.SainClientDirectory + "SAIN.Preset.Shared.dll",
            RecommendedModPackage.SainServerDirectory + "SAIN.Preset.Shared.dll",
            RecommendedModPackage.SainServerDirectory + "Data/NicknamePersonalities.json",
            RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll",
            RecommendedModPackage.AbpsServerDirectory + "acidphantasm-botplacementsystem.dll",
            RecommendedModPackage.AbpsServerDirectory + "Defaults/MapZones.json",
            RecommendedModPackage.ApbsDirectory + "Data/DefaultConfigs/blacklists.default.json",
            RecommendedModPackage.ApbsDirectory + "Data/Equipment/Tier7_equipment.json",
            RecommendedModPackage.WaypointsDirectory + "navmesh/woods-navmesh.bundle"
        })
        {
            var partial = NewCase("SAIN", "APBS", "ABPS", "BigBrain", "Waypoints");
            File.Delete(EnvironmentCopy.Inside(partial, missing));
            Reject(() => RecommendedModPackage.Enumerate(partial), "missing required component " + missing);
        }
        foreach (var path in new[]
        {
            RecommendedModPackage.ApbsDirectory + "logs/stolen.json",
            RecommendedModPackage.ApbsDirectory + "config.json",
            RecommendedModPackage.ApbsDirectory + "blacklists.json",
            RecommendedModPackage.ApbsDirectory + "Presets/normal/Ammo/Tier1_ammo.json",
            RecommendedModPackage.ApbsDirectory + "GeneratedVanillaMappings-DO_NOT_TOUCH/manifest.json",
            RecommendedModPackage.ApbsDirectory + "wwwroot/files/RELEASE_NOTES.txt",
            RecommendedModPackage.AbpsServerDirectory + "Data/0123456789abcdef01234567.json",
            RecommendedModPackage.AbpsServerDirectory + "config.json",
            "BepInEx/config/com.acidphantasm.botplacementsystem.cfg",
            RecommendedModPackage.SainClientDirectory + "EditorDefaults.json",
            RecommendedModPackage.SainServerDirectory + "CoreOverrides.json",
            RecommendedModPackage.SainServerDirectory + "Presets/normal.json",
            RecommendedModPackage.SainServerDirectory + "cache/releases.json",
            RecommendedModPackage.WaypointsDirectory + "custom/nodes.json",
            RecommendedModPackage.ApbsDirectory + "Data/Ammo/Tier1_ammo.sync-conflict-1.json",
            "BepInEx/plugins/Other/Other.dll", "SPT_Runtime/user/profiles/normal.json"
        })
        {
            Put(complete, path, "private original data");
            Check(!RecommendedModPackage.Allows(path), "mutable/unrelated source file allowed: " + path);
        }
        Check(RecommendedModPackage.Enumerate(complete).SequenceEqual(initial), "mutable/source profile data entered the package");
        foreach (var path in new[] { "../" + RecommendedModPackage.BigBrain, RecommendedModPackage.BigBrain + ":stream",
                     RecommendedModPackage.BigBrain.Replace('/', '\\'), RecommendedModPackage.ApbsDirectory + "Data/../config.json" })
            Check(!RecommendedModPackage.Allows(path), "noncanonical path accepted");
        Put(complete, RecommendedModPackage.ApbsDirectory + "LICENSE", "optional upstream notice");
        Check(RecommendedModPackage.Enumerate(complete).Count == initial.Count + 1, "optional installed LICENSE omitted");

        foreach (var root in new[] { RecommendedModPackage.SainClientDirectory, RecommendedModPackage.SainServerDirectory,
                     RecommendedModPackage.ApbsDirectory, RecommendedModPackage.AbpsClientDirectory,
                     RecommendedModPackage.AbpsServerDirectory, RecommendedModPackage.WaypointsDirectory })
        {
            var injected = NewCase("SAIN", "APBS", "ABPS", "BigBrain", "Waypoints");
            Put(injected, root + "nested/other.EXE", "executable");
            Reject(() => RecommendedModPackage.Enumerate(injected), "extra executable " + root);
        }
        var hiddenExecutable = NewCase("APBS");
        Put(hiddenExecutable, RecommendedModPackage.ApbsDirectory + "logs/extra.sync-conflict-1.DLL", "executable");
        Reject(() => RecommendedModPackage.Enumerate(hiddenExecutable), "sync-conflict executable disguised as log");

        foreach (var path in new[] { RecommendedModPackage.BigBrain, RecommendedModPackage.Waypoints,
                     RecommendedModPackage.ApbsDirectory + "acidphantasm-progressivebotsystem.dll",
                     RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll",
                     RecommendedModPackage.AbpsServerDirectory + "acidphantasm-botplacementsystem.dll",
                     RecommendedModPackage.SainClientDirectory + "SAIN.dll" })
        {
            var wrong = NewCase("SAIN", "APBS", "ABPS", "BigBrain", "Waypoints");
            AssemblyFile(wrong, path, name: "UnexpectedForeignAssembly");
            Reject(() => Validate(wrong), "wrong assembly identity " + path);
        }
        var broken = NewCase("APBS");
        Put(broken, RecommendedModPackage.ApbsDirectory + "acidphantasm-progressivebotsystem.dll", "not PE");
        Reject(() => Validate(broken), "nonmanaged DLL");
        var invalidData = NewCase("APBS");
        Put(invalidData, RecommendedModPackage.ApbsDirectory + "Data/Ammo/Tier1_ammo.json", "null");
        Reject(() => Validate(invalidData), "invalid required JSON data");
        foreach (var (path, version) in new[]
        {
            (RecommendedModPackage.BigBrain, new Version(1, 4, 9, 0)),
            (RecommendedModPackage.Waypoints, new Version(1, 8, 9, 0)),
            (RecommendedModPackage.SainClientDirectory + "SAIN.dll", new Version(4, 5, 0, 0)),
            (RecommendedModPackage.SainServerDirectory + "SAIN.ServerInterop.dll", new Version(4, 5, 2, 0))
        })
        {
            var old = NewCase("SAIN", "BigBrain", "Waypoints");
            AssemblyFile(old, path, version);
            Reject(() => Validate(old), "unsupported/mixed version " + path);
        }
        var sharedDrift = NewCase("SAIN", "BigBrain", "Waypoints");
        AssemblyFile(sharedDrift, RecommendedModPackage.SainServerDirectory + "SAIN.Preset.Shared.dll", new(4, 5, 1, 0), tag: "different-build");
        Reject(() => Validate(sharedDrift), "same version but different shared library bytes");
        var future = NewCase("SAIN", "APBS", "ABPS", "BigBrain", "Waypoints");
        foreach (var path in RecommendedModPackage.Enumerate(future).Where(p => p.EndsWith(".dll", StringComparison.Ordinal)))
            AssemblyFile(future, path, new(99, 0, 0, 0));
        File.Copy(EnvironmentCopy.Inside(future, RecommendedModPackage.SainClientDirectory + "SAIN.Preset.Shared.dll"),
            EnvironmentCopy.Inside(future, RecommendedModPackage.SainServerDirectory + "SAIN.Preset.Shared.dll"), true);
        Validate(future);
        count++;
        var dataOnly = NewCase();
        var orphanPath = RecommendedModPackage.ApbsDirectory + "Data/Ammo/Tier1_ammo.json";
        Put(dataOnly, orphanPath, "{}");
        Check(RecommendedModPackage.Enumerate(dataOnly).Count == 0, "leftover source data selects absent mod");
        using (var orphan = SharedAssets.OpenReadLease(EnvironmentCopy.Inside(dataOnly, orphanPath)))
            Reject(() => RecommendedModPackage.Validate(new Dictionary<string, FileStream> { [orphanPath] = orphan }),
                "leased orphan data passed without package DLL");

        foreach (var direction in new[] { "client-to-server", "server-to-client", "swapped" })
        {
            var roles = NewCase("ABPS");
            var clientPath = EnvironmentCopy.Inside(roles, RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll");
            var serverPath = EnvironmentCopy.Inside(roles, RecommendedModPackage.AbpsServerDirectory + "acidphantasm-botplacementsystem.dll");
            var clientBytes = File.ReadAllBytes(clientPath);
            var serverBytes = File.ReadAllBytes(serverPath);
            if (direction is "client-to-server" or "swapped") File.WriteAllBytes(serverPath, clientBytes);
            if (direction is "server-to-client" or "swapped") File.WriteAllBytes(clientPath, serverBytes);
            Reject(() => Validate(roles), "wrong ABPS DLL roles: " + direction);
        }
        foreach (var json in new[] { "{\"usePreset\":true,\"presetName\":\"normal\"}",
                     "{\"usePreset\":false,\"presetName\":\"../normal\"}",
                     "{\"usePreset\":false,\"presetName\":\"C:/normal\"}" })
        {
            var unsafeDefault = NewCase("APBS");
            Put(unsafeDefault, RecommendedModPackage.ApbsDirectory + "Data/DefaultConfigs/config.default.json", json);
            Reject(() => Validate(unsafeDefault), "source default imports custom/external preset");
        }

        // Exercise the real manifest transaction, not just package inventory.
        var managed = NewManagedCase();
        var oldPrivate = Snapshot(managed.Root);
        foreach (var package in new[] { "SAIN", "APBS", "ABPS", "BigBrain", "Waypoints" }) Install(managed.Source, package);
        Check(ModUpdates.Inspect(managed) == new ModUpdateSummary(84, 0, 0), "recommended installation delta");
        managed = ModUpdates.Apply(managed);
        EnvironmentCopy.Verify(managed.Root);
        Check(RecommendedModPackage.Enumerate(managed.Root).Count == 84, "transaction did not install all recommended files");
        Check(ModUpdates.Inspect(managed).Total == 0, "new package not stable after installation");
        CheckPrivate(oldPrivate, managed.Root);
        Put(managed.Root, RecommendedModPackage.ApbsDirectory + "config.json", "{\"usePreset\":false,\"presetName\":\"\",\"private\":true}");
        Put(managed.Root, RecommendedModPackage.AbpsServerDirectory + "config.json", "{\"private\":true}");
        Put(managed.Root, RecommendedModPackage.AbpsServerDirectory + "Data/0123456789abcdef01234567.json", "{\"bossProgress\":7}");
        Put(managed.Root, RecommendedModPackage.SainServerDirectory + "Presets/Arena private.json", "{\"private\":true}");
        var withSettings = Snapshot(managed.Root);
        var apbsDll = RecommendedModPackage.ApbsDirectory + "acidphantasm-progressivebotsystem.dll";
        AssemblyFile(managed.Source, apbsDll, new(2, 0, 0, 0), tag: "same-version-new-build");
        Put(managed.Source, RecommendedModPackage.SainServerDirectory + "Data/NicknamePersonalities.json", "{\"changed\":true}");
        Check(ModUpdates.Inspect(managed) == new ModUpdateSummary(0, 2, 0), "same-version DLL/data updates not detected");
        var rollbackManifest = managed;
        Reject(() => ModUpdates.Apply(rollbackManifest, checkpoint: stage =>
        {
            if (stage == "file:0") throw new IOException("injected recommended-package interruption");
        }), "transaction interruption");
        Check(Same(withSettings, managed.Root) && !ModUpdates.Pending(managed.Root), "recommended rollback did not restore old bytes and private state");
        managed = ModUpdates.Apply(managed);
        EnvironmentCopy.Verify(managed.Root);
        CheckPrivate(withSettings, managed.Root);
        Check(ModUpdates.Inspect(managed).Total == 0, "successful recommended update still differs");
        var installedSnapshot = Snapshot(managed.Root);
        var installedSourcePackage = RecommendedModPackage.Enumerate(managed.Source)
            .ToDictionary(path => path, path => File.ReadAllBytes(EnvironmentCopy.Inside(managed.Source, path)));
        File.Delete(EnvironmentCopy.Inside(managed.Source, RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll"));
        var partialManifest = managed;
        Reject(() => ModUpdates.Apply(partialManifest), "partial ABPS removal treated as full uninstall");
        Check(Same(installedSnapshot, managed.Root), "partial uninstall changed private runtime");
        foreach (var path in installedSourcePackage.Keys)
            File.Delete(EnvironmentCopy.Inside(managed.Source, path));
        Check(ModUpdates.Inspect(managed) == new ModUpdateSummary(0, 0, 84), "complete recommended removal delta");
        managed = ModUpdates.Apply(managed);
        EnvironmentCopy.Verify(managed.Root);
        Check(RecommendedModPackage.Enumerate(managed.Root).Count == 0, "uninstall left package executables");
        CheckPrivate(installedSnapshot, managed.Root);
        foreach (var (path, bytes) in installedSourcePackage)
        {
            var target = EnvironmentCopy.Inside(managed.Source, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        Check(ModUpdates.Inspect(managed) == new ModUpdateSummary(84, 0, 0), "recommended reinstall delta");
        managed = ModUpdates.Apply(managed);
        EnvironmentCopy.Verify(managed.Root);
        CheckPrivate(installedSnapshot, managed.Root);
        Check(ModUpdates.Inspect(managed).Total == 0 && RecommendedModPackage.Enumerate(managed.Root).Count == 84,
            "reinstall failed or reset private settings");

        Console.WriteLine($"RECOMMENDED_MOD_CHECKS_PASS ({count} assertions)");
        return count;

        void CheckPrivate(Dictionary<string, byte[]> before, string root)
        {
            foreach (var (path, bytes) in before.Where(pair => pair.Key.Replace('\\', '/') != EnvironmentCopy.ManifestPath &&
                         !ModPackage.Allows(pair.Key.Replace('\\', '/'), diagnostics: false)))
                Check(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(bytes), "private state changed during recommended update: " + path);
        }

        Manifest NewManagedCase()
        {
            var source = NewCase();
            var target = source + "-Arena";
            Directory.CreateDirectory(target);
            foreach (var path in ModPackage.Required)
            {
                if (path.EndsWith(".dll", StringComparison.Ordinal)) AssemblyFile(source, path, new(1, 2, 3, 0));
                else Put(source, path, "[{\"fixture\":true}]");
            }
            Put(source, ModPackage.ClientDirectory + "announcements/voice.ogg", "voice");
            Put(source, ModPackage.ClientDirectory + "sounds/hit.wav", "hit");
            Put(source, ModPackage.ClientDirectory + "docs/LICENSE", "notice");
            Put(source, "UnityPlayer.dll", "native binding");
            var owned = new List<OwnedFile>();
            foreach (var (path, bytes) in Snapshot(source))
            {
                var relative = path.Replace('\\', '/');
                var destination = EnvironmentCopy.Inside(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                owned.Add(new(relative, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
            }
            Put(target, "BepInEx/config/BepInEx.cfg", EnvironmentCopy.NativeLoaderConfig);
            Put(target, "BepInEx/config/maschine-ArenaMode.cfg", "private preferences");
            const string id = "0123456789abcdef01234567";
            Put(target, EnvironmentCopy.AccountPath, JsonSerializer.Serialize(new { profileId = id, username = RuntimeHost.Username }));
            Put(target, $"SPT_Runtime/user/profiles/{id}.json", JsonSerializer.Serialize(new { info = new { id, username = RuntimeHost.Username }, progress = 77 }));
            var manifest = new Manifest(2, target, source, owned, [owned.Single(file => file.Path == "UnityPlayer.dll")], false);
            Put(target, EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(manifest, EnvironmentCopy.Json));
            EnvironmentCopy.Verify(target);
            return manifest;
        }
    }

    private static void Install(string root, string package)
    {
        string prefix;
        switch (package)
        {
            case "BigBrain": AssemblyFile(root, RecommendedModPackage.BigBrain, new(1, 5, 0, 0)); return;
            case "Waypoints":
                prefix = RecommendedModPackage.WaypointsDirectory;
                AssemblyFile(root, RecommendedModPackage.Waypoints, new(1, 9, 0, 0));
                Put(root, prefix + "LICENSE", "notice");
                foreach (var map in new[] { "bigmap", "factory4", "interchange", "laboratory", "labyrinth", "lighthouse",
                             "rezervbase", "sandbox", "shoreline", "tarkovstreets", "woods" })
                    Put(root, prefix + "navmesh/" + map + "-navmesh.bundle", "bundle");
                return;
            case "SAIN":
                foreach (var path in new[] { RecommendedModPackage.SainClientDirectory + "SAIN.dll",
                             RecommendedModPackage.SainClientDirectory + "SAIN.Preset.Shared.dll",
                             RecommendedModPackage.SainServerDirectory + "SAINServerMod.dll",
                             RecommendedModPackage.SainServerDirectory + "SAIN.ServerInterop.dll",
                             RecommendedModPackage.SainServerDirectory + "SAIN.Preset.Shared.dll" })
                    AssemblyFile(root, path, new(4, 5, 1, 0));
                File.Copy(EnvironmentCopy.Inside(root, RecommendedModPackage.SainClientDirectory + "SAIN.Preset.Shared.dll"),
                    EnvironmentCopy.Inside(root, RecommendedModPackage.SainServerDirectory + "SAIN.Preset.Shared.dll"), true);
                Put(root, RecommendedModPackage.SainClientDirectory + "LICENSE", "notice");
                Put(root, RecommendedModPackage.SainServerDirectory + "LICENSE", "notice");
                Put(root, RecommendedModPackage.SainServerDirectory + "Data/NicknamePersonalities.json", "{}");
                Put(root, RecommendedModPackage.SainServerDirectory + "wwwroot/css/sain.css", "css");
                return;
            case "APBS":
                prefix = RecommendedModPackage.ApbsDirectory;
                AssemblyFile(root, prefix + "acidphantasm-progressivebotsystem.dll", new(2, 0, 0, 0));
                foreach (var category in new[] { "Ammo", "Appearance", "Chances", "Equipment", "Mods" })
                    for (var tier = 0; tier <= 7; tier++)
                        Put(root, prefix + $"Data/{category}/Tier{tier}_{category.ToLowerInvariant()}.json", "{}");
                foreach (var path in new[] { "Data/DefaultConfigs/config.default.json", "Data/DefaultConfigs/blacklists.default.json",
                             "Data/Quests/QuestData.json", "Data/Tiers/TierData.json" }) Put(root, prefix + path, "{}");
                break;
            case "ABPS":
                prefix = RecommendedModPackage.AbpsServerDirectory;
                AssemblyFile(root, RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll", new(1, 0, 0, 0));
                AssemblyFile(root, prefix + "acidphantasm-botplacementsystem.dll", new(2, 1, 0, 0));
                Put(root, RecommendedModPackage.AbpsClientDirectory + "LICENSE", "notice");
                Put(root, prefix + "LICENSE", "notice");
                foreach (var name in new[] { "Bosses", "Hostility", "MapZones", "PMCs", "Scavs", "config.default" })
                    Put(root, prefix + "Defaults/" + name + ".json", "{}");
                break;
            default: throw new InvalidOperationException("Unknown fixture package.");
        }
        Put(root, prefix + "wwwroot/css/app.css", "css");
        Put(root, prefix + "wwwroot/files/ReleaseNotes.json", "[]");
        Put(root, prefix + "wwwroot/images/background.jpg", "image");
    }

    private static void Put(string root, string path, string text)
    {
        var target = EnvironmentCopy.Inside(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text);
    }

    private static void AssemblyFile(string root, string path, Version? version = null, string? name = null, string tag = "fixture")
    {
        name ??= Path.GetFileNameWithoutExtension(path);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(name) { Version = version ?? new(1, 0, 0, 0) }, typeof(object).Assembly);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            ["BuildTag", tag]));
        if (path == ModPackage.Client)
            assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["RedeployCoreBuiltAgainst", "1.0.0"]));
        var marker = path == RecommendedModPackage.AbpsClientDirectory + "acidphantasm-botplacementsystem.dll" ? "BotPlacementSystemClient.Plugin" :
            path == RecommendedModPackage.AbpsServerDirectory + "acidphantasm-botplacementsystem.dll" ? "BotPlacementSystemServer.ModMetadata" : "FixtureOnly";
        assembly.DefineDynamicModule(name).DefineType(marker, TypeAttributes.Public).CreateType();
        var target = EnvironmentCopy.Inside(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        assembly.Save(target);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    private static bool Same(Dictionary<string, byte[]> before, string root)
    {
        var after = Snapshot(root);
        return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var bytes) && bytes.SequenceEqual(pair.Value));
    }
}
