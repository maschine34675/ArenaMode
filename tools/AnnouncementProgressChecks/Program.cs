using ArenaMode.Audio;
using ArenaMode.Modes;

internal static class Program
{
    private static int _assertions;

    private sealed class Scenario
    {
        internal readonly ArenaAnnouncementPolicy Policy = new();
        internal ArenaAnnouncementSnapshot Snapshot = new() { Active = true, Epoch = 1 };
        internal readonly List<ArenaAnnouncerCue> Heard = new();

        internal void Tick(params ArenaAnnouncerCue[] expected)
        {
            Heard.Clear();
            Policy.Update(Snapshot, Heard.Add);
            Equal(Heard.SequenceEqual(expected),
                $"Expected [{string.Join(", ", expected)}], heard [{string.Join(", ", Heard)}].");
        }
    }

    private static void Equal(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }

    private static int Main(string[] args)
    {
        var cases = new (string Name, Action Run)[]
        {
            ("countdown", Countdown), ("time", TimeThresholds), ("kills", KillThresholds),
            ("lead", Lead), ("series", Series), ("ladder", Ladder), ("draw", Draw),
            ("lifecycle", Lifecycle), ("invalid", Invalid),
        };
        try
        {
            foreach (var test in cases)
            {
                if (args.Length != 0 && args[0] != test.Name) continue;
                try { test.Run(); }
                catch (Exception error) { throw new InvalidOperationException($"{test.Name}: {error.Message}", error); }
            }
            Equal(_assertions > 0, "At least one matching scenario must execute.");
            Console.WriteLine($"ANNOUNCEMENT_PROGRESS_POLICY_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("ANNOUNCEMENT_PROGRESS_POLICY_FAILED: " + error.Message);
            return 1;
        }
    }

    private static void Countdown()
    {
        var s = new Scenario();
        s.Snapshot.Preparing = true;
        s.Tick(ArenaAnnouncerCue.PrepareToFight);
        s.Tick();
        s.Snapshot.Countdown = 3;
        s.Tick(ArenaAnnouncerCue.Three);
        s.Tick();
        s.Snapshot.Countdown = 1;
        s.Tick(ArenaAnnouncerCue.One); // skipped 2 is never replayed
        s.Tick();
        s.Snapshot.Countdown = null;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Tick();
        s.Snapshot.Countdown = 3;
        s.Tick(ArenaAnnouncerCue.Three); // same logical epoch, readiness restarted
        s.Snapshot.Countdown = 2;
        s.Tick(ArenaAnnouncerCue.Two);
        s.Snapshot.Live = true;
        s.Tick(ArenaAnnouncerCue.AdvanceStatus);
        s.Tick(); // stale Preparing and Countdown cannot speak while live
        s.Snapshot.Live = false;
        s.Snapshot.Terminal = true;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Tick();
        s.Snapshot.Epoch++;
        s.Snapshot.Terminal = false;
        s.Snapshot.Countdown = null;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.PrepareToFight);
        s.Snapshot.Preparing = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus);

