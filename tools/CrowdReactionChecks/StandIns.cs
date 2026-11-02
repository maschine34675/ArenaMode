// Stand-ins for the game, BepInEx and the ArenaMode services the three linked production files
// (binder, policy, service) touch. Only what they read is modelled; the production files run
// unchanged. The MapPort contract surfaces are in Surfaces.cs.
using System.Runtime.CompilerServices;
using ArenaMode.Combat;

namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); }
    public sealed class PluginInfo { public object Instance; }
}

namespace ArenaMode
{
    public static class Plugin { public static readonly Log ModLogger = new(); }
    public sealed class Log
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Debugs = new();
        public void LogWarning(object value) => Warnings.Add(value.ToString());
        public void LogDebug(object value) => Debugs.Add(value.ToString());
        public void Clear() { Warnings.Clear(); Debugs.Clear(); }
    }
}

namespace UnityEngine
{
    public static class Time { public static float unscaledTime = 100f; }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; }
}

namespace EFT
{
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public sealed class LocalGame : AbstractGame { }
    public sealed class GameWorld { }
    public sealed class Player { public string ProfileId = "local"; }
}

namespace ArenaMode.Combat
{
    internal readonly struct ArenaCollateralProof { }
    public enum ArenaRole { Unknown }

    // Same identity semantics as production ArenaHitHistory.BodyId: one weak id per physical body.
    internal static class ArenaHitHistory
    {
        private sealed class Life { internal readonly long Id = ++_next; }
        private static readonly ConditionalWeakTable<object, Life> Lives = new();
        private static long _next;
        internal static long BodyId(object body) => body == null ? 0 : Lives.GetValue(body, _ => new Life()).Id;
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool CrowdReactionsSelection = true;
        internal static bool ExactRaid = true;
        internal static EFT.AbstractGame StartedGame;
        internal static EFT.GameWorld StartedWorld;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => ExactRaid && game != null && world != null
               && ReferenceEquals(game, StartedGame) && ReferenceEquals(world, StartedWorld);
    }
}

namespace ArenaMode.Events
{
    public static class ArenaEvents
    {
        public static event Action<EFT.Player> OnPlayerDied;
        internal static void RaisePlayerDied(EFT.Player player) => OnPlayerDied?.Invoke(player);
        internal static int DiedSubscribers => OnPlayerDied?.GetInvocationList().Length ?? 0;
    }
}

namespace ArenaMode.Audio
{
    internal static class AnnouncerService { internal static bool IsPmcVoiceBlocked; }

    internal static class ArenaAnnouncementDirector
    {
        internal static ArenaAnnouncementSnapshot Snapshot;
        internal static int Reads;
        internal static bool Throw;
        internal static void Reset() { Snapshot = default; Reads = 0; Throw = false; }
        internal static ArenaAnnouncementSnapshot ReadSnapshot(ArenaMode.Modes.IArenaMode mode)
        {
            Reads++;
            if (Throw) throw new InvalidOperationException("snapshot unavailable");
            return Snapshot;
        }
    }
}

namespace ArenaMode.Modes
{
    public interface IArenaMode
    {
        string Id { get; }
        bool IsComplete { get; }
    }

    public sealed class TeamDeathmatchMode : IArenaMode { public string Id => "tdm"; public bool IsComplete { get; set; } }
    public sealed class TacticalMode : IArenaMode { public string Id => "tactical"; public bool IsComplete { get; set; } }
    public sealed class GunGameMode : IArenaMode { public string Id => "gungame"; public bool IsComplete { get; set; } }
    public sealed class ZombieMode : IArenaMode { public string Id => "zombie"; public bool IsComplete { get; set; } }

    public sealed class RoundsMode : IArenaMode
    {
        public sealed class RoundState { public int Number; }
        public string Id => "deathmatch";
        public bool IsComplete { get; set; }
        public event Action<RoundState> OnRoundEnded;
        public event Action<int> OnRoundStarted;
        internal void Start(int round) => OnRoundStarted?.Invoke(round);
        internal void End(int round) => OnRoundEnded?.Invoke(new RoundState { Number = round });
        internal int Subscribers => (OnRoundEnded?.GetInvocationList().Length ?? 0)
            + (OnRoundStarted?.GetInvocationList().Length ?? 0);
    }

    // The three events the crowd binds, raised in production order by the checks (ArenaMatch.ReportKill:
    // OnCallout, then OnKill, then mode.OnKill, which may publish the result cue).
    public sealed class ArenaMatch
    {
        public ArenaMatch(IArenaMode mode) { Mode = mode; }
        public IArenaMode Mode { get; }
        public event Action<ArenaKill> OnKill;
        public event Action<string> OnCallout;
        internal event Action<ArenaAnnouncerCue> OnAnnouncerCue;
        internal void Kill(ArenaKill kill) => OnKill?.Invoke(kill);
        internal void Callout(string label) => OnCallout?.Invoke(label);
        internal void Cue(ArenaAnnouncerCue cue) => OnAnnouncerCue?.Invoke(cue);
        internal int Subscribers => (OnKill?.GetInvocationList().Length ?? 0)
            + (OnCallout?.GetInvocationList().Length ?? 0) + (OnAnnouncerCue?.GetInvocationList().Length ?? 0);
    }

    internal static class ArenaMatchService
    {
        internal static ArenaMatch Current;
        internal static bool TeamStartAbortPending;
    }
}
