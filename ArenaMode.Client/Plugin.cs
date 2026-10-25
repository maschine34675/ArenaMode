using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Respawn;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Redeploy.Core;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using Redeploy.Drivers;
using System.Runtime.CompilerServices;

namespace ArenaMode;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ArenaBigBrainLibrary.BigBrainGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(PitFireTeamCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(DynamicMapsCorpseMarkers.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.lacyway.ch", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.ozen.continuousloadammo", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.pein.camerarecoilmod", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.ArenaMode";
    public const string PluginName = "maschine-ArenaMode";
    public const string PluginVersion = "0.2.1";

    public static ManualLogSource ModLogger { get; private set; }

    public static Plugin Instance { get; private set; }

    private bool _runtimeStarted;

    private void Awake()
    {
        ModLogger = base.Logger;
        string error = CoreVersionGate.Check(PluginName, typeof(Plugin).Assembly);
        if (error != null)
        {
            ModLogger.LogError(error);
            Chainloader.DependencyErrors.Add(error);
            // This UI island must not load Core or initialize any raid hooks.
            try { ArenaCoreUnavailableMenu.Enable(); }
            catch (System.Exception ex) { ModLogger.LogError("Arena dependency notice could not initialize: " + ex); }
            enabled = false;
            return;
        }
        Instance = this;
        // Preserve cleanup after partial initialization, but never run it for a
        // rejected dependency: Unity may call OnDestroy on disabled components.
        _runtimeStarted = true;
        InitializeRuntime();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitializeRuntime()
    {
        RedeployCore.Init(ModLogger);
        Settings.Init(Config);
        ArenaParticipantSettings.Init(Config);
        Pickups.ArenaPickupSettings.Init(Config);
        Hud.InfoPanels.InfoPanelSettings.Init(Config,
            System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.maschine.RaidInfoPanels.cfg"));
        PitFireTeamCompat.Initialize();
        ArenaMedicalBodyScope.Initialize();
        ArenaTarkovaColaCompat.Initialize();
        ArenaMenuSelection.RegisterCompatibilityAdmission(ArenaOptionalMods.GetRejectionReason);
        ArenaMenuSelection.RegisterCompatibilityCleanup(PitFireTeamCompat.Clear);
        ArenaMenuSelection.RegisterTeamArenaQuery(() => PitFireTeamCompat.TeamArenaActive);

        ArenaSession.Initialize();
        Diagnostics.ArenaNativeModelDiagnostics.Enable();
        DynamicMapsCorpseMarkers.Enable();
        Hud.ArenaExtractionList.Enable();
        // After ArenaSession: RaidLifecycle handlers run in subscription order and
        // the match service needs IsActive already latched when its handler fires.
        Modes.ArenaMatchService.Initialize();
        Performance.GcThresholdMonitor.Initialize();
        Spectator.ArenaSpectatorService.Initialize();
        ArenaLocalRaidStartedPatch.EnablePatch();
        ArenaSideSelectionPatch.Enable();
        ArenaRaidExit.Enable();
        ArenaRewardCapture.Enable();
        ArenaFailedStartIntro.Enable();
        ArenaPostRaidScreens.Enable();
        Session.Preview.ArenaPreviewFlowPatch.Enable();
        ArenaLocationSelectionPatch.Enable();
        ArenaSessionModeCaptionPatch.Enable();
        ArenaRaidSettingsApplyPatch.EnablePatch();
        SkipPreRaidScreensPatch.Enable();
        MapAccessPatch.Enable();

        DeathInterception.EnsurePatched();
        DeathInterception.Register(new ArenaRespawnDriver());
        Combat.CombatPatches.Enable();
        Combat.ArenaDefaultFireMode.Enable();
        Pickups.ArenaPowerups.Enable();
        Combat.Fire.ArenaFireRuntime.Enable(Config);
        Combat.Fire.ArenaMolotovModel.Enable();
        Combat.Akimbo.ArenaAkimbo.Enable();
        Pickups.ArenaInfiniteAmmo.Enable();
        Pickups.ArenaPickupExaminationPatch.EnablePatch();
        Movement.ArenaLegInjuryPatches.Enable();
        Combat.ArenaMedicalPatches.Enable();
        Combat.ArenaMagazinePatches.Enable();
        Bots.ArenaBotPatches.Enable();
        new Bots.ArenaNoBotGesturesPatch().Enable();
        new Bots.ArenaNoCorpseLootingPatch().Enable();
        Bots.ArenaBotLifecycleSafety.Enable();
        Bots.ArenaPickupTeardownSafety.Enable();
        Bots.ArenaWeaponCallbackSafety.Enable();
        Bots.ArenaSainDisableSafety.Enable();
        Bots.ArenaSainActionSafety.Enable();
        Bots.ArenaSainPersonalities.Enable();
        Performance.PoolPrewarmMonitor.Initialize();
        Bots.TdmSearchTempo.Enable();
        Bots.ArenaBotDifficultyClockPatch.EnablePatch();
        Bots.ArenaProfilePreloadPatch.EnablePatch();
        Bots.ZombieNativeEventBridge.EnablePatch();
        Bots.ZombiePerformancePatches.EnablePatch();
        ZombiePftFastPath.EnablePatch();
        RaidLifecycle.OnRaidEnded += ZombiePftFastPath.Reset;
        Bots.ZombieVoiceBudget.EnablePatch();
        RaidLifecycle.OnRaidEnded += Bots.ZombieVoiceBudget.Reset;
        Bots.ZombieCrowdRuntime.EnablePatch();
        RaidLifecycle.OnRaidEnded += Bots.ZombieCrowdRuntime.Reset;
        Bots.ZombieBodyCullingRuntime.EnablePatch();
        RaidLifecycle.OnRaidEnded += Bots.ZombieBodyCullingRuntime.Reset;
        Performance.ArenaBotHotPaths.Initialize();
        Performance.ProfileBuildBudget.Initialize();
        Bots.ZombieMeleePatch.EnablePatch();
        Areas.ArenaAreaNavigationPatches.Enable();
        Areas.ArenaAreaSpawnPointsPatch.EnablePatch();
        Areas.ArenaAreaInitialSpawnPatch.EnablePatch();
        Bots.ArenaTeamBoxRuntime.Enable();
        Areas.ArenaAreaLoadPatch.Enable();
        Effects.ArenaTracerPatches.Enable();
        Effects.ArenaTeamLaserColors.Enable();
        Notifications.ArenaProgressNotifications.Enable();
        Diagnostics.ArenaPreviewDiagnostics.Enable();
        Diagnostics.ArenaDamageExceptionLog.Enable();
        ArenaGpuAcceptanceCompat.Enable();
        Audio.LabyrinthAlarmPatches.Enable();
        LabyrinthSpawnTraps.Enable();
        Movement.ArenaWeightPatches.Enable();
        Movement.ArenaMovementPatches.Enable();
        Movement.ArenaSurgicalWalk.Enable();
        Movement.ArenaBotMovement.Enable();
        Movement.ArenaBotAgility.Enable();
        Bots.ArenaBotTempo.Enable();
        Movement.ArenaDoubleJump.Enable();
        Movement.ArenaAirControl.Enable();
        Movement.ArenaBunnyhop.Enable();
        Movement.ArenaHandlingPatches.Enable();
        Movement.ArenaAirVault.Enable();
        Movement.ArenaAirFire.Enable();
        Movement.SprintAdsPatches.Enable();
        Movement.FastDeployPatches.Enable();
        Loot.OpenLootTracker.Enable();
        Loot.ArenaLootPatches.Enable();
        Kits.ArenaReachPatches.Enable();
        Kits.ArenaBackpackReload.Enable();
        Kits.ArenaSlotVisibilityPatches.Enable();
        Loot.AutoSearchService.Initialize();
        PerfectCullingFix.EnableOnce();

        Hud.ArenaInputBlocker.Apply();
        Spectator.ArenaSpectatorCameraPatches.Enable();
        Spectator.ArenaSpectatorInputPatch.Enable();
        Hud.TacticalBuyPanel.ApplyInputPatch();
        gameObject.AddComponent<ArenaController>();
        gameObject.AddComponent<Combat.Akimbo.ArenaAkimbo>();
        gameObject.AddComponent<Areas.ArenaAreaCapture>();
        gameObject.AddComponent<Pickups.ArenaPickupCapture>();
        gameObject.AddComponent<Hud.ArenaPanel>();
        gameObject.AddComponent<Hud.TacticalBuyPanel>();
        gameObject.AddComponent<Spectator.ArenaSpectatorOverlay>();

        Audio.AnnouncerService.SetPluginDirectory(System.IO.Path.GetDirectoryName(Info.Location));
        Audio.HitmarkerAudioService.SetPluginDirectory(System.IO.Path.GetDirectoryName(Info.Location));

        // Last: every ArenaMode entry is bound by now (the incendiary prototype binds
        // late, in ArenaFireRuntime.Enable). Display names, order, advanced/hidden.
        ArenaConfigMenu.Apply(Config);

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Redeploy.Core {RedeployCore.Version}; build/MVID {typeof(Plugin).Module.ModuleVersionId})");
    }

    private void Start()
    {
        if (_runtimeStarted) StartRuntime();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StartRuntime() => ArenaBigBrainLibrary.Initialize(ModLogger, Info.Location);

    private void Update()
    {
        if (_runtimeStarted) UpdateRuntime();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateRuntime()
    {
        ArenaBigBrainLibrary.Tick();
    }

    private void OnDestroy()
    {
        if (!_runtimeStarted) return;
        _runtimeStarted = false;
        ShutdownRuntime();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ShutdownRuntime()
    {
        ArenaTarkovaColaCompat.Shutdown();
        ArenaMedicalBodyScope.Shutdown();
        ArenaBigBrainLibrary.Shutdown();
        Combat.ArenaDefaultFireMode.Shutdown();
        PitFireTeamAreaSpawns.Reset();
        Combat.ArenaSkills.Restore();
        Movement.VaultingTuning.Restore();
        Movement.FastDeployPatches.Restore();
        Pickups.ArenaPickupRuntime.Reset();
        Combat.Fire.ArenaFireRuntime.Reset();
        Combat.Akimbo.ArenaAkimbo.Reset();
        DynamicMapsCorpseMarkers.Shutdown();
        Hud.ArenaExtractionList.Shutdown();
        ArenaGpuAcceptanceCompat.Shutdown();
        Movement.ArenaDoubleJump.Shutdown();
        Movement.ArenaBunnyhop.Shutdown();
        Movement.ArenaAirControl.Shutdown();
        Movement.ArenaHandlingPatches.Shutdown();
        Movement.ArenaAirVault.Shutdown();
        Kits.KitOpticPreloadGuard.Shutdown();
        Areas.ArenaAreaRuntime.Reset();
        Bots.ArenaTeamBoxRuntime.Shutdown();
        Areas.ArenaAreaInitialSpawnPatch.Shutdown();
        Areas.ArenaAreaNavigationPatches.Shutdown();
        Areas.ArenaAreaSpawnPointsPatch.Shutdown();
        Areas.ArenaAreaLoadPatch.Shutdown();
        Bots.ArenaSainActionSafety.Shutdown();
        Bots.ArenaSainDisableSafety.Shutdown();
        Bots.ArenaPickupTeardownSafety.Shutdown();
        Bots.ArenaWeaponCallbackSafety.Shutdown();
        Bots.ArenaBotLifecycleSafety.Shutdown();
        Bots.ArenaSainPersonalities.Shutdown();
        Bots.TdmSearchTempo.Shutdown();
        Bots.ArenaBotTempo.Shutdown();
        Movement.ArenaBotAgility.Shutdown();
        Notifications.ArenaProgressNotifications.Shutdown();
        Diagnostics.ArenaPreviewDiagnostics.Shutdown();
        Diagnostics.ArenaNativeModelDiagnostics.Shutdown();
        Hud.ArenaHud.Detach();
        Combat.ArenaMagazinePatches.Shutdown();
        Combat.ArenaMedicalPatches.Shutdown();
        Effects.ArenaLightingDirector.Shutdown();
        Effects.ArenaTeamLaserColors.Reset();
        Effects.ArenaLaserBrightness.Reset();
        Audio.AnnouncerService.Shutdown();
        Audio.HitmarkerAudioService.Detach();
        Audio.LabyrinthAlarmPatches.Shutdown();
        LabyrinthSpawnTraps.Shutdown();
        Spectator.ArenaSpectatorService.Shutdown();
        Hud.ArenaInputBlocker.Shutdown();
    }

}