        s = new Scenario();
        s.Snapshot.Countdown = 2;
        s.Tick(ArenaAnnouncerCue.Two); // joining midway emits current number only
        s.Snapshot.Countdown = 0;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.Countdown = 4;
        s.Tick();
    }

    private static void TimeThresholds()
    {
        var s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.SecondsRemaining = 61;
        s.Tick();
        s.Snapshot.SecondsRemaining = 60;
        s.Tick(ArenaAnnouncerCue.OneMinuteRemaining);
        s.Tick();
        s.Snapshot.SecondsRemaining = 30;
        s.Tick(ArenaAnnouncerCue.ThirtySecondsRemaining);
        s.Snapshot.SecondsRemaining = 10;
        s.Tick(ArenaAnnouncerCue.TenSecondsRemaining);
        s.Snapshot.SecondsRemaining = 0;
        s.Tick();
        s.Snapshot.SecondsRemaining = 65;
        s.Tick();
        s.Snapshot.SecondsRemaining = 8;
        s.Tick();
        s.Snapshot.Epoch++;
        s.Snapshot.SecondsRemaining = 70;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.SecondsRemaining = 25;
        s.Tick(ArenaAnnouncerCue.ThirtySecondsRemaining);
        s.Snapshot.SecondsRemaining = 7;
        s.Tick(ArenaAnnouncerCue.TenSecondsRemaining);

        s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.SecondsRemaining = 9;
        s.Tick(); // attaching low never catches up
        s.Snapshot.SecondsRemaining = 70;
        s.Tick();
        s.Snapshot.SecondsRemaining = 29;
        s.Tick(); // already expired thresholds remain consumed
        s.Snapshot.Epoch++;
        s.Snapshot.SecondsRemaining = 70;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.SecondsRemaining = 9;
        s.Tick(ArenaAnnouncerCue.TenSecondsRemaining); // most urgent of three
        s.Snapshot.Epoch++;
        s.Snapshot.SecondsRemaining = 70;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.SecondsRemaining = -2;
        s.Tick(); // elapsed time is a result boundary, never a countdown warning

        s = new Scenario();
        s.Snapshot.SecondsRemaining = 70;
        s.Tick();
        s.Snapshot.SecondsRemaining = 59;
        s.Tick(); // warmup cannot consume or announce live match time
        s.Snapshot.Live = true;
        s.Tick();
        s.Snapshot.SecondsRemaining = 29;
        s.Tick(ArenaAnnouncerCue.ThirtySecondsRemaining);
        s.Snapshot.SecondsRemaining = null;
        s.Tick();
        s.Snapshot.SecondsRemaining = 9;
        s.Tick(); // newly supplied limit begins with a baseline
    }

    private static void KillThresholds()
    {
        var s = new Scenario();
        s.Snapshot.Live = true;
        s.Tick(); // unlimited means no remaining-kill warning
        s.Snapshot.KillsRemaining = 8;
        s.Tick();
        s.Snapshot.KillsRemaining = 5;
        s.Tick(ArenaAnnouncerCue.FiveKillsRemaining);
        s.Tick();
        s.Snapshot.KillsRemaining = 1;
        s.Tick(ArenaAnnouncerCue.OneKillRemaining);
        s.Snapshot.KillsRemaining = 8;
        s.Tick();
        s.Snapshot.KillsRemaining = 1;
        s.Tick();

        s = new Scenario();
        s.Snapshot.KillsRemaining = 8;
        s.Tick();
        s.Snapshot.KillsRemaining = 4;
        s.Tick(); // only actual live scoring has remaining-kill warnings
        s.Snapshot.Live = true;
        s.Tick();
        s.Snapshot.KillsRemaining = 1;
        s.Tick(ArenaAnnouncerCue.OneKillRemaining);
        s.Snapshot.Epoch++;
        s.Snapshot.KillsRemaining = 8;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.KillsRemaining = 1;
        s.Tick(ArenaAnnouncerCue.OneKillRemaining);
        s.Snapshot.Epoch++;
        s.Snapshot.KillsRemaining = 8;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.KillsRemaining = 0;
        s.Tick();

        s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.KillsRemaining = 3;
        s.Tick();
        s.Snapshot.KillsRemaining = 1;
        s.Tick(ArenaAnnouncerCue.OneKillRemaining);
        s.Snapshot.Live = false;
        s.Snapshot.KillsRemaining = 9;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.KillsRemaining = 1;
        s.Tick();
    }

    private static void Lead()
    {
        var s = new Scenario();
        s.Snapshot.Live = true;
        s.Tick(); // genuine scoreless null baseline
        s.Snapshot.Lead = 1;
        s.Tick(ArenaAnnouncerCue.TakenTheLead);
        s.Tick();
        s.Snapshot.Now = 1;
        s.Snapshot.Lead = 0;
        s.Tick();
        s.Snapshot.Now = 2;
        s.Snapshot.Lead = -1;
        s.Tick();
        s.Snapshot.Now = 3;
        s.Tick(ArenaAnnouncerCue.LostTheLead); // latest transition retained
        s.Snapshot.Now = 6;
        s.Snapshot.Lead = 0;
        s.Tick(ArenaAnnouncerCue.TiedForTheLead);
        s.Snapshot.Now = 7;
        s.Snapshot.Lead = 1;
        s.Tick();
        s.Snapshot.Now = 8;
        s.Snapshot.Lead = 0;
        s.Tick();
        s.Snapshot.Now = 9;
        s.Tick(); // returned to last spoken state, no duplicate
        s.Snapshot.Lead = null;
        s.Tick();
        s.Snapshot.Lead = -1;
        s.Tick(ArenaAnnouncerCue.LostTheLead);
        s.Snapshot.Now = 10;
        s.Snapshot.Lead = 1;
        s.Tick();
        s.Snapshot.Live = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.Now = 20;
        s.Tick(); // no deferred lead after result/exit
        s.Snapshot.Epoch++;
        s.Snapshot.Live = true;
        s.Snapshot.Lead = null;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.Lead = 0;
        s.Tick(ArenaAnnouncerCue.TiedForTheLead); // actual nonzero tie supplied by mode

        s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.Lead = 1;
        s.Tick(); // no catch-up when attached after scoring
        s.Snapshot.Lead = -1;
        s.Tick(ArenaAnnouncerCue.LostTheLead);
        s.Snapshot.Lead = 1;
        s.Snapshot.Now = 2.999;
        s.Tick();
        s.Snapshot.Now = 3;
        s.Tick(ArenaAnnouncerCue.TakenTheLead);
    }

    private static void Series()
    {
        var s = new Scenario();
        s.Snapshot.FinalRound = s.Snapshot.MatchPoint = true;
        s.Tick();
        s.Snapshot.Preparing = true;
        s.Tick(ArenaAnnouncerCue.PrepareToFight);
        s.Snapshot.Live = true;
        s.Tick(ArenaAnnouncerCue.AdvanceStatus, ArenaAnnouncerCue.FinalRound);
        s.Tick();
        s.Snapshot.FinalRound = s.Snapshot.MatchPoint = false;
        s.Tick();
        s.Snapshot.FinalRound = s.Snapshot.MatchPoint = true;
        s.Tick();
        s.Snapshot.Preparing = s.Snapshot.Live = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Snapshot.Live = true;
        s.Tick(); // same epoch is still the same logical round
        s.Snapshot.Epoch++;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.FinalRound);

        s = new Scenario();
        s.Snapshot.Live = true;
        s.Tick();
        s.Snapshot.MatchPoint = s.Snapshot.FinalRound = true;
        s.Tick(); // flags becoming true mid-round do not invent a live entry

        s = new Scenario();
        s.Snapshot.Live = s.Snapshot.MatchPoint = true;
        s.Tick(ArenaAnnouncerCue.MatchPoint); // one side can win, but not yet the deciding round
        s.Tick();
    }

    private static void Ladder()
    {
        var s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.LadderCycle = 0;
        s.Snapshot.WeaponCount = 3;
        s.Snapshot.WeaponTier = null;
        s.Tick();
        s.Snapshot.WeaponTier = 1;
        s.Tick();
        s.Snapshot.WeaponTier = 2;
        s.Tick(ArenaAnnouncerCue.FinalWeapon);
        s.Tick();
        s.Snapshot.WeaponTier = 1;
        s.Tick();
        s.Snapshot.WeaponTier = 2;
        s.Tick(); // demotion/re-promotion in same cycle is silent
        s.Snapshot.LadderCycle = 1;
        s.Snapshot.WeaponTier = null;
        s.Tick(ArenaAnnouncerCue.LadderComplete); // actual cycle increment, tier already cleared
        s.Tick();
        s.Snapshot.WeaponTier = 0;
        s.Tick();
        s.Snapshot.WeaponTier = 2;
        s.Tick(ArenaAnnouncerCue.FinalWeapon);
        s.Snapshot.LadderCycle = 0;
        s.Tick(); // stale cycle cannot repeat progress
        s.Snapshot.LadderCycle = 1;
        s.Tick();
        s.Snapshot.LadderCycle = 4;
        s.Snapshot.WeaponTier = null;
        s.Tick(ArenaAnnouncerCue.LadderComplete); // skipped cycles coalesce to one
        s.Snapshot.WeaponTier = 2;
        s.Tick(ArenaAnnouncerCue.FinalWeapon);
        s.Snapshot.LadderCycle = 5;
        s.Snapshot.WeaponTier = null;
        s.Snapshot.Terminal = true;
        s.Snapshot.Live = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.LadderComplete);
        s.Tick();

        s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.LadderCycle = 9;
        s.Snapshot.WeaponCount = 1;
        s.Snapshot.WeaponTier = 0;
        s.Tick(ArenaAnnouncerCue.FinalWeapon); // equipped one-weapon ladder is already final
        s.Snapshot.Epoch++;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.FinalWeapon);
    }

    private static void Draw()
    {
        var s = new Scenario();
        s.Snapshot.Terminal = true;
        s.Tick(ArenaAnnouncerCue.CancelStatus); // terminal/no-contest is not an inferred draw
        s.Snapshot.Draw = true;
        s.Tick(ArenaAnnouncerCue.Draw);
        s.Tick();
        s.Snapshot.Draw = false;
        s.Tick();
        s.Snapshot.Draw = true;
        s.Tick();
        s.Snapshot.Epoch++;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.Draw); // one cancellation at epoch/terminal overlap
        s.Snapshot.Epoch++;
        s.Snapshot.Terminal = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.Draw); // actual round draw
        s.Snapshot.Epoch++;
        s.Snapshot.Live = true;
        s.Tick(ArenaAnnouncerCue.CancelStatus); // a live tie is not a draw
    }

    private static void Lifecycle()
    {
        var s = new Scenario();
        s.Snapshot.Active = false;
        s.Snapshot.Preparing = s.Snapshot.Draw = s.Snapshot.FinalRound = s.Snapshot.MatchPoint = true;
        s.Snapshot.Countdown = 3;
        s.Tick();
        s.Snapshot.Active = true;
        s.Snapshot.Draw = false;
        s.Tick(ArenaAnnouncerCue.PrepareToFight, ArenaAnnouncerCue.Three);
        s.Snapshot.Active = false;
        s.Tick(ArenaAnnouncerCue.CancelStatus);
        s.Tick();
        s.Snapshot.Active = true;
        s.Tick(ArenaAnnouncerCue.PrepareToFight, ArenaAnnouncerCue.Three);
        s.Policy.Reset();
        s.Snapshot.Active = false;
        s.Tick(); // detach Reset leaves no pending emission
        s.Snapshot.Active = true;
        var before = s.Snapshot;
        s.Tick(ArenaAnnouncerCue.PrepareToFight, ArenaAnnouncerCue.Three);
        Equal(before.Equals(s.Snapshot), "Policy must not mutate even the input snapshot.");
    }

    private static void Invalid()
    {
        var s = new Scenario();
        s.Snapshot.Live = true;
        s.Snapshot.SecondsRemaining = double.NaN;
        s.Snapshot.Lead = 9;
        s.Snapshot.LadderCycle = -1;
        s.Snapshot.WeaponCount = 0;
        s.Snapshot.WeaponTier = -1;
        s.Tick();
        s.Snapshot.SecondsRemaining = double.PositiveInfinity;
        s.Tick();
        s.Snapshot.SecondsRemaining = 29;
        s.Tick();
        s.Snapshot.SecondsRemaining = 9;
        s.Tick(ArenaAnnouncerCue.TenSecondsRemaining);
        s.Snapshot.Now = 100;
        s.Snapshot.Lead = 1;
        s.Tick(ArenaAnnouncerCue.TakenTheLead);
        s.Snapshot.Now = 1;
        s.Snapshot.Lead = -1;
        s.Tick(ArenaAnnouncerCue.LostTheLead); // simulation-clock reset does not strand cooldown
        s.Snapshot.Now = double.NaN;
        s.Snapshot.Lead = 1;
        s.Tick();
        s.Snapshot.Now = 4;
        s.Tick(ArenaAnnouncerCue.TakenTheLead);
    }
}
