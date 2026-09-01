using System.Threading;

namespace ArenaMode.Server.Services;

/// <summary>
/// Authorizes the exact teammate-file writes that belong to one deliberate PFT
/// management request during Arena's bounded post-raid finalization window.
/// AsyncLocal follows nested synchronous service calls, while the mutable active
/// bit invalidates execution-context copies after the Harmony finalizer exits.
/// </summary>
internal static class PitFireTeamManagementScope
{
    private sealed class ScopeToken(string sessionId, ScopeToken? previous)
    {
        public string SessionId { get; } = sessionId;
        public ScopeToken? Previous { get; } = previous;
        public int Active = 1;
    }

    private static readonly AsyncLocal<ScopeToken?> Current = new();

    internal static object Enter(string sessionId)
    {
        var token = new ScopeToken(sessionId, Current.Value);
        Current.Value = token;
        return token;
    }

    internal static void Exit(object? state)
    {
        if (state is not ScopeToken token)
            return;

        Interlocked.Exchange(ref token.Active, 0);
        if (ReferenceEquals(Current.Value, token))
            Current.Value = token.Previous;
    }

    internal static bool Authorizes(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        for (ScopeToken? token = Current.Value; token != null; token = token.Previous)
        {
            if (Volatile.Read(ref token.Active) != 0
                && string.Equals(token.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasActiveScope()
    {
        for (ScopeToken? token = Current.Value; token != null; token = token.Previous)
        {
            if (Volatile.Read(ref token.Active) != 0)
                return true;
        }

        return false;
    }
}
