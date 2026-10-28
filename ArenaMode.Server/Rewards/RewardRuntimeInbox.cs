using System.Text.Json;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// Admits earned receipts independently of the current ordinary profile hash.
/// Their signed restoration proof concerns the Arena snapshot (possibly private),
/// never authorizes copying that snapshot back. A NEW allocation binds the latest
/// clean ordinary profile once, within native startup. Existing allocations and
/// approvals retain their original hash and cannot be silently rebased.
/// </summary>
internal static class RewardRuntimeInbox
{
    internal static RewardEarnedReceipt ReadReceipt(string root, string directory, RewardHandoff origin, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(path, RewardRuntimeFiles.MaxReceiptBytes);
        if (!RewardIdentity.IsGuid(signed.DelegationId)) throw new RewardDataException("reward-delegation-id-invalid");
        var delegationPath = Path.Combine(directory, "delegations", signed.DelegationId + ".json");
        var authority = RewardRuntimeFiles.ReadHandoff(root, delegationPath);
        if (authority.Binding != origin.Binding || !RewardRuntimeFiles.SameRoot(authority.RuntimeRoot, root) || !authority.Enabled)
            throw new RewardDataException("reward-receipt-origin-mismatch");
        var receipt = RewardRuntimeFiles.Verify(authority, signed);
        if (Path.GetFileName(path) != receipt.Evidence.Authorization.RunId + ".json")
            throw new RewardDataException("reward-receipt-filename-mismatch");
        RewardOfflineSession.RequireClosedReceipt(root, directory, authority, signed, receipt.Evidence.Authorization.RunId);
        return receipt;
    }

    // A validated discard rejects this delegation even when old signed files
    // reappear later. Keep them as evidence without poisoning unrelated rewards.
    internal static RewardEarnedReceipt? ReadRetainedReceipt(string root, string directory, RewardHandoff origin, string path)
    {
        try { return ReadReceipt(root, directory, origin, path); }
        catch (RewardOfflineDiscardedException)
        {
            string file = Path.GetFileName(path);
            if (RewardRuntimeFiles.Exists(root, Path.Combine(directory, "allocations", file))
                || RewardRuntimeFiles.Exists(root, Path.Combine(directory, "imported", file)))
                throw new RewardDataException("offline-discard-payment-history-conflict");
            return null;
        }
    }

    // Validate the COMPLETE retained transport inventory before checkpoint
    // publication can add a canonical receipt, and again before an inbox batch
    // can allocate/pay its first entry. A bad later file must not be discovered
    // only after an earlier, otherwise valid receipt has already changed XP/mail.
    // Missing native payment evidence is deliberately left to recovery/settlement;
    // this read-only preflight cannot repair or waive an unsettled old claim.
    internal static void Preflight(string root, string id)
    {
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        var paths = ReadPaths(root, Path.Combine(directory, "inbox"));
        if (paths.Length == 0) return;
        var enrollmentPath = Path.Combine(directory, "enrollment.json");
        RewardStartupFiles.RequireLocalPath(root, enrollmentPath);
        var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
        var origin = RewardRuntimeFiles.ReadHandoff(root, Path.Combine(directory, "handoff.json"));
        if (enrollment.Schema != 1 || enrollment.Binding is null || enrollment.Binding.ProfileId != id
            || !Path.IsPathFullyQualified(enrollment.RuntimeRoot) || !RewardRuntimeFiles.SameRoot(enrollment.RuntimeRoot, root)
            || origin.Binding != enrollment.Binding || !RewardRuntimeFiles.SameRoot(origin.RuntimeRoot, root))
            throw new RewardDataException("runtime-inbox-origin-mismatch");
        enrollment.Binding.Validate();
        foreach (var path in paths)
        {
            var earned = ReadRetainedReceipt(root, directory, origin, path);
            if (earned is null) continue;
            var fingerprint = RewardIdentity.Hash(RewardRuntimeFiles.Json(earned));
            var run = earned.Evidence.Authorization.RunId;
            var allocation = ReadAllocationIfPresent(root, Path.Combine(directory, "allocations"), Path.GetFileName(path));
            if (allocation is not null) RequireAllocationSource(allocation, earned, fingerprint);
            var importedPath = Path.Combine(directory, "imported", run + ".json");
            if (!RewardRuntimeFiles.Exists(root, importedPath)) continue;
            var imported = RewardStartupFiles.Read<RewardImported>(importedPath, 16384);
            if (imported.Schema != 1 || imported.RunId != run || imported.SourceFingerprint != fingerprint
                || allocation is null || imported.Sequence != allocation.Candidate.Authorization.Sequence)
                throw new RewardDataException("reward-import-history-conflict");
        }
    }

