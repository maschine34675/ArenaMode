using ArenaMode.Shared;

namespace ArenaMode.Server.Rewards;

/// <summary>The private process owns the original runtime lock, but never its profile.</summary>
internal sealed class RewardOfflinePrivate(RewardOfflineLease lease, IDisposable lifetime) : IDisposable
{
    private readonly object _sync = new();
    private bool _disposed;
    private RewardOfflineState? _last;
    // The reason the normal server recorded next to the lease, read once: it was written before this process existed.
    private string? _reason;
    private bool _reasonRead;

    private string? Reason()
    {
        if (!_reasonRead) { _reason = RewardOfflineSession.ReadAuthorityUnavailable(Lease); _reasonRead = true; }
        return _reason;
    }
    internal RewardOfflineLease Lease { get; } = lease;

    internal void Initialize()
    {
        string path = Path.Combine(Lease.PrivateRoot, RewardOfflineSession.StateFile);
        RewardRuntimeFiles.WriteNew(Lease.PrivateRoot, path, RewardOfflineSession.Sign(Lease,
            new(1, Lease.SessionId, RewardOfflineSession.Hash(Lease), Lease.ConsentRevision, Lease.Enabled)));
        _last = RewardOfflineSession.ReadState(Lease);
        if (Lease.Authority is { } authority)
            RewardRuntimeFiles.Write(Lease.PrivateRoot, Path.Combine(Lease.PrivateRoot, "arena-rewards-origin.json"), authority);
    }

    private RewardOfflineState Read()
    {
        if (_disposed) throw new RewardDataException("offline-owner-released");
        RewardOfflineSession.RequireActive(Lease);
        var state = RewardOfflineSession.ReadState(Lease);
        if (_last is not null && state != _last) throw new RewardDataException("offline-state-changed-externally");
        return state;
    }

    internal RewardStatusResponse Status()
    {
        lock (_sync)
        {
            var state = Read();
            var policy = RewardRuntimeCoordinator.Policy;
            // A lease without authority whose session directory names why (the normal start refused its reward step,
            // or the profile's records no longer match) is "withheld" with that sentence, as the normal session shows
            // it; the card then stops advising a return that would change nothing. Without a reason, enrollment is pending.
            string? withheld = state.Enabled && Lease.Authority is null ? RewardUnavailableText.ForLeaseReason(Reason()) : null;
            return new()
            {
                Available = true, PrivateSession = true, AutomaticReturn = true, CanChange = true,
                Enabled = state.Enabled, Enrolled = Lease.Authority is not null, ConsentRevision = state.Revision,
                State = !state.Enabled ? "disabled" : withheld is not null ? "withheld" : Lease.Authority is null ? "restart-required" : "ready",
                Detail = !state.Enabled ? "Optional profile rewards are off."
                    : withheld ?? (Lease.Authority is null ? "Rewards will activate after returning to normal SPT and starting a new Arena session."
                    : "Rewards are credited when returning to normal SPT."),
                RoublesPerKill = policy.RoublesPerKill, RoublesPerRoundWin = policy.RoublesPerRoundWin, MaxRoubles = policy.MaxRoubles,
                ExperiencePerKill = policy.ExperiencePerKill, ExperiencePerRoundWin = policy.ExperiencePerRoundWin, MaxExperience = policy.MaxExperience,
            };
        }
    }

    internal RewardHandoff? Authority(string id)
    { lock (_sync) return id == Lease.ProfileId && Read().Enabled ? Lease.Authority : null; }

    internal RewardStatusResponse SetConsent(string id, bool enabled, long? expectedRevision)
    {
        lock (_sync)
        {
            var state = Read();
            if (id != Lease.ProfileId) throw new RewardDataException("offline-consent-profile-mismatch");
            if (expectedRevision != state.Revision)
            {
                var stale = Status(); stale.State = "changed";
                stale.Detail = "Reward preference changed. Review the current setting before trying again.";
                return stale;
            }
            var next = state with { Revision = checked(state.Revision + 1), Enabled = enabled };
            var signed = RewardOfflineSession.Sign(Lease, next);
            // The readback is the acknowledgement boundary. A lost reply is reconciled by status.
            RewardRuntimeFiles.Write(Lease.PrivateRoot, Path.Combine(Lease.PrivateRoot, RewardOfflineSession.StateFile), signed);
            _last = RewardOfflineSession.ReadState(Lease);
            if (_last != next) throw new RewardDataException("offline-consent-readback-mismatch");
            return Status();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            RewardOfflineSession.Release(this);
            lifetime.Dispose();
        }
    }
}
