using ArenaMode.Modes;

namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); }
    public sealed class PluginInfo { public object Instance; }
}

namespace ArenaMode
{
    public static class Plugin { public static readonly Log ModLogger = new(); }
    public sealed class Log { public readonly List<string> Warnings = new(); public void LogWarning(object value) => Warnings.Add(value.ToString()); }
}

namespace ArenaMode.Effects
{
    // Agreed palette contract. The palette leaf separately verifies its native
    // presentation gates; this harness owns faction placement, not shading.
    internal static class ArenaTeamPalette
    {
        internal const int UsecRgb = 0x7FA7FF, BearRgb = 0xFF6B6B;
    }
}

namespace EFT
{
    public enum EBotState { PreActive, Active, Disposed }
    public sealed class Health { public bool IsAlive = true; }
    public sealed class Ai { public BotOwner BotOwner; }
    public sealed class Player
    {
        private readonly Health _health = new();
        public bool ThrowHealth;
        public Health HealthController => ThrowHealth ? throw new InvalidOperationException("disposed health") : _health;
        public string ProfileId;
        public bool IsAI;
        public Ai AIData = new();
    }
    public sealed class BotOwner
    {
        public string ProfileId;
        public Player GetPlayer;
        public EBotState BotState = EBotState.Active;
    }
    public sealed class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Players = new();
        public Player GetAlivePlayerByProfileID(string id) => Players.TryGetValue(id, out var p) ? p : null;
    }
}

namespace ArenaMode.Modes
{
    public enum ArenaScoreboardSide { Friendly, Opponent }
    public sealed class Totals { public int Kills; }
    public sealed class Scoreboard
    {
        public int Version, FriendlyKills, OpponentKills, TotalReads;
        public bool ThrowRead;
        public Totals Totals(ArenaScoreboardSide side)
        {
            TotalReads++;
            if (ThrowRead) throw new InvalidOperationException("score unavailable");
            return new Totals { Kills = side == ArenaScoreboardSide.Friendly ? FriendlyKills : OpponentKills };
        }
    }
    public sealed class ArenaMatch { public TeamDeathmatchMode Mode; public Scoreboard Scoreboard = new(); }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    internal sealed class TeamDeathmatchPopulation { internal bool LocalUsec; internal int TeamSize = 5; }

    public sealed partial class TeamDeathmatchMode
    {
        private ArenaMatch _match;
        private EFT.GameWorld _world;
        private EFT.Player _local;
        private string _localId;
        private TeamDeathmatchPopulation _population;
        private TeamDeathmatchMatchPolicy _rules;
        private readonly List<EFT.BotOwner> _bots = new();
        private readonly Dictionary<string, bool> _usecById = new();
        private bool _ended, _nativeStopObserved;
        private string _failure;
        private float _gateCountdownEndsAt;
        internal bool CurrentRaid = true;
        internal bool ThrowRaidRead;
        internal float Now;
        public int TeamSize => _population.TeamSize;
        internal bool HasResult => _rules.Phase == TdmMatchPhase.Finished;
        internal float PlayingElapsed => _rules.Elapsed(Now);
        private bool IsCurrentRaid() => ThrowRaidRead ? throw new InvalidOperationException("raid unavailable") : CurrentRaid;
        private static bool Alive(EFT.BotOwner bot) => bot != null && bot.GetPlayer?.HealthController?.IsAlive == true;

        internal TeamDeathmatchMode(bool localUsec, int limit = 10, int duration = 600)
        {
            _localId = "human";
            _local = new EFT.Player { ProfileId = _localId };
            _world = new EFT.GameWorld { MainPlayer = _local };
            _population = new TeamDeathmatchPopulation { LocalUsec = localUsec };
            _rules = new TeamDeathmatchMatchPolicy(limit, duration);
            _match = new ArenaMatch { Mode = this };
            ArenaMatchService.Current = _match;
        }
        internal void Claim() => ClaimMonitors();
        internal void Push() => PushMonitors();
        internal void Release() => ReleaseMonitors();
        internal void FinishOrStop(string condition)
        {
            if (condition == "ended") _ended = true;
            else if (condition == "native") _nativeStopObserved = true;
            else if (condition == "fail") _failure = "failed";
            else if (condition == "world") CurrentRaid = false;
            else if (condition == "replacement") ArenaMatchService.Current = new ArenaMatch();
            else throw new ArgumentException(condition);
        }
        internal bool HasSession => _monitors != null;
        internal Scoreboard Board => _match.Scoreboard;
        internal EFT.Player Local => _local;
        internal EFT.GameWorld World => _world;
        internal TeamDeathmatchMatchPolicy Rules => _rules;
        internal void Start(float now)
        {
            Now = now;
            _rules.Update(now, true, 0, 0);
            _gateCountdownEndsAt = now + 3;
        }
        internal void Advance(float now, bool ready = true)
        { Now = now; _rules.Update(now, ready, Board.FriendlyKills, Board.OpponentKills); }
        internal EFT.BotOwner AddBot(bool usec, string id, EFT.EBotState state = EFT.EBotState.Active)
        {
            var player = new EFT.Player { ProfileId = id, IsAI = true };
            var bot = new EFT.BotOwner { GetPlayer = player, ProfileId = id, BotState = state };
            player.AIData.BotOwner = bot;
            _bots.Add(bot); _usecById[id] = usec; _world.Players[id] = player;
            return bot;
        }
    }
}

