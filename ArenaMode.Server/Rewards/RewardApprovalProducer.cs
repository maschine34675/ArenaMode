using System.Text.Json;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// The startup-only bridge from server-owned run/restoration evidence and an
/// untrusted combat report to the settlement kernel's approval. No profile write
/// is performed here. The active ordinary server and the private copy must never
/// use this as a live writer. Authority capture/transport is a separate adapter.
/// </summary>
internal static class RewardApprovalProducer
{
    internal static RewardEvaluation? PrepareIfPresent(RewardStartupWindow window, RewardStartupEnrollment enrollment)
    {
        window.RequireOpen();
        enrollment.Binding.Validate();
        if (enrollment.Schema != 1 || !enrollment.Enabled || !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
            || !string.Equals(RewardStartupFiles.Canonical(enrollment.RuntimeRoot), window.Root, RewardStartupFiles.PathComparison))
            throw new RewardDataException("approval-enrollment-mismatch");
        var directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory, enrollment.Binding.ProfileId);
        var candidatePath = Path.Combine(directory, "candidate.json");
        if (!Exists(window.Root, candidatePath)) return null; // Existing trusted approval path remains supported.

        var lockPath = Path.Combine(directory, ".approval.lock");
        RewardStartupFiles.RequireLocalPath(window.Root, lockPath, allowMissingLeaf: true);
        using var guard = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var candidate = RewardStartupFiles.Read<RewardApprovalCandidate>(candidatePath);
        if (candidate.Schema != 1 || candidate.Authorization is null || candidate.Restoration is null || candidate.Report is null)
            throw new RewardDataException("invalid-approval-candidate");
        var run = candidate.Authorization;
        RewardResultValidator.ValidateAuthorization(run);
        if (run.Binding != enrollment.Binding) throw new RewardDataException("candidate-binding-mismatch");
        if (RewardRestorationCheckpoint.IsDiscardedRun(window.Root, run.Binding, run.RunId))
            throw new RewardDataException("offline-session-discarded");
        RequireRestoration(candidate);
        var evaluation = RewardResultValidator.Evaluate(run, candidate.Report);
        RewardStartupApproval? approval = evaluation.Status == RewardEvaluationStatus.Eligible
            ? new(1, RewardStartupApprovalState.RestoredAndApproved,
                new RewardClaim(run.Binding, run.RunId, run.Sequence, evaluation.Roubles, run.Policy.Version, evaluation.Experience, run.Cola), run.CleanProfileRevision)
            : null;
        approval?.Claim.Validate(new());
        // Persist the complete semantic input identity, not just the calculated
        // amount. Equal RUB from two different reports is still a conflict.
        var fingerprint = RewardIdentity.Hash(Json(candidate));
        var decision = new RewardApprovalDecision(1, run.Binding, run.RunId, run.Sequence, fingerprint, evaluation, approval);
        var decisions = Path.Combine(directory, "decisions");
        if (!Exists(window.Root, decisions))
        {
            Directory.CreateDirectory(decisions);
            RewardStartupFiles.RequireLocalPath(window.Root, decisions);
        }
        var history = ReadDecisions(window.Root, enrollment.Binding, decisions);
        var decisionPath = Path.Combine(decisions, run.RunId + ".json");
        var existingDecision = Exists(window.Root, decisionPath);
        if (existingDecision)
        {
            var previous = RewardStartupFiles.Read<RewardApprovalDecision>(decisionPath, RewardStartupFiles.MaxApprovalBytes);
            if (previous != decision) throw new RewardDataException("result-decision-conflict");
            if (run.Sequence != history.Count) throw new RewardDataException("stale-result-candidate");
        }
        else if (history.Count >= 4096 || run.Sequence != history.Count + 1)
            throw new RewardDataException("result-sequence-or-capacity");

