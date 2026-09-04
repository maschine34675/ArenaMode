using ArenaMode.Effects;
using ArenaMode.Modes;
using BepInEx.Configuration;
using System;
using UnityEngine;

namespace ArenaMode.Config;

public static class Settings
{
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
    public static ConfigEntry<float> CarryWeightLimitMultiplier { get; private set; }
    public static ConfigEntry<bool> NoInertia { get; private set; }
    public static ConfigEntry<float> StrafeSmoothing { get; private set; }
    public static ConfigEntry<bool> InstantLean { get; private set; }
    public static ConfigEntry<bool> SlideEnabled { get; private set; }
    public static ConfigEntry<bool> NoBushSlowdown { get; private set; }
    public static ConfigEntry<float> JumpHeightMult { get; private set; }
    public static ConfigEntry<bool> NoFallDamage { get; private set; }
    public static ConfigEntry<float> VaultSpeedMult { get; private set; }
    public static ConfigEntry<float> VaultReachMult { get; private set; }
    public static ConfigEntry<bool> SprintToAds { get; private set; }
    public static ConfigEntry<bool> FastDeploy { get; private set; }
    public static ConfigEntry<bool> SkipPreRaidScreens { get; private set; }
    public static ConfigEntry<bool> InstantSearch { get; private set; }
    public static ConfigEntry<bool> AutoSearchCorpses { get; private set; }
    public static ConfigEntry<bool> ShowVictimNames { get; private set; }
    public static ConfigEntry<int> MaxCorpses { get; private set; }
    public static ConfigEntry<bool> StripCorpseGear { get; private set; }
    public static ConfigEntry<bool> NoCorpseLooting { get; private set; }
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
    public static ConfigEntry<bool> ArenaLightingEnabled { get; private set; }
    public static ConfigEntry<float> ArenaLightingStrength { get; private set; }
    public static ConfigEntry<int> ArenaLightingPressureSeconds { get; private set; }
    public static ConfigEntry<float> ArenaLightingOutcomeSeconds { get; private set; }
    public static ConfigEntry<bool> ArenaLightingRespectExternalMods { get; private set; }
    public static ConfigEntry<bool> TeamTracers { get; private set; }
    public static ConfigEntry<ArenaTracerColor> FriendlyTracerColor { get; private set; }
    public static ConfigEntry<ArenaTracerColor> OpponentTracerColor { get; private set; }
    public static ConfigEntry<int> PmcFloor { get; private set; }
    public static ConfigEntry<bool> HuntPlayer { get; private set; }
    public static ConfigEntry<bool> MaxSkills { get; private set; }
    public static ConfigEntry<int> LegInjurySlowdownReductionPercent { get; private set; }
    public static ConfigEntry<float> MedicalUseSpeed { get; private set; }
    public static ConfigEntry<int> RoundKillTarget { get; private set; }
    public static ConfigEntry<int> RoundTimeLimitSeconds { get; private set; }
    public static ConfigEntry<int> RoundIntermissionSeconds { get; private set; }
    public static ConfigEntry<bool> RoundRespawn { get; private set; }
    public static ConfigEntry<string> GunGameLadder { get; private set; }
    public static ConfigEntry<int> GunGameSpareMagazines { get; private set; }
    public static ConfigEntry<bool> GunGameLoseTierOnDeath { get; private set; }
    public static ConfigEntry<bool> GunGameRedrawLadder { get; private set; }
    public static ConfigEntry<int> TacticalSquadSize { get; private set; }
    public static ConfigEntry<bool> TacticalSquadIsPmc { get; private set; }
    public static ConfigEntry<float> TacticalBuySeconds { get; private set; }
    public static ConfigEntry<float> TacticalRoundSeconds { get; private set; }
    public static ConfigEntry<int> TacticalStartMoney { get; private set; }
    public static ConfigEntry<int> TacticalKillReward { get; private set; }
    public static ConfigEntry<int> TacticalWinBonus { get; private set; }
    public static ConfigEntry<int> TacticalLossBonus { get; private set; }
    public static ConfigEntry<float> TacticalReplaySeconds { get; private set; }
    public static ConfigEntry<int> TacticalTeamRoundsToWin { get; private set; }
    public static ConfigEntry<bool> TacticalTeamSpectator { get; private set; }
    public static ConfigEntry<bool> ScaleDifficulty { get; private set; }
    public static ConfigEntry<int> RespawnMinDistance { get; private set; }
    public static ConfigEntry<int> PressureRampMinutes { get; private set; }
    public static ConfigEntry<int> PressureStreakPercent { get; private set; }
    public static ConfigEntry<int> PressurePmcBonus { get; private set; }
    public static ConfigEntry<BotDifficulty> MaxBotDifficulty { get; private set; }
    public static ConfigEntry<KeyboardShortcut> PanelHotkey { get; private set; }
    public static ConfigEntry<KeyboardShortcut> GiveUpHotkey { get; private set; }

