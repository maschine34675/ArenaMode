// Minimal native/Harmony behavioral model. Production Enable/Shutdown/prefixes
// are linked directly. This cannot prove live Harmony/Unity or audio behavior.
using System.Reflection;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    public class AbstractGame { }
    public class LocalGame : AbstractGame { }
    public class GameWorld { }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static EFT.AbstractGame Game;
        internal static EFT.GameWorld World;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && game != null && world != null && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode
{
    internal static class Plugin
    {
        internal const string PluginGuid = "test.arena";
        internal static Logger ModLogger = new();
    }
    internal sealed class Logger
    {
        internal readonly List<string> Lines = new();
        internal void LogWarning(string text) => Lines.Add(text);
    }
}
namespace EFT.Communications
{
    public class Notification { public bool ShowImmediately, ShowNotification = true; public string Text; }
    public class NotificationQuest : Notification { }
    public class NotificationAchievement : Notification { }
    public class CustomNotification : Notification { }
    public class ProfileChangeEventNotification : Notification { }
    public class MalfunctionOccurredNotification : Notification { }
    public class StatisticNotification : Notification { }
    public class SkillLevelUpNotification : Notification { }
    public class NotificationManager
    {
        public readonly List<Notification> History = new();
        public int OtherSubscribers;
        public NotifierView View = new();
        public void AddNotification(Notification notification)
        {
            // Production intentionally does not patch history or other receivers.
            History.Add(notification);
            OtherSubscribers++;
            View.NotificationReceivedHandler(notification);
        }
        public static void DisplayNotification(Notification notification)
            => Comfort.Common.Singleton<NotificationManager>.Instance?.AddNotification(notification);
    }
}
namespace EFT.UI
{
    public class NotifierView
    {
        public readonly Queue<Notification> Queue = new();
        public readonly List<Notification> Visible = new();
        public int PopupSounds, ProfileChangeChecks;
        public bool Busy;
        public void NotificationReceivedHandler(Notification notification)
        {
            if (!Hooks.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), notification, out _)) return;
            if (notification is ProfileChangeEventNotification) ProfileChangeChecks++;
            if (!notification.ShowNotification) return;
            if (notification.ShowImmediately) ShowNotification(notification);
            else Queue.Enqueue(notification);
        }
        public bool ShowNotification(Notification notification)
        {
            if (!Hooks.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), notification, out object result)) return (bool)result;
            Visible.Add(notification);
            PopupSounds++;
            return true;
        }
        public void ProcessQueuedNotifications()
        {
            if (Busy || Queue.Count == 0) return;
            var notification = Queue.Dequeue();
            Busy = true;
            if (!ShowNotification(notification)) Busy = false;
        }
        public void CloseCurrent() => Busy = false;
    }
    public static class GUISounds
    {
        public static int DirectQuestSounds, ArenaAnnouncerSounds, WarningSounds;
        public static float GlobalVolume = 0.25f;
    }
}
namespace EFT.Quests
{
    public class Quest { public int Progress; public bool Notify = true; }
    public class QuestControllerClient
    {
        public void CompleteCondition(Quest quest)
        {
            quest.Progress++;
            TryNotifyConditionChanged(quest);
        }
        public void TryNotifyConditionChanged(Quest quest)
        {
            if (!Hooks.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), quest, out _)) return;
            if (!quest.Notify) return;
            GUISounds.DirectQuestSounds++;
            NotificationManager.DisplayNotification(new NotificationQuest());
        }
        public void CompleteQuest(Quest quest)
        {
            quest.Progress++;
            NotificationManager.DisplayNotification(new NotificationQuest { ShowImmediately = true });
        }
    }
}
namespace EFT.Achievements
{
    public class Achievement { public bool IsDone; public bool NeedsSprite; }
    public class AchievementsControllerClient
    {
        public readonly Queue<Action> DeferredNotifications = new();
        public int SpriteLoads;
        public void Complete(Achievement achievement)
        {
            achievement.IsDone = true;
            TryNotifyConditionalStatusChanged(achievement);
        }
        public void TryNotifyConditionalStatusChanged(Achievement achievement)
        {
            if (!Hooks.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), achievement, out _)) return;
            if (!achievement.IsDone) return;
            if (achievement.NeedsSprite)
            {
                SpriteLoads++;
                DeferredNotifications.Enqueue(() => NotificationManager.DisplayNotification(new NotificationAchievement()));
            }
            else NotificationManager.DisplayNotification(new NotificationAchievement());
        }
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800, Normal = 400, Last = 0; }
    public sealed class HarmonyMethod
    {
        public MethodInfo method;
        public int priority = Priority.Normal;
        public string[] before = [];
        public HarmonyMethod(MethodInfo method) => this.method = method;
    }
    public static class AccessTools
    {
        internal static string Missing;
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => name == Missing ? null : parameters == null
                ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
                    null, parameters, null);
    }
    public sealed class Harmony
    {
        private readonly string _id;
        public static int FailPatchNumber, PatchCalls;
        public static bool FailUnpatch;
        public Harmony(string id) => _id = id;
        public void Patch(MethodBase original, HarmonyMethod prefix)
        {
            if (++PatchCalls == FailPatchNumber) throw new InvalidOperationException("modeled patch failure");
            Hooks.Patches.Add(new((MethodInfo)original, prefix, _id));
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("modeled unpatch failure");
            Hooks.Patches.RemoveAll(p => p.Owner == _id);
        }
    }
}
internal static class Hooks
{
    internal sealed record Patch(MethodInfo Target, HarmonyLib.HarmonyMethod Prefix, string Owner);
    internal static readonly List<Patch> Patches = new();
    internal static bool Invoke(MethodInfo method, object argument, out object result)
    {
        result = method.ReturnType == typeof(bool) ? false : null;
        bool run = true;
        var selected = Patches.Where(p => p.Target == method).OrderByDescending(p => p.Prefix.priority).ToList();
        foreach (var patch in selected)
        {
            // Harmony skips later side-effecting bool/ref prefixes once one
            // rejects the original. Simple void prefixes still run.
            var parameters = patch.Prefix.method.GetParameters();
            bool sideEffects = patch.Prefix.method.ReturnType != typeof(void) || parameters.Any(p => p.ParameterType.IsByRef);
            if (!run && sideEffects) continue;
            object previousResult = result;
            var args = parameters.Select(p => p.Name == "__result" ? previousResult : argument).ToArray();
            object allowed = patch.Prefix.method.Invoke(null, args);
            int resultAt = Array.FindIndex(parameters, p => p.Name == "__result");
            if (resultAt >= 0) result = args[resultAt];
            if (allowed is bool value && !value) run = false;
        }
        return run;
    }
}
