#if REPLAY_EFFECTS_SCOPE_CHECKS
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;

internal static class ReplayEffectsScopeChecks
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool answer, string label)
        { ++assertions; if (!answer) throw new InvalidOperationException("Assertion failed: " + label); }

        var cases = new (string Label, Action<LocalGame, GameWorld, Player> Change)[]
        {
            ("ordinary raid", (_, _, _) => ArenaSession.IsActive = false),
            ("disabled replay", (_, _, _) => ArenaReplaySelection.Seconds = 0f),
            ("stopped raid", (g, _, _) => g.Status = GameStatus.Stopped),
            ("alive local player", (_, _, p) => ((ReplayHealth)p.HealthController).IsAlive = true),
            ("missing health", (_, _, p) => p.HealthController = null),
            ("nonlocal body", (_, _, p) => p.IsYourPlayer = false),
            ("Unity-destroyed corpse", (_, _, p) => p.Destroyed = true),
            ("replaced game body", (g, _, _) => g.LocalPlayer = new Player()),
            ("replaced world body", (_, w, _) => w.MainPlayer = new Player()),
            ("missing world", (_, _, _) => Singleton<GameWorld>.Instance = null),
            ("stale native game", (_, _, _) => ArenaSession.Game = new LocalGame()),
            ("stale native world", (_, _, _) => ArenaSession.World = new GameWorld()),
            ("idle camera", (_, _, _) => KillcamReplayCompat.State = OptionalCameraOwnerState.Idle),
            ("absent camera", (_, _, _) => KillcamReplayCompat.State = OptionalCameraOwnerState.Absent),
            ("unreadable camera", (_, _, _) => KillcamReplayCompat.State = OptionalCameraOwnerState.Incompatible),
        };
        foreach (var item in cases)
        {
            var body = new Player { IsYourPlayer = true, HealthController = new ReplayHealth() };
            var game = new LocalGame { LocalPlayer = body };
            var world = new GameWorld { MainPlayer = body };
            Singleton<GameWorld>.Instance = world;
            ArenaSession.IsActive = true;
            ArenaSession.Game = game; ArenaSession.World = world;
            ArenaReplaySelection.Seconds = 3f;
            KillcamReplayCompat.State = OptionalCameraOwnerState.Busy;
            Check(SpectatorHealthEffects.HasExactReplayContext(game, body), "exact replay context permits visual cleanup");
            item.Change(game, world, body);
            Check(!SpectatorHealthEffects.HasExactReplayContext(game, body), "replay visuals reject " + item.Label);
        }
        Check(!SpectatorHealthEffects.HasExactReplayContext(null, null), "null replay context rejects cleanup");
        return assertions;
    }

    private sealed class ReplayHealth : IHealthController
    {
        public bool IsAlive { get; set; }
        public ValueStruct GetBodyPartHealth(EBodyPart part, bool rounded = false)
            => throw new InvalidOperationException("Visual context must not read or mutate actual health values.");
    }
}

namespace EFT
{
    public enum GameStatus { Started, Stopped }
    public sealed class LocalGame { public GameStatus Status = GameStatus.Started; public Player LocalPlayer; }
    public sealed class GameWorld { public Player MainPlayer; }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static LocalGame Game;
        internal static GameWorld World;
        internal static bool IsExactActiveRaid(LocalGame game, GameWorld world)
            => ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
    internal static class ArenaReplaySelection { internal static float Seconds; }
}

namespace ArenaMode.Spectator
{
    internal enum OptionalCameraOwnerState { Absent, Idle, Busy, Incompatible }
    internal static class KillcamReplayCompat
    {
        internal static OptionalCameraOwnerState State;
        internal static OptionalCameraOwnerState ReadState(out float seconds, out string problem)
        { seconds = 0f; problem = null; return State; }
    }
}
#endif