    private static string[] ReadPaths(string root, string inbox)
    {
        if (!RewardRuntimeFiles.Exists(root, inbox)) return [];
        var paths = Directory.EnumerateFileSystemEntries(inbox)
            .Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z"))
            .Take(4097).ToArray();
        if (paths.Length > 4096) throw new RewardDataException("reward-inbox-capacity");
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            if (!RewardIdentity.IsGuid(Path.GetFileNameWithoutExtension(path)) || Path.GetExtension(path) != ".json"
                || Directory.Exists(path)) throw new RewardDataException("unexpected-reward-inbox-file");
        }
        return paths;
    }

    internal static async Task<RewardStartupOutcome?> ProcessAsync(RewardStartupWindow window, RewardStartupEnrollment enrollment,
        CancellationToken token, Func<Task<RewardStartupOutcome>> settleCurrent)
    {
        window.RequireOpen();
        var root = window.Root;
        var id = enrollment.Binding.ProfileId;
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        var inbox = Path.Combine(directory, "inbox");
        Preflight(root, id);
        var origin = RewardRuntimeFiles.ReadHandoff(root, Path.Combine(directory, "handoff.json"));
        if (origin.Binding != enrollment.Binding || !RewardRuntimeFiles.SameRoot(origin.RuntimeRoot, root) || !origin.Enabled)
            throw new RewardDataException("runtime-inbox-origin-mismatch");
        var closures = RewardRecovery.ReadClosures(root, id, RewardRecoveryEvidence.ReadProfile(root, id));
        RequireNoPendingSnapshots(root, id, directory, origin, closures);
        RequireNoPendingRuns(root, id, directory, origin, closures);
        if (!RewardRuntimeFiles.Exists(root, inbox)) return null;
        var paths = ReadPaths(root, inbox);
        var allocations = Path.Combine(directory, "allocations");
        var imported = Path.Combine(directory, "imported");
        RewardRuntimeFiles.EnsureDirectory(root, allocations);
        RewardRuntimeFiles.EnsureDirectory(root, imported);
        // Recover allocated receipts first, so a random GUID sort cannot advance
        // the approval slot past a previously allocated but unfinished payment.
        var ordered = paths.Select(p => (Path: p, Allocation: ReadAllocationIfPresent(root, allocations, Path.GetFileName(p))))
            .OrderBy(p => p.Allocation?.Candidate.Authorization.Sequence ?? long.MaxValue)
            .ThenBy(p => Path.GetFileName(p.Path), StringComparer.Ordinal).ToArray();
        RewardStartupOutcome? last = null;
        long paid = 0;
        long experiencePaid = 0;
        bool closed = false;
        foreach (var item in ordered)
        {
            token.ThrowIfCancellationRequested();
            window.RequireOpen();
            if (!RewardEnrollment.WantsRewards(root, id)) return Outcome(RewardSettlementStatus.Deferred, "rewards-not-enabled");
            var earned = ReadRetainedReceipt(root, directory, origin, item.Path);
            if (earned is null) { closed = true; continue; }
            var fingerprint = RewardIdentity.Hash(RewardRuntimeFiles.Json(earned));
            var runId = earned.Evidence.Authorization.RunId;
            var importedPath = Path.Combine(imported, runId + ".json");
            var allocation = item.Allocation;
            if (allocation is not null) RequireAllocationSource(allocation, earned, fingerprint);
            if (closures.ClosesReceipt(earned.Evidence) || allocation is not null && closures.ClosesReceipt(allocation.Candidate))
            {
                // Keep the signed late source and every historical allocation /
                // import byte intact. The durable decision is the terminal record;
                // no new payment allocation or imported-as-paid marker is made.
                closed = true;
                continue;
            }
            if (RewardRuntimeFiles.Exists(root, importedPath))
            {
                var complete = RewardStartupFiles.Read<RewardImported>(importedPath, 16384);
                if (complete.Schema != 1 || complete.RunId != runId || complete.SourceFingerprint != fingerprint
                    || allocation is null || complete.Sequence != allocation.Candidate.Authorization.Sequence)
                    throw new RewardDataException("reward-import-history-conflict");
                RequireAllocationSource(allocation, earned, fingerprint);
                RequireSettled(root, directory, allocation.Candidate);
                continue;
            }
            if (allocation is null)
            {
                var profilePath = Path.Combine(root, "user", "profiles", id + ".json");
                RewardStartupFiles.RequireLocalPath(root, profilePath);
                var profile = File.ReadAllText(profilePath);
                RewardStartupFiles.RequireCleanProfile(profile, enrollment.Binding);
                _ = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new());
                var decisionsPath = Path.Combine(directory, "decisions");
                var count = RewardRuntimeFiles.Exists(root, decisionsPath) ? Directory.EnumerateFiles(decisionsPath, "*.json").Count() : 0;
                if (count >= 4096) throw new RewardDataException("reward-receipt-capacity");
                var revision = RewardIdentity.Hash(profile);
                // The source restoration is already authenticated above. This
                // startup allocation's proof explicitly binds the current clean
                // ordinary profile; the unchanged signed source stays retained.
                var candidate = earned.Evidence with
                {
                    Authorization = earned.Evidence.Authorization with { Sequence = count + 1, CleanProfileRevision = revision },
                    Restoration = earned.Evidence.Restoration with { ExpectedProfileRevision = revision, PersistedProfileRevision = revision }
                };
                allocation = new(1, fingerprint, candidate);
                RewardRuntimeFiles.WriteNew(root, Path.Combine(allocations, runId + ".json"), allocation);
            }
            RequireAllocationSource(allocation, earned, fingerprint);
            RewardRuntimeFiles.Write(root, Path.Combine(directory, "candidate.json"), allocation.Candidate);
            last = await settleCurrent();
            if (last.Result.Status is RewardSettlementStatus.Paid or RewardSettlementStatus.AlreadyPaid
                || last.Result.Status is RewardSettlementStatus.Deferred or RewardSettlementStatus.Rejected
                    && RewardResultValidator.Evaluate(allocation.Candidate.Authorization, allocation.Candidate.Report).Status != RewardEvaluationStatus.Eligible)
            {
                RequireSettled(root, directory, allocation.Candidate);
                RewardRuntimeFiles.WriteNew(root, importedPath, new RewardImported(1, fingerprint, runId, allocation.Candidate.Authorization.Sequence));
                if (last.Result.Status == RewardSettlementStatus.Paid)
                {
                    paid = checked(paid + last.Roubles);
                    experiencePaid = checked(experiencePaid + last.Experience);
                }
            }
            else return last;
        }
        if (paid > 0) return new(id, new(RewardSettlementStatus.Paid, "queued-rewards-delivered", last?.Result.MessageId), paid, experiencePaid);
        if (closed && last is null) return Outcome(RewardSettlementStatus.ClosedWithoutPayment, "reward-closed-without-payment");
        return last ?? (ordered.Length > 0 ? Outcome(RewardSettlementStatus.AlreadyPaid, "reward-inbox-already-imported") : null);

        RewardStartupOutcome Outcome(RewardSettlementStatus status, string reason) => new(id, new(status, reason), 0);
    }

    private static RewardAllocation? ReadAllocationIfPresent(string root, string directory, string file)
    {
        var path = Path.Combine(directory, file);
        return RewardRuntimeFiles.Exists(root, path) ? RewardStartupFiles.Read<RewardAllocation>(path) : null;
    }

    internal static void RequireAllocationSource(RewardAllocation allocation, RewardEarnedReceipt source, string fingerprint)
    {
        if (allocation.Schema != 1 || allocation.SourceFingerprint != fingerprint || allocation.Candidate?.Authorization is null
            || allocation.Candidate.Restoration is null) throw new RewardDataException("reward-allocation-source-mismatch");
        var reconstructed = allocation.Candidate with
        {
            Authorization = allocation.Candidate.Authorization with
            {
                Sequence = source.Evidence.Authorization.Sequence,
                CleanProfileRevision = source.Evidence.Authorization.CleanProfileRevision
            },
            Restoration = allocation.Candidate.Restoration with
            {
                ExpectedProfileRevision = source.Evidence.Restoration.ExpectedProfileRevision,
                PersistedProfileRevision = source.Evidence.Restoration.PersistedProfileRevision
            }
        };
        if (RewardRuntimeFiles.Json(reconstructed) != RewardRuntimeFiles.Json(source.Evidence))
            throw new RewardDataException("reward-allocation-content-conflict");
        RewardRuntimeFiles.RequireProof(allocation.Candidate);
    }

    private static void RequireSettled(string root, string directory, RewardApprovalCandidate candidate)
    {
        var run = candidate.Authorization;
        var decisionPath = Path.Combine(directory, "decisions", run.RunId + ".json");
        RewardStartupFiles.RequireLocalPath(root, decisionPath);
        var decision = RewardStartupFiles.Read<RewardApprovalDecision>(decisionPath, RewardStartupFiles.MaxApprovalBytes);
        var evaluation = RewardResultValidator.Evaluate(run, candidate.Report);
        if (decision.Schema != 1 || decision.Binding != run.Binding || decision.Sequence != run.Sequence || decision.RunId != run.RunId
            || decision.SourceFingerprint != RewardIdentity.Hash(RewardRuntimeFiles.Json(candidate)) || decision.Evaluation != evaluation)
            throw new RewardDataException("reward-import-decision-mismatch");
        if (evaluation.Status != RewardEvaluationStatus.Eligible)
        {
            if (decision.Approval is not null) throw new RewardDataException("reward-zero-import-has-approval");
            return;
        }
        var journalPath = Path.Combine(directory, "journal.json");
        var profilePath = Path.Combine(root, "user", "profiles", run.Binding.ProfileId + ".json");
        RewardStartupFiles.RequireLocalPath(root, journalPath);
        RewardStartupFiles.RequireLocalPath(root, profilePath);
        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(journalPath), run.Binding, new());
        var ledger = RewardProfileDocument.ReadLedger(File.ReadAllText(profilePath), run.Binding, new());
        var approval = decision.Approval;
        if (approval is null || approval.Claim.Roubles != evaluation.Roubles || approval.Claim.Experience != evaluation.Experience
            || approval.Claim.Cola != run.Cola
            || !journal.Entries.Any(e => e.Phase == RewardJournalPhase.Committed && ledger.Contains(e.Payment)
                && e.Payment.ReceiptKey == approval.Claim.Key && e.Payment.Fingerprint == approval.Claim.Fingerprint
                && e.Payment.Roubles == approval.Claim.Roubles && e.Payment.Experience == approval.Claim.Experience
                && RewardColaProfile.MatchesClaim(e.Payment.Cola, approval.Claim.Cola)
                && e.BeforeRevision == approval.CleanProfileRevision))
            throw new RewardDataException("reward-import-not-settled");
    }

    private static void RequireNoPendingRuns(string root, string id, string authorityDirectory, RewardHandoff origin, RewardRecoveryClosureSet closures)
    {
        var directory = Path.Combine(root, "user", "arena-reward-runs", id);
        if (!RewardRuntimeFiles.Exists(root, directory)) return;
        var paths = Directory.EnumerateFiles(directory, "*.json").Take(4097).ToArray();
        if (paths.Length > 4096) throw new RewardDataException("reward-run-history-capacity");
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
            var value = document.RootElement;
            var runId = value.GetProperty("runId").GetString();
            var armToken = value.GetProperty("armToken").GetString();
            if (value.GetProperty("schema").GetInt32() != 1 || !RewardIdentity.IsGuid(runId) || !RewardIdentity.IsGuid(armToken)
                || Path.GetFileName(path) != runId + ".json") throw new RewardDataException("reward-pending-state-invalid");
            if (!value.TryGetProperty("state", out var state) || state.GetString() is not ("restored" or "discarded" or "unrewarded-restored"))
            {
                if (closures.ClosesRun(runId!, armToken!)) continue;
                // Crash between immutable receipt publication and the terminal
                // marker may use that same signed, positively restored receipt.
                // Mere absence of a RAM snapshot still never clears a pending run.
                var receiptPath = Path.Combine(authorityDirectory, "inbox", runId + ".json");
                if (!RewardRuntimeFiles.Exists(root, receiptPath)) throw new RewardDataException("reward-run-restoration-unresolved");
                var proof = ReadReceipt(root, authorityDirectory, origin, receiptPath);
                if (proof.Evidence.Authorization.RunId != runId || proof.Evidence.Authorization.ArmToken != armToken)
                    throw new RewardDataException("reward-run-restoration-unresolved");
            }
        }
    }

    private static void RequireNoPendingSnapshots(string root, string id, string authorityDirectory, RewardHandoff origin, RewardRecoveryClosureSet closures)
    {
        var directory = Path.Combine(root, "user", "arena-reward-snapshots", id);
        if (!RewardRuntimeFiles.Exists(root, directory)) return;
        var paths = Directory.EnumerateFiles(directory, "*.json").Take(4097).ToArray();
        if (paths.Length > 4096) throw new RewardDataException("reward-snapshot-history-capacity");
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            var guard = RewardStartupFiles.Read<RewardSnapshotGuard>(path, 1024);
            if (guard.Schema != 1 || !RewardIdentity.IsGuid(guard.Generation) || Path.GetFileName(path) != guard.Generation + ".json")
                throw new RewardDataException("reward-snapshot-guard-invalid");
            if (guard.State is "restored" or "cancelled-before-start") continue;
            if (guard.State != "pending") throw new RewardDataException("reward-snapshot-guard-invalid");
            if (closures.ClosesSnapshot(guard.Generation)) continue;
            // The only recovery evidence is the immutable signed receipt made
            // after that exact snapshot's positive durable restoration.
            var inbox = Path.Combine(authorityDirectory, "inbox");
            var recovered = false;
            if (RewardRuntimeFiles.Exists(root, inbox))
                foreach (var receiptPath in Directory.EnumerateFiles(inbox, "*.json").Take(4096))
                    if (ReadRetainedReceipt(root, authorityDirectory, origin, receiptPath)?.Evidence.Restoration.RestorationId == guard.Generation)
                    { recovered = true; break; }
            if (!recovered) throw new RewardDataException("reward-snapshot-restoration-unresolved");
        }
    }
}
