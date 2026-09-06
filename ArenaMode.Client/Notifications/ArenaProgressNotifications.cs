using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Achievements;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using System;
using System.Reflection;

namespace ArenaMode.Notifications;

/// <summary>
/// Quiet quest/achievement/XP-toast presentation in the exact active Arena raid. Do not
/// intercept NotificationManager.AddNotification or quest/achievement updates:
/// their history, other subscribers and in-memory progression still run.
/// </summary>
internal static class ArenaProgressNotifications
{
    private static Harmony _harmony;
    private static bool _enabled;

    internal static void Enable()
    {
        if (_harmony != null) return;
        Harmony harmony = null;
        try
        {
            // Resolve all contracts before installing anything. The UI receive
            // gate prevents Arena entries being queued for later; ShowNotification
            // additionally catches direct calls and notifications queued earlier.
            var receive = Require(typeof(NotifierView), nameof(NotifierView.NotificationReceivedHandler),
                typeof(Notification));
            var show = Require(typeof(NotifierView), nameof(NotifierView.ShowNotification), typeof(Notification));
            var subtask = Require(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionChanged),
                typeof(Quest));
            var achievement = Require(typeof(AchievementsControllerClient),
                nameof(AchievementsControllerClient.TryNotifyConditionalStatusChanged), typeof(Achievement));
            harmony = new Harmony(Plugin.PluginGuid + ".progressnotifications");
            harmony.Patch(receive, prefix: Prefix(nameof(ReceivePrefix)));
            harmony.Patch(show, prefix: Prefix(nameof(ShowPrefix)));
            var direct = Prefix(nameof(DirectPresentationPrefix));
            // NotificationFilter replaces the raw subtask sound with a popup in
            // its bool prefix. Run first; the UI gates also block its replacement
            // even if another patch invokes that path independently or later.
            direct.before = new[] { "SilenceRaidSubtaskSoundPatch" };
            harmony.Patch(subtask, prefix: direct);
            harmony.Patch(achievement, prefix: Prefix(nameof(DirectPresentationPrefix)));
            _harmony = harmony;
            _enabled = true;
        }
        catch (Exception ex)
        {
            _enabled = false;
            try { harmony?.UnpatchSelf(); }
            catch (Exception cleanup) { Plugin.ModLogger.LogWarning("Arena progress notification cleanup failed: " + cleanup.Message); }
            Plugin.ModLogger.LogWarning("Arena progress notifications remain native: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        _enabled = false; // Any surviving callback is immediately fail-open.
        var harmony = _harmony;
        _harmony = null;
        try { harmony?.UnpatchSelf(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena progress notification cleanup failed: " + ex.Message); }
    }

    private static MethodInfo Require(Type type, string name, params Type[] arguments)
        => AccessTools.Method(type, name, arguments)
           ?? throw new MissingMethodException(type.FullName, name);

    private static HarmonyMethod Prefix(string name)
        => new(AccessTools.Method(typeof(ArenaProgressNotifications), name)) { priority = Priority.First };

    private static bool InArena()
        => ArenaSession.IsActive && _enabled
           && Singleton<AbstractGame>.Instantiated && Singleton<GameWorld>.Instantiated
           && Singleton<AbstractGame>.Instance is LocalGame game
           && ArenaSession.IsExactActiveRaid(game, Singleton<GameWorld>.Instance);

    private static bool ShouldSuppress(Notification notification)
        => InArena() && (notification is NotificationQuest || notification is NotificationAchievement
                        || notification is StatisticNotification);

    private static bool ReceivePrefix(Notification notification) => !ShouldSuppress(notification);

    private static bool ShowPrefix(Notification notification, ref bool __result)
    {
        if (!ShouldSuppress(notification)) return true;
        // Native queue processing releases its busy flag when ShowNotification
        // returns false. Do not strand unrelated messages behind a hidden entry.
        __result = false;
        return false;
    }

    // Both methods are presentation-only in native EFT: one plays the direct
    // QuestSubTrackComplete sound; the other can await an achievement sprite
    // before displaying it. Blocking the latter here prevents a notification
    // started in Arena from first appearing after the raid has already ended.
    private static bool DirectPresentationPrefix() => !InArena();
}
