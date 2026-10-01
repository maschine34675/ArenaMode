using System.Reflection;
using System.Runtime.CompilerServices;
using ArenaMode.Combat;
using ArenaMode.Modes;

#if !REAL_HARMONY_PROBE
namespace HarmonyLib
{
    // The normal behavior harness needs reflection lookup, not a detour. Real
    // HarmonyX is loaded only by the separately reported binding probe.
    internal static class AccessTools
    {
        internal static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters)
            => type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
}
#endif

// Only native/HUD boundaries are doubled. Capture, notice DTO, administrative
// leases and the exact extracted ShowUncreditedDeath method are production code.
namespace EFT
{
    public interface IPlayer { string ProfileId { get; } }
    public sealed class Health { public bool IsAlive; }
    public class Player : IPlayer
    {
        protected IPlayer LastAggressor;
        public Health HealthController = new();
        public string ProfileId { get; set; } = "bot-profile";
        public bool IsYourPlayer;
        public string DisplayName = "Victim";
        public string RoleLabel = "BEAR";
        public bool ThrowDuringClassification;
        public Action DeathSubscriber;
        public int NativeDeathCalls;
        public void SetAggressor(IPlayer aggressor) => LastAggressor = aggressor;
        public IPlayer ReadAggressorForDriver() => LastAggressor;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void OnDead(EDamageType damageType)
        {
            NativeDeathCalls++;
            DeathSubscriber?.Invoke();
        }
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute : Attribute { }
}
namespace ArenaMode.Session { internal static class ArenaSession { internal static bool IsActive; } }
namespace ArenaMode.Modes
{
    internal sealed class ModeBoundary { internal bool IsComplete; }
    internal sealed class ArenaMatch { internal ModeBoundary Mode = new(); }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
}
namespace ArenaMode.Combat
{
    internal static class RoleClassifier
    {
        internal static string DisplayName(EFT.Player p) => p.ThrowDuringClassification
            ? throw new InvalidOperationException("classification boundary failed") : p.DisplayName;
        internal static string Label(EFT.Player p) => p.RoleLabel;
    }
}
namespace ArenaMode.Config
{
    internal sealed class BoolSetting { internal bool Value = true; }
    internal static class Settings
    {
        internal static readonly BoolSetting ShowKillfeed = new();
        internal static readonly BoolSetting KillfeedOtherKills = new();
    }
}
namespace ArenaMode
{
    internal static class Plugin { internal static readonly LogBoundary ModLogger = new(); }
    internal sealed class LogBoundary
    {
        internal readonly List<string> Warnings = new();
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Hud
{
    internal sealed class FeedBoundary
    {
        internal readonly List<ArenaUncreditedDeath> Rows = new();
        internal bool ThrowOnPush;
        internal void PushUncreditedDeath(ArenaUncreditedDeath death)
        {
            if (ThrowOnPush) throw new InvalidOperationException("HUD boundary failed");
            Rows.Add(death);
        }
    }
    internal static partial class ArenaHud
    {
        private static ArenaMatch _match;
        private static FeedBoundary _killfeed;
        internal static void SetBoundary(ArenaMatch match, FeedBoundary feed) { _match = match; _killfeed = feed; }
    }
}
