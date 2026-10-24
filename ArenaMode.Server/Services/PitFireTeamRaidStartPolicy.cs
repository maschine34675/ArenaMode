namespace ArenaMode.Server.Services;

/// <summary>Pure fail-closed policy shared by the raid-start router and tests.</summary>
public static class PitFireTeamRaidStartPolicy
{
    public static string? GetRejectionReason(
        bool pitFireTeamTeamRequested,
        string? arenaMode,
        bool persistenceProtectionReady,
        string? incompatibilityReason,
        bool pitFireTeamInstalled = false)
    {
        if (!pitFireTeamTeamRequested && !pitFireTeamInstalled)
            return null;

        if (pitFireTeamTeamRequested
            && !string.Equals(arenaMode, "tactical", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(arenaMode, "zombie", StringComparison.OrdinalIgnoreCase))
            return "PitFireTeam companions require Tactical or Zombies Arena.";

        if (persistenceProtectionReady)
            return null;

        return string.IsNullOrWhiteSpace(incompatibilityReason)
            ? "PitFireTeam persistence protection is unavailable."
            : incompatibilityReason;
    }
}
