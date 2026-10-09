using ArenaMode.Effects;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Shared;
using BepInEx.Configuration;
using System;
using UnityEngine;

namespace ArenaMode.Config;

/// <summary>
/// Unity's per-frame time slice for integrating asynchronous loads, as the
/// load boost sets it while held. Game = leave the streamer's Low alone.
/// </summary>
public enum AsyncLoadPriority
{
    Game,
    Low,
    BelowNormal,
    Normal,
    High,
}

public static class Settings
{
    public static ConfigEntry<string> Maps { get; private set; }
    public static ConfigEntry<string> SelectedKit { get; private set; }
    public static ConfigEntry<bool> RandomKit { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }
    public static ConfigEntry<bool> DisableLabyrinthSpawnTraps { get; private set; }
    public static ConfigEntry<bool> MovementEnabled { get; private set; }
    public static ConfigEntry<bool> BotMovementEnabled { get; private set; }
    public static ConfigEntry<float> BotWalkSpeedMult { get; private set; }
    public static ConfigEntry<float> BotSprintSpeedMult { get; private set; }
    public static ConfigEntry<float> LandingRecoveryMultiplier { get; private set; }
    public static ConfigEntry<float> WeaponDrawSpeedMultiplier { get; private set; }
    public static ConfigEntry<float> WeaponHolsterSpeedMultiplier { get; private set; }
    public static ConfigEntry<float> WeaponReloadSpeedMultiplier { get; private set; }
    public static ConfigEntry<bool> AirVaultEnabled { get; private set; }
    public static ConfigEntry<bool> AirControlEnabled { get; private set; }
    public static ConfigEntry<float> AirControlDegreesPerSecond { get; private set; }
    public static ConfigEntry<bool> AirHipFireEnabled { get; private set; }
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
    public static ConfigEntry<bool> DoubleJumpEnabled { get; private set; }
    public static ConfigEntry<bool> BunnyhopEnabled { get; private set; }
    public static ConfigEntry<float> DoubleJumpHeightMult { get; private set; }
    public static ConfigEntry<bool> NoFallDamage { get; private set; }
    public static ConfigEntry<float> VaultSpeedMult { get; private set; }
    public static ConfigEntry<float> VaultReachMult { get; private set; }
    public static ConfigEntry<bool> SprintToAds { get; private set; }
    public static ConfigEntry<bool> FastDeploy { get; private set; }
    public static ConfigEntry<bool> SkipPreRaidScreens { get; private set; }
    public static ConfigEntry<bool> InstantSearch { get; private set; }
    public static ConfigEntry<bool> AutoQuickSlots { get; private set; }
    public static ConfigEntry<bool> UseItemsAnywhere { get; private set; }
    public static ConfigEntry<bool> AkimboEnabled { get; private set; }
    public static ConfigEntry<bool> EmptySecureContainer { get; private set; }
    public static ConfigEntry<bool> BotLoadBoost { get; private set; }
    public static ConfigEntry<float> BotLoadBoostBudget { get; private set; }
    public static ConfigEntry<float> BotLoadBoostDelaySeconds { get; private set; }
    public static ConfigEntry<AsyncLoadPriority> BotLoadBoostAsyncPriority { get; private set; }
    public static ConfigEntry<KitPreloadScope> KitPreloadInLoadingScreen { get; private set; }
    public static ConfigEntry<bool> AutoSearchCorpses { get; private set; }
    public static ConfigEntry<bool> ShowVictimNames { get; private set; }
    public static ConfigEntry<int> MaxCorpses { get; private set; }
    public static ConfigEntry<bool> StripCorpseGear { get; private set; }
    public static ConfigEntry<bool> ReleaseTrimmedCorpses { get; private set; }
    public static ConfigEntry<float> GcHeapThresholdGB { get; private set; }
    public static ConfigEntry<int> PoolPrewarmKeepIdle { get; private set; }

    public static ConfigEntry<bool> LogDamageExceptions { get; private set; }
    public static ConfigEntry<bool> NoCorpseLooting { get; private set; }
    public static ConfigEntry<bool> NoBotGestures { get; private set; }
    public static ConfigEntry<bool> CorpseLightsOff { get; private set; }
    public static ConfigEntry<string> GameMode { get; private set; }
    public static ConfigEntry<bool> HudEnabled { get; private set; }
    public static ConfigEntry<float> ScorePanelOffsetX { get; private set; }
    public static ConfigEntry<float> ScorePanelOffsetY { get; private set; }
    public static ConfigEntry<bool> ShowFriendlyMarkers { get; private set; }
    public static ConfigEntry<bool> ShowKillfeed { get; private set; }
    public static ConfigEntry<float> KillfeedOffsetX { get; private set; }
    public static ConfigEntry<float> KillfeedOffsetY { get; private set; }
    public static ConfigEntry<bool> KillfeedOtherKills { get; private set; }
    public static ConfigEntry<bool> ShowHitmarker { get; private set; }
    public static ConfigEntry<bool> ShowDamageNumbers { get; private set; }
    public static ConfigEntry<bool> CombineDamageNumbers { get; private set; }
    public static ConfigEntry<float> DeathmatchReplaySeconds { get; private set; }
    public static ConfigEntry<float> GunGameReplaySeconds { get; private set; }
    public static ConfigEntry<float> TeamDeathmatchReplaySeconds { get; private set; }
    public static ConfigEntry<bool> AnnouncerEnabled { get; private set; }
    public static ConfigEntry<float> AnnouncerVolume { get; private set; }
    public static ConfigEntry<bool> AnnouncerUseUiVolume { get; private set; }
    public static ConfigEntry<bool> DeathmatchAnnouncerEnabled { get; private set; }
    public static ConfigEntry<bool> GunGameAnnouncerEnabled { get; private set; }
    public static ConfigEntry<bool> TacticalAnnouncerEnabled { get; private set; }
    public static ConfigEntry<bool> TeamDeathmatchAnnouncerEnabled { get; private set; }
    public static ConfigEntry<bool> ZombieAnnouncerEnabled { get; private set; }
    public static ConfigEntry<bool> HitmarkerSounds { get; private set; }
    public static ConfigEntry<float> HitmarkerVolume { get; private set; }
    public static ConfigEntry<bool> MuteLabyrinthAlarm { get; private set; }
    public static ConfigEntry<float> RagdollImpulseScale { get; private set; }
    public static ConfigEntry<bool> ArenaLightingEnabled { get; private set; }
    public static ConfigEntry<float> ArenaLightingStrength { get; private set; }
    public static ConfigEntry<int> ArenaLightingPressureSeconds { get; private set; }
    public static ConfigEntry<float> ArenaLightingOutcomeSeconds { get; private set; }
    public static ConfigEntry<bool> ArenaLightingRespectExternalMods { get; private set; }
    public static ConfigEntry<bool> TeamTracers { get; private set; }
    public static ConfigEntry<bool> TeamDeathmatchFriendlyFire { get; private set; }
    public static ConfigEntry<bool> TacticalFriendlyFire { get; private set; }
    public static ConfigEntry<bool> ZombieFriendlyFire { get; private set; }

    /// <summary>Menu preference only; damage reads the snapshot taken at arming.</summary>
    internal static ConfigEntry<bool> FriendlyFireEntry(string mode, bool protectedTeam)
        => ArenaModeCatalog.NormalizeId(mode) switch
        {
            ArenaModeCatalog.TeamDeathmatchId => TeamDeathmatchFriendlyFire,
            ArenaModeCatalog.TacticalId when protectedTeam => TacticalFriendlyFire,
            ArenaModeCatalog.ZombieId when protectedTeam => ZombieFriendlyFire,
            _ => null
        };

    internal static bool GetFriendlyFireEnabled(string mode, bool protectedTeam)
        => FriendlyFireEntry(mode, protectedTeam)?.Value ?? true;

