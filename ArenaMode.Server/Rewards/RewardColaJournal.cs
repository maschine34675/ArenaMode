using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArenaMode.Server.Compat;

namespace ArenaMode.Server.Rewards;

// This is ordinary-server authority, never a transferable private profile.
// The before image is read from the current NORMAL Cola document at startup.
internal sealed record RewardColaPlan(int Schema, RewardStartupApproval Approval,
    string RelativePath, bool BeforeExists, string BeforeJson, string AfterJson);
internal sealed record RewardColaDone(int Schema, string ReceiptKey, string ClaimFingerprint,
    string PlanFingerprint, string AfterRevision);

// A native payment may already exist. Starting profile writers now would destroy
// the evidence needed to finish its other document without applying it twice.
internal sealed class RewardColaRecoveryException(string reason, Exception? inner = null)
    : Exception(reason, inner);

internal static class RewardColaJournal
{
    private const int MaxPlans = 4096;
    private const int MaxPlanBytes = 2_500_000;
    private static readonly Regex PlanName = new("\\A[a-f0-9]{32}\\.json\\z", RegexOptions.CultureInvariant);
    private static readonly Regex DoneName = new("\\A[a-f0-9]{32}\\.done\\.json\\z", RegexOptions.CultureInvariant);
    private static readonly Regex TemporaryName = new("\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z", RegexOptions.CultureInvariant);

    internal static RewardColaPlan? Prepare(RewardStartupWindow window, RewardStartupEnrollment enrollment,
        RewardStartupApproval approval, Func<string, string>? folderResolver = null)
    {
        if (approval.Claim.Cola is null) return null;
        window.RequireOpen();
        if (!enrollment.Enabled) throw new RewardDataException("cola-enrollment-disabled");
        ValidateAuthority(window, enrollment, approval);
        var target = Target(window.Root, enrollment.Binding.ProfileId, folderResolver);
        var directory = DirectoryFor(window.Root, enrollment.Binding.ProfileId);
        RewardRuntimeFiles.EnsureDirectory(window.Root, directory);
        var path = PlanPath(directory, approval.Claim.RunId);
        if (RewardRuntimeFiles.Exists(window.Root, path))
        {
            var existing = ReadPlan(window, enrollment, path);
            if (existing.Approval != approval || !SameTarget(window.Root, existing, target))
                throw new RewardDataException("cola-plan-intent-conflict");
            if (NativePayment(window, enrollment, existing.Approval.Claim) is null)
            {
                if (RewardRuntimeFiles.Exists(window.Root, DonePath(directory, approval.Claim.RunId)))
                    throw new RewardColaRecoveryException("cola-done-without-native-payment");
                RequireBefore(window.Root, target, existing);
            }
            return existing;
        }
        // Never reconstruct a missing plan from the new current document: its
        // progress may already include the old payment whose ack was lost.
        if (NativePayment(window, enrollment, approval.Claim) is not null
            || RewardRuntimeFiles.Exists(window.Root, DonePath(directory, approval.Claim.RunId)))
            throw new RewardColaRecoveryException("cola-paid-plan-missing");
        var before = ReadForeign(window.Root, target);
        var after = RewardColaDelta.Merge(before.Json, approval.Claim.Cola);
        ArenaTarkovaColaState.ValidateState(after);
        var plan = new RewardColaPlan(1, approval, Path.GetRelativePath(window.Root, target), before.Exists, before.Json, after);
        ValidatePlan(window, enrollment, plan);
        if (Encoding.UTF8.GetByteCount(RewardRuntimeFiles.Json(plan)) > MaxPlanBytes)
            throw new RewardDataException("cola-plan-too-large");
        // Creating an empty data directory before payment is harmless; no foreign
        // progress is written until the native profile transaction is committed.
        RewardRuntimeFiles.EnsureDirectory(window.Root, Path.GetDirectoryName(target)!);
        RewardRuntimeFiles.WriteNew(window.Root, path, plan);
        if (ReadPlan(window, enrollment, path) != plan) throw new IOException("Cola plan read-back mismatch.");
        RequireBefore(window.Root, target, plan);
        return plan;
    }

