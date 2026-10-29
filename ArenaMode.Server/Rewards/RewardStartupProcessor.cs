using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardStartupOutcome(string ProfileId, RewardSettlementResult Result, long Roubles, long Experience = 0,
    RewardAuthorityNotice? Authority = null);

internal static class RewardStartupProcessor
{
    internal static async Task<IReadOnlyList<RewardStartupOutcome>> ProcessAsync(RewardStartupWindow window, CancellationToken cancellationToken)
    {
        window.RequireOpen();
        var directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory);
        RewardStartupFiles.RequireLocalPath(window.Root, directory);
        var profiles = Directory.GetDirectories(directory).Where(p => RewardIdentity.IsHex(Path.GetFileName(p), 24))
            .OrderBy(p => p, StringComparer.Ordinal).Take(65).ToArray();
        if (profiles.Length > 64) throw new RewardDataException("too-many-reward-enrollments");
        var ids = profiles.Select(Path.GetFileName).Where(id => RewardIdentity.IsHex(id, 24))
            .Select(id => id!).Union(RewardStartupDefaults.ProfileIds(window), StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).Take(65).ToArray();
        if (ids.Length > 64) throw new RewardDataException("too-many-reward-enrollments");
        var results = new List<RewardStartupOutcome>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessProfileAsync(window, id, cancellationToken, applyDefault: true));
        }
        return results;
    }

    internal static async Task<RewardStartupOutcome> ProcessProfileAsync(RewardStartupWindow window, string id, CancellationToken cancellationToken,
        bool includeRuntimeInbox = true, bool applyDefault = false)
    {
        long roubles = 0;
        long experience = 0;
        RewardAuthorityNotice? authority = null;
        try
        {
            if (cancellationToken.IsCancellationRequested) return Outcome(RewardSettlementStatus.Deferred, "cancelled");
            window.RequireOpen();
            if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("invalid-profile-directory");
            // An enrollment the profile or the install location no longer backs is settled FIRST: every later
            // step reads the ledger inside the profile and would only report it as damaged. A nested pass
            // (includeRuntimeInbox false) belongs to a top-level call that already did this.
            if (includeRuntimeInbox) authority = RewardAuthorityRecovery.Reconcile(window, id);
            // Finish an already committed payment's companion write even if the
            // player has since opted out. This path cannot create a new payment.
            var cola = await RewardColaJournal.RecoverAsync(window, id, cancellationToken);
            // The Cola history was kept because the native side is gone. The authority recovery reports a lost
            // reward block itself; this covers the states it does not act on (no consent file, a legacy
            // enrollment) so that the player still reads, once, why rewards stay off. A deleted profile file has
            // no authority to report and stays a Debug line.
            if (includeRuntimeInbox && authority is null && cola == RewardColaRecoveryResult.KeptProfileLostRewardBlock)
                authority = new(false, "profile-reward-block-missing", "its reward records hold Tarkova Cola history, which stays unchanged", null);
            if (includeRuntimeInbox)
            {
                RewardRuntimeInbox.Preflight(window.Root, id);
                RewardRestorationCheckpoint.RecoverAtStartup(window, id);
                RewardRecovery.DiscardUncertainAtStartup(window, id);
                // A stale pre-upgrade discard request may have shielded a now
                // proven checkpoint on the first pass. Finish it this startup.
                RewardRestorationCheckpoint.RecoverAtStartup(window, id);
            }
            if (applyDefault) RewardStartupDefaults.EnableIfNew(window, id);
            var directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory, id);
            var enrollmentPath = Path.Combine(directory, "enrollment.json");
            var runtimeManaged = RewardRuntimeFiles.Exists(window.Root, enrollmentPath)
                && RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384).RuntimeManaged;
            var hasRuntimeConsent = RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "consent.json"));
            if (!hasRuntimeConsent && (runtimeManaged || RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "enrollment-plan.json"))
                || RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "handoff.json"))
                || RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "delegations"))
                || RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "allocations"))))
                throw new RewardDataException("runtime-reward-consent-missing");
            if (hasRuntimeConsent)
            {
                if (!RewardEnrollment.WantsRewards(window.Root, id)) return Outcome(RewardSettlementStatus.Deferred, "rewards-not-enabled");
                RewardEnrollment.EnsureAtStartup(window, id);
            }
            RewardStartupFiles.RequireLocalPath(window.Root, enrollmentPath);
            var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
            if (enrollment.Schema != 1 || enrollment.Binding is null || enrollment.Binding.ProfileId != id
                || string.IsNullOrWhiteSpace(enrollment.RuntimeRoot) || !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
                || !string.Equals(RewardStartupFiles.Canonical(enrollment.RuntimeRoot), window.Root, RewardStartupFiles.PathComparison))
                throw new RewardDataException("enrollment-origin-mismatch");
            enrollment.Binding.Validate();
            if (!enrollment.Enabled) return Outcome(RewardSettlementStatus.Deferred, "rewards-not-enabled");

            if (includeRuntimeInbox && hasRuntimeConsent)
            {
                var queued = await RewardRuntimeInbox.ProcessAsync(window, enrollment, cancellationToken,
                    () => ProcessProfileAsync(window, id, cancellationToken, includeRuntimeInbox: false));
                if (queued is not null) return queued with { Authority = authority };
            }
            if (hasRuntimeConsent && !RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "candidate.json"))
                && !RewardRuntimeFiles.Exists(window.Root, Path.Combine(directory, "approved.json")))
                return Outcome(RewardSettlementStatus.Deferred, "rewards-enrolled-no-pending-result");

            var closures = RewardRecovery.ReadClosures(window.Root, id, RewardRecoveryEvidence.ReadProfile(window.Root, id));
            var candidatePath = Path.Combine(directory, "candidate.json");
            if (RewardRuntimeFiles.Exists(window.Root, candidatePath))
            {
                var candidate = RewardStartupFiles.Read<RewardApprovalCandidate>(candidatePath);
                RewardRuntimeFiles.RequireProof(candidate);
                if (closures.ClosesReceipt(candidate)
                    || RewardRestorationCheckpoint.IsDiscardedRun(window.Root, candidate.Authorization.Binding, candidate.Authorization.RunId))
                    return Outcome(RewardSettlementStatus.ClosedWithoutPayment, "reward-closed-without-payment");
            }
            var evaluation = RewardApprovalProducer.PrepareIfPresent(window, enrollment);
            if (evaluation is { Status: RewardEvaluationStatus.Rejected })
                return Outcome(RewardSettlementStatus.Rejected, evaluation.Reason);
            if (evaluation is { Status: RewardEvaluationStatus.NoReward })
                return Outcome(RewardSettlementStatus.Deferred, evaluation.Reason);

            var approvalPath = Path.Combine(directory, "approved.json");
            RewardStartupFiles.RequireLocalPath(window.Root, approvalPath);
            var approval = RewardStartupFiles.Read<RewardStartupApproval>(approvalPath, RewardStartupFiles.MaxApprovalBytes);
            if (approval.Schema != 1 || approval.Claim is null || approval.Claim.Binding != enrollment.Binding
                || !Enum.IsDefined(approval.State) || !RewardIdentity.IsHex(approval.CleanProfileRevision, 64))
                throw new RewardDataException("invalid-startup-approval");
            if (approval.State != RewardStartupApprovalState.RestoredAndApproved)
                return Outcome(RewardSettlementStatus.Deferred, "profile-restoration-not-approved");
            var limits = new RewardLimits();
            approval.Claim.Validate(limits);
            if (closures.ClosesClaim(approval.Claim)
                || RewardRestorationCheckpoint.IsDiscardedRun(window.Root, approval.Claim.Binding, approval.Claim.RunId))
                return Outcome(RewardSettlementStatus.ClosedWithoutPayment, "reward-closed-without-payment");
            roubles = approval.Claim.Roubles;
            experience = approval.Claim.Experience;
            var store = new RewardStartupStore(window, enrollment, approval, limits);
            var colaPlan = RewardColaJournal.Prepare(window, enrollment, approval);
            var result = await new RewardSettlementCore(store, limits).SettleAsync(approval.Claim, cancellationToken);
            if (result.Status is RewardSettlementStatus.Paid or RewardSettlementStatus.AlreadyPaid)
            {
                window.RequireOpen();
                if (colaPlan is not null) RewardColaJournal.Complete(window, enrollment, colaPlan);
                var acknowledgement = new RewardStartupAcknowledgement(1, approval.Claim.Key, approval.Claim.Fingerprint, result.MessageId!);
                var json = JsonSerializer.Serialize(acknowledgement, RewardSettlementCore.JournalJson);
                var path = Path.Combine(directory, "acknowledged.json");
                RewardStartupFiles.WriteAtomic(window.Root, path, json);
                if (File.ReadAllText(path) != json) throw new IOException("Reward acknowledgement was not durable.");
            }
            return new(id, result, roubles, experience, authority);
        }
        catch (FileNotFoundException) { return Outcome(RewardSettlementStatus.Quarantined, "enrolled-state-missing"); }
        catch (DirectoryNotFoundException) { return Outcome(RewardSettlementStatus.Quarantined, "enrolled-state-missing"); }
        catch (IOException) { return Outcome(RewardSettlementStatus.Deferred, "startup-storage-unavailable"); }
        catch (UnauthorizedAccessException) { return Outcome(RewardSettlementStatus.Deferred, "startup-storage-unavailable"); }
        catch (JsonException) { return Outcome(RewardSettlementStatus.Quarantined, "invalid-startup-state"); }
        catch (RewardDataException e) { return Outcome(RewardSettlementStatus.Quarantined, e.Message); }
        catch (InvalidOperationException) { return Outcome(RewardSettlementStatus.Quarantined, "invalid-startup-state"); }
        catch (FormatException) { return Outcome(RewardSettlementStatus.Quarantined, "invalid-startup-state"); }
        catch (ArgumentException) { return Outcome(RewardSettlementStatus.Quarantined, "invalid-startup-state"); }

        RewardStartupOutcome Outcome(RewardSettlementStatus status, string reason) => new(id, new(status, reason), roubles, experience, authority);
    }
}
