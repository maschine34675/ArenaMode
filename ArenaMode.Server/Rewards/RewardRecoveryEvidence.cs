using System.Text.Json;

namespace ArenaMode.Server.Rewards;

// Evidence is bounded and profile-specific. A waiver never repairs a journal,
// invents restoration proof, or suppresses an unclassified corrupt document.
internal static class RewardRecoveryEvidence
{
    internal const int Capacity = 64;
    internal static (RewardStartupEnrollment Enrollment, string Profile, RewardJournal Journal) ReadBase(string root, string id)
    {
        if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("recovery-profile-invalid");
        if (!RewardStartupFiles.MarkerAbsent(root) || RewardOfflineSession.HasActive(root))
            throw new RewardDataException("recovery-normal-session-required");
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        var enrollmentPath = Path.Combine(directory, "enrollment.json");
        RewardStartupFiles.RequireLocalPath(root, enrollmentPath);
        var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
        if (enrollment.Schema != 1 || enrollment.Binding is null || enrollment.Binding.ProfileId != id
            || !Path.IsPathFullyQualified(enrollment.RuntimeRoot) || !RewardRuntimeFiles.SameRoot(enrollment.RuntimeRoot, root))
            throw new RewardDataException("recovery-enrollment-invalid");
        enrollment.Binding.Validate();
        var profile = ReadProfile(root, id);
        RewardStartupFiles.RequireCleanProfile(profile, enrollment.Binding);
        _ = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new());
        var journalPath = Path.Combine(directory, "journal.json");
        RewardStartupFiles.RequireLocalPath(root, journalPath);
        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(journalPath), enrollment.Binding, new());
        var cola = Path.Combine(directory, "cola-plans");
        if (RewardRuntimeFiles.Exists(root, cola))
            foreach (var path in Files(root, cola, 4096).Where(p => !p.EndsWith(".done.json", StringComparison.Ordinal)))
                if (!RewardRuntimeFiles.Exists(root, path[..^5] + ".done.json"))
                    throw new RewardDataException("recovery-cola-transaction-unresolved");
        return (enrollment, profile, journal);
    }

    internal static string ReadProfile(string root, string id)
    {
        var path = Path.Combine(root, "user", "profiles", id + ".json");
        RewardStartupFiles.RequireLocalPath(root, path);
        if (new FileInfo(path).Length > 67_108_864) throw new RewardDataException("recovery-profile-too-large");
        return File.ReadAllText(path);
    }

    internal static RewardRecoveryReview Review(string root, string id)
    {
        var (enrollment, profile, journal) = ReadBase(root, id);
        RewardRuntimeInbox.Preflight(root, id);
        var binding = enrollment.Binding;
        var closures = RewardRecovery.ReadClosures(root, id, profile, allowUnacknowledged: true);
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        var receipts = new List<(RewardEarnedReceipt Receipt, string Path)>();
        var inbox = Path.Combine(directory, "inbox");
        if (RewardRuntimeFiles.Exists(root, inbox))
        {
            var origin = RewardRuntimeFiles.ReadHandoff(root, Path.Combine(directory, "handoff.json"));
            if (origin.Binding != binding || !RewardRuntimeFiles.SameRoot(origin.RuntimeRoot, root))
                throw new RewardDataException("recovery-receipt-origin-invalid");
            foreach (var path in Files(root, inbox, 4096)) receipts.Add((RewardRuntimeInbox.ReadReceipt(root, directory, origin, path), path));
        }
        var cases = new List<RewardRecoveryCase>();
        var provenRuns = new HashSet<(string Run, string Arm)>();
        var snapshots = Path.Combine(root, "user", "arena-reward-snapshots", id);
        foreach (var path in Files(root, snapshots, 4096))
        {
            var guard = RewardStartupFiles.Read<RewardSnapshotGuard>(path, 1024);
            if (guard.Schema != 1 || !RewardIdentity.IsGuid(guard.Generation) || Path.GetFileName(path) != guard.Generation + ".json"
                || guard.State is not ("pending" or "restored" or "cancelled-before-start"))
                throw new RewardDataException("reward-snapshot-guard-invalid");
            if (closures.ClosesSnapshot(guard.Generation)) continue;
            // Even a restored guard can have a pending run after a crash between
            // the two terminal writes. Positive durable proof is automatic work,
            // not a player waiver. The helper also validates offline membership.
            if (guard.State != "cancelled-before-start"
                && RewardRestorationCheckpoint.HasDurableProof(root, id, guard.Generation, out var proven))
            {
                if (proven is not null) provenRuns.Add((proven.RunId, proven.ArmToken));
                continue;
            }
            if (guard.State != "pending" || receipts.Any(r => r.Receipt.Evidence.Authorization.RestorationId == guard.Generation)) continue;
            var evidence = new List<RewardRecoveryEvidenceFile> { FileEvidence(root, path) };
            foreach (var checkpoint in Files(root, Path.Combine(root, "user", "arena-reward-checkpoints", id, guard.Generation), 32))
            {
                RequireCheckpointSchema(checkpoint);
                evidence.Add(FileEvidence(root, checkpoint));
            }
            cases.Add(new("snapshot", guard.Generation, null, null, evidence.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray()));
        }
        var runs = Path.Combine(root, "user", "arena-reward-runs", id);
        foreach (var path in Files(root, runs, 4096))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var value = document.RootElement;
            RewardStartupFiles.RejectDuplicateProperties(value);
            var run = value.GetProperty("runId").GetString();
            var arm = value.GetProperty("armToken").GetString();
            var hasState = value.TryGetProperty("state", out var state);
            if (value.GetProperty("schema").GetInt32() != 1 || !RewardIdentity.IsGuid(run) || !RewardIdentity.IsGuid(arm)
                || value.EnumerateObject().Count() != (hasState ? 4 : 3) || Path.GetFileName(path) != run + ".json"
                || hasState && state.GetString() is not ("restored" or "discarded" or "unrewarded-restored"))
                throw new RewardDataException("reward-pending-state-invalid");
            if (hasState || closures.ClosesRun(run!, arm!) || provenRuns.Contains((run!, arm!))
                || receipts.Any(r => r.Receipt.Evidence.Authorization.RunId == run && r.Receipt.Evidence.Authorization.ArmToken == arm)) continue;
            cases.Add(new("run", run!, arm, null, RelatedFiles(root, directory, run!, path)));
        }
        var ledger = RewardProfileDocument.ReadLedger(profile, binding, new());
        if (ledger.Any(p => !journal.Entries.Any(e => e.Payment == p))) throw new RewardDataException("recovery-ledger-diverged");
        var decisions = Path.Combine(directory, "decisions");
        var history = RewardRuntimeFiles.Exists(root, decisions) ? RewardApprovalProducer.ReadDecisions(root, binding, decisions) : [];
        foreach (var entry in journal.Entries)
            if (!history.Any(d => d.Approval is { } approval && approval.Claim.Key == entry.Payment.ReceiptKey
                && approval.Claim.Fingerprint == entry.Payment.Fingerprint && approval.Claim.Roubles == entry.Payment.Roubles
                && approval.Claim.Experience == entry.Payment.Experience && RewardColaProfile.MatchesClaim(entry.Payment.Cola, approval.Claim.Cola)
                && approval.CleanProfileRevision == entry.BeforeRevision))
                throw new RewardDataException("recovery-result-history-incomplete");
        foreach (var entry in journal.Entries)
        {
            if (ledger.Contains(entry.Payment) || closures.ClosesPayment(entry)) continue;
            if (entry.Phase != RewardJournalPhase.Committed || entry.Payment.Cola is not null)
                throw new RewardDataException("recovery-payment-not-closable");
            var run = entry.Payment.ReceiptKey.Split(':')[0];
            cases.Add(new("payment", entry.Payment.ReceiptKey, null, entry, RelatedFiles(root, directory, run)));
        }
        var allAttempts = RewardRecovery.ReadAttempts(root, id);
        var markers = RewardProfileDocument.ReadRecoveryMarkers(profile, binding);
        foreach (var prior in allAttempts.Where(a => a.Completion?.State == "closed-without-payment"
            && !markers.Any(m => m.ReviewId == a.Request.ReviewId)))
            foreach (var item in prior.Request.Review.Cases)
                if (!cases.Any(c => c.Kind == item.Kind && c.Id == item.Id)
                    && !(item.Kind == "snapshot" && closures.ClosesSnapshot(item.Id))
                    && !(item.Kind == "run" && closures.ClosesRun(item.Id, item.ArmToken!))
                    && !(item.Kind == "payment" && closures.ClosesPayment(item.Payment!))) cases.Add(item);
        if (cases.Count > Capacity) throw new RewardDataException("recovery-case-capacity");
        var attempts = allAttempts.Where(a => a.Completion is not null)
            .Select(a => RewardIdentity.Hash(RewardRuntimeFiles.Json(a.Completion))).Order(StringComparer.Ordinal).ToArray();
        return new(1, binding, RewardProfileDocument.RecoveryFingerprint(profile, binding), attempts,
            cases.OrderBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyList<RewardRecoveryEvidenceFile> RelatedFiles(string root, string directory, string run, params string[] extra)
    {
        var result = extra.Select(p => FileEvidence(root, p)).ToList();
        foreach (var name in new[] { "decisions", "allocations", "imported", "inbox" })
        {
            var path = Path.Combine(directory, name, run + ".json");
            if (RewardRuntimeFiles.Exists(root, path)) result.Add(FileEvidence(root, path));
        }
        return result.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal static void RequireEvidence(string root, RewardBinding binding, RewardRecoveryCase item, RewardJournal journal,
        bool allowProvenTerminalTransition = false)
    {
        if (item is null || item.Evidence is null || item.Evidence.Count > 128 || item.Evidence.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count() != item.Evidence.Count)
            throw new RewardDataException("recovery-evidence-invalid");
        string? transitionedPrimary = null;
        if (item.Kind == "payment")
        {
            if (item.Payment is null || item.Payment.Payment is null || item.Id != item.Payment.Payment.ReceiptKey || item.ArmToken is not null
                || item.Payment.Phase != RewardJournalPhase.Committed || item.Payment.Payment.Cola is not null
                || !journal.Entries.Contains(item.Payment)) throw new RewardDataException("recovery-payment-evidence-changed");
        }
        else if (item.Kind is "snapshot" or "run")
        {
            if (!RewardIdentity.IsGuid(item.Id) || item.Payment is not null
                || item.Kind == "snapshot" && item.ArmToken is not null || item.Kind == "run" && !RewardIdentity.IsGuid(item.ArmToken)
                || item.Evidence.Count == 0) throw new RewardDataException("recovery-case-invalid");
            var primary = $"user/arena-reward-{(item.Kind == "snapshot" ? "snapshots" : "runs")}/{binding.ProfileId}/{item.Id}.json";
            if (!item.Evidence.Any(e => e.RelativePath == primary)) throw new RewardDataException("recovery-case-primary-missing");
            var primaryPath = Path.Combine(root, primary.Replace('/', Path.DirectorySeparatorChar));
            var originalPrimary = item.Evidence.Single(e => e.RelativePath == primary);
            if (FileEvidence(root, primaryPath) != originalPrimary)
            {
                // A completed discard remains permanent even if another host
                // subsequently supplies the exact operation's valid restore.
                // This exception grants no payout or profile-write authority.
                if (!allowProvenTerminalTransition
                    || !ProvesTerminalTransition(root, binding, item, primaryPath, originalPrimary.Sha256))
                    throw new RewardDataException("recovery-source-evidence-changed");
                transitionedPrimary = primary;
            }
            else if (item.Kind == "snapshot")
            {
                var guard = RewardStartupFiles.Read<RewardSnapshotGuard>(primaryPath, 1024);
                if (guard.Schema != 1 || guard.Generation != item.Id || guard.State != "pending") throw new RewardDataException("recovery-snapshot-evidence-changed");
            }
            else
            {
                var run = RewardStartupFiles.Read<RewardRuntimePending>(primaryPath, 1024);
                if (run.Schema != 1 || run.RunId != item.Id || run.ArmToken != item.ArmToken) throw new RewardDataException("recovery-run-evidence-changed");
            }
        }
        else throw new RewardDataException("recovery-case-unsupported");
        foreach (var file in item.Evidence)
        {
            if (file is null || !RewardIdentity.IsHex(file.Sha256, 64) || Path.IsPathRooted(file.RelativePath)
                || file.RelativePath.Contains('\\') || file.RelativePath.Split('/').Any(p => p is "" or "." or "..")
                || !AllowedEvidence(binding.ProfileId, item, file.RelativePath)) throw new RewardDataException("recovery-evidence-path-invalid");
            var path = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (file.RelativePath != transitionedPrimary && FileEvidence(root, path) != file)
                throw new RewardDataException("recovery-source-evidence-changed");
        }
    }

    private static bool ProvesTerminalTransition(string root, RewardBinding binding, RewardRecoveryCase item,
        string path, string originalHash)
    {
        if (item.Kind == "snapshot")
        {
            var guard = RewardStartupFiles.Read<RewardSnapshotGuard>(path, 1024);
            return guard.Schema == 1 && guard.Generation == item.Id && guard.State == "restored"
                && RewardIdentity.Hash(RewardRuntimeFiles.Json(guard with { State = "pending" })) == originalHash
                && RewardRestorationCheckpoint.HasDurableProof(root, binding.ProfileId, item.Id);
        }
        var run = RewardStartupFiles.Read<RecoveryTerminalRun>(path, 1024);
        return run.Schema == 1 && run.RunId == item.Id && run.ArmToken == item.ArmToken
            && run.State is "restored" or "unrewarded-restored"
            && RewardIdentity.Hash(RewardRuntimeFiles.Json(new RewardRuntimePending(1, run.ArmToken, run.RunId))) == originalHash
            && RewardRestorationCheckpoint.HasDurableRunProof(root, binding.ProfileId, run.RunId, run.ArmToken);
    }

    private sealed record RecoveryTerminalRun(int Schema, string ArmToken, string RunId, string State);

    private static bool AllowedEvidence(string id, RewardRecoveryCase item, string path)
    {
        var run = item.Kind == "payment" ? item.Id.Split(':')[0] : item.Id;
        if (path == $"user/arena-reward-snapshots/{id}/{item.Id}.json" && item.Kind == "snapshot") return true;
        if (path.StartsWith($"user/arena-reward-checkpoints/{id}/{item.Id}/", StringComparison.Ordinal) && item.Kind == "snapshot") return true;
        if (path == $"user/arena-reward-runs/{id}/{item.Id}.json" && item.Kind == "run") return true;
        return new[] { "decisions", "allocations", "imported", "inbox" }.Any(n => path == $"user/arena-rewards/{id}/{n}/{run}.json");
    }

    internal static RewardRecoveryEvidenceFile FileEvidence(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        if (new FileInfo(path).Length > RewardRuntimeFiles.MaxReceiptBytes) throw new RewardDataException("recovery-evidence-too-large");
        return new(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    private static void RequireCheckpointSchema(string path)
    {
        // Typed checkpoint readers reject future/unknown fields before any waiver.
        object record = Path.GetFileName(path) switch
        {
            "checkpoint.json" => RewardStartupFiles.Read<RewardCheckpoint>(path),
            "run.json" => RewardStartupFiles.Read<RewardCheckpointRun>(path),
            "observed.json" => RewardStartupFiles.Read<RewardCheckpointObservation>(path),
            "persisted.json" => RewardStartupFiles.Read<RewardCheckpointProof>(path),
            "final.json" => RewardStartupFiles.Read<RewardCheckpointFinal>(path),
            "complete.json" => RewardStartupFiles.Read<RewardCheckpointCompletion>(path),
            "mismatch.json" => RewardStartupFiles.Read<RecoveryCheckpointMismatch>(path),
            _ => throw new RewardDataException("recovery-checkpoint-unsupported")
        };
        using var document = JsonDocument.Parse(RewardRuntimeFiles.Json(record));
        if (document.RootElement.GetProperty("schema").GetInt32() != 1) throw new RewardDataException("recovery-checkpoint-schema");
    }
    private sealed record RecoveryCheckpointMismatch(int Schema, string Generation, string Phase, string Expected, string Actual, IReadOnlyList<string> DifferingPathHashes);

    internal static string[] Files(string root, string directory, int maximum)
    {
        if (!RewardRuntimeFiles.Exists(root, directory)) return [];
        var paths = Directory.EnumerateFileSystemEntries(directory).Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z"))
            .Order(StringComparer.Ordinal).Take(maximum + 1).ToArray();
        if (paths.Length > maximum) throw new RewardDataException("recovery-evidence-capacity");
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            if (Path.GetExtension(path) != ".json" || Directory.Exists(path)) throw new RewardDataException("recovery-evidence-unexpected-file");
        }
        return paths;
    }
}