    /// <summary>
    /// Colored tracers are a team feature, exactly like <see cref="TeamLasers"/>
    /// (2026-10-08): the one <see cref="TeamTracers"/> switch applies to Team
    /// Deathmatch and protected Tactical/Zombie squads (PitFireTeam); solo modes
    /// (Deathmatch, Gun Game, Zombie, solo Tactical) have NO entry, return null,
    /// the renderer keeps the native tracer and the mutator row shows N/A. There
    /// are no per-mode solo tracer settings any more. Solo shooters are still
    /// classified friendly/opponent by <c>ArenaTracerTeams.Classify</c>, which
    /// the killfeed names keep using in every mode (the friendly markers have
    /// their own classification and are not affected); only the TRACER rendering
    /// is team-only.
    /// </summary>
    internal static ConfigEntry<bool> TracerEntry(string mode, bool protectedTeam)
        => ArenaModeCatalog.NormalizeId(mode) switch
        {
            ArenaModeCatalog.TeamDeathmatchId => TeamTracers,
            ArenaModeCatalog.TacticalId when protectedTeam => TeamTracers,
            ArenaModeCatalog.ZombieId when protectedTeam => TeamTracers,
            _ => null
        };
    public static ConfigEntry<bool> TeamLasers { get; private set; }
    public static ConfigEntry<float> VisibleLaserBrightness { get; private set; }
    public static ConfigEntry<float> VisibleLaserBeamBrightness { get; private set; }
    public static ConfigEntry<ArenaTracerColor> FriendlyTracerColor { get; private set; }
    public static ConfigEntry<ArenaTracerColor> OpponentTracerColor { get; private set; }
    public static ConfigEntry<int> PmcFloor { get; private set; }
    public static ConfigEntry<bool> HuntPlayer { get; private set; }
    public static ConfigEntry<bool> ExcludePassiveSainPersonalities { get; private set; }
    public static ConfigEntry<bool> MaxSkills { get; private set; }
    public static ConfigEntry<int> LegInjurySlowdownReductionPercent { get; private set; }
    public static ConfigEntry<float> MedicalUseSpeed { get; private set; }
    public static ConfigEntry<float> BleedingDamageMultiplier { get; private set; }
    public static ConfigEntry<float> MagazineHandlingSpeed { get; private set; }
    public static ConfigEntry<int> DeathmatchRounds { get; private set; }
    public static ConfigEntry<int> DeathmatchKills { get; private set; }
    public static ConfigEntry<int> DeathmatchSeconds { get; private set; }
    public static ConfigEntry<bool> DeathmatchFreeForAll { get; private set; }
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
    public static ConfigEntry<int> TeamDeathmatchSize { get; private set; }
    public static ConfigEntry<int> TeamDeathmatchKillLimit { get; private set; }
    public static ConfigEntry<int> TeamDeathmatchTimeLimitSeconds { get; private set; }
    public static ConfigEntry<int> ZombieMaxAlive { get; private set; }
    public static ConfigEntry<int> ZombieWaveStartSize { get; private set; }
    public static ConfigEntry<int> ZombieWaveGrowth { get; private set; }
    public static ConfigEntry<int> ZombieIntermissionSeconds { get; private set; }
    public static ConfigEntry<float> ZombieSpawnIntervalSeconds { get; private set; }
    public static ConfigEntry<int> ZombieShooterPercent { get; private set; }
    public static ConfigEntry<float> ZombieStuckWaveTimeoutSeconds { get; private set; }
    public static ConfigEntry<bool> ZombieOwnGear { get; private set; }
    public static ConfigEntry<bool> ZombiePftFastPathEnabled { get; private set; }
    public static ConfigEntry<bool> ZombieVoiceBudgetEnabled { get; private set; }
    public static ConfigEntry<float> ZombieReplaySeconds { get; private set; }
    public static ConfigEntry<int> AreaTeamSize { get; private set; }
    public static ConfigEntry<string> TeamDeathmatchArea { get; private set; }
    public static ConfigEntry<bool> AreaCaptureEnabled { get; private set; }
    public static ConfigEntry<bool> AreaCaptureAllowInNormalPmc { get; private set; }
    public static ConfigEntry<KeyboardShortcut> AreaCaptureAim { get; private set; }
    public static ConfigEntry<KeyboardShortcut> AreaCaptureFeet { get; private set; }
    public static ConfigEntry<KeyboardShortcut> AreaCaptureUndo { get; private set; }
    public static ConfigEntry<KeyboardShortcut> AreaCaptureExport { get; private set; }
    public static ConfigEntry<float> AreaCaptureMinY { get; private set; }
    public static ConfigEntry<float> AreaCaptureMaxY { get; private set; }
    public static ConfigEntry<float> AreaCaptureSpawnMinY { get; private set; }
    public static ConfigEntry<float> AreaCaptureSpawnMaxY { get; private set; }
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

    internal static DeathmatchOptions FreezeDeathmatchRules()
        => new DeathmatchOptions(DeathmatchRounds.Value, DeathmatchKills.Value, DeathmatchSeconds.Value,
            DeathmatchFreeForAll.Value, RoundIntermissionSeconds.Value, RoundRespawn.Value);

    private static ConfigEntry<float> ReplayEntry(string mode)
        => ArenaModeCatalog.NormalizeId(mode) switch
        {
            ArenaModeCatalog.GunGameId => GunGameReplaySeconds,
            ArenaModeCatalog.TeamDeathmatchId => TeamDeathmatchReplaySeconds,
            ArenaModeCatalog.TacticalId => TacticalReplaySeconds,
            ArenaModeCatalog.ZombieId => ZombieReplaySeconds,
            _ => DeathmatchReplaySeconds,
        };

