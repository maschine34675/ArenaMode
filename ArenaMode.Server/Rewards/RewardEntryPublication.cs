namespace ArenaMode.Server.Rewards;

/// <summary>
/// Publishes the durable entry proof after the native stop sequence succeeded. At that point the update
/// loop is stopped for good (native StopAsync cannot be undone) and requests are held, so the normal server
/// can never go back to serving: whatever happens here, the same graceful shutdown the success path uses
/// MUST be scheduled. A failed publication therefore ends in an honest "declined" reply plus that shutdown,
/// never in a server that stays paused with no exit registered (the helper's Retry/Cancel loop cannot
/// finish without a lease, and the player used to have to kill the process by hand).
/// </summary>
internal static class RewardEntryPublication
{
    internal const int Attempts = 3;

    /// <summary>
    /// Entry refused before anything was paused (see RewardOfflineSession.PreflightReady): the exact reply a
    /// failed pre-stop preparation gives, which the helper answers by reopening the normal session.
    /// </summary>
    internal static RewardReturnReply Declined(string detail, bool canResumeNormal)
        => new(false, "return-not-ready", false, canResumeNormal, "entry-preflight", detail);

    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    /// <param name="shutdown">Called exactly once, after the outcome is known; true when the proof was published.</param>
    /// <param name="warn">Receives a fixed-vocabulary description of the failure, never a path or secret.</param>
    internal static async Task<RewardReturnReply> PublishAsync(Func<RewardOfflineLease> create, RewardReturnReply ready,
        Action<bool> shutdown, Action<string> warn, Func<TimeSpan, Task>? delay = null)
    {
        bool published = false;
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    create();
                    published = true;
                    return ready;
                }
                // A transient sharing violation (virus scanner, indexer) may clear within a moment; a
                // refusal by the entry state itself (RewardDataException) is deterministic and is not retried.
                catch (Exception e) when (attempt < Attempts && e is IOException or UnauthorizedAccessException)
                { await (delay ?? (span => Task.Delay(span)))(RetryDelay); }
            }
        }
        catch (Exception exception)
        {
            string reason = exception is RewardDataException ? exception.Message : exception.GetType().Name;
            warn($"The Arena entry proof could not be written ({reason}). The normal server has already saved its profiles and "
                + "stopped its update loop, so it cannot resume and is shutting down now. No profile data is lost; start SPT again normally.");
            // Accepted=false/Held=false: the helper reports a declined entry and copies nothing. Held=true would
            // keep it in its Retry/Cancel loop against a server that is about to exit.
            return new(false, "entry-proof-unpublished", false, Stage: "publish-entry-proof", Detail: reason);
        }
        finally { shutdown(published); }
    }
}
