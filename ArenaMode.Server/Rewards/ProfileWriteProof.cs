using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace ArenaMode.Server.Rewards;

internal enum ProfileProofMode { Strict, Pipeline }

// Pipeline vocabulary: none | transformed | opaque | unsafe | unavailable
//   none        = hook ready, latest sealed record has input == output
//   transformed = sealed, input != output, subtractive projection and protected subtrees hold
//   opaque      = hook ready, no usable sealed record (absent, direct byte[] writer, unpaired Begin, over a limit)
//   unsafe      = sealed and transformed, but the projection or the protected subtrees failed
//   unavailable = hook not ready
// OutputDiskMatch: true | false | unknown | n/a
internal sealed record ProfileProof(
    ProfileProofMode Mode,
    string Comparable,        // text RAM is compared with: disk (Strict) or Input (Pipeline)
    string Pipeline,
    string OutputDiskMatch,
    string? Input = null,     // only when Pipeline == "transformed" (kept on a strict result for diagnostics samples)
    string? Output = null)
{
    // A strict proof of a transformed write may still carry input and output, for the diagnostics samples only.
    internal static ProfileProof Strict(string disk, string pipeline, string outputDiskMatch = "n/a",
        string? input = null, string? output = null)
        => new(ProfileProofMode.Strict, disk, pipeline, outputDiskMatch, input, output);
}

// What SPT's own write path did to a profile between "this is the text to save" and "these are the bytes on disk".
// Another mod (Pack a Punch strips its own data) may transform that text in a prefix. The readback then cannot compare
// RAM with disk directly. The capture records the text at the entry of the string overload (Priority.First) and the
// bytes at the byte[] overload (Priority.Last), and the proof accepts RAM == input only together with
// output == disk, a subtractive projection and unchanged protected subtrees. See
// docs/reviews/PROFILE-WRITE-PIPELINE-20261120.md.
//
// No Harmony dependency here: the patches live in Patches/ArenaProfileWriteCapturePatches.cs.
internal static class ProfileWriteProof
{
    internal const int MaxProfileBytes = 64 << 20;
    private const int MaxIds = 1024;
    private const int DefaultVisitBudget = 20_000_000;
    private const long DefaultMaxRetainedBytes = 192L << 20;

    private static volatile bool _ready;
    private static readonly ConcurrentDictionary<string, Capture> Records = new(StringComparer.Ordinal);
    private static readonly object BudgetLock = new();
    private static long _retained;
    private static long _sequence;
    private static int _loggedUnpaired;
    private static int _loggedTransform;

    internal static bool Ready => _ready;
    // Evaluated per call, never cached: the working directory is part of the answer.
    internal static Func<string> ProfilesDirectory = DefaultProfilesDirectory;
    internal static long MaxRetainedBytes = DefaultMaxRetainedBytes;
    // Tests: the node-visit budget of the subtractive check.
    internal static int VisitBudget = DefaultVisitBudget;

    [ThreadStatic] private static int t_depth;
    [ThreadStatic] private static string? t_id;
    [ThreadStatic] private static string? t_input;
    [ThreadStatic] private static bool t_poisoned;

    internal static void MarkReady(bool ready) => _ready = ready;

    internal static string DefaultProfilesDirectory()
        => Path.Combine(RewardStartupFiles.Canonical(Directory.GetCurrentDirectory()), "user", "profiles");

    // The string overload, Priority.First prefix. Never throws into SPT's write path.
    internal static void Begin(string path, string input)
    {
        try
        {
            t_depth++;
            if (!IsProfilePath(path, out string id)) return;
            if (t_id is not null) { t_poisoned = true; return; }
            t_id = id;
            t_input = input;
        }
        catch { }
    }

