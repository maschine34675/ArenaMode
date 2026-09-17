using ArenaMode.Combat;
using ArenaMode.Modes;

internal static partial class Program
{
    private static ArenaKill Shot(bool airborne, bool bolt, float time = 100)
    {
        var kill = Kill(time: time);
        kill.AirborneHipFire = airborne;
        kill.BoltActionHipFire = bolt;
        kill.SingleHitHeadshot = kill.Headshot = true;
        return kill;
    }

    private static void ShotAwards()
    {
        foreach (var facts in new[] { (Air: true, Bolt: false, Label: "AIRBORNE"),
            (Air: false, Bolt: true, Label: "NO SCOPE"), (Air: true, Bolt: true, Label: "AIRBORNE") })
        {
            var s = New();
            s.Match.ReportKill(Kill("other", 30)); // consume First Blood without a local chain
            s.Match.ReportKill(Shot(facts.Air, facts.Bolt));
            Check(s.Labels.SequenceEqual(new[] { facts.Label }), "verified shot selects exactly one logical award: " + facts.Label);
            Check(s.Match.Kills == 1 && s.Match.Streak == 1 && s.Match.Score == 250,
                "shot awards preserve native headshot points without adding a bonus");
            var first = New(); first.Match.ReportKill(Shot(facts.Air, facts.Bolt));
            Check(first.Labels.SequenceEqual(new[] { "FIRST BLOOD" }), "First Blood precedes shot awards");
            var spree = New();
            for (int i = 1; i <= 4; i++) spree.Match.ReportKill(Kill(time: i * 10));
            spree.Match.ReportKill(Shot(facts.Air, facts.Bolt, 50));
            Check(spree.Labels.Last() == facts.Label, "shot award precedes simultaneous spree: " + facts.Label);
            Check(spree.Match.Score == 1150 && spree.Match.Kills == 5 && spree.Match.Streak == 5,
                "shot selection retains the existing five-kill score bonus");
            for (int chain = 2; chain <= 5; chain++)
            {
                var multi = New();
                for (int i = 1; i < chain; i++) multi.Match.ReportKill(Kill(time: i));
                multi.Match.ReportKill(Shot(facts.Air, facts.Bolt, chain));
                Check(multi.Labels.Last() == ScoreRules.MultiKillLabel(chain), "multi precedes shot award at chain " + chain);
            }
            var shutdown = New(); Sync(shutdown.Match, 1, "enemy"); FeedStreak(shutdown.Match);
            var stopped = Shot(facts.Air, facts.Bolt); stopped.VictimProfileId = "enemy"; stopped.VictimBodyId = 20;
            shutdown.Match.ReportKill(stopped);
            Check(shutdown.Labels.Last() == "SHUTDOWN", "Shutdown precedes shot awards");
            var payback = New();
            payback.Match.ReportKill(Kill("enemy", 20, "local", 1)); payback.Match.ReportLocalDeath();
            var revenge = Shot(facts.Air, facts.Bolt); revenge.VictimProfileId = "enemy"; revenge.VictimBodyId = 20;
            payback.Match.ReportKill(revenge);
            Check(payback.Labels.Last() == "PAYBACK", "Payback precedes shot awards");
        }
        foreach (string invalid in new[] { "not-local", "local-victim", "ineligible", "not-scored", "tdm-friendly", "closed" })
        {
            var mode = new TeamDeathmatchMode { FriendlyKill = invalid == "tdm-friendly", AdmitScores = invalid != "closed" };
            var s = New(mode); s.Match.ReportKill(Kill("other", 30));
            var shot = Shot(true, true);
            if (invalid == "not-local") shot.KilledByLocal = false;
            if (invalid == "local-victim") shot.LocalIsVictim = true;
            if (invalid == "ineligible") shot.AwardEligible = false;
            if (invalid == "not-scored") shot.ScoreEligible = false;
            s.Match.ReportKill(shot);
            Check(!s.Labels.Contains("AIRBORNE") && !s.Labels.Contains("NO SCOPE"), "shot awards retain admission guard: " + invalid);
        }
        var unknown = New(); unknown.Match.ReportKill(Kill("other", 30)); unknown.Match.ReportKill(Shot(false, false));
        Check(unknown.Labels.SequenceEqual(new[] { "HEADSHOT" }), "unknown shot-time facts retain ordinary headshot fallback");
    }
}