    /// <summary>The plugin's config file — panels batch writes through it.</summary>
    public static ConfigFile File { get; private set; }

    public static string SelectedGameModeId
        => ArenaModeCatalog.NormalizeId(GameMode?.Value);

    /// <summary>
    /// Apply a native menu choice to the same entry used by F12 and raid start,
    /// then publish the client-owned value so a later settings sync sees it.
    /// </summary>
    internal static bool TrySelectGameMode(string modeId)
    {
        if (GameMode == null)
            return false;

        var normalized = ArenaModeCatalog.NormalizeId(modeId);
        if (string.Equals(GameMode.Value, normalized, StringComparison.Ordinal))
        {
            SettingsSync.Report();
            return true;
        }

        try
        {
            GameMode.Value = normalized;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena map-screen mode selection failed: {ex.Message}");
            return false;
        }

        try
        {
            // ConfigEntry saves immediately under BepInEx's normal setting.
            // If another Arena panel temporarily batches writes, flush this
            // explicit menu choice instead of leaving it only in memory.
            if (File?.SaveOnConfigSet == false)
                File.Save();
        }
        catch (Exception ex)
        {
            // The in-memory selection still owns this raid. Keep persistence
            // failure visible without lying to the picker about the active mode.
            Plugin.ModLogger?.LogWarning($"Arena map-screen mode save failed: {ex.Message}");
        }

        SettingsSync.Report();
        Plugin.ModLogger?.LogInfo($"Arena mode selected in map screen: {normalized}.");
        return true;
    }

