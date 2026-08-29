using BepInEx.Configuration;
using System;
using UnityEngine;

namespace ArenaMode.Config;

public static class Settings
{
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<string> Maps { get; private set; }
    public static ConfigEntry<string> SelectedKit { get; private set; }
    public static ConfigEntry<bool> RandomKit { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }
    public static ConfigEntry<bool> MovementEnabled { get; private set; }
    public static ConfigEntry<float> WalkSpeedMult { get; private set; }
    public static ConfigEntry<float> SprintSpeedMult { get; private set; }
    public static ConfigEntry<float> ExtraMotionScale { get; private set; }
    public static ConfigEntry<float> AdsMoveFactor { get; private set; }
    public static ConfigEntry<float> AdsTransitionMult { get; private set; }
    public static ConfigEntry<bool> InfiniteStamina { get; private set; }
    public static ConfigEntry<bool> NoInertia { get; private set; }
    public static ConfigEntry<float> StrafeSmoothing { get; private set; }
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
    public static ConfigEntry<bool> CorpseLightsOff { get; private set; }
    public static ConfigEntry<string> GameMode { get; private set; }
    public static ConfigEntry<bool> HudEnabled { get; private set; }
    public static ConfigEntry<bool> ShowKillfeed { get; private set; }
    public static ConfigEntry<bool> KillfeedOtherKills { get; private set; }
    public static ConfigEntry<bool> ShowHitmarker { get; private set; }
    public static ConfigEntry<bool> ShowDamageNumbers { get; private set; }
    public static ConfigEntry<bool> AnnouncerEnabled { get; private set; }
    public static ConfigEntry<float> AnnouncerVolume { get; private set; }
    public static ConfigEntry<float> RagdollImpulseScale { get; private set; }
    public static ConfigEntry<int> PmcFloor { get; private set; }
    public static ConfigEntry<bool> HuntPlayer { get; private set; }
    public static ConfigEntry<bool> MaxSkills { get; private set; }
    public static ConfigEntry<bool> NoFractures { get; private set; }
    public static ConfigEntry<int> RoundKillTarget { get; private set; }
    public static ConfigEntry<int> RoundTimeLimitSeconds { get; private set; }
    public static ConfigEntry<int> RoundIntermissionSeconds { get; private set; }
    public static ConfigEntry<bool> RoundRespawn { get; private set; }
    public static ConfigEntry<string> GunGameLadder { get; private set; }
    public static ConfigEntry<int> GunGameSpareMagazines { get; private set; }
    public static ConfigEntry<bool> GunGameLoseTierOnDeath { get; private set; }
    public static ConfigEntry<bool> ScaleDifficulty { get; private set; }
    public static ConfigEntry<int> PressureRampMinutes { get; private set; }
    public static ConfigEntry<int> PressureStreakPercent { get; private set; }
    public static ConfigEntry<int> PressurePmcBonus { get; private set; }
    public static ConfigEntry<BotDifficulty> MaxBotDifficulty { get; private set; }
    public static ConfigEntry<KeyboardShortcut> PanelHotkey { get; private set; }
    public static ConfigEntry<KeyboardShortcut> GiveUpHotkey { get; private set; }

    /// <summary>The plugin's config file — panels batch writes through it.</summary>
    public static ConfigFile File { get; private set; }

