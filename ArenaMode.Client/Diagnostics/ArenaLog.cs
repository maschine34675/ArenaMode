namespace ArenaMode.Diagnostics;

/// <summary>
/// Opt-in diagnostic traces. Ordinary lifecycle detail uses LogDebug directly;
/// player-facing events and actionable failures keep their normal severity.
/// </summary>
internal static class ArenaLog
{
    internal static bool DeveloperEnabled => Config.Settings.DeveloperMode?.Value == true;

    // Callers collecting expensive context must check DeveloperEnabled first.
    // Info is intentional: enabling DeveloperMode alone must make probes visible
    // without also changing BepInEx's global Debug filter for every installed mod.
    internal static void Developer(string message)
    {
        if (DeveloperEnabled)
            Plugin.ModLogger?.LogInfo("[dev] " + message);
    }
}