    public static void Init(ConfigFile config)
    {
        File = config;

        Maps = config.Bind(
            "General",
            "Maps",
            "factory4_day,labyrinth,laboratory",
            "Arena map allowlist for the native Arena choice (Factory day, Labyrinth, The Lab). " +
            "Only maps also enabled on the server are offered. The Lab is " +
            "keycard-locked in the menu; arena raids waive that, ordinary ones do not.");

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

        CarryWeightLimitMultiplier = config.Bind(
            "Movement",
            "CarryWeightLimitMultiplier",
            2f,
            new ConfigDescription(
                "Raises the local Arena PMC's weight thresholds for sprinting, movement speed, " +
                "encumbrance and inertia (1 = vanilla, 2 = twice the normal thresholds).",
                new AcceptableValueRange<float>(1f, 4f)));

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

        VaultSpeedMult = config.Bind(
            "Movement",
            "VaultSpeedMult",
            1.6f,
            new ConfigDescription(
                "How much faster vaulting and climbing play out. Scales the whole " +
                "speed range, so an injured or overloaded vault stays slower than a " +
                "healthy one - it is just faster than it was.",
                new AcceptableValueRange<float>(1f, 2.5f)));

        VaultReachMult = config.Bind(
            "Movement",
            "VaultReachMult",
            1.3f,
            new ConfigDescription(
                "How much taller, longer and further away an obstacle may be and " +
                "still be vaultable. Only the upper limits move; the lower ones stay " +
                "put, so kerbs do not suddenly become vaults. The search grid is a " +
                "hard ceiling on this - see VaultingTuning.",
                new AcceptableValueRange<float>(1f, 1.6f)));

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

        SkipPreRaidScreens = config.Bind(
            "General",
            "SkipPreRaidScreens",
            true,
            "Go straight from the map to the loading screen for arena raids, past " +
            "Game Settings, Insurance and Confirmation. Nothing on those screens " +
            "survives an arena raid: bot amount and raid time are overwritten " +
            "server-side, insurance is void when the profile is restored afterwards, " +
            "and the loadout being confirmed is replaced by a kit on the first second.");

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

        StripCorpseGear = config.Bind(
            "Performance",
            "StripCorpseGear",
            true,
            "Take the gear off corpses once they are hidden. Every worn item is " +
            "an instantiated model, and late-raid bodies wear far more of them " +
            "than early ones — removing it hands each model back to its pool.");

        NoCorpseLooting = config.Bind(
            "Performance",
            "NoCorpseLooting",
            true,
            "Stop bots working over corpses. They swap gear on the spot and drop " +
            "what they were wearing, and nothing ever cleans a dropped item up.");

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

        LegInjurySlowdownReductionPercent = config.Bind(
            "Character",
            "LegInjurySlowdownReductionPercent",
            50,
            new ConfigDescription(
                "Reduction of the walking-speed penalty from fractured or destroyed " +
                "legs during an arena raid. 50 halves the penalty (one leg: 0.30 to " +
                "0.65; both legs: 0.20 to 0.60). Injuries and the vanilla sprint " +
                "restriction remain active; 0 keeps vanilla movement.",
                new AcceptableValueRange<int>(0, 100)));

        MedicalUseSpeed = config.Bind(
            "Character",
            "MedicalUseSpeed",
            2f,
            new ConfigDescription(
                "Application speed for the local PMC's medkits, bandages, splints, " +
                "surgery kits, pills and injectors during an arena raid. Both the " +
                "health effect and hands animation use this factor; 1 keeps vanilla timing.",
                new AcceptableValueRange<float>(1f, 4f)));

        GameMode = config.Bind(
            "Match",
            "GameMode",
            ArenaModeCatalog.EndlessId,
            new ConfigDescription(
                "Arena game mode selected on the native map screen or in F12. " +
                "endless: a pure counter. " +
                "rounds: kill target or time limit with a scoreboard between rounds. " +
                "gungame: climb a ladder of weapons, one rung per kill. " +
                "tactical: buy a loadout, fight one squad, and sit the round out if you fall.",
                new AcceptableValueList<string>(
                    ArenaModeCatalog.EndlessId,
                    ArenaModeCatalog.RoundsId,
                    ArenaModeCatalog.GunGameId,
                    ArenaModeCatalog.TacticalId)));

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

        TacticalSquadSize = config.Bind(
            "Tactical",
            "SquadSize",
            5,
            new ConfigDescription(
                "How many enemies a round spawns. The round is over when they are " +
                "all dead — or when you are.",
                new AcceptableValueRange<int>(1, 15)));

        TacticalSquadIsPmc = config.Bind(
            "Tactical",
            "SquadIsPmc",
            true,
            "Send PMCs rather than scavs. PMCs are the fight this mode is built " +
            "around; scavs make a round short and cheap.");

        TacticalBuySeconds = config.Bind(
            "Tactical",
            "BuySeconds",
            20f,
            new ConfigDescription("How long the buy phase lasts.", new AcceptableValueRange<float>(5f, 90f)));

        TacticalRoundSeconds = config.Bind(
            "Tactical",
            "RoundSeconds",
            240f,
            new ConfigDescription(
                "Safety net on a round that never resolves — a squad member stuck " +
                "somewhere unreachable would otherwise hold the match open forever.",
                new AcceptableValueRange<float>(60f, 900f)));

        TacticalStartMoney = config.Bind(
            "Tactical",
            "StartMoney",
            250000,
            "Roubles at the first buy phase. Kits are priced at their handbook " +
            "value minus the secure container, so this is a currency you already " +
            "have a feel for. The catalogue runs from about 180k to 590k, so this " +
            "buys the bottom third — round one is a choice, not a handout.");

        TacticalKillReward = config.Bind(
            "Tactical",
            "KillReward",
            35000,
            "Roubles per kill.");

        TacticalWinBonus = config.Bind(
            "Tactical",
            "WinBonus",
            150000,
            "Roubles for taking a round.");

        TacticalLossBonus = config.Bind(
            "Tactical",
            "LossBonus",
            80000,
            "Roubles for losing one. Not charity: without it a bad round becomes " +
            "an unrecoverable one, which is the failure mode every game with an " +
            "economy has to design against.");

        TacticalReplaySeconds = config.Bind(
            "Tactical",
            "ReplaySeconds",
            3f,
            new ConfigDescription(
                "Seconds of KillcamReplay after you go down. This is the one mode " +
                "with room for it — everywhere else you respawn before it could " +
                "play. 0 switches it off. Needs the KillcamReplay mod installed.",
                new AcceptableValueRange<float>(0f, 10f)));

        TacticalTeamRoundsToWin = config.Bind(
            "Tactical Team",
            "RoundsToWin",
            3,
            new ConfigDescription(
                "Protected PitFireTeam 5v5: rounds required to win the match. " +
                "Three produces a best-of-five match; one keeps the old single-round result.",
                new AcceptableValueRange<int>(1, 9)));

        TacticalTeamSpectator = config.Bind(
            "Tactical Team",
            "SpectatorAfterDeath",
            true,
            "Protected PitFireTeam 5v5: follow living teammates or opponents in third person " +
            "after your PMC dies, until the round ends.");

        GunGameRedrawLadder = config.Bind(
            "Gun Game",
            "GunGameRedrawLadder",
            true,
            "Cut a fresh ladder when you finish one, instead of climbing the same " +
            "weapons a second time in the same raid. The new rungs are warmed in " +
            "the background, so the promotions that follow do not wait for them.");

        ScaleDifficulty = config.Bind(
            "Difficulty",
            "ScaleDifficulty",
            true,
            "Let the arena push back harder as the raid goes on and as you go on " +
            "a run. Off: the fixed difficulties this mod used before.");

        RespawnMinDistance = config.Bind(
            "General",
            "RespawnMinDistance",
            40,
            new ConfigDescription(
                "How far a respawn point has to be from where you died. Points " +
                "closer than this are only used when nothing else is left.",
                new AcceptableValueRange<int>(0, 150)));

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
            "Announcer stingers (multi-kills, streaks, Tactical round cues) — plays .ogg files from " +
            "BepInEx/plugins/ArenaMode/sounds/. Missing sounds are skipped and logged.");

