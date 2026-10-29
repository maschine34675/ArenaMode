using System.Text.Json;

namespace ArenaMode.Server.Rewards;

// This retires disposable session authority. It is never a restoration or payment proof.
internal sealed record RewardOfflineDiscard(int Schema, string SessionId, string LeaseHash,
    string ActivationHash, RewardConsent Before, RewardConsent After);
internal sealed record RewardOfflineDiscardCompletion(int Schema, string SessionId, string DiscardHash);
internal sealed class RewardOfflineDiscardedException() : Exception("offline-session-discarded");

internal static partial class RewardOfflineSession
{
    private static string DiscardPath(string root, RewardOfflineLease lease)
        => Path.Combine(SessionDirectory(root, lease.SessionId), "discarded.json");

    internal static RewardOfflineDiscard? ReadDiscard(string root, RewardOfflineLease lease)
    {
        string path = DiscardPath(root, lease);
        if (!RewardRuntimeFiles.Exists(root, path)) return null;
        var value = RewardStartupFiles.Read<RewardOfflineDiscard>(path, 8192);
        var activation = ReadActivation(root, lease);
        if (value.Schema != 1 || value.SessionId != lease.SessionId || value.LeaseHash != Hash(lease)
            || value.ActivationHash != RewardIdentity.Hash(RewardRuntimeFiles.Json(activation))
            || value.Before is null || value.After is null || value.Before.Schema != 1 || value.Before.Revision < 0
            || value.After.Schema != 1 || value.After.Enabled || value.Before.Revision == long.MaxValue
            || value.After.Revision != value.Before.Revision + 1
            || RewardRuntimeFiles.Exists(root, Path.Combine(SessionDirectory(root, lease.SessionId), "closed.json"))
            || RewardRuntimeFiles.Exists(root, Path.Combine(SessionDirectory(root, lease.SessionId), "completion.json")))
            throw new RewardDataException("offline-discard-invalid");
        return value;
    }

    internal static bool IsDiscarded(string root, RewardHandoff authority)
    {
        if (authority.OfflineSessionId is null) return false;
        if (!RewardRuntimeFiles.Exists(root, Path.Combine(SessionDirectory(root, authority.OfflineSessionId), "discarded.json")))
            return false;
        var lease = ReadLease(root, LeasePath(root, authority.OfflineSessionId));
        if (lease.Authority != authority || !RewardRuntimeFiles.SameRoot(root, lease.RuntimeRoot))
            throw new RewardDataException("offline-discard-authority-mismatch");
        return ReadDiscard(root, lease) is not null;
    }

    internal static bool DiscardCompleted(string root, RewardOfflineLease lease, RewardOfflineDiscard record)
    {
        string path = Path.Combine(SessionDirectory(root, lease.SessionId), "discard-completion.json");
        if (!RewardRuntimeFiles.Exists(root, path)) return false;
        var completion = RewardStartupFiles.Read<RewardOfflineDiscardCompletion>(path, 4096);
        if (completion != new RewardOfflineDiscardCompletion(1, lease.SessionId, RewardIdentity.Hash(RewardRuntimeFiles.Json(record))))
            throw new RewardDataException("offline-discard-completion-invalid");
        return true;
    }

    private static RewardOfflineActivation ReadActivation(string root, RewardOfflineLease lease)
    {
        var path = Path.Combine(SessionDirectory(root, lease.SessionId), "activation.json");
        RewardStartupFiles.RequireLocalPath(root, path);
        var activation = RewardStartupFiles.Read<RewardOfflineActivation>(path, 4096);
        if (activation.Schema != 1 || activation.SessionId != lease.SessionId || activation.LeaseHash != Hash(lease)
            || activation.PrivatePid <= 0 || activation.PrivateStartedUtc == default)
            throw new RewardDataException("offline-activation-invalid");
        return activation;
    }

    // This catch is confined to disposable marker/lease/consent reads, before
    // any checkpoint archive, immutable closure, import or payout has started.
    // Sharing, permissions, linked paths and ordinary authority failures still fail closed.
    private static bool LostPrivateState(Exception exception) => exception is
        FileNotFoundException or DirectoryNotFoundException or JsonException or FormatException
        or KeyNotFoundException or InvalidOperationException
        || exception is RewardDataException data && data.Message is not
            ("reward-root-is-linked" or "reward-path-is-linked" or "reward-path-outside-root");

    private static void DiscardStopped(RewardStartupWindow window, RewardOfflineLease lease,
        RewardOfflineReference reference, Action<string>? checkpoint, Action? discarded)
    {
        window.RequireOpen();
        var activation = ReadActivation(window.Root, lease);
        if (ProcessStillAlive(activation)) throw new RewardDataException("offline-private-process-still-running");
        var record = ReadDiscard(window.Root, lease);
        if (record is null)
        {
            var before = RewardEnrollment.ReadConsent(window.Root, lease.ProfileId);
            record = new(1, lease.SessionId, Hash(lease), RewardIdentity.Hash(RewardRuntimeFiles.Json(activation)),
                before, new(1, false, checked(before.Revision + 1)));
            RewardRuntimeFiles.WriteNew(window.Root, DiscardPath(window.Root, lease), record);
        }
        checkpoint?.Invoke("discard-record");
        _ = ReadDiscard(window.Root, lease) ?? throw new RewardDataException("offline-discard-missing");
        // The last private choice may have been OFF. Never revive the entry ON
        // from a missing or corrupt signed state. This touches metadata only.
        if (!DiscardCompleted(window.Root, lease, record))
        {
            RewardEnrollment.ApplyOfflineConsent(window.Root, lease.ProfileId, record.Before, record.After);
            checkpoint?.Invoke("discard-consent");
            RewardRuntimeFiles.WriteNew(window.Root, Path.Combine(SessionDirectory(window.Root, lease.SessionId), "discard-completion.json"),
                new RewardOfflineDiscardCompletion(1, lease.SessionId, RewardIdentity.Hash(RewardRuntimeFiles.Json(record))));
        }
        checkpoint?.Invoke("discard-completion");
        window.RequireOpen();
        if (ReadReference(window.Root, ActivePath(window.Root)) != reference)
            throw new RewardDataException("offline-owner-changed-during-recovery");
        File.Delete(ActivePath(window.Root));
        if (HasActive(window.Root)) throw new IOException("Offline discard retirement was not durable.");
        checkpoint?.Invoke("discard-retired");
        discarded?.Invoke();
    }
}
