using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool accepted, string description)
    {
        if (!accepted) throw new InvalidOperationException(description);
        _checks++;
    }

    private sealed class Raid : IDisposable
    {
        internal readonly LocalGame Game = new();
        internal readonly GameWorld World;
        internal readonly TeamDeathmatchMode Mode = new();
        internal readonly ArenaMatch Match;
        internal List<BotOwner> Bots => Game.BotsController.BotSpawner._bots.BotOwners;
        internal Raid(string map, bool usec = true, string mode = "teamdeathmatch", bool active = true,
            bool pft = false, float seconds = 4f, Action<Raid> beforeStart = null)
        {
            ArenaMode.Areas.ArenaAreaRuntime.Reset();
            ArenaTeamBoxRuntime.Reset();
            TdmMatchExit.Reset();
            World = new GameWorld { LocationId = map };
            Singleton<AbstractGame>.Instance = Game;
            Singleton<IBotGame>.Instance = Game;
            Singleton<GameWorld>.Instance = World;
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = 0f;
            ArenaSession.IsActive = active;
            ArenaSession.ActiveMode = mode;
            ArenaSession.OwnedGame = Game;
            ArenaSession.OwnedWorld = World;
            ArenaSession.Participants = 4;
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = pft;
            ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
            ArenaMode.Respawn.LiveKitRespawn.IsInitialSwapPending = false;
            Settings.TeamDeathmatchSize.Value = 2;
            Settings.TeamDeathmatchKillLimit.Value = Settings.TeamDeathmatchTimeLimitSeconds.Value = 0;
            Settings.SpawnProtectionSeconds.Value = seconds;
            ArenaAdministrativeDeathGate.Reset();
            TeamDeathmatchRelations.Reset();
            ArenaTracerTeams.Reset();
            BurstSpawnService.Requests.Clear();
            BurstSpawnService.OnRequest = request =>
            {
                if (!request.Submit()) throw new InvalidOperationException("native spawn dispatch must be current");
                var side = request.Role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear;
                var body = Model.Body("scope-bot-" + ++Model.NextId, side, true, request.World, request.Game);
                body.AIData.BotOwner.BotState = EBotState.Active;
                request.Delivered = true;
                request.Observer(body.AIData.BotOwner);
                request.DispatchFinished(true);
            };
            Model.Body("local", usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            Match = new ArenaMatch { Mode = Mode, Scoreboard = new ArenaScoreboard("local", "You", "PMC") };
            ArenaMatchService.Current = Match;
            beforeStart?.Invoke(this);
            Mode.OnMatchStart(Match);
        }
        internal void Step(float at)
        {
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = at;
            Mode.Tick(1f);
        }
        internal void Fill()
        {
            for (int i = 0; !Mode.InitialRosterReady && !Mode.IsComplete && i < 12; i++) Step(Time.time + 1f);
            Check(Mode.InitialRosterReady && Bots.Count == 3, "native-model roster must reach live before damage checks");
        }
        public void Dispose() => Mode.OnMatchEnd();
    }

    private static void Main()
    {
        TeamDeathmatchRelations.Enable();
        foreach (string map in new[] { "arena_equator", "arena_bay5", "arena_airpit", "ARENA_AIRPIT", "arena_bowl", "ARENA_BOWL" })
        foreach (bool usec in new[] { true, false })
        {
            using var raid = new Raid(map, usec);
            raid.Fill();
            Check(raid.Bots.All(bot => raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "ported team-box bots must receive shields");
            Check(!raid.Mode.IsBotSpawnProtected(raid.World.MainPlayer), "bot admission never grants a local shield");
            Check(!raid.Mode.ShouldBlockMatchDamage(raid.World.MainPlayer), "live player damage policy remains separate");
            float live = Time.time;
            Settings.SpawnProtectionSeconds.Value = 30;
            raid.Step(live + 4f);
            Check(raid.Bots.All(bot => !raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "frozen original duration expires after a later settings edit");
            var old = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
            old.HealthController.IsAlive = false;
            raid.Step(live + 5f);
            var replacement = raid.Bots.Last(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
            Check(!ReferenceEquals(old, replacement) && raid.Mode.IsBotSpawnProtected(replacement.GetPlayer), "fresh team-box replacement gets its own shield");
            raid.Step(live + 9f);
            Check(!raid.Mode.IsBotSpawnProtected(replacement.GetPlayer), "replacement keeps the original match duration");
        }

        // Do not derive the expected allow-list from the production catalog.
        // A widened/mistyped catalog must not silently widen this oracle too.
        foreach (string map in new[] { "factory4_day", "factory4_night", "bigmap", "laboratory", "labyrinth",
                     "woods", "interchange", "shoreline", "rezervbase", "lighthouse", "tarkovstreets", "sandbox",
                     "sandbox_high", "arena_unknown", "arena_equator_copy", "equator", " arena_bay5", "", null })
        {
            Check(!ArenaTeamBoxCatalog.Applies(map, "teamdeathmatch"), "non-box map must not enter the catalog");
            using var raid = new Raid(map);
            raid.Fill();
            Settings.SpawnProtectionSeconds.Value = 30;
            Check(raid.Bots.All(bot => !raid.Mode.ShouldBlockMatchDamage(bot.GetPlayer)), "non-box live bot must not receive spawn immunity");
            var old = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
            old.HealthController.IsAlive = false;
            raid.Step(Time.time + 1f);
            Check(raid.Bots.All(bot => !raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "non-box replacements stay unprotected after settings edits");
        }

        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie", "endless", "rounds", "", null })
        foreach (string map in new[] { "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl" })
        {
            Check(!ArenaTeamBoxCatalog.Applies(map, mode), "other modes must not enter the box catalog");
            using var raid = new Raid(map, mode: mode);
            Check(raid.Mode.IsComplete && !raid.Mode.IsBotSpawnProtected(raid.World.MainPlayer), "other modes cannot start independent TDM shield authority");
        }
        foreach (float seconds in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            using var raid = new Raid("arena_equator", seconds: seconds);
            raid.Fill();
            Check(raid.Bots.All(bot => !raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "invalid or disabled duration cannot protect a box bot");
        }
        using (var raid = new Raid("arena_bay5", seconds: 999))
        {
            raid.Fill(); float live = Time.time;
            Check(raid.Bots.All(bot => raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "oversized duration begins on first combat-ready observation");
            raid.Step(live + 29.99f);
            Check(raid.Bots.All(bot => raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "oversized duration positive control");
            raid.Step(live + 30f);
            Check(raid.Bots.All(bot => !raid.Mode.IsBotSpawnProtected(bot.GetPlayer)), "duration safety cap remains 30 seconds");
        }
        foreach (Action<Raid> invalidation in new Action<Raid>[] {
            r => ArenaSession.IsActive = false,
            r => ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = true,
            r => ArenaSession.OwnedWorld = new GameWorld(),
            r => ArenaSession.OwnedGame = new LocalGame(),
            r => r.Game.Status = GameStatus.Stopping })
        {
            using var raid = new Raid("arena_airpit", beforeStart: invalidation);
            Check(raid.Mode.IsComplete, "unarmed, PFT or stale raid cannot initialize shield authority");
            Check(!raid.Mode.IsBotSpawnProtected(raid.World.MainPlayer), "rejected match does not protect a body");
        }
        foreach (Action<Raid> invalidation in new Action<Raid>[] {
            r => ArenaMatchService.Current = new ArenaMatch(),
            r => r.Match.Mode = new TeamDeathmatchMode(),
            r => ArenaSession.OwnedWorld = new GameWorld(),
            r => Singleton<GameWorld>.Instance = new GameWorld(),
            r => r.Game.Status = GameStatus.Stopping,
            r => ArenaSession.IsActive = false,
            r => ArenaSession.ActiveMode = "deathmatch" })
        {
            using var raid = new Raid("arena_equator");
            raid.Fill();
            var body = raid.Bots[0].GetPlayer;
            Check(raid.Mode.IsBotSpawnProtected(body), "current owner positive control");
            invalidation(raid);
            Check(!raid.Mode.IsBotSpawnProtected(body), "lost current match or raid ownership revokes shield authority");
        }
        Console.WriteLine($"PASS: {_checks} production-linked spawn-shield scope checks.");
        Console.WriteLine("ARENA_SPAWN_SHIELD_SCOPE_CHECKS_PASSED");
    }
}
