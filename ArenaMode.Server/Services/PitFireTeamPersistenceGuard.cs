namespace ArenaMode.Server.Services;

/// <summary>
/// Keeps PitFireTeam's separate teammate store read-only while an Arena raid is
/// protected. PitFireTeam stores those profiles outside SPT's main profile, so
/// the normal Arena snapshot cannot restore them after the raid.
///
/// This class deliberately knows only session ids and paths. Optional
/// PitFireTeam types are discovered and patched elsewhere, without a compile-time
/// dependency or an attempt to load the mod when it is absent.
/// </summary>
public sealed class PitFireTeamPersistenceGuard
{
    public static readonly TimeSpan PostRaidFinalizationWindow = TimeSpan.FromSeconds(30);

    private sealed record ProtectionWindow(DateTime ExpiresAtUtc);

    private readonly Dictionary<string, ProtectionWindow> _protectedSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly Func<DateTime> _utcNow;

    public PitFireTeamPersistenceGuard(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void BeginArmed(string sessionId, TimeSpan maximumDuration)
        => ProtectUntil(sessionId, _utcNow() + Positive(maximumDuration));

    public void BeginRaid(string sessionId, TimeSpan maximumDuration)
        => ProtectUntil(sessionId, _utcNow() + Positive(maximumDuration));

    public void BeginPostRaidFinalization(string sessionId)
        => ProtectUntil(sessionId, _utcNow() + PostRaidFinalizationWindow);

    public void Clear(string sessionId)
    {
        lock (_lock)
            _protectedSessions.Remove(sessionId);
    }

    public bool IsProtected(string sessionId)
    {
        lock (_lock)
        {
            RemoveExpiredLocked();
            return _protectedSessions.ContainsKey(sessionId);
        }
    }

    public bool AnyProtected()
    {
        lock (_lock)
        {
            RemoveExpiredLocked();
            return _protectedSessions.Count != 0;
        }
    }

    public bool IsSoleProtectedSession(string sessionId)
    {
        lock (_lock)
        {
            RemoveExpiredLocked();
            return _protectedSessions.Count == 1
                   && _protectedSessions.ContainsKey(sessionId);
        }
    }

    public bool IsEveryProtectedSessionAuthorized(Func<string, bool> authorizes)
    {
        lock (_lock)
        {
            RemoveExpiredLocked();
            return _protectedSessions.Count == 0
                   || _protectedSessions.Keys.All(authorizes);
        }
    }

    /// <summary>
    /// Match only the canonical PitFireTeam teammate directory belonging to one
    /// currently protected Arena session. Resolving both sides with GetFullPath
    /// makes "session/../other-session" fail open while an equivalent path that
    /// resolves back into the protected directory remains blocked.
    /// </summary>
    public bool ShouldBlockFileMutation(
        string? filePath,
        string? pitFireTeamModPath,
        out string? protectedSessionId)
    {
        protectedSessionId = null;
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(pitFireTeamModPath))
            return false;

        string candidate;
        string teammateRoot;
        try
        {
            candidate = Path.GetFullPath(filePath);
            teammateRoot = Path.GetFullPath(Path.Combine(
                pitFireTeamModPath,
                "Resources",
                "teammates"));
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }

        lock (_lock)
        {
            RemoveExpiredLocked();
            foreach (string sessionId in _protectedSessions.Keys)
            {
                string sessionRoot;
                try
                {
                    sessionRoot = Path.GetFullPath(Path.Combine(teammateRoot, sessionId));
                }
                catch
                {
                    // Session ids originate from MongoId. If an unexpected value
                    // reaches this guard, fail open instead of broadening the path.
                    continue;
                }

                if (!IsSamePathOrDescendant(candidate, sessionRoot))
                    continue;

                protectedSessionId = sessionId;
                return true;
            }
        }

        return false;
    }

    private void ProtectUntil(string sessionId, DateTime expiresAtUtc)
    {
        if (!IsMongoIdString(sessionId))
            return;

        lock (_lock)
        {
            RemoveExpiredLocked();
            _protectedSessions[sessionId] = new ProtectionWindow(expiresAtUtc);
        }
    }

    private void RemoveExpiredLocked()
    {
        DateTime now = _utcNow();
        foreach (string key in _protectedSessions
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _protectedSessions.Remove(key);
        }
    }

    private static bool IsSamePathOrDescendant(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            return true;

        string withSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMongoIdString(string value)
        => value.Length == 24 && value.All(Uri.IsHexDigit);

    private static TimeSpan Positive(TimeSpan value)
        => value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(1);
}