        var profilePath = Path.Combine(window.Root, "user", "profiles", run.Binding.ProfileId + ".json");
        var journalPath = Path.Combine(directory, "journal.json");
        RewardStartupFiles.RequireLocalPath(window.Root, profilePath);
        RewardStartupFiles.RequireLocalPath(window.Root, journalPath);
        var profile = new RewardProfileSnapshot(File.ReadAllText(profilePath));
        RewardStartupFiles.RequireCleanProfile(profile.Json, run.Binding);
        var ledger = RewardProfileDocument.ReadLedger(profile.Json, run.Binding, new());
        var journalJson = File.ReadAllText(journalPath);
        var journal = RewardSettlementCore.ParseJournal(journalJson, run.Binding, new());
        var closures = RewardRecovery.ReadClosures(window.Root, run.Binding.ProfileId, profile.Json);
        if (closures.ClosesReceipt(candidate)) throw new RewardDataException("result-closed-without-payment");
        // Switching into the producer protocol cannot infer missing decisions
        // from a populated payment journal, even when only the newest was lost.
        foreach (var entry in journal.Entries)
            if (!history.Any(d => d.Approval is { } known && known.Claim.Key == entry.Payment.ReceiptKey
                && known.Claim.Fingerprint == entry.Payment.Fingerprint && known.Claim.Roubles == entry.Payment.Roubles
                && known.Claim.Experience == entry.Payment.Experience
                && known.CleanProfileRevision == entry.BeforeRevision))
                throw new RewardDataException("payment-without-result-decision");
        var key = run.RunId + ":" + run.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payment = journal.Entries.SingleOrDefault(e => e.Payment.ReceiptKey == key);
        if (payment is not null && !existingDecision)
            throw new RewardDataException("payment-without-result-decision");
        RequireHistory(ledger, journal, approval, closures);
        foreach (var priorDecision in history.Where(d => d.RunId != run.RunId && d.Approval is not null))
        {
            var priorClaim = priorDecision.Approval!.Claim;
            if (!journal.Entries.Any(e => e.Phase == RewardJournalPhase.Committed && (ledger.Contains(e.Payment) || closures.ClosesPayment(e))
                && e.Payment.ReceiptKey == priorClaim.Key && e.Payment.Fingerprint == priorClaim.Fingerprint
                && e.Payment.Roubles == priorClaim.Roubles && e.Payment.Experience == priorClaim.Experience
                && e.BeforeRevision == priorDecision.Approval.CleanProfileRevision))
                throw new RewardDataException("earlier-result-not-settled");
        }
        if (profile.Revision != run.CleanProfileRevision)
        {
            // This exception recovers an already executed payment only. The
            // settlement adapter still forces a confirmation-only profile lease.
            if (approval is null || payment is null || payment.BeforeRevision != run.CleanProfileRevision
                || payment.Payment.Fingerprint != approval.Claim.Fingerprint || payment.Payment.Roubles != approval.Claim.Roubles
                || payment.Payment.Experience != approval.Claim.Experience
                || payment.Phase is not (RewardJournalPhase.Attempting or RewardJournalPhase.Committed)
                || !ledger.Contains(payment.Payment))
                throw new RewardDataException("result-clean-profile-changed");
        }

        var approvalPath = Path.Combine(directory, "approved.json");
        RewardStartupApproval? previousApproval = null;
        if (Exists(window.Root, approvalPath))
        {
            previousApproval = RewardStartupFiles.Read<RewardStartupApproval>(approvalPath, RewardStartupFiles.MaxApprovalBytes);
            if (previousApproval != approval)
            {
                // A slot can only advance past a positively committed prior
                // payment. An acknowledgement alone is not sufficient evidence.
                if (previousApproval.Schema != 1 || previousApproval.State != RewardStartupApprovalState.RestoredAndApproved
                    || previousApproval.Claim is null || previousApproval.Claim.Binding != run.Binding
                    || !RewardIdentity.IsHex(previousApproval.CleanProfileRevision, 64))
                    throw new RewardDataException("prior-approval-invalid");
                previousApproval.Claim.Validate(new());
                var prior = journal.Entries.SingleOrDefault(e => e.Payment.ReceiptKey == previousApproval.Claim.Key);
                if (prior is null || prior.Phase != RewardJournalPhase.Committed || !(ledger.Contains(prior.Payment) || closures.ClosesPayment(prior))
                    || prior.Payment.Fingerprint != previousApproval.Claim.Fingerprint
                    || prior.Payment.Roubles != previousApproval.Claim.Roubles
                    || prior.Payment.Experience != previousApproval.Claim.Experience
                    || prior.BeforeRevision != previousApproval.CleanProfileRevision)
                    throw new RewardDataException("prior-approval-unsettled");
                if (previousApproval.Claim.Key == approval?.Claim.Key) throw new RewardDataException("prior-approval-conflict");
            }
        }
        RequireUnchanged();
        if (!existingDecision)
        {
            RewardStartupFiles.WriteAtomic(window.Root, decisionPath, Json(decision));
            if (RewardStartupFiles.Read<RewardApprovalDecision>(decisionPath, RewardStartupFiles.MaxApprovalBytes) != decision)
                throw new IOException("Reward result decision was not durable.");
        }
        if (approval is not null && previousApproval != approval)
        {
            RequireUnchanged();
            RewardStartupFiles.WriteAtomic(window.Root, approvalPath, Json(approval));
            if (RewardStartupFiles.Read<RewardStartupApproval>(approvalPath, RewardStartupFiles.MaxApprovalBytes) != approval)
                throw new IOException("Reward approval was not durable.");
        }
        return evaluation;

