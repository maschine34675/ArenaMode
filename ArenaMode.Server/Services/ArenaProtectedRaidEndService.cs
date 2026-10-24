using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Server.Core.Services.Profile;

namespace ArenaMode.Server.Services;

/// <summary>
/// Owns the complete protected native-end transaction. Arena deliberately does
/// not run SPT's ordinary post-raid merge, insurance, transfer, quest, health or
/// reward paths. It persists the already-restored pre-raid profile, resets the
/// two server intervals changed by local-start, clears raid caches and then
/// closes the token-bound session state. Identical HTTP retries share one Task.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ArenaProtectedRaidEndService(
    SaveServer saveServer,
    BackupService backupService,
    BotLootCacheService botLootCacheService,
    MatchBotDetailsCacheService matchBotDetailsCacheService,
    RagfairConfig ragfairConfig,
    HideoutConfig hideoutConfig,
    ArenaSessionService sessions,
    ISptLogger<ArenaProtectedRaidEndService> logger)
{
    private static readonly TimeSpan ProtectedEndTimeout = TimeSpan.FromMinutes(2);

    private sealed record PendingEnd(string NativeEndIdentity, Task Task);

    private readonly Dictionary<string, PendingEnd> _pending = new();
    private readonly Lock _lock = new();

    public Task GetOrStart(
        MongoId sessionId,
        string nativeEndIdentity)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (_pending.TryGetValue(key, out PendingEnd? existing)
                && string.Equals(
                    existing.NativeEndIdentity,
                    nativeEndIdentity,
                    StringComparison.Ordinal)
                && !existing.Task.IsFaulted
                && !existing.Task.IsCanceled)
            {
                return existing.Task;
            }

            Task task = FinalizeAsync(sessionId, nativeEndIdentity);
            _pending[key] = new PendingEnd(nativeEndIdentity, task);
            return task;
        }
    }

    private async Task FinalizeAsync(
        MongoId sessionId,
        string nativeEndIdentity)
    {
        try
        {
            // These are the non-profile lifecycle effects from SPT's ordinary
            // EndLocalRaidAsync that must still happen after Arena skips it.
            ResetNonProfileLifecycle();

            // The native end is already committed at this point. A client
            // disconnect may cancel only that caller's wait, never this shared
            // persistence transaction. The internal deadline faults/cancels a
            // genuinely stuck task so the next exact retry can start afresh.
            using var transactionTimeout = new CancellationTokenSource(
                ProtectedEndTimeout);
            await saveServer.SaveProfileAsync(
                sessionId,
                transactionTimeout.Token);
            await backupService.InitializeAsync(transactionTimeout.Token);

            if (!sessions.CompleteNativeEndFinalization(sessionId, nativeEndIdentity,
                    () => ArenaRewardLifecycle.Persisted(sessionId, nativeEndIdentity)))
            {
                // Another exact retry may already have closed the same
                // transaction. Its completed tombstone is equivalent success.
                if (sessions.ClassifyLocalRaidEnd(sessionId, nativeEndIdentity)
                    != ArenaLocalRaidEndDisposition.CompletedArenaEndRetry)
                {
                    throw new InvalidOperationException(
                        "Arena native-end transaction lost its exact session identity");
                }
            }

            logger.Info(
                $"[ArenaMode] Protected Arena end completed for session {sessionId}; " +
                "ordinary post-raid processing was skipped and the snapshot was persisted.");
        }
        catch (Exception ex)
        {
            // FinalizingEnd remains protected. EFT's identical native retry will
            // create a fresh task after this one faults and retry persistence.
            logger.Error(
                $"[ArenaMode] Protected Arena end failed and remains retryable: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Undo the non-profile effects SPT may publish before a native start later
    /// faults. The token-bound failed-start route uses the same cleanup as a
    /// completed Arena end before it releases the protected session.
    /// </summary>
    public void ResetNonProfileLifecycle()
    {
        botLootCacheService.ClearCache();
        matchBotDetailsCacheService.ClearCache();
        ragfairConfig.RunIntervalSeconds = ragfairConfig.RunIntervalValues.OutOfRaid;
        hideoutConfig.RunIntervalSeconds = hideoutConfig.RunIntervalValues.OutOfRaid;
    }

    public async Task PersistRestoredProfileAfterFailedStartAsync(MongoId sessionId)
    {
        using var transactionTimeout = new CancellationTokenSource(
            ProtectedEndTimeout);
        await saveServer.SaveProfileAsync(
            sessionId,
            transactionTimeout.Token);
        ArenaRewardLifecycle.Persisted(sessionId, string.Empty, recoveryOnly: true);
    }
}
