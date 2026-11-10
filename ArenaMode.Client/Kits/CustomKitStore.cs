using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ArenaMode.Kits;

/// <summary>One stored kit as read: the raw record, written back as it is unless it is edited.</summary>
internal sealed class CustomKitRecord
{
    internal JToken Raw;
    /// <summary>The record read as a kit; null when it cannot be (<see cref="Problem"/> says why).</summary>
    internal KitDefinition Kit;
    internal string Problem;
    internal DateTime SavedUtc;
}

/// <summary>What <see cref="CustomKitStore.Load"/> found.</summary>
internal sealed class CustomKitFile
{
    internal readonly List<CustomKitRecord> Records = new();
    /// <summary>One line for the kit window when something about the file needs saying; null otherwise.</summary>
    internal string Banner;
    /// <summary>True when nothing may be written: a newer schema, too many kits, or a file that could not be set aside.</summary>
    internal bool ReadOnly;
}

/// <summary>
/// The file of the player's own kits (docs/reviews/CUSTOM-KITS-20261110.md), plain I/O so it is
/// pinned offline. One JSON file, <c>{"version":1,"kits":[...]}</c>, under BepInEx/config, which is
/// the same folder in a normal and an Arena session and is neither the profile nor the private server
/// tree. Each kit record keeps the field names of a kit (id, name, items, fill) plus its saved time;
/// a price or an order in a record is never trusted (the catalogue computes both).
///
/// The failures the fork's file had are closed here. Every change is a read-modify-write of a FRESH
/// read under one lock, written to a temp file in the same folder and swapped in with a backup, after
/// checking that nobody wrote the file since the read. A record that cannot be read is kept verbatim,
/// so saving another kit never drops it. A file whose content cannot be read is set aside (the newest
/// three are kept), the last good copy is restored, and the window says so; a file that cannot be
/// opened (held by another program) loads read-only and is left alone; a missing file with a backup
/// gets the backup back; a newer schema or more kits than the limit load read-only, so nothing a newer
/// build or a hand edit wrote is destroyed. The lock is per process: two game instances on one
/// install are caught by the stamp check, not serialised.
/// </summary>
internal static class CustomKitStore
{
    internal const int SchemaVersion = 1;
    internal const long MaxFileBytes = 1024 * 1024;
    internal const int MaxKits = 64;
    internal const int KeptQuarantines = 3;

    private static readonly object Gate = new object();
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    internal static string BackupPath(string path) => path + ".bak";

    internal static string TempPath(string path) => path + ".tmp";

    internal static CustomKitFile Load(string path)
    {
        lock (Gate) return LoadLocked(path);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the stored records of a fresh read and writes the result.
    /// The change returns null to go ahead or a reason to refuse. Returns null when the file was
    /// written, otherwise the reason it was not; the old file is untouched then.
    /// </summary>
    internal static string Mutate(string path, Func<List<JToken>, string> change)
    {
        if (string.IsNullOrEmpty(path)) return "no kit file";
        if (change == null) throw new ArgumentNullException(nameof(change));
        lock (Gate)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // The stamp BEFORE the read: taken after it, a write by another program in between
                // would already be in the stamp and the check below would pass over it. A load that
                // restored the file itself changes the stamp too; the second attempt then goes ahead.
                Stamp before = Stamp.Of(path);
                CustomKitFile file = LoadLocked(path);
                if (file.ReadOnly) return file.Banner ?? "the kit file is read-only";
                var records = file.Records.Select(record => record.Raw.DeepClone()).ToList();
                string refused;
                // A kept record the change cannot handle refuses the change; it never escapes into the window.
                try { refused = change(records); }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException || ex is FormatException || ex is JsonException)
                {
                    return "a stored kit record could not be handled: " + ex.Message;
                }

                if (refused != null) return refused;
                if (records.Count > MaxKits) return "at most " + MaxKits + " kits";

                byte[] bytes = Utf8.GetBytes(new JObject { ["version"] = SchemaVersion, ["kits"] = new JArray(records) }.ToString(Formatting.Indented));
                if (bytes.Length > MaxFileBytes) return "the kit file would be larger than 1 MiB";

                string temp = TempPath(path);
                try
                {
                    string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                    if (File.Exists(temp)) File.Delete(temp);
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    if (!Stamp.Of(path).Equals(before))
                    {
                        File.Delete(temp);
                        if (attempt == 0) continue;
                        return "the kit file was changed by another program; try again";
                    }

                    Commit(temp, path);
                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    TryDelete(temp);
                    return "the kit file could not be written: " + ex.Message;
                }
            }

