using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardStartupOutcome(string ProfileId, RewardSettlementResult Result, long Roubles);

internal static class RewardStartupProcessor
{
    internal static async Task<IReadOnlyList<RewardStartupOutcome>> ProcessAsync(RewardStartupWindow window, CancellationToken cancellationToken)
    {
        window.RequireOpen();
        var directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory);
        RewardStartupFiles.RequireLocalPath(window.Root, directory);
        var profiles = Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal).Take(65).ToArray();
        if (profiles.Length > 64) throw new RewardDataException("too-many-reward-enrollments");
        var results = new List<RewardStartupOutcome>();
        foreach (var profileDirectory in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(profileDirectory);
            if (!RewardIdentity.IsHex(id, 24)) continue;
            results.Add(await ProcessProfileAsync(window, id, cancellationToken));
        }
        return results;
    }

    internal static async Task<RewardStartupOutcome> ProcessProfileAsync(RewardStartupWindow window, string id, CancellationToken cancellationToken)
    {
        long roubles = 0;
        try
        {
            window.RequireOpen();
            if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("invalid-profile-directory");
            var directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory, id);
            var enrollmentPath = Path.Combine(directory, "enrollment.json");
            RewardStartupFiles.RequireLocalPath(window.Root, enrollmentPath);
            var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
            if (enrollment.Schema != 1 || enrollment.Binding is null || enrollment.Binding.ProfileId != id
                || string.IsNullOrWhiteSpace(enrollment.RuntimeRoot) || !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
                || !string.Equals(RewardStartupFiles.Canonical(enrollment.RuntimeRoot), window.Root, RewardStartupFiles.PathComparison))
                throw new RewardDataException("enrollment-origin-mismatch");
            enrollment.Binding.Validate();
            if (!enrollment.Enabled) return Outcome(RewardSettlementStatus.Deferred, "rewards-not-enabled");

            var approvalPath = Path.Combine(directory, "approved.json");
            RewardStartupFiles.RequireLocalPath(window.Root, approvalPath);
            var approval = RewardStartupFiles.Read<RewardStartupApproval>(approvalPath, 16384);
            if (approval.Schema != 1 || approval.Claim is null || approval.Claim.Binding != enrollment.Binding
                || !Enum.IsDefined(approval.State) || !RewardIdentity.IsHex(approval.CleanProfileRevision, 64))
                throw new RewardDataException("invalid-startup-approval");
            if (approval.State != RewardStartupApprovalState.RestoredAndApproved)
                return Outcome(RewardSettlementStatus.Deferred, "profile-restoration-not-approved");
            var limits = new RewardLimits();
            approval.Claim.Validate(limits);
            roubles = approval.Claim.Roubles;
            var store = new RewardStartupStore(window, enrollment, approval, limits);
            var result = await new RewardSettlementCore(store, limits).SettleAsync(approval.Claim, cancellationToken);
            if (result.Status is RewardSettlementStatus.Paid or RewardSettlementStatus.AlreadyPaid)
            {
                window.RequireOpen();
                var acknowledgement = new RewardStartupAcknowledgement(1, approval.Claim.Key, approval.Claim.Fingerprint, result.MessageId!);
                var json = JsonSerializer.Serialize(acknowledgement, RewardSettlementCore.JournalJson);
                var path = Path.Combine(directory, "acknowledged.json");
                RewardStartupFiles.WriteAtomic(window.Root, path, json);
                if (File.ReadAllText(path) != json) throw new IOException("Reward acknowledgement was not durable.");
            }
            return new(id, result, roubles);
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

        RewardStartupOutcome Outcome(RewardSettlementStatus status, string reason) => new(id, new(status, reason), roubles);
    }
}
