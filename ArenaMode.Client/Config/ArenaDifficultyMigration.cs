using System;

namespace ArenaMode.Config;

/// <summary>
/// One-time migration of the retired [Difficulty] settings (ScaleDifficulty,
/// MaxBotDifficulty) to the single Step setting. Pure on purpose: the bind site hands in
/// the saved strings and gets back the step to select, or null to leave the default.
/// </summary>
internal static class ArenaDifficultyMigration
{
    /// <summary>
    /// Bumped when a later change needs another one-time rewrite. The retired lines stay in
    /// the player's file for good (BepInEx writes unbound lines back on every save), so a
    /// later revision must not read them again: it would undo a Maximum chosen by hand.
    /// </summary>
    public const int Revision = 1;

    /// <summary>
    /// Reduced when the player had switched the scaling off or capped the bots below
    /// impossible; null otherwise (an unset, unparseable or default-like value changes
    /// nothing, so a fresh or untouched configuration keeps Maximum).
    /// </summary>
    public static Shared.ArenaDifficultyStep? StepFromLegacy(string savedScaleDifficulty, string savedMaxBotDifficulty)
    {
        if (bool.TryParse(savedScaleDifficulty?.Trim(), out var scale) && !scale)
            return Shared.ArenaDifficultyStep.Reduced;
        var cap = savedMaxBotDifficulty?.Trim();
        if (!string.IsNullOrEmpty(cap) && !string.Equals(cap, "impossible", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(cap, "easy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cap, "normal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cap, "hard", StringComparison.OrdinalIgnoreCase)))
            return Shared.ArenaDifficultyStep.Reduced;
        return null;
    }

    /// <summary>
    /// Reads the two retired keys of the [Difficulty] section out of a BepInEx .cfg text
    /// (comment lines and other sections are skipped) and applies <see cref="StepFromLegacy"/>.
    /// </summary>
    public static Shared.ArenaDifficultyStep? StepFromConfigText(string cfgText)
    {
        if (string.IsNullOrEmpty(cfgText))
            return null;
        bool inSection = false;
        string scale = null, cap = null;
        foreach (var raw in cfgText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                inSection = string.Equals(line.Substring(1, line.Length - 2).Trim(), "Difficulty", StringComparison.Ordinal);
                continue;
            }
            if (!inSection)
                continue;
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            if (string.Equals(key, "ScaleDifficulty", StringComparison.Ordinal)) scale = value;
            else if (string.Equals(key, "MaxBotDifficulty", StringComparison.Ordinal)) cap = value;
        }
        return StepFromLegacy(scale, cap);
    }
}