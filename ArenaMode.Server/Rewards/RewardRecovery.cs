using ArenaMode.Shared;
using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal static class RewardRecovery
{
    // Tests inject a crash AFTER a durable boundary; production never sets this.
    internal static Action<string>? Checkpoint { get; set; }
    internal sealed record Attempt(string Directory, RewardRecoveryRequestRecord Request,
        RewardRecoveryPlan? Plan, RewardRecoveryCompletion? Completion);

    internal static RewardRecoveryStatusResponse Preview(string root, string id)
    {
        try
        {
            root = RewardStartupFiles.Canonical(root);
            var review = RewardRecoveryEvidence.Review(root, id);
            var reviewId = Hash(review);
            var queued = ReadAttempts(root, id).Any(a => a.Completion is null);
            return new()
            {
                Available = review.Cases.Count > 0 || queued, CanClose = review.Cases.Count > 0 && !queued,
                Queued = queued, ReviewId = reviewId, Detail = queued ? "recovery-queued" : review.Cases.Count > 0 ? "recovery-review-ready" : "recovery-not-needed",
                Cases = review.Cases.Select(c => new RewardRecoveryCaseSummary
                {
                    Kind = c.Kind, ShortId = c.Id[..Math.Min(8, c.Id.Length)], AmountKnown = c.Payment is not null,
                    Roubles = c.Payment?.Payment.Roubles ?? 0, Experience = c.Payment?.Payment.Experience ?? 0
                }).ToList()
            };
        }
        catch (Exception e) when (Expected(e))
        {
            return new() { Detail = Reason(e), RequiresNormalSession = e is RewardDataException { Message: "recovery-normal-session-required" } };
        }
    }

    // Consent to this exact immutable review is queued. No current/native profile
    // write, consent toggle, history reset, or reward settlement can occur here.
    internal static RewardRecoveryResponse Request(string root, string id, string reviewId)
    {
        try
        {
            root = RewardStartupFiles.Canonical(root);
            if (!RewardIdentity.IsHex(reviewId, 64)) throw new RewardDataException("recovery-review-invalid");
            var review = RewardRecoveryEvidence.Review(root, id);
            if (Hash(review) != reviewId || review.Cases.Count == 0) throw new RewardDataException("recovery-review-stale");
            var attempts = ReadAttempts(root, id);
            if (attempts.Any(a => a.Completion is null && a.Request.ReviewId != reviewId)) throw new RewardDataException("recovery-request-pending");
            if (attempts.Count >= RewardRecoveryEvidence.Capacity && !attempts.Any(a => a.Request.ReviewId == reviewId))
                throw new RewardDataException("recovery-attempt-capacity");
            var directory = Path.Combine(DirectoryFor(root, id), reviewId);
            RewardRuntimeFiles.EnsureDirectory(root, directory);
            var request = new RewardRecoveryRequestRecord(1, reviewId, review);
            RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "request.json"), request);
            Checkpoint?.Invoke("request");
            return new() { Accepted = true, Reason = "recovery-queued", Status = Preview(root, id) };
        }
        catch (Exception e) when (Expected(e))
        { return new() { Reason = Reason(e), Status = Preview(root, id) }; }
    }

    internal static void ProcessAtStartup(RewardStartupWindow window, string id)
    {
        window.RequireOpen();
        var root = window.Root;
        foreach (var attempt in ReadAttempts(root, id).Where(a => a.Completion is null))
        {
            window.RequireOpen();
            var (enrollment, current, journal) = RewardRecoveryEvidence.ReadBase(root, id);
            var request = attempt.Request;
            if (request.Review.Binding != enrollment.Binding) throw new RewardDataException("recovery-binding-changed");
            var requestFingerprint = Hash(request);
            var marker = new RewardRecoveryMarker(request.ReviewId, requestFingerprint);
            var beforePath = Path.Combine(attempt.Directory, "before-profile.json");
            var planPath = Path.Combine(attempt.Directory, "plan.json");
            var plan = attempt.Plan;
            if (plan is null)
            {
                // Unrelated gameplay fields are deliberately outside the review.
                // The actual edit is nevertheless a CAS against the WHOLE latest
                // profile at the exclusive, pre-native-load startup boundary.
                if (Hash(RewardRecoveryEvidence.Review(root, id)) != request.ReviewId)
                {
                    Complete(root, attempt, "stale", "");
                    continue;
                }
                if (RewardRuntimeFiles.Exists(root, beforePath))
                {
                    var retained = ReadBackup(root, beforePath);
                    // A crash after backup cannot replace it with a newer profile.
                    // Stale recovery is reviewable again, never a rollback writer.
                    if (retained != current) { Complete(root, attempt, "stale", ""); continue; }
                }
                else
                {
                    RewardStartupFiles.WriteAtomic(root, beforePath, current);
                    if (ReadBackup(root, beforePath) != current) throw new IOException("Recovery backup was not durable.");
                    Checkpoint?.Invoke("backup");
                }
                var after = RewardProfileDocument.ApplyRecoveryMarker(current, enrollment.Binding, marker);
                plan = new(1, request.ReviewId, requestFingerprint, RewardIdentity.Hash(current), RewardIdentity.Hash(after));
                RewardRuntimeFiles.WriteNew(root, planPath, plan);
                Checkpoint?.Invoke("plan");
            }
            var before = ReadBackup(root, beforePath);
            if (RewardIdentity.Hash(before) != plan.BeforeRevision || plan.RequestFingerprint != requestFingerprint)
                throw new RewardDataException("recovery-plan-input-changed");
            var expected = RewardProfileDocument.ApplyRecoveryMarker(before, enrollment.Binding, marker);
            if (RewardIdentity.Hash(expected) != plan.AfterRevision
                || RewardProfileDocument.RecoveryFingerprint(before, enrollment.Binding) != request.Review.LedgerFingerprint)
                throw new RewardDataException("recovery-plan-invalid");
            var ownMarker = RewardProfileDocument.ReadRecoveryMarkers(current, enrollment.Binding).SingleOrDefault(m => m.ReviewId == request.ReviewId);
            if (ownMarker is null && (RewardIdentity.Hash(current) != plan.BeforeRevision
                || Hash(RewardRecoveryEvidence.Review(root, id)) != request.ReviewId))
            {
                // A failed startup may subsequently load/save normal progress.
                // Do not rebase the old plan, and do not strand the player: retire
                // this unacknowledged plan without editing the current profile.
                Complete(root, attempt with { Plan = plan }, "stale", Hash(plan));
                continue;
            }
            foreach (var item in request.Review.Cases) RewardRecoveryEvidence.RequireEvidence(root, enrollment.Binding, item, journal);
            // Even a prepared plan cannot close a newly introduced unrelated
            // conflict. Revalidate its original review using the untouched base.
            if (RewardIdentity.Hash(current) == plan.BeforeRevision)
            {
                window.RequireOpen();
                if (RewardRecoveryEvidence.ReadProfile(root, id) != current) throw new RewardDataException("recovery-profile-changed");
                RewardStartupFiles.WriteAtomic(root, Path.Combine(root, "user", "profiles", id + ".json"), expected);
                Checkpoint?.Invoke("profile");
                current = RewardRecoveryEvidence.ReadProfile(root, id);
                if (RewardIdentity.Hash(current) != plan.AfterRevision) throw new IOException("Recovery profile marker was not durable.");
                Checkpoint?.Invoke("readback");
            }
            else if (RewardProfileDocument.RecoveryFingerprint(current, enrollment.Binding)
                != RewardProfileDocument.RecoveryFingerprint(expected, enrollment.Binding))
                throw new RewardDataException("recovery-profile-write-outcome-unknown");
            // A native save after an interrupted acknowledgement may change other
            // gameplay fields. Identical owned metadata confirms ONLY this marker;
            // no profile/snapshot contents are copied back in that branch.
            window.RequireOpen();
            Complete(root, attempt with { Plan = plan }, "closed-without-payment", Hash(plan));
        }
    }

    internal static RewardRecoveryClosureSet ReadClosures(string root, string id, string profileJson, bool allowUnacknowledged = false)
    {
        var attempts = ReadAttempts(root, id);
        // Still validate unexpected markers when no sidecar is present.
        var enrollmentPath = Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "enrollment.json");
        if (!RewardRuntimeFiles.Exists(root, enrollmentPath))
        {
            if (attempts.Count > 0) throw new RewardDataException("recovery-enrollment-missing");
            return RewardRecoveryClosureSet.Empty;
        }
        var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
        if (enrollment.Schema != 1 || enrollment.Binding is null || enrollment.Binding.ProfileId != id)
            throw new RewardDataException("recovery-enrollment-invalid");
        var binding = enrollment.Binding;
        var markers = RewardProfileDocument.ReadRecoveryMarkers(profileJson, binding);
        if (attempts.Count == 0 && markers.Count == 0) return RewardRecoveryClosureSet.Empty;
        var journalPath = Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "journal.json");
        RewardStartupFiles.RequireLocalPath(root, journalPath);
        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(journalPath), binding, new());
        var active = new List<RewardRecoveryCase>();
        var missing = new List<RewardRecoveryCase>();
        foreach (var attempt in attempts)
        {
            var request = attempt.Request;
            if (request.Review.Binding != binding) throw new RewardDataException("recovery-binding-changed");
            var marker = markers.SingleOrDefault(m => m.ReviewId == request.ReviewId);
            if (attempt.Completion?.State != "closed-without-payment")
            {
                if (marker is not null) throw new RewardDataException("recovery-confirmation-incomplete");
                continue;
            }
            var plan = attempt.Plan ?? throw new RewardDataException("recovery-plan-missing");
            if (attempt.Completion.PlanFingerprint != Hash(plan) || plan.RequestFingerprint != Hash(request))
                throw new RewardDataException("recovery-confirmation-invalid");
            var original = ReadBackup(root, Path.Combine(attempt.Directory, "before-profile.json"));
            if (RewardIdentity.Hash(original) != plan.BeforeRevision
                || RewardProfileDocument.RecoveryFingerprint(original, binding) != request.Review.LedgerFingerprint
                || RewardIdentity.Hash(RewardProfileDocument.ApplyRecoveryMarker(original, binding,
                    new(request.ReviewId, plan.RequestFingerprint))) != plan.AfterRevision)
                throw new RewardDataException("recovery-confirmation-base-invalid");
            foreach (var item in request.Review.Cases) RewardRecoveryEvidence.RequireEvidence(root, binding, item, journal);
            if (marker is null) missing.AddRange(request.Review.Cases);
            else
            {
                if (marker.RequestFingerprint != Hash(request)) throw new RewardDataException("recovery-marker-conflict");
                active.AddRange(request.Review.Cases);
            }
        }
        if (markers.Any(m => !attempts.Any(a => a.Request.ReviewId == m.ReviewId && a.Completion?.State == "closed-without-payment")))
            throw new RewardDataException("recovery-marker-without-decision");
        // A lost acknowledgement never auto-reappears. A NEW explicit review may
        // acknowledge those exact cases again; then the old missing marker is
        // covered by that new confirmed decision, without changing old records.
        if (!allowUnacknowledged && missing.Any(c => !active.Any(a => a.Kind == c.Kind && a.Id == c.Id
            && a.ArmToken == c.ArmToken && a.Payment == c.Payment && c.Evidence.All(e => a.Evidence.Contains(e)))))
            throw new RewardDataException("recovery-profile-confirmation-lost");
        return new(binding, active);
    }

    // Checkpoint publication runs before recovery commit. It must leave an
    // explicitly requested generation untouched across a marker/commit crash gap
    // or lost marker. This result MUST NOT be used by the payment kernel/inbox.
    internal static RewardRecoveryClosureSet ReadCheckpointClosures(string root, string id, string profileJson)
    {
        var attempts = ReadAttempts(root, id);
        if (attempts.Count == 0) return RewardRecoveryClosureSet.Empty;
        var (enrollment, _, journal) = RewardRecoveryEvidence.ReadBase(root, id);
        _ = RewardProfileDocument.ReadRecoveryMarkers(profileJson, enrollment.Binding);
        var cases = new List<RewardRecoveryCase>();
        foreach (var attempt in attempts.Where(a => a.Completion?.State != "stale"))
        {
            if (attempt.Request.Review.Binding != enrollment.Binding) throw new RewardDataException("recovery-binding-changed");
            if (attempt.Completion?.State == "closed-without-payment")
                foreach (var item in attempt.Request.Review.Cases) RewardRecoveryEvidence.RequireEvidence(root, enrollment.Binding, item, journal);
            cases.AddRange(attempt.Request.Review.Cases);
        }
        return new(enrollment.Binding, cases);
    }

    internal static IReadOnlyList<Attempt> ReadAttempts(string root, string id)
    {
        if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("recovery-profile-invalid");
        var directory = DirectoryFor(root, id);
        if (!RewardRuntimeFiles.Exists(root, directory)) return [];
        var paths = Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal).Take(RewardRecoveryEvidence.Capacity + 1).ToArray();
        if (paths.Length > RewardRecoveryEvidence.Capacity) throw new RewardDataException("recovery-attempt-capacity");
        var result = new List<Attempt>();
        foreach (var path in paths)
        {
            RewardStartupFiles.RequireLocalPath(root, path);
            var reviewId = Path.GetFileName(path);
            if (!RewardIdentity.IsHex(reviewId, 64) || !Directory.Exists(path)) throw new RewardDataException("recovery-directory-invalid");
            var files = RewardRecoveryEvidence.Files(root, path, 4);
            if (files.Any(p => Path.GetFileName(p) is not ("request.json" or "plan.json" or "before-profile.json" or "completion.json")))
                throw new RewardDataException("recovery-unexpected-file");
            if (files.Length == 0) continue; // crash before the immutable request
            var request = RewardStartupFiles.Read<RewardRecoveryRequestRecord>(Path.Combine(path, "request.json"));
            if (request.Schema != 1 || request.ReviewId != reviewId || request.Review is null || request.Review.Schema != 1
                || request.Review.Binding is null || request.Review.Binding.ProfileId != id
                || request.Review.Cases is null || request.Review.Cases.Count is < 1 or > RewardRecoveryEvidence.Capacity
                || request.Review.Cases.Any(c => c is null || c.Id is null || c.Evidence is null
                    || c.Evidence.Any(e => e is null || e.RelativePath is null || !RewardIdentity.IsHex(e.Sha256, 64))
                    || c.Kind is not ("snapshot" or "run" or "payment"))
                || request.Review.Cases.Select(c => c.Kind + ":" + c.Id).Distinct(StringComparer.Ordinal).Count() != request.Review.Cases.Count
                || request.Review.PreviousAttempts is null || request.Review.PreviousAttempts.Count > RewardRecoveryEvidence.Capacity
                || request.Review.PreviousAttempts.Any(p => !RewardIdentity.IsHex(p, 64))
                || !RewardIdentity.IsHex(request.Review.LedgerFingerprint, 64) || Hash(request.Review) != reviewId)
                throw new RewardDataException("recovery-request-invalid");
            request.Review.Binding.Validate();
            var planPath = Path.Combine(path, "plan.json");
            var plan = RewardRuntimeFiles.Exists(root, planPath) ? RewardStartupFiles.Read<RewardRecoveryPlan>(planPath, 16384) : null;
            if (plan is not null && (plan.Schema != 1 || plan.ReviewId != reviewId || plan.RequestFingerprint != Hash(request)
                || !RewardIdentity.IsHex(plan.BeforeRevision, 64) || !RewardIdentity.IsHex(plan.AfterRevision, 64)))
                throw new RewardDataException("recovery-plan-invalid");
            var completionPath = Path.Combine(path, "completion.json");
            var completion = RewardRuntimeFiles.Exists(root, completionPath) ? RewardStartupFiles.Read<RewardRecoveryCompletion>(completionPath, 16384) : null;
            if (completion is not null && (completion.Schema != 1 || completion.ReviewId != reviewId
                || completion.State is not ("stale" or "closed-without-payment")
                || completion.State == "closed-without-payment" && (plan is null || completion.PlanFingerprint != Hash(plan))
                || completion.State == "stale" && completion.PlanFingerprint != (plan is null ? "" : Hash(plan))))
                throw new RewardDataException("recovery-completion-invalid");
            result.Add(new(path, request, plan, completion));
        }
        return result;
    }

    private static string ReadBackup(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        if (new FileInfo(path).Length > 67_108_864) throw new RewardDataException("recovery-backup-too-large");
        return File.ReadAllText(path);
    }

    private static void Complete(string root, Attempt attempt, string state, string planFingerprint)
    {
        RewardRuntimeFiles.WriteNew(root, Path.Combine(attempt.Directory, "completion.json"), new RewardRecoveryCompletion(1, attempt.Request.ReviewId, state, planFingerprint));
        Checkpoint?.Invoke("completion");
    }
    private static string DirectoryFor(string root, string id) => Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "recovery");
    internal static string Hash<T>(T value) => RewardIdentity.Hash(RewardRuntimeFiles.Json(value));
    private static bool Expected(Exception e) => e is RewardDataException or IOException or UnauthorizedAccessException or JsonException
        or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException;
    private static string Reason(Exception e) => e is RewardDataException ? e.Message : "recovery-evidence-unavailable";
}
