// Native identity and omitted mode internals only. The linked production files
// perform the actual snapshot conversion, winner-counter queries and dispatch.
namespace UnityEngine { internal static class Time { internal static float time; } }
namespace Comfort.Common { internal static class Singleton<T> { internal static T Instance; } }
namespace EFT
{
    internal class AbstractGame { }
    internal enum GameStatus { Started, Stopped }
    internal class LocalGame : AbstractGame { internal GameStatus Status; }
    internal class GameWorld { internal Player MainPlayer; }
    internal class Player { internal Health HealthController = new(); }
    internal class Health { internal bool IsAlive = true; }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static EFT.LocalGame Game;
        internal static EFT.GameWorld World;
        internal static bool Active = true;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => Active && game != null && world != null && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Respawn
{
    internal static class LiveKitRespawn { internal static bool IsInitialSwapPending; }
    internal static class ArenaRespawnDriver { internal static bool IsRespawnInProgress; }
}
namespace ArenaMode.Combat { internal static class WeaponSwapService { internal static bool IsSwapping; } }
namespace ArenaMode
{
    internal static class Plugin { internal static Logger ModLogger = new(); }
    internal class Logger { internal List<string> Lines = new(); internal void LogWarning(string line) => Lines.Add(line); }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
    internal class ArenaMatch
    {
        internal IArenaMode Mode;
        internal ArenaScoreboard Scoreboard = new();
        internal readonly List<ArenaAnnouncerCue> Cues = new();
        internal void ReportAnnouncerCue(ArenaAnnouncerCue cue, bool beginAwardRound = true)
        { if (beginAwardRound) throw new Exception("Director may not reset awards"); Cues.Add(cue); }
    }
    internal enum ArenaScoreboardSide { Friendly, Opponent }
    internal struct ArenaScoreboardTotals { internal int Kills; internal int Score; }
    internal class ArenaScoreboard
    {
        internal ArenaScoreboardTotals Friendly, Opponent;
        internal ArenaScoreboardTotals Totals(ArenaScoreboardSide side) => side == ArenaScoreboardSide.Friendly ? Friendly : Opponent;
    }
    public sealed partial class RoundsMode : IArenaMode
    {
        internal bool _ended, Valid = true;
        internal DeathmatchRules _rules;
        internal DeathmatchOptions _options;
        private bool IsCurrentRaid() => Valid;
        private bool WaitingForStart => _rules.Phase == DeathmatchPhase.Waiting;
        private bool InIntermission => _rules.Phase == DeathmatchPhase.Intermission;
        private bool HasResult => _rules.Phase == DeathmatchPhase.Finished;
    }
    public sealed partial class TeamDeathmatchMode : IArenaMode
    {
        internal bool _ended, Valid = true;
        internal TeamDeathmatchMatchPolicy _rules;
        internal ArenaMatch _match;
        private bool IsCurrentRaid() => Valid;
        private bool HasResult => _rules.Phase == TdmMatchPhase.Finished;
        private float PlayingElapsed => _rules.Elapsed(UnityEngine.Time.time);
    }
    public sealed partial class TacticalMode : IArenaMode
    {
        internal enum Phase { Buy, Staging, Live, Aftermath, Ended }
        internal ArenaMatch _match;
        internal Phase _phase;
        internal bool _teamArena, _soloCombatReady;
        internal float _phaseEndsAt;
        internal int _teamRoundsToWin, RoundNumber, RoundsWon, RoundsLost;
    }
    public sealed partial class GunGameMode : IArenaMode
    {
        internal ArenaMatch _match;
        internal bool _givenUp, _preloading, _loadoutDirty;
        internal List<object> _ladder = new();
        internal int _tier, _wins;
    }
}
