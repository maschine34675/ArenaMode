using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// An inert, storage-independent settlement protocol. There is intentionally no
/// adapter for a live SaveServer profile: its save semaphore does not exclude
/// profile mutators. The separate startup adapter only runs before native load.
/// A completed native save may skip disk persistence. See docs/REWARDS-CORE.md.
/// </summary>
internal sealed class RewardSettlementCore
{
    internal static readonly JsonSerializerOptions JournalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private readonly IRewardSettlementStore _store;
    private readonly RewardLimits _limits;
    private readonly TimeProvider _time;

    public RewardSettlementCore(IRewardSettlementStore store, RewardLimits? limits = null, TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _limits = limits ?? new RewardLimits();
        _limits.Validate();
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<RewardSettlementResult> SettleAsync(RewardClaim claim, CancellationToken cancellationToken = default)
    {
        if (claim is null) return Result(RewardSettlementStatus.Rejected, "missing-claim");
        try { claim.Validate(_limits); }
        catch (RewardDataException e) { return Result(RewardSettlementStatus.Rejected, e.Message); }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = await _store.TryAcquireAsync(claim.Binding, cancellationToken);
            if (lease is null) return Result(RewardSettlementStatus.Deferred, "profile-boundary-unavailable");
            // Cancel only before the first mutation. Once begun, complete the bounded
            // synchronous transaction or leave durable recovery evidence behind.
            cancellationToken.ThrowIfCancellationRequested();
            return SettleUnderLease(lease, claim);
        }
        catch (OperationCanceledException) { return Result(RewardSettlementStatus.Deferred, "cancelled"); }
        catch (FileNotFoundException) { return Result(RewardSettlementStatus.Quarantined, "durable-state-missing"); }
        catch (IOException) { return Result(RewardSettlementStatus.Deferred, "storage-unavailable"); }
        catch (UnauthorizedAccessException) { return Result(RewardSettlementStatus.Deferred, "storage-unavailable"); }
        catch (JsonException) { return Result(RewardSettlementStatus.Quarantined, "invalid-reward-data"); }
        catch (RewardDataException e) { return Result(RewardSettlementStatus.Quarantined, e.Message); }
    }

    private RewardSettlementResult SettleUnderLease(IRewardSettlementLease lease, RewardClaim claim)
    {
        var profile = lease.ReadProfile();
        var ledger = RewardProfileDocument.ReadLedger(profile.Json, claim.Binding, _limits)
            .ToDictionary(p => p.ReceiptKey, StringComparer.Ordinal);
        var journal = ParseJournal(lease.ReadJournal(), claim.Binding, _limits);

        // Check the complete history, not just the requested receipt: an older
        // restored profile must not evade detection by submitting a fresh run ID.
        var journalKeys = journal.Entries.Select(e => e.Payment.ReceiptKey).ToHashSet(StringComparer.Ordinal);
        if (ledger.Keys.Any(key => !journalKeys.Contains(key)))
            return Result(RewardSettlementStatus.Quarantined, "ledger-without-journal");

        RewardJournalEntry? pending = null;
        var recovered = journal.Entries.ToArray();
        for (var i = 0; i < recovered.Length; i++)
        {
            var entry = recovered[i];
            if (ledger.TryGetValue(entry.Payment.ReceiptKey, out var paid))
            {
                if (paid != entry.Payment) return Result(RewardSettlementStatus.Quarantined, "history-disagrees");
                if (entry.Phase != RewardJournalPhase.Committed)
                {
                    if (!RewardProfileDocument.HasOriginalMail(profile.Json, paid, _limits))
                        return Result(RewardSettlementStatus.Quarantined, "unconfirmed-mail-changed");
                    recovered[i] = entry with { Phase = RewardJournalPhase.Committed };
                }
            }
            else
            {
                if (entry.Phase != RewardJournalPhase.Prepared)
                    return Result(RewardSettlementStatus.Quarantined, "commit-state-ambiguous-or-rolled-back");
                if (entry.BeforeRevision != profile.Revision || pending is not null)
                    return Result(RewardSettlementStatus.Quarantined, "prepared-profile-changed");
                pending = entry;
            }
        }

        var existing = recovered.SingleOrDefault(e => e.Payment.ReceiptKey == claim.Key);
        if (existing is not null && (existing.Payment.Fingerprint != claim.Fingerprint || existing.Payment.Roubles != claim.Roubles
            || existing.Payment.Experience != claim.Experience))
            return Result(RewardSettlementStatus.Quarantined, "receipt-content-conflict");

        if (!recovered.SequenceEqual(journal.Entries))
        {
            journal = journal with { Entries = recovered };
            WriteJournalVerified(lease, journal);
        }

        if (existing?.Phase == RewardJournalPhase.Committed)
            return new(RewardSettlementStatus.AlreadyPaid, "durable-receipt-found", existing.Payment.MessageId);
        if (pending is not null && pending.Payment.ReceiptKey != claim.Key)
            return Result(RewardSettlementStatus.Deferred, "earlier-receipt-pending");
        if (existing is null && journal.Entries.Count >= _limits.MaxReceipts)
            return Result(RewardSettlementStatus.Rejected, "receipt-capacity-reached");

        var payment = existing?.Payment ?? new RewardPayment(claim.Key, claim.Fingerprint, claim.Roubles,
            RewardIdentity.MessageId(claim.Binding, claim.Key), _time.GetUtcNow().ToUnixTimeSeconds(), claim.Experience);
        ValidatePayment(payment, claim.Binding, _limits);
        // Compose and validate before any write. The editor works on a detached
        // document; it never holds or mutates a live SptProfile reference.
        var updated = RewardProfileDocument.Apply(profile.Json, claim, payment, _limits);
        var updatedRevision = RewardIdentity.Hash(updated);
        var prepared = new RewardJournalEntry(payment, RewardJournalPhase.Prepared, profile.Revision);
        if (existing is null)
        {
            journal = journal with { Entries = journal.Entries.Append(prepared).ToArray() };
            WriteJournalVerified(lease, journal);
        }

        journal = Replace(journal, prepared with { Phase = RewardJournalPhase.Attempting });
        WriteJournalVerified(lease, journal);

        try
        {
            if (!lease.TryWriteProfile(profile.Revision, updated))
                return Result(RewardSettlementStatus.Quarantined, "profile-revision-conflict");
        }
        catch (IOException) { /* A replace may have succeeded before throwing: read disk. */ }
        catch (UnauthorizedAccessException) { /* The same read-back rule applies. */ }

        var persisted = lease.ReadProfile();
        if (persisted.Revision == profile.Revision)
        {
            // Only this invocation, under the still-held writer boundary, can
            // prove that no commit happened. A later process must not infer it.
            journal = Replace(journal, prepared);
            WriteJournalVerified(lease, journal);
            return Result(RewardSettlementStatus.Deferred, "profile-write-not-durable");
        }
        if (persisted.Revision != updatedRevision)
            return Result(RewardSettlementStatus.Quarantined, "profile-write-outcome-unknown");

        journal = Replace(journal, prepared with { Phase = RewardJournalPhase.Committed });
        WriteJournalVerified(lease, journal);
        // Only now may a future caller acknowledge the receipt or notify the UI.
        return new(RewardSettlementStatus.Paid, "durable-payment-confirmed", payment.MessageId);
    }