    internal static void Complete(RewardStartupWindow window, RewardStartupEnrollment enrollment,
        RewardColaPlan? plan, Func<string, string>? folderResolver = null)
    {
        if (plan is null) return;
        try
        {
            window.RequireOpen();
            ValidatePlan(window, enrollment, plan);
            var directory = DirectoryFor(window.Root, enrollment.Binding.ProfileId);
            var persisted = ReadPlan(window, enrollment, PlanPath(directory, plan.Approval.Claim.RunId));
            if (persisted != plan) throw new RewardDataException("cola-plan-changed");
            RequireNativeCommitted(window, enrollment, plan.Approval);
            var done = DoneFor(plan);
            var donePath = DonePath(directory, plan.Approval.Claim.RunId);
            if (RewardRuntimeFiles.Exists(window.Root, donePath))
            {
                if (RewardStartupFiles.Read<RewardColaDone>(donePath, 16384) != done)
                    throw new RewardDataException("cola-done-conflict");
                return; // Ordinary progression after this commit is legitimate.
            }
            var target = Target(window.Root, enrollment.Binding.ProfileId, folderResolver);
            if (!SameTarget(window.Root, plan, target)) throw new RewardDataException("cola-plan-target-changed");
            var current = ReadForeign(window.Root, target);
            if (!current.Exists || current.Json != plan.AfterJson)
            {
                if (current.Exists != plan.BeforeExists || current.Json != plan.BeforeJson)
                    throw new RewardDataException("cola-progress-conflict-after-payment");
                window.RequireOpen();
                RewardRuntimeFiles.EnsureDirectory(window.Root, Path.GetDirectoryName(target)!);
                RequireBefore(window.Root, target, plan);
                RewardStartupFiles.WriteAtomic(window.Root, target, plan.AfterJson);
                var written = ReadForeign(window.Root, target);
                if (!written.Exists || written.Json != plan.AfterJson) throw new IOException("Cola progress read-back mismatch.");
            }
            window.RequireOpen();
            RewardRuntimeFiles.WriteNew(window.Root, donePath, done);
            if (RewardStartupFiles.Read<RewardColaDone>(donePath, 16384) != done)
                throw new IOException("Cola completion read-back mismatch.");
        }
        catch (RewardColaRecoveryException) { throw; }
        catch (Exception e)
        {
            throw new RewardColaRecoveryException("Tarkova Cola reward recovery is incomplete; retain the normal profile and Cola progress files and retry before starting the game.", e);
        }
    }