    // The byte[] overload, Priority.Last prefix: the bytes that will be written, after every other prefix.
    internal static void Seal(string path, byte[] bytes)
    {
        string? id = null;
        try
        {
            if (!IsProfilePath(path, out string profile)) return;
            id = profile;
            string? input = null;
            bool paired = t_id == profile && !t_poisoned && t_depth >= 1;
            if (t_id == profile)
            {
                input = t_input;
                t_id = null;
                t_input = null;
            }
            if (!paired || input is null || bytes is null) { InstallOpaque(profile); return; }
            Install(profile, input, bytes);
        }
        catch
        {
            if (id is not null) { try { InstallOpaque(id); } catch { } }
        }
    }

    // A Harmony finalizer on the string overload: it also runs when an earlier prefix skipped the original or threw.
    internal static void End()
    {
        try
        {
            if (t_id is not null)
            {
                // Begin without Seal: the original did not reach the byte[] write (a prefix skipped it, or the JIT
                // inlined it past the hook). Whatever is on disk is not what we captured.
                InstallOpaque(t_id);
                t_id = null;
                t_input = null;
                if (Interlocked.Exchange(ref _loggedUnpaired, 1) == 0)
                    ArenaContext.Logger?.Debug("[ArenaMode] A profile write was captured without its output; the save comparison stays strict.");
            }
            if (t_depth > 0) t_depth--;
            if (t_depth <= 0) ResetSlot();
        }
        catch { }
    }

    internal static ProfileProof Resolve(string profileId, string diskText)
    {
        try
        {
            if (!_ready) return ProfileProof.Strict(diskText, "unavailable");
            if (!Records.TryGetValue(profileId.ToLowerInvariant(), out var record) || record.Opaque)
                return ProfileProof.Strict(diskText, "opaque");
            if (!record.Transformed) return ProfileProof.Strict(diskText, "none");
            string input = record.Input!;
            string output = record.OutputText();
            if (!record.IsSafe(profileId, input, output)) return ProfileProof.Strict(diskText, "unsafe");
            bool outputMatchesDisk;
            try { outputMatchesDisk = RewardRuntimeFiles.ExactSameProfile(output, diskText); }
            catch { return ProfileProof.Strict(diskText, "transformed", "unknown", input, output); }
            if (!outputMatchesDisk) return ProfileProof.Strict(diskText, "transformed", "false", input, output);
            return new ProfileProof(ProfileProofMode.Pipeline, input, "transformed", "true", input, output);
        }
        catch { return ProfileProof.Strict(diskText, "opaque"); }
    }

    // RAM equals the disk text (today's rule), or RAM equals the captured input and the captured output
    // equals the disk (Resolve checked that on this very disk text). Both use the unpatched comparison.
    internal static bool Confirms(string ram, string disk, ProfileProof proof)
        => RewardRuntimeFiles.ExactSameProfile(ram, disk)
            || proof.Mode == ProfileProofMode.Pipeline && RewardRuntimeFiles.ExactSameProfile(ram, proof.Comparable);

    internal static void ResetForTests()
    {
        _ready = false;
        Records.Clear();
        lock (BudgetLock) _retained = 0;
        _sequence = 0;
        _loggedUnpaired = 0;
        _loggedTransform = 0;
        ProfilesDirectory = DefaultProfilesDirectory;
        MaxRetainedBytes = DefaultMaxRetainedBytes;
        VisitBudget = DefaultVisitBudget;
        ResetSlot();
    }

    internal static long RetainedBytesForTests => Volatile.Read(ref _retained);
    internal static bool HasTransformedRecordForTests(string id)
        => Records.TryGetValue(id.ToLowerInvariant(), out var record) && record.Transformed && !record.Opaque;

    private static void ResetSlot()
    {
        t_depth = 0;
        t_id = null;
        t_input = null;
        t_poisoned = false;
    }

    // ---- path match