    internal static RewardJournal ParseJournal(string json, RewardBinding binding, RewardLimits limits)
    {
        if (json.Length > 4_194_304) throw new RewardDataException("journal-too-large");
        using var document = JsonDocument.Parse(json);
        RejectDuplicateKeys(document.RootElement);
        var journal = document.Deserialize<RewardJournal>(JournalJson);
        if (journal is null || journal.Schema != 1 || journal.Binding != binding || journal.Entries is null
            || journal.Entries.Count > limits.MaxReceipts)
            throw new RewardDataException("invalid-journal-binding-or-schema");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in journal.Entries)
        {
            if (entry is null || !Enum.IsDefined(entry.Phase) || !RewardIdentity.IsHex(entry.BeforeRevision, 64))
                throw new RewardDataException("invalid-journal-entry");
            ValidatePayment(entry.Payment, binding, limits);
            if (!keys.Add(entry.Payment.ReceiptKey)) throw new RewardDataException("duplicate-journal-receipt");
        }
        return journal;
    }

    private void WriteJournalVerified(IRewardSettlementLease lease, RewardJournal journal)
    {
        var json = JsonSerializer.Serialize(journal, JournalJson);
        lease.WriteJournal(json);
        if (lease.ReadJournal() != json) throw new IOException("Reward journal write was not durable.");
    }

    private static RewardJournal Replace(RewardJournal journal, RewardJournalEntry replacement) => journal with
    {
        Entries = journal.Entries.Select(e => e.Payment.ReceiptKey == replacement.Payment.ReceiptKey ? replacement : e).ToArray()
    };

    internal static void ValidatePayment(RewardPayment payment, RewardBinding binding, RewardLimits limits)
    {
        if (payment is null) throw new RewardDataException("missing-payment");
        var parts = payment.ReceiptKey?.Split(':');
        if (parts is not { Length: 2 } || !RewardIdentity.IsGuid(parts[0])
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0
            || sequence.ToString(CultureInfo.InvariantCulture) != parts[1]
            || !RewardIdentity.IsHex(payment.Fingerprint, 64) || payment.Roubles <= 0 || payment.Roubles > limits.MaxRoublesPerReceipt
            || payment.Experience < 0 || payment.Experience > limits.MaxExperiencePerReceipt
            || payment.MessageId != RewardIdentity.MessageId(binding, payment.ReceiptKey!)
            || payment.PaidAtUnixSeconds is <= 0 or > 253402300799L)
            throw new RewardDataException("invalid-payment");
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new RewardDataException("duplicate-journal-property");
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item);
    }

    private static RewardSettlementResult Result(RewardSettlementStatus status, string reason) => new(status, reason);
}
