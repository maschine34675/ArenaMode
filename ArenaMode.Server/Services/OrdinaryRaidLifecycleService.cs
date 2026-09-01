using ArenaMode.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Services;

public enum OrdinaryRaidPhase
{
    Starting,
    InRaid,
    Ending,
}

internal static class OrdinaryRaidRecoveryPolicy
{
    public static bool AllowsAbandon(
        bool clientRaidStarted,
        string ownerProcessId,
        string requesterProcessId)
        => !clientRaidStarted
           || !string.Equals(
               ownerProcessId,
               requesterProcessId,
               StringComparison.Ordinal);
}

/// <summary>
/// Serializes the complete vanilla local-start/local-end lifecycle for one
/// profile. EFT retries both POSTs. A client-owned start generation and SPT's
/// exact returned server id let identical retries share one Task while every
/// overlapping foreign generation fails before any vanilla side effect runs.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class OrdinaryRaidLifecycleService(
    MatchController matchController,
    HttpResponseUtil httpResponseUtil,
    ArenaSessionService sessions,
    ProfileSnapshotService snapshots,
    ISptLogger<OrdinaryRaidLifecycleService> logger)
{
    private static readonly TimeSpan NativeTransactionTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CompletedEndTtl = TimeSpan.FromHours(2);

    private sealed class Entry
    {
        public required string StartIdentity;
        public required string ClientProcessId;
        public required string RequestId;
        public OrdinaryRaidPhase Phase;
        public bool ClientRaidStarted;
        public string? RecoveryRequesterProcessId;
        public string? NativeEndIdentity;
        public DateTime UpdatedAtUtc;
        public Task<string>? StartTask;
        public Task<string>? EndTask;
    }

    private sealed record EndExecution(string SessionKey, string NativeEndIdentity);

    private static readonly AsyncLocal<EndExecution?> CurrentEndExecution = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _retiredStarts = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _completedEnds = new();
    private readonly Lock _lock = new();

    public Task<string> GetOrStart(
        MongoId sessionId,
        StartLocalRaidRequestData request)
    {
        string identity = request.ServerId ?? string.Empty;
        if (!Protocol.TryParseNativeOrdinaryStartIdentity(
                identity,
                out string? clientProcessId,
                out string? requestId))
        {
            return Task.FromException<string>(new InvalidOperationException(
                "Ordinary local-start did not carry a valid client generation"));
        }

        string key = sessionId.ToString();
        lock (_lock)
        {
            PruneLocked(key);
            if (IsRetiredStartLocked(key, identity))
            {
                return Task.FromException<string>(new InvalidOperationException(
                    "The ordinary raid generation has already been retired"));
            }

            if (_entries.TryGetValue(key, out Entry? existing))
            {
                if (string.Equals(
                        existing.StartIdentity,
                        identity,
                        StringComparison.Ordinal)
                    && existing.StartTask != null)
                {
                    return existing.StartTask;
                }

                return Task.FromException<string>(new InvalidOperationException(
                    "A different ordinary raid generation already owns this session"));
            }

            var entry = new Entry
            {
                StartIdentity = identity,
                ClientProcessId = clientProcessId!,
                RequestId = requestId!,
                Phase = OrdinaryRaidPhase.Starting,
                ClientRaidStarted = false,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _entries[key] = entry;
            entry.StartTask = ExecuteStartAsync(sessionId, request, entry);
            return entry.StartTask;
        }
    }

    public bool TryGetOrStartEnd(
        MongoId sessionId,
        EndLocalRaidRequestData request,
        out Task<string>? transaction)
    {
        string key = sessionId.ToString();
        string identity = request.ServerId ?? string.Empty;
        lock (_lock)
        {
            PruneLocked(key);
            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                if (IsCompletedEndLocked(key, identity))
                {
                    transaction = Task.FromResult(httpResponseUtil.NullResponse());
                    return true;
                }

                transaction = null;
                return false;
            }

            // SPT's native server id contains a seconds-resolution timestamp and
            // can repeat. An active generation always outranks an older success
            // tombstone with the same id; otherwise the new vanilla end would be
            // swallowed and its profile result would never be persisted.
            if (string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
                || !string.Equals(
                    entry.NativeEndIdentity,
                    identity,
                    StringComparison.Ordinal))
            {
                transaction = Task.FromException<string>(new InvalidOperationException(
                    "Native local-end did not match the active ordinary raid identity"));
                return true;
            }

            if (entry.Phase == OrdinaryRaidPhase.Starting)
            {
                transaction = Task.FromException<string>(new InvalidOperationException(
                    "Ordinary local-start has not completed; local-end rejected"));
                return true;
            }

            if (entry.Phase == OrdinaryRaidPhase.Ending
                && entry.EndTask != null
                && !entry.EndTask.IsFaulted
                && !entry.EndTask.IsCanceled)
            {
                transaction = entry.EndTask;
                return true;
            }

            entry.Phase = OrdinaryRaidPhase.Ending;
            entry.UpdatedAtUtc = DateTime.UtcNow;
            entry.EndTask = ExecuteEndAsync(sessionId, request, entry);
            transaction = entry.EndTask;
            return true;
        }
    }

    public bool HasActiveTransaction(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            PruneLocked(key);
            return _entries.ContainsKey(key);
        }
    }

    internal (bool active, OrdinaryRaidPhase? phase, string? startIdentity, string? nativeEndIdentity)
        GetStatus(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            PruneLocked(key);
            return _entries.TryGetValue(key, out Entry? entry)
                ? (true, entry.Phase, entry.StartIdentity, entry.NativeEndIdentity)
                : (false, null, null, null);
        }
    }

    internal (
        bool active,
        OrdinaryRaidPhase? phase,
        string? startIdentity,
        string? clientProcessId,
        string? nativeEndIdentity,
        bool clientRaidStarted) GetRecoveryStatus(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            PruneLocked(key);
            return _entries.TryGetValue(key, out Entry? entry)
                ? (
                    true,
                    entry.Phase,
                    entry.StartIdentity,
                    entry.ClientProcessId,
                    entry.NativeEndIdentity,
                    entry.ClientRaidStarted)
                : (false, null, null, null, null, false);
        }
    }

    public bool ConfirmClientRaidStarted(
        MongoId sessionId,
        string? startIdentity,
        string? clientProcessId,
        string? nativeEndIdentity)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out Entry? entry)
                || entry.Phase != OrdinaryRaidPhase.InRaid
                || !string.Equals(entry.StartIdentity, startIdentity, StringComparison.Ordinal)
                || !string.Equals(entry.ClientProcessId, clientProcessId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
                || !string.Equals(
                    entry.NativeEndIdentity,
                    nativeEndIdentity,
                    StringComparison.Ordinal))
            {
                return false;
            }

            entry.ClientRaidStarted = true;
            entry.UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }
    }

    public bool TryBeginAbandonedStartRecovery(
        MongoId sessionId,
        string? startIdentity,
        string? requesterProcessId,
        out string? reason)
    {
        reason = null;
        if (!Protocol.IsLifecycleRequestId(requesterProcessId ?? string.Empty))
        {
            reason = "ordinary recovery process identity was malformed";
            return false;
        }

        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out Entry? entry)
                || !string.Equals(entry.StartIdentity, startIdentity, StringComparison.Ordinal))
            {
                reason = "ordinary recovery did not match the active start generation";
                return false;
            }

            if (!Protocol.TryParseNativeOrdinaryStartIdentity(
                    startIdentity ?? string.Empty,
                    out string? ownerProcessId,
                    out string? retainedRequestId)
                || !string.Equals(
                    entry.ClientProcessId,
                    ownerProcessId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.RequestId,
                    retainedRequestId,
                    StringComparison.Ordinal))
            {
                reason = "ordinary recovery process/request identity was malformed";
                return false;
            }

            if (!OrdinaryRaidRecoveryPolicy.AllowsAbandon(
                    entry.ClientRaidStarted,
                    entry.ClientProcessId,
                    requesterProcessId!))
            {
                reason = "the owning client process cannot abandon its confirmed ordinary raid";
                return false;
            }

            if (entry.Phase == OrdinaryRaidPhase.Ending && entry.EndTask != null)
            {
                reason = "ordinary native end is already running";
                return false;
            }

            if (entry.Phase != OrdinaryRaidPhase.InRaid
                && entry.Phase != OrdinaryRaidPhase.Ending)
            {
                reason = "ordinary local-start has not settled";
                return false;
            }

            // The same process retains the exact full identity locally. A new
            // process receives it only from the gate-serialized status route;
            // its different process id is the crash-orphan proof for a raid the
            // old process had already confirmed.
            entry.Phase = OrdinaryRaidPhase.Ending;
            entry.EndTask = null;
            entry.RecoveryRequesterProcessId = requesterProcessId;
            entry.UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }
    }

    public bool CompleteAbandonedStartRecovery(
        MongoId sessionId,
        string? startIdentity)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out Entry? entry)
                || entry.Phase != OrdinaryRaidPhase.Ending
                || entry.EndTask != null
                || string.IsNullOrWhiteSpace(entry.RecoveryRequesterProcessId)
                || !string.Equals(entry.StartIdentity, startIdentity, StringComparison.Ordinal))
            {
                return false;
            }

            // The recovering process has no trustworthy native end payload for
            // this orphan (which may have reached live play before a crash).
            // Retire its returned end id so a delayed teardown cannot run
            // vanilla post-raid mutation later.
            if (!string.IsNullOrWhiteSpace(entry.NativeEndIdentity))
                RememberCompletedEndLocked(key, entry.NativeEndIdentity);
            RememberRetiredStartLocked(key, entry.StartIdentity);
            _entries.Remove(key);
            return true;
        }
    }

    internal bool IsExecutingExactEnd(MongoId sessionId, string? nativeEndIdentity)
    {
        EndExecution? current = CurrentEndExecution.Value;
        return current != null
               && string.Equals(current.SessionKey, sessionId.ToString(), StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(nativeEndIdentity)
               && string.Equals(
                   current.NativeEndIdentity,
                   nativeEndIdentity,
                   StringComparison.Ordinal);
    }

    private async Task<string> ExecuteStartAsync(
        MongoId sessionId,
        StartLocalRaidRequestData request,
        Entry entry)
    {
        // GetOrStart publishes the Task while holding only the short registry
        // lock. Force the transaction body onto a later continuation so no
        // synchronous controller prefix can run under that lock.
        await Task.Yield();
        string key = sessionId.ToString();
        using (await ArenaRaidStartGate.EnterAsync(sessionId, CancellationToken.None))
        {
            try
            {
                lock (_lock)
                {
                    if (!_entries.TryGetValue(key, out Entry? current)
                        || !ReferenceEquals(current, entry)
                        || current.Phase != OrdinaryRaidPhase.Starting)
                    {
                        throw new InvalidOperationException(
                            "Ordinary local-start generation lost its lifecycle claim");
                    }
                }

                // Drain any PFT callback that entered while the previous Arena
                // finalization guard was still visible, then publish the ordinary
                // boundary synchronously. ReaderWriterLockSlim is thread-affine,
                // so its writer is deliberately released before the await below.
                sessions.CutOverToOrdinaryRaid(
                    sessionId,
                    () => snapshots.HasSnapshot(sessionId));

                // The marker belongs only to this outer idempotency boundary.
                // Preserve the controller input that stock SPT receives.
                request.ServerId = null;
                using var transactionTimeout = new CancellationTokenSource(
                    NativeTransactionTimeout);
                StartLocalRaidResponseData response =
                    await matchController.StartLocalRaidAsync(
                        sessionId,
                        request,
                        transactionTimeout.Token);

                string nativeEndIdentity = response.ServerId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(nativeEndIdentity)
                    || Protocol.HasNativeStartMarker(nativeEndIdentity)
                    || Protocol.HasNativeEndMarker(nativeEndIdentity))
                {
                    throw new InvalidOperationException(
                        "SPT returned an invalid ordinary native server id");
                }

                lock (_lock)
                {
                    if (!_entries.TryGetValue(key, out Entry? current)
                        || !ReferenceEquals(current, entry)
                        || current.Phase != OrdinaryRaidPhase.Starting)
                    {
                        throw new InvalidOperationException(
                            "Ordinary local-start generation changed before identity binding");
                    }

                    current.NativeEndIdentity = nativeEndIdentity;
                    current.Phase = OrdinaryRaidPhase.InRaid;
                    current.UpdatedAtUtc = DateTime.UtcNow;
                }

                return httpResponseUtil.GetBody(response);
            }
            catch
            {
                lock (_lock)
                {
                    if (_entries.TryGetValue(key, out Entry? current)
                        && ReferenceEquals(current, entry)
                        && current.Phase == OrdinaryRaidPhase.Starting)
                    {
                        // Preserve the exact failed generation. EFT's remaining
                        // transport retries must observe this same fault instead
                        // of running a possibly half-mutated vanilla start again.
                        // The client/new process releases it explicitly through
                        // the abandoned-start recovery route after this Task has
                        // fully unwound and the gate is available.
                        current.Phase = OrdinaryRaidPhase.Ending;
                        current.EndTask = null;
                        current.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }

                throw;
            }
        }
    }

    private async Task<string> ExecuteEndAsync(
        MongoId sessionId,
        EndLocalRaidRequestData request,
        Entry entry)
    {
        // See ExecuteStartAsync: classification/task publication must finish
        // before any vanilla end side effect or gate wait begins.
        await Task.Yield();
        string key = sessionId.ToString();
        string identity = request.ServerId!;
        using (await ArenaRaidStartGate.EnterAsync(sessionId, CancellationToken.None))
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out Entry? current)
                    || !ReferenceEquals(current, entry)
                    || current.Phase != OrdinaryRaidPhase.Ending
                    || !string.Equals(
                        current.NativeEndIdentity,
                        identity,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Ordinary local-end generation lost its lifecycle claim");
                }
            }

            EndExecution? previous = CurrentEndExecution.Value;
            CurrentEndExecution.Value = new EndExecution(key, identity);
            try
            {
                using var transactionTimeout = new CancellationTokenSource(
                    NativeTransactionTimeout);
                await matchController.EndLocalRaidAsync(
                    sessionId,
                    request,
                    transactionTimeout.Token);

                string response = httpResponseUtil.NullResponse();
                lock (_lock)
                {
                    if (!_entries.TryGetValue(key, out Entry? current)
                        || !ReferenceEquals(current, entry)
                        || current.Phase != OrdinaryRaidPhase.Ending
                        || !string.Equals(
                            current.NativeEndIdentity,
                            identity,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Ordinary local-end generation changed before completion");
                    }

                    RememberCompletedEndLocked(key, identity);
                    RememberRetiredStartLocked(key, current.StartIdentity);
                    _entries.Remove(key);
                }

                return response;
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    if (_entries.TryGetValue(key, out Entry? current)
                        && ReferenceEquals(current, entry))
                    {
                        current.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }

                logger.Error(
                    $"[ArenaMode] Ordinary native end failed and remains exact-retryable: {ex}");
                throw;
            }
            finally
            {
                CurrentEndExecution.Value = previous;
            }
        }
    }

    private void PruneLocked(string key)
    {
        PruneRetiredStartsLocked(key);
        PruneCompletedEndsLocked(key);
        // Never convert a faulted/incomplete vanilla end into a successful
        // tombstone. It must remain exact-retryable (or fail closed) so ordinary
        // profile persistence cannot be silently skipped. Orphaned starts are
        // released only through the explicit process/request-bound recovery
        // route above; a confirmed raid additionally requires a replacement
        // client process as crash proof.
    }

    private bool IsRetiredStartLocked(string key, string identity)
    {
        if (!_retiredStarts.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
            return false;

        PruneRetiredStartsLocked(key);
        return tombstones.ContainsKey(identity);
    }

    private void RememberRetiredStartLocked(string key, string identity)
    {
        if (!_retiredStarts.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
        {
            tombstones = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _retiredStarts[key] = tombstones;
        }

        tombstones[identity] = DateTime.UtcNow;
        foreach (string oldest in tombstones
                     .OrderBy(pair => pair.Value)
                     .Take(Math.Max(0, tombstones.Count - 8))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(oldest);
        }
    }

    private void PruneRetiredStartsLocked(string key)
    {
        if (!_retiredStarts.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
            return;

        DateTime cutoff = DateTime.UtcNow - CompletedEndTtl;
        foreach (string expired in tombstones
                     .Where(pair => pair.Value < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(expired);
        }

        if (tombstones.Count == 0)
            _retiredStarts.Remove(key);
    }

    private bool IsCompletedEndLocked(string key, string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)
            || !_completedEnds.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
        {
            return false;
        }

        PruneCompletedEndsLocked(key);
        return tombstones.ContainsKey(identity);
    }

    private void RememberCompletedEndLocked(string key, string identity)
    {
        if (!_completedEnds.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
        {
            tombstones = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _completedEnds[key] = tombstones;
        }

        tombstones[identity] = DateTime.UtcNow;
        foreach (string oldest in tombstones
                     .OrderBy(pair => pair.Value)
                     .Take(Math.Max(0, tombstones.Count - 8))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(oldest);
        }
    }

    private void PruneCompletedEndsLocked(string key)
    {
        if (!_completedEnds.TryGetValue(key, out Dictionary<string, DateTime>? tombstones))
            return;

        DateTime cutoff = DateTime.UtcNow - CompletedEndTtl;
        foreach (string expired in tombstones
                     .Where(pair => pair.Value < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(expired);
        }

        if (tombstones.Count == 0)
            _completedEnds.Remove(key);
    }
}
