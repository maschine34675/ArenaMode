// The policy, selection, hold coroutine, and reflection reader are production
// sources. Doubles provide only scene/lifetime, time, logging, and plugin APIs.
namespace UnityEngine
{
    internal static class Time { internal static float time; }
}

namespace EFT
{
    internal sealed class HealthController { internal bool IsAlive; }
    internal sealed class LocalPlayer { internal HealthController HealthController = new(); }
    internal sealed class LocalGame { internal LocalPlayer LocalPlayer; internal bool Started = true; }
}

namespace ArenaMode
{
    internal static class Plugin { internal static readonly TestLogger ModLogger = new(); }
    internal sealed class TestLogger
    {
        internal readonly List<string> Entries = new();
        internal void LogInfo(string text) => Entries.Add(text); internal void LogDebug(string text) => Entries.Add(text);
    }
}

namespace ArenaMode.Modes
{
    internal sealed class TestMode { internal bool HoldRespawn; }
    internal sealed class ArenaMatch { internal TestMode Mode = new(); }
    internal static class ArenaMatchResult
    {
        internal static bool OwnsRaid(TestMode mode) => false;
        internal static void ReportProblem(TestMode mode, string problem) { }
        internal static void RequestExit(TestMode mode) { }
    }
    internal static class ArenaMatchService
    {
        internal static ArenaMatch Current;
        internal static readonly List<string> Aborts = new();
        internal static void ScheduleFailedStartAbort(string problem) => Aborts.Add(problem);
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static float ReplaySeconds => ArenaReplaySelection.Seconds;
    }
}

namespace ArenaMode.Respawn
{
    internal static class ArenaRespawnDriver
    {
        internal static bool StandDown;
        internal static int Epoch;
        internal static bool IsSameActiveRaid(EFT.LocalGame game, int epoch)
            => Session.ArenaSession.IsActive && game?.Started == true && epoch == Epoch;
    }
}

namespace ArenaMode.Spectator
{
    // Visual effects have their own exact native-context checks in
    // SpectatorHealthChecks. This boundary neither grants nor releases ownership.
    internal static class SpectatorHealthEffects
    {
        internal static void ClearForDeathReplay(EFT.LocalGame game, EFT.LocalPlayer body) { }
    }
}

namespace BepInEx.Bootstrap
{
    internal sealed class TestPluginInfo { internal object Instance; }
    internal sealed class TestRegistry
    {
        internal readonly Dictionary<string, TestPluginInfo> Entries = new();
        internal int Reads;
        internal bool TryGetValue(string key, out TestPluginInfo info)
        {
            ++Reads;
            return Entries.TryGetValue(key, out info);
        }
    }
    internal static class Chainloader { internal static readonly TestRegistry PluginInfos = new(); }
}

// These exact names and public signatures are the external reflection boundary,
// not a replacement for KillcamCompat's state selection algorithm.
namespace Killcam
{
    public sealed class Plugin
    {
        public static Replay.ReplayController ReplayCtrl;
        public static bool DeathReplayActive { get; set; }
    }
    namespace Replay
    {
        public sealed class ReplayController
        {
            public static object PendingReplayKiller;
            public static float Delay;
            public static bool ThrowDelay;
            public static int DelayReads;
            public static int StateReads;
            public bool Playing;
            public bool Ready = true;
            public bool ThrowState;
            public bool IsPlaying
            {
                get { ++StateReads; if (ThrowState) throw new InvalidOperationException("fixture read failure"); return Playing; }
            }
            public bool ReadyForStop
            {
                get { ++StateReads; if (ThrowState) throw new InvalidOperationException("fixture read failure"); return Ready; }
            }
            public static float GetExtraDelay()
            {
                ++DelayReads;
                if (ThrowDelay) throw new InvalidOperationException("fixture delay failure");
                return Delay;
            }
        }
    }
}
