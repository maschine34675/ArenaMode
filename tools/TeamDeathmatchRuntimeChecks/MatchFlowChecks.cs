using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static Raid AssembleForCountdown()
    {
        var raid = new Raid(size: 2);
        BurstSpawnService.OnRequest = request =>
        {
            Deliver(request, EBotState.Active);
            request.DispatchFinished(true);
        };
        for (int tick = 0; tick < raid.Mode.TeamSize + 2; tick++)
        {
            raid.Step(Time.time + 1);
            if (raid.Mode.TryGetMatchNotice(out string heading, out _) && heading.Contains("MATCH STARTS IN"))
            {
                // Adoption wakes the mode immediately. Run that legitimate
                // wakeup so the next fractional-frame test is really throttled.
                raid.Step(Time.time + 0.01f);
                return raid;
            }
        }
        throw new InvalidOperationException("real countdown could not be assembled");
    }

    private static void MatchReadinessLossBetweenRosterTicks()
    {
        var unreadable = AssembleForCountdown();
        var native = unreadable.Bots;
        unreadable.Game.BotsController.BotSpawner._bots.BotOwners = null;
        unreadable.Step(Time.time + 1);
        Check(unreadable.Mode.TryGetMatchNotice(out string heading, out _) && heading == "ASSEMBLING TEAMS",
            "unreadable native roster cancels an already running countdown");
        unreadable.Game.BotsController.BotSpawner._bots.BotOwners = native;
        unreadable.Step(Time.time + 10);
        float recoveredAt = Time.time;
        Check(!unreadable.Mode.InitialRosterReady && unreadable.Mode.PlayingElapsed == 0,
            "native roster recovery cannot reuse an expired countdown deadline");
        unreadable.Step(recoveredAt + TeamDeathmatchMatchPolicy.CountdownSeconds - 1);
        Check(!unreadable.Mode.InitialRosterReady, "readable roster receives a fresh complete countdown");
        unreadable.Step(recoveredAt + TeamDeathmatchMatchPolicy.CountdownSeconds);
        Check(unreadable.Mode.InitialRosterReady, "readable roster releases combat after the replacement countdown");

        foreach (int cause in Enumerable.Range(0, 5))
        {
            var raid = AssembleForCountdown();
            var local = raid.Local;
            if (cause == 0) ArenaMode.Areas.ArenaAreaRuntime.IsPending = true;
            if (cause == 1) ArenaRespawnDriver.IsRespawnInProgress = true;
            if (cause == 2) LiveKitRespawn.IsInitialSwapPending = true;
            if (cause == 3) local.HealthController.IsAlive = false;
            if (cause == 4) raid.World.MainPlayer = new Player { ProfileId = "local" };
            raid.Step(Time.time + 0.25f); // Native roster Tick remains throttled.
            Check(raid.Mode.TryGetMatchNotice(out heading, out _) && heading == "ASSEMBLING TEAMS",
                $"short local readiness loss cancels countdown before the roster tick (cause={cause})");
            ArenaMode.Areas.ArenaAreaRuntime.IsPending = false;
            ArenaRespawnDriver.IsRespawnInProgress = false;
            LiveKitRespawn.IsInitialSwapPending = false;
            local.HealthController.IsAlive = true;
            raid.World.MainPlayer = local;
            raid.Step(Time.time + 10);
            Check(!raid.Mode.InitialRosterReady && raid.Mode.PlayingElapsed == 0,
                "short readiness loss cannot release combat on the old deadline");
            raid.Step(Time.time + TeamDeathmatchMatchPolicy.CountdownSeconds);
            Check(raid.Mode.InitialRosterReady,
                $"short readiness loss recovers through a full countdown (cause={cause}, state={raid.Mode.HudStatusLine})");
        }
    }

    private static void MatchReadinessAndCountdown()
    {
        var raid = new Raid(size: 2);
        raid.Step(0);
        var friend = Deliver(Request(EPlayerSide.Usec), EBotState.Active);
        var foe = Deliver(Request(EPlayerSide.Bear), EBotState.Active);
        raid.Step(1);
        var secondFoe = Deliver(Request(EPlayerSide.Bear), EBotState.PreActive);
        raid.Step(2);
        Check(!raid.Mode.TryBeginScoredEvent() && raid.Mode.PlayingElapsed == 0
            && raid.Mode.ShouldBlockMatchDamage(friend.GetPlayer),
            "preactive full roster cannot release scoring, time or staging damage");
        secondFoe.BotState = EBotState.Active;
        LiveKitRespawn.IsInitialSwapPending = true;
        raid.Step(3);
        raid.Step(10);
        Check(!raid.Mode.InitialRosterReady && raid.Mode.PlayingElapsed == 0
            && raid.Mode.TryGetMatchNotice(out string waitingHeading, out _) && waitingHeading == "ASSEMBLING TEAMS",
            "the initial player kit swap must finish before countdown admission");
        LiveKitRespawn.IsInitialSwapPending = false;
        raid.Step(11);
        Check(raid.Mode.TryGetMatchNotice(out string heading, out _) && heading.Contains("MATCH STARTS IN")
            && !raid.Mode.TryBeginScoredEvent() && raid.Match.Cues.Count == 0,
            "full roster and completed kit begin a visible non-scoring countdown");
        var stranger = new Player { ProfileId = "foreign" };
        Check(raid.Mode.ShouldBlockMatchDamage(raid.Local) && raid.Mode.ShouldBlockMatchDamage(foe.GetPlayer)
            && !raid.Mode.ShouldBlockMatchDamage(stranger), "countdown protects exact participants only");
        raid.Local.HealthController.IsAlive = false;
        raid.Step(12);
        Check(raid.Mode.TryGetMatchNotice(out heading, out _) && heading == "ASSEMBLING TEAMS",
            "loss of local readiness resets the countdown to assembly");
        raid.Local.HealthController.IsAlive = true;
        raid.Step(13);
        raid.Step(13 + TeamDeathmatchMatchPolicy.CountdownSeconds - 1);
        Check(!raid.Mode.InitialRosterReady && raid.Mode.PlayingElapsed == 0,
            "readiness recovery receives a complete fresh countdown");
        raid.Step(13 + TeamDeathmatchMatchPolicy.CountdownSeconds);
        Check(raid.Mode.InitialRosterReady && raid.Mode.TryBeginScoredEvent()
            && raid.Match.Cues.SequenceEqual(new[] { ArenaAnnouncerCue.RoundStart })
            && raid.Mode.PlayingElapsed == 0 && !raid.Mode.ShouldBlockMatchDamage(foe.GetPlayer),
            "combat and match clock release together exactly once after the countdown");
        raid.Step(Time.time + 2);
        Check(raid.Mode.PlayingElapsed == 2, "only released combat consumes match time");
    }

    private static void MatchTerminalPopulationAndPresentation()
    {
        var raid = new Raid(size: 2, killLimit: 1);
        raid.Fill();
        BurstSpawnService.OnRequest = null;
        var foe = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        foe.HealthController.IsAlive = false;
        raid.Step(Time.time + 1);
        var pending = Request(EPlayerSide.Bear);
        Check(pending.Submit(), "terminal race starts with a real in-flight native reservation");
        var victory = Kill("local", foe.ProfileId, true);
        raid.Mode.ClassifyKill(victory);
        raid.Match.Scoreboard.ReportKill(victory);
        raid.Mode.OnKill(victory);
        Check(raid.Mode.HasResult && raid.Mode.IsComplete && raid.Mode.HoldRespawn
            && raid.Mode.ResultHeading == "VICTORY" && !raid.Mode.InitialRosterReady,
            "a kill-limit result freezes play and holds local respawn");
        Check(raid.Match.Cues.Count(cue => cue == ArenaAnnouncerCue.MatchWon) == 1,
            "a terminal winning cue is emitted exactly once");
        Check(!pending.Current() && !pending.Submit(), "result immediately revokes pending request and dispatch ownership");
        var late = Deliver(pending, EBotState.Active, submit: false);
        Check(!late.HealthController.IsAlive && late.HealthController.KillCalls == 1,
            "late native arrival after result is retired instead of admitted");
        int count = BurstSpawnService.Requests.Count;
        int version = raid.Match.Scoreboard.Version;
        var local = raid.Local;
        local.HealthController.IsAlive = false;
        raid.Mode.TrySynchronizeScoreboardRoster(out _);
        Check(raid.Match.Scoreboard.Version == version,
            "terminal roster synchronization cannot mutate the final scoreboard");
        var postResult = Kill("local", raid.Bots.First(bot => bot.HealthController.IsAlive
            && bot.Profile.Info.Side == EPlayerSide.Bear).ProfileId, true);
        raid.Mode.ClassifyKill(postResult);
        Check(!postResult.ScoreEligible && !postResult.AwardEligible && !raid.Mode.TryBeginScoredEvent(),
            "terminal delayed kills have neither score nor award eligibility");
        TdmMatchExit.PipelineRunning = true;
        ArenaRespawnDriver.IsRespawnInProgress = true;
        raid.Step(Time.time + TeamDeathmatchMatchPolicy.ResultSeconds + 5);
        raid.Step(Time.time + 1);
        Check(TdmMatchExit.StopCalls == 0 && raid.Mode.ResultDisplayRemaining == TeamDeathmatchMatchPolicy.ResultSeconds,
            "result display waits for actual replacement work without spending its display interval");
        TdmMatchExit.PipelineRunning = false;
        raid.Step(Time.time + 1);
        Check(TdmMatchExit.StopCalls == 0 && ArenaRespawnDriver.IsRespawnInProgress,
            "held death flag does not prevent the result display from beginning");
        raid.Step(Time.time + TeamDeathmatchMatchPolicy.ResultSeconds);
        Check(TdmMatchExit.StopCalls == 1 && BurstSpawnService.Requests.Count == count
            && raid.Match.Scoreboard.Version == version,
            "terminal match requests exit after its display without refills or final-score mutation");
        raid.Mode.OnKill(victory);
        Check(raid.Match.Cues.Count(cue => cue == ArenaAnnouncerCue.MatchWon) == 1,
            "late result checks cannot replay the winning cue");
        var other = new Raid(size: 2);
        raid.Mode.RequestResultExit();
        raid.Mode.Tick(1);
        Check(TdmMatchExit.StopCalls == 0 && !raid.Mode.ShouldBlockMatchDamage(other.Local),
            "an old result cannot exit or protect another raid");
    }

    private static void MatchTimeLimitAndRuleSnapshot()
    {
        var raid = new Raid(size: 2, timeLimit: 5);
        raid.Fill();
        float releasedAt = Time.time - raid.Mode.PlayingElapsed;
        Settings.TeamDeathmatchTimeLimitSeconds.Value = 0;
        Settings.TeamDeathmatchKillLimit.Value = 1;
        raid.Step(releasedAt + 4);
        Check(!raid.Mode.IsComplete, "time limit is not reached before its exact deadline");
        raid.Step(releasedAt + 5);
        Check(raid.Mode.HasResult && raid.Mode.ResultHeading == "DRAW" && raid.Mode.PlayingElapsed == 5,
            "frozen time-limit settings finish an equal score as a draw exactly at deadline");
        Check(raid.Match.Cues.SequenceEqual(new[] { ArenaAnnouncerCue.RoundStart }),
            "draw never borrows a victory or defeat announcer cue");
        float elapsed = raid.Mode.PlayingElapsed;
        raid.Step(releasedAt + 99);
        Check(raid.Mode.PlayingElapsed == elapsed, "terminal elapsed time stays frozen");
        var unlimited = new Raid(size: 2);
        unlimited.Fill();
        unlimited.Step(10000);
        Check(!unlimited.Mode.IsComplete && unlimited.Mode.TryBeginScoredEvent(),
            "explicit unlimited settings preserve continuous Team DM");
    }
}
