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
        private int _followerEpoch;
        // Replaces the four companions the way a real rebuild does (the fixture's lifecycle double
        // calls it from TryStartFollowerSpawn), so a protected squad can cross several wave breaks.
        private void AddFollowers(BotGame bots)
        {
            _followerEpoch++;
            PitFireTeamCompat.Followers = new();
            for (int i = 0; i < 4; i++)
            {
                var player = new Player { IsAI = true, Profile = new() { Id = $"friend-{_followerEpoch}-{i}", AccountId = $"account-{i}" } };
                var bot = new BotOwner { GetPlayer = player }; player.AIData = new() { BotOwner = bot };
                World.RegisteredPlayers.Add(player); World.Ever[player.ProfileId] = player;
                bots.BotsController.Bots.BotOwners.Add(bot); PitFireTeamCompat.Followers.Add(bot);
            }
        }
        internal Fixture(int size = 1, bool ownGear = true, bool team = false, float watchdog = 0f, bool snapshot = false, int maxAlive = 2, int shooter = 37, float intermission = 2)
        {
            Time.time = 0; Time.timeScale = 1;
            Singleton<AbstractGame>.Instance = Game;
            Singleton<GameWorld>.Instance = World;
            var bots = new BotGame(); Singleton<IBotGame>.Instance = bots;
            Game.LocalPlayer = new LocalPlayer { IsYourPlayer = true, Profile = new() { Id = "local", AccountId = "human" } };
            World.MainPlayer = Game.LocalPlayer; World.RegisteredPlayers.Add(Game.LocalPlayer); World.Ever["local"] = Game.LocalPlayer;
            ArenaSession.Active = true; ArenaSession.ReplaySeconds = 0; ArenaSession.OwnGearActive = ownGear;
            ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = false;
            PitFireTeamCompat.TeamArenaActive = team; PitFireTeamCompat.PersistenceSuppressionActive = true;
            PitFireTeamCompat.RoundTransitionActive = false; PitFireTeamCompat.Followers = new();
            PitFireTeamRoundLifecycle.Teardowns = 0; PitFireTeamRoundLifecycle.Status = new();
            PitFireTeamRoundLifecycle.Spawn = team ? () => AddFollowers(bots) : null;
            if (team) AddFollowers(bots);
            Settings.ZombieMaxAlive.Value = maxAlive; Settings.ZombieWaveStartSize.Value = size; Settings.ZombieWaveGrowth.Value = 0;
            Settings.ZombieSpawnIntervalSeconds.Value = .2f; Settings.ZombieIntermissionSeconds.Value = intermission;
            Settings.ZombieStuckWaveTimeoutSeconds.Value = watchdog;
            Settings.ZombieShooterPercent.Value = shooter;
            ZombieRoleMix.Script = null; ZombieRoleMix.Draws = 0;
            ArenaMode.Plugin.ModLogger.Lines.Clear(); ArenaMode.Plugin.ModLogger.InfoLines.Clear(); ArenaMode.Plugin.ModLogger.DebugLines.Clear();
            ZombieNativeEventBridge.HasSnapshot = snapshot;
            ZombieNativeEventBridge.SnapshotSize = size; ZombieNativeEventBridge.SnapshotShooter = 23;
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
            Check(ZombieRoleMix.LastWave == 1 && ZombieRoleMix.LastShooter == 37,
                "opening draw receives frozen shooter setting and actual first wave");
            Settings.ZombieShooterPercent.Value = 99;
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
            Check(ZombieRoleMix.LastWave == 2, "draw follows actual second wave instead of opening composition");
            Check(ZombieRoleMix.LastShooter == 37, "later draw retains frozen shooter percentage after live config edit");
        }

        // The step is frozen at match start: the alive limit and every draw use it, not the live value.
        foreach (var (step, expectedDispatches) in new[] { (ArenaMode.Shared.ArenaDifficultyStep.Reduced, 0), (ArenaMode.Shared.ArenaDifficultyStep.Maximum, 1) })
        {
            ArenaDifficulty.Step = step;
            using var f = new Fixture(size: 30, maxAlive: 40);
            ArenaDifficulty.Step = step == ArenaMode.Shared.ArenaDifficultyStep.Reduced ? ArenaMode.Shared.ArenaDifficultyStep.Maximum : ArenaMode.Shared.ArenaDifficultyStep.Reduced;
            ZombieSpawnService.Alive = 24;
            f.Tick(.2f);
            Check(ZombieSpawnService.Dispatched == expectedDispatches, "the alive limit follows the frozen step (Reduced caps 40 at 24, Maximum keeps it)");
            ZombieSpawnService.Alive = 0; ZombieSpawnService.Pending = 0; ZombieRoleMix.Draws = 0;
            f.Tick(.5f);
            Check(ZombieRoleMix.Draws > 0 && ZombieRoleMix.LastStep == step, "draws use the step frozen at match start, not the live value");
        }
        ArenaDifficulty.Step = ArenaMode.Shared.ArenaDifficultyStep.Maximum;

        using (var f = new Fixture(snapshot: true))
        {
            Settings.ZombieShooterPercent.Value = 100;
            f.Tick(.2f);
            Check(ZombieRoleMix.LastShooter == 23, "runtime draw uses native opening snapshot despite subsequent settings edit");
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
            // A failed dispatch holds the next attempt for ZombieWavePolicy.FailedDispatchBackoffSeconds (2 s,
            // Chop Shop finding of 2026-11-12): the failure is acknowledged at 1, the owed slot is retried at 3.
            f.Tick(.2f); ZombieSpawnService.Pending = 0; ZombieSpawnService.FailedDispatches = 1;
            f.Tick(1); f.Tick(2.9f);
            Check(ZombieSpawnService.Dispatched == 1, "a failed dispatch is not retried on the pacing cadence");
            f.Tick(3);
            Check(ZombieSpawnService.Dispatched == 2 && ZombieSpawnService.Pending == 1 && ZombieLoadoutRefill.Calls == 0,
                "known asynchronous failure retries its owed wave slot before clearance");
            f.Tick(4); Check(ZombieSpawnService.Dispatched == 2, "failed-slot acknowledgement is consumed once");
        }
        using (var f = new Fixture())
        {
            ZombieSpawnService.RejectRequest = true;
            for (int i = 1; i < 10; i++) f.Tick(i);
            Check(ZombieLoadoutRefill.Calls == 0 && ZombieSpawnService.Dispatched == 0, "synchronous rejections do not silently clear a wave");
            // The rejections at 1, 3, 5, 7 and 9 each held the next attempt for the backoff: the slot recovers at 11.
            ZombieSpawnService.RejectRequest = false; f.Tick(10);
            Check(ZombieSpawnService.Dispatched == 0, "a rejected slot waits for its backoff");
            f.Tick(11);
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
        using (var f = new Fixture(watchdog:100))
        {
            f.Tick(.2f); ZombieSpawnService.Pending=0; ZombieSpawnService.Alive=ZombieSpawnService.ConfirmedArrivals=1;
            f.Tick(1); ZombieRecoveryRuntime.Recoveries=1; f.Tick(1.5f);
            Check(ZombieSpawnService.Dispatched==2 && ZombieSpawnService.Pending==1 && ZombieLoadoutRefill.Calls==0
                && f.Mode.HudStatusLine.Contains("WAVE 1"), "confirmed individual retirement returns its slot before wave clearance");
            f.Tick(2); Check(ZombieSpawnService.Dispatched==2,"mode consumes individual recovery once");
        }
        using (var f = new Fixture())
        {
            ArenaSession.Active = false; f.Tick(1);
            Check(ZombieSpawnService.Dispatched == 0 && ZombieLoadoutRefill.Calls == 0, "ordinary or ended raid cannot enter wave work");
        }
        CheckCapAndZeroPause();
        CheckSpecialWaves();
        CheckCompositionTally();
        Console.WriteLine($"ZOMBIE_WAVE_PENDING_CHECKS_PASSED {_checks} assertions");
    }

    // One-infected waves: clears the running wave (arrival, then death), lets the intermission tick once
    // more and starts the next wave. A protected squad crosses its restore barrier first.
    private static void ClearAndStartNext(Fixture f, ref float t, int wave, bool team = false)
    {
        t += .2f; ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.ConfirmedArrivals = wave; f.Tick(t);
        t += .2f; ZombieSpawnService.Alive = 0; f.Tick(t);
        if (team)
        {
            for (int i = 0; i < 60 && f.Mode.HudStatusLine.Contains("RESTORING"); i++) { t += .3f; f.Tick(t); }
            Check(!f.Mode.HudStatusLine.Contains("RESTORING"), "protected squad completes its restore barrier in the fixture");
        }
        t += .3f; f.Tick(t);
        t += 2.1f; f.Tick(t);
    }

    private static List<string> Callouts(Fixture f) => f.Match.Callouts;

    private static void CheckSpecialWaves()
    {
        Check(ZombieMode.ThemeLabel(ArenaMode.Shared.ZombieWaveTheme.Runners) == "ALL RUNNERS"
            && ZombieMode.ThemeLabel(ArenaMode.Shared.ZombieWaveTheme.Shooters) == "ALL SHOOTERS"
            && ZombieMode.ThemeLabel(ArenaMode.Shared.ZombieWaveTheme.None).Length == 0,
            "theme labels are the announced words");
        Check(ZombieMode.ThemeLabel(ArenaMode.Shared.ZombieWaveTheme.Runners).Length <= 12
            && ZombieMode.ThemeLabel(ArenaMode.Shared.ZombieWaveTheme.Shooters).Length <= 12,
            "theme labels stay short enough for the score panel line");

        // Maximum, shooter 37: waves 1 to 10 of a solo raid.
        using (var f = new Fixture(size: 1, maxAlive: 2))
        {
            float t = .2f; f.Tick(t);
            var expected = new List<string> { "WAVE 1" };
            for (int wave = 1; wave <= 9; wave++)
            {
                ClearAndStartNext(f, ref t, wave);
                int next = wave + 1;
                if (next == 5) expected.Add("NEXT: ALL RUNNERS");
                if (next == 10) expected.Add("NEXT: ALL SHOOTERS");
                expected.Add(next == 5 ? "WAVE 5: ALL RUNNERS" : next == 10 ? "WAVE 10: ALL SHOOTERS" : $"WAVE {next}");
                Check(Callouts(f).SequenceEqual(expected),
                    $"special waves are announced once before and once at their start, ordinary waves keep the plain callout (wave {next}: {string.Join(" / ", Callouts(f))})");
                string status = f.Mode.HudStatusLine;
                if (next == 5) Check(status.Contains("WAVE 5 (ALL RUNNERS) | ") && !status.Contains("WAVE 5 | "), "status line names the running special wave");
                else if (next == 10) Check(status.Contains("WAVE 10 (ALL SHOOTERS) | "), "status line names the running special wave");
                else Check(!status.Contains("("), "ordinary waves carry no theme in the status line");
            }
        }

        // The intermission before a special wave: the status line names the coming wave and the warning is raised once.
        using (var f = new Fixture(size: 1, maxAlive: 2))
        {
            float t = .2f; f.Tick(t);
            for (int wave = 1; wave <= 3; wave++) ClearAndStartNext(f, ref t, wave);
            t += .2f; ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.ConfirmedArrivals = 4; f.Tick(t);
            Check(!Callouts(f).Any(c => c.StartsWith("NEXT")), "no warning while the current wave is running");
            t += .2f; ZombieSpawnService.Alive = 0; f.Tick(t);
            Check(f.Mode.HudStatusLine.Contains("WAVE 5 (ALL RUNNERS) IN "), "status line of the intermission names the coming special wave");
            Check(Callouts(f).Count(c => c == "NEXT: ALL RUNNERS") == 1, "the warning appears at the start of the intermission");
            t += .3f; f.Tick(t); t += .3f; f.Tick(t);
            Check(Callouts(f).Count(c => c == "NEXT: ALL RUNNERS") == 1, "the warning is not repeated during the intermission");
        }

        // The user's shooter share is respected: 0 never gets an all-shooter wave, 100 never an all-runner wave.
        foreach (int shooter in new[] { 0, 100 })
        using (var f = new Fixture(size: 1, maxAlive: 2, shooter: shooter))
        {
            float t = .2f; f.Tick(t);
            for (int wave = 1; wave <= 4; wave++) ClearAndStartNext(f, ref t, wave);
            bool runners = Callouts(f).Contains("WAVE 5: ALL RUNNERS"), warned = Callouts(f).Contains("NEXT: ALL RUNNERS");
            Check(shooter == 0 ? runners && warned : !runners && !warned && Callouts(f).Contains("WAVE 5"),
                shooter == 0 ? "a shooter share of 0 still gets the runner wave" : "a shooter share of 100 never gets a runner wave");
            for (int wave = 5; wave <= 9; wave++) ClearAndStartNext(f, ref t, wave);
            Check(shooter == 0 ? Callouts(f).Contains("WAVE 10") && !Callouts(f).Any(c => c.Contains("SHOOTERS"))
                : Callouts(f).Contains("WAVE 10: ALL SHOOTERS"),
                shooter == 0 ? "a shooter share of 0 never gets an all-shooter wave" : "a shooter share of 100 keeps the shooter wave");
        }

        // Reduced has no special waves at all, even when the live step changes after the match started.
        ArenaDifficulty.Step = ArenaMode.Shared.ArenaDifficultyStep.Reduced;
        using (var f = new Fixture(size: 1, maxAlive: 2))
        {
            ArenaDifficulty.Step = ArenaMode.Shared.ArenaDifficultyStep.Maximum;
            float t = .2f; f.Tick(t);
            for (int wave = 1; wave <= 9; wave++) ClearAndStartNext(f, ref t, wave);
            Check(Callouts(f).SequenceEqual(Enumerable.Range(1, 10).Select(n => $"WAVE {n}")) && !f.Mode.HudStatusLine.Contains("("),
                "Reduced announces no special wave and ignores a later live step");
        }
        ArenaDifficulty.Step = ArenaMode.Shared.ArenaDifficultyStep.Maximum;

        // A zero-length intermission crosses the break inside one call: no warning can precede the wave, the start callout still names it.
        using (var f = new Fixture(size: 1, maxAlive: 2, intermission: 0))
        {
            float t = .2f; f.Tick(t);
            for (int wave = 1; wave <= 4; wave++)
            {
                t += .2f; ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.ConfirmedArrivals = wave; f.Tick(t);
                t += .2f; ZombieSpawnService.Alive = 0; f.Tick(t);
            }
            Check(Callouts(f).Contains("WAVE 5: ALL RUNNERS") && !Callouts(f).Any(c => c.StartsWith("NEXT")),
                "a zero-length intermission names the wave at its start and cannot warn before it");
        }

        // PitFireTeam: the status line carries the theme through the restore barrier, the warning waits for the countdown.
        using (var f = new Fixture(size: 1, team: true, maxAlive: 2))
        {
            float t = .2f; f.Tick(t);
            for (int i = 0; i < 40 && ZombieSpawnService.Dispatched == 0; i++) { t += .3f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == 1, "protected squad reaches its first wave in the fixture");
            for (int wave = 1; wave <= 3; wave++) ClearAndStartNext(f, ref t, wave, team: true);
            t += .2f; ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.ConfirmedArrivals = 4; f.Tick(t);
            t += .2f; ZombieSpawnService.Alive = 0; f.Tick(t);
            Check(f.Mode.HudStatusLine.Contains("RESTORING") && !Callouts(f).Any(c => c.StartsWith("NEXT")),
                "no warning is raised while the squad is held in the restore barrier");
            int guard = 0;
            while (f.Mode.HudStatusLine.Contains("RESTORING") && guard++ < 60)
            {
                Check(!Callouts(f).Any(c => c.StartsWith("NEXT")), "no warning is raised while the squad is held in the restore barrier");
                t += .3f; f.Tick(t);
            }
            t += .3f; f.Tick(t);
            Check(Callouts(f).Count(c => c == "NEXT: ALL RUNNERS") == 1 && f.Mode.HudStatusLine.Contains("WAVE 5 (ALL RUNNERS) IN "),
                "the warning is raised once, on the first evaluated tick after the restore barrier");
            t += 2.1f; f.Tick(t);
            Check(Callouts(f).Contains("WAVE 5: ALL RUNNERS") && f.Mode.HudStatusLine.Contains("WAVE 5 (ALL RUNNERS) | "),
                "a protected squad gets the same wave-start text and status line");
        }
    }

    // The dispatched infected arrive: pending requests become alive, confirmed arrivals, so the wave keeps filling.
    private static void Arrive()
    {
        ZombieSpawnService.Alive += ZombieSpawnService.Pending;
        ZombieSpawnService.ConfirmedArrivals += ZombieSpawnService.Pending;
        ZombieSpawnService.Pending = 0;
    }

    private static List<string> CompositionLines(string wave)
        => ArenaMode.Plugin.ModLogger.Lines.Where(l => l.StartsWith($"Zombie wave {wave} composition:")).ToList();

    private static void CheckCompositionTally()
    {
        // Six infected per wave, drawn easy / normal / hard in turn: the line counts what was dispatched, once per wave.
        using (var f = new Fixture(size: 6, maxAlive: 6))
        {
            ZombieRoleMix.Script = n => (n % 3) switch { 0 => "easy", 1 => "normal", _ => "hard" };
            float t = .2f; f.Tick(t);
            for (int i = 0; i < 12 && ZombieSpawnService.Dispatched < 6; i++) { Arrive(); t += .25f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == 6 && CompositionLines("1").Count == 1
                && CompositionLines("1")[0] == "Zombie wave 1 composition: easy 2, normal 2, hard 2 (size 6, theme None).",
                "the composition line counts the dispatched draws by difficulty once the wave is fully dispatched");
            Check(ArenaMode.Plugin.ModLogger.DebugLines.Any(l => l.StartsWith("Zombie wave 1 composition:"))
                && !ArenaMode.Plugin.ModLogger.InfoLines.Any(l => l.Contains("composition")),
                "the composition line is a Debug line, never Info (gameplay events log at Debug only)");
            ZombieRoleMix.Script = n => "hard";
            Arrive(); t += .3f; f.Tick(t);
            ZombieSpawnService.Alive = 0; t += .3f; f.Tick(t);
            Check(CompositionLines("1").Count == 1, "a wave's composition line is written once, not again when the wave ends");
            t += .3f; f.Tick(t); t += 2.1f; f.Tick(t);
            for (int i = 0; i < 12 && CompositionLines("2").Count == 0; i++) { Arrive(); t += .25f; f.Tick(t); }
            Check(CompositionLines("2").Count == 1 && CompositionLines("2")[0] == "Zombie wave 2 composition: easy 0, normal 0, hard 6 (size 6, theme None).",
                "the tally starts again for every wave");
        }

        // A rejected dispatch returned its slot, so it is not part of the composition.
        using (var f = new Fixture(size: 2, maxAlive: 2))
        {
            ZombieSpawnService.RejectRequest = true;
            float t = .2f; f.Tick(t);
            ZombieSpawnService.RejectRequest = false;
            for (int i = 0; i < 12 && ZombieSpawnService.Dispatched < 1; i++) { Arrive(); t += .5f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == 1 && CompositionLines("1").Count == 0,
                "a rejected dispatch is not tallied as a draw of the wave");
            for (int i = 0; i < 12 && ZombieSpawnService.Dispatched < 2; i++) { Arrive(); t += .5f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == 2 && CompositionLines("1").Count == 1
                && CompositionLines("1")[0] == "Zombie wave 1 composition: easy 0, normal 2, hard 0 (size 2, theme None).",
                "the line follows the accepted dispatches only");
        }

        // A slot that fails asynchronously is drawn again: the draws can reach the wave size while the wave
        // still owes a slot, and the line must wait for the policy's own count (it then holds the requested mix,
        // the failed first draw included).
        using (var f = new Fixture(size: 2, maxAlive: 2))
        {
            ZombieRoleMix.Script = n => n == 0 ? "easy" : "hard";
            float t = .2f; f.Tick(t);
            Check(ZombieSpawnService.Dispatched == 1, "the first infected of two is dispatched");
            ZombieSpawnService.Pending = 0; ZombieSpawnService.FailedDispatches = 1;
            for (int i = 0; i < 12 && ZombieSpawnService.Dispatched < 2; i++) { t += .5f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == 2 && CompositionLines("1").Count == 0,
                "the line waits for the policy count while a returned slot is owed, although the draws already add up to the wave size");
            for (int i = 0; i < 12 && CompositionLines("1").Count == 0; i++) { Arrive(); t += .5f; f.Tick(t); }
            Check(CompositionLines("1").Count == 1
                && CompositionLines("1")[0] == "Zombie wave 1 composition: easy 1, normal 0, hard 2 (size 2, theme None).",
                "the line is written once the policy count is reached and holds the requested draws, failed ones included");
            // The slot that fails after the line is drawn again; the line is not written a second time.
            ZombieSpawnService.Pending = 0; ZombieSpawnService.Alive = 1; ZombieSpawnService.FailedDispatches = 1;
            int before = ZombieSpawnService.Dispatched;
            for (int i = 0; i < 12 && ZombieSpawnService.Dispatched == before; i++) { t += .5f; f.Tick(t); }
            Check(ZombieSpawnService.Dispatched == before + 1 && CompositionLines("1").Count == 1,
                "a wave's composition line is written once, not again when a returned slot is drawn again");
        }

        // The theme is part of the line, from the same policy the draws use.
        using (var f = new Fixture(size: 1, maxAlive: 2))
        {
            float t = .2f; f.Tick(t);
            for (int wave = 1; wave <= 4; wave++) ClearAndStartNext(f, ref t, wave);
            Check(CompositionLines("5").Count == 1 && CompositionLines("5")[0].EndsWith("(size 1, theme Runners).")
                && CompositionLines("4")[0].EndsWith("theme None)."),
                "the composition line names the special wave's theme");
        }
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
