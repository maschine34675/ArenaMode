using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

// These files are ordinary-server authority, not an incoming client/outbox format.
// A future trusted coordinator must opt in, enroll and capture a verified run and
// restoration. RewardApprovalProducer validates that evidence before approving;
// neither producer nor consumer infers authority from a disposable Arena copy.
internal sealed record RewardStartupEnrollment(int Schema, bool Enabled, string RuntimeRoot, RewardBinding Binding, bool RuntimeManaged = false);
internal enum RewardStartupApprovalState { Blocked, RestoredAndApproved }
internal sealed record RewardStartupApproval(int Schema, RewardStartupApprovalState State, RewardClaim Claim, string CleanProfileRevision);
internal sealed record RewardStartupAcknowledgement(int Schema, string ReceiptKey, string Fingerprint, string MessageId);

internal static class RewardStartupFiles
{
    internal const string RelativeDirectory = "user/arena-rewards";
    // Typed Cola progression can accompany the otherwise small approval.
    internal const int MaxApprovalBytes = 524288;
    internal static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static T Read<T>(string path, int maxBytes = 4_194_304)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes) throw new RewardDataException("reward-state-too-large");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        RejectDuplicateProperties(document.RootElement);
        return document.Deserialize<T>(RewardSettlementCore.JournalJson) ?? throw new RewardDataException("empty-reward-state");
    }

    internal static void RejectDuplicateProperties(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new RewardDataException("duplicate-reward-property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var value in node.EnumerateArray()) RejectDuplicateProperties(value);
    }

    // No directory creation, fallback path, symlink traversal or profile selection
    // supplied by the receipt. Paths derive from the installed root and bound ID.
    internal static void RequireLocalPath(string root, string path, bool allowMissingLeaf = false)
    {
        root = Canonical(root);
        path = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison))
            throw new RewardDataException("reward-path-outside-root");
        var current = new DirectoryInfo(root);
        var ancestors = new Stack<DirectoryInfo>();
        for (var ancestor = current; ancestor is not null; ancestor = ancestor.Parent)
            ancestors.Push(ancestor);
        // Check existing parents first: a missing child below a junction is
        // still a linked path, not permission to discard unavailable state.
        foreach (var ancestor in ancestors)
            if ((File.GetAttributes(ancestor.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new RewardDataException("reward-root-is-linked");
        var full = root;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "" or ".") continue;
            full = Path.Combine(full, parts[i]);
            try
            {
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new RewardDataException("reward-path-is-linked");
            }
            catch (FileNotFoundException) when (allowMissingLeaf && i == parts.Length - 1) { }
        }
    }

    internal static bool MarkerAbsent(string root)
    {
        // File.Exists alone swallows access errors and misses a directory marker.
        try { _ = File.GetAttributes(Path.Combine(root, "arena-session.json")); return false; }
        catch (FileNotFoundException) { return true; }
    }

    internal static void WriteAtomic(string root, string path, string json)
    {
        RequireLocalPath(root, path, allowMissingLeaf: true);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".arena-reward-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            RequireLocalPath(root, path, allowMissingLeaf: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // A leftover staged file has no profile .json suffix and is never an
            // acknowledgement. Cleanup failure must not hide an already-done replace.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static void RequireCleanProfile(string json, RewardBinding binding)
    {
        var profile = JsonNode.Parse(json) as JsonObject ?? throw new RewardDataException("invalid-profile");
        if (profile["info"] is not JsonObject info || info["id"]?.GetValue<string>() != binding.ProfileId
            || info["wipe"]?.GetValue<bool>() == true || info["invalidOrUnloadableProfile"]?.GetValue<bool>() == true
            || profile["characters"]?["pmc"]?["Info"] is not JsonObject
            || profile["characters"]?["scav"]?["Info"] is not JsonObject
            || profile["characters"]?["pmc"]?["Inventory"]?["items"] is not JsonArray
            || profile["inraid"] is not JsonObject inraid
            || !IdleRaidField(inraid["location"]) || !IdleRaidField(inraid["character"]))
            throw new RewardDataException("profile-not-confirmed-clean");
    }

    // SPT 4.1.6 CreateProfileService uses new Inraid(), serialized as {}.
    // Its load router only supplies "none"/"none" when the whole object was
    // absent. Both native idle forms are valid; a non-idle member still refuses
    // settlement. This shape check never replaces snapshot/run recovery proof.
    private static bool IdleRaidField(JsonNode? value) => value is null
        || value is JsonValue primitive && primitive.TryGetValue<string>(out var text) && text == "none";
}
