using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Bots;

/// <summary>
/// Owns exactly one synchronous native dispatch, not an async execution context.
/// The latched raid mode keeps external spawners out even before Current exists
/// or while the mode is tearing down. Ordinary raids and Tactical fail open.
/// </summary>
internal static class TeamDeathmatchSpawnDispatch
{
    [ThreadStatic]
    private static Lease _current;

    internal static bool IsActive => ArenaSession.IsActive
        && string.Equals(ArenaSession.ActiveMode, "teamdeathmatch", StringComparison.Ordinal)
        && Singleton<AbstractGame>.Instantiated
        && Singleton<GameWorld>.Instantiated
        && ArenaSession.IsExactActiveRaid(
            Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance);

    internal static Lease Enter(BotSpawner spawner, BotCreationData data)
    {
        var lease = new Lease(spawner, data, _current);
        _current = lease;
        return lease;
    }

    internal static bool CanDispatch(BotSpawner spawner, BotCreationData data)
    {
        if (!IsActive)
            return true;

        var lease = _current;
        if (lease == null || !lease.Matches(spawner, data) || lease.Consumed)
            return false;

        lease.Consumed = true;
        return true;
    }

    /// <summary>
    /// Our own point filter can prove rejection happened before native counters.
    /// A different mod refusing later is deliberately left uncertain.
    /// </summary>
    internal static void MarkRejected(BotSpawner spawner, BotCreationData data)
    {
        if (_current?.Matches(spawner, data) == true)
            _current.WasRejected = true;
    }

    internal sealed class Lease : IDisposable
    {
        private readonly BotSpawner _spawner;
        private readonly BotCreationData _data;
        private readonly Lease _previous;
        private readonly AbstractGame _game;
        private readonly GameWorld _world;
        private bool _disposed;

        internal bool Consumed;
        internal bool WasRejected { get; set; }

        internal Lease(BotSpawner spawner, BotCreationData data, Lease previous)
        {
            _spawner = spawner;
            _data = data;
            _previous = previous;
            _game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
            _world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        }

        internal bool Matches(BotSpawner spawner, BotCreationData data)
            => !_disposed
               && !ReferenceEquals(_spawner, null)
               && !ReferenceEquals(_data, null)
               && ReferenceEquals(_spawner, spawner)
               && ReferenceEquals(_data, data)
               && Singleton<AbstractGame>.Instantiated
               && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
               && Singleton<GameWorld>.Instantiated
               && ReferenceEquals(Singleton<GameWorld>.Instance, _world);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(_current, this))
            {
                // Defensive out-of-order disposal must not resurrect a closed
                // owner or retain its world after the innermost scope exits.
                var previous = _previous;
                while (previous != null && previous._disposed)
                    previous = previous._previous;
                _current = previous;
            }
        }
    }
}
