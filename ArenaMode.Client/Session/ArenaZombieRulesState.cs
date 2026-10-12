using ArenaMode.Config;
using ArenaMode.Modes;
using BepInEx.Configuration;
using System;

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
        ArenaZombieRule.MaxAlive => "MAX ALIVE",
        ArenaZombieRule.WaveStartSize => "WAVE START SIZE",
        ArenaZombieRule.WaveGrowth => "WAVE GROWTH",
        _ => string.Empty
    };

    internal static int Value(ArenaZombieRule rule) => Entry(rule)?.Value ?? 0;
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