        void RequireUnchanged()
        {
            window.RequireOpen();
            // Defence against an accidental out-of-contract coordinator. This is
            // NOT a lock against arbitrary editors/foreign profile-writing threads.
            if (RewardIdentity.Hash(Json(RewardStartupFiles.Read<RewardApprovalCandidate>(candidatePath))) != fingerprint
                || RewardStartupFiles.Read<RewardStartupEnrollment>(Path.Combine(directory, "enrollment.json"), 16384) != enrollment
                || new RewardProfileSnapshot(File.ReadAllText(profilePath)).Revision != profile.Revision
                || File.ReadAllText(journalPath) != journalJson)
                throw new RewardDataException("approval-input-changed");
        }
    }

    private static void RequireRestoration(RewardApprovalCandidate candidate)
    {
        var run = candidate.Authorization;
        var proof = candidate.Restoration;
        if (proof.Schema != 1 || proof.Binding != run.Binding || proof.RunId != run.RunId || proof.ArmToken != run.ArmToken
            || proof.NativeEndIdentity != run.NativeEndIdentity || proof.RestorationId != run.RestorationId
            || proof.State != RewardRestorationState.Persisted || proof.ExpectedProfileRevision != run.CleanProfileRevision
            || proof.PersistedProfileRevision != run.CleanProfileRevision)
            throw new RewardDataException("restoration-evidence-mismatch");
    }

    private static void RequireHistory(IReadOnlyList<RewardPayment> ledger, RewardJournal journal, RewardStartupApproval? current,
        RewardRecoveryClosureSet closures)
    {
        if (ledger.Any(p => !journal.Entries.Any(e => e.Payment == p))) throw new RewardDataException("approval-ledger-diverged");
        foreach (var entry in journal.Entries)
        {
            if (entry.Phase == RewardJournalPhase.Committed && ledger.Contains(entry.Payment)) continue;
            if (closures.ClosesPayment(entry)) continue;
            // Only the identical already-approved claim can finish its interrupted
            // transaction. Unrelated pending writes must be resolved first.
            if (current is null || entry.Payment.ReceiptKey != current.Claim.Key
                || entry.Payment.Fingerprint != current.Claim.Fingerprint || entry.Payment.Roubles != current.Claim.Roubles
                || entry.Payment.Experience != current.Claim.Experience
                || !RewardColaProfile.MatchesClaim(entry.Payment.Cola, current.Claim.Cola)
                || entry.BeforeRevision != current.CleanProfileRevision
                || entry.Phase == RewardJournalPhase.Committed)
                throw new RewardDataException("prior-payment-unsettled-or-diverged");
        }
    }

    internal static IReadOnlyList<RewardApprovalDecision> ReadDecisions(string root, RewardBinding binding, string directory)
    {
        // A crash before atomic rename leaves an unpublished staged file. It is
        // never authority or a gap; ignore only our exact sibling-temp naming.
        var paths = Directory.EnumerateFileSystemEntries(directory)
            .Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z"))
            .Take(4097).ToArray();
        if (paths.Length > 4096) throw new RewardDataException("result-decision-capacity");
        var history = new List<RewardApprovalDecision>();
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            var value = RewardStartupFiles.Read<RewardApprovalDecision>(path, RewardStartupFiles.MaxApprovalBytes);
            if (value.Schema != 1 || value.Binding != binding || !RewardIdentity.IsGuid(value.RunId)
                || Path.GetFileName(path) != value.RunId + ".json" || value.Sequence is < 1 or > 4096
                || !RewardIdentity.IsHex(value.SourceFingerprint, 64) || value.Evaluation is null
                || !Enum.IsDefined(value.Evaluation.Status)
                || value.Evaluation.Reason is null || !System.Text.RegularExpressions.Regex.IsMatch(value.Evaluation.Reason, "\\A[a-z0-9-]{1,80}\\z")
                || value.Evaluation.EligibleKills is < 0 or > RewardResultValidator.MaxLives || value.Evaluation.RoundWins is < 0 or > 100
                || value.Evaluation.Roubles is < 0 or > 10_000_000
                || value.Evaluation.Experience is < 0 or > 1_000_000
                || value.Evaluation.Status != RewardEvaluationStatus.Eligible && value.Evaluation.Experience != 0
                || (value.Evaluation.Status == RewardEvaluationStatus.Eligible) != (value.Evaluation.Roubles > 0 || value.Approval?.Claim?.Cola is not null)
                || value.Evaluation.Status == RewardEvaluationStatus.Rejected && (value.Evaluation.EligibleKills != 0 || value.Evaluation.RoundWins != 0)
                || (value.Evaluation.Status == RewardEvaluationStatus.Eligible) != (value.Approval is not null))
                throw new RewardDataException("invalid-result-history");
            if (value.Approval is { } approved)
            {
                if (approved.Schema != 1 || approved.State != RewardStartupApprovalState.RestoredAndApproved
                    || approved.Claim is null || approved.Claim.Binding != binding || approved.Claim.RunId != value.RunId
                    || approved.Claim.Sequence != value.Sequence || approved.Claim.Roubles != value.Evaluation.Roubles
                    || approved.Claim.Experience != value.Evaluation.Experience
                    || !RewardIdentity.IsHex(approved.CleanProfileRevision, 64)) throw new RewardDataException("invalid-result-history");
                approved.Claim.Validate(new());
            }
            history.Add(value);
        }
        if (!history.Select(d => d.Sequence).Order().SequenceEqual(Enumerable.Range(1, history.Count).Select(i => (long)i)))
            throw new RewardDataException("result-history-gap-or-duplicate");
        return history;
    }

    private static bool Exists(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path, allowMissingLeaf: true);
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson);
}
