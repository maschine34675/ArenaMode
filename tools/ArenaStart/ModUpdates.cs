using System.Security.Cryptography;
using System.Text.Json;

namespace ArenaStart;

public sealed record ModUpdateSummary(int Added, int Changed, int Removed)
{
    public int Total => Added + Changed + Removed;
}

internal sealed record ModUpdateJournal(int Schema, string Root, string Transaction,
    string OldManifestHash, string NewManifestHash);

internal static class ModUpdates
{
    internal const string JournalPath = ".arena-start/mod-update.json";
    private const string Storage = ".arena-start/mod-updates";
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    internal static bool Pending(string root) => File.Exists(EnvironmentCopy.Inside(root, JournalPath));

    internal static ModUpdateSummary Inspect(Manifest manifest)
    {
        using var capture = new Capture(manifest);
        return Difference(manifest, capture.Entries);
    }

    // RuntimeHost owns the exclusive running lease and has verified the entire old
    // environment. Its native shared-asset leases stay held throughout this update.
    internal static Manifest Apply(Manifest manifest, Action<string>? report = null, Action<string>? checkpoint = null)
    {
        RuntimeHost.RequireIdle();
        var messages = new RuntimeReporter(report ?? (_ => { }));
        var root = EnvironmentCopy.Root(manifest.Root);
        if (Pending(root)) throw new InvalidDataException("An interrupted mod update must be recovered before verification.");
        using var capture = new Capture(manifest);
        ModPackage.Validate(capture.Streams);
        var delta = Difference(manifest, capture.Entries);
        if (delta.Total == 0)
        {
            messages.Write("Arena mods are up to date. Private mod files are reused.");
            return manifest;
        }
        // The large asset check can take minutes. Recheck and retain native
        // source bindings while refreshing mods, including copy-only runtimes,
        // so an intervening SPT update cannot supply mods for a different base.
        capture.BindNativeVersion();
        var oldBytes = ReadBytes(root, EnvironmentCopy.ManifestPath);
        var diskManifest = ParseManifest(oldBytes, root);
        if (Serialize(diskManifest).AsSpan().SequenceEqual(Serialize(manifest)) == false)
            throw new InvalidDataException("The Arena manifest changed after verification.");
        var updated = manifest with
        {
            Files = manifest.Files.Where(f => !ModPackage.Allows(f.Path, manifest.Diagnostics))
                .Concat(capture.Entries).OrderBy(f => f.Path, StringComparer.Ordinal).ToList()
        };
        ValidatePair(manifest, updated);
        var changes = ChangedPaths(manifest, updated);
        // All affected destinations are checked before any staging or replacement.
        foreach (var path in changes)
            CheckTarget(root, path, Find(manifest, path), Find(manifest, path), requireWritable: true);
        var newBytes = Serialize(updated);
        var needed = changes.Sum(p => (Find(manifest, p)?.Length ?? 0) + (Find(updated, p)?.Length ?? 0));
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < needed + 256L * 1024 * 1024)
            throw new IOException("Not enough space for the mod update, rollback copies and 256 MiB reserve.");
        var transaction = Guid.NewGuid().ToString("N");
        var directory = Storage + "/" + transaction;
        var journal = new ModUpdateJournal(1, root, transaction, Digest(oldBytes), Digest(newBytes));
        var activated = false;
        try
        {
            Directory.CreateDirectory(EnvironmentCopy.Inside(root, directory));
            WriteBlob(root, directory + "/old-manifest.bin", oldBytes);
            WriteBlob(root, directory + "/new-manifest.bin", newBytes);
            messages.Write($"Preparing mod update: {delta.Added} new, {delta.Changed} changed, {delta.Removed} removed files.");
            for (var i = 0; i < changes.Count; i++)
            {
                var old = Find(manifest, changes[i]);
                var next = Find(updated, changes[i]);
                if (old != null) CopyBlob(root, old.Path, directory + $"/old-{i}.bin", old);
                if (next != null)
                {
                    var input = capture.Streams[next.Path];
                    input.Position = 0;
                    WriteStream(root, directory + $"/new-{i}.bin", input);
                    CheckBlob(root, directory + $"/new-{i}.bin", next);
                }
            }
            capture.CheckCatalog();
            checkpoint?.Invoke("staged");
            capture.CheckCatalog();
            foreach (var path in changes)
                CheckTarget(root, path, Find(manifest, path), Find(manifest, path), requireWritable: true);
            // WriteNew + rename: incomplete journal writes cannot look committed.
            WriteBlob(root, directory + "/journal.bin", JsonSerializer.SerializeToUtf8Bytes(journal, EnvironmentCopy.Json));
            File.Move(EnvironmentCopy.Inside(root, directory + "/journal.bin"), EnvironmentCopy.Inside(root, JournalPath));
            activated = true;
            checkpoint?.Invoke("journal");
            messages.Write($"Updating Arena mods: 0 / {changes.Count} files.");
            for (var i = 0; i < changes.Count; i++)
            {
                var before = Find(manifest, changes[i]);
                Replace(root, changes[i], Find(updated, changes[i]), directory + $"/new-{i}.bin", before, before);
                checkpoint?.Invoke("file:" + i);
                messages.Write($"Updating Arena mods: {i + 1} / {changes.Count} files.");
            }
            foreach (var file in capture.Entries) CheckTarget(root, file.Path, file, file, requireWritable: false);
            foreach (var removed in changes.Where(p => Find(updated, p) == null))
                CheckTarget(root, removed, null, null, requireWritable: false);
            var oldManifestFile = new OwnedFile(EnvironmentCopy.ManifestPath, oldBytes.Length, journal.OldManifestHash);
            var newManifestFile = new OwnedFile(EnvironmentCopy.ManifestPath, newBytes.Length, journal.NewManifestHash);
            AtomicCopy(root, directory + "/new-manifest.bin", EnvironmentCopy.ManifestPath, newManifestFile,
                () => CheckTarget(root, EnvironmentCopy.ManifestPath, oldManifestFile, oldManifestFile, requireWritable: true));
            checkpoint?.Invoke("manifest");
            File.Delete(EnvironmentCopy.Inside(root, JournalPath));
            activated = false;
            messages.Write($"Arena mod update complete: {delta.Total} files refreshed. Internal profile and private settings preserved.");
            return updated;
        }
        catch (Exception updateError)
        {
            if (activated)
            {
                try { Recover(root, messages.Write); }
                catch (Exception recoveryError)
                {
                    throw new AggregateException("Mod update was interrupted; rollback is pending. No game/server was started. Close programs using Arena files, then retry.",
                        updateError, recoveryError);
                }
            }
            throw;
        }
        finally
        {
            // Recovery data must survive if any rollback action failed.
            if (!Pending(root)) Cleanup(root, directory);
        }
    }

    internal static void Recover(string root, Action<string>? report = null, Action<string>? checkpoint = null)
    {
        root = EnvironmentCopy.Root(root);
        if (!Pending(root)) return;
        var messages = new RuntimeReporter(report ?? (_ => { }));
        var journalBytes = ReadBytes(root, JournalPath);
        var journal = JsonSerializer.Deserialize<ModUpdateJournal>(journalBytes, EnvironmentCopy.Json)
            ?? throw new InvalidDataException("Empty mod update journal.");
        if (journal.Schema != 1 || !Paths.Equals(journal.Root, root) || !Guid.TryParseExact(journal.Transaction, "N", out _))
            throw new InvalidDataException("Invalid mod update journal identity.");
        var directory = Storage + "/" + journal.Transaction;
        var oldBytes = ReadBytes(root, directory + "/old-manifest.bin");
        var newBytes = ReadBytes(root, directory + "/new-manifest.bin");
        if (Digest(oldBytes) != journal.OldManifestHash || Digest(newBytes) != journal.NewManifestHash)
            throw new InvalidDataException("Damaged mod update manifest backup. No files were restored.");
        var old = ParseManifest(oldBytes, root);
        var next = ParseManifest(newBytes, root);
        ValidatePair(old, next);
        var current = Digest(ReadBytes(root, EnvironmentCopy.ManifestPath));
        if (current != journal.OldManifestHash && current != journal.NewManifestHash)
            throw new InvalidDataException("The manifest changed outside the interrupted update. Recovery refused.");
        var changes = ChangedPaths(old, next);
        // Validate the complete recovery set before touching even its first file.
        for (var i = 0; i < changes.Count; i++)
        {
            var before = Find(old, changes[i]);
            var after = Find(next, changes[i]);
            if (before != null) CheckBlob(root, directory + $"/old-{i}.bin", before);
            if (after != null) CheckBlob(root, directory + $"/new-{i}.bin", after);
            CheckTarget(root, changes[i], before, after, requireWritable: true);
        }
        messages.Write("Recovering interrupted mod update. Restoring the previous Arena package before verification.");
        for (var i = 0; i < changes.Count; i++)
        {
            Replace(root, changes[i], Find(old, changes[i]), directory + $"/old-{i}.bin", Find(old, changes[i]), Find(next, changes[i]));
            checkpoint?.Invoke("restore:" + i);
        }
        foreach (var path in changes)
        {
            var before = Find(old, path);
            CheckTarget(root, path, before, before, requireWritable: false);
        }
        var oldManifestFile = new OwnedFile(EnvironmentCopy.ManifestPath, oldBytes.Length, journal.OldManifestHash);
        var newManifestFile = new OwnedFile(EnvironmentCopy.ManifestPath, newBytes.Length, journal.NewManifestHash);
        AtomicCopy(root, directory + "/old-manifest.bin", EnvironmentCopy.ManifestPath, oldManifestFile,
            () => CheckTarget(root, EnvironmentCopy.ManifestPath, oldManifestFile, newManifestFile, requireWritable: true));
        checkpoint?.Invoke("restored-manifest");
        if (!ReadBytes(root, JournalPath).AsSpan().SequenceEqual(journalBytes))
            throw new InvalidDataException("The recovery journal changed. Recovery evidence retained.");
        File.Delete(EnvironmentCopy.Inside(root, JournalPath));
        Cleanup(root, directory);
        messages.Write("Previous Arena mod package restored. The next update will be checked again.");
    }

    private static byte[] Serialize(Manifest value) => JsonSerializer.SerializeToUtf8Bytes(value, EnvironmentCopy.Json);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static OwnedFile? Find(Manifest manifest, string path) => manifest.Files.FirstOrDefault(f => Paths.Equals(f.Path, path));
    private static List<string> ChangedPaths(Manifest old, Manifest next)
    {
        var before = old.Files.ToDictionary(f => f.Path, Paths);
        var after = next.Files.ToDictionary(f => f.Path, Paths);
        return before.Keys.Union(after.Keys, Paths)
            .Where(p => before.GetValueOrDefault(p) != after.GetValueOrDefault(p))
            .Order(StringComparer.Ordinal).ToList();
    }

    private static ModUpdateSummary Difference(Manifest manifest, List<OwnedFile> entries)
    {
        var old = manifest.Files.Where(f => ModPackage.Allows(f.Path, manifest.Diagnostics)).ToDictionary(f => f.Path, Paths);
        return new(entries.Count(f => !old.ContainsKey(f.Path)),
            entries.Count(f => old.TryGetValue(f.Path, out var previous) && previous != f),
            old.Keys.Count(p => !entries.Any(f => Paths.Equals(f.Path, p))));
    }

    private static Manifest ParseManifest(byte[] bytes, string root)
    {
        var value = JsonSerializer.Deserialize<Manifest>(bytes, EnvironmentCopy.Json);
        if (value == null || value.Schema is not (1 or 2) || !Paths.Equals(value.Root, root) || value.Files.Count == 0)
            throw new InvalidDataException("Invalid mod update manifest.");
        EnvironmentCopy.Disjoint(root, EnvironmentCopy.Root(value.Source));
        var unique = new HashSet<string>(Paths);
        foreach (var file in value.Files)
        {
            EnvironmentCopy.Inside(root, file.Path);
            if (!unique.Add(file.Path) || file.Length < 0 || file.Sha256.Length != 64 ||
                file.Sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid mod update catalog.");
        }
        return value;
    }

    private static void ValidatePair(Manifest old, Manifest next)
    {
        // Removing the package must leave exactly the same manifest, including
        // all native hashes, source bindings, diagnostics and the private root.
        Manifest WithoutPackage(Manifest m) => m with
        {
            Files = m.Files.Where(f => !ModPackage.Allows(f.Path, m.Diagnostics))
                .OrderBy(f => f.Path, StringComparer.Ordinal).ToList()
        };
        if (!Serialize(WithoutPackage(old)).AsSpan().SequenceEqual(Serialize(WithoutPackage(next))))
            throw new InvalidDataException("An update attempted to change files outside the approved mod package.");
        foreach (var manifest in new[] { old, next })
        {
            ParseManifest(Serialize(manifest), manifest.Root);
            foreach (var required in ModPackage.Required.Concat(manifest.Diagnostics ? [ModPackage.Bridge] : Array.Empty<string>()))
                if (Find(manifest, required) is not { Shared: false })
                    throw new InvalidDataException("Incomplete or shared mod update package.");
            if (manifest.Files.Any(f => f.Shared && ModPackage.Allows(f.Path, manifest.Diagnostics)))
                throw new InvalidDataException("Mod update data must be private.");
        }
        foreach (var path in ChangedPaths(old, next))
            if (!ModPackage.Allows(path, old.Diagnostics) ||
                Find(old, path) is { } previous && Find(next, path) is { } after && previous.Path != after.Path)
                throw new InvalidDataException("Unapproved or case-renamed update path.");
    }

    private static void CheckTarget(string root, string path, OwnedFile? old, OwnedFile? next, bool requireWritable)
    {
        var target = EnvironmentCopy.Inside(root, path);
        if (Directory.Exists(target)) throw new IOException("Directory occupies mod file path: " + path);
        if (!File.Exists(target))
        {
            if (old != null && next != null) throw new InvalidDataException("Missing private update target: " + path);
            return;
        }
        using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, requireWritable ? FileShare.None : FileShare.Read);
        EnvironmentCopy.RequireSingleLink(stream.SafeFileHandle);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        bool Matches(OwnedFile? entry) => entry != null && entry.Length == stream.Length && entry.Sha256 == hash;
        if (!Matches(old) && !Matches(next)) throw new InvalidDataException("Unowned/changed mod update target: " + path);
        if (requireWritable && (File.GetAttributes(target) & FileAttributes.ReadOnly) != 0)
            throw new IOException("Read-only mod update target: " + path);
    }

    private static byte[] ReadBytes(string root, string relative)
    {
        using var file = new FileStream(EnvironmentCopy.Inside(root, relative), FileMode.Open, FileAccess.Read, FileShare.Read);
        EnvironmentCopy.RequireSingleLink(file.SafeFileHandle);
        if (file.Length > 32 * 1024 * 1024) throw new InvalidDataException("Oversized update metadata.");
        using var output = new MemoryStream();
        file.CopyTo(output);
        return output.ToArray();
    }

    private static void CheckBlob(string root, string relative, OwnedFile entry)
    {
        var file = EnvironmentCopy.Inside(root, relative);
        if (!File.Exists(file) || new FileInfo(file).Length != entry.Length || EnvironmentCopy.Hash(file) != entry.Sha256)
            throw new InvalidDataException("Damaged/missing update blob: " + relative);
    }

    private static void CopyBlob(string root, string source, string target, OwnedFile entry)
    {
        using var input = new FileStream(EnvironmentCopy.Inside(root, source), FileMode.Open, FileAccess.Read, FileShare.Read);
        EnvironmentCopy.RequireSingleLink(input.SafeFileHandle);
        WriteStream(root, target, input);
        CheckBlob(root, target, entry);
    }

    private static void WriteBlob(string root, string relative, byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        WriteStream(root, relative, input);
    }

    private static void WriteStream(string root, string relative, Stream input)
    {
        using var output = new FileStream(EnvironmentCopy.Inside(root, relative), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void Replace(string root, string target, OwnedFile? file, string blob, OwnedFile? before, OwnedFile? after)
    {
        void Validate() => CheckTarget(root, target, before, after, requireWritable: true);
        if (file == null)
        {
            Validate();
            File.Delete(EnvironmentCopy.Inside(root, target));
        }
        else AtomicCopy(root, blob, target, file, Validate);
    }

    private static void AtomicCopy(string root, string blob, string relative, OwnedFile expected, Action validate)
    {
        var target = EnvironmentCopy.Inside(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Atomic rename stays on this private volume. GUID scratch files contain
        // no executable suffix and are never used as rollback authority.
        var temporary = Storage + "/replace-" + Guid.NewGuid().ToString("N") + ".bin";
        try
        {
            using (var input = new FileStream(EnvironmentCopy.Inside(root, blob), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                EnvironmentCopy.RequireSingleLink(input.SafeFileHandle);
                if (input.Length != expected.Length || Convert.ToHexString(SHA256.HashData(input)) != expected.Sha256)
                    throw new InvalidDataException("Update/rollback blob changed before copying: " + blob);
                input.Position = 0;
                WriteStream(root, temporary, input);
            }
            validate();
            File.Move(EnvironmentCopy.Inside(root, temporary), target, overwrite: true);
        }
        finally { File.Delete(EnvironmentCopy.Inside(root, temporary)); }
    }

    private static void Cleanup(string root, string relative)
    {
        // Best-effort, shallow cleanup of our exact transaction files only.
        // Never recursively delete an unexpected directory or an arbitrary path.
        try
        {
            var directory = EnvironmentCopy.Inside(root, relative);
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(path);
                if (name is not ("old-manifest.bin" or "new-manifest.bin" or "journal.bin") &&
                    !System.Text.RegularExpressions.Regex.IsMatch(name, @"^(old|new)-[0-9]+\.bin$"))
                    continue;
                EnvironmentCopy.AssertAncestors(path);
                EnvironmentCopy.InspectPrivate(path);
                File.Delete(path);
            }
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException) { /* Harmless orphaned data; it is not a pending update. */ }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Capture : IDisposable
    {
        private readonly Manifest manifest;
        private readonly List<string> paths;
        internal readonly Dictionary<string, FileStream> Streams = new(Paths);
        internal readonly List<OwnedFile> Entries = [];
        private readonly List<FileStream> nativeBindings = [];
        internal Capture(Manifest manifest)
        {
            this.manifest = manifest;
            EnvironmentCopy.Disjoint(EnvironmentCopy.Root(manifest.Root), EnvironmentCopy.Root(manifest.Source));
            paths = ModPackage.Enumerate(manifest.Source, manifest.Diagnostics);
            try
            {
                foreach (var path in paths)
                {
                    var file = new FileStream(EnvironmentCopy.Inside(manifest.Source, path), FileMode.Open, FileAccess.Read, FileShare.Read);
                    Streams.Add(path, file);
                    EnvironmentCopy.RequireSingleLink(file.SafeFileHandle);
                    Entries.Add(new(path, file.Length, Convert.ToHexString(SHA256.HashData(file))));
                }
                CheckCatalog();
            }
            catch { Dispose(); throw; }
        }
        internal void CheckCatalog()
        {
            if (!paths.SequenceEqual(ModPackage.Enumerate(manifest.Source, manifest.Diagnostics)))
                throw new InvalidDataException("The installed mod package changed while being captured. Finish updating normal SPT and retry.");
        }
        internal void BindNativeVersion()
        {
            var bindings = manifest.Schema == 1
                ? manifest.Files.Where(f => EnvironmentCopy.IsSourceBinding(f.Path)).ToList()
                : manifest.SourceBindings;
            if (bindings == null || bindings.Count == 0)
                throw new InvalidDataException("Missing native source version bindings for the mod update.");
            foreach (var binding in bindings)
            {
                var file = new FileStream(EnvironmentCopy.Inside(manifest.Source, binding.Path),
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                nativeBindings.Add(file);
                EnvironmentCopy.RequireSingleLink(file.SafeFileHandle);
                if (file.Length != binding.Length || Convert.ToHexString(SHA256.HashData(file)) != binding.Sha256)
                    throw new InvalidDataException("Native source version changed. Prepare a new Arena environment before updating mods.");
            }
        }
        public void Dispose()
        {
            foreach (var stream in Streams.Values) stream.Dispose();
            foreach (var stream in nativeBindings) stream.Dispose();
        }
    }
}
