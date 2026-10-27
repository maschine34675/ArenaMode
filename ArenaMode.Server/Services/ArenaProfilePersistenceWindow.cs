using System.Collections.Concurrent;
using ArenaMode.Server.Rewards;

namespace ArenaMode.Server.Services;

/// <summary>Drains native background writers before publishing a restored profile.</summary>
internal sealed class ArenaProfilePersistenceWindow(RewardReturnRequestGate updates)
{
    private sealed class Pending
    {
        internal readonly SemaphoreSlim Serial = new(1, 1);
        internal IDisposable? Hold;
    }

    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    internal async Task RunAsync(string id, Func<bool> hasPendingPersistence, Func<bool> restore,
        Func<Task> saveVerifyAndComplete, CancellationToken ct)
    {
        var pending = _pending.GetOrAdd(id, _ => new());
        await pending.Serial.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            pending.Hold ??= await updates.HoldAndDrainAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!hasPendingPersistence() && !restore())
                throw new InvalidOperationException("Arena profile snapshot could not be restored; retry retained");
            await saveVerifyAndComplete().ConfigureAwait(false);
            pending.Hold.Dispose();
            pending.Hold = null;
        }
        catch
        {
            // After the swap, keep native writers paused across a failed save
            // and its exact retry. They must not mutate the detached witness.
            // Before the swap, cancellation/failure may safely resume updates.
            if (!hasPendingPersistence())
            {
                pending.Hold?.Dispose();
                pending.Hold = null;
            }
            throw;
        }
        finally { pending.Serial.Release(); }
    }
}
