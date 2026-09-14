using ArenaMode.Combat;
using ArenaMode.Modes;
using EFT;

internal static partial class Program
{
    private static int _checks;
    private static long _body = 100;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static ArenaKill Kill(string killer = "local", long killerBody = 1,
        string victim = null, long victimBody = 0, float time = 10)
    {
        victimBody = victimBody == 0 ? ++_body : victimBody;
        victim ??= "victim-" + victimBody;
        return new ArenaKill
        {
            KillerProfileId = killer, KillerBodyId = killerBody, KillerName = killer,
            VictimProfileId = victim, VictimBodyId = victimBody, VictimName = victim,
            KilledByLocal = killer == "local", LocalIsVictim = victim == "local",
            AwardEligible = true, ScoreEligible = true, VictimRole = ArenaRole.Pmc, Time = time,
        };
    }
    private static (ArenaMatch Match, List<string> Labels, List<ArenaAnnouncerCue> Cues) New(IArenaMode mode = null)
    {
        var match = new ArenaMatch(mode ?? new TestMode(), "local", "You", "PMC");
        var labels = new List<string>(); var cues = new List<ArenaAnnouncerCue>();
        match.OnCallout += labels.Add; match.OnAnnouncerCue += cues.Add;
        return (match, labels, cues);
    }
    private static void Sync(ArenaMatch match, int epoch, string opponent, bool alive = true,
        string stable = null, bool friendly = false)
    {
        var actor = new ArenaScoreboardActor(opponent, opponent, "PMC", alive, stable);
        Check(match.Scoreboard.TrySynchronizeRoster(epoch,
            new ArenaScoreboardActor("local", "You", "PMC", true),
            friendly ? new[] { actor } : Array.Empty<ArenaScoreboardActor>(),
            friendly ? Array.Empty<ArenaScoreboardActor>() : new[] { actor }, out _), "roster fixture accepted");
    }
    private static void FeedStreak(ArenaMatch match, string actor = "enemy", long body = 20, int count = 5)
    { for (int i = 0; i < count; i++) match.ReportKill(Kill(actor, body, time: 10 + 10 * i)); }
    private static void Multi()
    {
        foreach (var boundary in new[] { 5, 10, 15, 20, 25, 35 })
        foreach (var chain in new[] { 2, 3, 4, 5 })
        {
            var s = New();
            for (int i = 1; i <= boundary; i++)
                s.Match.ReportKill(Kill(time: i <= boundary - chain + 1 ? i * 10 : (boundary - chain + 1) * 10 + i - (boundary - chain + 1)));
            Check(s.Labels.LastOrDefault() == ScoreRules.MultiKillLabel(chain), "multi-kill must beat simultaneous spree");
            Check(s.Match.Kills == boundary && s.Match.Streak == boundary
                && s.Match.Score == 200 * boundary + 100 * (boundary / 5), "priority preserves score and streak bonuses");
        }
        var single = New();
        for (int i = 1; i <= 5; i++) single.Match.ReportKill(Kill(time: i * 10));
        Check(single.Labels.LastOrDefault() == "KILLING SPREE", "uncontested spree is still announced");
        var early = New(); early.Match.BeginAwardRound();
        early.Match.ReportKill(Kill(time: 1));
        early.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart, beginAwardRound: false);
        early.Match.ReportKill(Kill(time: 2));
        Check(early.Labels.SequenceEqual(new[] { "FIRST BLOOD", "DOUBLE KILL" }), "delayed cue preserves early First Blood and multi-kill chain");
        early.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
        early.Match.ReportKill(Kill(time: 3));
        Check(early.Labels.LastOrDefault() == "FIRST BLOOD", "new award round resets First Blood");
        var payback = New(); payback.Match.ReportKill(Kill("enemy", 20, "local", 1));
        payback.Match.ReportLocalDeath(); payback.Match.ReportLocalRespawn(new Player { ProfileId = "local" });
        FeedStreak(payback.Match);
        payback.Match.ReportKill(Kill(victim: "enemy", victimBody: 20));
        Check(payback.Labels.LastOrDefault() == "PAYBACK", "Payback retains precedence over Shutdown");
        Check(payback.Match.Deaths == 1, "revenge bookkeeping preserves one native death");
        var head = New(); head.Match.ReportKill(Kill("other", 30));
        var headKill = Kill(); headKill.SingleHitHeadshot = true; head.Match.ReportKill(headKill);
        Check(head.Labels.LastOrDefault() == "HEADSHOT", "headshot fallback survives priority expansion");
    }
    private static void Shutdown()
    {
        var success = New(); Sync(success.Match, 1, "enemy"); FeedStreak(success.Match);
        int killsBefore = success.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills;
        success.Match.ReportKill(Kill(victim: "enemy", victimBody: 20));
        Check(success.Labels.LastOrDefault() == "SHUTDOWN", "shutdown captured before victim reset");
        Check(success.Match.Kills == 1 && success.Match.Score == 200
            && success.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == killsBefore,
            "shutdown does not add or subtract score");
        foreach (string fault in new[] { "four", "unknown-profile", "unknown-victim-body", "unknown-killer-body",
            "wrong-killer-profile", "wrong-victim-body", "not-local", "local-victim", "ineligible", "no-score",
            "friendly-flag", "friendly-row", "wrong-killer-side", "expired", "inactive", "rebound", "dead-life", "new-life" })
        {
            var s = New(); Sync(s.Match, 1, "enemy", stable: fault == "rebound" ? "account" : null,
                friendly: fault == "friendly-row");
            FeedStreak(s.Match, count: fault == "four" ? 4 : 5);
            var kill = Kill(victim: "enemy", victimBody: 20);
            switch (fault)
            {
                case "unknown-profile": kill.VictimProfileId = "absent"; break;
                case "unknown-victim-body": kill.VictimBodyId = 0; break;
                case "unknown-killer-body": kill.KillerBodyId = 0; break;
                case "wrong-killer-profile": kill.KillerProfileId = "retired-local"; break;
                case "wrong-victim-body": kill.VictimBodyId = 21; break;
                case "not-local": kill.KilledByLocal = false; break;
                case "local-victim": kill.LocalIsVictim = true; break;
                case "ineligible": kill.AwardEligible = false; break;
                case "no-score": kill.ScoreEligible = false; break;
                case "friendly-flag": kill.VictimIsFriendly = true; break;
                case "wrong-killer-side": kill.KillerIsFriendly = false; break;
                case "expired": Sync(s.Match, 2, "replacement"); break;
                case "inactive": Sync(s.Match, 1, "enemy", alive: false); break;
                case "rebound": Sync(s.Match, 2, "new-profile", stable: "account"); break;
                case "dead-life":
                    s.Match.ReportKill(Kill("third", 30, "enemy", 20));
                    Sync(s.Match, 1, "enemy"); break;
                case "new-life":
                    FeedStreak(s.Match, body: 21, count: 1); kill.VictimBodyId = 21; break;
            }
            s.Match.ReportKill(kill);
            Check(!s.Labels.Contains("SHUTDOWN"), "shutdown rejects " + fault);
        }
        var aliases = New(); Sync(aliases.Match, 1, "enemy", stable: "account"); FeedStreak(aliases.Match);
        Sync(aliases.Match, 2, "successor", stable: "account");
        FeedStreak(aliases.Match, "enemy", 20, 10);
        FeedStreak(aliases.Match, "successor", 21, 1);
        aliases.Match.ReportKill(Kill(victim: "successor", victimBody: 21));
        Check(!aliases.Labels.Contains("SHUTDOWN"), "old alias scores never become successor life streak");
        var first = New(); FeedStreak(first.Match); first.Match.BeginAwardRound();
        first.Match.ReportKill(Kill(victim: "enemy", victimBody: 20));
        Check(first.Labels.LastOrDefault() == "FIRST BLOOD", "First Blood retains precedence over Shutdown");
    }
    private static void Clutch()
    {
        foreach (var win in new[] { ArenaAnnouncerCue.RoundWon, ArenaAnnouncerCue.MatchWon })
        {
            var s = New(new TacticalMode { IsTeamArena = true });
            s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
            s.Match.ReportTeamSurvivors(true, 1, 3); s.Match.ReportTeamSurvivors(true, 1, 1);
            s.Match.ReportTeamSurvivors(true, 1, 0);
            // Production Tactical ends the roster before its result event.
            s.Match.Scoreboard.MarkCurrentRosterInactive(); s.Match.ReportAnnouncerCue(win);
            Check(s.Cues.TakeLast(2).SequenceEqual(new[] { ArenaAnnouncerCue.Clutch, win }), "clutch immediately precedes preserved win lifecycle");
            Check(s.Cues.Count(c => c == ArenaAnnouncerCue.LastManStanding) == 1, "last man remains once per round");
            s.Match.ReportAnnouncerCue(win);
            Check(s.Cues.Count(c => c == ArenaAnnouncerCue.Clutch) == 1, "clutch consumed once");
            s.Match.ReportTeamSurvivors(true, 1, 1); s.Match.ReportTeamSurvivors(true, 1, 0);
            s.Match.ReportAnnouncerCue(win);
            Check(s.Cues.Count(c => c == ArenaAnnouncerCue.Clutch) == 1, "settled round cannot reacquire clutch");
            s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
            s.Match.ReportTeamSurvivors(true, 1, 2); s.Match.ReportTeamSurvivors(true, 1, 0);
            s.Match.ReportAnnouncerCue(win);
            Check(s.Cues.Count(c => c == ArenaAnnouncerCue.Clutch) == 2, "new round can earn a new clutch");
        }
        foreach (string fault in new[] { "solo", "other-mode", "no-opponents", "never-last", "death", "captured-death",
            "environmental-death", "respawn", "resurrection", "spectator", "stale-alive", "new-round", "loss", "draw", "ally-returned" })
        {
            IArenaMode mode = fault == "other-mode" ? new TestMode() : new TacticalMode { IsTeamArena = fault != "solo" };
            var s = New(mode); s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
            s.Match.ReportTeamSurvivors(true, fault == "never-last" ? 2 : 1, fault == "no-opponents" ? 0 : 2);
            switch (fault)
            {
                case "death": s.Match.ReportLocalDeath(); break;
                case "captured-death": s.Match.ReportKill(Kill("enemy", 20, "local", 1)); break;
                case "environmental-death":
                    var death = Kill("enemy", 20, "local", 1); death.AwardEligible = false;
                    s.Match.ReportKill(death); break;
                case "respawn": s.Match.ReportLocalRespawn(new Player { ProfileId = "local" }); break;
                case "resurrection":
                    s.Match.ReportLocalDeath(); s.Match.ReportLocalRespawn(new Player { ProfileId = "local" });
                    s.Match.ReportTeamSurvivors(true, 1, 1); break;
                case "spectator": s.Match.ReportTeamSurvivors(false, 1, 1); break;
                case "new-round": s.Match.BeginAwardRound(); break;
                case "loss": s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundLost); break;
                case "draw": s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.Draw); break;
                case "ally-returned": s.Match.ReportTeamSurvivors(true, 2, 1); break;
            }
            if (fault != "stale-alive") s.Match.ReportTeamSurvivors(true, 1, 0);
            s.Match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundWon);
            Check(!s.Cues.Contains(ArenaAnnouncerCue.Clutch), "clutch rejects " + fault);
            Check(s.Cues.Last() == ArenaAnnouncerCue.RoundWon, "ordinary win lifecycle survives " + fault);
        }
    }
    private static void Safety()
    {
        foreach (bool team in new[] { true, false })
        {
            TestMode mode = team ? new TeamDeathmatchMode() : new RoundsMode();
            var s = New(mode); s.Match.ReportKill(Kill("enemy", 20, "local", 1)); s.Match.ReportLocalDeath();
            Check(s.Match.Deaths == 1, "native kill then death remains one scored casualty");
            var reverseMode = team ? (TestMode)new TeamDeathmatchMode() : new RoundsMode();
            var reverse = New(reverseMode); reverse.Match.ReportLocalDeath(); reverse.Match.ReportKill(Kill("enemy", 20, "local", 1));
            Check(reverse.Match.Deaths == 1, "native death then kill remains one scored casualty");
        }
        foreach (IArenaMode mode in new IArenaMode[] { new TeamDeathmatchMode { AdmitScores = false }, new RoundsMode { AdmitScores = false } })
        {
            var s = New(mode); s.Match.ReportKill(Kill());
            Check(s.Match.Kills == 0 && s.Labels.Count == 0 && ((TestMode)mode).KillsSeen == 0, "closed scoring admission remains closed");
        }
        var friendly = New(new TeamDeathmatchMode { FriendlyKill = true }); friendly.Match.ReportKill(Kill());
        Check(friendly.Match.Score == 0 && friendly.Labels.Count == 0, "TDM classification precedes all awards");
        var throws = New(); throws.Match.OnCallout += _ => throw new Exception("presentation failure");
        throws.Match.ReportKill(Kill());
        Check(throws.Match.Kills == 1 && ((TestMode)throws.Match.Mode).KillsSeen == 1, "callout faults cannot suppress scores or mode callbacks");
        var outcome = New(new TacticalMode { IsTeamArena = true });
        outcome.Match.ReportTeamSurvivors(true, 1, 1); outcome.Match.ReportTeamSurvivors(true, 1, 0);
        outcome.Match.OnAnnouncerCue += cue => { if (cue == ArenaAnnouncerCue.Clutch) throw new Exception("clutch listener"); };
        outcome.Match.ReportAnnouncerCue(ArenaAnnouncerCue.MatchWon);
        Check(outcome.Cues.Last() == ArenaAnnouncerCue.MatchWon, "clutch listener fault cannot suppress original win");
    }
    public static void Main(string[] args)
    {
        string part = args.FirstOrDefault() ?? "all";
        if (part is "all" or "multi") Multi();
        if (part is "all" or "shutdown") Shutdown();
        if (part is "all" or "clutch") Clutch();
        if (part is "all" or "safety") Safety();
        if (part is "all" or "shot") ShotAwards();
        Check(_checks > 0, "requested test group exists");
        Console.WriteLine($"ANNOUNCER_COMBAT_PRODUCTION_PASSED: {_checks} assertions ({part})");
    }
}
