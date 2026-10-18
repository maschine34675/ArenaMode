using System;
using System.Collections.Generic;

namespace ArenaMode.Config;

/// <summary>
/// How ArenaMode's settings appear in the F12 ConfigurationManager window:
/// section order, human-readable names and their order inside a section.
/// Display only. The .cfg section and key names are the stored contract and
/// are never changed here; BepInEx writes the .cfg sections alphabetically
/// regardless of this order.
///
/// Rules of SPT's ConfigurationManager 18.4.0 (decompiled, BepInEx\plugins\spt\
/// ConfigurationManager): a plugin's sections are shown in the order of their
/// first entry in the plugin's ConfigFile, entries inside a section by Order
/// descending, then by display name. <see cref="ArenaConfigMenu.Apply"/> runs once
/// every entry is bound: it applies the section order below and gives each entry
/// its display name and an Order derived from its position in <see cref="Sections"/>
/// (first listed = top).
///
/// No Unity or BepInEx types, so ArenaMode.Tests links this file.
/// </summary>
internal static class ArenaConfigMenuLayout
{
    internal readonly struct Item
    {
        internal Item(string key, string displayName, bool hidden = false, bool percent = false)
        {
            Key = key;
            DisplayName = displayName;
            Hidden = hidden;
            Percent = percent;
        }

        /// <summary>The .cfg key; "*" matches every key of the section.</summary>
        internal string Key { get; }

        internal string DisplayName { get; }

        /// <summary>Internal migration markers and map-screen state: not shown in F12.</summary>
        internal bool Hidden { get; }

        /// <summary>
        /// The value is a share or a percentage. ConfigurationManager draws an int
        /// range 0..100 or 1..100 and a float range 0..1 as a percentage on its own
        /// (and shows the slider position, not the value, for 1..100); entries
        /// without this flag get that guess switched off.
        /// </summary>
        internal bool Percent { get; }
    }

    internal readonly struct Section
    {
        internal Section(string name, bool advanced, params Item[] items)
        {
            Name = name;
            Advanced = advanced;
            Items = items;
        }

        internal string Name { get; }

        /// <summary>
        /// IsAdvanced: hidden by default, shown with the manager's "Advanced settings"
        /// filter or when a search term matches (the manager skips the filter while
        /// searching). Hidden items (Browsable false) stay hidden in search too.
        /// </summary>
        internal bool Advanced { get; }

        internal Item[] Items { get; }
    }

    /// <summary>Order step between neighbouring entries of a section.</summary>
    internal const int OrderStep = 10;

    private static Item I(string key, string name) => new Item(key, name);
    private static Item Pct(string key, string name) => new Item(key, name, percent: true);
    private static Item Hide(string key) => new Item(key, key, hidden: true);

