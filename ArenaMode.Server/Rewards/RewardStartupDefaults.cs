using System.Text.Json;

namespace ArenaMode.Server.Rewards;

/// <summary>Default ON is recorded once, inside the admitted ordinary startup window.</summary>
internal static class RewardStartupDefaults
{
    internal static bool PrepareDirectory(string root)
    {
        // Private copies never enroll themselves, even if their reward store is absent.
        if (!RewardStartupFiles.MarkerAbsent(root)) return false;
        RewardRuntimeFiles.EnsureDirectory(root, Path.Combine(root, RewardStartupFiles.RelativeDirectory));
        return true;
    }

    internal static IEnumerable<string> ProfileIds(RewardStartupWindow window)
    {
        window.RequireOpen();
        var directory = Path.Combine(window.Root, "user", "profiles");
        if (!RewardRuntimeFiles.Exists(window.Root, directory)) return [];
        RewardStartupFiles.RequireLocalPath(window.Root, directory);
        // Ignore backup/conflict files, subdirectories and non-native names.
        var ids = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).Where(id => RewardIdentity.IsHex(id, 24))
            .Select(id => id!).Take(65).ToArray();
        if (ids.Length > 64) throw new RewardDataException("too-many-reward-enrollments");
        return ids;
    }

    internal static void EnableIfNew(RewardStartupWindow window, string id)
    {
        window.RequireOpen();
        var root = window.Root;
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        // Any existing state belongs to the previous protocol. Never infer ON
        // from a lost OFF preference, empty directory or damaged enrollment.
        if (RewardRuntimeFiles.Exists(root, directory)) return;
        var profilePath = Path.Combine(root, "user", "profiles", id + ".json");
        RewardStartupFiles.RequireLocalPath(root, profilePath);
        var profile = File.ReadAllText(profilePath);
        using var parsed = JsonDocument.Parse(profile);
        RewardStartupFiles.RejectDuplicateProperties(parsed.RootElement);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object
            || parsed.RootElement.TryGetProperty(RewardProfileDocument.ExtensionKey, out _))
            throw new RewardDataException("default-reward-profile-has-history");
        // Only validate here; the existing enrollment plan owns profile writes.
        RewardStartupFiles.RequireCleanProfile(profile,
            new RewardBinding(Guid.NewGuid().ToString("N"), id, Guid.NewGuid().ToString("N")));
        window.RequireOpen();
        RewardRuntimeFiles.EnsureDirectory(root, directory);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "consent.json"), new RewardConsent(1, true));
    }
}
