using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Services;

/// <summary>
/// Owns the complete native local-start transaction for an Arena arm. The
/// session gate stays held across SPT's async work, identical HTTP retries share
/// the same task and the task itself ignores a caller disconnect. Only the
/// caller's wait is cancelable.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ArenaProtectedRaidStartService(
    MatchController matchController,
    HttpResponseUtil httpResponseUtil,
    ArenaSessionService sessions,
    ProfileSnapshotService snapshots,
    ISptLogger<ArenaProtectedRaidStartService> logger)
{
    private static readonly TimeSpan ProtectedStartTimeout = TimeSpan.FromMinutes(10);

    private sealed record PendingStart(string NativeStartIdentity, Task<string> Task);

    private readonly Dictionary<string, PendingStart> _pending = new();
    private readonly Lock _lock = new();

    public Task<string> GetOrStart(
        MongoId sessionId,
        StartLocalRaidRequestData request)
    {
        string key = sessionId.ToString();
        string identity = request.ServerId
            ?? throw new InvalidOperationException("Arena native start identity was missing");

        lock (_lock)
        {
            if (_pending.TryGetValue(key, out PendingStart? existing))
            {
                if (string.Equals(
                        existing.NativeStartIdentity,
                        identity,
                        StringComparison.Ordinal))
                {
                    if (!existing.Task.IsCompleted
                        || existing.Task.IsFaulted
                        || existing.Task.IsCanceled
                        || sessions.CanReplayNativeStartResponse(
                            sessionId,
                            identity))
                    {
                        return existing.Task;
                    }

                    return Task.FromException<string>(new InvalidOperationException(
                        "The completed Arena local-start response no longer belongs to the active session"));
                }

                if (!existing.Task.IsCompleted
                    || sessions.OwnsNativeStartTransaction(
                        sessionId,
                        existing.NativeStartIdentity))
                {
                    return Task.FromException<string>(new InvalidOperationException(
                        "A different Arena local-start transaction still owns this session"));
                }
            }

            Task<string> task = ExecuteAsync(sessionId, request, identity);
            _pending[key] = new PendingStart(identity, task);
            return task;
        }
    }

    /// <summary>
    /// Runs the same controller/body wrapper as MatchCallbacks while holding the
    /// session gate. This lets an ordinary raid clear an expired PFT finalization
    /// guard atomically and prevents a new Arena snapshot from being armed until
    /// the ordinary start has completed its profile mutations.
    /// </summary>
    public async Task<string> StartOrdinaryAsync(
        MongoId sessionId,
        StartLocalRaidRequestData request,
        CancellationToken cancellationToken)
    {
        using (await ArenaRaidStartGate.EnterAsync(sessionId, cancellationToken))
        {
            // Idle + no snapshot is the authoritative ordinary boundary. Any
            // bounded delayed-PFT guard from the previous completed raid must
            // end before this new vanilla/PFT transaction begins. The synchronous
            // cutover drains old PFT readers without holding its thread-affine
            // write lease across the native asynchronous start below.
            sessions.CutOverToOrdinaryRaid(
                sessionId,
                () => snapshots.HasSnapshot(sessionId));
            StartLocalRaidResponseData response =
                await matchController.StartLocalRaidAsync(
                    sessionId,
                    request,
                    cancellationToken);
            return httpResponseUtil.GetBody(response);
        }
    }

    private async Task<string> ExecuteAsync(
        MongoId sessionId,
        StartLocalRaidRequestData request,
        string nativeStartIdentity)
    {
        using (await ArenaRaidStartGate.EnterAsync(sessionId, CancellationToken.None))
        {
            try
            {
                ArenaLocalRaidStartDisposition disposition = sessions.ProcessLocalRaidStart(
                    sessionId,
                    request.Location,
                    nativeStartIdentity);
                if (disposition != ArenaLocalRaidStartDisposition.ArenaStarted)
                {
                    throw new InvalidOperationException(
                        $"Arena native local-start was rejected ({disposition})");
                }

                // Once this protected transaction is claimed, an HTTP caller
                // disconnect must not cancel SPT half-way through profile/start
                // mutations. A generous internal deadline prevents an abandoned
                // server operation from owning the gate forever; recovery becomes
                // eligible only after that task has actually unwound.
                using var transactionTimeout = new CancellationTokenSource(
                    ProtectedStartTimeout);
                StartLocalRaidResponseData response =
                    await matchController.StartLocalRaidAsync(
                        sessionId,
                        request,
                        transactionTimeout.Token);

                if (!sessions.BindNativeEndIdentity(
                        sessionId,
                        nativeStartIdentity,
                        response.ServerId ?? string.Empty,
                        out string? exactNativeEndIdentity))
                {
                    throw new InvalidOperationException(
                        "Arena could not bind SPT's native server id to the protected arm");
                }

                response.ServerId = exactNativeEndIdentity;
                logger.Info(
                    $"[ArenaMode] Protected native Arena start completed for session {sessionId}; " +
                    "the exact SPT server id is bound for retry-safe end processing.");
                return httpResponseUtil.GetBody(response);
            }
            finally
            {
                // Failed-start recovery is forbidden until every possible SPT
                // mutation above has stopped. The route shares this same gate,
                // and this flag provides a second state-machine invariant.
                sessions.MarkNativeStartTaskSettled(sessionId, nativeStartIdentity);
            }
        }
    }
}
