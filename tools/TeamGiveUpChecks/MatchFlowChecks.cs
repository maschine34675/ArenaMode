using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static ArenaKill CombatKill(Player killer, Player victim, bool localVictim = false)
        => new()
        {
            KillerProfileId = killer.ProfileId, VictimProfileId = victim.ProfileId,
            KillerName = killer.ProfileId, VictimName = victim.ProfileId,
            VictimRole = ArenaRole.Pmc, KilledByLocal = !killer.IsAI,
            LocalIsVictim = localVictim, AwardEligible = true, Headshot = true,
            SingleHitHeadshot = true, KillerBodyId = killer.GetHashCode(),
            VictimBodyId = victim.GetHashCode(), Time = Time.time
        };

    private static void MatchStagingAccounting()
    {
        var raid = new Raid(startLive: false);
        int initialVersion = raid.Match.Scoreboard.Version;
        raid.Match.ReportLocalDeath();
        raid.Match.ReportKill(CombatKill(raid.Local, raid.Local));
        Check(raid.Match.Deaths == 0 && raid.Match.Kills == 0 && raid.Match.Score == 0
            && raid.CapturedKills == 0 && raid.Callouts.Count == 0
            && raid.Match.Scoreboard.Version == initialVersion,
            "assembling callbacks cannot change scores, deaths, awards or kill events");
        raid.StartPlaying();
        var foe = raid.AddBot(false).GetPlayer;
        raid.Match.ReportKill(CombatKill(raid.Local, foe));
        Check(raid.Match.Kills == 1 && raid.Match.Deaths == 0 && raid.Callouts.SequenceEqual(new[] { "FIRST BLOOD" }),
            "ignored staging callbacks cannot consume the first live combat award");
    }

    private static void CountdownDeathAndHandoverCallbacks()
    {
        foreach (bool handover in new[] { false, true })
        {
            var raid = new Raid(startLive: false);
            BurstSpawnService.OnRequest = request =>
            {
                var body = Model.Body("stage-" + ++Model.NextId,
                    request.Role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear, true);
                body.AIData.BotOwner.BotState = EBotState.Active;
                Check(request.Submit(), "countdown callback setup uses real bot reservations");
                request.Observer(body.AIData.BotOwner);
                request.DispatchFinished(true);
            };
            for (int i = 0; i < 5; i++)
            {
                raid.Step(Time.time + 1);
                if (raid.Mode.TryGetMatchNotice(out string text, out _) && text.Contains("MATCH STARTS IN")) break;
            }
            Check(raid.Mode.TryGetMatchNotice(out string heading, out _) && heading.Contains("MATCH STARTS IN"),
                "callback fixture reaches the real production countdown");
            raid.Step(Time.time + 0.01f); // settle the adoption wakeup, then no Tick
            if (handover)
            {
                var replacement = Model.Body("local", EPlayerSide.Usec);
                raid.Match.ReportLocalRespawn(replacement);
            }
            else
            {
                raid.Local.HealthController.IsAlive = false;
                raid.Match.ReportLocalDeath();
                raid.Local.HealthController.IsAlive = true;
            }
            Check(raid.Mode.TryGetMatchNotice(out heading, out _) && heading == "ASSEMBLING TEAMS"
                && raid.Match.Deaths == 0 && raid.Match.Kills == 0,
                handover ? "staging body handover immediately invalidates countdown without a score"
                    : "unscored staging death immediately invalidates countdown before any roster tick");
            raid.Step(Time.time + 10);
            Check(!raid.Mode.InitialRosterReady && raid.Mode.PlayingElapsed == 0,
                "a short staging callback cannot leave an expired countdown armed");
            raid.Step(Time.time + TeamDeathmatchMatchPolicy.CountdownSeconds);
            Check(raid.Mode.InitialRosterReady, "staging callback recovery completes a fresh countdown");
        }
    }

    private static void FinalLocalDeathOrdering()
    {
        foreach (bool usec in new[] { true, false })
        foreach (bool driverFirst in new[] { false, true })
        {
            var raid = new Raid(usec, killLimit: 1);
            var foe = raid.AddBot(!usec).GetPlayer;
            var local = raid.Local;
            local.HealthController.IsAlive = false;
            ArenaRespawnDriver.IsRespawnInProgress = true;
            var death = CombatKill(foe, local, localVictim: true);
            if (driverFirst) raid.Match.ReportLocalDeath();
            raid.Match.ReportKill(death);
            if (!driverFirst) raid.Match.ReportLocalDeath();
            Check(raid.Mode.HasResult && raid.Mode.ResultHeading == "DEFEAT"
                && raid.Match.Deaths == 1
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Deaths == 1
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "final local kill and driver death count exactly once in either native event order");
            int version = raid.Match.Scoreboard.Version;
            int events = raid.CapturedKills;
            raid.Match.ReportKill(death);
            raid.Match.ReportLocalDeath();
            raid.Match.ReportLocalRespawn(local);
            var replacement = Model.Body("local", usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            raid.Match.ReportLocalRespawn(replacement);
            Check(raid.Match.Deaths == 1 && raid.Match.Scoreboard.Version == version
                && !raid.Match.Scoreboard.Local.IsActive && raid.CapturedKills == events,
                "post-result kill, death and replacement callbacks cannot mutate the final board or events");
            int calls = replacement.HealthController.KillCalls;
            ArenaGiveUp.Reset();
            ArenaGiveUp.Request();
            Check(replacement.HealthController.KillCalls == calls,
                "give up on the result screen requests exit without killing a replacement");
            raid.Step(Time.time + 1);
            Check(TdmMatchExit.StopCalls == 1,
                "result exit works while the dead-player respawn hold remains set");
        }
    }

    private static void GiveUpCanFinishMatch()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, killLimit: 1);
            var body = raid.Local;
            var cues = new List<ArenaAnnouncerCue>();
            raid.Match.OnAnnouncerCue += cues.Add;
            ArenaGiveUp.Request();
            Check(raid.Mode.HasResult && raid.Mode.ResultHeading == "DEFEAT"
                && raid.Match.Deaths == 1 && raid.Match.Kills == 0
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "a confirmed give-up life can reach the opposing team's winning limit");
            Check(raid.CapturedKills == 0 && raid.Callouts.Count == 0
                && cues.SequenceEqual(new[] { ArenaAnnouncerCue.MatchLost }),
                "a give-up match loss emits only its lifecycle cue, never a fabricated kill award");
            int version = raid.Match.Scoreboard.Version;
            raid.Mode.CompleteLocalGiveUp(body, true);
            raid.Match.ReportLocalDeath();
            ArenaGiveUp.Reset();
            ArenaGiveUp.Request();
            Check(body.HealthController.KillCalls == 1 && raid.Match.Scoreboard.Version == version
                && cues.Count == 1, "terminal forfeit callbacks cannot score or announce the same life twice");
        }
    }

    private static void DeadlineBeforeCombatAndForfeit()
    {
        foreach (bool giveUp in new[] { false, true })
        {
            var raid = new Raid(timeLimit: 5);
            var foe = raid.AddBot(false).GetPlayer;
            float startedAt = Time.time - raid.Mode.PlayingElapsed;
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = startedAt + 5;
            // Deliberately do not Tick: a death/kill callback can precede the
            // controller tick on the frame reaching the deadline.
            if (giveUp) ArenaGiveUp.Request();
            else raid.Match.ReportKill(CombatKill(raid.Local, foe));
            Check(raid.Mode.HasResult && raid.Mode.ResultHeading == "DRAW"
                && raid.Match.Kills == 0 && raid.Match.Deaths == 0
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0
                && raid.CapturedKills == 0 && raid.Callouts.Count == 0,
                "an event at the exact time limit cannot score into an expired match");
            if (giveUp)
                Check(raid.Local.HealthController.KillCalls == 0 && raid.Local.HealthController.IsAlive,
                    "give-up that first discovers an expired match requests exit without a post-result native death");
        }
    }
}