            return "the kit file was changed by another program; try again";
        }
    }

    /// <summary>The record for <paramref name="kit"/>: the existing raw record with its kit fields replaced, so fields a newer build added survive.</summary>
    internal static JObject ToRecord(KitDefinition kit, DateTime savedUtc, JToken existing = null)
    {
        if (kit == null) throw new ArgumentNullException(nameof(kit));
        var record = existing is JObject old ? (JObject)old.DeepClone() : new JObject();
        record.Remove("price");
        record.Remove("order");
        record["id"] = kit.Id;
        record["name"] = kit.Name ?? string.Empty;
        record["saved"] = savedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        record["items"] = JArray.FromObject(kit.Items ?? new List<KitItemNode>());
        record["fill"] = JObject.FromObject(kit.Fill ?? new KitFill());
        return record;
    }

    /// <summary>
    /// The index of the record with this id, or -1. A kept record whose id is not text (an object,
    /// an array, a number) is skipped, never cast: a cast that throws would fail every edit and
    /// delete in the file, the bad record's own deletion included.
    /// </summary>
    internal static int IndexOf(List<JToken> records, string id)
    {
        for (int index = 0; index < records.Count; index++)
            if (records[index] is JObject record && record["id"] is JValue value && value.Type == JTokenType.String
                && string.Equals((string)value, id, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }

    private const int OpenAttempts = 3;
    private const int OpenRetryMilliseconds = 40;

    private enum ReadOutcome { Read, Unreadable, Inaccessible }

    private static CustomKitFile LoadLocked(string path)
    {
        var file = new CustomKitFile();
        if (string.IsNullOrEmpty(path)) return file;
        if (!File.Exists(path)) return RestoreMissing(path) ?? file;

        // A file another program holds open (the other game session swapping it in, a sync tool, a
        // virus scan) is not a corrupt file: it is tried again briefly, then shown read-only, and
        // never set aside or replaced by the older backup.
        ReadOutcome outcome = ReadOutcome.Inaccessible;
        string problem = null;
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            file = new CustomKitFile();
            outcome = Read(path, file, out problem);
            if (outcome != ReadOutcome.Inaccessible) break;
            if (attempt + 1 < OpenAttempts) Thread.Sleep(OpenRetryMilliseconds);
        }

        if (outcome == ReadOutcome.Read) return file;
        if (outcome == ReadOutcome.Inaccessible)
        {
            if (!File.Exists(path)) return RestoreMissing(path) ?? new CustomKitFile();
            return new CustomKitFile
            {
                ReadOnly = true,
                Banner = "Your kit file could not be opened (" + problem + "); nothing is saved until it can be.",
            };
        }

        // Unreadable as a whole: set it aside, never write over it.
        string kept = Quarantine(path);
        if (kept == null)
        {
            var stuck = new CustomKitFile { ReadOnly = true };
            stuck.Banner = "Your kit file could not be read (" + problem + ") nor set aside; nothing is saved until it is fixed.";
            return stuck;
        }

        string backup = BackupPath(path);
        var restored = new CustomKitFile();
        if (File.Exists(backup) && Read(backup, restored, out _) == ReadOutcome.Read && !restored.ReadOnly)
        {
            try
            {
                File.Copy(backup, path, false);
                restored.Banner = "Your kit file could not be read (" + problem + "); it was kept as " + Path.GetFileName(kept) + " and the last good copy restored.";
                return restored;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Fall through to the empty list; the backup is still there.
            }
        }

        var empty = new CustomKitFile();
        empty.Banner = "Your kit file could not be read (" + problem + "); it was kept as " + Path.GetFileName(kept) + ".";
        return empty;
    }

    /// <summary>
    /// The main file is missing but its backup is there: a save whose last step failed (the fallback
    /// below deletes the file before it moves the new one in) or a sync tool. The backup is restored,
    /// with a banner; starting empty instead would let the next save write over the backup with a
    /// file of one kit. Null when there is no usable backup.
    /// </summary>
    private static CustomKitFile RestoreMissing(string path)
    {
        string backup = BackupPath(path);
        if (!File.Exists(backup)) return null;
        var restored = new CustomKitFile();
        if (Read(backup, restored, out _) != ReadOutcome.Read) return null;
        if (restored.ReadOnly) return restored;
        try
        {
            File.Copy(backup, path, false);
            restored.Banner = "Your kit file was missing; the last good copy was restored.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            restored.ReadOnly = true;
            restored.Banner = "Your kit file is missing and its last good copy could not be restored (" + ex.Message + "); nothing is saved until it is.";
        }

        return restored;
    }

    /// <summary>
    /// Reads <paramref name="path"/> into <paramref name="file"/>. Unreadable means the content
    /// (not JSON, no schema, too large); inaccessible means the file could not be opened at all.
    /// </summary>
    private static ReadOutcome Read(string path, CustomKitFile file, out string problem)
    {
        problem = null;
        JObject root;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) { problem = "larger than 1 MiB"; return ReadOutcome.Unreadable; }
            // Dates stay text: a record nobody edits is written back as it was read.
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path, Utf8))) { DateParseHandling = DateParseHandling.None })
            {
                root = JObject.Load(reader);
                if (reader.Read() && reader.TokenType != JsonToken.Comment) { problem = "not valid JSON"; return ReadOutcome.Unreadable; }
            }
        }
        catch (JsonException) { problem = "not valid JSON"; return ReadOutcome.Unreadable; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { problem = ex.Message; return ReadOutcome.Inaccessible; }

        problem = Content(root, file);
        return problem == null ? ReadOutcome.Read : ReadOutcome.Unreadable;
    }

    private static string Content(JObject root, CustomKitFile file)
    {
        // Never cast the version: an integer past the range of long is a BigInteger to Newtonsoft,
        // and the cast throws out of every load. Such a number is a far-future schema (read-only),
        // or below 1 when negative.
        if (!(root["version"] is JValue number) || number.Type != JTokenType.Integer) return "no schema version";
        long version;
        try { version = Convert.ToInt64(number.Value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException)
        {
            version = Convert.ToString(number.Value, CultureInfo.InvariantCulture).StartsWith("-", StringComparison.Ordinal) ? long.MinValue : long.MaxValue;
        }

        if (version < 1) return "no schema version";
        if (!(root["kits"] is JArray kits)) return "no kit list";

        if (version > SchemaVersion)
        {
            file.ReadOnly = true;
            file.Banner = "Your kit file was written by a newer ArenaMode; it is shown but not changed.";
        }
        else if (kits.Count > MaxKits)
        {
            file.ReadOnly = true;
            file.Banner = "Your kit file holds more than " + MaxKits + " kits; it is shown but not changed.";
        }

        foreach (JToken token in kits)
            file.Records.Add(ReadRecord(token));
        return null;
    }

    private static CustomKitRecord ReadRecord(JToken token)
    {
        var record = new CustomKitRecord { Raw = token ?? JValue.CreateNull() };
        if (!(token is JObject json))
        {
            record.Problem = "not a kit record";
            return record;
        }

        try
        {
            var kit = json.ToObject<KitDefinition>();
            if (kit == null || string.IsNullOrWhiteSpace(kit.Id))
            {
                record.Problem = "a kit record without an id";
                return record;
            }

            kit.Price = 0;
            kit.Order = 0;
            kit.Items ??= new List<KitItemNode>();
            kit.Fill ??= new KitFill();
            record.Kit = kit;
            if (DateTime.TryParse((string)json["saved"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                record.SavedUtc = parsed;
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidCastException || ex is FormatException || ex is ArgumentException)
        {
            record.Kit = null;
            record.Problem = "an unreadable kit record";
        }

        return record;
    }

    /// <summary>Moves the unreadable file aside under a time-stamped name and trims older ones; null when it could not.</summary>
    private static string Quarantine(string path)
    {
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(path);
            string target = Path.Combine(folder, stem + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json");
            if (File.Exists(target)) return null;
            File.Move(path, target);
            var olders = Directory.GetFiles(folder, stem + ".corrupt-*.json").OrderByDescending(name => name, StringComparer.Ordinal).Skip(KeptQuarantines);
            foreach (string older in olders) TryDelete(older);
            return target;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Commit(string temp, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try
        {
            File.Replace(temp, path, BackupPath(path), true);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException || ex is NotSupportedException)
        {
            // A runtime without ReplaceFile: the same three steps, not atomic, in an order that
            // always leaves either the old file or its backup.
            File.Copy(path, BackupPath(path), true);
            File.Delete(path);
            File.Move(temp, path);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    private readonly struct Stamp : IEquatable<Stamp>
    {
        private readonly bool _exists;
        private readonly long _length;
        private readonly long _written;

        private Stamp(bool exists, long length, long written)
        {
            _exists = exists;
            _length = length;
            _written = written;
        }

        internal static Stamp Of(string path)
        {
            var info = new FileInfo(path);
            info.Refresh();
            return info.Exists ? new Stamp(true, info.Length, info.LastWriteTimeUtc.Ticks) : new Stamp(false, 0, 0);
        }

        public bool Equals(Stamp other) => _exists == other._exists && _length == other._length && _written == other._written;

        public override bool Equals(object obj) => obj is Stamp other && Equals(other);

        public override int GetHashCode() => (_exists ? 1 : 0) ^ _length.GetHashCode() ^ _written.GetHashCode();
    }
}