    /// <summary>Sections in display order, entries in display order (top first).</summary>
    internal static readonly Section[] Sections =
    {
        new Section("General", false,
            I("Maps", "Arena Maps"),
            I("FastDeploy", "Fast Deploy Countdown"),
            I("SkipPreRaidScreens", "Skip Pre-Raid Screens"),
            I("RespawnMinDistance", "Respawn Distance From Death (m)"),
            I("PanelHotkey", "Arena Panel Hotkey"),
            I("GiveUpHotkey", "Give Up Life Hotkey"),
            Hide("OptionalMapsMigrated"),
            Hide("OptionalMapsRevision")),
        new Section("Arena", false,
            I("DisableLabyrinthSpawnTraps", "Disable Labyrinth Spawn Traps")),
        new Section("Match", false,
            I("GameMode", "Game Mode"),
            I("RoundIntermissionSeconds", "Deathmatch Round Break (s)"),
            Hide("RoundKillTarget"),
            Hide("RoundTimeLimitSeconds")),
        new Section("Deathmatch", false,
            I("FreeForAll", "Free For All"),
            I("Rounds", "Rounds Per Match (0 = Endless)"),
            I("KillLimit", "Kills Per Round (0 = Off)"),
            I("TimeLimitSeconds", "Round Time Limit (s, 0 = Off)"),
            I("ReplaySeconds", "Killcam Replay (s)"),
            I("Announcer", "Announcer"),
            Hide("LegacyRulesMigrated")),
        new Section("Rounds", false,
            I("RoundRespawn", "Fresh Kit At Round End")),
        new Section("Gun Game", false,
            I("GunGameLadder", "Weapon Ladder (Rung IDs)"),
            I("GunGameRedrawLadder", "New Ladder After Finishing"),
            I("GunGameSpareMagazines", "Spare Magazines Per Gun"),
            I("GunGameLoseTierOnDeath", "Lose Tier On Death"),
            I("ReplaySeconds", "Killcam Replay (s)"),
            I("Announcer", "Announcer")),
        new Section("Team Deathmatch", false,
            I("ExperimentalArea", "Arena Area (All Modes)"),
            I("PlayersPerTeam", "Players Per Team"),
            I("AreaPlayersPerTeam", "Players Per Team (Customs Areas)"),
            I("KillLimit", "Team Kill Limit (0 = Unlimited)"),
            I("TimeLimitSeconds", "Match Duration (s, 0 = Unlimited)"),
            I("FriendlyFire", "Friendly Fire"),
            I("ReplaySeconds", "Killcam Replay (s)"),
            I("Announcer", "Announcer")),
        new Section("Tactical", false,
            I("SquadSize", "Enemy Squad Size"),
            I("SquadIsPmc", "Enemy Squad Are PMCs"),
            I("TeamOwnGear", "Own Gear With PitFireTeam"),
            I("BuySeconds", "Buy Phase (s)"),
            I("RoundSeconds", "Round Time Limit (s)"),
            I("StartMoney", "Starting Money (Roubles)"),
            I("KillReward", "Kill Reward (Roubles)"),
            I("WinBonus", "Round Win Bonus (Roubles)"),
            I("LossBonus", "Round Loss Bonus (Roubles)"),
            I("FriendlyFire", "Friendly Fire (PitFireTeam)"),
            I("ReplaySeconds", "Killcam Replay (s)"),
            I("Announcer", "Announcer")),
        new Section("Tactical Team", false,
            I("RoundsToWin", "Rounds To Win"),
            I("SpectatorAfterDeath", "Spectate After Death")),
        new Section("Zombie", false,
            I("OwnGear", "Bring Own Gear"),
            I("MaxAlive", "Max Infected Alive"),
            I("WaveStartSize", "First Wave Size"),
            I("WaveGrowth", "Extra Infected Per Wave"),
            I("IntermissionSeconds", "Break Between Waves (s)"),
            I("SpawnIntervalSeconds", "Spawn Interval (s)"),
            Pct("ShooterPercent", "Pistol-Armed Infected (%)"),
            I("CrowdEnabled", "Zombie Crowd (Harder Horde)"),
            I("FriendlyFire", "Friendly Fire (PitFireTeam)"),
            I("ReplaySeconds", "Killcam Replay (s)"),
            I("Announcer", "Announcer"),
            I("StuckWaveTimeoutSeconds", "Stuck Wave Timeout (s)"),
            I("VoiceBudgetEnabled", "Limit Overlapping Voices"),
            I("PftFastPathEnabled", "PitFireTeam Fast Path"),
            I("CullOffscreenAnimators", "Cull Unseen Animators (Test)"),
            I("HideOffscreenShadows", "Hide Unseen Shadows (Test)")),
        new Section("Kits", false,
            I("RandomKit", "Random Kit Each Life"),
            I("SelectedKit", "Fixed Kit ID"),
            I("AutoQuickSlots", "Auto-Bind Medical Quick Slots"),
            I("UseItemsAnywhere", "Use Items Anywhere"),
            I("EmptySecureContainer", "Leave Secure Container Contents Home")),
        new Section("Respawn", false,
            I("SpawnProtectionSeconds", "Spawn Protection (s)")),
        new Section("Difficulty", false,
            I("ScaleDifficulty", "Scale Difficulty Over Time"),
            I("MaxBotDifficulty", "Max Bot Difficulty"),
            I("PressureRampMinutes", "Time To Full Pressure (min)"),
            Pct("PressureStreakPercent", "Kill Streak Pressure (%)"),
            I("PressurePmcBonus", "Extra PMCs At Full Pressure")),
        new Section("Bots", false,
            I("PmcFloor", "Minimum PMCs Alive"),
            I("HuntPlayer", "Bots Hunt The Player"),
            I("ExcludePassiveSainPersonalities", "Replace Passive SAIN Personalities"),
            I("NoBotGestures", "No Bot Hand-Signal Pauses")),
        new Section("Bot movement", false,
            I("BotMovementEnabled", "Faster Bot Movement"),
            I("BotWalkSpeedMult", "Bot Walk Speed (x)"),
            I("BotSprintSpeedMult", "Bot Sprint Speed (x)")),
        new Section("Movement", false,
            I("Enabled", "Arena Movement"),
            I("WalkSpeedMult", "Walk Speed (x)"),
            I("SprintSpeedMult", "Sprint Speed (x)"),
            I("ExtraMotionScale", "Extra Motion Scale (x)"),
            I("InfiniteStamina", "Infinite Stamina"),
            I("NoInertia", "No Inertia"),
            Pct("StrafeSmoothing", "Strafe Smoothing"),
            I("InstantLean", "Instant Lean"),
            I("SprintToAds", "Sprint Into Aiming"),
            Pct("AdsMoveFactor", "Aiming Slowdown Removed"),
            I("AdsTransitionMult", "Aiming Transition Speed (x)"),
            I("JumpHeightMult", "Jump Height (x)"),
            I("DoubleJumpEnabled", "Double Jump"),
            I("DoubleJumpHeightMult", "Double Jump Height (x)"),
            I("BunnyhopEnabled", "Bunnyhop"),
            I("AirControlEnabled", "Air Control"),
            I("AirControlDegreesPerSecond", "Air Control Turn Rate (deg/s)"),
            I("AirVaultEnabled", "Vault While Airborne"),
            I("AirHipFireEnabled", "Hip Fire While Airborne"),
            I("SlideEnabled", "Combat Slide"),
            I("VaultSpeedMult", "Vault Speed (x)"),
            I("VaultReachMult", "Vault Reach (x)"),
            I("NoFallDamage", "No Fall Damage"),
            I("LandingRecoveryMultiplier", "Hard Landing Slowdown (x)"),
            I("NoBushSlowdown", "No Bush Slowdown"),
            I("CarryWeightLimitMultiplier", "Carry Weight Limits (x)"),
            I("WeaponDrawSpeedMultiplier", "Weapon Draw Speed (x)"),
            I("WeaponHolsterSpeedMultiplier", "Weapon Holster Speed (x)"),
            I("WeaponReloadSpeedMultiplier", "Reload Speed (x)")),
        new Section("Character", false,
            I("MaxSkills", "Elite Skills"),
            I("MedicalUseSpeed", "Medical Use Speed (x)"),
            I("MagazineHandlingSpeed", "Magazine Loading Speed (x)"),
            I("BleedingDamageMultiplier", "Bleeding Damage (x)"),
            Pct("LegInjurySlowdownReductionPercent", "Leg Injury Slowdown Reduction (%)")),
        new Section("Combat", false,
            I("AkimboEnabled", "Akimbo Pistols")),
        new Section("Loot", false,
            I("InstantSearch", "Instant Search"),
            I("AutoSearchCorpses", "Auto-Search Corpses"),
            I("ShowVictimNames", "Show Victim Names In Kill List")),
        new Section("Arena pickups", false,
            I("Enabled", "Arena Pickups"),
            I("BotsCollectPowerups", "Bots Collect Power-Ups"),
            Pct("SoundVolume", "Pickup Sound Volume"),
            I("PrototypeWeaponKit", "Pickup Weapon Kit ID"),
            Hide("M60DefaultMigrated")),
        new Section("Arena incendiary prototype", false,
            I("Enabled", "Bonus Molotov (WIP)")),
        new Section("HUD", false,
            I("Enabled", "Arena HUD"),
            I("CombatElements", "Combat Elements"),
            I("Killfeed", "Killfeed"),
            I("KillfeedOtherKills", "Killfeed: Bot vs Bot Kills"),
            I("Hitmarker", "Hitmarker"),
            I("DamageNumbers", "Damage Numbers"),
            I("CombineDamageNumbers", "Combine Damage Numbers"),
            I("ShowFriendlyMarkers", "Friendly Markers"),
            I("ScorePanelOffsetX", "Score Panel Offset X (px)"),
            I("ScorePanelOffsetY", "Score Panel Offset Y (px)"),
            I("KillfeedOffsetX", "Killfeed Offset X (px)"),
            I("KillfeedOffsetY", "Killfeed Offset Y (px)")),
        new Section("HUD Panels - General", false,
            I("Enabled", "Info Panels"),
            I("UpdateIntervalSeconds", "Update Interval (s)"),
            I("WarningColor", "Warning Color")),
        new Section("HUD Panels - Weapon", false,
            I("Enabled", "Weapon Panel"),
            I("UseShortName", "Short Weapon Name"),
            I("ShowSpareMagCount", "Spare Magazine Count"),
            I("PanelOffset", "Panel Offset (px)"),
            I("TextColor", "Text Color")),
        new Section("HUD Panels - Status", false,
            I("Enabled", "Status Panel"),
            I("ShowWeight", "Show Carried Weight"),
            I("HealthWarningPercent", "Health Warning Below (%)"),
            I("ArmorWarningPercent", "Armor Warning Below (%)"),
            I("PanelOffset", "Panel Offset (px)"),
            I("HealthColor", "Health Color"),
            I("WeightColor", "Weight Color")),
        new Section("HUD Panels - Appearance", false,
            I("Scale", "Scale (x)"),
            I("PanelOpacity", "Panel Opacity")),
        new Section("Audio", false,
            I("HitmarkerSounds", "Hitmarker Sounds"),
            Pct("HitmarkerVolume", "Hitmarker Volume"),
            I("Announcer", "Announcer (All Modes)"),
            Pct("AnnouncerVolume", "Announcer Volume"),
            I("AnnouncerUseUiVolume", "Announcer Follows Interface Volume"),
            I("MuteLabyrinthAlarm", "Mute Labyrinth Alarm")),
        new Section("Effects", false,
            I("TeamTracers", "Team Tracers"),
            I("TeamLasers", "Team Lasers"),
            I("FriendlyTracerColor", "Friendly Color"),
            I("OpponentTracerColor", "Opponent Color"),
            I("VisibleLaserBrightness", "Laser Dot Brightness (x)"),
            I("VisibleLaserBeamBrightness", "Laser Beam Brightness (x)"),
            I("ArenaLighting", "Arena Lighting"),
            I("ArenaLightingStrength", "Arena Lighting Strength (x)"),
            I("ArenaLightingPressureSeconds", "Final-Seconds Red Light (s)"),
            I("ArenaLightingOutcomeSeconds", "Round Outcome Light (s)"),
            I("RespectExternalLightingMods", "Respect Blackout / Going Dark"),
            I("RagdollImpulseScale", "Ragdoll Impulse (x)")),
        new Section("Performance", false,
            I("MaxCorpses", "Visible Corpses Kept"),
            I("StripCorpseGear", "Strip Hidden Corpses"),
            I("ReleaseTrimmedCorpses", "Release Hidden Corpses"),
            I("CorpseLightsOff", "Corpse Lights Off"),
            I("NoCorpseLooting", "No Bot Corpse Looting"),
            I("KitPreloadInLoadingScreen", "Kit Preload In Loading Screen"),
            I("BotLoadBoost", "Bot Load Boost"),
            I("BotLoadBoostBudget", "Bot Load Boost Budget (x)"),
            I("BotLoadBoostDelaySeconds", "Bot Load Boost Delay (s)"),
            I("BotLoadBoostAsyncPriority", "Async Load Priority (Experimental)"),
            I("ProfileBuildBudgetMs", "Bot Profile Build Budget (ms per frame)"),
            I("GcHeapThresholdGB", "GC Heap Threshold (GB)"),
            I("PoolPrewarmKeepIdle", "Idle Pool Instances Kept"),
            I("TrimDeadBrainList", "Trim Dead Bot Brains"),
            I("SkipBotFovCoroutine", "Skip Bot FOV Coroutine")),
        new Section("Diagnostics", true,
            I("DeveloperMode", "Developer Mode"),
            I("LogDamageExceptions", "Log Damage Exceptions")),
        new Section("Development - Area outline", true,
            I("Enabled", "Area Outline Capture"),
            I("AllowInNormalPmc", "Allow In Normal PMC Raids"),
            I("CaptureAim", "Capture Aimed Point"),
            I("CaptureFeet", "Capture Feet Position"),
            I("Undo", "Undo Last Point"),
            I("Export", "Export Draft"),
            I("FloorY", "Floor Y"),
            I("CeilingY", "Ceiling Y"),
            I("SpawnMinY", "Spawn Min Y"),
            I("SpawnMaxY", "Spawn Max Y")),
        new Section("Development - Pickups", true,
            I("Enabled", "Pickup Capture"),
            I("Kind", "Pickup Kind"),
            I("RespawnSeconds", "Pickup Respawn (s)"),
            I("CaptureAim", "Capture Aimed Point"),
            I("CaptureFeet", "Capture Feet Position"),
            I("Undo", "Undo Last Point"),
            I("Export", "Export Draft")),
        // Per-map participant totals chosen on the Arena map screen; never edited in F12.
        new Section("Map participants", false,
            Hide("*")),
    };

