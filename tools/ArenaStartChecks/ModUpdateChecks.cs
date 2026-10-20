using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using ArenaStart;

internal static class ModUpdateChecks
{
    internal static int Run(string scratch)
    {
        var count = 0;
        var caseNumber = 0;
        void Check(bool value, string reason)
        {
            count++;
            if (!value) throw new Exception("Mod updates: " + reason);
        }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or
                JsonException or BadImageFormatException or AggregateException) { count++; return; }
            throw new Exception("Accepted negative mod update control: " + reason);
        }
        static Dictionary<string, byte[]> Snapshot(string root) =>
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllBytes);
        static bool Same(Dictionary<string, byte[]> before, string root)
        {
            var after = Snapshot(root);
            return after.Count == before.Count && before.All(p => after.TryGetValue(p.Key, out var bytes) && bytes.SequenceEqual(p.Value));
        }
        static void Put(string root, string path, string value)
        {
            var target = EnvironmentCopy.Inside(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, value);
        }
        Manifest NewCase(bool diagnostics = false)
        {
            var directory = Path.Combine(scratch, "mod-update-" + ++caseNumber);
            var source = Path.Combine(directory, "source");
            var target = Path.Combine(directory, "arena");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            foreach (var path in ModPackage.Required.Concat(diagnostics ? [ModPackage.Bridge] : Array.Empty<string>()))
            {
                if (path.EndsWith(".dll", StringComparison.Ordinal)) AssemblyFile(source, path);
                else Put(source, path, """[{"fixture":"kit"}]""");
            }
            Put(source, ModPackage.ClientDirectory + "announcements/old.ogg", "old voice");
            Put(source, ModPackage.ClientDirectory + "sounds/hit.wav", "old sound");
            Put(source, ModPackage.ClientDirectory + "docs/LICENSE", "license");
            Put(source, "UnityPlayer.dll", "native binding, never refreshed");
            var owned = new List<OwnedFile>();
            foreach (var (path, bytes) in Snapshot(source))
            {
                var destination = EnvironmentCopy.Inside(target, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                owned.Add(new(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
            }
            Put(target, "BepInEx/config/BepInEx.cfg", EnvironmentCopy.NativeLoaderConfig);
            Put(target, "BepInEx/config/maschine-ArenaMode.cfg", "private cfg");
            Put(target, "SPT_Runtime/user/sptSettings/Game.ini", """{"private":true}""");
            Put(target, "SPT_Runtime/user/mods/ArenaMode/config/config.json", """{"privateMod":true}""");
            Put(target, "SPT_Runtime/user/mods/ArenaMode/config/client-settings.json", """{"privateClient":true}""");
            const string id = "0123456789abcdef01234567";
            Put(target, EnvironmentCopy.AccountPath, JsonSerializer.Serialize(new { profileId = id, username = RuntimeHost.Username }));
            Put(target, $"SPT_Runtime/user/profiles/{id}.json",
                JsonSerializer.Serialize(new { info = new { id, username = RuntimeHost.Username }, progress = 987 }));
            var manifest = new Manifest(2, target, source, owned, [owned.Single(f => f.Path == "UnityPlayer.dll")], diagnostics);
            Put(target, EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(manifest, EnvironmentCopy.Json));
            EnvironmentCopy.Verify(target);
            return manifest;
        }
        void Change(Manifest m)
        {
            AssemblyFile(m.Source, ModPackage.Client, tag: "same-version-new-build");
            Put(m.Source, ModPackage.ClientDirectory + "sounds/hit.wav", "updated hit sound");
            Put(m.Source, ModPackage.ClientDirectory + "sounds/new.wav", "new sound");
            File.Delete(EnvironmentCopy.Inside(m.Source, ModPackage.ClientDirectory + "announcements/old.ogg"));
        }
        void Preserved(Manifest m, Dictionary<string, byte[]> before)
        {
            foreach (var (path, bytes) in before.Where(p =>
                p.Key != EnvironmentCopy.ManifestPath && !ModPackage.Allows(p.Key, m.Diagnostics)))
                Check(File.ReadAllBytes(EnvironmentCopy.Inside(m.Root, path)).SequenceEqual(bytes), "protected file changed: " + path);
        }
        void RejectedUnchanged(Manifest m, string reason)
        {
            var before = Snapshot(m.Root);
            Reject(() => ModUpdates.Apply(m), reason);
            Check(Same(before, m.Root), "failed preflight changed private contents: " + reason);
        }

        var basic = NewCase();
        var initial = Snapshot(basic.Root);
        var sourceInitial = Snapshot(basic.Source);
        var unchanged = ModUpdates.Apply(basic);
        Check(unchanged == basic && Same(initial, basic.Root), "no-op wrote files");
        Check(Same(sourceInitial, basic.Source), "no-op modified source");
        Change(basic);
        var sourceUpdated = Snapshot(basic.Source);
        var preview = ModUpdates.Inspect(basic);
        Check(preview == new ModUpdateSummary(1, 2, 1), "preview did not find add/change/delete with same assembly version");
        var messages = new List<string>();
        var updated = ModUpdates.Apply(basic, messages.Add);
        Check(messages.Contains("Updating Arena mods: 0 / 4 files.") && messages.Contains("Updating Arena mods: 4 / 4 files."),
            "progress totals do not match completed changes");
        Check(ModUpdates.Inspect(updated).Total == 0, "successful update still differs");
        EnvironmentCopy.Verify(updated.Root);
        Check(!ModUpdates.Pending(basic.Root), "success left pending transaction");
        Preserved(basic, initial);
        Check(Same(sourceUpdated, basic.Source), "update modified source");
        var finished = Snapshot(updated.Root);
        ModUpdates.Apply(updated, _ => throw new Exception("broken UI observer"));
        Check(Same(finished, updated.Root), "second launch not a no-op");

        foreach (var required in ModPackage.Required)
        {
            var missing = NewCase();
            File.Delete(EnvironmentCopy.Inside(missing.Source, required));
            RejectedUnchanged(missing, "missing required " + required);
        }
        var incomplete = NewCase();
        Put(incomplete.Source, ModPackage.Required[^1], "{}");
        RejectedUnchanged(incomplete, "kit data is not an array");
        var empty = NewCase();
        Put(empty.Source, ModPackage.Required[^1], "[]");
        RejectedUnchanged(empty, "empty kit array");
        var versionMismatch = NewCase();
        AssemblyFile(versionMismatch.Source, ModPackage.Server, version: new(2, 0, 0, 0));
        RejectedUnchanged(versionMismatch, "mixed client/server releases");
        var oldCore = NewCase();
        AssemblyFile(oldCore.Source, ModPackage.Client, minimum: "9.0.0");
        RejectedUnchanged(oldCore, "old Core");
        var noMinimum = NewCase();
        AssemblyFile(noMinimum.Source, ModPackage.Client, minimum: null);
        RejectedUnchanged(noMinimum, "no minimum Core contract");
        var wrongDll = NewCase();
        Put(wrongDll.Source, ModPackage.Client, "not a PE");
        RejectedUnchanged(wrongDll, "broken assembly");
        var injected = NewCase();
        Put(injected.Source, ModPackage.ClientDirectory + "sounds/evil.dll", "bad");
        RejectedUnchanged(injected, "executable in asset tree");
        var filters = NewCase();
        foreach (var path in new[] { "sounds/reserve/later.wav", "sounds/hit.sync-conflict-123.wav",
                     "announcements/readme.txt", "docs/tool.ps1", "AreaDrafts/draft.json" })
            Put(filters.Source, ModPackage.ClientDirectory + path, "not approved");
        Put(filters.Source, ModPackage.Bridge, "not in diagnostic mode");
        Put(filters.Source, "SPT_Runtime/user/mods/ArenaMode/config/config.json", """{"foreign":true}""");
        var filtered = ModUpdates.Apply(filters);
        Check(ModUpdates.Inspect(filtered).Total == 0 && filtered == filters, "unapproved source content entered package");
        foreach (var path in new[] { "../profiles/p.json", "a:stream", ModPackage.ClientDirectory + "sounds/../evil.wav",
                     ModPackage.ClientDirectory + "sounds/a.dll", EnvironmentCopy.AccountPath })
            Check(!ModPackage.Allows(path, true), "path accepted by catalog: " + path);

        var drift = NewCase();
        Change(drift);
        Put(drift.Root, ModPackage.Client, "manually changed");
        Reject(() => EnvironmentCopy.Verify(drift.Root), "whole environment accepts changed old DLL");
        RejectedUnchanged(drift, "updater repairs altered private DLL");
        var collision = NewCase();
        Change(collision);
        Put(collision.Root, ModPackage.ClientDirectory + "sounds/new.wav", "private unknown collision");
        RejectedUnchanged(collision, "unknown new destination");
        var locked = NewCase();
        Change(locked);
        using (var blocker = new FileStream(EnvironmentCopy.Inside(locked.Root, ModPackage.Client), FileMode.Open, FileAccess.Read, FileShare.None))
            Reject(() => ModUpdates.Apply(locked), "locked old destination");
        Check(!ModUpdates.Pending(locked.Root), "lock preflight activated transaction");
        var readonlyCase = NewCase();
        Change(readonlyCase);
        var readonlyPath = EnvironmentCopy.Inside(readonlyCase.Root, ModPackage.Client);
        File.SetAttributes(readonlyPath, FileAttributes.ReadOnly);
        try { RejectedUnchanged(readonlyCase, "read-only old target"); }
        finally { File.SetAttributes(readonlyPath, FileAttributes.Normal); }
        var changingSource = NewCase();
        var beforeChangingSource = Snapshot(changingSource.Root);
        Change(changingSource);
        Reject(() => ModUpdates.Apply(changingSource, checkpoint: stage =>
        {
            if (stage != "staged") return;
            Reject(() =>
            {
                using var writer = new FileStream(EnvironmentCopy.Inside(changingSource.Source, ModPackage.Client),
                    FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }, "source lease did not block a concurrent installer");
            Put(changingSource.Source, ModPackage.ClientDirectory + "sounds/arrived.wav", "new during capture");
        }), "source catalog changed during staging");
        Check(Same(beforeChangingSource, changingSource.Root), "source catalog drift changed target");
        foreach (var stageToChange in new[] { "staged", "journal" })
        {
            var changedTarget = NewCase();
            Change(changedTarget);
            const string extraPath = ModPackage.ClientDirectory + "sounds/new.wav";
            Reject(() => ModUpdates.Apply(changedTarget, checkpoint: stage =>
            {
                if (stage == stageToChange) Put(changedTarget.Root, extraPath, "unknown intervening file");
            }), "intervening new target at " + stageToChange);
            Check(File.ReadAllText(EnvironmentCopy.Inside(changedTarget.Root, extraPath)) == "unknown intervening file",
                "intervening target overwritten at " + stageToChange);
        }
        var reappeared = NewCase();
        Change(reappeared);
        const string removedPath = ModPackage.ClientDirectory + "announcements/old.ogg";
        Reject(() => ModUpdates.Apply(reappeared, checkpoint: stage =>
        {
            // Sorted package: the removed announcement is the first operation.
            if (stage == "file:0") Put(reappeared.Root, removedPath, "external reappearance");
        }), "removed file reappeared before commit");
        Check(File.ReadAllText(EnvironmentCopy.Inside(reappeared.Root, removedPath)) == "external reappearance" &&
            ModUpdates.Pending(reappeared.Root), "unknown reappeared file lost or pending recovery discarded");
        var linkedSource = NewCase();
        var link = EnvironmentCopy.Inside(linkedSource.Source, "extra-hardlink.bin");
        Check(NativeLinks.CreateHardLink(link, EnvironmentCopy.Inside(linkedSource.Source, ModPackage.Client), IntPtr.Zero),
            "source hardlink negative fixture not created");
        RejectedUnchanged(linkedSource, "source mod hardlink");
        File.Delete(link);
        var diagnostics = NewCase(diagnostics: true);
        AssemblyFile(diagnostics.Source, ModPackage.Bridge, tag: "bridge-update");
        var diagnosticUpdate = ModUpdates.Apply(diagnostics);
        Check(diagnosticUpdate.Diagnostics && ModUpdates.Inspect(diagnosticUpdate).Total == 0,
            "explicit diagnostics not preserved/updated");
        File.Delete(EnvironmentCopy.Inside(diagnostics.Source, ModPackage.Bridge));
        RejectedUnchanged(diagnosticUpdate, "missing required diagnostic bridge");
        var fullCopy = NewCase();
        fullCopy = fullCopy with { Schema = 1, SourceBindings = null };
        Put(fullCopy.Root, EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(fullCopy, EnvironmentCopy.Json));
        Change(fullCopy);
        Put(fullCopy.Source, "UnityPlayer.dll", "updated native runtime");
        RejectedUnchanged(fullCopy, "fullcopy mixed native source version");
        var nativeDrift = NewCase();
        Change(nativeDrift);
        Put(nativeDrift.Source, "UnityPlayer.dll", "updated after initial verification");
        RejectedUnchanged(nativeDrift, "thin source native drift during long asset check");
        var nativeLease = NewCase();
        Change(nativeLease);
        ModUpdates.Apply(nativeLease, checkpoint: stage =>
        {
            if (stage != "staged") return;
            Reject(() =>
            {
                using var writer = new FileStream(EnvironmentCopy.Inside(nativeLease.Source, "UnityPlayer.dll"),
                    FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }, "native source binding not leased during update");
        });
        using (var writer = new FileStream(EnvironmentCopy.Inside(nativeLease.Source, "UnityPlayer.dll"),
                   FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            Check(writer.CanWrite, "native source lease leaked after update");

        // Catchable failures roll back automatically. Separately capture each live
        // transaction state and restore those bytes to model process termination.
        foreach (var stop in new[] { "staged", "journal", "file:0", "file:1", "file:2", "file:3", "manifest" })
        {
            var interrupted = NewCase();
            var before = Snapshot(interrupted.Root);
            Change(interrupted);
            Dictionary<string, byte[]>? crash = null;
            Reject(() => ModUpdates.Apply(interrupted, checkpoint: stage =>
            {
                if (stage != stop) return;
                crash = Snapshot(interrupted.Root);
                throw new IOException("injected update failure at " + stop);
            }), "checkpoint " + stop);
            Check(crash != null && Same(before, interrupted.Root), "automatic rollback failed at " + stop);
            if (stop == "staged") continue;
            Restore(interrupted.Root, crash!);
            ModUpdates.Recover(interrupted.Root);
            Check(Same(before, interrupted.Root), "restart recovery failed at " + stop);
            ModUpdates.Recover(interrupted.Root);
            Check(Same(before, interrupted.Root), "recovery not idempotent");
            EnvironmentCopy.Verify(interrupted.Root);
        }

        // Missing/tampered backups and unrelated file changes must fail before
        // the first restore, rather than silently erasing evidence.
        foreach (var defect in new[] { "backup", "journal", "manifest", "third-content", "scope" })
        {
            var damaged = NewCase();
            Change(damaged);
            Dictionary<string, byte[]>? crash = null;
            Reject(() => ModUpdates.Apply(damaged, checkpoint: stage =>
            {
                if (stage != "manifest") return;
                crash = Snapshot(damaged.Root);
                throw new IOException("capture");
            }), "capture crash fixture");
            Restore(damaged.Root, crash!);
            var journalFile = EnvironmentCopy.Inside(damaged.Root, ModUpdates.JournalPath);
            var journal = JsonSerializer.Deserialize<ModUpdateJournal>(File.ReadAllBytes(journalFile), EnvironmentCopy.Json)!;
            var folder = ".arena-start/mod-updates/" + journal.Transaction;
            switch (defect)
            {
                case "backup": Put(damaged.Root, folder + "/old-0.bin", "corrupt"); break;
                case "journal": File.WriteAllText(journalFile, "{}"); break;
                case "manifest": File.AppendAllText(EnvironmentCopy.Inside(damaged.Root, EnvironmentCopy.ManifestPath), " "); break;
                case "third-content": Put(damaged.Root, ModPackage.Client, "external modification"); break;
                case "scope":
                    var newPath = EnvironmentCopy.Inside(damaged.Root, folder + "/new-manifest.bin");
                    var candidate = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(newPath), EnvironmentCopy.Json)!;
                    candidate = candidate with { Files = candidate.Files.Select(f =>
                        f.Path == "UnityPlayer.dll" ? f with { Sha256 = new string('A', 64) } : f).ToList() };
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate, EnvironmentCopy.Json);
                    File.WriteAllBytes(newPath, bytes);
                    File.WriteAllBytes(journalFile, JsonSerializer.SerializeToUtf8Bytes(journal with {
                        NewManifestHash = Convert.ToHexString(SHA256.HashData(bytes)) }, EnvironmentCopy.Json));
                    break;
            }
            var damagedBefore = Snapshot(damaged.Root);
            Reject(() => ModUpdates.Recover(damaged.Root), "damaged recovery " + defect);
            Check(Same(damagedBefore, damaged.Root), "damaged recovery wrote something: " + defect);
        }
        var repeated = NewCase();
        var repeatedBefore = Snapshot(repeated.Root);
        Change(repeated);
        Dictionary<string, byte[]>? restart = null;
        Reject(() => ModUpdates.Apply(repeated, checkpoint: stage =>
        {
            if (stage != "manifest") return;
            restart = Snapshot(repeated.Root);
            throw new IOException("capture");
        }), "capture repeated rollback");
        Restore(repeated.Root, restart!);
        Reject(() => ModUpdates.Recover(repeated.Root, checkpoint: stage =>
        {
            if (stage == "restore:1") throw new IOException("rollback interrupted");
        }), "rollback interruption");
        Check(ModUpdates.Pending(repeated.Root), "interrupted rollback lost journal");
        ModUpdates.Recover(repeated.Root);
        Check(Same(repeatedBefore, repeated.Root), "repeated rollback failed");
        foreach (var defect in new[] { "later-backup", "restored-target" })
        {
            var changingRecovery = NewCase();
            Change(changingRecovery);
            Dictionary<string, byte[]>? state = null;
            Reject(() => ModUpdates.Apply(changingRecovery, checkpoint: stage =>
            {
                if (stage != "manifest") return;
                state = Snapshot(changingRecovery.Root);
                throw new IOException("capture changing recovery");
            }), "capture changing recovery");
            Restore(changingRecovery.Root, state!);
            var transaction = JsonSerializer.Deserialize<ModUpdateJournal>(
                File.ReadAllBytes(EnvironmentCopy.Inside(changingRecovery.Root, ModUpdates.JournalPath)), EnvironmentCopy.Json)!.Transaction;
            var recoveryMessages = new List<string>();
            Reject(() => ModUpdates.Recover(changingRecovery.Root, recoveryMessages.Add, stage =>
            {
                if (stage != "restore:0") return;
                if (defect == "later-backup") Put(changingRecovery.Root, ".arena-start/mod-updates/" + transaction + "/old-1.bin", "corrupt");
                else Put(changingRecovery.Root, ModPackage.ClientDirectory + "announcements/old.ogg", "external");
            }), "changing recovery " + defect);
            Check(ModUpdates.Pending(changingRecovery.Root) && !recoveryMessages.Any(m => m.StartsWith("Previous Arena mod package restored")),
                "changing recovery lost journal or reported success: " + defect);
        }

        // Preview must allow recovery even while the normal installation is
        // temporarily incomplete. It must never recover during read-only inspect.
        var recoveryPreview = NewCase();
        var managedRoot = ManagedEnvironment.Destination(recoveryPreview.Source);
        if (new[] { recoveryPreview.Root, managedRoot }.Any(p =>
            !Path.GetFullPath(p).StartsWith(Path.GetFullPath(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Managed recovery fixture escaped scratch.");
        Directory.Move(recoveryPreview.Root, managedRoot);
        recoveryPreview = recoveryPreview with { Root = managedRoot };
        Put(managedRoot, EnvironmentCopy.ManifestPath, JsonSerializer.Serialize(recoveryPreview, EnvironmentCopy.Json));
        Put(managedRoot, ModUpdates.JournalPath, "pending fixture; inspect must not read or mutate it");
        File.Delete(EnvironmentCopy.Inside(recoveryPreview.Source, ModPackage.Client));
        var beforePreview = Snapshot(managedRoot);
        var managedPreview = ManagedEnvironment.Inspect(recoveryPreview.Source);
        Check(managedPreview.RecoveryPending && managedPreview.Updates == null && Same(beforePreview, managedRoot),
            "read-only pending preview depends on source or recovered files");

        Console.WriteLine($"MOD_UPDATE_CHECKS_PASS ({count} assertions)");
        return count;

        void Restore(string root, Dictionary<string, byte[]> state)
        {
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(Path.GetFullPath(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Recovery test root escaped its scratch directory.");
            Directory.Delete(resolved, recursive: true);
            Directory.CreateDirectory(resolved);
            foreach (var (path, bytes) in state)
            {
                var target = EnvironmentCopy.Inside(resolved, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
        }
    }

    private static void AssemblyFile(string root, string path, string tag = "original", Version? version = null, string? minimum = "1.0.0")
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(name) { Version = version ?? new(1, 2, 3, 0) }, typeof(object).Assembly);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, ["BuildTag", tag]));
        if (path == ModPackage.Client && minimum != null)
            assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, ["RedeployCoreBuiltAgainst", minimum]));
        var module = assembly.DefineDynamicModule(name);
        module.DefineType("PackageFixture", TypeAttributes.Public).CreateType();
        var target = EnvironmentCopy.Inside(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        assembly.Save(target);
    }
}
