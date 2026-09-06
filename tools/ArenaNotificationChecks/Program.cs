using ArenaMode.Notifications;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Achievements;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

internal static class Program
{
    private static int _checks, _filterCalls;
    private static float _filterVolume;
    private static void Check(bool result, string text)
    {
        if (!result) throw new InvalidOperationException(text);
        _checks++;
    }
    private static NotificationManager Reset(bool active = true)
    {
        ArenaProgressNotifications.Shutdown();
        Hooks.Patches.Clear();
        Harmony.PatchCalls = Harmony.FailPatchNumber = 0;
        Harmony.FailUnpatch = false;
        AccessTools.Missing = null;
        ArenaSession.IsActive = active;
        ArenaSession.Game = Singleton<AbstractGame>.Instance = new LocalGame();
        ArenaSession.World = Singleton<GameWorld>.Instance = new GameWorld();
        var manager = Singleton<NotificationManager>.Instance = new NotificationManager();
        GUISounds.DirectQuestSounds = GUISounds.ArenaAnnouncerSounds = GUISounds.WarningSounds = 0;
        GUISounds.GlobalVolume = 0.25f;
        _filterCalls = 0;
        return manager;
    }
    private static void InstallFilter(float volume)
    {
        _filterVolume = volume;
        new Harmony("SilenceRaidSubtaskSoundPatch").Patch(
            AccessTools.Method(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionChanged)),
            new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(FilterPrefix))));
    }
    private static bool FilterPrefix(Quest quest)
    {
        _filterCalls++;
        if (_filterVolume >= 1) return true;
        NotificationManager.DisplayNotification(new NotificationQuest { Text = "NotificationFilter replacement" });
        return false;
    }
    private static void Drain(NotifierView view)
    {
        while (view.Queue.Count > 0) { view.CloseCurrent(); view.ProcessQueuedNotifications(); }
        view.CloseCurrent();
    }
    private static void TypedAndDirectPaths()
    {
        foreach (bool immediate in new[] { false, true })
        foreach (bool achievement in new[] { false, true })
        {
            var manager = Reset(); ArenaProgressNotifications.Enable();
            Notification note = achievement ? new NotificationAchievement() : new NotificationQuest();
            note.ShowImmediately = immediate;
            NotificationManager.DisplayNotification(note);
            Check(manager.History.Count == 1 && manager.OtherSubscribers == 1, "notification history and non-UI observers still run");
            Check(manager.View.Queue.Count == 0 && manager.View.Visible.Count == 0 && manager.View.PopupSounds == 0,
                "Arena quest/achievement notifications never enter the UI queue");
            Check(!manager.View.ShowNotification(note) && manager.View.Visible.Count == 0 && manager.View.PopupSounds == 0,
                "direct native ShowNotification cannot recreate audible progress popup");
            manager.View.Queue.Enqueue(note); // queued before Arena became active
            manager.View.ProcessQueuedNotifications();
            Check(!manager.View.Busy && manager.View.PopupSounds == 0, "hidden queued item releases native processing flag");
            var warning = new MalfunctionOccurredNotification();
            manager.AddNotification(warning); Drain(manager.View);
            Check(manager.View.Visible.SequenceEqual(new[] { warning }) && manager.View.PopupSounds == 1,
                "unrelated warnings remain audible after a dropped queued message");
        }
    }
    private static void SelectiveAndLifecycle()
    {
        var manager = Reset(); ArenaProgressNotifications.Enable();
        Notification[] allowed = [new CustomNotification { Text = "FIRST BLOOD Quest completed" },
            new MalfunctionOccurredNotification(), new ProfileChangeEventNotification(),
            new SkillLevelUpNotification(), new Notification()];
        foreach (var note in allowed) NotificationManager.DisplayNotification(note);
        Drain(manager.View);
        GUISounds.ArenaAnnouncerSounds++; GUISounds.WarningSounds++;
        Check(manager.View.Visible.SequenceEqual(allowed) && manager.View.PopupSounds == allowed.Length,
            "unrelated custom/skills/warning notifications remain unchanged");
        Check(manager.View.ProfileChangeChecks == 1 && manager.OtherSubscribers == allowed.Length,
            "profile-change handler and other subscribers untouched");
        Check(GUISounds.ArenaAnnouncerSounds == 1 && GUISounds.WarningSounds == 1 && GUISounds.GlobalVolume == 0.25f,
            "announcer/unrelated sounds and global volume untouched");

        Action[] leave = [() => ArenaSession.IsActive = false, () => Singleton<AbstractGame>.Instance = new LocalGame(),
            () => Singleton<GameWorld>.Instance = new GameWorld(), () => ArenaSession.Game = new LocalGame(),
            () => ArenaSession.World = new GameWorld(), () => Singleton<AbstractGame>.Instance = null,
            () => Singleton<GameWorld>.Instance = null, () => ArenaProgressNotifications.Shutdown(),
            () => ArenaSession.Game = Singleton<AbstractGame>.Instance = new AbstractGame()];
        foreach (var invalidate in leave)
        {
            manager = Reset(); ArenaProgressNotifications.Enable(); invalidate();
            NotificationManager.DisplayNotification(new NotificationQuest());
            NotificationManager.DisplayNotification(new NotificationAchievement());
            NotificationManager.DisplayNotification(new StatisticNotification()); Drain(manager.View);
            var quest = new Quest(); new QuestControllerClient().CompleteCondition(quest); Drain(manager.View);
            Check(manager.View.PopupSounds == 4 && GUISounds.DirectQuestSounds == 1 && quest.Progress == 1,
                "outside exact Arena including postraid, stale worlds and shutdown is native");
        }
        manager = Reset(false); ArenaProgressNotifications.Enable();
        var ordinary = new Quest(); new QuestControllerClient().CompleteCondition(ordinary); Drain(manager.View);
        Check(ordinary.Progress == 1 && manager.View.PopupSounds == 1 && GUISounds.DirectQuestSounds == 1,
            "ordinary raid condition progress and both native sounds preserved");
    }
    private static void ExperiencePresentation()
    {
        foreach (bool immediate in new[] { false, true })
        {
            var manager = Reset(); ArenaProgressNotifications.Enable();
            var xp = new StatisticNotification { ShowImmediately = immediate };
            NotificationManager.DisplayNotification(xp);
            Check(manager.History.SequenceEqual(new[] { xp }) && manager.OtherSubscribers == 1,
                "XP history and non-UI subscribers remain intact");
            Check(manager.View.Queue.Count == 0 && manager.View.Visible.Count == 0 && manager.View.PopupSounds == 0,
                "Arena XP gain is silent before UI queue admission");
            Check(!manager.View.ShowNotification(xp), "direct XP display is suppressed inside Arena");
            manager.View.Queue.Enqueue(xp);
            manager.View.ProcessQueuedNotifications();
            Check(!manager.View.Busy, "suppressed queued XP releases the UI processing flag");
            ArenaSession.IsActive = false;
            Drain(manager.View);
            Check(manager.View.Visible.Count == 0, "Arena XP does not replay after raid end");
            NotificationManager.DisplayNotification(new StatisticNotification()); Drain(manager.View);
            Check(manager.View.PopupSounds == 1, "postraid XP presentation is native");
        }
    }
    private static void DirectSoundAndProgress()
    {
        {
            var manager = Reset(); ArenaProgressNotifications.Enable();
            var quest = new Quest(); new QuestControllerClient().CompleteCondition(quest); Drain(manager.View);
            Check(quest.Progress == 1 && GUISounds.DirectQuestSounds == 0 && manager.View.PopupSounds == 0,
                "direct subtask and NotificationFilter replacement sounds suppressed");
        }
        foreach (bool filterFirst in new[] { false, true })
        foreach (float volume in new[] { 0f, 0.3f, 1f })
        {
            var manager = Reset();
            if (filterFirst) InstallFilter(volume);
            ArenaProgressNotifications.Enable();
            if (!filterFirst) InstallFilter(volume);
            var quest = new Quest(); var controller = new QuestControllerClient();
            controller.CompleteCondition(quest); controller.CompleteQuest(quest); Drain(manager.View);
            Check(quest.Progress == 2, "quest conditions/completion still advance");
            Check(GUISounds.DirectQuestSounds == 0 && manager.View.PopupSounds == 0 && manager.View.Visible.Count == 0,
                "direct subtask and NotificationFilter replacement sounds suppressed");
            Check(_filterCalls == 0, "Arena direct prefix runs before coinstalled NotificationFilter");
            // A replacement emitted later/directly is still blocked at the UI
            // boundary, regardless of whether it honored prefix ordering.
            FilterPrefix(quest); Drain(manager.View);
            Check(manager.View.PopupSounds == 0, "independent NotificationFilter replacement cannot recreate sound");
            ArenaSession.IsActive = false;
            controller.CompleteCondition(quest); Drain(manager.View);
            Check(quest.Progress == 3 && manager.View.PopupSounds == 1
                && GUISounds.DirectQuestSounds == (volume >= 1 ? 1 : 0), "NotificationFilter normal/postraid behavior retained");
        }
        {
            var manager = Reset(); ArenaProgressNotifications.Enable();
            var controller = new AchievementsControllerClient();
            foreach (bool deferred in new[] { false, true })
            {
                var achievement = new Achievement { NeedsSprite = deferred };
                controller.Complete(achievement);
                Check(achievement.IsDone, "achievement completion still advances");
            }
            Check(controller.SpriteLoads == 0 && controller.DeferredNotifications.Count == 0,
                "Arena achievement does not schedule a delayed postraid popup");
            ArenaSession.IsActive = false;
            while (controller.DeferredNotifications.TryDequeue(out var send)) send();
            Drain(manager.View);
            Check(manager.View.PopupSounds == 0, "Arena-born achievement never reappears after raid end");
            controller.Complete(new Achievement { NeedsSprite = true });
            Check(controller.SpriteLoads == 1 && controller.DeferredNotifications.Count == 1, "postraid achievement sprite loading is native");
            controller.DeferredNotifications.Dequeue()(); Drain(manager.View);
            Check(manager.View.PopupSounds == 1, "postraid achievement popup and sound restored");
        }
        {
            var manager = Reset(false); var controller = new AchievementsControllerClient();
            controller.Complete(new Achievement { NeedsSprite = true });
            ArenaSession.IsActive = true; ArenaProgressNotifications.Enable();
            controller.DeferredNotifications.Dequeue()();
            Check(manager.History.Count == 1 && manager.View.Queue.Count == 0 && manager.View.PopupSounds == 0,
                "preexisting deferred achievement is also quiet when delivered inside Arena");
        }
    }
    private static void InstallationFailureAndReentry()
    {
        var manager = Reset(); InstallFilter(1); ArenaProgressNotifications.Enable();
        int count = Hooks.Patches.Count; ArenaProgressNotifications.Enable();
        Check(Hooks.Patches.Count == count && count == 5, "Enable is idempotent and adds only four presentation hooks");
        Check(Hooks.Patches.Single(p => p.Target.DeclaringType == typeof(QuestControllerClient) && p.Owner.StartsWith("test.arena"))
            .Prefix.before.Contains("SilenceRaidSubtaskSoundPatch"), "explicit NotificationFilter order owner matches installed ModulePatch identity");
        ArenaProgressNotifications.Shutdown(); ArenaProgressNotifications.Shutdown();
        Check(Hooks.Patches.Count == 1, "Shutdown removes only own hooks");
        ArenaProgressNotifications.Enable(); Check(Hooks.Patches.Count == 5, "re-enable installs exactly one copy");
        for (int failure = 1; failure <= 4; failure++)
        {
            manager = Reset(); Harmony.FailPatchNumber = failure; ArenaProgressNotifications.Enable();
            Check(Hooks.Patches.Count == 0, "partial installation rolls back own hooks");
            NotificationManager.DisplayNotification(new NotificationQuest()); Drain(manager.View);
            Check(manager.View.PopupSounds == 1, "failed installation remains native");
        }
        manager = Reset(); AccessTools.Missing = nameof(NotifierView.ShowNotification); ArenaProgressNotifications.Enable();
        Check(Hooks.Patches.Count == 0 && Harmony.PatchCalls == 0, "all native methods resolved before any patch is installed");
        AccessTools.Missing = null; ArenaProgressNotifications.Enable();
        NotificationManager.DisplayNotification(new NotificationQuest()); Drain(manager.View);
        Check(manager.View.PopupSounds == 0, "failed resolution can later recover cleanly");
        Harmony.FailUnpatch = true;
        ArenaProgressNotifications.Shutdown();
        Check(Hooks.Patches.Count == 4, "modeled failed cleanup actually left callbacks installed");
        NotificationManager.DisplayNotification(new NotificationQuest()); Drain(manager.View);
        Check(manager.View.PopupSounds == 1, "shutdown fail-open flag protects normal behavior even if unpatch throws");
        Harmony.FailUnpatch = false;
    }
    private static void Main()
    {
        TypedAndDirectPaths(); SelectiveAndLifecycle(); ExperiencePresentation(); DirectSoundAndProgress(); InstallationFailureAndReentry();
        Console.WriteLine($"PASS: {_checks} production notification filter/presentation model checks.");
        Console.WriteLine("ARENA_NOTIFICATION_CHECKS_PASSED");
    }
}
