using BepInEx.Configuration;
using System;

namespace ArenaMode.Config;

public static class Settings
{
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<string> Maps { get; private set; }
    public static ConfigEntry<string> SelectedKit { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }
    public static ConfigEntry<bool> MovementEnabled { get; private set; }
    public static ConfigEntry<float> WalkSpeedMult { get; private set; }
    public static ConfigEntry<float> SprintSpeedMult { get; private set; }
    public static ConfigEntry<float> ExtraMotionScale { get; private set; }
    public static ConfigEntry<float> AdsMoveFactor { get; private set; }
    public static ConfigEntry<float> AdsTransitionMult { get; private set; }
    public static ConfigEntry<bool> InfiniteStamina { get; private set; }
    public static ConfigEntry<bool> NoInertia { get; private set; }
    public static ConfigEntry<bool> InstantLean { get; private set; }
    public static ConfigEntry<bool> SlideEnabled { get; private set; }
    public static ConfigEntry<bool> NoBushSlowdown { get; private set; }
    public static ConfigEntry<float> JumpHeightMult { get; private set; }
    public static ConfigEntry<bool> NoFallDamage { get; private set; }
    public static ConfigEntry<bool> SprintToAds { get; private set; }
    public static ConfigEntry<bool> FastDeploy { get; private set; }
    public static ConfigEntry<bool> InstantSearch { get; private set; }
    public static ConfigEntry<bool> AutoSearchCorpses { get; private set; }
    public static ConfigEntry<bool> ShowVictimNames { get; private set; }
    public static ConfigEntry<int> MaxCorpses { get; private set; }

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

        MovementEnabled = config.Bind(
            "Movement",
            "Enabled",
            true,
            "Arena movement overhaul (all knobs below only apply during arena sessions).");

        WalkSpeedMult = config.Bind(
            "Movement",
            "WalkSpeedMult",
            1.25f,
            new ConfigDescription(
                "Base movement speed multiplier (effect tops out at an internal cap of 1.0 total — " +
                "values much above ~1.4 change little; sprint scales separately).",
                new AcceptableValueRange<float>(1f, 1.5f)));

        SprintSpeedMult = config.Bind(
            "Movement",
            "SprintSpeedMult",
            1.3f,
            new ConfigDescription("Sprint speed multiplier.", new AcceptableValueRange<float>(1f, 2f)));

        ExtraMotionScale = config.Bind(
            "Movement",
            "ExtraMotionScale",
            1.1f,
            new ConfigDescription(
                "Horizontal displacement scale beyond the animator (slight foot-slide above ~1.3).",
                new AcceptableValueRange<float>(1f, 1.6f)));

        AdsMoveFactor = config.Bind(
            "Movement",
            "AdsMoveFactor",
            0.7f,
            new ConfigDescription(
                "How much of the aim-down-sights movement penalty is removed (1 = no ADS slowdown).",
                new AcceptableValueRange<float>(0f, 1f)));

        AdsTransitionMult = config.Bind(
            "Movement",
            "AdsTransitionMult",
            1.5f,
            new ConfigDescription("Aim-down-sights transition speed multiplier.", new AcceptableValueRange<float>(1f, 3f)));

        InfiniteStamina = config.Bind(
            "Movement",
            "InfiniteStamina",
            true,
            "Keep legs/arms stamina and breath topped off.");

        NoInertia = config.Bind(
            "Movement",
            "NoInertia",
            true,
            "Remove movement inertia (direction changes, pose changes, sprint braking).");

        InstantLean = config.Bind(
            "Movement",
            "InstantLean",
            true,
            "Use the game's legacy (pre-inertia) fast lean.");

        SlideEnabled = config.Bind(
            "Movement",
            "SlideEnabled",
            true,
            "Sprint-into-crouch combat slide (absorbed from CombatSlide, arena-only).");

        NoBushSlowdown = config.Bind(
            "Movement",
            "NoBushSlowdown",
            true,
            "Foliage no longer slows movement.");

        JumpHeightMult = config.Bind(
            "Movement",
            "JumpHeightMult",
            1.3f,
            new ConfigDescription("Jump height multiplier.", new AcceptableValueRange<float>(1f, 2f)));

        NoFallDamage = config.Bind(
            "Movement",
            "NoFallDamage",
            true,
            "Disable fall damage (pairs with the jump boost).");

        SprintToAds = config.Bind(
            "Movement",
            "SprintToAds",
            true,
            "Aim input during a sprint cancels the sprint and enters ADS in one motion.");

        FastDeploy = config.Bind(
            "General",
            "FastDeploy",
            true,
            "Shorten the pre-raid deploy countdown to 2s for arena raids.");

        InstantSearch = config.Bind(
            "Loot",
            "InstantSearch",
            true,
            "Corpse/container searches complete instantly during arena sessions.");

        AutoSearchCorpses = config.Bind(
            "Loot",
            "AutoSearchCorpses",
            true,
            "Auto-search rig/pockets/backpack when opening a corpse (absorbed AutoCorpseSearch; " +
            "defers to the standalone mod when that is installed).");

        ShowVictimNames = config.Bind(
            "Loot",
            "ShowVictimNames",
            true,
            "End-of-raid kill list shows PMC names without requiring their dogtags.");

        MaxCorpses = config.Bind(
            "Performance",
            "MaxCorpses",
            10,
            new ConfigDescription(
                "Oldest corpses beyond this count are removed for performance (0 = unlimited).",
                new AcceptableValueRange<int>(0, 50)));
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
