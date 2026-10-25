namespace ArenaMode.Server.Rewards;

internal static partial class RewardOfflineSession
{
    // Caller owns original .startup.lock and .server.lock, before native profiles load.
    // This also handles a dead helper/private process; no timestamp can waive either lock.
    internal static void RecoverBeforeStartup(RewardStartupWindow window, Action<string>? checkpoint = null)
    {
        window.RequireOpen();
        string root = window.Root;
        try
        {
            RecoverMissingPointer(root);
            if (!HasActive(root)) return;
            var reference = ReadReference(root, ActivePath(root));
            var lease = ReadLease(root, LeasePath(root, reference.SessionId));
            if (Hash(lease) != reference.LeaseHash || !RewardRuntimeFiles.SameRoot(root, lease.RuntimeRoot))
                throw new RewardDataException("offline-recovery-lease-mismatch");
            string directory = SessionDirectory(root, lease.SessionId);
            string activationProof = Path.Combine(directory, "activation.json");
            bool hasActivation = RewardRuntimeFiles.Exists(root, activationProof);
            if (reference.Activated && !hasActivation) throw new RewardDataException("offline-activation-proof-missing");
            if (hasActivation)
            {
                var activation = RewardStartupFiles.Read<RewardOfflineActivation>(activationProof, 4096);
                if (activation.Schema != 1 || activation.SessionId != lease.SessionId || activation.LeaseHash != Hash(lease)
                    || activation.PrivatePid <= 0 || activation.PrivateStartedUtc == default)
                    throw new RewardDataException("offline-activation-invalid");
                // DI disposal can release a lifetime handle before the last native
                // shutdown callbacks exit. The helper's exit check is also required
                // for a manual normal startup recovering this record.
                if (ProcessStillAlive(activation)) throw new RewardDataException("offline-private-process-still-running");
            }
            string closurePath = Path.Combine(directory, "closed.json");
            RewardOfflineClosure closure;
            if (RewardRuntimeFiles.Exists(root, closurePath))
                closure = ReadClosure(root, lease);
            else
            {
                string activationPath = Path.Combine(directory, "activation.json");
                bool activated = RewardRuntimeFiles.Exists(root, activationPath);
                RewardOfflineState state;
                RewardOfflineReceipt[] receipts;
                if (activated)
                {
                    var activation = RewardStartupFiles.Read<RewardOfflineActivation>(activationPath, 4096);
                    if (activation.Schema != 1 || activation.SessionId != lease.SessionId || activation.LeaseHash != Hash(lease)
                        || activation.PrivatePid <= 0 || activation.PrivateStartedUtc == default)
                        throw new RewardDataException("offline-activation-invalid");
                    ValidateMarker(lease.PrivateRoot, lease);
                    if (Hash(ReadLease(lease.PrivateRoot, Path.Combine(lease.PrivateRoot, LeaseFile))) != Hash(lease))
                        throw new RewardDataException("offline-private-lease-changed");
                    state = ReadState(lease); // missing after activation must never restore an old ON choice
                    receipts = Published(lease).Select(path => ReceiptReference(lease, path)).OrderBy(r => r.RunId, StringComparer.Ordinal).ToArray();
                }
                else
                {
                    // Even if the index's final activation update failed, a signed
                    // state proves initialization began. Never discard a possible
                    // acknowledged OFF merely because its activation proof vanished.
                    if (Directory.Exists(lease.PrivateRoot)
                        && RewardRuntimeFiles.Exists(lease.PrivateRoot, Path.Combine(lease.PrivateRoot, StateFile)))
                        throw new RewardDataException("offline-initialization-proof-incomplete");
                    // No native private startup crossed the activation boundary. No rewards/choice
                    // may be inferred from a half-copied tree; original consent stays unchanged.
                    state = new(1, lease.SessionId, Hash(lease), lease.ConsentRevision, lease.Enabled);
                    receipts = [];
                }
                closure = new(1, lease.SessionId, Hash(lease), state, activated, receipts);
                ValidateClosure(lease, closure);
                RewardRuntimeFiles.WriteNew(root, closurePath, closure);
                checkpoint?.Invoke("closure");
            }

            window.RequireOpen();
            RewardEnrollment.ApplyOfflineConsent(root, lease.ProfileId,
                new(1, lease.Enabled, lease.ConsentRevision), new(1, closure.State.Enabled, closure.State.Revision));
            checkpoint?.Invoke("consent");

            string inbox = Path.Combine(RewardRuntimeFiles.DirectoryFor(root, lease.ProfileId), "inbox");
            if (closure.Receipts.Length != 0) RewardRuntimeFiles.EnsureDirectory(root, inbox);
            var remaining = closure.Activated ? Published(lease) : [];
            var allowed = closure.Receipts.ToDictionary(r => r.RunId, StringComparer.Ordinal);
            foreach (string source in remaining)
            {
                var receipt = ReceiptReference(lease, source);
                if (!allowed.TryGetValue(receipt.RunId, out var bound) || bound != receipt)
                    throw new RewardDataException("offline-result-changed-after-closure");
            }
            foreach (var receipt in closure.Receipts)
            {
                window.RequireOpen();
                string source = Path.Combine(lease.PrivateRoot, "user", "arena-rewards-outbox", receipt.RunId + ".json");
                string destination = Path.Combine(inbox, receipt.RunId + ".json");
                if (RewardRuntimeFiles.Exists(root, destination))
                {
                    if (ReceiptReference(lease, destination, root) != receipt)
                        throw new RewardDataException("offline-inbox-conflict");
                }
                else
                {
                    if (ReceiptReference(lease, source) != receipt) throw new RewardDataException("offline-result-missing");
                    var signed = RewardStartupFiles.Read<RewardSignedReceipt>(source, RewardRuntimeFiles.MaxReceiptBytes);
                    RewardRuntimeFiles.WriteNew(root, destination, signed);
                    if (ReceiptReference(lease, destination, root) != receipt) throw new RewardDataException("offline-import-readback-mismatch");
                }
                checkpoint?.Invoke("receipt");
            }
            // Only after the whole closure and every destination are durable may the
            // disposable outbox be drained. A later retry uses immutable inbox bytes.
            foreach (string source in remaining)
            {
                RewardStartupFiles.RequireLocalPath(lease.PrivateRoot, source);
                File.Delete(source);
            }
            checkpoint?.Invoke("imported");
            window.RequireOpen();
            RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "completion.json"),
                new RewardOfflineCompletion(1, lease.SessionId, RewardIdentity.Hash(RewardRuntimeFiles.Json(closure))));
            if (ReadReference(root, ActivePath(root)) != reference) throw new RewardDataException("offline-owner-changed-during-recovery");
            File.Delete(ActivePath(root));
            if (HasActive(root)) throw new IOException("Offline ownership retirement was not durable.");
        }
        catch (Exception exception) when (exception is not RewardOfflineException)
        { throw new RewardOfflineException("Offline Arena recovery is incomplete; normal startup stopped without replacing a profile.", exception); }
    }

    // Caller already holds both ordinary locks. An immutable lease was published
    // only after final save/readback; recovering its pointer cannot mint authority.
    private static void RecoverMissingPointer(string root)
    {
        string directory = Path.Combine(root, RewardStartupFiles.RelativeDirectory, "offline-sessions");
        if (!RewardRuntimeFiles.Exists(root, directory)) return;
        RewardStartupFiles.RequireLocalPath(root, directory);
        var entries = Directory.EnumerateFileSystemEntries(directory).Take(4097).ToArray();
        if (entries.Length > 4096) throw new RewardDataException("offline-history-capacity");
        var pending = new List<RewardOfflineLease>();
        foreach (string entry in entries)
        {
            RewardStartupFiles.RequireLocalPath(root, entry);
            if (!Directory.Exists(entry) || !RewardIdentity.IsGuid(Path.GetFileName(entry)))
                throw new RewardDataException("offline-unexpected-history-entry");
            string path = Path.Combine(entry, "lease.json");
            // The only legitimate unfinished directory creation precedes the
            // lease write; it has not granted any private authority.
            if (!RewardRuntimeFiles.Exists(root, path))
            {
                if (Directory.EnumerateFileSystemEntries(entry).Any(p => !System.Text.RegularExpressions.Regex.IsMatch(
                        Path.GetFileName(p), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z")))
                    throw new RewardDataException("offline-history-lease-missing");
                continue;
            }
            var lease = ReadLease(root, path);
            if (lease.SessionId != Path.GetFileName(entry) || !RewardRuntimeFiles.SameRoot(root, lease.RuntimeRoot))
                throw new RewardDataException("offline-history-binding-mismatch");
            string completionPath = Path.Combine(entry, "completion.json");
            bool hasClosure = RewardRuntimeFiles.Exists(root, Path.Combine(entry, "closed.json"));
            if (hasClosure)
            {
                var closure = ReadClosure(root, lease);
                if (RewardRuntimeFiles.Exists(root, completionPath))
                {
                    var completion = RewardStartupFiles.Read<RewardOfflineCompletion>(completionPath, 4096);
                    if (completion.Schema != 1 || completion.SessionId != lease.SessionId
                        || completion.ClosureHash != RewardIdentity.Hash(RewardRuntimeFiles.Json(closure)))
                        throw new RewardDataException("offline-completion-invalid");
                    continue;
                }
            }
            else if (RewardRuntimeFiles.Exists(root, completionPath)) throw new RewardDataException("offline-completion-without-closure");
            pending.Add(lease);
        }
        if (pending.Count > 1) throw new RewardDataException("offline-multiple-unclosed-sessions");
        if (HasActive(root))
        {
            var active = ReadReference(root, ActivePath(root));
            if (pending.Count == 1 && (active.SessionId != pending[0].SessionId || active.LeaseHash != Hash(pending[0])))
                throw new RewardDataException("offline-active-history-mismatch");
            return; // An existing closed plan with a not-yet-retired pointer is a valid retry.
        }
        if (pending.Count == 0) return;
        var recovered = pending[0];
        bool activated = RewardRuntimeFiles.Exists(root, Path.Combine(SessionDirectory(root, recovered.SessionId), "activation.json"));
        RewardRuntimeFiles.WriteNew(root, ActivePath(root), new RewardOfflineReference(1, recovered.SessionId, Hash(recovered), activated));
    }

    private static bool ProcessStillAlive(RewardOfflineActivation activation)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(activation.PrivatePid);
            return !process.HasExited && process.StartTime.ToUniversalTime() == activation.PrivateStartedUtc.UtcDateTime;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // Access failures intentionally propagate; unreadable is not proof of death.
    }

    private static string[] Published(RewardOfflineLease lease)
    {
        string directory = Path.Combine(lease.PrivateRoot, "user", "arena-rewards-outbox");
        if (!Directory.Exists(lease.PrivateRoot)) return [];
        if (!RewardRuntimeFiles.Exists(lease.PrivateRoot, directory)) return [];
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .Where(path => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z"))
            .Take(4097).ToArray();
        if (entries.Length > 4096) throw new RewardDataException("offline-receipt-capacity");
        foreach (var path in entries)
            if (!RewardIdentity.IsGuid(Path.GetFileNameWithoutExtension(path)) || Path.GetExtension(path) != ".json")
                throw new RewardDataException("offline-unexpected-receipt");
        return entries;
    }

    private static RewardOfflineReceipt ReceiptReference(RewardOfflineLease lease, string path, string? root = null)
    {
        RewardStartupFiles.RequireLocalPath(root ?? lease.PrivateRoot, path);
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(path, RewardRuntimeFiles.MaxReceiptBytes);
        var authority = lease.Authority ?? throw new RewardDataException("offline-unenrolled-receipt");
        var earned = RewardRuntimeFiles.Verify(authority, signed);
        string run = earned.Evidence.Authorization.RunId;
        if (Path.GetFileName(path) != run + ".json") throw new RewardDataException("offline-receipt-name-mismatch");
        return new(run, RewardIdentity.Hash(RewardRuntimeFiles.Json(signed)));
    }

    internal static RewardOfflineClosure ReadClosure(string root, RewardOfflineLease lease)
    {
        string path = Path.Combine(SessionDirectory(root, lease.SessionId), "closed.json");
        RewardStartupFiles.RequireLocalPath(root, path);
        var closure = RewardStartupFiles.Read<RewardOfflineClosure>(path, MaximumMetadata);
        ValidateClosure(lease, closure);
        return closure;
    }

    private static void ValidateClosure(RewardOfflineLease lease, RewardOfflineClosure closure)
    {
        if (closure.Schema != 1 || closure.SessionId != lease.SessionId || closure.LeaseHash != Hash(lease)
            || closure.State is null || closure.Receipts is null || closure.Receipts.Length > 4096)
            throw new RewardDataException("offline-closure-invalid");
        ValidateState(lease, closure.State);
        string? previous = null;
        foreach (var receipt in closure.Receipts)
        {
            if (receipt is null || !RewardIdentity.IsGuid(receipt.RunId) || !RewardIdentity.IsHex(receipt.Fingerprint, 64)
                || previous is not null && string.CompareOrdinal(previous, receipt.RunId) >= 0)
                throw new RewardDataException("offline-closure-receipt-invalid");
            previous = receipt.RunId;
        }
        if (!closure.Activated && (closure.Receipts.Length != 0 || closure.State.Revision != lease.ConsentRevision || closure.State.Enabled != lease.Enabled))
            throw new RewardDataException("offline-unstarted-closure-invalid");
        if (System.Text.Encoding.UTF8.GetByteCount(RewardRuntimeFiles.Json(closure)) > MaximumMetadata)
            throw new RewardDataException("offline-closure-too-large");
    }

    // Legacy delegation receipts remain unchanged. Offline delegations additionally
    // require a closed session whitelist, so a consumed key cannot add another run.
    internal static void RequireClosedReceipt(string root, string profileDirectory, RewardHandoff authority, RewardSignedReceipt signed, string runId)
    {
        string index = Path.Combine(profileDirectory, "offline-delegations", signed.DelegationId + ".json");
        if (authority.OfflineSessionId is null)
        {
            if (RewardRuntimeFiles.Exists(root, index)) throw new RewardDataException("offline-delegation-binding-missing");
            return;
        }
        var reference = ReadReference(root, index);
        var lease = ReadLease(root, LeasePath(root, reference.SessionId));
        if (reference.LeaseHash != Hash(lease) || lease.Authority?.DelegationId != signed.DelegationId
            || authority.OfflineSessionId != lease.SessionId)
            throw new RewardDataException("offline-receipt-authority-mismatch");
        var closure = ReadClosure(root, lease);
        var expected = closure.Receipts.SingleOrDefault(r => r.RunId == runId);
        if (expected is null || expected.Fingerprint != RewardIdentity.Hash(RewardRuntimeFiles.Json(signed)))
            throw new RewardDataException("offline-receipt-not-closed");
    }
}
