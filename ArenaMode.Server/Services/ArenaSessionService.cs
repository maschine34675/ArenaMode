using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace ArenaMode.Server.Services;

public enum ArenaSessionState
{
    Idle,
    Armed,
    InRaid,
}

/// <summary>
/// Per-session arena state machine: Idle → Armed (client announced an arena raid,
/// snapshot taken) → InRaid (StartLocalRaidAsync consumed the flag for a matching
/// map) → Idle (post-raid restore ran). Armed expires after a TTL so an aborted
/// matchmaking never leaves a stale flag behind.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ArenaSessionService(ISptLogger<ArenaSessionService> logger)
{
    private sealed class SessionEntry
    {
        public ArenaSessionState State;
        public string? Map;
        public string? Mode;
        public DateTime ArmedAtUtc;
    }

    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly Lock _lock = new();

    public void Arm(MongoId sessionId, string map, string? mode)
    {
        lock (_lock)
        {
            _sessions[sessionId.ToString()] = new SessionEntry
            {
                State = ArenaSessionState.Armed,
                Map = map.ToLowerInvariant(),
                Mode = mode,
                ArmedAtUtc = DateTime.UtcNow,
            };
        }

        logger.Info($"[ArenaMode] Armed for session {sessionId}: map={map}, mode={mode}");
    }

    /// <summary>
    /// Consume the armed flag when a local raid actually starts. Returns true only
    /// for a live (non-expired) flag whose map matches the starting raid.
    /// </summary>
    public bool TryConsumeArm(MongoId sessionId, string? location)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.Armed)
            {
                return false;
            }

            var age = DateTime.UtcNow - entry.ArmedAtUtc;
            if (age.TotalSeconds > ArenaContext.Config.ArmedFlagTtlSeconds)
            {
                logger.Warning($"[ArenaMode] Armed flag expired after {age.TotalSeconds:0}s — raid starts as a normal raid.");
                entry.State = ArenaSessionState.Idle;
                return false;
            }

            if (location == null || !string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning($"[ArenaMode] Armed for '{entry.Map}' but raid starts on '{location}' — raid starts as a normal raid.");
                entry.State = ArenaSessionState.Idle;
                return false;
            }

            entry.State = ArenaSessionState.InRaid;
            return true;
        }
    }

    public bool IsInRaid(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid;
        }
    }

    public bool IsArmedOrInRaid(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State != ArenaSessionState.Idle;
        }
    }

    public void MarkIdle(MongoId sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry))
                entry.State = ArenaSessionState.Idle;
        }
    }

    public (ArenaSessionState state, string? map, string? mode) GetStatus(MongoId sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry))
                return (entry.State, entry.Map, entry.Mode);

            return (ArenaSessionState.Idle, null, null);
        }
    }
}
