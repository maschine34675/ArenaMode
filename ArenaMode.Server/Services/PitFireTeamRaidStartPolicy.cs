namespace ArenaMode.Server.Services;

/// <summary>Pure fail-closed policy shared by the raid-start router and tests.</summary>
public static class PitFireTeamRaidStartPolicy
{
    public static string? GetRejectionReason(
        bool pitFireTeamTeamRequested,
        string? arenaMode,
        bool persistenceProtectionReady,
        string? incompatibilityReason)
    {
        if (!pitFireTeamTeamRequested)
            return null;

        if (!string.Equals(arenaMode, "tactical", StringComparison.OrdinalIgnoreCase))
            return "PitFireTeam companions are supported only in Tactical Arena.";

        if (persistenceProtectionReady)
            return null;

        return string.IsNullOrWhiteSpace(incompatibilityReason)
            ? "PitFireTeam persistence protection is unavailable."
            : incompatibilityReason;
    }
}
