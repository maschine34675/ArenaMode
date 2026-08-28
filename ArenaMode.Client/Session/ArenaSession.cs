using ArenaMode.Config;
using EFT;
using EFT.Communications;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Client-side arena session state. Armed-for-next-raid is set by the
/// LocalRaidStartedPatch after the server accepted the raid-start announcement;
/// IsActive is a per-raid latch flipped when the armed raid actually starts.
/// Every arena patch gates on IsActive as its first check, so normal raids are
/// bit-identical with the mod installed.
/// </summary>
public static class ArenaSession
{
    public static bool ArmedForNextRaid { get; private set; }
    public static string ArmedMap { get; private set; }

    public static bool IsActive { get; private set; }

    public static void Initialize()
    {
        RaidLifecycle.EnsurePatched();
        RaidLifecycle.OnRaidStarted += OnRaidStarted;
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
    }

    public static void MarkArmed(string map)
    {
        ArmedForNextRaid = true;
        ArmedMap = map;
        Plugin.ModLogger.LogInfo($"Arena armed for next raid on {map}.");
    }

    /// <summary>Drops a stale armed flag and tells the server to discard its snapshot.</summary>
    public static void ClearArmed(string reason)
    {
        if (!ArmedForNextRaid)
            return;

        ArmedForNextRaid = false;
        Plugin.ModLogger.LogWarning($"Arena arming cleared ({reason}).");
        ArenaServerClient.PostRaidCancel();
    }

    private static void OnRaidStarted(GameWorld world)
    {
        if (!ArmedForNextRaid)
            return;

        var location = world?.LocationId;
        ArmedForNextRaid = false;

        if (FikaCompatCheck.IsFikaLoaded)
        {
            ArenaServerClient.PostRaidCancel();
            return;
        }

        if (!string.Equals(location, ArmedMap, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.ModLogger.LogWarning(
                $"Arena was armed for {ArmedMap} but raid started on {location} — arena stays inactive.");
            ArenaServerClient.PostRaidCancel();
            return;
        }

        IsActive = true;
        Respawn.ArenaRespawnDriver.ResetSession();
        Plugin.ModLogger.LogInfo($"Arena session active on {location}.");
        try
        {
            NotificationManager.DisplayMessageNotification(
                "Arena mode active — nothing gained, nothing lost.",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.cyan);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena toast failed: {ex.Message}");
        }

        // Bot groups created during the deploy countdown predate the IsActive latch —
        // their one-time hostility decision missed the IsPlayerEnemy patch. Force the
        // human into every existing group's enemy list once.
        ForceExistingGroupsHostile(world);

        // Pre-warm the spawn system (respawns need it) and swap the real gear for
        // the selected kit via an in-place respawn.
        if (Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame game)
        {
            Redeploy.Core.Spawning.SpawnSystemCache.Warm(game);
            Respawn.RaidStartKitSwap.Run(game);
        }
    }

    private static void ForceExistingGroupsHostile(GameWorld world)
    {
        try
        {
            var mainPlayer = world?.MainPlayer;
            var spawner = (Comfort.Common.Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            if (mainPlayer == null || spawner?._bots?.BotOwners == null)
                return;

            var forced = 0;
            foreach (var bot in spawner._bots.BotOwners)
            {
                var group = bot?.BotsGroup;
                if (group == null || group.IsEnemy(mainPlayer))
                    continue;

                if (group.AddEnemy(mainPlayer, EBotEnemyCause.initial))
                    forced++;
            }

            if (forced > 0)
                Plugin.ModLogger.LogInfo($"Arena: forced {forced} pre-start bot group(s) hostile.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena: pre-start hostility sweep failed: {ex.Message}");
        }
    }

    private static void OnRaidEnded()
    {
        if (!IsActive && !ArmedForNextRaid)
            return;

        IsActive = false;
        ArmedForNextRaid = false;

        // Best-effort confirmation; the authoritative snapshot restore already ran
        // server-side inside the post-raid skip patch.
        ArenaServerClient.PostRaidEnd();
        Plugin.ModLogger.LogInfo("Arena session ended.");
    }
}
