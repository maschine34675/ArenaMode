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
        public DateTime InRaidAtUtc;
        public object? LocationClone;
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
            entry.InRaidAtUtc = DateTime.UtcNow;
            Patches.BotTierScalePatch.ResetReporting();

            // A fresh ladder per raid, cut here rather than when the client asks
            // for it: the ask happens while the raid loads and can repeat.
            LadderPicker.DrawForRaid(
                ArenaContext.LadderJson,
                ArenaContext.Config.GunGameRungs,
                message => logger.Info(message));
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
            {
                entry.State = ArenaSessionState.Idle;
                entry.LocationClone = null;
            }
        }
    }

    /// <summary>Remember the per-raid LocationBase clone so the hostility postfix can
    /// verify by reference that it is adjusting OUR arena clone, not a normal raid's.</summary>
    public void StashLocationClone(MongoId sessionId, object clone)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry))
                entry.LocationClone = clone;
        }
    }

    public bool IsStashedClone(object candidate)
    {
        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                if (entry.State == ArenaSessionState.InRaid && ReferenceEquals(entry.LocationClone, candidate))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Any session currently in an arena raid on the given location?</summary>
    /// <summary>
    /// Whether an InRaid entry can still plausibly BE a raid.
    ///
    /// Nothing removes session entries and only the armed flag has a deadline,
    /// so a raid that ended by crash or Alt-F4 leaves its entry claiming to be
    /// in progress for the life of the server. The map-wide lookups below would
    /// then answer for a raid that finished hours ago — and since the tier ramp
    /// clamps at its maximum, a later NORMAL raid on the same map would face
    /// fully kitted bots. The same shape of deadline the armed flag already has,
    /// with slack for a raid that ran into overtime.
    /// </summary>
    private static bool IsLiveRaid(SessionEntry entry)
    {
        if (entry.State != ArenaSessionState.InRaid)
            return false;

        if (entry.InRaidAtUtc == default)
            return false;

        var limit = TimeSpan.FromMinutes(Math.Max(1, ArenaContext.Config.RaidTimeMinutes) + 15);
        return DateTime.UtcNow - entry.InRaidAtUtc <= limit;
    }

    /// <summary>
    /// How long the arena raid on this map has been running. The bot tier ramp
    /// rides on this: it is read fresh on every bot generation request, so the
    /// answer moving is the whole point.
    /// </summary>
    public bool TryGetRaidElapsed(string? location, out TimeSpan elapsed)
    {
        elapsed = TimeSpan.Zero;
        if (string.IsNullOrEmpty(location))
            return false;

        var wanted = location!.ToLowerInvariant();
        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                if (!IsLiveRaid(entry) || !string.Equals(entry.Map, wanted, StringComparison.Ordinal))
                    continue;

                elapsed = DateTime.UtcNow - entry.InRaidAtUtc;
                return true;
            }
        }

        return false;
    }

    public bool AnyInRaidOnMap(string? location)
    {
        if (location == null)
            return false;

        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                // Same staleness guard: a crashed raid must not keep raising the
                // bot cap for every ordinary raid on this map afterwards.
                if (IsLiveRaid(entry)
                    && string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
