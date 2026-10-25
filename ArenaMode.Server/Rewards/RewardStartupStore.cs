namespace ArenaMode.Server.Rewards;

internal sealed class RewardStartupStore(
    RewardStartupWindow window,
    RewardStartupEnrollment enrollment,
    RewardStartupApproval approval,
    RewardLimits limits) : IRewardSettlementStore
{
    private readonly RewardStartupWindow _window = window;
    private readonly RewardStartupEnrollment _enrollment = enrollment;
    private readonly RewardStartupApproval _approval = approval;
    private readonly RewardLimits _limits = limits;
    private readonly string _profilePath = Path.Combine(window.Root, "user", "profiles", enrollment.Binding.ProfileId + ".json");
    private readonly string _directory = Path.Combine(window.Root, RewardStartupFiles.RelativeDirectory, enrollment.Binding.ProfileId);
    private int _leased;

    public ValueTask<IRewardSettlementLease?> TryAcquireAsync(RewardBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_window.IsOpen || binding != _enrollment.Binding || Interlocked.CompareExchange(ref _leased, 1, 0) != 0)
            return ValueTask.FromResult<IRewardSettlementLease?>(null);
        try
        {
            if (_enrollment.Schema != 1 || !_enrollment.Enabled || _enrollment.Binding is null
                || !Path.IsPathFullyQualified(_enrollment.RuntimeRoot)
                || !string.Equals(RewardStartupFiles.Canonical(_enrollment.RuntimeRoot), _window.Root, RewardStartupFiles.PathComparison)
                || _approval.Schema != 1 || _approval.State != RewardStartupApprovalState.RestoredAndApproved
                || _approval.Claim is null || _approval.Claim.Binding != binding
                || !RewardIdentity.IsHex(_approval.CleanProfileRevision, 64))
                throw new RewardDataException("startup-approval-invalid");
            binding.Validate();
            _approval.Claim.Validate(_limits);
            var lease = new Lease(this);
            var profile = lease.ReadProfile();
            // Also verifies enrollment, unknown/duplicate fields and invalid flag.
            var ledger = RewardProfileDocument.ReadLedger(profile.Json, binding, _limits);
            RewardStartupFiles.RequireCleanProfile(profile.Json, binding);
            if (profile.Revision != _approval.CleanProfileRevision)
            {
                // This exception ONLY confirms a prior write. It can never mint a
                // new payment on a stale clean-profile approval.
                var journal = RewardSettlementCore.ParseJournal(lease.ReadJournal(), binding, _limits);
                var entry = journal.Entries.SingleOrDefault(e => e.Payment.ReceiptKey == _approval.Claim.Key);
                if (entry is null || entry.BeforeRevision != _approval.CleanProfileRevision
                    || entry.Payment.Fingerprint != _approval.Claim.Fingerprint
                    || entry.Phase is not (RewardJournalPhase.Attempting or RewardJournalPhase.Committed)
                    || !ledger.Contains(entry.Payment))
                    throw new RewardDataException("approved-clean-profile-changed");
                lease.RecoveryOnly = true;
            }
            return ValueTask.FromResult<IRewardSettlementLease?>(lease);
        }
        catch { Interlocked.Exchange(ref _leased, 0); throw; }
    }

    private sealed class Lease(RewardStartupStore store) : IRewardSettlementLease
    {
        private bool _disposed;
        internal bool RecoveryOnly { get; set; }
        private string JournalPath => Path.Combine(store._directory, "journal.json");
        private void Require(string path)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Lease));
            store._window.RequireOpen();
            RewardStartupFiles.RequireLocalPath(store._window.Root, path);
        }
        public RewardProfileSnapshot ReadProfile()
        {
            Require(store._profilePath);
            return new(File.ReadAllText(store._profilePath));
        }
        public string ReadJournal()
        {
            Require(JournalPath);
            return File.ReadAllText(JournalPath);
        }
        public void WriteJournal(string json)
        {
            Require(JournalPath);
            var before = RewardSettlementCore.ParseJournal(ReadJournal(), store._enrollment.Binding, store._limits);
            var after = RewardSettlementCore.ParseJournal(json, store._enrollment.Binding, store._limits);
            // The adapter accepts transitions only for its approved receipt. It
            // cannot be reused to submit a different claim through the core API.
            foreach (var entry in after.Entries)
            {
                if (before.Entries.Contains(entry)) continue;
                if (entry.Payment.ReceiptKey != store._approval.Claim.Key
                    || entry.Payment.Fingerprint != store._approval.Claim.Fingerprint
                    || entry.Payment.Roubles != store._approval.Claim.Roubles
                    || entry.Payment.Experience != store._approval.Claim.Experience
                    || !RewardColaProfile.MatchesClaim(entry.Payment.Cola, store._approval.Claim.Cola))
                    throw new RewardDataException("unapproved-journal-change");
            }
            if (before.Entries.Any(old => !after.Entries.Any(e => e.Payment == old.Payment)))
                throw new RewardDataException("journal-history-removal");
            RewardStartupFiles.WriteAtomic(store._window.Root, JournalPath, json);
        }
        public bool TryWriteProfile(string expectedRevision, string json)
        {
            Require(store._profilePath);
            if (RecoveryOnly) throw new RewardDataException("recovery-cannot-write-profile");
            if (expectedRevision != store._approval.CleanProfileRevision || ReadProfile().Revision != expectedRevision) return false;
            var before = RewardProfileDocument.ReadLedger(ReadProfile().Json, store._enrollment.Binding, store._limits);
            var after = RewardProfileDocument.ReadLedger(json, store._enrollment.Binding, store._limits);
            var added = after.Except(before).ToArray();
            if (after.Count != before.Count + 1 || before.Any(p => !after.Contains(p)) || added.Length != 1
                || added[0].ReceiptKey != store._approval.Claim.Key || added[0].Fingerprint != store._approval.Claim.Fingerprint
                || added[0].Roubles != store._approval.Claim.Roubles
                || added[0].Experience != store._approval.Claim.Experience
                || !RewardColaProfile.MatchesClaim(added[0].Cola, store._approval.Claim.Cola))
                throw new RewardDataException("unapproved-profile-payment");
            // Recompose from the exact durable base: reject any changes outside the
            // core's mail/ledger/counter edit, even from an accidental wrong caller.
            if (RewardProfileDocument.Apply(ReadProfile().Json, store._approval.Claim, added[0], store._limits) != json)
                throw new RewardDataException("unexpected-profile-edit");
            store._window.RequireOpen();
            RewardStartupFiles.WriteAtomic(store._window.Root, store._profilePath, json);
            return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Exchange(ref store._leased, 0);
        }
    }
}