    internal readonly struct Resolved
    {
        internal Resolved(string displayName, int order, bool hidden, bool advanced, bool percent, int sectionRank)
        {
            DisplayName = displayName;
            Order = order;
            Hidden = hidden;
            Advanced = advanced;
            Percent = percent;
            SectionRank = sectionRank;
        }

        internal string DisplayName { get; }
        internal int Order { get; }
        internal bool Hidden { get; }
        internal bool Advanced { get; }
        internal bool Percent { get; }
        internal int SectionRank { get; }
    }

    private static readonly Dictionary<string, int> SectionRanks = BuildRanks();

    private static Dictionary<string, int> BuildRanks()
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Sections.Length; i++)
            ranks[Sections[i].Name] = i;
        return ranks;
    }

    /// <summary>Position of a section in the menu; unknown sections go after every listed one.</summary>
    internal static int SectionRank(string section)
        => section != null && SectionRanks.TryGetValue(section, out int rank) ? rank : Sections.Length;

    /// <summary>Display data for one .cfg entry; false when the table does not list it.</summary>
    internal static bool TryResolve(string section, string key, out Resolved resolved)
    {
        resolved = default;
        if (section == null || key == null || !SectionRanks.TryGetValue(section, out int rank))
            return false;
        var block = Sections[rank];
        for (int i = 0; i < block.Items.Length; i++)
        {
            var item = block.Items[i];
            if (item.Key != "*" && !string.Equals(item.Key, key, StringComparison.Ordinal))
                continue;
            int order = (block.Items.Length - i) * OrderStep;
            string name = item.Key == "*" ? key : item.DisplayName;
            resolved = new Resolved(name, order, item.Hidden, block.Advanced, item.Percent, rank);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether ConfigurationManager would draw this range as a percentage on its
    /// own (ConfigSettingEntry.GetAcceptableValues: int min 0 or 1 with max 100,
    /// or float 0 to 1).
    /// </summary>
    internal static bool LooksLikePercentRange(object min, object max)
        => ((min is int a && (a == 0 || a == 1)) && max is int b && b == 100)
           || (min is float f && f == 0f && max is float g && g == 1f);
}