        AnnouncerVolume = config.Bind(
            "Audio",
            "AnnouncerVolume",
            0.8f,
            new ConfigDescription("Announcer volume.", new AcceptableValueRange<float>(0f, 1f)));

        ArenaLightingEnabled = config.Bind(
            "Effects",
            "ArenaLighting",
            true,
            "Labs only: add restrained runtime lighting for round starts, the final seconds, " +
            "and Tactical victory/defeat. No map bundles or shared map materials are changed.");

        ArenaLightingStrength = config.Bind(
            "Effects",
            "ArenaLightingStrength",
            1f,
            new ConfigDescription(
                "Brightness multiplier for Arena's additive Labs phase light (0 = invisible).",
                new AcceptableValueRange<float>(0f, 2f)));

        ArenaLightingPressureSeconds = config.Bind(
            "Effects",
            "ArenaLightingPressureSeconds",
            30,
            new ConfigDescription(
                "Turn the Labs lighting red for this many final seconds in timed Rounds and Tactical combat (0 = disabled).",
                new AcceptableValueRange<int>(0, 120)));

        ArenaLightingOutcomeSeconds = config.Bind(
            "Effects",
            "ArenaLightingOutcomeSeconds",
            3f,
            new ConfigDescription(
                "How long Tactical victory/defeat colors fade over the map.",
                new AcceptableValueRange<float>(0.25f, 8f)));

        ArenaLightingRespectExternalMods = config.Bind(
            "Effects",
            "RespectExternalLightingMods",
            true,
            "Leave Arena phase lighting off while Blackout or Going Dark is installed. " +
            "Disable this only if you deliberately want both additive effects together.");

        TeamTracers = config.Bind(
            "Effects",
            "TeamTracers",
            true,
            "Give every Arena projectile a team-colored tracer, including ammunition " +
            "that has no tracer in ordinary raids.");

        FriendlyTracerColor = config.Bind(
            "Effects",
            "FriendlyTracerColor",
            ArenaTracerColor.Cyan,
            "Tracer color for the local PMC and protected 5v5 teammates.");

        OpponentTracerColor = config.Bind(
            "Effects",
            "OpponentTracerColor",
            ArenaTracerColor.Red,
            "Tracer color for Arena opponents.");

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

        // The native Arena flow supports exactly these three locations. An old
        // or remotely edited allowlist must not expose unsupported normal maps.
        if (!string.Equals(locationId, "factory4_day", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationId, "labyrinth", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationId, "laboratory", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var entry in Maps.Value.Split(','))
        {
            if (string.Equals(entry.Trim(), locationId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
