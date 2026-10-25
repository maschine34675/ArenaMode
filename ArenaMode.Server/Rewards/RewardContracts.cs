using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace ArenaMode.Server.Rewards;

// These are internal settlement inputs, not an HTTP contract or a client authority
// to mint RUB. Only the separately guarded pre-load consumer is registered in DI.
internal sealed record RewardBinding(string InstallationId, string ProfileId, string Epoch)
{
    public void Validate()
    {
        if (!RewardIdentity.IsGuid(InstallationId) || !RewardIdentity.IsGuid(Epoch)
            || !RewardIdentity.IsHex(ProfileId, 24))
            throw new RewardDataException("invalid-binding");
    }
}

internal sealed record RewardClaim(RewardBinding Binding, string RunId, long Sequence, long Roubles, string RulesVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long Experience = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cola = null)
{
    public string Key => RunId + ":" + Sequence.ToString(CultureInfo.InvariantCulture);
    // Preserve the exact old fingerprint for RUB-only authorizations and journals.
    public string Fingerprint => Cola is not null
        ? RewardIdentity.Hash(string.Join("\n", "arena-reward-v3", Binding.InstallationId,
            Binding.ProfileId, Binding.Epoch, Key, Roubles.ToString(CultureInfo.InvariantCulture),
            Experience.ToString(CultureInfo.InvariantCulture), RulesVersion, Cola))
        : Experience == 0
        ? RewardIdentity.Hash(string.Join("\n", "arena-reward-v1", Binding.InstallationId,
            Binding.ProfileId, Binding.Epoch, Key, Roubles.ToString(CultureInfo.InvariantCulture), RulesVersion))
        : RewardIdentity.Hash(string.Join("\n", "arena-reward-v2", Binding.InstallationId,
            Binding.ProfileId, Binding.Epoch, Key, Roubles.ToString(CultureInfo.InvariantCulture),
            Experience.ToString(CultureInfo.InvariantCulture), RulesVersion));

    public void Validate(RewardLimits limits)
    {
        if (Binding is null) throw new RewardDataException("missing-binding");
        Binding.Validate();
        if (!RewardIdentity.IsGuid(RunId) || Sequence <= 0 || Roubles < 0 || Roubles == 0 && Cola is null || Roubles > limits.MaxRoublesPerReceipt
            || Experience < 0 || Experience > limits.MaxExperiencePerReceipt
            || RulesVersion is null || !Regex.IsMatch(RulesVersion, "\\A[A-Za-z0-9._-]{1,32}\\z"))
            throw new RewardDataException("invalid-claim");
        if (Cola is not null) RewardColaDelta.Validate(Cola);
    }
}

// Safety bounds for the kernel, not gameplay reward rates. Live adoption must freeze
// the actual RUB template's stack limit and the policy revision at enrollment.
internal sealed record RewardLimits(long MaxRoublesPerReceipt = 10_000_000, int StackMax = 500_000,
    int MaxReceipts = 4096, int MailLifetimeSeconds = 2_592_000, long MaxExperiencePerReceipt = 1_000_000)
{
    public void Validate()
    {
        if (MaxRoublesPerReceipt is <= 0 or > 10_000_000 || StackMax is <= 0 or > 500_000
            || (MaxRoublesPerReceipt - 1) / StackMax + 1 > 100
            || MaxReceipts is <= 0 or > 4096 || MailLifetimeSeconds is <= 0 or > 2_592_000
            || MaxExperiencePerReceipt is <= 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(RewardLimits));
    }
}

internal sealed record RewardPayment(string ReceiptKey, string Fingerprint, long Roubles, string MessageId, long PaidAtUnixSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long Experience = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cola = null);
internal enum RewardJournalPhase { Prepared, Attempting, Committed }
internal sealed record RewardJournalEntry(RewardPayment Payment, RewardJournalPhase Phase, string BeforeRevision);
internal sealed record RewardJournal(int Schema, RewardBinding Binding, IReadOnlyList<RewardJournalEntry> Entries);

internal sealed record RewardProfileSnapshot(string Json)
{
    public string Revision => RewardIdentity.Hash(Json);
}

internal enum RewardSettlementStatus { Paid, AlreadyPaid, Deferred, Rejected, Quarantined }
internal sealed record RewardSettlementResult(RewardSettlementStatus Status, string Reason, string? MessageId = null);

internal interface IRewardSettlementStore
{
    // Returning null means not ready: private server, active raid, pending snapshot
    // restoration, disabled rewards, or no exclusive native persistence boundary.
    ValueTask<IRewardSettlementLease?> TryAcquireAsync(RewardBinding binding, CancellationToken cancellationToken);
}

internal interface IRewardSettlementLease : IDisposable
{
    // The lease covers ALL profile mutators/savers and journal writers, not only
    // Arena calls. It must bind the trusted ordinary-server origin/profile/epoch.
    // The startup adapter relies on native writers not having started and the
    // documented cooperative startup contract; it is never a live-profile lock.
    RewardProfileSnapshot ReadProfile();
    string ReadJournal();
    // Return only after durable replacement. The kernel independently reads back;
    // an adapter must not substitute a mutable in-memory cache for these reads.
    void WriteJournal(string json);
    // CAS is an additional conflict guard, not a replacement for the writer lease.
    bool TryWriteProfile(string expectedRevision, string json);
}

internal sealed class RewardDataException(string reason) : Exception(reason);

internal static class RewardIdentity
{
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsGuid(string? value) => value is not null && Guid.TryParseExact(value, "N", out var id)
        && id != Guid.Empty && id.ToString("N") == value;
    public static bool IsHex(string? value, int length) => value is not null && value.Length == length
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string MessageId(RewardBinding binding, string key) => Hash(
        $"arena-mail-v1\n{binding.InstallationId}\n{binding.ProfileId}\n{binding.Epoch}\n{key}")[..24];
}