internal static class FakeHub
{
    internal static int Version = 1, Handle, NextHandle, Claims, Releases, SidesCalls, ScoreCalls, ClockCalls;
    internal static bool Available = true, RefuseClaim, ThrowGetter, ThrowClaim, ThrowRelease;
    internal static string FailSetter, ThrowSetter, Owner;
    internal static (int a, int b, int size) Sides;
    internal static (int a, int b, int aliveA, int aliveB) Score;
    internal static (int mode, float seconds) Clock;
    internal static readonly List<string> Owners = new();
    internal static void Reset()
    {
        Version = 1; Handle = 0; Claims = Releases = SidesCalls = ScoreCalls = ClockCalls = 0;
        Available = true; RefuseClaim = ThrowGetter = ThrowClaim = ThrowRelease = false;
        FailSetter = ThrowSetter = Owner = null; Sides = default; Score = default; Clock = default; Owners.Clear();
        ArenaMode.Plugin.ModLogger.Warnings.Clear();
        BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
        BepInEx.Bootstrap.Chainloader.PluginInfos["com.maschine.ArenaMapPort"] = new BepInEx.Bootstrap.PluginInfo { Instance = new ArenaMapPort.Client.PluginAnchor() };
    }
    internal static int Claim(string owner)
    {
        Claims++;
        if (ThrowClaim) throw new InvalidOperationException("claim exception");
        if (RefuseClaim || !Available || string.IsNullOrWhiteSpace(owner)) return 0;
        owner = owner.Length > 40 ? owner.Substring(0, 40) : owner;
        if (Handle != 0) return owner == Owner ? Handle : 0;
        Owners.Add(owner); Owner = owner; return Handle = ++NextHandle;
    }
    internal static bool Release(int handle)
    {
        Releases++;
        if (ThrowRelease) throw new InvalidOperationException("release exception");
        if (handle != Handle || handle == 0) return false;
        Handle = 0; Owner = null; return true;
    }
    internal static bool Setter(int handle, string kind)
    {
        if (kind == "sides") SidesCalls++;
        if (kind == "score") ScoreCalls++;
        if (kind == "clock") ClockCalls++;
        if (ThrowSetter == kind) throw new InvalidOperationException(kind + " exception");
        return handle > 0 && handle == Handle && FailSetter != kind;
    }
}

namespace ArenaMapPort.Client
{
    // Same assembly as this anchor lets the real plugin lookup find the fake
    // public contract without replacing the production reflection boundary.
    public sealed class PluginAnchor { }
    public static class ScoreMonitors
    {
        public static int ContractVersion => FakeHub.ThrowGetter ? throw new Exception("getter exception") : FakeHub.Version;
        public static bool IsAvailable => FakeHub.Available;
        public static bool IsClaimed => FakeHub.Handle > 0;
        public static int Count => 0;
        public static int Claim(string owner) => FakeHub.Claim(owner);
        public static bool Release(int handle) => FakeHub.Release(handle);
        public static bool SetSides(int handle, int a, int b, int size)
        { if (!FakeHub.Setter(handle, "sides")) return false; FakeHub.Sides = (a, b, size); return true; }
        public static bool SetScore(int handle, int a, int b, int aliveA, int aliveB)
        { if (!FakeHub.Setter(handle, "score")) return false; FakeHub.Score = (a, b, aliveA, aliveB); return true; }
        public static bool SetClock(int handle, int mode, float seconds)
        { if (!FakeHub.Setter(handle, "clock")) return false; FakeHub.Clock = (mode, seconds); return true; }
    }
}

public static class WrongSignature
{
    public static int ContractVersion => 1;
    public static bool IsAvailable => true;
    public static int Claim(string owner) => FakeHub.Claim(owner);
    public static bool Release(int handle) => FakeHub.Release(handle);
    public static bool SetSides(int handle, int a, int b, int size) => true;
    public static bool SetScore(int handle, int a, int b, int aliveA, int aliveB) => true;
    public static bool SetClock(int handle, int mode, double seconds) => true;
}

public static class ConvertibleClaimSignature
{
    public static int ContractVersion => 1;
    public static bool IsAvailable => true;
    public static int Claim(object owner) => FakeHub.Claim(owner.ToString());
    public static bool Release(int handle) => FakeHub.Release(handle);
    public static bool SetSides(int handle, int a, int b, int size) => true;
    public static bool SetScore(int handle, int a, int b, int aliveA, int aliveB) => true;
    public static bool SetClock(int handle, int mode, float seconds) => true;
}
