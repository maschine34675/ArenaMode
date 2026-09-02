using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Immutable, process-local movement authority published by the protected team
/// Arena pursuit service. The optional BigBrain companion consumes this API by
/// profile ID; neither side retains a BotOwner across a round or raid boundary.
/// </summary>
public readonly struct TeamArenaPursuitLease
{
    internal TeamArenaPursuitLease(
        string sourceProfileId,
        string targetProfileId,
        Vector3 destination,
        int epoch,
        long generation,
        float issuedAt,
        float expiresAt)
    {
        SourceProfileId = sourceProfileId;
        TargetProfileId = targetProfileId;
        Destination = destination;
        Epoch = epoch;
        Generation = generation;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public string SourceProfileId { get; }
    public string TargetProfileId { get; }
    public Vector3 Destination { get; }
    public int Epoch { get; }
    public long Generation { get; }
    public float IssuedAt { get; }
    public float ExpiresAt { get; }
}

/// <summary>
/// Narrow public bridge for optional movement-owner companions. A round must
/// first claim an epoch. Publications from an old epoch, unbounded durations,
/// malformed identities and non-Arena reads are rejected rather than repaired.
/// </summary>
public static class TeamArenaPursuitLeaseBridge
{
    private readonly struct ConsumerRejection
    {
        public ConsumerRejection(string targetProfileId, int epoch, long generation)
        {
            TargetProfileId = targetProfileId;
            Epoch = epoch;
            Generation = generation;
        }

        public string TargetProfileId { get; }
        public int Epoch { get; }
        public long Generation { get; }
    }

    private readonly struct ConsumerExecution
    {
        public ConsumerExecution(long generation, float heartbeatAt)
        {
            Generation = generation;
            HeartbeatAt = heartbeatAt;
        }

        public long Generation { get; }
        public float HeartbeatAt { get; }
    }

    public const float MaximumLeaseLifetimeSeconds = 5f;
    public const float MaximumHeartbeatAgeSeconds = 1.5f;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, TeamArenaPursuitLease> LeasesBySource =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> ReadySourceRegistrations =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ConsumerExecution> ExecutionsBySource =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ConsumerRejection> RejectionsBySource =
        new(StringComparer.Ordinal);

    private static int _activeEpoch = -1;
    private static long _nextGeneration;
    private static string _consumerId;

    /// <summary>
    /// Exposes only the main plugin's exact raid/team gate so optional action
    /// owners never need access to Arena's internal session or PFT adapters.
    /// </summary>
    public static bool HasExactTeamArenaContext()
    {
        GameWorld world = Singleton<GameWorld>.Instance;
        AbstractGame game = Singleton<AbstractGame>.Instance;
        return ArenaSession.IsExactActiveRaid(game, world)
               && PitFireTeamCompat.TeamArenaActive;
    }

    /// <summary>
    /// Claims the single optional movement-consumer slot. Publication returns
    /// false until a companion has registered, preserving Arena's existing
    /// fallback when BigBrain or the companion is absent.
    /// </summary>
    public static bool RegisterConsumer(string consumerId)
    {
        string candidate = consumerId?.Trim();
        if (string.IsNullOrEmpty(candidate))
            return false;

        lock (Sync)
        {
            if (!string.IsNullOrEmpty(_consumerId)
                && !string.Equals(_consumerId, candidate, StringComparison.Ordinal))
            {
                return false;
            }

            _consumerId = candidate;
            ReadySourceRegistrations.Clear();
            return true;
        }
    }

    /// <summary>
    /// Marks one profile as having received the consumer's concrete movement
    /// layer. A globally loaded companion cannot claim bots whose brain name
    /// BigBrain did not match.
    /// </summary>
    public static bool RegisterSource(string consumerId, string sourceProfileId)
    {
        string consumer = consumerId?.Trim();
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(consumer) || string.IsNullOrEmpty(source))
            return false;

        lock (Sync)
        {
            if (!string.Equals(_consumerId, consumer, StringComparison.Ordinal))
                return false;

            ReadySourceRegistrations.TryGetValue(source, out int registrations);
            if (registrations == int.MaxValue)
                return false;

            ReadySourceRegistrations[source] = registrations + 1;
            return true;
        }
    }

    /// <summary>Removes only the exact consumer's concrete profile claim.</summary>
    public static void UnregisterSource(string consumerId, string sourceProfileId)
    {
        string consumer = consumerId?.Trim();
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(consumer) || string.IsNullOrEmpty(source))
            return;

        lock (Sync)
        {
            if (!string.Equals(_consumerId, consumer, StringComparison.Ordinal))
                return;

            if (!ReadySourceRegistrations.TryGetValue(source, out int registrations))
                return;

            if (registrations > 1)
            {
                ReadySourceRegistrations[source] = registrations - 1;
                return;
            }

            ReadySourceRegistrations.Remove(source);
            ExecutionsBySource.Remove(source);
            RejectionsBySource.Remove(source);
            if (LeasesBySource.Remove(source))
                AdvanceGenerationUnsafe();
        }
    }

    /// <summary>
    /// Releases only the exact registered consumer and invalidates its leases.
    /// A later consumer must also receive a fresh BeginEpoch from Arena.
    /// </summary>
    public static void UnregisterConsumer(string consumerId)
    {
        string candidate = consumerId?.Trim();
        if (string.IsNullOrEmpty(candidate))
            return;

        lock (Sync)
        {
            if (!string.Equals(_consumerId, candidate, StringComparison.Ordinal))
                return;

            _consumerId = null;
            ReadySourceRegistrations.Clear();
            LeasesBySource.Clear();
            ExecutionsBySource.Clear();
            RejectionsBySource.Clear();
            _activeEpoch = -1;
            AdvanceGenerationUnsafe();
        }
    }

    /// <summary>
    /// Starts a new exact round owner and invalidates every lease from the
    /// previous owner, including a previous raid which happened to reuse the
    /// same integer epoch.
    /// </summary>
    public static void BeginEpoch(int epoch)
    {
        lock (Sync)
        {
            LeasesBySource.Clear();
            ExecutionsBySource.Clear();
            RejectionsBySource.Clear();
            _activeEpoch = epoch >= 0 ? epoch : -1;
            AdvanceGenerationUnsafe();
        }
    }

    /// <summary>
    /// Publishes or refreshes one source bot's authority. Refreshing the same
    /// live source/target/epoch tuple preserves its generation so BigBrain can
    /// update a moving destination without restarting the action. A changed,
    /// revoked or expired tuple receives a new generation.
    /// </summary>
    public static bool Publish(
        string sourceProfileId,
        string targetProfileId,
        Vector3 destination,
        int epoch,
        float lifetimeSeconds,
        out TeamArenaPursuitLease lease)
    {
        lease = default;
        string source = sourceProfileId?.Trim();
        string target = targetProfileId?.Trim();
        float now = Time.time;

        if (!HasExactPostDeathContext()
            || string.IsNullOrEmpty(source)
            || string.IsNullOrEmpty(target)
            || string.Equals(source, target, StringComparison.Ordinal)
            || epoch < 0
            || !IsFinite(destination)
            || !IsFinite(lifetimeSeconds)
            || lifetimeSeconds <= 0f
            || lifetimeSeconds > MaximumLeaseLifetimeSeconds)
        {
            return false;
        }

        lock (Sync)
        {
            if (_activeEpoch != epoch
                || string.IsNullOrEmpty(_consumerId)
                || !ReadySourceRegistrations.ContainsKey(source))
                return false;

            long generation;
            float issuedAt;
            if (LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                && current.Epoch == epoch
                && current.Generation > 0
                && current.ExpiresAt > now
                && string.Equals(current.TargetProfileId, target, StringComparison.Ordinal))
            {
                generation = current.Generation;
                issuedAt = current.IssuedAt;
            }
            else
            {
                generation = AdvanceGenerationUnsafe();
                issuedAt = now;
                ExecutionsBySource.Remove(source);
            }

            lease = new TeamArenaPursuitLease(
                source,
                target,
                destination,
                epoch,
                generation,
                issuedAt,
                now + lifetimeSeconds);
            LeasesBySource[source] = lease;
            return true;
        }
    }

    /// <summary>
    /// Acknowledges that the exact consumer action acquired and updated EFT's
    /// native route. Readiness alone is deliberately not treated as movement.
    /// </summary>
    public static bool ReportStarted(
        string consumerId,
        string sourceProfileId,
        int epoch,
        long generation)
    {
        string consumer = consumerId?.Trim();
        string source = sourceProfileId?.Trim();
        if (!HasExactPostDeathContext()
            || string.IsNullOrEmpty(consumer)
            || string.IsNullOrEmpty(source)
            || generation <= 0)
        {
            return false;
        }

        lock (Sync)
        {
            if (!LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                || !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                    string.Equals(_consumerId, consumer, StringComparison.Ordinal),
                    ReadySourceRegistrations.ContainsKey(source),
                    _activeEpoch,
                    current.Epoch,
                    current.Generation,
                    epoch,
                    generation))
            {
                return false;
            }

            ExecutionsBySource[source] = new ConsumerExecution(generation, Time.time);
            return true;
        }
    }

    /// <summary>
    /// Reports a concrete native route failure for the exact action generation.
    /// Arena consumes the target identity on its next sweep and switches this
    /// source to a guarded fallback for the remainder of the round.
    /// </summary>
    public static bool ReportRejected(
        string consumerId,
        string sourceProfileId,
        int epoch,
        long generation)
    {
        string consumer = consumerId?.Trim();
        string source = sourceProfileId?.Trim();
        if (!HasExactPostDeathContext()
            || string.IsNullOrEmpty(consumer)
            || string.IsNullOrEmpty(source)
            || generation <= 0)
        {
            return false;
        }

        lock (Sync)
        {
            if (!LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                || !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                    string.Equals(_consumerId, consumer, StringComparison.Ordinal),
                    ReadySourceRegistrations.ContainsKey(source),
                    _activeEpoch,
                    current.Epoch,
                    current.Generation,
                    epoch,
                    generation))
            {
                return false;
            }

            RejectionsBySource[source] = new ConsumerRejection(
                current.TargetProfileId,
                epoch,
                generation);
            LeasesBySource.Remove(source);
            ExecutionsBySource.Remove(source);
            AdvanceGenerationUnsafe();
            return true;
        }
    }

    internal static bool HasStarted(
        string sourceProfileId,
        int epoch,
        long generation)
    {
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(source) || generation <= 0)
            return false;

        lock (Sync)
        {
            float now = Time.time;
            return _activeEpoch == epoch
                   && LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                   && current.Epoch == epoch
                   && current.Generation == generation
                   && ExecutionsBySource.TryGetValue(source, out ConsumerExecution execution)
                   && execution.Generation == generation
                   && TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(
                       now,
                       execution.HeartbeatAt,
                       MaximumHeartbeatAgeSeconds);
        }
    }

    internal static bool TryConsumeRejection(
        string sourceProfileId,
        int epoch,
        out string targetProfileId)
    {
        targetProfileId = null;
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(source))
            return false;

        lock (Sync)
        {
            if (_activeEpoch != epoch
                || !RejectionsBySource.TryGetValue(source, out ConsumerRejection rejection)
                || rejection.Epoch != epoch
                || rejection.Generation <= 0)
            {
                return false;
            }

            RejectionsBySource.Remove(source);
            targetProfileId = rejection.TargetProfileId;
            return !string.IsNullOrEmpty(targetProfileId);
        }
    }

    /// <summary>Reads one currently owned, non-expired lease.</summary>
    public static bool TryGet(string sourceProfileId, out TeamArenaPursuitLease lease)
    {
        lease = default;
        string source = sourceProfileId?.Trim();
        if (!HasExactPostDeathContext() || string.IsNullOrEmpty(source))
            return false;

        float now = Time.time;
        lock (Sync)
        {
            if (_activeEpoch < 0
                || !LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current))
            {
                return false;
            }

            if (!IsValidUnsafe(current, source, now))
            {
                LeasesBySource.Remove(source);
                ExecutionsBySource.Remove(source);
                return false;
            }

            lease = current;
            return true;
        }
    }

    /// <summary>
    /// Reads only the exact lease generation captured by a running action.
    /// This prevents an old action from consuming a replacement lease.
    /// </summary>
    public static bool TryGet(
        string sourceProfileId,
        int epoch,
        long generation,
        out TeamArenaPursuitLease lease)
    {
        if (!TryGet(sourceProfileId, out lease)
            || lease.Epoch != epoch
            || lease.Generation != generation)
        {
            lease = default;
            return false;
        }

        return true;
    }

    /// <summary>Revokes an epoch's source lease without touching a newer round.</summary>
    public static bool Revoke(string sourceProfileId, int epoch)
    {
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(source))
            return false;

        lock (Sync)
        {
            if (_activeEpoch != epoch
                || !LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                || current.Epoch != epoch)
            {
                return false;
            }

            LeasesBySource.Remove(source);
            ExecutionsBySource.Remove(source);
            AdvanceGenerationUnsafe();
            return true;
        }
    }

    /// <summary>
    /// Revokes only the exact generation owned by a running action. A stale
    /// path failure therefore cannot cancel a newer target assignment.
    /// </summary>
    public static bool Revoke(string sourceProfileId, int epoch, long generation)
    {
        string source = sourceProfileId?.Trim();
        if (string.IsNullOrEmpty(source) || generation <= 0)
            return false;

        lock (Sync)
        {
            if (_activeEpoch != epoch
                || !LeasesBySource.TryGetValue(source, out TeamArenaPursuitLease current)
                || current.Epoch != epoch
                || current.Generation != generation)
            {
                return false;
            }

            LeasesBySource.Remove(source);
            ExecutionsBySource.Remove(source);
            AdvanceGenerationUnsafe();
            return true;
        }
    }

    /// <summary>Unconditionally invalidates all process-local movement authority.</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            LeasesBySource.Clear();
            ExecutionsBySource.Clear();
            RejectionsBySource.Clear();
            _activeEpoch = -1;
            AdvanceGenerationUnsafe();
        }
    }

    private static bool IsValidUnsafe(
        TeamArenaPursuitLease lease,
        string sourceProfileId,
        float now)
    {
        return lease.Epoch == _activeEpoch
               && lease.Epoch >= 0
               && lease.Generation > 0
               && IsFinite(lease.IssuedAt)
               && lease.IssuedAt <= lease.ExpiresAt
               && lease.ExpiresAt > now
               && IsFinite(lease.ExpiresAt)
               && IsFinite(lease.Destination)
               && !string.IsNullOrEmpty(_consumerId)
               && ReadySourceRegistrations.ContainsKey(sourceProfileId)
               && string.Equals(lease.SourceProfileId, sourceProfileId, StringComparison.Ordinal)
               && !string.IsNullOrEmpty(lease.TargetProfileId)
               && !string.Equals(
                   lease.SourceProfileId,
                   lease.TargetProfileId,
                   StringComparison.Ordinal);
    }

    private static bool IsFinite(Vector3 value)
        => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool HasExactPostDeathContext()
    {
        GameWorld world = Singleton<GameWorld>.Instance;
        return HasExactTeamArenaContext()
               && world.MainPlayer?.HealthController?.IsAlive == false;
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static long AdvanceGenerationUnsafe()
    {
        if (_nextGeneration == long.MaxValue)
            _nextGeneration = 1;
        else
            _nextGeneration++;

        if (_nextGeneration <= 0)
            _nextGeneration = 1;

        return _nextGeneration;
    }
}
