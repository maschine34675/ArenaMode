using System.Security.Cryptography;
using System.Text.Json;

namespace ArenaStart;

internal sealed record ArenaGeneration(int Schema, string Anchor, string Source, string Id,
    bool Diagnostics, string InitialManifestHash);
internal sealed record RebuildJournal(int Schema, ArenaGeneration Next, string? PreviousPointerHash,
    string PreviousRoot, string PreviousManifestHash, bool Validated);

// The original managed directory stays the control anchor. Generations never move:
// their absolute manifest roots, native settings and logs remain valid. A tiny
// pointer is the only activation operation; previous profiles are never overwritten.
public static class BaseRebuild
{
    internal const string Pointer = ".arena-start/active-generation.json";
    internal const string Journal = ".arena-start/base-rebuild.json";
    internal const string Owner = ".arena-start/generation.json";
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    internal static string GenerationRoot(string anchor, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid rebuild generation.");
        anchor = EnvironmentCopy.Root(anchor);
        return EnvironmentCopy.Root(Path.Combine(anchor + ".generations", id));
    }

    internal static FileStream Acquire(string anchor)
    {
        anchor = EnvironmentCopy.Root(anchor);
        if (File.Exists(EnvironmentCopy.Inside(anchor, Owner)) ||
            Path.GetFileName(Path.GetDirectoryName(anchor)!).EndsWith(".generations", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use the managed Arena folder, not a generation directly.");
        var owner = Read<Manifest>(anchor, EnvironmentCopy.ManifestPath);
        if (owner.Schema is not (1 or 2) || !Paths.Equals(owner.Root, anchor) || string.IsNullOrEmpty(owner.Source))
            throw new InvalidDataException("No owned Arena environment at this path.");
        EnvironmentCopy.Disjoint(anchor, EnvironmentCopy.Root(owner.Source));
        var lease = new FileStream(EnvironmentCopy.Inside(anchor, ".arena-start/running"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { EnvironmentCopy.RequireSingleLink(lease.SafeFileHandle); return lease; }
        catch { lease.Dispose(); throw; }
    }

    internal static bool Pending(string anchor) => File.Exists(EnvironmentCopy.Inside(anchor, Journal));

    internal static string Resolve(string anchor, string source, bool diagnostics)
    {
        anchor = EnvironmentCopy.Root(anchor);
        source = EnvironmentCopy.Root(source);
        _ = ReadManifest(anchor, source, diagnostics);
        if (!File.Exists(EnvironmentCopy.Inside(anchor, Pointer))) return anchor;
        var generation = Read<ArenaGeneration>(anchor, Pointer);
        ValidateGeneration(generation, anchor, source, diagnostics);
        var root = GenerationRoot(anchor, generation.Id);
        if (Read<ArenaGeneration>(root, Owner) != generation)
            throw new InvalidDataException("The active generation ownership record differs.");
        _ = ReadManifest(root, source, diagnostics);
        return root;
    }

    internal static Manifest ReadManifest(string root, string source, bool diagnostics)
    {
        var value = Read<Manifest>(root, EnvironmentCopy.ManifestPath);
        if (value.Schema != 2 || value.Diagnostics != diagnostics || !Paths.Equals(value.Root, root) ||
            !Paths.Equals(value.Source, source) || value.Files is not { Count: > 0 } ||
            value.SourceBindings is not { Count: > 0 })
            throw new InvalidDataException("The Arena directory does not belong to this source/diagnostics mode.");
        EnvironmentCopy.Disjoint(EnvironmentCopy.Root(root), EnvironmentCopy.Root(source));
        var paths = new HashSet<string>(Paths);
        foreach (var file in value.Files)
        {
            _ = EnvironmentCopy.Inside(root, file.Path);
            if (!paths.Add(file.Path) || file.Length < 0 || !ValidHash(file.Sha256) ||
                file.Shared && !SharedAssets.IsContent(file.Path))
                throw new InvalidDataException("Invalid Arena file catalog.");
        }
        SharedAssets.ValidateCatalog(paths);
        var bindings = value.Files.Where(f => EnvironmentCopy.IsSourceBinding(f.Path))
            .Select(f => f with { Shared = false }).OrderBy(f => f.Path, StringComparer.Ordinal);
        if (!bindings.SequenceEqual(value.SourceBindings.OrderBy(f => f.Path, StringComparer.Ordinal)))
            throw new InvalidDataException("Invalid native source binding catalog.");
        return value;
    }

    internal static string? SourceChange(Manifest manifest)
    {
        foreach (var file in manifest.SourceBindings!)
        {
            var path = EnvironmentCopy.Inside(manifest.Source, file.Path);
            if (!File.Exists(path) || EnvironmentCopy.Hash(path) != file.Sha256)
                return "The SPT/game base changed. Rebuild the Arena environment before launching.";
        }
        return null;
    }

    public static Task Rebuild(string source, bool diagnostics = false, Action<string>? report = null) =>
        Execute(source, diagnostics, report, EnvironmentCopy.Prepare,
            (root, assets, messages) => RuntimeHost.RunCore(root, smoke: true, messages,
                directGame: true, retainedAssets: assets));

    // The two seams let offline checks use small real filesystem runtimes without
    // launching native processes. Production always uses Prepare + RunCore above.
    internal static async Task Execute(string source, bool diagnostics, Action<string>? report,
        Action<CopyPlan, Action<string>?> prepare,
        Func<string, AssetLease, Action<string>, Task> probe, Action<string>? checkpoint = null)
    {
        RuntimeHost.RequireIdle();
        source = EnvironmentCopy.Root(source);
        var anchor = ManagedEnvironment.Destination(source);
        using var control = Acquire(anchor);
        var messages = new RuntimeReporter(report ?? (_ => { }));
        var previous = Resolve(anchor, source, diagnostics);
        if (Pending(anchor))
        {
            var pending = ReadJournal(anchor, source, diagnostics);
            var pointerHash = OptionalHash(anchor, Pointer);
            if (pointerHash == Digest(JsonSerializer.SerializeToUtf8Bytes(pending.Next, EnvironmentCopy.Json)))
            {
                if (!pending.Validated || previous != GenerationRoot(anchor, pending.Next.Id) ||
                    EnvironmentCopy.Hash(EnvironmentCopy.Inside(previous, EnvironmentCopy.ManifestPath)) != pending.Next.InitialManifestHash)
                    throw new InvalidDataException("Cannot finalize an unverified rebuild activation.");
                EnvironmentCopy.ValidateProfileFiles(previous);
                ArchiveJournal(anchor, pending);
                messages.Write("Rebuild activation completed. The active internal profile was not rolled back.");
                return;
            }
            if (pointerHash != pending.PreviousPointerHash || !Paths.Equals(previous, pending.PreviousRoot) ||
                EnvironmentCopy.Hash(EnvironmentCopy.Inside(previous, EnvironmentCopy.ManifestPath)) != pending.PreviousManifestHash)
                throw new InvalidDataException("Rebuild state changed outside this operation. Nothing was replaced.");
            // A half-built generation is retained, not trusted/resumed or deleted.
            ArchiveJournal(anchor, pending);
            messages.Write("Interrupted rebuild retained for diagnosis. Preparing a fresh candidate; the previous account stays untouched.");
        }
        // Mod rollback is private and does not need the old source base to exist.
        // Recover it here too: otherwise a source update during a pending mod
        // transaction would leave both launch and rebuild permanently blocked.
        using var previousLease = previous == anchor ? null : new FileStream(
            EnvironmentCopy.Inside(previous, ".arena-start/running"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (previousLease != null) EnvironmentCopy.RequireSingleLink(previousLease.SafeFileHandle);
        ModUpdates.Recover(previous, messages.Write);
        _ = ReadManifest(previous, source, diagnostics);
        EnvironmentCopy.ValidateProfileFiles(previous);
        using var state = new PrivateState(previous, diagnostics);
        var id = Guid.NewGuid().ToString("N");
        var candidate = GenerationRoot(anchor, id);
        var plan = EnvironmentCopy.Plan(source, candidate, shareAssets: true, diagnostics);
        var next = new ArenaGeneration(1, anchor, source, id, diagnostics, new string('0', 64));
        var journal = new RebuildJournal(1, next, OptionalHash(anchor, Pointer), previous,
            EnvironmentCopy.Hash(EnvironmentCopy.Inside(previous, EnvironmentCopy.ManifestPath)), false);
        EnvironmentCopy.WriteNew(EnvironmentCopy.Inside(anchor, Journal), JsonSerializer.Serialize(journal, EnvironmentCopy.Json));
        checkpoint?.Invoke("journal");
        messages.Write("Rebuilding Arena: preparing a separate runtime. Previous private files are retained; shared assets are not a rollback snapshot.");
        prepare(plan, messages.Write);
        checkpoint?.Invoke("prepared");
        _ = ReadManifest(candidate, source, diagnostics);
        state.CopyTo(candidate);
        checkpoint?.Invoke("imported");
        var candidateManifest = ReadManifest(candidate, source, diagnostics);
        using var native = new NativeBindings(candidateManifest);
        using var immutable = new CandidateFiles(candidateManifest);
        var checkedManifestHash = EnvironmentCopy.Hash(EnvironmentCopy.Inside(candidate, EnvironmentCopy.ManifestPath));
        using var assets = new AssetLease();
        messages.Write("Rebuilding Arena: checking the candidate and private server login before activation.");
        await probe(candidate, assets, messages.Write);
        RuntimeHost.RequireIdle();
        EnvironmentCopy.ValidateProfileFiles(candidate);
        state.CheckIdentity(candidate);
        state.CheckUnchanged();
        if (EnvironmentCopy.Hash(EnvironmentCopy.Inside(previous, EnvironmentCopy.ManifestPath)) != journal.PreviousManifestHash ||
            OptionalHash(anchor, Pointer) != journal.PreviousPointerHash)
            throw new InvalidDataException("The previous Arena state changed during rebuilding.");
        checkpoint?.Invoke("probed");
        using var finalState = new PrivateState(candidate, diagnostics);
        next = next with { InitialManifestHash = checkedManifestHash };
        EnvironmentCopy.WriteNew(EnvironmentCopy.Inside(candidate, Owner), JsonSerializer.Serialize(next, EnvironmentCopy.Json));
        using var ownerLease = SharedAssets.OpenReadLease(EnvironmentCopy.Inside(candidate, Owner));
        EnvironmentCopy.RequireSingleLink(ownerLease.SafeFileHandle);
        if (SharedAssets.Hash(ownerLease) != Digest(JsonSerializer.SerializeToUtf8Bytes(next, EnvironmentCopy.Json)))
            throw new InvalidDataException("Candidate ownership record changed before activation.");
        journal = journal with { Next = next, Validated = true };
        ReplaceRecord(anchor, Journal, JsonSerializer.SerializeToUtf8Bytes(journal, EnvironmentCopy.Json));
        checkpoint?.Invoke("validated");
        // Recheck after test/native callbacks and before the single activation write.
        state.CheckUnchanged();
        EnvironmentCopy.ValidateProfileFiles(candidate);
        state.CheckIdentity(candidate);
        if (ReadJournal(anchor, source, diagnostics) != journal || OptionalHash(anchor, Pointer) != journal.PreviousPointerHash)
            throw new InvalidDataException("Rebuild activation authority changed.");
        // Native startup may create files. Recheck the complete resulting tree;
        // continuously retained asset leases avoid hashing 50+ GiB a third time.
        EnvironmentCopy.Verify(candidate, messages.Write, assetLease: assets);
        RuntimeHost.RequireIdle();
        ReplaceRecord(anchor, Pointer, JsonSerializer.SerializeToUtf8Bytes(next, EnvironmentCopy.Json));
        checkpoint?.Invoke("activated");
        ArchiveJournal(anchor, journal);
        messages.Write("Arena rebuild complete. New base activated; previous internal account/settings remain in the retained runtime.");
    }

    internal static void RequireNoPending(string anchor)
    {
        if (Pending(anchor)) throw new InvalidDataException("An interrupted base rebuild needs attention. Check the installation and choose Rebuild Arena environment.");
    }

    private static RebuildJournal ReadJournal(string anchor, string source, bool diagnostics)
    {
        var journal = Read<RebuildJournal>(anchor, Journal);
        if (journal.Next == null || string.IsNullOrEmpty(journal.PreviousRoot))
            throw new InvalidDataException("Incomplete rebuild journal.");
        ValidateGeneration(journal.Next, anchor, source, diagnostics);
        if (journal.Schema != 1 || !ValidHash(journal.PreviousManifestHash) ||
            journal.PreviousPointerHash != null && !ValidHash(journal.PreviousPointerHash) ||
            !(Paths.Equals(journal.PreviousRoot, anchor) ||
              Paths.Equals(Path.GetDirectoryName(journal.PreviousRoot), anchor + ".generations") &&
              Guid.TryParseExact(Path.GetFileName(journal.PreviousRoot), "N", out _)))
            throw new InvalidDataException("Invalid rebuild journal ownership.");
        return journal;
    }

    private static void ValidateGeneration(ArenaGeneration generation, string anchor, string source, bool diagnostics)
    {
        if (generation == null || generation.Schema != 1 || !Paths.Equals(generation.Anchor, anchor) || !Paths.Equals(generation.Source, source) ||
            generation.Diagnostics != diagnostics || !ValidHash(generation.InitialManifestHash))
            throw new InvalidDataException("Invalid Arena generation ownership.");
        _ = GenerationRoot(anchor, generation.Id);
    }

    private static void ArchiveJournal(string anchor, RebuildJournal journal)
    {
        if (Read<RebuildJournal>(anchor, Journal) != journal) throw new InvalidDataException("Rebuild journal changed.");
        var history = EnvironmentCopy.Inside(anchor, ".arena-start/rebuild-history/" + journal.Next.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!);
        File.Move(EnvironmentCopy.Inside(anchor, Journal), history);
    }

    internal static T Read<T>(string root, string relative) =>
        JsonSerializer.Deserialize<T>(EnvironmentCopy.ReadPrivate(EnvironmentCopy.Inside(root, relative)), EnvironmentCopy.Json)
        ?? throw new InvalidDataException("Empty Arena ownership record.");

    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string? OptionalHash(string root, string path) => File.Exists(EnvironmentCopy.Inside(root, path))
        ? EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, path)) : null;

    private static void ReplaceRecord(string root, string relative, byte[] bytes)
    {
        var target = EnvironmentCopy.Inside(root, relative);
        if (File.Exists(target)) EnvironmentCopy.InspectPrivate(target);
        var temporary = EnvironmentCopy.Inside(root, ".arena-start/record-" + Guid.NewGuid().ToString("N") + ".bin");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(true); }
        File.Move(temporary, target, overwrite: true);
    }

    private sealed class NativeBindings : IDisposable
    {
        private readonly List<FileStream> handles = [];
        internal NativeBindings(Manifest manifest)
        {
            try
            {
                foreach (var entry in manifest.SourceBindings!)
                {
                    var handle = SharedAssets.OpenReadLease(EnvironmentCopy.Inside(manifest.Source, entry.Path));
                    handles.Add(handle);
                    EnvironmentCopy.RequireSingleLink(handle.SafeFileHandle);
                    if (handle.Length != entry.Length || SharedAssets.Hash(handle) != entry.Sha256)
                        throw new InvalidDataException("Native source changed during rebuilding.");
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); }
    }

    private sealed class CandidateFiles : IDisposable
    {
        private readonly List<FileStream> handles = [];
        internal CandidateFiles(Manifest manifest)
        {
            try
            {
                foreach (var path in manifest.Files.Where(f => !f.Shared).Select(f => f.Path)
                    .Append(EnvironmentCopy.ManifestPath))
                {
                    var full = EnvironmentCopy.Inside(manifest.Root, path);
                    if (path == "EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll" && !File.Exists(full)) continue;
                    var handle = SharedAssets.OpenReadLease(full);
                    handles.Add(handle);
                    EnvironmentCopy.RequireSingleLink(handle.SafeFileHandle);
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); }
    }

    private sealed class PrivateState : IDisposable
    {
        private readonly string root;
        private readonly Dictionary<string, (FileStream Handle, byte[] Bytes)> files = new(StringComparer.Ordinal);
        private readonly string[] allowed;
        internal PrivateState(string root, bool diagnostics)
        {
            this.root = root;
            var paths = new List<string> { EnvironmentCopy.AccountPath,
                "BepInEx/config/com.maschine.ArenaMode.cfg",
                "SPT_Runtime/user/mods/ArenaMode/config/config.json",
                "SPT_Runtime/user/mods/ArenaMode/config/client-settings.json" };
            if (diagnostics) paths.Add("BepInEx/config/com.maschine.UiBridge.cfg");
            paths.AddRange(new[] { "Control.ini", "Game.ini", "Graphics.ini", "PostFx.ini", "Sound.ini" }
                .Select(n => NativeGameSettings.RelativeDirectory + "/" + n));
            paths.AddRange(EnvironmentCopy.Walk(root, "SPT_Runtime/user/profiles"));
            RecommendedModState.Validate(root);
            paths.AddRange(RecommendedModState.PreservedFiles(root));
            allowed = paths.ToArray();
            try
            {
                foreach (var path in allowed)
                {
                    var full = EnvironmentCopy.Inside(root, path);
                    if (Directory.Exists(full)) throw new InvalidDataException("Private setting is a directory: " + path);
                    if (!File.Exists(full)) continue;
                    var stream = SharedAssets.OpenReadLease(full);
                    try
                    {
                        EnvironmentCopy.RequireSingleLink(stream.SafeFileHandle);
                        if (stream.Length > 128L * 1024 * 1024) throw new InvalidDataException("Private state file exceeds rebuild limit.");
                        var bytes = new byte[checked((int)stream.Length)];
                        stream.ReadExactly(bytes);
                        files.Add(path, (stream, bytes));
                    }
                    catch { stream.Dispose(); throw; }
                }
            }
            catch { Dispose(); throw; }
        }
        internal void CopyTo(string candidate)
        {
            foreach (var (path, item) in files)
            {
                var target = EnvironmentCopy.Inside(candidate, path);
                if (File.Exists(target)) throw new InvalidDataException("Candidate already contains private state: " + path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(item.Bytes);
            }
        }
        internal void CheckIdentity(string candidate)
        {
            if (files.TryGetValue(EnvironmentCopy.AccountPath, out var receipt) &&
                EnvironmentCopy.Hash(EnvironmentCopy.Inside(candidate, EnvironmentCopy.AccountPath)) != Digest(receipt.Bytes))
                throw new InvalidDataException("Candidate changed the internal account identity.");
        }
        internal void CheckUnchanged()
        {
            EnvironmentCopy.ValidateProfileFiles(root);
            if (RecommendedModState.PreservedFiles(root).Any(path => !allowed.Contains(path, StringComparer.Ordinal)))
                throw new InvalidDataException("Previous private mod settings appeared during rebuild.");
            foreach (var path in allowed)
            {
                if (files.TryGetValue(path, out var item))
                {
                    if (EnvironmentCopy.Hash(EnvironmentCopy.Inside(root, path)) != Digest(item.Bytes))
                        throw new InvalidDataException("Previous private state changed during rebuild.");
                }
                else if (File.Exists(EnvironmentCopy.Inside(root, path)))
                    throw new InvalidDataException("Previous private state appeared during rebuild.");
            }
        }
        public void Dispose() { foreach (var item in files.Values) item.Handle.Dispose(); }
    }
}
