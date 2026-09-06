using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Respawn;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Logging;
using Redeploy.Core;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;

namespace ArenaMode;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(PitFireTeamCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
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
        Settings.Init(Config);
        PitFireTeamCompat.Initialize();
        ArenaMenuSelection.RegisterCompatibilityCleanup(PitFireTeamCompat.Clear);
        ArenaMenuSelection.RegisterTeamArenaQuery(() => PitFireTeamCompat.TeamArenaActive);

        ArenaSession.Initialize();
        // After ArenaSession: RaidLifecycle handlers run in subscription order and
        // the match service needs IsActive already latched when its handler fires.
        Modes.ArenaMatchService.Initialize();
        Spectator.ArenaSpectatorService.Initialize();
        LocalRaidStartedPatch.EnablePatch();
        ArenaSideSelectionPatch.Enable();
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
        Effects.ArenaTracerPatches.Enable();
        Effects.ArenaTeamLaserColors.Enable();
        Notifications.ArenaProgressNotifications.Enable();
        Audio.LabyrinthAlarmPatches.Enable();
        LabyrinthSpawnTraps.Enable();
        Movement.ArenaWeightPatches.Enable();
        Movement.ArenaMovementPatches.Enable();
        Movement.SprintAdsPatches.Enable();
        Movement.FastDeployPatches.Enable();
        Loot.OpenLootTracker.Enable();
        Loot.ArenaLootPatches.Enable();
        Loot.AutoSearchService.Initialize();
        PerfectCullingFix.EnableOnce();

        Hud.ArenaInputBlocker.Apply();
        Spectator.ArenaSpectatorCameraPatches.Enable();
        Spectator.ArenaSpectatorInputPatch.Enable();
        gameObject.AddComponent<ArenaController>();
        gameObject.AddComponent<Hud.ArenaPanel>();
        gameObject.AddComponent<Hud.TacticalBuyPanel>();
        gameObject.AddComponent<Spectator.ArenaSpectatorOverlay>();

        Audio.AnnouncerService.SetPluginDirectory(System.IO.Path.GetDirectoryName(Info.Location));

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Redeploy.Core {RedeployCore.Version})");
    }

    private void OnDestroy()
    {
        Notifications.ArenaProgressNotifications.Shutdown();
        Hud.ArenaHud.Detach();
        Combat.ArenaMagazinePatches.Shutdown();
        Combat.ArenaMedicalPatches.Shutdown();
        Effects.ArenaLightingDirector.Shutdown();
        Effects.ArenaTeamLaserColors.Reset();
        Audio.AnnouncerService.Shutdown();
        Audio.LabyrinthAlarmPatches.Shutdown();
        LabyrinthSpawnTraps.Shutdown();
        Spectator.ArenaSpectatorService.Shutdown();
    }
}