    // Invoked before consent/default-on logic. This finishes only evidence of an
    // ALREADY paid result, so switching OFF cannot strand half a committed reward.
    internal static async Task RecoverAsync(RewardStartupWindow window, string id,
        CancellationToken cancellationToken, Func<string, string>? folderResolver = null)
    {
        window.RequireOpen();
        cancellationToken.ThrowIfCancellationRequested();
        if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("cola-invalid-profile");
        var directory = DirectoryFor(window.Root, id);
        bool hasPlans;
        try { hasPlans = RewardRuntimeFiles.Exists(window.Root, directory); }
        catch (Exception e)
        {
            throw new RewardColaRecoveryException("Cola transaction history is inaccessible; it cannot be treated as absent.", e);
        }
        var profilePath = Path.Combine(window.Root, "user", "profiles", id + ".json");
        bool hasProfile;
        try { hasProfile = RewardRuntimeFiles.Exists(window.Root, profilePath); }
        catch (Exception e) when (hasPlans)
        {
            throw new RewardColaRecoveryException("The native profile for existing Cola plans is inaccessible.", e);
        }
        if (!hasProfile)
        {
            if (hasPlans) throw new RewardColaRecoveryException("cola-profile-missing-with-plans");
            return;
        }
        // No Cola history means this component adds no new failure mode to fresh
        // profiles or to the legacy RUB/XP-only enrollment path.
        bool safeUnpaid = !hasPlans;
        Exception? unpaidPlanFailure = null;
        try
        {
            var profile = ReadBounded(profilePath, 64 * 1024 * 1024);
            // Do not materialize arbitrary native objects merely to find our
            // extension: duplicate unrelated fields belong to normal profile
            // validation, not a fatal companion-recovery failure.
            bool hasCola = ContainsColaPayment(profile, journal: false);
            if (hasCola) safeUnpaid = false;
            var journalPath = Path.Combine(RewardRuntimeFiles.DirectoryFor(window.Root, id), "journal.json");
            string? journalText = RewardRuntimeFiles.Exists(window.Root, journalPath) ? ReadBounded(journalPath, 4_194_304) : null;
            // Also retain rollback evidence when BOTH the native ledger and
            // companion directory disappeared but the native journal survived.
            bool hasColaJournal = journalText is not null && ContainsColaPayment(journalText, journal: true);
            if (!hasPlans && !hasCola && !hasColaJournal) return;
            safeUnpaid = false;
            var enrollmentPath = Path.Combine(RewardRuntimeFiles.DirectoryFor(window.Root, id), "enrollment.json");
            RewardStartupFiles.RequireLocalPath(window.Root, enrollmentPath);
            var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
            if (enrollment.Binding is null || enrollment.Binding.ProfileId != id) throw new RewardDataException("cola-enrollment-mismatch");
            enrollment.Binding.Validate();
            var ledger = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new());
            RewardStartupFiles.RequireLocalPath(window.Root, journalPath);
            var journal = RewardSettlementCore.ParseJournal(journalText ?? throw new RewardDataException("cola-native-journal-missing"), enrollment.Binding, new());
            var paths = hasPlans ? Directory.EnumerateFileSystemEntries(directory).Take(MaxPlans * 3 + 1).ToArray() : [];
            // Only positively unpaid intent may be refused without stopping profile
            // loading. An Attempting journal can represent an interrupted native CAS.
            safeUnpaid = !hasCola && !paths.Any(p => DoneName.IsMatch(Path.GetFileName(p)))
                && !journal.Entries.Any(e => e.Payment.Cola is not null && e.Phase != RewardJournalPhase.Prepared);
            if (paths.Length > MaxPlans * 3) throw new RewardDataException("cola-plan-capacity");
            // Retain only small payment authority/checksums while inspecting
            // history; thousands of before/after documents must not stay in RAM.
            var plans = new Dictionary<string, PlanIndex>(StringComparer.Ordinal);
            var doneRuns = paths.Select(Path.GetFileName).Where(n => n is not null && DoneName.IsMatch(n))
                .Select(n => n![..32]).ToHashSet(StringComparer.Ordinal);
            var paidOrUncertainRuns = ledger.Select(p => p.ReceiptKey.Split(':')[0]).Concat(journal.Entries
                .Where(e => e.Phase != RewardJournalPhase.Prepared).Select(e => e.Payment.ReceiptKey.Split(':')[0]))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                RewardStartupFiles.RequireLocalPath(window.Root, path);
                var name = Path.GetFileName(path);
                if (TemporaryName.IsMatch(name)) continue;
                if (DoneName.IsMatch(name)) continue;
                if (!PlanName.IsMatch(name)) throw new RewardDataException("cola-unknown-plan-file");
                try
                {
                    var plan = ReadPlan(window, enrollment, path);
                    if (plan.Approval.Claim.RunId != name[..32]
                        || !plans.TryAdd(plan.Approval.Claim.Key, new(path, plan.Approval, DoneFor(plan))))
                        throw new RewardDataException("cola-plan-identity-conflict");
                }
                catch (Exception e) when (!paidOrUncertainRuns.Contains(name[..32]) && !doneRuns.Contains(name[..32]))
                {
                    // A new unpaid intent must not turn into a fatal error just
                    // because older, completed Cola history exists. Still inspect
                    // ALL paid history before returning this ordinary refusal.
                    unpaidPlanFailure ??= e;
                }
            }
            var planRuns = plans.Values.Select(p => p.Approval.Claim.RunId).ToHashSet(StringComparer.Ordinal);
            if (plans.Count > MaxPlans || !doneRuns.IsSubsetOf(planRuns))
                throw new RewardDataException("cola-plan-history-incomplete");
            var payments = ledger.ToDictionary(p => p.ReceiptKey, StringComparer.Ordinal);
            var nativeEntries = journal.Entries.ToDictionary(e => e.Payment.ReceiptKey, StringComparer.Ordinal);
            bool incompleteCola = plans.Values.Any(p => payments.ContainsKey(p.Approval.Claim.Key)
                && (!doneRuns.Contains(p.Approval.Claim.RunId)
                    || !nativeEntries.TryGetValue(p.Approval.Claim.Key, out var entry)
                    || entry.Phase != RewardJournalPhase.Committed));
            // An independently confirmed old RUB/XP closure stays terminal
            // during recovery of a newer Cola transaction. Never exempt Cola
            // payments themselves or a merely queued recovery request.
            var closures = incompleteCola ? RewardRecovery.ReadClosures(window.Root, id, profile) : RewardRecoveryClosureSet.Empty;
            foreach (var payment in ledger.Where(p => incompleteCola || p.Cola is not null))
                if (!nativeEntries.TryGetValue(payment.ReceiptKey, out var entry) || entry.Payment != payment)
                    throw new RewardDataException("cola-native-history-disagrees");
            foreach (var entry in journal.Entries.Where(e => incompleteCola || e.Payment.Cola is not null))
                if (entry.Phase != RewardJournalPhase.Prepared && !payments.ContainsKey(entry.Payment.ReceiptKey)
                    && !(entry.Payment.Cola is null && closures.ClosesPayment(entry)))
                    throw new RewardDataException("cola-native-payment-ambiguous-or-rolled-back");
            foreach (var payment in ledger.Where(p => p.Cola is not null))
                if (!plans.TryGetValue(payment.ReceiptKey, out var plan) || payment.Fingerprint != plan.Approval.Claim.Fingerprint)
                    throw new RewardDataException("cola-paid-plan-missing");
            foreach (var plan in plans.Values.OrderBy(p => p.Approval.Claim.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var claim = plan.Approval.Claim;
                payments.TryGetValue(claim.Key, out var payment);
                if (payment is null)
                {
                    if (doneRuns.Contains(claim.RunId)) throw new RewardDataException("cola-committed-native-history-missing");
                    // No payment: do not apply progress or grant a new native
                    // reward here. The ordinary enabled startup path owns that.
                    continue;
                }
                if (payment.Fingerprint != claim.Fingerprint || payment.Cola is null
                    || payment.Roubles != claim.Roubles || payment.Experience != claim.Experience
                    || !RewardColaProfile.MatchesClaim(payment.Cola, claim.Cola))
                    throw new RewardDataException("cola-native-payment-conflict");
                var nativeEntry = nativeEntries[claim.Key];
                if (nativeEntry.BeforeRevision != plan.Approval.CleanProfileRevision)
                    throw new RewardDataException("cola-native-approval-history-conflict");
                if (nativeEntry.Phase != RewardJournalPhase.Committed)
                {
                    var beforeNative = ReadBounded(profilePath, 64 * 1024 * 1024);
                    var store = new RewardStartupStore(window, enrollment with { Enabled = true }, plan.Approval, new());
                    var result = await new RewardSettlementCore(new ConfirmationOnlyStore(store), new()).SettleAsync(claim, cancellationToken);
                    if (result.Status != RewardSettlementStatus.AlreadyPaid || ReadBounded(profilePath, 64 * 1024 * 1024) != beforeNative)
                        throw new RewardDataException("cola-native-payment-not-confirmed");
                    // Only the original approval may confirm its native transition.
                    // Historical committed Cola work must not attempt to recover a
                    // different, ordinary RUB-only payment through this authority.
                    journal = RewardSettlementCore.ParseJournal(ReadBounded(journalPath, 4_194_304), enrollment.Binding, new());
                    nativeEntries = journal.Entries.ToDictionary(e => e.Payment.ReceiptKey, StringComparer.Ordinal);
                }
                if (!nativeEntries.TryGetValue(claim.Key, out var committed) || committed.Phase != RewardJournalPhase.Committed
                    || committed.Payment != payment || committed.BeforeRevision != plan.Approval.CleanProfileRevision)
                    throw new RewardDataException("cola-native-approval-history-conflict");
                if (doneRuns.Contains(claim.RunId))
                {
                    if (RewardStartupFiles.Read<RewardColaDone>(DonePath(directory, claim.RunId), 16384) != plan.Done)
                        throw new RewardDataException("cola-done-conflict");
                    continue; // Ordinary progress since this completed plan is valid.
                }
                var pendingPlan = ReadPlan(window, enrollment, plan.Path);
                if (DoneFor(pendingPlan) != plan.Done) throw new RewardDataException("cola-plan-changed-during-recovery");
                Complete(window, enrollment, pendingPlan, folderResolver);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RewardColaRecoveryException) { throw; }
        catch (Exception e)
        {
            // Preserve ordinary startup's retry classification for a locked or
            // temporarily inaccessible legacy reward file with no paid Cola work.
            if (safeUnpaid && e is (IOException or UnauthorizedAccessException)) throw;
            if (safeUnpaid) throw new RewardDataException("cola-unpaid-plan-invalid: " + e.GetType().Name);
            throw new RewardColaRecoveryException("Tarkova Cola reward history needs recovery before profiles can load; retain the native profile, Cola progress and transaction files.", e);
        }
        if (unpaidPlanFailure is IOException or UnauthorizedAccessException) throw unpaidPlanFailure;
        if (unpaidPlanFailure is not null) throw new RewardDataException("cola-unpaid-plan-invalid: " + unpaidPlanFailure.GetType().Name);
    }

