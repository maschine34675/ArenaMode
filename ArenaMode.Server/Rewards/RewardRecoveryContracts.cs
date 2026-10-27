namespace ArenaMode.Server.Rewards;

// Internal diagnostics and migration tests only; no client recovery protocol.
internal sealed class RewardRecoveryCaseSummary
{
    public string Kind { get; set; } = "";
    public string ShortId { get; set; } = "";
    public bool AmountKnown { get; set; }
    public long Roubles { get; set; }
    public long Experience { get; set; }
}
internal sealed class RewardRecoveryStatusResponse
{
    public bool Available { get; set; }
    public bool CanClose { get; set; }
    public bool Queued { get; set; }
    public bool RequiresNormalSession { get; set; }
    public string ReviewId { get; set; } = "";
    public string Detail { get; set; } = "";
    public List<RewardRecoveryCaseSummary> Cases { get; set; } = [];
}
internal sealed class RewardRecoveryResponse
{
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
    public RewardRecoveryStatusResponse Status { get; set; } = new();
}

// A closure is an explicit refusal to pay an old operation again. It is never
// a payment, a reconstruction of missing history, or proof of restoration.
internal sealed record RewardRecoveryEvidenceFile(string RelativePath, string Sha256);
internal sealed record RewardRecoveryCase(string Kind, string Id, string? ArmToken,
    RewardJournalEntry? Payment, IReadOnlyList<RewardRecoveryEvidenceFile> Evidence);
internal sealed record RewardRecoveryReview(int Schema, RewardBinding Binding, string LedgerFingerprint,
    IReadOnlyList<string> PreviousAttempts, IReadOnlyList<RewardRecoveryCase> Cases);
internal sealed record RewardRecoveryRequestRecord(int Schema, string ReviewId, RewardRecoveryReview Review);
internal sealed record RewardRecoveryPlan(int Schema, string ReviewId, string RequestFingerprint,
    string BeforeRevision, string AfterRevision);
internal sealed record RewardRecoveryCompletion(int Schema, string ReviewId, string State, string PlanFingerprint);
internal sealed record RewardRecoveryMarker(string ReviewId, string RequestFingerprint);

// Only the startup store supplies these validated decisions to the kernel.
// Synthetic/unrelated stores retain the original strict history behavior.
internal interface IRewardRecoveryLease
{
    RewardRecoveryClosureSet ReadRecoveryClosures(string profileJson);
}

internal sealed class RewardRecoveryClosureSet
{
    internal static readonly RewardRecoveryClosureSet Empty = new(null, []);
    private readonly RewardBinding? _binding;
    private readonly IReadOnlyList<RewardRecoveryCase> _cases;
    internal RewardRecoveryClosureSet(RewardBinding? binding, IReadOnlyList<RewardRecoveryCase> cases)
    { _binding = binding; _cases = cases; }

    internal bool ClosesSnapshot(string generation)
        => _cases.Any(c => c.Kind == "snapshot" && c.Id == generation);
    internal bool ClosesRun(string runId, string armToken)
        => _cases.Any(c => c.Kind == "run" && c.Id == runId && c.ArmToken == armToken);
    internal bool ClosesPayment(RewardJournalEntry entry)
        => _cases.Any(c => c.Kind == "payment" && c.Payment == entry);
    internal bool ClosesClaim(RewardClaim claim)
        => claim.Binding == _binding && _cases.Any(c => c.Kind == "run" && c.Id == claim.RunId
            || c.Kind == "payment" && c.Payment is { } p && p.Payment.ReceiptKey == claim.Key
                && p.Payment.Fingerprint == claim.Fingerprint && p.Payment.Roubles == claim.Roubles
                && p.Payment.Experience == claim.Experience && RewardColaProfile.MatchesClaim(p.Payment.Cola, claim.Cola));
    internal bool ClosesReceipt(RewardApprovalCandidate candidate)
    {
        if (candidate.Schema != 1 || candidate.Authorization is null || candidate.Report is null || candidate.Report.Schema != 1
            || candidate.Restoration is null) throw new RewardDataException("recovery-candidate-schema-invalid");
        return candidate.Authorization.Binding == _binding
            && (ClosesSnapshot(candidate.Authorization.RestorationId)
                || ClosesRun(candidate.Authorization.RunId, candidate.Authorization.ArmToken)
                || ClosesClaim(Claim(candidate)));
    }

    private static RewardClaim Claim(RewardApprovalCandidate candidate)
    {
        var run = candidate.Authorization;
        var evaluation = RewardResultValidator.Evaluate(run, candidate.Report);
        return new(run.Binding, run.RunId, run.Sequence, evaluation.Roubles, run.Policy.Version, evaluation.Experience, run.Cola);
    }
}
