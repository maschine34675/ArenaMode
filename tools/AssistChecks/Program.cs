using ArenaMode.Combat;
using ArenaMode.Modes;

// Real match/scoreboard/awards/rules compiled by this project. Only native clock,
// player identity and the unrelated mode implementations are explicit doubles.
internal static class Program
{
    private static int _checks;
    private static void Main(string[] args)
    {
        try
        {
            string suite = args.FirstOrDefault() ?? "all";
            if (suite is "all" or "shares") Shares();
            if (suite is "all" or "eligibility") Eligibility();
            if (suite is "all" or "identity") Identity();
            if (suite is "all" or "lifecycle") Lifecycle();
            if (suite is "all" or "events") Events();
            if (suite is "all" or "bounds") Bounds();
            Console.WriteLine($"ASSIST_PRODUCTION_PASSED: {_checks} assertions ({suite})");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) throw new Exception("FAILED: " + label);
    }
    private static void Equal<T>(T actual, T expected, string label)
        => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{label} (actual {actual}; expected {expected})");
    private static ArenaScoreboardActor Actor(string id, string stable = null, bool alive = true)
        => new(id, id, "PMC", alive, stable);
    private static ArenaMatch New(IArenaMode mode = null)
    {
        var match = new ArenaMatch(mode ?? new TestMode(), "local-1", "You", "PMC");
        Roster(match, 1, "local-1", new[] { Actor("ally", "ally-account") },
            new[] { Actor("killer"), Actor("victim"), Actor("enemy-helper") });
        return match;
    }
    private static void Roster(ArenaMatch match, int epoch, string local,
        IReadOnlyList<ArenaScoreboardActor> friends, IReadOnlyList<ArenaScoreboardActor> enemies)
        => Check(match.Scoreboard.TrySynchronizeRoster(epoch, new(local, "You", "PMC", true), friends, enemies, out var problem),
            "roster admitted: " + problem);
    private static ArenaScoreboardEntry Row(ArenaMatch match, string key)
        => match.Scoreboard.Standings(null, false, 10000, false).Single(entry => entry.Key == key);
    private static ArenaAssistDamage Damage(string attacker, double hp, long victim = 100, long body = 10,
        bool local = false, bool? friendly = null)
        => new()
        {
            VictimBodyId = victim, AttackerBodyId = body, AttackerProfileId = attacker,
            AttackerIsLocal = local,
            AttackerIsFriendly = friendly, Damage = hp,
        };
    private static ArenaKill Kill(string killer = "killer", string victim = "victim", long victimBody = 100,
        long killerBody = 20, bool local = false, ArenaRole role = ArenaRole.Pmc)
        => new()
        {
            KillerProfileId = killer, KillerName = killer, KillerRoleLabel = "PMC",
            VictimProfileId = victim, VictimName = victim, VictimRoleLabel = "PMC", VictimRole = role,
            KilledByLocal = local, LocalIsVictim = victim.StartsWith("local-", StringComparison.Ordinal),
            KillerBodyId = killerBody, VictimBodyId = victimBody, AwardEligible = true,
            ScoreEligible = true, Time = 1,
        };
    private static void Contribute(ArenaMatch match, string who, double hp, long victim = 100,
        long body = 10, bool local = false, bool? friendly = null)
        => match.RecordAssistDamage(Damage(who, hp, victim, body, local, friendly));

