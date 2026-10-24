// Only native boundary and mode callbacks are substituted. Match, scoreboard,
// kill DTO, scoring rules and awards are compiled from full production sources.
namespace UnityEngine { public static class Time { public static float time; } }
namespace EFT { public class Player { public string ProfileId; } }
namespace ArenaMode
{
    internal static class Plugin { internal static readonly Logger ModLogger = new(); }
    internal class Logger { internal void LogWarning(object message) { } }
}
namespace ArenaMode.Combat
{
    internal static class RoleClassifier
    {
        internal static string DisplayName(EFT.Player player) => "You";
        internal static string Label(EFT.Player player) => "PMC";
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaMatchResult
    {
        internal static bool HasResult(IArenaMode mode) => mode.IsComplete;
    }
    public class TestMode : IArenaMode
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public bool IsComplete { get; set; }
        public string HudStatusLine => null;
        public float DeathReplaySeconds => 0;
        public bool HoldRespawn => false;
        public bool ControlsOwnSpawns => false;
        public int KillsSeen, DeathsSeen, RespawnsSeen;
        public Action<ArenaMode.Combat.ArenaKill> KillCallback;
        public void OnMatchStart(ArenaMatch match) { }
        public void Tick(float deltaTime) { }
        public void OnKill(ArenaMode.Combat.ArenaKill kill) { KillsSeen++; KillCallback?.Invoke(kill); }
        public void OnLocalDeath() { DeathsSeen++; }
        public void OnLocalRespawn(EFT.Player player) { RespawnsSeen++; }
        public void OnMatchEnd() { }
    }
    public class TacticalMode : TestMode { public bool IsTeamArena { get; set; } }
    public class ZombieMode : TestMode
    {
        public bool IsTeamArena => false;
        public bool TryRecordLocalDeath() => !IsComplete;
        public bool TryPublishLocalDeathKill() => true;
    }
    public class TeamDeathmatchMode : TestMode
    {
        public bool AdmitScores = true;
        public bool AdmitDeaths = true;
        public bool FriendlyKill;
        public int Classifications;
        public bool TryBeginScoredEvent() => AdmitScores;
        public bool TryRecordLocalDeath() { bool admit = AdmitDeaths; AdmitDeaths = false; return admit; }
        public void ClassifyKill(ArenaMode.Combat.ArenaKill kill)
        {
            Classifications++;
            if (!FriendlyKill) return;
            kill.ScoreEligible = kill.AwardEligible = false;
            kill.KillerIsFriendly = kill.VictimIsFriendly = true;
        }
    }
    public class RoundsMode : TestMode
    {
        public bool AdmitScores = true;
        public bool AdmitDeaths = true;
        public bool TryBeginScoredEvent() => AdmitScores;
        public bool TryRecordLocalDeath() { bool admit = AdmitDeaths; AdmitDeaths = false; return admit; }
    }
}

// Reward transport is a separate tested subsystem. These gameplay fixtures
// exercise the real match/mode code without HTTP, file writes or enrollment.
namespace ArenaMode.Session
{
    internal static class ArenaRewardCapture
    {
        internal static void Kill(object kill) { }
        internal static void LocalForfeit(object player) { }
        internal static void Invalidate(string reason) { }
        internal static void FlushFinal() { }
    }
}
