using System;

namespace ArenaMode.Compat;

internal static partial class PitFireTeamCompat
{
    /// <summary>
    /// Bounded startup publishes the exact active raid before navigation is
    /// ready. Its mode has not yet had the opportunity to bind the leader.
    /// Defer only that initial empty state, never a lost/destroyed prior bind.
    /// Called after the existing error latch and live/version checks.
    /// </summary>
    internal static bool ShouldDeferInitialRosterDiscovery()
    {
        if (Areas.ArenaAreaRuntime.CurrentArea == null || Modes.ArenaMatchService.Current != null)
            return false;
        lock (StateLock)
            return ReferenceEquals(_currentLocalPlayer, null)
                && string.IsNullOrEmpty(_currentLocalProfileId)
                && CurrentBodiesByAccountId.Count == 0;
    }
}
