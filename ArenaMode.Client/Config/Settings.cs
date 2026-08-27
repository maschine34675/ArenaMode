using BepInEx.Configuration;
using System;

namespace ArenaMode.Config;

public static class Settings
{
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<string> Maps { get; private set; }
    public static ConfigEntry<string> SelectedKit { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind(
            "General",
            "Enabled",
            false,
            "Arena mode: the next raid on an arena map runs as an arena session " +
            "(arcade — nothing is gained or lost, the profile stays untouched).");

        Maps = config.Bind(
            "General",
            "Maps",
            "factory4_day",
            "Comma-separated location ids arena mode activates on.");

        SelectedKit = config.Bind(
            "Kits",
            "SelectedKit",
            "",
            "Kit id from the server catalog (user/mods/ArenaMode/config/kits.json). " +
            "Empty = first kit in the catalog.");

        SpawnProtectionSeconds = config.Bind(
            "Respawn",
            "SpawnProtectionSeconds",
            3f,
            "Invulnerability after an arena respawn (0 = disabled).");
    }

    public static bool IsArenaMap(string locationId)
    {
        if (string.IsNullOrEmpty(locationId))
            return false;

        foreach (var entry in Maps.Value.Split(','))
        {
            if (string.Equals(entry.Trim(), locationId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
