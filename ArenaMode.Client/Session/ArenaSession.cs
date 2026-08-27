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

    private static void OnRaidStarted(GameWorld world)
    {
        if (!ArmedForNextRaid)
            return;

        var location = world?.LocationId;
        ArmedForNextRaid = false;

        if (FikaCompatCheck.IsFikaLoaded)
            return;

        if (!string.Equals(location, ArmedMap, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.ModLogger.LogWarning(
                $"Arena was armed for {ArmedMap} but raid started on {location} — arena stays inactive.");
            return;
        }

        IsActive = true;
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
