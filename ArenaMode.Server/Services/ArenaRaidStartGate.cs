using SPTarkov.Server.Core.Models.Common;
using System.Collections.Concurrent;

namespace ArenaMode.Server.Services;

/// <summary>
/// Serializes Arena's snapshot/arm transaction with the matching SPT local-start
/// prefix for one profile. This prevents an ordinary start from observing Idle,
/// then clearing a newly published protected arm while its snapshot is captured.
/// </summary>
internal static class ArenaRaidStartGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static async ValueTask<IDisposable> EnterAsync(
        MongoId sessionId,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = Get(sessionId);
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    public static IDisposable Enter(MongoId sessionId)
    {
        SemaphoreSlim gate = Get(sessionId);
        gate.Wait();
        return new Lease(gate);
    }

    private static SemaphoreSlim Get(MongoId sessionId)
        => Gates.GetOrAdd(sessionId.ToString(), static _ => new SemaphoreSlim(1, 1));

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release();
        }
    }
}