    internal static bool IsProfilePath(string? path, out string id)
    {
        id = "";
        if (string.IsNullOrEmpty(path)) return false;
        // Cheap reject first: nearly every write is not a profile.
        ReadOnlySpan<char> name = Path.GetFileName(path.AsSpan());
        if (name.Length != 29 || !name[24..].Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = 0; i < 24; i++)
            if (!char.IsAsciiHexDigit(name[i])) return false;
        // SaveServer passes a relative path; only the full path says which directory it is.
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (directory is null) return false;
        string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProfilesDirectory()));
        if (!string.Equals(Path.TrimEndingDirectorySeparator(directory), expected, RewardStartupFiles.PathComparison))
            return false;
        id = name[..24].ToString().ToLowerInvariant();
        return true;
    }

    // ---- records

    private static void InstallOpaque(string id)
        => Put(id, new Capture { Sequence = Interlocked.Increment(ref _sequence), Opaque = true });

    private static void Install(string id, string input, byte[] bytes)
    {
        if (input.Length > MaxProfileBytes || bytes.Length > MaxProfileBytes) { InstallOpaque(id); return; }
        long sequence = Interlocked.Increment(ref _sequence);
        if (Utf8Equals(input, bytes))
        {
            // Nothing retained: with no transforming mod this is every write.
            Put(id, new Capture { Sequence = sequence });
            return;
        }
        // Existing references, no copies. The cost also covers the decoded output text that Resolve caches on the
        // record (at most one UTF-16 char, 2 bytes, per output byte), so MaxRetainedBytes bounds what is really held.
        Put(id, new Capture
        {
            Sequence = sequence,
            Transformed = true,
            Input = input,
            OutputBytes = bytes,
            Cost = 2L * input.Length + 3L * bytes.Length,
        });
        if (Interlocked.Exchange(ref _loggedTransform, 1) == 0)
            ArenaContext.Logger?.Debug($"[ArenaMode] A profile write is transformed before it reaches the disk by another mod (profile {Alias(id)}); "
                + "the save comparison follows the captured write.");
    }

    private static void Put(string id, Capture next)
    {
        bool needsLock = next.Cost > 0 || Records.TryGetValue(id, out var previous) && previous.Cost > 0;
        if (!needsLock)
        {
            if (Records.ContainsKey(id) || Records.Count < MaxIds) Records[id] = next;
            return;
        }
        lock (BudgetLock)
        {
            long old = Records.TryGetValue(id, out var current) ? current.Cost : 0;
            long without = _retained - old;
            if (next.Cost > 0 && without + next.Cost > MaxRetainedBytes)
                next = new Capture { Sequence = next.Sequence, Opaque = true };
            if (current is null && Records.Count >= MaxIds) { _retained = without; return; }
            Records[id] = next;
            _retained = without + next.Cost;
        }
    }

    // Zero allocation beyond a pooled buffer: the UTF-8 form of input, compared with bytes chunk by chunk.
    internal static bool Utf8Equals(string input, byte[] bytes)
    {
        if (Encoding.UTF8.GetByteCount(input) != bytes.Length) return false;
        const int chunk = 8192;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunk * 3 + 8);
        try
        {
            int position = 0, offset = 0;
            while (position < input.Length)
            {
                int window = Math.Min(chunk, input.Length - position);
                bool last = position + window == input.Length;
                var status = Utf8.FromUtf16(input.AsSpan(position, window), buffer, out int read, out int written,
                    replaceInvalidSequences: true, isFinalBlock: last);
                if (status is not (System.Buffers.OperationStatus.Done or System.Buffers.OperationStatus.NeedMoreData)) return false;
                if (read <= 0 && !last) return false;
                if (offset + written > bytes.Length) return false;
                if (!buffer.AsSpan(0, written).SequenceEqual(bytes.AsSpan(offset, written))) return false;
                offset += written;
                position += read;
                if (read <= 0) break;
            }
            return offset == bytes.Length;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static string Alias(string id) => RewardIdentity.Hash(id)[..8];

    private sealed class Capture
    {
        internal long Sequence;
        internal bool Opaque;
        internal bool Transformed;
        internal string? Input;
        internal byte[]? OutputBytes;
        internal long Cost;
        private string? _output;
        private int _safe; // 0 unknown, 1 safe, 2 unsafe: a pure function of the immutable input and output

        internal string OutputText() => _output ??= new UTF8Encoding(false, false).GetString(OutputBytes!);

        internal bool IsSafe(string id, string input, string output)
        {
            int value = Volatile.Read(ref _safe);
            if (value == 0)
            {
                value = Subtractive(id, input, output) ? 1 : 2;
                Volatile.Write(ref _safe, value);
            }
            return value == 1;
        }
    }

    private static bool Subtractive(string id, string input, string output)
    {
        try
        {
            var before = ParseStrict(input);
            var after = ParseStrict(output);
            if (!ProtectedUnchanged(before, after)) return false;
            if (!IsSubtractive(before, after)) return false;
            LogSections(id, before, after);
            return true;
        }
        catch { return false; } // duplicate property, parse error or budget: fail closed
    }

    private static void LogSections(string id, JsonNode? before, JsonNode? after)
    {
        try
        {
            if (ArenaContext.Logger is null || before is not JsonObject a || after is not JsonObject b) return;
            int changed = 0;
            foreach (var pair in a)
                if (!b.TryGetPropertyValue(pair.Key, out var other) || !JsonNode.DeepEquals(pair.Value, other)) changed++;
            // Counts only: a mod's section names would identify it.
            ArenaContext.Logger.Debug($"[ArenaMode] Captured profile write (profile {Alias(id)}) differs from its input in {changed} top-level sections.");
        }
        catch { }
    }

    private static JsonNode? ParseStrict(string json)
    {
        using var document = JsonDocument.Parse(json);
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        return JsonNode.Parse(json);
    }

    // H1: the output is a subtractive projection of the input.
    internal static bool IsSubtractive(string input, string output) => IsSubtractive(ParseStrict(input), ParseStrict(output));

    internal static bool IsSubtractive(JsonNode? input, JsonNode? output)
    {
        int budget = VisitBudget;
        return Subtractive(input, output, ref budget);
    }

    private static bool Subtractive(JsonNode? input, JsonNode? output, ref int budget)
    {
        if (--budget < 0) throw new InvalidOperationException("profile projection budget exceeded");
        if (JsonNode.DeepEquals(input, output)) return true;
        if (input is JsonObject inObject && output is JsonObject outObject)
        {
            foreach (var pair in outObject)
            {
                if (!inObject.TryGetPropertyValue(pair.Key, out var source)) return false;
                if (!Subtractive(source, pair.Value, ref budget)) return false;
            }
            return true;
        }
        if (input is JsonArray inArray && output is JsonArray outArray)
        {
            if (outArray.Count > inArray.Count) return false;
            // Greedy earliest match is optimal for any pairwise predicate.
            int next = 0;
            for (int i = 0; i < inArray.Count && next < outArray.Count; i++)
                if (Subtractive(inArray[i], outArray[next], ref budget)) next++;
            return next == outArray.Count;
        }
        return false; // a changed scalar, a null-out or a kind change
    }

    // H2: what the pipeline must never touch. Provenance is not integrity.
    private static readonly string[][] Protected =
    [
        [RewardProfileDocument.ExtensionKey],
        ["info"],
        ["characters", "pmc", "Info"],
        ["characters", "scav", "Info"],
    ];

    internal static bool ProtectedUnchanged(JsonNode? input, JsonNode? output)
    {
        foreach (string[] path in Protected)
        {
            bool had = TryPath(input, path, out var a);
            bool has = TryPath(output, path, out var b);
            if (had != has || had && !JsonNode.DeepEquals(a, b)) return false;
        }
        return true;
    }

    private static bool TryPath(JsonNode? node, string[] path, out JsonNode? value)
    {
        foreach (string part in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
            {
                value = null;
                return false;
            }
        }
        value = node;
        return true;
    }
}
