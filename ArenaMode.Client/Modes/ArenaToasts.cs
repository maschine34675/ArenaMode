using EFT.Communications;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>Milestone/notice toasts via the game's notification drawer (proven call).</summary>
internal static class ArenaToasts
{
    public static void Show(string text)
    {
        try
        {
            NotificationManager.DisplayMessageNotification(
                text,
                ENotificationDurationType.Default,
                ENotificationIconType.Default,
                Color.cyan);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena toast failed: {ex.Message}");
        }
    }
}
