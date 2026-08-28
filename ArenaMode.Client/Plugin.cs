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

        ArenaSession.Initialize();
        LocalRaidStartedPatch.EnablePatch();

        DeathInterception.EnsurePatched();
        DeathInterception.Register(new ArenaRespawnDriver());
        Bots.ArenaBotPatches.Enable();
        Movement.ArenaMovementPatches.Enable();
        Movement.SprintAdsPatches.Enable();
        Movement.FastDeployPatches.Enable();
        Loot.OpenLootTracker.Enable();
        Loot.ArenaLootPatches.Enable();
        Loot.AutoSearchService.Initialize();
        PerfectCullingFix.EnableOnce();

        gameObject.AddComponent<ArenaController>();

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Redeploy.Core {RedeployCore.Version})");
    }
}
