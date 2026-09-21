using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Bots;

/// <summary>Exactly one synchronous spawner/data submission, never an async scope.</summary>
internal static class ArenaSoloSpawnDispatch
{
    [ThreadStatic] private static Lease _current;

    internal static bool IsActive
    {
        get
        {
            if (!ArenaSession.Participants.HasValue || !Singleton<GameWorld>.Instantiated)
                return false;
            if (ArenaSession.IsActive)
                return ArenaParticipantPolicy.IsSolo(ArenaSession.ActiveMode)
                    && Singleton<AbstractGame>.Instantiated
                    && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance);
            // Confirmed matching-map loading exception, as with solo hostility.
            return ArenaSession.ArmedForNextRaid && ArenaParticipantPolicy.IsSolo(ArenaSession.ArmedMode)
                && string.Equals(ArenaSession.ArmedMap, Singleton<GameWorld>.Instance.LocationId, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static Lease Enter(BotSpawner spawner, BotCreationData data)
    {
        var lease = new Lease(spawner, data, _current);
        _current = lease;
        return lease;
    }

    internal static bool CanDispatch(BotSpawner spawner, BotCreationData data)
    {
        if (!IsActive) return true;
        if (_current == null || !_current.Matches(spawner, data) || _current.Consumed) return false;
        _current.Consumed = true;
        return true;
    }

    internal static void MarkRejected(BotSpawner spawner, BotCreationData data)
    {
        if (_current?.Matches(spawner, data) == true) _current.WasRejected = true;
    }

    internal static bool CanActivate(BotCreatorClient creator, BotCreationData data)
    {
        if (!IsActive) return true;
        var lease = _current;
        if (lease == null || !lease.MatchesCreator(creator, data) || !lease.Consumed || lease.ActivationConsumed
            || data.Count != 1) return false;
        lease.ActivationConsumed = true;
        return true;
    }

    internal static bool CanCreate(BotCreatorClient creator, Profile profile)
    {
        if (!IsActive) return true;
        var lease = _current;
        if (lease == null || !lease.MatchesCreator(creator, lease._data) || !lease.ActivationConsumed
            || lease.ProfileConsumed || lease._data.Count != 1 || !ReferenceEquals(profile, lease._data.Profiles[0])) return false;
        lease.ProfileConsumed = true;
        return true;
    }

    internal sealed class Lease : IDisposable
    {
        private readonly BotSpawner _spawner;
        internal readonly BotCreationData _data;
        private readonly AbstractGame _game;
        private readonly GameWorld _world;
        private readonly Lease _previous;
        private bool _disposed;
        internal bool Consumed;
        internal bool ActivationConsumed;
        internal bool ProfileConsumed;
        internal bool WasRejected;

        internal Lease(BotSpawner spawner, BotCreationData data, Lease previous)
        {
            _spawner = spawner; _data = data; _previous = previous;
            _game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
            _world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        }

        internal bool Matches(BotSpawner spawner, BotCreationData data)
            => !_disposed && !ReferenceEquals(_spawner, null) && !ReferenceEquals(_data, null)
                && ReferenceEquals(spawner, _spawner) && ReferenceEquals(data, _data)
                && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
                && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world);

        internal bool MatchesCreator(BotCreatorClient creator, BotCreationData data)
            => Matches(_spawner, data) && ReferenceEquals(_spawner._botCreator, creator);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(_current, this)) return;
            var previous = _previous;
            while (previous != null && previous._disposed) previous = previous._previous;
            _current = previous;
        }
    }
}
