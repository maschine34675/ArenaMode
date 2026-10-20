using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaStart;

internal static class BaseRebuildChecks
{
    internal static async Task<int> Run(string scratch)
    {
        var count = 0;
        var number = 0;
        void Check(bool value, string why) { count++; if (!value) throw new Exception("Base rebuild: " + why); }
        async Task Reject(Func<Task> operation, string why)
        {
            try { await operation(); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or JsonException)
            { count++; return; }
            throw new Exception("Base rebuild accepted: " + why);
        }
        void Put(string root, string path, string contents)
        {
            var target = EnvironmentCopy.Inside(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, contents);
        }
        string Hash(string root, string path) => EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, path));
        const string profile = "SPT_Runtime/user/profiles/012345678901234567890123.json";
        const string config = "BepInEx/config/com.maschine.ArenaMode.cfg";
        const string native = "SPT_Runtime/SPT.Server.dll";
        (string Source, string Anchor) Fixture()
        {
            var source = Path.Combine(scratch, "base-" + ++number);
            foreach (var path in EnvironmentCopy.RuntimeFiles) Put(source, "SPT_Runtime/" + path, "native fixture");
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(source, "SPT_Runtime/SPT.Server.exe"), true);
            foreach (var path in EnvironmentCopy.CorePlugins) Put(source, "BepInEx/plugins/spt/" + path, "core fixture");
            foreach (var path in new[] { "EscapeFromTarkov.exe", "UnityPlayer.dll", "winhttp.dll",
                "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/native.dll",
                "BepInEx/core/BepInEx.dll", "BepInEx/patchers/spt-prepatch.dll",
                "BepInEx/plugins/spt/ConfigurationManager/ConfigurationManager.dll",
                "SPT_Runtime/SPT_Data/database/test.json",
                "SPT_Runtime/SPT_Data/Launcher/Patches/SPT-core/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.delta" })
                Put(source, path, "fixture");
            Put(source, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", "original native");
            Put(source, "EscapeFromTarkov_Data/StreamingAssets/Windows/example.bundle", "shared asset");
            foreach (var path in ModPackage.Required)
            {
                if (!path.EndsWith(".dll", StringComparison.Ordinal)) { Put(source, path, "[{}]"); continue; }
                var name = Path.GetFileNameWithoutExtension(path);
                var assembly = new PersistedAssemblyBuilder(new AssemblyName(name) { Version = new(1, 2, 3, 0) }, typeof(object).Assembly);
                var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
                if (path == ModPackage.Client) assembly.SetCustomAttribute(new(constructor, ["RedeployCoreBuiltAgainst", "1.0.0"]));
                assembly.DefineDynamicModule(name).DefineType("Fixture", TypeAttributes.Public).CreateType();
                var target = EnvironmentCopy.Inside(source, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                assembly.Save(target);
            }
            foreach (var path in new[] { "announcements/cue.ogg", "sounds/hit.wav", "docs/LICENSE" })
                Put(source, ModPackage.ClientDirectory + path, "fixture asset");
            Put(source, "SPT_Runtime/user/profiles/foreign.json", "never import");
            var anchor = ManagedEnvironment.Destination(source);
            EnvironmentCopy.Prepare(EnvironmentCopy.Plan(source, anchor, shareAssets: true));
            Put(anchor, EnvironmentCopy.AccountPath, """{"username":"Arena-Internal","profileId":"012345678901234567890123"}""");
            Put(anchor, profile, """{"info":{"id":"012345678901234567890123","username":"Arena-Internal"},"fixtureProgress":17}""");
            Put(anchor, config, "[Movement]\nFixture = private");
            Put(anchor, "SPT_Runtime/user/mods/ArenaMode/config/config.json", "{\"fixture\":42}");
            Put(anchor, NativeGameSettings.RelativeDirectory + "/Sound.ini", "{\"volume\":0.7}");
            return (source, anchor);
        }
        Task Probe(string root, AssetLease assets, Action<string> report)
        {
            var manifest = EnvironmentCopy.Verify(root, assetLease: assets);
            ModUpdates.Apply(manifest);
            var path = EnvironmentCopy.Inside(root, profile);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["fixtureNativeMigration"] = true;
            File.WriteAllText(path, json.ToJsonString());
            return Task.CompletedTask;
        }
        Task Execute(string source, Action<string>? checkpoint = null,
            Func<string, AssetLease, Action<string>, Task>? probe = null) =>
            BaseRebuild.Execute(source, false, null, EnvironmentCopy.Prepare, probe ?? Probe, checkpoint);

        var first = Fixture();
        var privateModFiles = new[] {
            "BepInEx/config/me.sol.sain.cfg", "BepInEx/plugins/SAIN/EditorDefaults.json",
            RecommendedModState.Sain + "Presets/My Arena.json", RecommendedModState.Sain + "ServerConfig.json",
            RecommendedModState.Apbs + "config.json", RecommendedModState.Apbs + "blacklists.json",
            RecommendedModState.Apbs + "Presets/Arena/manifest.json",
            RecommendedModState.Apbs + "Presets/Arena/Ammo/Tier1_ammo.json",
            RecommendedModState.Abps + "config.json",
            RecommendedModState.Abps + "Data/012345678901234567890123.json" };
        foreach (var path in privateModFiles) Put(first.Anchor, path, "{\"fixturePrivateSetting\":17}");
        var disposableModFiles = new[] { RecommendedModState.Sain + "cache/releases.json",
            RecommendedModState.Sain + "Presets/My Arena.json.bak",
            RecommendedModState.Apbs + "config.json.bak", RecommendedModState.Apbs + "blacklists.json.bak",
            RecommendedModState.Abps + "config.json.bak",
            RecommendedModState.Apbs + "GeneratedVanillaMappings-DO_NOT_TOUCH/manifest.json",
            RecommendedModState.Apbs + "wwwroot/files/RELEASE_NOTES.txt",
            RecommendedModState.Apbs + "logs/pmc_generation.json.tmp" };
        foreach (var path in disposableModFiles) Put(first.Anchor, path, "{}");
        EnvironmentCopy.Verify(first.Anchor);
        var beforeProfile = Hash(first.Anchor, profile);
        var beforeManifest = Hash(first.Anchor, EnvironmentCopy.ManifestPath);
        var beforeAccount = Hash(first.Anchor, EnvironmentCopy.AccountPath);
        Check(!EnvironmentCopy.Plan(first.Source, first.Anchor + "-unused", shareAssets: true).Files
            .Contains("BepInEx/patchers/FixPluginTypesSerialization.dll"), "clean SPT must not require optional patcher");
        Put(first.Source, native, "updated native server");
        Check(ManagedEnvironment.Inspect(first.Source).RebuildReason != null, "source update not visible");
        await Reject(() => RuntimeHost.Run(first.Anchor, smoke: true), "updated source started old runtime");
        await Execute(first.Source);
        var active = BaseRebuild.Resolve(first.Anchor, first.Source, false);
        Check(active != first.Anchor && Directory.Exists(first.Anchor), "old runtime must stay put");
        Check(Hash(first.Anchor, profile) == beforeProfile && Hash(first.Anchor, EnvironmentCopy.ManifestPath) == beforeManifest,
            "old internal state was mutated");
        Check(Hash(active, EnvironmentCopy.AccountPath) == beforeAccount && Hash(active, config) == Hash(first.Anchor, config),
            "internal identity/settings not preserved");
        Check(Hash(active, native) == Hash(first.Source, native) && !BaseRebuild.Pending(first.Anchor), "new native package not active");
        foreach (var path in privateModFiles)
            Check(Hash(active, path) == Hash(first.Anchor, path), "private mod state lost during rebuild: " + path);
        foreach (var path in disposableModFiles)
            Check(!File.Exists(EnvironmentCopy.Inside(active, path)), "cache/log/crash residue imported during rebuild: " + path);
        Check(ManagedEnvironment.Inspect(first.Source).Destination == active &&
            ManagedEnvironment.Inspect(first.Source).RebuildReason == null, "preview not following active generation");
        Check(JsonNode.Parse(File.ReadAllText(EnvironmentCopy.Inside(active, profile)))!["fixtureProgress"]!.GetValue<int>() == 17 &&
            Hash(first.Source, "SPT_Runtime/user/profiles/foreign.json") == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("never import"u8)),
            "profile progress or foreign source changed");
        await Reject(() => { using var rejected = BaseRebuild.Acquire(active); return Task.CompletedTask; }, "direct generation start");
        var activeProfile = Hash(active, profile);
        await Execute(first.Source);
        Check(BaseRebuild.Resolve(first.Anchor, first.Source, false) != active && Hash(active, profile) == activeProfile,
            "second rebuild overwrote previous generation");

        foreach (var phase in new[] { "journal", "prepared", "imported", "probed", "validated", "activated" })
        {
            var fixture = Fixture();
            var old = Hash(fixture.Anchor, profile);
            await Reject(() => Execute(fixture.Source, p => { if (p == phase) throw new IOException("simulated process exit"); }), phase);
            Check(BaseRebuild.Pending(fixture.Anchor) && Hash(fixture.Anchor, profile) == old, phase + " damaged previous profile");
            var visible = BaseRebuild.Resolve(fixture.Anchor, fixture.Source, false);
            Check((visible == fixture.Anchor) == (phase != "activated"), phase + " wrong activation boundary");
            await Reject(() => RuntimeHost.Run(fixture.Anchor, smoke: true), "pending rebuild started native process");
            var profilesBefore = Hash(visible, profile);
            var probes = 0;
            await Execute(fixture.Source, probe: async (root, assets, messages) => { probes++; await Probe(root, assets, messages); });
            Check(!BaseRebuild.Pending(fixture.Anchor), phase + " retry not settled");
            if (phase == "activated") Check(probes == 0 && Hash(visible, profile) == profilesBefore,
                "completed activation rolled back or recreated profile");
            else Check(probes == 1 && Directory.GetDirectories(fixture.Anchor + ".generations").Length == (phase == "journal" ? 1 : 2),
                "interrupted candidate deleted or reused");
        }

        var leases = Fixture();
        await Execute(leases.Source, probe: async (root, assets, messages) =>
        {
            await Reject(() => { using var s = BaseRebuild.Acquire(leases.Anchor); return Task.CompletedTask; }, "second controller");
            await Reject(() => { Put(leases.Anchor, profile, "damaged"); return Task.CompletedTask; }, "old profile write while rebuilding");
            await Reject(() => { Put(leases.Source, native, "changed"); return Task.CompletedTask; }, "source base changed during probe");
            await Reject(() => { Put(root, native, "changed"); return Task.CompletedTask; }, "candidate immutable file changed");
            await Reject(() => { Put(root, EnvironmentCopy.ManifestPath, "{}"); return Task.CompletedTask; }, "candidate manifest changed");
            await Probe(root, assets, messages);
        });
        // Handles must be released after success, without changing protection attributes.
        Put(leases.Source, native, "next version");
        Check(BaseRebuild.SourceChange(BaseRebuild.ReadManifest(BaseRebuild.Resolve(leases.Anchor, leases.Source, false), leases.Source, false)) != null,
            "native lease did not release / next base change hidden");

        var changed = Fixture();
        await Reject(() => Execute(changed.Source, p =>
        {
            if (p == "prepared") Put(changed.Source, native, "source changed after staging");
        }), "source change before binding");
        Check(BaseRebuild.Resolve(changed.Anchor, changed.Source, false) == changed.Anchor, "changed source candidate activated");

        var wrongProfile = Fixture();
        Put(wrongProfile.Anchor, "SPT_Runtime/user/profiles/extra.json", "{}");
        await Reject(() => Execute(wrongProfile.Source), "second old profile");
        Check(!BaseRebuild.Pending(wrongProfile.Anchor), "bad old profile created rebuild journal");
        var wrongIdentity = Fixture();
        await Reject(() => Execute(wrongIdentity.Source, probe: (root, assets, messages) =>
        {
            Put(root, EnvironmentCopy.AccountPath, "{\"username\":\"wrong\"}");
            return Task.CompletedTask;
        }), "native probe lost account");
        Check(BaseRebuild.Resolve(wrongIdentity.Anchor, wrongIdentity.Source, false) == wrongIdentity.Anchor,
            "bad candidate identity activated");
        var pendingMods = Fixture();
        Put(pendingMods.Anchor, ModUpdates.JournalPath, "{}");
        await Reject(() => Execute(pendingMods.Source), "pending mod transaction bypassed");

        var corrupt = Fixture();
        Put(corrupt.Anchor, BaseRebuild.Journal, "{}");
        await Reject(() => Execute(corrupt.Source), "corrupt rebuild journal");
        Check(File.ReadAllText(EnvironmentCopy.Inside(corrupt.Anchor, BaseRebuild.Journal)) == "{}", "corrupt journal discarded");
        var link = Fixture();
        var external = EnvironmentCopy.Inside(link.Source, "external.cfg");
        File.WriteAllText(external, "external");
        File.Delete(EnvironmentCopy.Inside(link.Anchor, config));
        Check(NativeLinks.CreateHardLink(EnvironmentCopy.Inside(link.Anchor, config), external, IntPtr.Zero), "fixture hardlink failed");
        await Reject(() => Execute(link.Source), "hardlinked private config");
        Check(File.ReadAllText(external) == "external", "outside config modified");
        var altered = Fixture();
        await Reject(() => Execute(altered.Source, p => { if (p == "journal") throw new IOException("stop"); }), "setup pending");
        var alteredManifest = BaseRebuild.ReadManifest(altered.Anchor, altered.Source, false);
        Put(altered.Anchor, EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(alteredManifest with
        { Files = alteredManifest.Files.Append(new OwnedFile("new.txt", 0, new string('A', 64))).ToList() }, EnvironmentCopy.Json));
        await Reject(() => Execute(altered.Source), "previous manifest changed after interruption");
        Check(BaseRebuild.Pending(altered.Anchor), "unexpected old manifest lost journal evidence");
        var wrongOwner = Fixture();
        await Execute(wrongOwner.Source);
        var ownedActive = BaseRebuild.Resolve(wrongOwner.Anchor, wrongOwner.Source, false);
        Put(ownedActive, BaseRebuild.Owner, "{}");
        await Reject(() => { BaseRebuild.Resolve(wrongOwner.Anchor, wrongOwner.Source, false); return Task.CompletedTask; }, "changed active owner");
        var foreignPointer = Fixture();
        Put(foreignPointer.Anchor, BaseRebuild.Pointer, JsonSerializer.Serialize(new ArenaGeneration(1,
            foreignPointer.Anchor, foreignPointer.Source, "../../foreign", false, new string('A', 64))));
        await Reject(() => Execute(foreignPointer.Source), "pointer path escape");
        var failure = Fixture();
        await Reject(() => Execute(failure.Source, probe: (_, _, _) => throw new IOException("native server refused account")), "native probe failed");
        Check(BaseRebuild.Resolve(failure.Anchor, failure.Source, false) == failure.Anchor, "failed server activated candidate");
        await Execute(failure.Source);
        Check(!BaseRebuild.Pending(failure.Anchor), "native failure retry did not rebuild");
        var extraCandidate = Fixture();
        await Reject(() => Execute(extraCandidate.Source, probe: (root, _, _) =>
        { Put(root, "SPT_Runtime/user/profiles/foreign.json", "{}"); return Task.CompletedTask; }), "foreign candidate profile");
        Check(BaseRebuild.Resolve(extraCandidate.Anchor, extraCandidate.Source, false) == extraCandidate.Anchor,
            "foreign candidate profile activated");
        var changedOwner = Fixture();
        await Reject(() => Execute(changedOwner.Source, p =>
        {
            if (p != "validated") return;
            var candidate = Directory.GetDirectories(changedOwner.Anchor + ".generations").Single();
            Put(candidate, BaseRebuild.Owner, "{}");
        }), "ownership record changed before activation");
        Check(BaseRebuild.Resolve(changedOwner.Anchor, changedOwner.Source, false) == changedOwner.Anchor,
            "damaged owner activated");
        var injected = Fixture();
        await Reject(() => Execute(injected.Source, p =>
        {
            if (p != "validated") return;
            Put(Directory.GetDirectories(injected.Anchor + ".generations").Single(), "BepInEx/plugins/Injected.dll", "foreign");
        }), "foreign executable appeared after probe");
        Check(BaseRebuild.Resolve(injected.Anchor, injected.Source, false) == injected.Anchor, "foreign executable candidate activated");
        using (var retainedAssets = new AssetLease())
        {
            var root = BaseRebuild.Resolve(leases.Anchor, leases.Source, false);
            // Read an owned shared fixture independent of the deliberately changed native source above.
            var manifest = BaseRebuild.ReadManifest(root, leases.Source, false);
            var entry = manifest.Files.Single(f => f.Shared);
            var original = EnvironmentCopy.Inside(leases.Source, entry.Path);
            var destination = EnvironmentCopy.Inside(root, entry.Path);
            retainedAssets.Add(entry.Path, SharedAssets.OpenReadLease(original), destination, entry);
            retainedAssets.Add(entry.Path, SharedAssets.OpenReadLease(original), destination, entry);
            Check(retainedAssets.Count == 1, "retained asset reverification duplicated leases");
            await Reject(() => { retainedAssets.Add(entry.Path, SharedAssets.OpenReadLease(original), destination,
                entry with { Sha256 = new string('A', 64) }); return Task.CompletedTask; }, "retained lease authorized changed catalog");
        }
        var modAndBase = Fixture();
        var oldModManifest = BaseRebuild.ReadManifest(modAndBase.Anchor, modAndBase.Source, false);
        Put(modAndBase.Source, ModPackage.ClientDirectory + "sounds/new.wav", "new mod sound");
        Dictionary<string, byte[]>? crashed = null;
        await Reject(() =>
        {
            ModUpdates.Apply(oldModManifest, checkpoint: phase =>
            {
                if (phase != "journal") return;
                crashed = Directory.GetFiles(EnvironmentCopy.Inside(modAndBase.Anchor, ".arena-start"), "*", SearchOption.AllDirectories)
                    .Where(p => Path.GetRelativePath(modAndBase.Anchor, p).Replace('\\', '/').StartsWith(".arena-start/mod-update", StringComparison.Ordinal))
                    .ToDictionary(p => Path.GetRelativePath(modAndBase.Anchor, p).Replace('\\', '/'), File.ReadAllBytes);
                throw new IOException("capture interrupted mod transaction");
            });
            return Task.CompletedTask;
        }, "capture mod transaction");
        Check(crashed != null && !ModUpdates.Pending(modAndBase.Anchor), "mod crash capture / ordinary rollback failed");
        foreach (var (path, bytes) in crashed!)
        {
            var full = EnvironmentCopy.Inside(modAndBase.Anchor, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        Put(modAndBase.Source, native, "new base after interrupted mod update");
        var combinedPreview = ManagedEnvironment.Inspect(modAndBase.Source);
        Check(combinedPreview.RecoveryPending && combinedPreview.RebuildReason != null, "combined recovery state not visible");
        await Execute(modAndBase.Source);
        Check(!ModUpdates.Pending(modAndBase.Anchor) && !BaseRebuild.Pending(modAndBase.Anchor) &&
            File.Exists(EnvironmentCopy.Inside(BaseRebuild.Resolve(modAndBase.Anchor, modAndBase.Source, false),
                ModPackage.ClientDirectory + "sounds/new.wav")), "combined mod recovery + native base change did not finish");
        Console.WriteLine($"BASE_REBUILD_CHECKS_PASS ({count} assertions)");
        return count;
    }
}
