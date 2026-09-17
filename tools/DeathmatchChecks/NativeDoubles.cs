// These deterministic native I/O doubles exercise the actual RoundsMode adapter.
// They cannot certify Unity coroutines, native owners or delivered assemblies.
using System;

namespace UnityEngine
{
    public static class Time { public static float time, unscaledTime; }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static int FloorToInt(float x) => (int)Math.Floor(x);
    }
}
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
    public enum GameStatus { Started, Stopping, Stopped }
    public enum EBodyPart { Common }
    public class AbstractGame { }
    public sealed class LocalGame : AbstractGame { public GameStatus Status; public string ProfileId = "local"; public Player LocalPlayer; }
    public sealed class GameWorld { public Player MainPlayer = new(); }
    public class Player
    {
        public string ProfileId = "local";
        public Health HealthController = new();
        public Health ActiveHealthController = new();
        public int Recomputed;
        public void UpdateSpeedLimitByHealth() { Recomputed++; }
        public void UpdateArmsCondition() { Recomputed++; }
    }
    public sealed class Health
    {
        public bool IsAlive = true;
        public int Heals;
        public void RestoreFullHealth() { Heals++; }
        public void RemoveNegativeEffects(EBodyPart part) { }
        public void RemoveIntoxication() { }
        public void ChangeEnergy(float amount) { }
        public void ChangeHydration(float amount) { }
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public sealed class Logger
    {
        public void LogInfo(object value) { }
        public void LogWarning(object value) { }
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Exact = true;
        public static bool IsExactActiveRaid(EFT.LocalGame game, EFT.GameWorld world) => Exact;
    }
}
namespace ArenaMode.Combat
{
    public sealed class ArenaKill
    {
        public string KillerProfileId, KillerName, VictimProfileId;
        public bool KilledByLocal, LocalIsVictim, ScoreEligible = true;
    }
    // Only the cues RoundsMode dispatches; NoContest joined them on 2026-09-17.
    public enum ArenaAnnouncerCue { MatchWon, MatchLost, NoContest }
    public static class ArenaToasts { public static void Show(string value) { } }
    public static class ArenaProtection
    {
        public static bool IsProtectedNow;
        public static int Requests;
        public static bool Begin(float duration) { Requests++; return IsProtectedNow; }
    }
}
namespace ArenaMode.Respawn
{
    public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; }
    public static class LiveKitRespawn
    {
        public static bool Accepted;
        public static bool IsInitialSwapPending;
        public static bool ThrowOnRequest;
        public static bool? SynchronousResult;
        public static int Requests;
        public static Func<bool> Wanted;
        public static Action<bool> Complete;
        public static bool AtRoundBreak(EFT.LocalGame game, float duration, Func<bool> wanted, Action<bool> complete)
        {
            Requests++;
            if (ThrowOnRequest) throw new InvalidOperationException("injected native request failure");
            Wanted = wanted;
            Complete = complete;
            if (SynchronousResult.HasValue) complete(SynchronousResult.Value);
            return Accepted;
        }
    }
}
namespace ArenaMode.Modes
{
    public sealed class ArenaMatch
    {
        public int Score;
        public int AwardResets;
        public readonly Board Scoreboard = new();
        public int Cues;
        public void BeginAwardRound(bool clearRevenge) { AwardResets++; }
        public void ReportAnnouncerCue(Combat.ArenaAnnouncerCue cue) { Cues++; }
    }
    public sealed class Board { public Row Local = new(); }
    public sealed class Row { public string DisplayName = "You"; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    internal static class TdmMatchExit
    {
        internal static bool PipelineRunning;
        internal static int Calls;
        internal static EFT.LocalGame LastGame;
        internal static EFT.GameWorld LastWorld;
        internal static string LastProfile;
        internal static void TryStop(EFT.LocalGame game, EFT.GameWorld world, string profileId, Action<string> report)
        { Calls++; LastGame = game; LastWorld = world; LastProfile = profileId; }
    }
}
