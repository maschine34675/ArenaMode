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

namespace ArenaMode;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(PitFireTeamCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(DynamicMapsCorpseMarkers.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.ArenaMode";
    public const string PluginName = "maschine-ArenaMode";
    public const string PluginVersion = "0.1.0";

    public static ManualLogSource ModLogger { get; private set; }

    public static Plugin Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        ModLogger = base.Logger;
        RedeployCore.Init(ModLogger);
        WarnIfCoreIsOlderThanBuild();
        Settings.Init(Config);
        Hud.InfoPanels.InfoPanelSettings.Init(Config,
            System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.maschine.RaidInfoPanels.cfg"));
        PitFireTeamCompat.Initialize();
        ArenaMenuSelection.RegisterCompatibilityCleanup(PitFireTeamCompat.Clear);
        ArenaMenuSelection.RegisterTeamArenaQuery(() => PitFireTeamCompat.TeamArenaActive);

        ArenaSession.Initialize();
        DynamicMapsCorpseMarkers.Enable();
        Hud.ArenaExtractionList.Enable();
        // After ArenaSession: RaidLifecycle handlers run in subscription order and
        // the match service needs IsActive already latched when its handler fires.
        Modes.ArenaMatchService.Initialize();
        Spectator.ArenaSpectatorService.Initialize();
        LocalRaidStartedPatch.EnablePatch();
        ArenaSideSelectionPatch.Enable();
        Session.Preview.ArenaPreviewFlowPatch.Enable();
        ArenaLocationSelectionPatch.Enable();
        ArenaRaidSettingsApplyPatch.EnablePatch();
        SkipPreRaidScreensPatch.Enable();
        MapAccessPatch.Enable();

        DeathInterception.EnsurePatched();
        DeathInterception.Register(new ArenaRespawnDriver());
        Combat.CombatPatches.Enable();
        Movement.ArenaLegInjuryPatches.Enable();
        Combat.ArenaMedicalPatches.Enable();
        Combat.ArenaMagazinePatches.Enable();
        Bots.ArenaBotPatches.Enable();
        new Bots.NoBotGesturesPatch().Enable();
        new Bots.NoCorpseLootingPatch().Enable();
        Bots.ArenaBotLifecycleSafety.Enable();
        Bots.ArenaPickupTeardownSafety.Enable();
        Bots.ArenaWeaponCallbackSafety.Enable();
        Bots.ArenaSainDisableSafety.Enable();
        Bots.ArenaSainActionSafety.Enable();
        Bots.ArenaSainPersonalities.Enable();
        Bots.TdmSearchTempo.Enable();
        Bots.ArenaBotDifficultyClockPatch.EnablePatch();
        Bots.ArenaProfilePreloadPatch.EnablePatch();
        Areas.ArenaAreaNavigationPatches.Enable();
        Areas.ArenaAreaSpawnPointsPatch.EnablePatch();
        Areas.ArenaAreaInitialSpawnPatch.EnablePatch();
        Areas.ArenaAreaLoadPatch.Enable();
        Effects.ArenaTracerPatches.Enable();
        Effects.ArenaTeamLaserColors.Enable();
        Notifications.ArenaProgressNotifications.Enable();
        Diagnostics.ArenaPreviewDiagnostics.Enable();
        ArenaGpuAcceptanceCompat.Enable();
        Audio.LabyrinthAlarmPatches.Enable();
        LabyrinthSpawnTraps.Enable();
        Movement.ArenaWeightPatches.Enable();
        Movement.ArenaMovementPatches.Enable();
        Movement.ArenaBotMovement.Enable();
        Movement.ArenaDoubleJump.Enable();
        Movement.ArenaAirControl.Enable();
        Movement.ArenaHandlingPatches.Enable();
        Movement.ArenaAirVault.Enable();
        Movement.ArenaAirFire.Enable();
        Movement.SprintAdsPatches.Enable();
        Movement.FastDeployPatches.Enable();
        Loot.OpenLootTracker.Enable();
        Loot.ArenaLootPatches.Enable();
        Kits.ArenaReachPatches.Enable();
        Kits.ArenaSlotVisibilityPatches.Enable();
        Loot.AutoSearchService.Initialize();
        PerfectCullingFix.EnableOnce();

        Hud.ArenaInputBlocker.Apply();
        Spectator.ArenaSpectatorCameraPatches.Enable();
        Spectator.ArenaSpectatorInputPatch.Enable();
        Hud.TacticalBuyPanel.ApplyInputPatch();
        gameObject.AddComponent<ArenaController>();
        gameObject.AddComponent<Areas.ArenaAreaCapture>();
        gameObject.AddComponent<Hud.ArenaPanel>();
        gameObject.AddComponent<Hud.TacticalBuyPanel>();
        gameObject.AddComponent<Spectator.ArenaSpectatorOverlay>();

        Audio.AnnouncerService.SetPluginDirectory(System.IO.Path.GetDirectoryName(Info.Location));
        Audio.HitmarkerAudioService.SetPluginDirectory(System.IO.Path.GetDirectoryName(Info.Location));

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Redeploy.Core {RedeployCore.Version})");
    }

    private void OnDestroy()
    {
        DynamicMapsCorpseMarkers.Shutdown();
        Hud.ArenaExtractionList.Shutdown();
        ArenaGpuAcceptanceCompat.Shutdown();
        Movement.ArenaDoubleJump.Shutdown();
        Movement.ArenaAirControl.Shutdown();
        Movement.ArenaHandlingPatches.Shutdown();
        Movement.ArenaAirVault.Shutdown();
        Kits.KitOpticPreloadGuard.Shutdown();
        Areas.ArenaAreaRuntime.Reset();
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
        Notifications.ArenaProgressNotifications.Shutdown();
        Diagnostics.ArenaPreviewDiagnostics.Shutdown();
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

    /// <summary>
    /// Driver skew check (Redeploy docs/RESPAWN-API.md 3.9): every zip carries the same flat
    /// maschine-Redeploy.Core.dll and the last one unpacked wins, so an older zip can put an
    /// older Core under this build. Arena keeps running on it, without its fixes; the finding
    /// goes to the log and to Chainloader.DependencyErrors, which SPT reports after loading.
    /// RedeployCore.Version exists since Core 0.9, so reading it is safe against any Core.
    /// </summary>
    private static void WarnIfCoreIsOlderThanBuild()
    {
        try
        {
            var warning = CoreVersionGate.Evaluate(
                PluginName, RedeployCore.Version, CoreVersionGate.ReadBuiltAgainst(typeof(Plugin).Assembly));
            if (warning == null)
                return;

            ModLogger.LogWarning(warning);
            Chainloader.DependencyErrors.Add(warning);
        }
        catch (System.Exception ex)
        {
            ModLogger.LogWarning($"Core version check skipped: {ex.Message}");
        }
    }
}
