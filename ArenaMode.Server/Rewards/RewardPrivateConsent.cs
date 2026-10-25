using System.Text.Json;
using ArenaMode.Shared;

namespace ArenaMode.Server.Rewards;

internal sealed class RewardPrivateConsent
{
    private readonly string _root;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<RewardControlGrant, RewardControlRequest, CancellationToken, Task<RewardControlReply>> _send;
    private readonly SemaphoreSlim _requests = new(1);
    private readonly object _sync = new();
    private readonly RewardControlLease? _lease;
    private RewardStatusResponse _status = Unavailable();
    private RewardHandoff? _authority;
    private long _revision = -1;
    private DateTimeOffset _confirmed;
    internal bool HasControl { get; }
    internal bool IsOffline { get; }

    internal RewardPrivateConsent(string root, Func<DateTimeOffset> now,
        Func<RewardControlGrant, RewardControlRequest, CancellationToken, Task<RewardControlReply>>? send = null)
    {
        _root = root; _now = now; _send = send ?? RewardConsentTransport.SendAsync;
        // No fallback to an old copied ON handoff if offline ownership is missing.
        try { if (RewardOfflineSession.Requested(root)) { IsOffline = true; HasControl = true; return; } }
        catch { IsOffline = true; HasControl = true; return; }
        if (RewardStartupFiles.MarkerAbsent(root)) return;
        try
        {
            string markerPath = Path.Combine(root, "arena-session.json");
            RewardStartupFiles.RequireLocalPath(root, markerPath);
            var marker = RewardStartupFiles.Read<JsonElement>(markerPath, 16384);
            string? expectedControl = marker.TryGetProperty("rewardControlId", out var expected) ? expected.GetString() : null;
            HasControl = !string.IsNullOrEmpty(expectedControl)
                || RewardRuntimeFiles.Exists(root, Path.Combine(root, "arena-rewards-control.json"));
            if (!HasControl) return;
            RewardStartupFiles.RequireLocalPath(root, Path.Combine(root, "arena-rewards-control.json"));
            _lease = RewardStartupFiles.Read<RewardControlLease>(Path.Combine(root, "arena-rewards-control.json"), 16384);
            RewardConsentControl.ValidateGrant(_lease.Grant, now());
            if (_lease.Schema != 1 || !RewardRuntimeFiles.SameRoot(_lease.PrivateRoot, root)
                || expectedControl is not null && expectedControl != _lease.Grant.ControlId
                || RewardRuntimeFiles.SameRoot(_lease.Grant.RuntimeRoot, root)
                || marker.GetProperty("schema").GetInt32() != 1
                || marker.GetProperty("profileId").GetString() != _lease.Grant.ProfileId
                || marker.GetProperty("createdUtc").GetString() != _lease.MarkerCreatedUtc
                || marker.GetProperty("port").GetInt32() != _lease.Port
                || !RewardRuntimeFiles.SameRoot(marker.GetProperty("sourceRuntime").GetString()!, _lease.Grant.RuntimeRoot)
                || new Uri(_lease.Grant.OriginUrl).Port == _lease.Port)
                throw new RewardDataException("reward-control-marker-mismatch");
        }
        catch (Exception)
        {
            // A malformed present sidecar must never fall back to a copied ON authority.
            HasControl = true; _lease = null;
        }
    }

    internal RewardStatusResponse Status()
    {
        if (IsOffline)
        {
            try
            {
                var status = Offline().Status();
                lock (_sync) if (_status.State == "changed" && _status.ConsentRevision == status.ConsentRevision)
                { status.State = _status.State; status.Detail = _status.Detail; }
                return status;
            }
            catch { return new() { PrivateSession = true, State = "unavailable", Detail = "Offline Arena reward authority needs recovery; return to normal SPT." }; }
        }
        lock (_sync) return JsonSerializer.Deserialize<RewardStatusResponse>(JsonSerializer.Serialize(_status))!;
    }

    internal RewardHandoff? Authority(string id)
    {
        if (IsOffline) return Offline().Authority(id);
        lock (_sync) return _lease?.Grant.ProfileId == id && _status.Available && _status.Enabled
            && _now() - _confirmed <= TimeSpan.FromMinutes(2) && _lease.Grant.ExpiresUtc > _now() ? _authority : null;
    }

    internal async Task RefreshAsync(string id, bool? enabled, CancellationToken ct, long? expectedRevision = null)
    {
        await _requests.WaitAsync(ct);
        try
        {
            if (IsOffline)
            {
                var owner = Offline();
                if (id != owner.Lease.ProfileId) throw new RewardDataException("offline-profile-mismatch");
                var offlineStatus = enabled.HasValue ? owner.SetConsent(id, enabled.Value, expectedRevision) : owner.Status();
                lock (_sync) _status = offlineStatus;
                return;
            }
            if (_lease is null || _lease.Grant.ProfileId != id) throw new RewardDataException("reward-control-not-bound");
            RewardConsentControl.ValidateGrant(_lease.Grant, _now());
            long revision;
            lock (_sync) revision = _revision;
            if (enabled.HasValue && revision < 0) throw new RewardDataException("reward-control-refresh-required");
            var reply = await _send(_lease.Grant, new(1, _lease.Grant.ControlId, _lease.Grant.Token,
                enabled.HasValue ? expectedRevision ?? revision : null, enabled), ct);
            if (reply.Status is null || reply.Revision < 0 || (!reply.Accepted && reply.Reason != "stale-consent"))
                throw new RewardDataException("reward-control-rejected");
            var status = reply.Status;
            status.PrivateSession = true;
            RewardHandoff? authority = null;
            if (status.Enabled && status.Enrolled && status.Available)
            {
                authority = RewardRuntimeFiles.ValidateHandoff(reply.Handoff ?? throw new RewardDataException("reward-control-no-authority"));
                if (!authority.Enabled || authority.Binding.ProfileId != id
                    || !RewardRuntimeFiles.SameRoot(authority.RuntimeRoot, _lease.Grant.RuntimeRoot))
                    throw new RewardDataException("reward-control-authority-mismatch");
                // Never replace the key needed to return already earned receipts.
                var path = Path.Combine(_root, "arena-rewards-origin.json");
                if (RewardRuntimeFiles.Exists(_root, path))
                {
                    var previous = RewardRuntimeFiles.ReadHandoff(_root, path);
                    if (previous.Binding != authority.Binding || previous.DelegationId != authority.DelegationId
                        || previous.Key != authority.Key || !RewardRuntimeFiles.SameRoot(previous.RuntimeRoot, authority.RuntimeRoot))
                        throw new RewardDataException("reward-control-origin-changed");
                }
                else RewardRuntimeFiles.WriteNew(_root, path, authority);
            }
            if (!reply.Accepted)
            {
                status.State = "changed";
                status.Detail = "Reward preference changed elsewhere. Review the current setting before trying again.";
            }
            lock (_sync)
            {
                _revision = reply.Revision; _status = status; _authority = authority; _confirmed = _now();
            }
        }
        catch (Exception)
        {
            // No POST retries, even after a lost reply. A later GET reconciles the actual choice.
            lock (_sync) { _status = Unavailable(); _authority = null; _revision = -1; }
        }
        finally { _requests.Release(); }
    }

    private RewardOfflinePrivate Offline() => RewardOfflineSession.Owner(_root)
        ?? throw new RewardDataException("offline-owner-not-active");

    private static RewardStatusResponse Unavailable() => new()
    {
        PrivateSession = true, State = "unavailable",
        Detail = "Cannot reach the normal server for rewards. Keep it running; after restarting it, re-enter the Arena session."
    };
}