    private static void Shares()
    {
        var match = New();
        Contribute(match, "local-1", 30, local: true);
        Contribute(match, "killer", 70, body: 20);
        var kill = Kill(); match.ReportKill(kill);
        Equal(match.Score, 60, "local assist receives proportional PMC base points");
        Equal(match.Assists, 1, "local assist count");
        Equal(match.Kills, 0, "assist is not a kill");
        Equal(match.Streak, 0, "assist is not a streak");
        Equal(match.Headshots, 0, "assist is not a headshot");
        Equal(Row(match, "body:killer").Score, 200, "killer keeps full reward");
        Equal(Row(match, "body:killer").Assists, 0, "killer never assists own kill");
        Equal(kill.Assists.Count, 1, "one plain assist in final kill payload");
        var assist = kill.Assists.Single();
        Equal(assist.Points, 60, "DTO points");
        Check(Math.Abs(assist.DamageShare - .3) < 1e-9, "DTO fraction");
        Check(assist.IsLocal, "DTO local identity");
        Equal(assist.ContributorKey, "local", "DTO logical key");
        Equal(assist.DisplayName, "You", "DTO safe scoreboard display");
        Equal(assist.VictimBodyId, 100L, "DTO physical victim");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Assists, 1, "team assist total");

        match = New();
        Contribute(match, "local-1", 1, local: true);
        Contribute(match, "ally", 1, body: 11);
        Contribute(match, "enemy-helper", 1, body: 12);
        Contribute(match, "killer", 3, body: 20);
        kill = Kill(); kill.Headshot = true; match.ReportKill(kill);
        Equal(match.Score, 33, "shares floor rather than round up");
        Equal(Row(match, "stable:ally-account").Score, 33, "second helper own share");
        Equal(Row(match, "body:enemy-helper").Score, 33, "FFA helper allowed despite same relative side as victim");
        Equal(Row(match, "body:killer").Score, 250, "headshot bonus belongs only to killer");
        Equal(kill.Assists.Count, 3, "all qualifying helper rows settled");

        foreach (var role in Enum.GetValues<ArenaRole>())
        {
            match = New(); Contribute(match, "local-1", 5, local: true);
            Contribute(match, "killer", 5, body: 20); match.ReportKill(Kill(role: role));
            Equal(match.Score, ScoreRules.PointsFor(role) / 2, "assist uses role base: " + role);
        }

        match = New(); Contribute(match, "local-1", .1, local: true);
        Contribute(match, "killer", 99.9, body: 20); kill = Kill(); match.ReportKill(kill);
        Equal(match.Score, 0, "sub-point contribution earns zero");
        Equal(match.Assists, 0, "zero points is not an assist");
        Equal(kill.Assists.Count, 0, "zero points omitted from payload");