    public static void Init(ConfigFile config)
    {
        File = config;

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

        RandomKit = config.Bind(
            "Kits",
            "RandomKit",
            true,
            "Draw a random kit for every life instead of a fixed one, avoiding the " +
            "same loadout twice in a row. Overrides SelectedKit while on.");

        SelectedKit = config.Bind(
            "Kits",
            "SelectedKit",
            "",
            "Kit id from the server catalog (user/mods/ArenaMode/config/kits.json). " +
            "Empty = first kit in the catalog. Ignored while RandomKit is on.");

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
                "Walk/strafe displacement multiplier (stacks with ExtraMotionScale; " +
                "legs animate at vanilla pace, so high values show light foot-slide).",
                new AcceptableValueRange<float>(1f, 1.5f)));

        SprintSpeedMult = config.Bind(
            "Movement",
            "SprintSpeedMult",
            1.3f,
            new ConfigDescription(
                "Sprint displacement multiplier (stacks with ExtraMotionScale; legs " +
                "animate at vanilla pace, so high values show light foot-slide).",
                new AcceptableValueRange<float>(1f, 2f)));

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

        StrafeSmoothing = config.Bind(
            "Movement",
            "StrafeSmoothing",
            0.25f,
            new ConfigDescription(
                "Scales the game's 0.15s walking direction-blend floor with NoInertia on " +
                "(0 = instant strafe flips, 1 = full vanilla blend). Applies immediately.",
                new AcceptableValueRange<float>(0f, 1f)));

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
                "Visible corpses kept. Older ones are hidden for performance — they stay " +
                "in the world but become invisible and can no longer be looted (0 = keep all).",
                new AcceptableValueRange<int>(0, 50)));

        PmcFloor = config.Bind(
            "Bots",
            "PmcFloor",
            8,
            new ConfigDescription(
                "Minimum PMCs alive. Top-up waves spawn PMCs (alternating USEC/BEAR) " +
                "until this many are around, and only fill up with scavs after that.",
                new AcceptableValueRange<int>(0, 20)));

        HuntPlayer = config.Bind(
            "Bots",
            "HuntPlayer",
            true,
            "Keep pointing every living bot at the player's position, so they " +
            "actively close in instead of patrolling until they happen to see you.");

        CorpseLightsOff = config.Bind(
            "Performance",
            "CorpseLightsOff",
            true,
            "Switch off flashlights and lasers on corpses. Shadow-casting lights " +
            "are expensive and dead bodies keep theirs on forever; a looted " +
            "flashlight still works normally.");

        MaxSkills = config.Bind(
            "Character",
            "MaxSkills",
            true,
            "Run every character skill at elite level during an arena raid (kit " +
            "weapons count as mastered too). Reverted when the raid ends; the " +
            "saved profile is never touched.");

        NoFractures = config.Bind(
            "Character",
            "NoFractures",
            true,
            "Limbs never fracture during an arena raid. The Health skill alone " +
            "only cuts the chance by about a third, which is not enough for an " +
            "arcade mode.");

        GameMode = config.Bind(
            "Match",
            "GameMode",
            "endless",
            "Arena game mode for the next raid: \"endless\" (pure counter) or " +
            "\"rounds\" (kill target / time limit with a scoreboard between rounds).");

        RoundKillTarget = config.Bind(
            "Match",
            "RoundKillTarget",
            20,
            new ConfigDescription(
                "Rounds mode: kills that end a round (0 = time limit only).",
                new AcceptableValueRange<int>(0, 200)));

        RoundTimeLimitSeconds = config.Bind(
            "Match",
            "RoundTimeLimitSeconds",
            300,
            new ConfigDescription(
                "Rounds mode: seconds that end a round (0 = kill target only).",
                new AcceptableValueRange<int>(0, 1800)));

        RoundIntermissionSeconds = config.Bind(
            "Match",
            "RoundIntermissionSeconds",
            15,
            new ConfigDescription(
                "Rounds mode: scoreboard time between rounds.",
                new AcceptableValueRange<int>(3, 60)));

        RoundRespawn = config.Bind(
            "Rounds",
            "RoundRespawn",
            true,
            "Respawn with a fresh kit when a round ends, at a spawn point away " +
            "from the fight. Off: keep standing where the round ended, healed up.");

        GunGameLadder = config.Bind(
            "Gun Game",
            "GunGameLadder",
            "",
            "Rung ids in ladder order, comma separated. Empty uses the generated " +
            "weapon ladder (config/ladder.json), or the kit catalog if there is none.");

        GunGameSpareMagazines = config.Bind(
            "Gun Game",
            "GunGameSpareMagazines",
            2,
            new ConfigDescription(
                "Loaded magazines handed out with each new gun.",
                new AcceptableValueRange<int>(0, 6)));

        GunGameLoseTierOnDeath = config.Bind(
            "Gun Game",
            "GunGameLoseTierOnDeath",
            false,
            "Drop back one tier when you die. Off by default — losing progress " +
            "to a grenade you never saw is not what makes gun game fun.");

        ScaleDifficulty = config.Bind(
            "Difficulty",
            "ScaleDifficulty",
            true,
            "Let the arena push back harder as the raid goes on and as you go on " +
            "a run. Off: the fixed difficulties this mod used before.");

        PressureRampMinutes = config.Bind(
            "Difficulty",
            "PressureRampMinutes",
            12,
            new ConfigDescription(
                "Minutes of raid time to reach full pressure on the clock alone. " +
                "This half only ever climbs — it is what stops a long raid from " +
                "getting easier as you settle in.",
                new AcceptableValueRange<int>(1, 60)));

        PressureStreakPercent = config.Bind(
            "Difficulty",
            "PressureStreakPercent",
            35,
            new ConfigDescription(
                "How much a kill streak adds on top of the clock, at eight kills " +
                "without dying. Dying clears this part, never the clock.",
                new AcceptableValueRange<int>(0, 100)));

        PressurePmcBonus = config.Bind(
            "Difficulty",
            "PressurePmcBonus",
            4,
            new ConfigDescription(
                "Extra PMCs kept alive at full pressure, on top of the PMC floor. " +
                "A heavier mix is the gentlest way to make a raid harder.",
                new AcceptableValueRange<int>(0, 12)));

        MaxBotDifficulty = config.Bind(
            "Difficulty",
            "MaxBotDifficulty",
            BotDifficulty.impossible,
            "Ceiling for the scaling. PMCs reach it late in a hot raid; drop this " +
            "to hard if the top end is too much. Only hard and impossible do " +
            "anything: this bounds the CLIMB, and anything lower is ignored rather " +
            "than quietly making the arena easier than it has ever been.");

        PanelHotkey = config.Bind(
            "General",
            "PanelHotkey",
            new KeyboardShortcut(KeyCode.F9),
            "Opens the in-raid arena panel (scoreboard and live settings).");

        GiveUpHotkey = config.Bind(
            "General",
            "GiveUpHotkey",
            new KeyboardShortcut(KeyCode.Backspace),
            "Ends the current life and respawns immediately — for when you are " +
            "bleeding out with nothing left to do. Counts as a death.");

        HudEnabled = config.Bind(
            "HUD",
            "Enabled",
            true,
            "Arena HUD (score panel, killfeed, callouts) during arena sessions.");

        ShowKillfeed = config.Bind(
            "HUD",
            "Killfeed",
            true,
            "Killfeed lines (victim, weapon, distance) in the top-right corner.");

        KillfeedOtherKills = config.Bind(
            "HUD",
            "KillfeedOtherKills",
            false,
            "Also show bot-vs-bot kills in the killfeed.");

        ShowHitmarker = config.Bind(
            "HUD",
            "Hitmarker",
            true,
            "Crosshair hitmarker on dealt damage (kill and headshot variants).");

        ShowDamageNumbers = config.Bind(
            "HUD",
            "DamageNumbers",
            true,
            "Floating damage numbers at the hit position.");

        AnnouncerEnabled = config.Bind(
            "Audio",
            "Announcer",
            true,
            "Announcer stingers (multi-kills, streaks) — plays .ogg files from " +
            "BepInEx/plugins/ArenaMode/sounds/, silently disabled when absent.");

        AnnouncerVolume = config.Bind(
            "Audio",
            "AnnouncerVolume",
            0.8f,
            new ConfigDescription("Announcer volume.", new AcceptableValueRange<float>(0f, 1f)));

        RagdollImpulseScale = config.Bind(
            "Effects",
            "RagdollImpulseScale",
            3f,
            new ConfigDescription(
                "Fatal-shot ragdoll impulse multiplier (1 = vanilla, punchy deaths above).",
                new AcceptableValueRange<float>(1f, 10f)));
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