    internal static float GetReplaySeconds(string mode)
    {
        float value = ReplayEntry(mode)?.Value ?? 0f;
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(10f, value));
    }

    internal static bool TrySelectKillcam(string mode, bool enabled)
    {
        if (Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid) return false;
        var entry = ReplayEntry(mode);
        if (entry == null) return false;
        float seconds = enabled ? 3f : 0f;
        try { entry.Value = seconds; }
        catch (Exception ex)
        { Plugin.ModLogger?.LogWarning($"Arena killcam selection failed: {ex.Message}"); return false; }
        if (entry.Value != seconds) return false;
        try { if (File?.SaveOnConfigSet == false) File.Save(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena killcam save failed: {ex.Message}"); }
        return true; // Presentation/respawn timing is client-owned; no server settings traffic.
    }

    /// <summary>
    /// Backing per-mode entry for item 2 of the 2026-10-06 follow-up
    /// ("announcer mutator"), dispatched the same way <see cref="ReplayEntry"/>
    /// dispatches Killcam. Unlike Killcam (a float "seconds" entry) and the
    /// equipment choice (offered for only one mode today), every mode maps to
    /// a real bool entry here — there is no "unavailable" case — so
    /// <see cref="Session.ArenaMutatorSelection"/> can read/write this
    /// directly through its generic Entry()/Enabled()/TryToggle() path, the
    /// same way it already does for <see cref="TeamLasers"/>.
    /// </summary>
    internal static ConfigEntry<bool> AnnouncerEntry(string mode)
        => ArenaModeCatalog.NormalizeId(mode) switch
        {
            ArenaModeCatalog.GunGameId => GunGameAnnouncerEnabled,
            ArenaModeCatalog.TacticalId => TacticalAnnouncerEnabled,
            ArenaModeCatalog.TeamDeathmatchId => TeamDeathmatchAnnouncerEnabled,
            ArenaModeCatalog.ZombieId => ZombieAnnouncerEnabled,
            _ => DeathmatchAnnouncerEnabled,
        };

    /// <summary>
    /// The live, unfrozen master-AND-per-mode decision, combined through the
    /// single pure function in <see cref="Modes.ArenaAnnouncerGate.IsEnabled"/>.
    /// Menu-time display only now (the same role <see cref="GetReplaySeconds"/>
    /// and <c>ZombieOwnGear.Value</c> already play) — <c>AnnouncerService</c>'s
    /// own live-match call sites (attach-time log line, callout submission,
    /// the queued-result drain in Tick, Receive) do NOT call this any more.
    /// They read <see cref="Session.ArenaSession.AnnouncerEnabledSelection"/>,
    /// the per-mode half frozen once at arm (captured from
    /// <see cref="GetAnnouncerPerModeEnabled"/>, exactly like
    /// <c>GunGameLadderSelection</c>/<c>ZombieOwnGearSelection</c>), and
    /// re-combine it with the live master switch themselves — see that
    /// property's own doc comment for why a live call here would have let a
    /// mid-raid F12 edit desync an already-armed/active match (ANN-1, the
    /// 2026-10-06 follow-up's own review). A null/unmapped mode normalizes to
    /// Deathmatch's entry via <see cref="AnnouncerEntry"/>, never throws.
    /// </summary>
    internal static bool IsAnnouncerEnabledFor(string mode)
        => Modes.ArenaAnnouncerGate.IsEnabled(AnnouncerEnabled.Value, AnnouncerEntry(mode)?.Value == true);

    /// <summary>
    /// The per-mode entry's raw value ALONE — no master switch — the half of
    /// <see cref="IsAnnouncerEnabledFor"/> that <see cref="Session.
    /// ArenaSession"/> freezes once at arm into <c>AnnouncerEnabledSelection</c>.
    /// Exists only for that one freeze, the same role <see cref="GetReplaySeconds"/>
    /// plays for <c>ArenaReplaySelection.Capture</c>. The master switch is
    /// deliberately excluded: the 2026-10-06 follow-up's own design keeps it a
    /// live kill switch ("the global switch, if any, remains a master gate"),
    /// so freezing it alongside the per-mode half would silence that design —
    /// <c>AnnouncerService</c> reads <see cref="AnnouncerEnabled"/> live and
    /// ANDs it with the frozen snapshot at each call site instead.
    /// </summary>
    internal static bool GetAnnouncerPerModeEnabled(string mode) => AnnouncerEntry(mode)?.Value == true;

    /// <summary>
    /// Backing entry for item 1 of the 2026-10-06 follow-up ("own gear to the
    /// left")'s left-panel equipment-choice row, dispatched by mode the same
    /// way <see cref="ReplayEntry"/> dispatches Killcam. Null for any mode
    /// <see cref="Modes.ArenaEquipmentChoice.IsOfferedFor"/> does not offer a
    /// choice for (today: every mode except Zombie) — adding Deathmatch/Gun
    /// Game later is one more switch arm plus a new bound entry, not a new
    /// read/write path.
    /// </summary>
    private static ConfigEntry<bool> EquipmentChoiceEntry(string mode)
        => ArenaModeCatalog.NormalizeId(mode) switch
        {
            ArenaModeCatalog.ZombieId => ZombieOwnGear,
            _ => null,
        };

    internal static bool GetEquipmentChoiceOwnGear(string mode) => EquipmentChoiceEntry(mode)?.Value == true;

    /// <summary>
    /// Same ConfigEntry, same <c>ArenaSession.IsActive||ArmedForNextRaid</c>
    /// lock and the same File.Save()-when-SaveOnConfigSet-is-false fallback
    /// <see cref="ArenaMutatorSelection.TryToggle"/> used before this setting's
    /// write path moved out of the mutators panel and into the left mode
    /// panel's own row — only the caller (<see cref="Session.
    /// ArenaEquipmentChoiceView"/> instead of the mutators panel) changed.
    /// </summary>
    internal static bool TrySelectEquipmentChoice(string mode, bool ownGear)
    {
        if (Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid) return false;
        var entry = EquipmentChoiceEntry(mode);
        if (entry == null) return false;
        try { entry.Value = ownGear; }
        catch (Exception ex)
        { Plugin.ModLogger?.LogWarning($"Arena equipment choice selection failed: {ex.Message}"); return false; }
        if (entry.Value != ownGear) return false;
        try { if (File?.SaveOnConfigSet == false) File.Save(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena equipment choice save failed: {ex.Message}"); }
        return true;
    }

    internal static bool TrySelectDeathmatchRule(int row, int value)
    {
        if (Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid) return false;
        var rounds = row == 0 ? value : DeathmatchRounds.Value;
        var kills = row == 1 ? value : DeathmatchKills.Value;
        var seconds = row == 2 ? value : DeathmatchSeconds.Value;
        if (rounds < 0 || rounds > 100 || kills < 0 || kills > 1000 || seconds < 0 || seconds > 7200
            || (rounds > 0 && kills == 0 && seconds == 0) || row < 0 || row > 3
            || (row == 3 && value != 0 && value != 1)) return false;
        if (row == 0) DeathmatchRounds.Value = value;
        else if (row == 1) DeathmatchKills.Value = value;
        else if (row == 2) DeathmatchSeconds.Value = value;
        else DeathmatchFreeForAll.Value = value == 0;
        if (File?.SaveOnConfigSet == false) File.Save();
        SettingsSync.Report();
        return true;
    }

    internal static bool TrySelectArea(string areaId)
    {
        // Use the same constraint as F12 and persisted config loading. A second
        // ID list here can silently reject a newly offered area (Factory East).
        if (TeamDeathmatchArea == null || TeamDeathmatchArea.Description.AcceptableValues?.IsValid(areaId) != true)
            return false;
        if (TeamDeathmatchArea.Value == areaId) return true;
        try { TeamDeathmatchArea.Value = areaId; }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena play-area selection failed: {ex.Message}");
            return false;
        }
        if (TeamDeathmatchArea.Value != areaId) return false;
        try { if (File?.SaveOnConfigSet == false) File.Save(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena play-area save failed: {ex.Message}"); }
        SettingsSync.Report();
        Plugin.ModLogger?.LogInfo($"Arena play area selected in map screen: {areaId}.");
        return true;
    }

    internal static bool TrySelectAreaTeamSize(int size)
    {
        if (AreaTeamSize == null || size < 2 || size > 5) return false;
        if (AreaTeamSize.Value == size) return true;
        try { AreaTeamSize.Value = size; }
        catch (Exception ex)
        { Plugin.ModLogger?.LogWarning($"Arena area team-size selection failed: {ex.Message}"); return false; }
        if (AreaTeamSize.Value != size) return false;
        try { if (File?.SaveOnConfigSet == false) File.Save(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena area team-size save failed: {ex.Message}"); }
        SettingsSync.Report();
        Plugin.ModLogger?.LogInfo($"Arena area team size selected in map screen: {size}v{size}.");
        return true;
    }

    internal static bool TrySelectTeamDeathmatchKillLimit(int value)
        => TrySelectTeamDeathmatchLimit(TeamDeathmatchKillLimit, value, 1000, "kill limit");

    internal static bool TrySelectTeamDeathmatchTimeLimit(int value)
        => TrySelectTeamDeathmatchLimit(TeamDeathmatchTimeLimitSeconds, value, 7200, "time limit");

    private static bool TrySelectTeamDeathmatchLimit(ConfigEntry<int> entry, int value, int maximum, string label)
    {
        // Menu writes are pre-raid only. F12 remains editable, but the running
        // match owns its frozen limits and never reads later config changes.
        if (Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid
            || entry == null || value < 0 || value > maximum) return false;
        if (entry.Value == value) return true;
        try { entry.Value = value; }
        catch (Exception ex)
        { Plugin.ModLogger?.LogWarning($"Arena TDM {label} selection failed: {ex.Message}"); return false; }
        if (entry.Value != value) return false;
        try { if (File?.SaveOnConfigSet == false) File.Save(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena TDM {label} save failed: {ex.Message}"); }
        // Limits are client-owned. No route/protocol change or settings report.
        return true;
    }

    private static ConfigEntry<float> BindReplay(ConfigFile config, string section)
        => config.Bind(section, "ReplaySeconds", 0f, new ConfigDescription(
            "Recorded seconds shown after death; 0 keeps instant respawn. Requires enabled KillcamReplay. " +
            "Respawn waits for actual playback, slow motion and live tail. Frozen when the raid is armed.",
            new AcceptableValueRange<float>(0f, 10f)));

    /// <summary>
    /// Item 2 of the 2026-10-06 follow-up ("announcer mutator"), bound once per
    /// mode the same way <see cref="BindReplay"/> binds Killcam — same key
    /// ("Announcer") under a distinct per-mode section, so it cannot collide
    /// with the pre-existing global "Audio"/"Announcer" entry
    /// (<see cref="AnnouncerEnabled"/>), which this does NOT replace: that
    /// switch stays the master gate (see <see cref="Modes.ArenaAnnouncerGate.IsEnabled"/>).
    /// <paramref name="defaultValue"/> always comes from
    /// <see cref="Modes.ArenaAnnouncerGate.DefaultEnabled"/> so the bound
    /// default can never drift from the table ArenaMode.Tests exercises.
    /// </summary>
    private static ConfigEntry<bool> BindAnnouncer(ConfigFile config, string section, bool defaultValue)
        => config.Bind(section, "Announcer", defaultValue,
            "Per-mode gate for the Arena announcer (stingers, round/match cues and the on-screen " +
            "kill callouts such as DOUBLE KILL). The global Audio/Announcer switch above stays a " +
            "master gate: both it and this must be on for this mode's announcer to speak and show " +
            "its callouts; AnnouncerVolume stays global too. Frozen like the other per-mode " +
            "mutators while the raid is active or armed. HUD text callouts (e.g. WAVE n) are a " +
            "separate system and are unaffected by either switch.");

    public static void Init(ConfigFile config)
    {
        File = config;
        AreaCaptureEnabled = config.Bind("Development - Area outline", "Enabled", false,
            "Opt-in point capture. Normal PMC raids additionally require AllowInNormalPmc. Exports drafts; NEVER changes live boundaries or spawns. " +
            "Use Waypoints for spawn visualization. Draft points survive respawns but clear when disabled or the raid ends; export first.");
        AreaCaptureAllowInNormalPmc = config.Bind("Development - Area outline", "AllowInNormalPmc", false,
            "Allow the enabled capture tool in normal local PMC raids so you can walk an entire new outline. " +
            "Survey only: NO Arena kit, respawn, invulnerability or profile protection. Normal raid consequences still apply. No Scav or Hideout capture.");
        AreaCaptureAim = config.Bind("Development - Area outline", "CaptureAim", new KeyboardShortcut(KeyCode.F6, KeyCode.LeftControl),
            "Capture the map surface under the crosshair, up to 250m away. Ignores Arena's own boundary colliders for this read-only ray.");
        AreaCaptureFeet = config.Bind("Development - Area outline", "CaptureFeet", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
            "Capture the current local player's foot position. Walk the contour in order; the closing edge is automatic.");
        AreaCaptureUndo = config.Bind("Development - Area outline", "Undo", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl),
            "Remove the last draft point.");
        AreaCaptureExport = config.Bind("Development - Area outline", "Export", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
            "Validate and export a new draft JSON to BepInEx/plugins/ArenaMode/AreaDrafts. Not automatically loaded or applied.");
        AreaCaptureMinY = config.Bind("Development - Area outline", "FloorY", 0f,
            "Draft enclosing floor in absolute map Y coordinates. Defaults match the existing Customs candidate.");
        AreaCaptureMaxY = config.Bind("Development - Area outline", "CeilingY", 12f,
            "Draft enclosing ceiling in absolute map Y coordinates; leave headroom for jumping and vaulting.");
        AreaCaptureSpawnMinY = config.Bind("Development - Area outline", "SpawnMinY", .5f,
            "Draft minimum spawn foot height in absolute map Y coordinates. Separate from the enclosing floor.");
        AreaCaptureSpawnMaxY = config.Bind("Development - Area outline", "SpawnMaxY", 3f,
            "Draft maximum spawn foot height in absolute map Y coordinates; must remain inside floor/ceiling.");

        Maps = config.Bind(
            "General",
            "Maps",
            ArenaMapCatalog.DefaultClientAllowlist,
            "Arena map allowlist (Factory day/night, Labyrinth, The Lab, Customs, optional Equator/Bay5). " +
            "Only maps also enabled on the server are offered. The Lab is " +
            "keycard-locked in the menu; arena raids waive that, ordinary ones do not.");

        var optionalMapsMigrated = config.Bind("General", "OptionalMapsMigrated", false,
            "Internal one-time map-default migration marker. Leave enabled after migration.");
        var optionalMapsRevision = config.Bind("General", "OptionalMapsRevision", 0,
            "Internal map-default migration revision. Preserve the recorded value.");
        if (optionalMapsRevision.Value < ArenaMapCatalog.OptionalMapsRevision)
        {
            var upgradedMaps = ArenaMapCatalog.UpgradeOptionalClientAllowlist(
                Maps.Value, optionalMapsRevision.Value, optionalMapsMigrated.Value);
            if (!string.Equals(upgradedMaps, Maps.Value, StringComparison.Ordinal))
            {
                Maps.Value = upgradedMaps;
                Plugin.ModLogger?.LogInfo("Arena default map allowlist upgraded with optional Equator/Bay5.");
            }
            // Record even an edited list; deleting Equator later must not
            // accidentally recreate an exact old default and re-enable it.
            optionalMapsMigrated.Value = true;
            optionalMapsRevision.Value = ArenaMapCatalog.OptionalMapsRevision;
        }

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
            4f,
            "Invulnerability after an arena respawn (0 = disabled).");

        DisableLabyrinthSpawnTraps = config.Bind(
            "Arena", "DisableLabyrinthSpawnTraps", true,
            "Disable Labyrinth's entrance gun, flame, liquid and steam traps and drain the toxic pool during Arena only, " +
            "including their active damage, animations and continuous sounds. " +
            "Read once per raid; changes apply to the next Arena raid. Other puzzles/traps and ordinary raids are unchanged.");

        MovementEnabled = config.Bind(
            "Movement",
            "Enabled",
            true,
            "Arena movement overhaul (all knobs below only apply during arena sessions).");

        LandingRecoveryMultiplier = config.Bind("Movement", "LandingRecoveryMultiplier", 0.5f,
            new ConfigDescription("Duration of the local Arena player's hard-landing slowdown (0.5 = half, 1 = vanilla).",
                new AcceptableValueRange<float>(0.1f, 1f)));
        WeaponDrawSpeedMultiplier = config.Bind("Movement", "WeaponDrawSpeedMultiplier", 1.5f,
            new ConfigDescription("Local Arena firearm draw speed. Does not change reload or firing speed.",
                new AcceptableValueRange<float>(1f, 3f)));
        WeaponHolsterSpeedMultiplier = config.Bind("Movement", "WeaponHolsterSpeedMultiplier", 1.5f,
            new ConfigDescription("Local Arena firearm holster speed. Does not change reload or firing speed.",
                new AcceptableValueRange<float>(1f, 3f)));
        WeaponReloadSpeedMultiplier = config.Bind("Movement", "WeaponReloadSpeedMultiplier", 1.5f,
            new ConfigDescription("Local Arena firearm reload animation speed, including quick reloads, internal magazines and revolvers. " +
                "1 keeps native timing. Underbarrel launchers, firing, malfunction repair and inventory magazine loading stay unchanged.",
                new AcceptableValueRange<float>(1f, 3f)));
        AirVaultEnabled = config.Bind("Movement", "AirVaultEnabled", true,
            "Allow a deliberate native vault/climb input during an Arena jump when the obstacle and hands checks permit it.");
        AirControlEnabled = config.Bind("Movement", "AirControlEnabled", true,
            "Allow gentle WASD steering during local Arena jumps and falls. Preserves native horizontal speed, gravity and collision; no extra impulse from a stationary jump.");
        AirControlDegreesPerSecond = config.Bind("Movement", "AirControlDegreesPerSecond", 80f,
            new ConfigDescription("Maximum horizontal turn rate in degrees per second while airborne; 0 keeps native direction. Changes apply live. Long frame steps are capped for safety.",
                new AcceptableValueRange<float>(0f, 180f)));
        AirHipFireEnabled = config.Bind("Movement", "AirHipFireEnabled", true,
            "Allow firearm hip fire during an Arena jump. Native aiming, ammo, reload, malfunction and vault restrictions remain.");

        BotMovementEnabled = config.Bind("Bot movement", "BotMovementEnabled", true,
            "Boost grounded Arena bots on both teams, including PitFireTeam followers. " +
            "Also enables supported SAIN search sprint, shorter completed-reload wait and faster combat strafe. " +
            "Includes faster starts/direction blending and reduced weight-related locomotion inertia. " +
            "Does not change the local player, ordinary raids, presets or weapon animations.");
        BotWalkSpeedMult = config.Bind("Bot movement", "BotWalkSpeedMult", 1.5f,
            new ConfigDescription("Arena bot walk/strafe displacement multiplier. " +
                "Preserves native slowdowns; the bonus fades near native path corners and is suppressed during door handling or large low-FPS steps. 1 = no boost.",
                new AcceptableValueRange<float>(1f, 1.5f)));
        BotSprintSpeedMult = config.Bind("Bot movement", "BotSprintSpeedMult", 1.5f,
            new ConfigDescription("Arena bot sprint displacement multiplier. " +
                "Only affects a sprint the AI already chose; does not force sprint or add stamina. Safety limits can reduce the bonus. 1 = no boost.",
                new AcceptableValueRange<float>(1f, 1.5f)));

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

        BunnyhopEnabled = config.Bind("Movement", "BunnyhopEnabled", true,
            "Time a jump just before landing to chain hops and retain momentum. " +
            "6% speed gain per hop, capped at 25% above chain entry speed and 12 m/s. " +
            "Near-ground landing input takes priority over Double Jump; hard falls break the chain.");

        AkimboEnabled = config.Bind("Combat", "AkimboEnabled", false,
            "Automatically clone the held pistol in Arena, including automatic pistols and handgun revolvers. " +
            "One shot per trigger, alternating right and left; two shots per loaded cartridge, shared magazine/reload. " +
            "Hipfire only. Replica hides during reload/sprint/interactions. Flares and launchers are excluded. " +
            "Applies to the local player only; resumes on pistol draw and respawn. Experimental hand pose.");

        DoubleJumpEnabled = config.Bind(
            "Movement",
            "DoubleJumpEnabled",
            true,
            "Press the normal jump binding in the air for one extra jump, including after falling from a ledge. " +
            "Local Arena player only; landing restores the extra jump.");

        DoubleJumpHeightMult = config.Bind(
            "Movement",
            "DoubleJumpHeightMult",
            1f,
            new ConfigDescription("Extra jump height relative to the initial Arena jump (1 = same upward impulse).",
                new AcceptableValueRange<float>(0.25f, 1.5f)));

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

        BotLoadBoost = config.Bind(
            "Performance",
            "BotLoadBoost",
            true,
            "Let bot and kit loading borrow frame time while it is running. Once a frame is over " +
            "the game's scheduler budget (1000 / framerate limit, the lobby limit if the game " +
            "limit is off), loading advances one pass per frame instead of running until the " +
            "budget is spent - so a machine held at 60 fps under a 120 limit turns a one-second " +
            "bot load into a minute. This re-enables the scheduler's own force mode for the " +
            "duration of a load, at the cost of a longer frame while it runs. Arena only.");

        BotLoadBoostBudget = config.Bind(
            "Performance",
            "BotLoadBoostBudget",
            1f,
            new ConfigDescription(
                "Loading time granted per frame while boosted, as a multiple of half the target " +
                "frame (at a 120 fps limit, 1.0 = 4 ms; the value scales with the limit). Untested " +
                "above 1: on the scheduler's arithmetic only instantiation-bound steps get faster, " +
                "while every asynchronous wait still costs at least one frame and that frame is " +
                "longer by the extra budget. 3 is the game's own loading-screen value.",
                new AcceptableValueRange<float>(0.25f, 3f)));

        BotLoadBoostDelaySeconds = config.Bind(
            "Performance",
            "BotLoadBoostDelaySeconds",
            1f,
            new ConfigDescription(
                "How long loading must have been running before the boost kicks in on a machine " +
                "the scheduler sees as under budget, so one that finishes each load inside this " +
                "window never pays a frame for it. A machine it sees as over budget boosts after " +
                "0.25 s regardless (or this value if lower): there the boost is the only thing " +
                "moving loads at all - and a fast machine in a heavy moment reads over budget too, " +
                "which is a trade the log line shows. The verdict is fixed per load episode. Gaps " +
                "under half a second between loads do not restart the clock.",
                new AcceptableValueRange<float>(0f, 10f)));

        BotLoadBoostAsyncPriority = config.Bind(
            "Performance",
            "BotLoadBoostAsyncPriority",
            AsyncLoadPriority.Game,
            "EXPERIMENTAL, off by default. Unity's per-frame time slice for finishing asynchronous " +
            "loads, held at this level while the boost is held. On maps that use the game's scene " +
            "streamer it drops the slice to Low (2 ms a frame) and never puts it back; no arena map " +
            "has been shown to do that, and without it the value is Unity's own default - which the " +
            "boost's log line now reports, so a raid settles it. Raising it only helps if a single " +
            "load's integration needs more than the slice it has, which nothing has measured, while " +
            "the cost is certain: main-thread time ON TOP of the step budget, paid during live " +
            "combat. Low = 2 ms, BelowNormal = 4, Normal = 10, High = 50 (about 15 fps on a 60 fps " +
            "machine while loading - diagnostic only). Game = leave it alone. Handed back on release.");

        KitPreloadInLoadingScreen = config.Bind(
            "Performance",
            "KitPreloadInLoadingScreen",
            KitPreloadScope.InitialKit,
            "Load the arena kit's item bundles while the raid is still loading, instead of on " +
            "the first second of the raid. In the loading screen the game's own loading mode " +
            "multiplies the in-raid step budget (by 3 unless the app config says otherwise) and " +
            "nothing else is asking for the loader yet; measured on Customs, the start kit took " +
            "22 s to load on a 60 fps " +
            "machine (10 s on a fast one) once the raid had begun, sharing the loader with " +
            "the first bots. InitialKit = the kit the raid starts you in (the random draw is " +
            "made in the loading screen and kept for the raid start). WholeCatalogue = every " +
            "other kit as well, one after another, for as long as the loading screen lasts; " +
            "whatever has not started when the raid begins is left to the in-raid warm-up as " +
            "before, and a respawn that lands while one kit is still loading waits for that " +
            "kit. Untested on a slow machine - try it and read the log lines. Off = the " +
            "previous behaviour. Arena only.");

        AutoQuickSlots = config.Bind(
            "Kits",
            "AutoQuickSlots",
            true,
            "Bind the kit's medical items to the free quick slots (keys 4-0) on every " +
            "life. The game clears bindings with the body, which is a chore in a mode " +
            "built on dying. The layout is fixed by what an item DOES, so the same key " +
            "always holds the same kind of thing.");

        UseItemsAnywhere = config.Bind(
            "Kits",
            "UseItemsAnywhere",
            true,
            "Let anything worn be used and bound, and reload from magazines directly in the backpack during Arena. " +
            "The bulky medical items go where they FIT — a Grizzly is 2x2 and a Surv12 3x1 — " +
            "so without this the best medicine in the catalogue sits unreachable in the backpack.");

        EmptySecureContainer = config.Bind(
            "Kits",
            "EmptySecureContainer",
            true,
            "Leave the contents of the secure container at home and hide the now-empty " +
            "slot. The container itself still comes along because it is unlootable. Off: " +
            "your own supplies come with you, which outclasses every kit's medicine and " +
            "makes the medical tiers decorative. The special slots are a separate matter " +
            "and this switch does not reach them: arena kits wear pockets that have none, " +
            "so there is nothing there to empty either way. A protected 5v5 raid keeps " +
            "your own loadout and is exempt: the container stays as it is, shown and usable.");

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

        ReleaseTrimmedCorpses = config.Bind(
            "Performance",
            "ReleaseTrimmedCorpses",
            true,
            "Take a hidden corpse out of the scene for good once nothing can reach " +
            "it any more, instead of leaving it lying there invisible. A hidden body " +
            "is still a complete player object, and every search over the scene — the " +
            "game's own included — gets slower with each one that is left. PitFireTeam " +
            "teammates and your own NEWEST body are never taken away; an older body of " +
            "your own is released like anybody else's, once nothing still needs it.");

        GcHeapThresholdGB = config.Bind(
            "Performance",
            "GcHeapThresholdGB",
            4f,
            new ConfigDescription(
                "Switch Unity's incremental garbage collector back on once the managed " +
                "heap passes this many GB during an arena raid, instead of leaving it off " +
                "for the whole raid the way EFT does by default. 0 = never (pure vanilla): " +
                "the heap grows unbounded for as long as the raid runs, same as an ordinary " +
                "raid on a machine with enough RAM. Only acts while an arena session is " +
                "active AND the collector is currently disabled — on a machine EFT itself " +
                "kept it enabled (under 25 GB system RAM by default) this setting has " +
                "nothing to switch. The switch itself is EFT's own cheap collector-mode " +
                "assignment, never a forced collection, and it is timed to a local death, a " +
                "round break, or a bounded 120 s wait if neither comes first, so a short " +
                "raid never pays for it and a long one stays bounded. See " +
                "GC-THRESHOLD-REVIEW.md for the measured cost.",
                new AcceptableValueRange<float>(0f, 32f)));

        PoolPrewarmKeepIdle = config.Bind(
            "Performance",
            "PoolPrewarmKeepIdle",
            4,
            new ConfigDescription(
                "Cap how many idle instances EFT's own asset-prewarm keeps per resource " +
                "(weapons, AFAKs, survival kits, hands prefabs, ...) during an arena raid, " +
                "instead of letting it grow to match every bot that has ever spawned. 0 = " +
                "never (pure vanilla): ObjectsFactory.InitAndFillPools sizes every pool to " +
                "the TOTAL request count across the whole raid and that count never drops, " +
                "so Arena's continuous respawn keeps growing every pool for as long as the " +
                "raid runs (measured: 210 idle AFAKs and 207 idle survival kits after 267 " +
                "bodies in 17 minutes, ~380 stale components per body). Only acts while an " +
                "arena session is active AND the request is EFT's own periodic top-up to " +
                "total demand (reason \"LoadPlayerResources\") — pool CREATION and the " +
                "separate MaintainMinPoolSize top-up are never touched, and a pool run down " +
                "to this cap still creates on demand exactly as vanilla does for any pool's " +
                "first use, so a spawn is never short an item, only the standing idle " +
                "surplus is bounded. See POOL-PREWARM-REVIEW.md for the measured cost.",
                new AcceptableValueRange<int>(0, 64)));

        // Reports only when a damage event actually throws, which a healthy raid
        // never does — so it is on by default while the grenade finding in
        // CORPSE-CLEANUP-REVIEW.md is open, and costs a null check until then.
        LogDamageExceptions = config.Bind(
            "Diagnostics",
            "LogDamageExceptions",
            true,
            "Log the state of both sides of a hit when applying damage throws: which " +
            "of them is an object that has been destroyed, and which method the throw " +
            "came from. For the open grenade finding in CORPSE-CLEANUP-REVIEW.md. " +
            "It records and rethrows; it never changes a damage outcome.");

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
            "actively close in instead of patrolling until they happen to see you. " +
            "Team Deathmatch instead keeps both factions aware of live opponents.");

        ExcludePassiveSainPersonalities = config.Bind(
            "Bots",
            "ExcludePassiveSainPersonalities",
            true,
            "Arena only: replace SAIN Rat, Timmy and Coward assignments with the current preset's Normal personality. " +
            "Other personalities and SAIN preset files stay unchanged. Start a new raid when comparing this setting; " +
            "existing bots are not reassigned. PFT keeps its own follower combat behavior.");

        NoBotGestures = config.Bind(
            "Bots",
            "NoBotGestures",
            true,
            "Stop bots pausing for friendly hand signals during an arena raid. Vanilla " +
            "pairs a bot with the nearest bot of any side for a gesture, and every " +
            "gesture throws a random item out of the bot's secure container — with " +
            "SPT's bot loot that is a PMC's Surv12 or AFAK, left on the ground.");

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

        BleedingDamageMultiplier = config.Bind(
            "Character", "BleedingDamageMultiplier", 3f,
            new ConfigDescription(
                "HP loss from light and heavy bleeding for living Arena players and bots. " +
                "1 keeps vanilla damage; treatment, bleed duration and other damage stay unchanged.",
                new AcceptableValueRange<float>(1f, 10f)));

        MedicalUseSpeed = config.Bind(
            "Character",
            "MedicalUseSpeed",
            2f,
            new ConfigDescription(
                "Application speed for the local PMC's medkits, bandages, splints, " +
                "surgery kits, pills and injectors during an arena raid. Both the " +
                "health effect and hands animation use this factor; 1 keeps vanilla timing.",
                new AcceptableValueRange<float>(1f, 4f)));

        MagazineHandlingSpeed = config.Bind(
            "Character",
            "MagazineHandlingSpeed",
            2f,
            new ConfigDescription(
                "Loading and unloading speed for loose rounds in the local PMC's " +
                "magazines through the raid inventory, including internal magazines, " +
                "during an arena raid. 1 keeps vanilla timing; this does not change " +
                "weapon or hands reload animations.",
                new AcceptableValueRange<float>(1f, 4f)));

        GameMode = config.Bind(
            "Match",
            "GameMode",
            ArenaModeCatalog.DeathmatchId,
            new ConfigDescription(
                "Arena game mode selected on the native map screen or in F12. " +
                "deathmatch: individual kills with optional finite or endless rounds. " +
                "gungame: climb a ladder of weapons, one rung per kill. " +
                "tactical: buy a loadout, fight one squad, and sit the round out if you fall. " +
                "teamdeathmatch: equal USEC and BEAR teams with instant respawns and Arena kits. " +
                "zombie: endless waves of the infected, with instant respawns and Arena kits.",
                new AcceptableValueList<string>(
                    ArenaModeCatalog.DeathmatchId,
                    ArenaModeCatalog.EndlessId,
                    ArenaModeCatalog.RoundsId,
                    ArenaModeCatalog.GunGameId,
                    ArenaModeCatalog.TacticalId,
                    ArenaModeCatalog.TeamDeathmatchId,
                    ArenaModeCatalog.ZombieId)));

        TeamDeathmatchArea = config.Bind(
            "Team Deathmatch",
            "ExperimentalArea",
            ArenaAreaCatalog.FullMapId,
            new ConfigDescription(
                "Selected Arena area for every mode (the existing config key is retained). " +
                "Customs requires one of its three surveyed areas; other maps use the whole map. " +
                "Each Customs area has opposing faction starts; independent Team DM also selects its team size. " +
                "All surveyed outlines have side walls only, with no height restriction. The old rectangle test areas are retired. " +
                "Also selectable in the native Arena map menu, which adapts this value to the chosen map/mode. " +
                "Selected before loading; changes during a raid apply next match. " +
                "The area must pass its live navigation checks before any match starts.",
                new AreaSelectionValues()));

        AreaTeamSize = config.Bind("Team Deathmatch", "AreaPlayersPerTeam", 2,
            new ConfigDescription("Players per faction, including your PMC, for Customs areas with opposing team starts. " +
                "Separate from whole-map team size. Selected before loading; changes apply next match.",
                new AcceptableValueRange<int>(2, 5)));

        TeamDeathmatchSize = config.Bind(
            "Team Deathmatch",
            "PlayersPerTeam",
            5,
            new ConfigDescription(
                "Team Deathmatch capacity per faction, including your PMC on its own side. " +
                "Both teams refill fallen bots; your slot stays reserved while you respawn. " +
                "Read at match start. PitFireTeam companions are not used in this mode.",
                new AcceptableValueRange<int>(2, 12)));

        TeamDeathmatchKillLimit = config.Bind("Team Deathmatch", "KillLimit", 50,
            new ConfigDescription("Team kills needed to win independent Team Deathmatch (0 = unlimited). " +
                "The first kill or time limit reached ends the match. Selected before loading; changes during a raid apply next match.",
                new AcceptableValueRange<int>(0, 1000)));

        TeamDeathmatchTimeLimitSeconds = config.Bind("Team Deathmatch", "TimeLimitSeconds", 600,
            new ConfigDescription("Independent Team Deathmatch duration after both teams are ready and the countdown ends (0 = unlimited). " +
                "At the time limit the leading team wins; equal scores draw. Changes during a raid apply next match.",
                new AcceptableValueRange<int>(0, 7200)));

        ZombieMaxAlive = config.Bind("Zombie", "MaxAlive", 20,
            new ConfigDescription(
                "Infected allowed alive at once, across the whole raid. Also raises the native bot " +
                "cap to at least this + 2 (ArenaBotCapPatch). The ceiling is 56, not 60: the server " +
                "clone's own BotMax/MaxBotPerZone floor (ZombieLocationSettings.MinimumBotCap) is a " +
                "fixed 60, and the client cap must stay under that or the engine's own per-zone point " +
                "throttling would start silently dropping spawns below what this value asks for. " +
                "Frozen at match start.",
                new AcceptableValueRange<int>(4, 56)));

        ZombieStuckWaveTimeoutSeconds = config.Bind("Zombie", "StuckWaveTimeoutSeconds", 100f,
            new ConfigDescription(
                "Safety net for a wave's last infected getting wedged in geometry or stranded on an " +
                "unreachable NavMesh island: once the wave is SATURATED — every infected in it has already " +
                "been requested, OR alive+pending already fills what MaxAlive/the wave's own remainder " +
                "allows right now — and no infected has died for this long, whatever is still alive is " +
                "administratively retired. If the wave still has spawns left after that, it keeps going " +
                "(only the stuck survivor(s) were cleared); if not, the wave clears like normal. This " +
                "measures PROGRESS, not mere duration: any infected death restarts the clock, so a wave " +
                "the player is still actively (if slowly) clearing is never cleared out from under them — " +
                "only saturation with no deaths at all for the whole ceiling is \"stuck\" (progress-not-" +
                "duration changed 2026-10-06 after a GPU raid showed the old wall-clock-since-tail-start " +
                "version ending a wave mid-fight; saturation arming added the same day after a second GPU " +
                "raid showed a wave pinned at MaxAlive with 10 of 30 still to spawn and zero infected " +
                "deaths for over four minutes never arming this at all, because it was never \"fully " +
                "spawned\"). 0 disables the watchdog (the wave then waits for every infected to die " +
                "naturally, with no recovery if one cannot reach the player). Frozen at match start.",
                new AcceptableValueRange<float>(0f, 600f)));

        ZombieWaveStartSize = config.Bind("Zombie", "WaveStartSize", 30,
            new ConfigDescription(
                "Infected in wave 1. Raised from the v0 default of 8 (2026-10-06): within a wave, many " +
                "more infected spawn — not necessarily all at once, but without any pause, bounded by " +
                "MaxAlive — so the raid stays busy rather than trickling in a handful at a time. Frozen " +
                "at match start.",
                new AcceptableValueRange<int>(1, 200)));

        ZombieWaveGrowth = config.Bind("Zombie", "WaveGrowth", 10,
            new ConfigDescription(
                "Extra infected each wave adds over the last: wave n = WaveStartSize + WaveGrowth * (n - 1). " +
                "Raised from the v0 default of 4 (2026-10-06) to match the larger WaveStartSize. Frozen at " +
                "match start.",
                new AcceptableValueRange<int>(0, 100)));

        ZombieIntermissionSeconds = config.Bind("Zombie", "IntermissionSeconds", 10,
            new ConfigDescription(
                "Pause between a cleared wave (everything in it spawned, nothing from it still alive) " +
                "and the next one starting. Frozen at match start.",
                new AcceptableValueRange<int>(0, 60)));

        ZombieSpawnIntervalSeconds = config.Bind("Zombie", "SpawnIntervalSeconds", 0.75f,
            new ConfigDescription(
                "Seconds between individual infected spawn requests while a wave is filling up to MaxAlive " +
                "or refilling a gap a kill just opened. Lowered from the v0 default of 1.0s (2026-10-06) so " +
                "a bigger wave (see WaveStartSize) reaches MaxAlive faster and refills kills with less of a " +
                "gap. Frozen at match start.",
                new AcceptableValueRange<float>(0.2f, 10f)));

        ZombieShooterPercent = config.Bind("Zombie", "ShooterPercent", 0,
            new ConfigDescription(
                "Percent chance each spawned infected is the hard/Shooting variant (PM pistol) instead of " +
                "normal/Fast melee. 0 keeps every infected melee; 100 makes every infected a shooter. " +
                "Never easy (too slow to matter) or impossible (the native brain throws on construction). " +
                "Frozen at match start.",
                new AcceptableValueRange<int>(0, 100)));

        ZombiePftFastPathEnabled = config.Bind("Zombie", "PftFastPathEnabled", true,
            "Avoid redundant PitFireTeam layer inspection for verified native Arena infected. " +
            "Standby wake-up, companions and custom brains retain the original path. " +
            "Turn off for comparison or compatibility troubleshooting; applies immediately.");

        ZombieVoiceBudgetEnabled = config.Bind("Zombie", "VoiceBudgetEnabled", true,
            "Limit overlapping repetitive infected voices in Arena Zombies. " +
            "Nearby attack calls, critical sounds, AI hearing and companions keep native behavior. " +
            "Turn off for comparison; applies immediately.");

        ZombieOwnGear = config.Bind("Zombie", "OwnGear", false,
            "Bring your own protected equipment instead of a random Arena kit -- item 4 of the " +
            "2026-10-06 follow-up, mirroring how protected 5v5 (PitFireTeam) protects a PMC's entry " +
            "loadout for solo Zombie play: the raid-start kit swap and the loading-screen kit preload " +
            "are both skipped, every death respawns with the entry loadout restored instead of a fresh " +
            "kit, the wave-intermission ammo/consumable refill restores that same entry loadout, and " +
            "the secure container is left alone (not emptied) exactly like EmptySecureContainer already " +
            "skips for a frozen PitFireTeam roster. Also shown as the ZOMBIES row's EQUIPMENT fact " +
            "('Own gear' instead of 'Arena kits') and, since item 1 of the 2026-10-06 follow-up ('own " +
            "gear to the left'), as the LEFT mode panel's own EQUIPMENT choice row (moved out of the " +
            "right-hand mutators panel, which never grows a per-mode choice row again -- see " +
            "EquipmentChoiceEntry/TrySelectEquipmentChoice and Modes.ArenaEquipmentChoice). Frozen at " +
            "match start, like the other Zombie mode choices.");

        RoundKillTarget = config.Bind(
            "Match",
            "RoundKillTarget",
            20,
            new ConfigDescription(
                "Legacy migration only. Current rules use Deathmatch / KillLimit.",
                new AcceptableValueRange<int>(0, 200)));

        RoundTimeLimitSeconds = config.Bind(
            "Match",
            "RoundTimeLimitSeconds",
            300,
            new ConfigDescription(
                "Legacy migration only. Current rules use Deathmatch / TimeLimitSeconds.",
                new AcceptableValueRange<int>(0, 1800)));

        RoundIntermissionSeconds = config.Bind(
            "Match",
            "RoundIntermissionSeconds",
            15,
            new ConfigDescription(
                "Deathmatch: scoreboard time between rounds (frozen before loading).",
                new AcceptableValueRange<int>(3, 60)));

        RoundRespawn = config.Bind(
            "Rounds",
            "RoundRespawn",
            true,
            "Respawn with a fresh kit when a round ends, at a spawn point away " +
            "from the fight. Off: keep standing where the round ended, healed up.");

        DeathmatchRounds = config.Bind("Deathmatch", "Rounds", 0,
            new ConfigDescription("Rounds per match (0 = endless). Finite rounds require a kill or time limit. Frozen before loading.",
                new AcceptableValueRange<int>(0, 100)));
        DeathmatchKills = config.Bind("Deathmatch", "KillLimit", 0,
            new ConfigDescription("Kills per round (0 = off). FFA: first individual at the limit. All vs player: your kills. Frozen before loading.",
                new AcceptableValueRange<int>(0, 1000)));
        DeathmatchSeconds = config.Bind("Deathmatch", "TimeLimitSeconds", 0,
            new ConfigDescription("Seconds per round (0 = off). Both limits off means continuous play. Frozen before loading.",
                new AcceptableValueRange<int>(0, 7200)));
        DeathmatchFreeForAll = config.Bind("Deathmatch", "FreeForAll", true,
            "True: every individual fights everyone. False: all bots fight only the player. Frozen with server acknowledgement before loading.");
        var migrated = config.Bind("Deathmatch", "LegacyRulesMigrated", false,
            "Internal one-time migration from Endless/Rounds; leave enabled.");
        if (!migrated.Value)
        {
            if (string.Equals(GameMode.Value, ArenaModeCatalog.RoundsId, StringComparison.OrdinalIgnoreCase))
            {
                DeathmatchKills.Value = RoundKillTarget.Value;
                DeathmatchSeconds.Value = RoundTimeLimitSeconds.Value;
            }
            GameMode.Value = ArenaModeCatalog.NormalizeId(GameMode.Value);
            migrated.Value = true;
        }

        GunGameLadder = config.Bind(
            "Gun Game",
            "GunGameLadder",
            "",
            "Rung ids in ladder order, comma separated. Empty uses the generated " +
            "weapon ladder (config/ladder.json), or the kit catalog if there is none. " +
            "Read when the arena raid is armed and kept for that raid; an edit made " +
            "while a raid loads or runs applies to the next one.");

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
                "Recorded seconds shown after you go down; 0 disables killcam. " +
                "Requires enabled KillcamReplay. Respawn waits for playback, slow motion and live tail. " +
                "Frozen when the raid is armed; Tactical still waits for the next round.",
                new AcceptableValueRange<float>(0f, 10f)));

        DeathmatchReplaySeconds = BindReplay(config, "Deathmatch");
        GunGameReplaySeconds = BindReplay(config, "Gun Game");
        TeamDeathmatchReplaySeconds = BindReplay(config, "Team Deathmatch");
        ZombieReplaySeconds = BindReplay(config, "Zombie");

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
            "after your PMC dies, until the round ends. The observer banner shows the selected bot's current and maximum HP.");

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
                "For matches without a time limit: minutes to reach full pressure on the clock alone. " +
                "Timed Team DM instead reaches full pressure at 80% of its frozen match duration, excluding assembly/countdown. " +
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

        ScorePanelOffsetX = config.Bind(
            "HUD", "ScorePanelOffsetX", 0f,
            new ConfigDescription("Horizontal offset from the score panel's default top-center position. " +
                "Positive moves right, negative moves left; 0 restores the default. " +
                "Uses scaled 1920x1080 HUD units. Applies immediately, including the start/wait notice below it.",
                new AcceptableValueRange<float>(-500f, 500f)));

        ScorePanelOffsetY = config.Bind(
            "HUD", "ScorePanelOffsetY", 0f,
            new ConfigDescription("Vertical offset from the score panel's default position below the quickslots. " +
                "Positive moves down, negative moves up; 0 restores the default. " +
                "Uses scaled 1920x1080 HUD units. Applies immediately, including the start/wait notice below it.",
                new AcceptableValueRange<float>(-100f, 700f)));

        ShowFriendlyMarkers = config.Bind(
            "HUD", "ShowFriendlyMarkers", true,
            "Show a small diamond above visible friendly bots in Team Deathmatch and protected PitFireTeam squads. " +
            "Uses FriendlyTracerColor; markers are hidden behind obstacles and in ordinary raids.");

        ShowKillfeed = config.Bind(
            "HUD",
            "Killfeed",
            true,
            "Killfeed lines (attacker, victim, weapon, distance) in the top-left corner; leaves the top-right free for a minimap.");

        KillfeedOtherKills = config.Bind(
            "HUD",
            "KillfeedOtherKills",
            true,
            "Also show bot-vs-bot kills in the killfeed.");

        KillfeedOffsetX = config.Bind(
            "HUD", "KillfeedOffsetX", 0f,
            new ConfigDescription("Horizontal offset from the killfeed's default top-left position. " +
                "Positive moves right, negative moves left; 0 restores the default. " +
                "Uses scaled 1920x1080 HUD units. Applies immediately to existing and new entries.",
                new AcceptableValueRange<float>(-500f, 1500f)));

        KillfeedOffsetY = config.Bind(
            "HUD", "KillfeedOffsetY", 0f,
            new ConfigDescription("Vertical offset from the killfeed's default position. " +
                "Positive moves down, negative moves up; 0 restores the default. " +
                "Uses scaled 1920x1080 HUD units. Applies immediately to existing and new entries.",
                new AcceptableValueRange<float>(-100f, 700f)));

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

        CombineDamageNumbers = config.Bind("HUD", "CombineDamageNumbers", false,
            "Add hits on the same target within 0.25 seconds to one floating number. " +
            "Body and armor damage stay separate. Presentation only; applies immediately.");

        HitmarkerSounds = config.Bind(
            "Audio", "HitmarkerSounds", true,
            "Short Arena hit, headshot and kill sounds, independent of the visual HUD and announcer. " +
            "Replaceable PCM16 mono WAVs in BepInEx/plugins/ArenaMode/hitmarkers/; see AUDIO-LICENSES.md.");

        HitmarkerVolume = config.Bind(
            "Audio", "HitmarkerVolume", 0.7f,
            new ConfigDescription("Hitmarker sound volume, independent of Interface and announcer volume. " +
                "Still respects overall game volume and mute; changes apply immediately.",
                new AcceptableValueRange<float>(0f, 1f)));

        AnnouncerEnabled = config.Bind(
            "Audio",
            "Announcer",
            true,
            "Announcer stingers (combat awards, streaks, Tactical round/match cues) — plays .ogg files from " +
            "BepInEx/plugins/ArenaMode/sounds/. Missing sounds are skipped and logged. " +
            "Master switch: when off, the on-screen kill callouts (DOUBLE KILL, ...) are hidden too.");

        AnnouncerVolume = config.Bind(
            "Audio",
            "AnnouncerVolume",
            0.7f,
            new ConfigDescription("Announcer volume, independent of Interface volume by default. " +
                "Still respects the game's overall volume and mute.", new AcceptableValueRange<float>(0f, 1f)));

        AnnouncerUseUiVolume = config.Bind(
            "Audio", "AnnouncerUseUiVolume", false,
            "Off: use the dedicated Arena output controlled by AnnouncerVolume and overall game volume. " +
            "On: restore the legacy Interface-volume output for compatibility.");

        // Item 2 of the 2026-10-06 follow-up ("announcer mutator"): per-mode
        // gate layered on top of the master Announcer switch above. Defaults
        // come from ArenaAnnouncerGate.DefaultEnabled so this table can never
        // drift from the one ArenaMode.Tests exercises.
        DeathmatchAnnouncerEnabled = BindAnnouncer(config, "Deathmatch",
            Modes.ArenaAnnouncerGate.DefaultEnabled(ArenaModeCatalog.DeathmatchId));
        GunGameAnnouncerEnabled = BindAnnouncer(config, "Gun Game",
            Modes.ArenaAnnouncerGate.DefaultEnabled(ArenaModeCatalog.GunGameId));
        TacticalAnnouncerEnabled = BindAnnouncer(config, "Tactical",
            Modes.ArenaAnnouncerGate.DefaultEnabled(ArenaModeCatalog.TacticalId));
        TeamDeathmatchAnnouncerEnabled = BindAnnouncer(config, "Team Deathmatch",
            Modes.ArenaAnnouncerGate.DefaultEnabled(ArenaModeCatalog.TeamDeathmatchId));
        ZombieAnnouncerEnabled = BindAnnouncer(config, "Zombie",
            Modes.ArenaAnnouncerGate.DefaultEnabled(ArenaModeCatalog.ZombieId));

        MuteLabyrinthAlarm = config.Bind(
            "Audio", "MuteLabyrinthAlarm", true,
            "Silence the repeating center siren on Labyrinth during Arena only. " +
            "Other sounds and map triggers are unchanged. Takes effect immediately; " +
            "turning this off lets an active alarm resume at its next sound cycle (about 4 seconds).");

        ArenaLightingEnabled = config.Bind(
            "Effects",
            "ArenaLighting",
            true,
            "Labs, Factory (day/night) and Labyrinth: add restrained runtime lighting for round starts, the final seconds, " +
            "and Tactical victory/defeat. No map bundles or shared map materials are changed.");

        ArenaLightingStrength = config.Bind(
            "Effects",
            "ArenaLightingStrength",
            1f,
            new ConfigDescription(
                "Brightness multiplier for Arena's additive phase light on Labs, Factory and Labyrinth (0 = invisible).",
                new AcceptableValueRange<float>(0f, 2f)));

        ArenaLightingPressureSeconds = config.Bind(
            "Effects",
            "ArenaLightingPressureSeconds",
            30,
            new ConfigDescription(
                "Turn the Arena lighting red for this many final seconds in timed Rounds and Tactical combat (0 = disabled).",
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

        TeamDeathmatchFriendlyFire = config.Bind("Team Deathmatch", "FriendlyFire", true,
            "Allow damage between teammates. Frozen when the next Arena raid is armed; self-damage is unchanged.");
        TacticalFriendlyFire = config.Bind("Tactical", "FriendlyFire", true,
            "Allow damage between teammates in PitFireTeam 5v5. Frozen at arming; solo Tactical is unchanged.");
        ZombieFriendlyFire = config.Bind("Zombie", "FriendlyFire", false,
            "Allow damage between the PMC and PitFireTeam companions, including companion-to-companion damage. " +
            "Frozen at arming; solo Zombies, enemy damage and self-damage are unchanged.");

        TeamTracers = config.Bind(
            "Effects",
            "TeamTracers",
            true,
            "Give every Arena projectile a team-colored tracer, including ammunition " +
            "that has no tracer in ordinary raids. Team-only, like TeamLasers: applies to Team " +
            "Deathmatch and Tactical/Zombie squads (PitFireTeam). Deathmatch, Gun Game, solo Zombie and solo " +
            "Tactical always keep the native ammunition tracers.");

        TeamLasers = config.Bind(
            "Effects",
            "TeamLasers",
            true,
            "Team Deathmatch and protected PitFireTeam squads: color active visible weapon lasers using FriendlyTracerColor and OpponentTracerColor. " +
            "Does not switch lasers on or change infrared visibility or flashlights. Ordinary raids stay unchanged.");

        VisibleLaserBrightness = config.Bind(
            "Effects",
            "VisibleLaserBrightness",
            3f,
            new ConfigDescription(
                "Arena visible weapon laser intensity minimum relative to the original prefab (1 = no Arena boost). " +
                "An existing larger intensity, including BrightLasers, is preserved rather than multiplied again. " +
                "Independent of team colors; does not change laser activation, IR, flashlights, width or ordinary raids.",
                new AcceptableValueRange<float>(1f, 5f)));

        VisibleLaserBeamBrightness = config.Bind(
            "Effects",
            "VisibleLaserBeamBrightness",
            3f,
            new ConfigDescription(
                "Arena visible laser BEAM brightness minimum relative to the original material (1 = no extra beam boost). " +
                "Brightens the full beam without further brightening its impact point or spot light. " +
                "An already larger beam factor, including BrightLasers, is preserved rather than multiplied again. " +
                "Independent of VisibleLaserBrightness; leaves IR/night-vision factors, width and ordinary raids unchanged.",
                new AcceptableValueRange<float>(1f, 5f)));

        FriendlyTracerColor = config.Bind(
            "Effects",
            "FriendlyTracerColor",
            ArenaTracerColor.Cyan,
            "Color for friendly tracers, killfeed names and friendly markers. Team-mode lasers also use it when TeamLasers is enabled; killfeed colors do not depend on effects being enabled.");

        OpponentTracerColor = config.Bind(
            "Effects",
            "OpponentTracerColor",
            ArenaTracerColor.Red,
            "Color for opposing tracers and killfeed names. Team-mode lasers also use it when TeamLasers is enabled; killfeed colors do not depend on effects being enabled.");

        RagdollImpulseScale = config.Bind(
            "Effects",
            "RagdollImpulseScale",
            5f,
            new ConfigDescription(
                "Fatal-shot ragdoll impulse multiplier (1 = vanilla, punchy deaths above).",
                new AcceptableValueRange<float>(1f, 10f)));
    }

    public static bool IsArenaMap(string locationId)
        // The fixed catalog prevents a remotely edited allowlist from exposing
        // an unsupported normal map. Factory night is the day tile's time variant.
        => ArenaMapCatalog.AllowlistContains(Maps?.Value, locationId);
}

/// <summary>Upgrade exact saved preferences only; retired wire identities remain invalid.</summary>
internal sealed class AreaSelectionValues : AcceptableValueList<string>
{
    internal AreaSelectionValues() : base(ArenaAreaCatalog.FullMapId,
        ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaAreaCatalog.CustomsFactoryEastPolygonId,
        ArenaAreaCatalog.CustomsYardId) { }
    public override object Clamp(object value)
    {
        if (value is string id)
            value = id switch
            {
                "customs-factory-center-polygon-v1" or "customs-factory-center-teams-v1"
                    => ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
                "customs-factory-east-polygon-v1" => ArenaAreaCatalog.CustomsFactoryEastPolygonId,
                _ => id,
            };
        return base.Clamp(value);
    }
}