        match = New(); Contribute(match, "local-1", 50, local: true);
        // Native reports losses, not net remaining health. Healing cannot remove
        // the earlier 50 HP and is never a negative contribution to this ledger.
        Contribute(match, "local-1", -50, local: true);
        Contribute(match, null, 50, body: 0);
        Contribute(match, "untracked", 50, body: 40);
        Contribute(match, "killer", 50, body: 20); kill = Kill(); match.ReportKill(kill);
        Equal(match.Score, 50, "healing ignored and unknown losses remain in denominator");
        Equal(kill.Assists.Count, 1, "unknown attackers never invented");
        Equal(match.Scoreboard.HistoricalParticipantCount, 5, "damage creates no new score identity");
    }

    private static void Eligibility()
    {
        foreach (string condition in new[] { "score-ineligible", "award-ineligible", "friendly", "self-body", "no-body" })
        {
            var mode = condition == "friendly" ? new TeamDeathmatchMode { FriendlyKill = true } : new TestMode();
            var match = New(mode); Contribute(match, "local-1", 90, local: true);
            Contribute(match, "killer", 10, body: 20); var kill = Kill();
            if (condition == "score-ineligible") kill.ScoreEligible = false;
            if (condition == "award-ineligible") kill.AwardEligible = false;
            if (condition == "self-body") kill.KillerBodyId = kill.VictimBodyId;
            if (condition == "no-body") kill.VictimBodyId = 0;
            match.ReportKill(kill);
            Equal(match.Assists, 0, "ineligible casualty gives no assist: " + condition);
            Equal(kill.Assists.Count, 0, "ineligible payload empty: " + condition);
        }
        var m = New(); Contribute(m, "local-1", 60, local: true);
        Contribute(m, "killer", 40, body: 20); var unscored = Kill();
        unscored.AwardEligible = false; m.ReportKill(unscored);
        m.ReportKill(Kill()); Equal(m.Assists, 0, "ineligible death consumes ledger before repeated eligible callback");

        m = New(); Contribute(m, "local-1", 60, local: true); Contribute(m, "killer", 40, body: 20);
        var first = Kill(); m.ReportKill(first); m.ReportKill(first); m.ReportKill(Kill());
        Equal(m.Assists, 1, "duplicate callbacks cannot duplicate assists");
        Equal(m.Score, 120, "duplicate assist score settled once");

        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -1d })
        {
            m = New(); Contribute(m, "local-1", invalid, local: true);
            Contribute(m, "killer", 100, body: 20); m.ReportKill(Kill());
            Equal(m.Assists, 0, "invalid loss cannot contribute: " + invalid);
        }
        foreach (var data in new[]
        {
            Damage("local-1", 50, body: 0, local: true),
            Damage("local-1", 50, body: 100, local: true),
            Damage("local-1", 50, victim: 0, local: true),
            Damage("untracked", 50, local: true),
            Damage("killer", 50, local: true),
            Damage("local-1", 50, local: false),
        })
        {
            m = New(); m.RecordAssistDamage(data); Contribute(m, "killer", 50, body: 20);
            m.ReportKill(Kill()); Equal(m.Assists, 0, "invalid/contradictory damage identity cannot force local assist");
        }
    }

    private static void Identity()
    {
        var match = New(); Contribute(match, "local-1", 50, local: true);
        match.ReportLocalDeath(); match.ReportLocalRespawn(new EFT.Player { ProfileId = "local-2" });
        Contribute(match, "killer", 50, body: 20); match.ReportKill(Kill());
        Equal(match.Score, 100, "helper death and local respawn preserve damage to living victim");
        Equal(match.Assists, 1, "assist belongs to logical local after physical respawn");
        Equal(match.Deaths, 1, "assist causes no extra death");

        match = New(); Contribute(match, "ally", 40, body: 11);
        Roster(match, 2, "local-1", new[] { Actor("ally-2", "ally-account") }, new[] { Actor("killer"), Actor("victim") });
        Contribute(match, "ally-2", 20, body: 12); Contribute(match, "killer", 40, body: 20);
        var kill = Kill(); match.ReportKill(kill);
        Equal(Row(match, "stable:ally-account").Assists, 1, "PFT aliases fold into one logical assist");
        Equal(Row(match, "stable:ally-account").Score, 120, "PFT damage survives replacement body");
        Equal(kill.Assists.Count, 1, "one stable contributor payload across aliases");

        match = New(); Contribute(match, "ally", 50, body: 11);
        Roster(match, 2, "local-1", new[] { Actor("ally-2", "ally-account") }, new[] { Actor("victim") });
        Contribute(match, "ally-2", 50, body: 12);
        match.ReportKill(Kill("ally-2", killerBody: 12));
        Equal(Row(match, "stable:ally-account").Assists, 0, "new PFT body landing kill excludes older same-account contribution");
        Equal(Row(match, "stable:ally-account").Score, 200, "stable killer earns only full kill reward");

        match = ArchivedHelper();
        int before = match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Score;
        Contribute(match, "killer", 50, body: 20); match.ReportKill(Kill());
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Score, before + 300,
            "archived helper receives exactly one archived score increment");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Assists, 1,
            "archived helper assist remains in team total");

        match = ArchivedHelper();
        // A later grenade can finish the same victim after the original ordinary
        // contributor row was compacted. Resolve recreates the same logical key.
        Contribute(match, null, 50, body: 0);
        kill = Kill("enemy-helper", killerBody: 11); match.ReportKill(kill);
        Equal(kill.Assists.Count, 0, "archived final killer cannot assist newly resolved same logical key");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Assists, 0,
            "recreated final killer earns no archived self-assist");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Score, 200,
            "recreated final killer receives exactly the unchanged kill reward");

        match = ArchivedHelper();
        match.ReportKill(Kill("enemy-helper", "separate-victim", victimBody: 200, killerBody: 11));
        Contribute(match, "enemy-helper", 50, body: 11); Contribute(match, "killer", 50, body: 20);
        kill = Kill(); match.ReportKill(kill);
        Equal(kill.Assists.Count, 1, "contributions before and after row recreation coalesce by logical key");
        Equal(kill.Assists.Single().Points, 133, "recreated logical helper receives one rounding of aggregate damage");
        Equal(Row(match, "body:enemy-helper").Assists, 1, "recreated current helper row receives exactly one assist");
        Equal(Row(match, "body:enemy-helper").Score, 333, "recreated row receives full aggregate assist once");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Score, 533,
            "recreated helper aggregate credited once across archive and current totals");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Assists, 1,
            "recreated helper aggregate produces one team assist");
    }

    private static ArenaMatch ArchivedHelper()
    {
        var match = new ArenaMatch(new TestMode(), "local-1", "You", "PMC");
        var original = Enumerable.Range(1, 20).Select(i => Actor("old-" + i)).ToList();
        original.Add(Actor("killer")); original.Add(Actor("victim")); original.Add(Actor("enemy-helper"));
        Roster(match, 1, "local-1", Array.Empty<ArenaScoreboardActor>(), original);
        Contribute(match, "enemy-helper", 50, body: 11);
        // Zero-score row is outside both the oldest top-tie group and recent runs.
        for (int i = 0; i < 90; i++)
            Roster(match, i + 2, "local-1", Array.Empty<ArenaScoreboardActor>(),
                new[] { Actor("killer"), Actor("victim"), Actor("filler-" + i) });
        Check(match.Scoreboard.ArchivedParticipantCount > 0, "ordinary history actually compacted");
        Check(!match.Scoreboard.Standings(null, false, 10000, false).Any(r => r.Key == "body:enemy-helper"),
            "pending ordinary helper no longer retained as visible row");
        return match;
    }

    private static void Lifecycle()
    {
        foreach (string boundary in new[] { "round", "end", "forget" })
        {
            var match = New(); Contribute(match, "local-1", 50, local: true);
            if (boundary == "round") match.BeginAwardRound();
            if (boundary == "end") match.End();
            if (boundary == "forget") match.ForgetAssistVictim(100);
            Contribute(match, "killer", 50, body: 20); match.ReportKill(Kill());
            Equal(match.Assists, 0, "assist ledger cleared at " + boundary);
        }
        var m = New(); Contribute(m, "local-1", 40, victim: 100, local: true);
        Contribute(m, "local-1", 75, victim: 101, local: true);
        m.ForgetAssistVictim(100); Contribute(m, "killer", 25, victim: 101, body: 20);
        m.ReportKill(Kill(victimBody: 101)); Equal(m.Score, 150, "forget targets only exact physical victim");

        m = New(); Contribute(m, "local-1", 50, local: true); Contribute(m, "killer", 50, body: 20);
        m.ReportKill(Kill()); m.BeginAwardRound();
        Roster(m, 2, "local-1", new[] { Actor("ally", "ally-account") }, new[] { Actor("killer"), Actor("victim") });
        Contribute(m, "local-1", 25, local: true); Contribute(m, "killer", 75, body: 20); m.ReportKill(Kill());
        Equal(m.Score, 150, "round reset allows fresh damage without erasing match score");
        Equal(m.Assists, 2, "match assist total survives round boundary");

        foreach (var mode in new TestMode[] { new TeamDeathmatchMode { AdmitScores = false }, new RoundsMode { AdmitScores = false } })
        {
            m = New(mode); Contribute(m, "local-1", 50, local: true); Contribute(m, "killer", 50, body: 20);
            m.ReportKill(Kill()); Equal(m.Assists, 0, "staging casualty cannot create assist");
            Equal(mode.KillsSeen, 0, "staging casualty does not reach mode");
            if (mode is TeamDeathmatchMode teams) teams.AdmitScores = true;
            if (mode is RoundsMode rounds) rounds.AdmitScores = true;
            m.ReportKill(Kill()); Equal(m.Assists, 0, "rejected staging victim cannot pay after phase changes");
        }
    }

    private static void Events()
    {
        var mode = new TestMode(); var match = New(mode);
        int assists = 0, scores = 0, kills = 0, callouts = 0, cues = 0, texts = 0;
        match.OnAssist += a => { assists++; Equal(a.Points, 80, "assist event settled payload"); };
        match.OnScoreChanged += () => scores++;
        match.OnKill += k => { kills++; Equal(k.Assists.Count, 1, "OnKill sees already-settled assists"); };
        match.OnCallout += _ => callouts++; match.OnTextCallout += _ => texts++;
        match.OnAnnouncerCue += _ => cues++;
        Contribute(match, "local-1", 40, local: true); Contribute(match, "killer", 60, body: 20);
        match.ReportKill(Kill());
        Equal(assists, 1, "exactly one assist event"); Equal(scores, 1, "local assist refreshes points once");
        Equal(kills, 1, "assist does not synthesize OnKill"); Equal(mode.KillsSeen, 1, "assist does not synthesize mode kill");
        Equal(callouts, 0, "assist does not announce kill award"); Equal(cues, 0, "assist does not synthesize audio cue");
        Equal(texts, 0, "assist does not synthesize taunt");

        mode = new TestMode(); match = New(mode); scores = kills = 0;
        match.OnAssist += _ => throw new InvalidOperationException("deliberate presentation failure");
        match.OnScoreChanged += () => scores++; match.OnKill += _ => kills++;
        Contribute(match, "local-1", 50, local: true); Contribute(match, "killer", 50, body: 20);
        match.ReportKill(Kill()); Equal(match.Score, 100, "throwing assist listener cannot undo score");
        Equal(scores, 1, "throwing assist listener cannot prevent score refresh");
        Equal(kills, 1, "throwing assist listener cannot prevent real kill feed");
        Equal(mode.KillsSeen, 1, "throwing assist listener cannot prevent real mode callback");

        match = New(); scores = 0; match.OnScoreChanged += () => scores++;
        Contribute(match, "ally", 50, body: 11); Contribute(match, "killer", 50, body: 20);
        match.ReportKill(Kill()); Equal(scores, 0, "nonlocal assist does not pretend local score changed");
    }

    private static void Bounds()
    {
        var match = New();
        for (int i = 1; i <= 129; i++) Contribute(match, "local-1", 50, victim: 1000 + i, local: true);
        Contribute(match, "killer", 50, victim: 1129, body: 20); match.ReportKill(Kill(victimBody: 1129));
        Equal(match.Assists, 0, "victim overflow fails closed rather than awarding incomplete fraction");
        match.BeginAwardRound(); Contribute(match, "local-1", 50, local: true);
        Contribute(match, "killer", 50, body: 20); match.ReportKill(Kill());
        Equal(match.Assists, 1, "round reset recovers bounded ledger");

        match = New();
        var enemies = Enumerable.Range(1, 129).Select(i => Actor("helper-" + i)).ToList();
        enemies.Add(Actor("victim")); enemies.Add(Actor("killer"));
        Roster(match, 2, "local-1", Array.Empty<ArenaScoreboardActor>(), enemies);
        for (int i = 1; i <= 129; i++) Contribute(match, "helper-" + i, 10, body: 1000 + i);
        Contribute(match, "killer", 10, body: 20); var kill = Kill(); match.ReportKill(kill);
        Equal(kill.Assists.Count, 0, "contributor overflow discards incomplete victim ledger");
        Equal(match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Assists, 0, "overflow creates no archived or active assists");
    }
}
