using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Shared;
using BepInEx.Configuration;
using System;
using System.Globalization;

namespace ArenaMode.Session;

internal enum ArenaZombieRule { MaxAlive, WaveStartSize, WaveGrowth }

/// <summary>Menu-only access to the existing, frozen-at-start Zombie preferences.</summary>
internal sealed class ArenaZombieRulesState
{
    internal bool Visible { get; private set; }
    internal bool Editable => Visible && !ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid;
    internal void Synchronize(string mode) => Visible = ArenaModeCatalog.NormalizeId(mode) == ArenaModeCatalog.ZombieId;

    private static ConfigEntry<int> Entry(ArenaZombieRule rule) => rule switch
    {
        ArenaZombieRule.MaxAlive => Settings.ZombieMaxAlive,
        ArenaZombieRule.WaveStartSize => Settings.ZombieWaveStartSize,
        ArenaZombieRule.WaveGrowth => Settings.ZombieWaveGrowth,
        _ => null
    };

    internal static string Label(ArenaZombieRule rule) => rule switch
    {
        ArenaZombieRule.MaxAlive => "ALIVE AT ONCE",
        ArenaZombieRule.WaveStartSize => "ENEMIES IN WAVE 1",
        ArenaZombieRule.WaveGrowth => "EXTRA ENEMIES / WAVE",
        _ => string.Empty
    };

    internal static int Value(ArenaZombieRule rule) => Entry(rule)?.Value ?? 0;

    /// <summary>
    /// The difficulty step this menu describes: the step frozen at arm when the raid is armed (an F12 edit
    /// after the arm changes nothing for that raid, and the row must not promise it), otherwise the live
    /// setting, which is exactly what the arm path freezes next.
    /// </summary>
    internal static ArenaDifficultyStep CurrentStep
        => ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;

    /// <summary>
    /// What the row shows next to its slider. The slider stays the editor of the setting (<paramref name="setting"/>
    /// is what it holds and what the cfg keeps); only ALIVE AT ONCE can show something else, and only at
    /// Reduced when the cap bites (a setting above 24): the effective value first, then the setting, so the
    /// player reads what the raid will use. At Maximum, and at Reduced with a setting of 24 or less, it is the
    /// plain setting. The effective value comes from the same policy the raid reads
    /// (<see cref="ArenaDifficultyPolicy.ZombieMaxAlive"/>), so the row cannot drift from the rule.
    /// </summary>
    internal static string ValueText(ArenaZombieRule rule, int setting, ArenaDifficultyStep step)
    {
        if (rule == ArenaZombieRule.MaxAlive)
        {
            int effective = ArenaDifficultyPolicy.ZombieMaxAlive(step, setting);
            if (step == ArenaDifficultyStep.Reduced && effective != setting)
                return effective.ToString(CultureInfo.InvariantCulture) + " (setting "
                    + setting.ToString(CultureInfo.InvariantCulture) + ", Reduced)";
        }
        return setting.ToString(CultureInfo.InvariantCulture);
    }
    internal static AcceptableValueRange<int> Range(ArenaZombieRule rule)
        => Entry(rule)?.Description.AcceptableValues as AcceptableValueRange<int>;

    internal bool TrySelect(ArenaZombieRule rule, int value)
    {
        var entry = Entry(rule);
        var range = Range(rule);
        if (!Editable || entry == null || range == null || value < range.MinValue || value > range.MaxValue)
            return false;
        if (entry.Value == value) return true;
        try
        {
            entry.Value = value;
            if (entry.Value != value) return false;
            if (Settings.File?.SaveOnConfigSet == false) Settings.File.Save();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena Zombie setting {rule} could not be saved: {ex.Message}");
            return false;
        }
    }
}
