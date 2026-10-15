using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }

    private sealed class Fixture : IDisposable
    {
        internal readonly LocalGame Game = new();
        internal readonly GameWorld World = new();
        internal readonly ArenaMatch Match = new();
        internal readonly ZombieMode Mode;
        internal Fixture(int size = 1, bool ownGear = true, bool team = false, float watchdog = 0f)
        {
            Time.time = 0; Time.timeScale = 1;
            Singleton<AbstractGame>.Instance = Game;
            Singleton<GameWorld>.Instance = World;
            var bots = new BotGame(); Singleton<IBotGame>.Instance = bots;
            Game.LocalPlayer = new LocalPlayer { IsYourPlayer = true, Profile = new() { Id = "local", AccountId = "human" } };
            World.MainPlayer = Game.LocalPlayer; World.RegisteredPlayers.Add(Game.LocalPlayer); World.Ever["local"] = Game.LocalPlayer;
            ArenaSession.Active = true; ArenaSession.ReplaySeconds = 0; ArenaSession.ZombieOwnGearActive = ownGear;
            ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = false;
            PitFireTeamCompat.TeamArenaActive = team; PitFireTeamCompat.PersistenceSuppressionActive = true;
            PitFireTeamCompat.RoundTransitionActive = false; PitFireTeamCompat.Followers = new();
            PitFireTeamRoundLifecycle.Teardowns = 0; PitFireTeamRoundLifecycle.Status = new();
            for (int i = 0; team && i < 4; i++)
            {
                var player = new Player { IsAI = true, Profile = new() { Id = $"friend-{i}", AccountId = $"account-{i}" } };
                var bot = new BotOwner { GetPlayer = player }; player.AIData = new() { BotOwner = bot };
                World.RegisteredPlayers.Add(player); World.Ever[player.ProfileId] = player;
                bots.BotsController.Bots.BotOwners.Add(bot); PitFireTeamCompat.Followers.Add(bot);
            }
            Settings.ZombieMaxAlive.Value = 2; Settings.ZombieWaveStartSize.Value = size; Settings.ZombieWaveGrowth.Value = 0;
            Settings.ZombieSpawnIntervalSeconds.Value = .2f; Settings.ZombieIntermissionSeconds.Value = 2;
            Settings.ZombieStuckWaveTimeoutSeconds.Value = watchdog;
            ZombieLoadoutRefill.Calls = 0; ArenaMatchService.Current = Match;
            LiveKitRespawn.Reset();
            Mode = new(); Match.Mode = Mode; Mode.OnMatchStart(Match); ArenaMode.Events.ArenaEvents.Apply();
            // These scenarios begin after the independently tested opening
            // countdown; retain their original wave-relative timeline.
            Mode.Tick(0);
        }
        internal void Tick(float time) { Time.time = time + 2.1f; Mode.Tick(.2f); }
        public void Dispose() => Mode.OnMatchEnd();
    }

    public static void Main()
    {
        foreach (bool ownGear in new[] { false, true })
        using (var f = new Fixture(ownGear: ownGear))
        {
            f.Tick(.2f);
            Check(ZombieSpawnService.Dispatched == 1 && ZombieSpawnService.Pending == 1, "opening request is owned");
            foreach (float time in new[] { 1f, 5f, 25f, 55f })
            {
                f.Tick(time);
                Check(ZombieLoadoutRefill.Calls == 0 && ZombieSpawnService.Dispatched == 1, "solo final pending must not refill or advance");
                Check(f.Mode.HudStatusLine.Contains("WAVE 1 | 1 LEFT"), "pending final body remains visible in current wave");
            }
            ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = ZombieSpawnService.ConfirmedArrivals = 1;
            f.Tick(56);
            Check(ZombieLoadoutRefill.Calls == 0, "arrival itself is not clearance");
            ZombieSpawnService.Alive = 0; f.Tick(57);
            Check(ZombieLoadoutRefill.Calls == 1 && f.Mode.HudStatusLine.Contains("WAVE 2 IN"), "real final death refills once");
            f.Tick(57.5f); Check(ZombieLoadoutRefill.Calls == 1, "intermission refill not repeated");
            f.Tick(59.1f); Check(ZombieSpawnService.Dispatched == 2 && f.Mode.HudStatusLine.Contains("WAVE 2"), "next wave begins after actual clearance interval");
        }

        foreach (bool team in new[] { false, true })
        using (var f = new Fixture(size: 2, team: team, watchdog: 1))
        {
            f.Tick(.2f); ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = ZombieSpawnService.ConfirmedArrivals = 1;
            f.Tick(.5f); f.Tick(.8f); f.Tick(2);
            Check(ZombieSpawnService.Retired == 1, "watchdog retires the known alive infected");
            Check(ZombieLoadoutRefill.Calls == 0 && PitFireTeamRoundLifecycle.Teardowns == 0,
                "watchdog cannot refill or tear down team while final body is pending");
            Check(ZombieSpawnService.Pending == 1 && ZombieSpawnService.Dispatched == 2, "watchdog preserves pending debt without a replacement request");
            f.Tick(30);
            Check(ZombieSpawnService.Retired == 1 && ZombieSpawnService.Dispatched == 2, "empty pending tail does not repeatedly retire or advance");
            ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.ConfirmedArrivals = 2;
            f.Tick(31); ZombieSpawnService.Alive = 0; f.Tick(31.5f);
            Check(team ? f.Mode.HudStatusLine.Contains("RESTORING") : ZombieLoadoutRefill.Calls == 1,
                "settled final death enters the correct solo or protected team transition");
        }

        using (var f = new Fixture())
        {
            f.Tick(.2f); ZombieSpawnService.Pending = 0; ZombieSpawnService.FailedDispatches = 1;
            f.Tick(1);
            Check(ZombieSpawnService.Dispatched == 2 && ZombieSpawnService.Pending == 1 && ZombieLoadoutRefill.Calls == 0,
                "known asynchronous failure retries its owed wave slot before clearance");
            f.Tick(2); Check(ZombieSpawnService.Dispatched == 2, "failed-slot acknowledgement is consumed once");
        }
        using (var f = new Fixture())
        {
            ZombieSpawnService.RejectRequest = true;
            for (int i = 1; i < 10; i++) f.Tick(i);
            Check(ZombieLoadoutRefill.Calls == 0 && ZombieSpawnService.Dispatched == 0, "synchronous rejections do not silently clear a wave");
            ZombieSpawnService.RejectRequest = false; f.Tick(10);
            Check(ZombieSpawnService.Dispatched == 1, "immediate rejection leaves slot available for recovery");
        }
        foreach (bool team in new[] { false, true })
        using (var f = new Fixture(team: team))
        {
            f.Tick(.2f); ZombieSpawnService.PendingFailure = "native activation unconfirmed"; f.Tick(1);
            Check(f.Mode.IsComplete && f.Mode.HoldRespawn && f.Mode.HudStatusLine.Contains("NO CONTEST"),
                "uncertain activation enters held no-contest for solo and PFT");
            f.Tick(2);
            Check(ZombieSpawnService.Dispatched == 1 && ZombieLoadoutRefill.Calls == 0 && PitFireTeamRoundLifecycle.Teardowns == 0,
                "terminal uncertainty never retries into a new wave or rebuilds the team");
        }
        using (var f = new Fixture())
        {
            f.Tick(.2f); f.Game.LocalPlayer.HealthController.IsAlive = false; f.Tick(1);
            Check(f.Mode.IsComplete && f.Mode.HoldRespawn && ZombieSpawnService.Dispatched == 1, "local death dominates pending-wave work");
        }
        using (var f = new Fixture())
        {
            ArenaSession.Active = false; f.Tick(1);
            Check(ZombieSpawnService.Dispatched == 0 && ZombieLoadoutRefill.Calls == 0, "ordinary or ended raid cannot enter wave work");
        }
        CheckCapAndZeroPause();
        Console.WriteLine($"ZOMBIE_WAVE_PENDING_CHECKS_PASSED {_checks} assertions");
    }

    private static void CheckCapAndZeroPause()
    {
        var config = new ZombieWavePolicy.Config { WaveStartSize = 5, MaxAlive = 2, SpawnIntervalSeconds = .1f, IntermissionSeconds = 0 };
        var state = ZombieWavePolicy.Initial();
        int pending = 0;
        for (int i = 0; i < 10; i++) pending += ZombieWavePolicy.Evaluate(ref state, config, i, 0, pending).SpawnNow;
        Check(pending == 2 && state.SpawnedThisWave == 2, "pending reservations fill alive cap without exceeding it");
        config.WaveStartSize = 1; state = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref state, config, 0, 0, 0);
        var waiting = ZombieWavePolicy.Evaluate(ref state, config, 100, 0, 1);
        Check(waiting.WaveNumber == 1 && waiting.SpawnNow == 0, "zero intermission cannot leapfrog a pending final body");
        ZombieWavePolicy.Evaluate(ref state, config, 101, 1, 0, 1);
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, 102, 0, 0, 1);
        Check(cleared.WaveNumber == 2 && cleared.SpawnNow == 1, "zero intermission still advances immediately after actual clearance");
    }
}