    private static void ValidateAuthority(RewardStartupWindow window, RewardStartupEnrollment enrollment, RewardStartupApproval approval)
    {
        window.RequireOpen();
        if (enrollment.Schema != 1 || enrollment.Binding is null || !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
            || !RewardRuntimeFiles.SameRoot(window.Root, enrollment.RuntimeRoot)
            || approval.Schema != 1 || approval.State != RewardStartupApprovalState.RestoredAndApproved
            || approval.Claim is null || approval.Claim.Binding != enrollment.Binding || approval.Claim.Cola is null
            || !RewardIdentity.IsHex(approval.CleanProfileRevision, 64)) throw new RewardDataException("cola-plan-authority-invalid");
        approval.Claim.Validate(new());
    }

    private static bool ContainsColaPayment(string json, bool journal)
    {
        // JsonDocument decodes escaped property names without constructing or
        // validating unrelated native dictionaries. Inspect every matching
        // property so duplicate enclosing names cannot hide transaction evidence;
        // full strict native validation follows once any Cola history is found.
        try
        {
            using var document = JsonDocument.Parse(json);
            static IEnumerable<JsonElement> Members(JsonElement parent, string name)
                => parent.ValueKind == JsonValueKind.Object
                    ? parent.EnumerateObject().Where(p => p.NameEquals(name)).Select(p => p.Value)
                    : [];
            var roots = journal ? new[] { document.RootElement }
                : Members(document.RootElement, RewardProfileDocument.ExtensionKey);
            foreach (var root in roots)
                foreach (var array in Members(root, journal ? "entries" : "payments"))
                    if (array.ValueKind == JsonValueKind.Array)
                        foreach (var entry in array.EnumerateArray())
                            foreach (var payment in journal ? Members(entry, "payment") : new[] { entry })
                                if (Members(payment, "cola").Any()) return true;
            return false;
        }
        catch (JsonException)
        {
            // With existing plans/the other native document as evidence, strict
            // parsing below refuses recovery. Without ANY Cola evidence, native
            // profile/reward validation retains ownership of an unrelated file.
            return false;
        }
    }
    private static void ValidatePlan(RewardStartupWindow window, RewardStartupEnrollment enrollment, RewardColaPlan plan)
    {
        if (plan.Schema != 1 || plan.Approval is null || plan.RelativePath is null || plan.BeforeJson is null || plan.AfterJson is null)
            throw new RewardDataException("cola-plan-invalid");
        ValidateAuthority(window, enrollment, plan.Approval);
        if (Path.IsPathRooted(plan.RelativePath) || plan.RelativePath.Split('/', '\\').Any(p => p is "" or "." or "..")
            || !plan.RelativePath.Replace('\\', '/').StartsWith("user/mods/", StringComparison.Ordinal)
            || !plan.RelativePath.Replace('\\', '/').EndsWith("/profiles/" + enrollment.Binding.ProfileId + ".json", StringComparison.Ordinal))
            throw new RewardDataException("cola-plan-path-invalid");
        ArenaTarkovaColaState.ValidateState(plan.BeforeJson);
        ArenaTarkovaColaState.ValidateState(plan.AfterJson);
        if (!plan.BeforeExists && plan.BeforeJson != "{}") throw new RewardDataException("cola-missing-before-image-invalid");
        if (RewardColaDelta.Merge(plan.BeforeJson, plan.Approval.Claim.Cola!) != plan.AfterJson)
            throw new RewardDataException("cola-plan-delta-mismatch");
    }
    private static RewardColaPlan ReadPlan(RewardStartupWindow window, RewardStartupEnrollment enrollment, string path)
    {
        RewardStartupFiles.RequireLocalPath(window.Root, path);
        var plan = RewardStartupFiles.Read<RewardColaPlan>(path, MaxPlanBytes);
        ValidatePlan(window, enrollment, plan);
        return plan;
    }
    private static RewardPayment? NativePayment(RewardStartupWindow window, RewardStartupEnrollment enrollment, RewardClaim claim)
    {
        var path = Path.Combine(window.Root, "user", "profiles", enrollment.Binding.ProfileId + ".json");
        RewardStartupFiles.RequireLocalPath(window.Root, path);
        var profile = ReadBounded(path, 64 * 1024 * 1024);
        var payment = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new()).SingleOrDefault(p => p.ReceiptKey == claim.Key);
        if (payment is not null && (payment.Fingerprint != claim.Fingerprint || payment.Roubles != claim.Roubles
            || payment.Experience != claim.Experience || payment.Cola is null
            || !RewardColaProfile.MatchesClaim(payment.Cola, claim.Cola))) throw new RewardDataException("cola-native-payment-conflict");
        return payment;
    }
    private static void RequireNativeCommitted(RewardStartupWindow window, RewardStartupEnrollment enrollment, RewardStartupApproval approval)
    {
        var payment = NativePayment(window, enrollment, approval.Claim) ?? throw new RewardDataException("cola-native-payment-absent");
        var path = Path.Combine(RewardRuntimeFiles.DirectoryFor(window.Root, enrollment.Binding.ProfileId), "journal.json");
        RewardStartupFiles.RequireLocalPath(window.Root, path);
        var journal = RewardSettlementCore.ParseJournal(ReadBounded(path, 64 * 1024 * 1024), enrollment.Binding, new());
        if (!journal.Entries.Any(e => e.Phase == RewardJournalPhase.Committed && e.Payment == payment
            && e.BeforeRevision == approval.CleanProfileRevision))
            throw new RewardDataException("cola-native-payment-not-committed");
    }
    private static string Target(string root, string id, Func<string, string>? resolver)
    {
        var folder = (resolver ?? ArenaTarkovaColaState.ResolveFolder)(root);
        RewardStartupFiles.RequireLocalPath(root, folder);
        var mods = RewardStartupFiles.Canonical(Path.Combine(root, "user", "mods")) + Path.DirectorySeparatorChar;
        if (!RewardStartupFiles.Canonical(folder).StartsWith(mods, RewardStartupFiles.PathComparison)) throw new RewardDataException("cola-folder-outside-mods");
        return Path.Combine(folder, "profiles", id + ".json");
    }
    private static bool SameTarget(string root, RewardColaPlan plan, string target)
        => string.Equals(Path.GetFullPath(Path.Combine(root, plan.RelativePath)), Path.GetFullPath(target), RewardStartupFiles.PathComparison);
    private static (bool Exists, string Json) ReadForeign(string root, string path)
    {
        if (!RewardRuntimeFiles.Exists(root, path)) return (false, "{}");
        var value = ReadBounded(path, ArenaTarkovaColaState.MaxStateBytes);
        ArenaTarkovaColaState.ValidateState(value);
        return (true, value);
    }
    private static void RequireBefore(string root, string path, RewardColaPlan plan)
    {
        var current = ReadForeign(root, path);
        if (current.Exists != plan.BeforeExists || current.Json != plan.BeforeJson) throw new RewardDataException("cola-prepared-progress-changed");
    }
    private static string ReadBounded(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) throw new RewardDataException("cola-state-too-large");
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static RewardColaDone DoneFor(RewardColaPlan plan) => new(1, plan.Approval.Claim.Key,
        plan.Approval.Claim.Fingerprint, RewardIdentity.Hash(RewardRuntimeFiles.Json(plan)), RewardIdentity.Hash(plan.AfterJson));
    private static string DirectoryFor(string root, string id) => Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "cola-plans");
    private static string PlanPath(string directory, string run) => Path.Combine(directory, run + ".json");
    private static string DonePath(string directory, string run) => Path.Combine(directory, run + ".done.json");
    private sealed record PlanIndex(string Path, RewardStartupApproval Approval, RewardColaDone Done);

    private sealed class ConfirmationOnlyStore(IRewardSettlementStore inner) : IRewardSettlementStore
    {
        public async ValueTask<IRewardSettlementLease?> TryAcquireAsync(RewardBinding binding, CancellationToken token)
        {
            var lease = await inner.TryAcquireAsync(binding, token);
            return lease is null ? null : new ConfirmationOnlyLease(lease);
        }
    }
    private sealed class ConfirmationOnlyLease(IRewardSettlementLease inner) : IRewardSettlementLease, IRewardRecoveryLease
    {
        public RewardProfileSnapshot ReadProfile() => inner.ReadProfile();
        public string ReadJournal() => inner.ReadJournal();
        public RewardRecoveryClosureSet ReadRecoveryClosures(string profileJson)
            => inner is IRewardRecoveryLease recovery ? recovery.ReadRecoveryClosures(profileJson) : RewardRecoveryClosureSet.Empty;
        public void WriteJournal(string json) => inner.WriteJournal(json);
        public bool TryWriteProfile(string expectedRevision, string json) => throw new RewardDataException("cola-recovery-cannot-pay");
        public void Dispose() => inner.Dispose();
    }
}
