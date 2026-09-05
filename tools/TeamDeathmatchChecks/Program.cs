using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;

internal static class Program
{
    private static int _checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void CapacityAndLoading()
    {
        foreach (bool localUsec in new[] { true, false })
        foreach (int configured in new[] { int.MinValue, 1, 2, 5, 12, 13, int.MaxValue })
        {
            int size = Math.Clamp(configured, 2, 12);
            var population = new TeamDeathmatchPopulation(configured, localUsec);
            Check(population.TeamSize == size && population.LocalUsec == localUsec,
                "team capacity clamps to 2-12 for both local factions");
            Check(population.BotTarget(localUsec) == size - 1 && population.BotTarget(!localUsec) == size,
                "human slot is reserved on its actual PMC side");
            foreach (bool usec in new[] { true, false })
            {
                int target = population.BotTarget(usec);
                Check(!population.TryReserve(usec, -1, 0f, out var invalid) && invalid == null,
                    "unknown negative occupancy cannot admit a request");
                Check(!population.TryReserve(usec, target, 0f, out var full) && full == null,
                    "full team occupancy prevents overfill");
                Check(!population.TryReserve(usec, target + 1, 0f, out _),
                    "overfull team occupancy prevents another request");
                Check(population.TryReserve(usec, target - 1, 0f, out var loading),
                    "one vacant team slot can reserve a profile load");
                Check(loading.Usec == usec && population.IsCurrent(loading, 0f),
                    "new request carries its own faction and valid admission");
                Check(!population.TryReserve(usec, 0, 1f, out _),
                    "only one profile load may assemble per faction");
                Check(!population.IsCurrent(loading, -1f),
                    "a callback from before ticket creation cannot be admitted");
                Check(population.IsCurrent(loading, 44.99f)
                      && !population.IsCurrent(loading, TeamDeathmatchPopulation.RequestTimeout),
                    "ticket admission expires exactly at its bounded deadline");
                Check(!population.TryReserve(usec, 0, 90f, out _),
                    "expiry does not start a concurrent profile assembly");
                Check(population.IsStalled(90f), "unreturned expired loading is reported stalled");
                population.DispatchFinished(loading, handedToNative: false);
                Check(!population.IsCurrent(loading, 1f), "terminal failure permanently invalidates its ticket");
                Check(population.TryReserve(usec, target - 1, 91f, out var next),
                    "confirmed terminal failure releases the faction load slot");
                Check(next.Id > loading.Id, "replacement requests use distinct monotonic identities");
                population.Settle(next);
            }

            // The policy has no alive/dead human count: the bot target remains
            // fixed throughout death/respawn, so a human cannot be replaced by AI.
            var board = Board(localUsec);
            board.ReportLocalDeath();
            Check(!board.Local.IsActive && population.BotTarget(localUsec) == size - 1,
                "human slot remains reserved while its scoreboard body is dead");
            Check(!population.TryReserve(localUsec, size - 1, 100f, out _),
                "player death does not open an extra friendly bot slot");
            board.ReportLocalRespawn("local", "You", localUsec ? "USEC" : "BEAR");
            Check(population.BotTarget(localUsec) == size - 1 && population.OutstandingCount == 0,
                "local respawn preserves capacity and does not retain settled requests");
        }
    }

    private static void BoundedDebtAndIndependentFactions()
    {
        var population = new TeamDeathmatchPopulation(5, true);
        var usecTickets = new List<TeamDeathmatchPopulation.Ticket>();
        var bearTickets = new List<TeamDeathmatchPopulation.Ticket>();
        for (int index = 0; index < 3; index++)
        {
            float now = index * TeamDeathmatchPopulation.RequestTimeout;
            foreach (bool usec in new[] { true, false })
            {
                Check(population.TryReserve(usec, 0, now, out var ticket),
                    "an expired dispatched request permits a bounded replacement per faction");
                (usec ? usecTickets : bearTickets).Add(ticket);
                population.DispatchFinished(ticket, handedToNative: true);
                Check(!population.TryReserve(usec, 0, now + 1f, out _),
                    "a dispatched but current request still owns the faction admission slot");
            }
        }
        Check(population.OutstandingCount == 6, "uncertain native work is bounded to three debts per faction");
        Check(!population.TryReserve(true, 0, 999f, out _)
              && !population.TryReserve(false, 0, 999f, out _),
            "three uncertain dispatched requests pause further growth");
        Check(usecTickets.All(ticket => !population.IsCurrent(ticket, 999f)),
            "late success callbacks cannot reuse expired admission");
        population.Settle(usecTickets[1]);
        Check(population.TryReserve(true, 0, 999f, out var retry),
            "a settled uncertainty releases only its own faction debt");
        Check(!population.TryReserve(false, 0, 999f, out _),
            "settling USEC work cannot release BEAR debt");
        population.DispatchFinished(retry, handedToNative: false);
        Check(population.OutstandingCount == 5, "a refusal before native dispatch carries no uncertain debt");

        var foreign = new TeamDeathmatchPopulation.Ticket(usecTickets[0].Id, true, 0f);
        Check(!population.IsCurrent(foreign, 1f), "matching numeric id does not forge a ticket reference");
        population.Settle(foreign);
        Check(population.OutstandingCount == 5, "foreign settlement cannot remove real outstanding work");
        population.Stop();
        Check(population.OutstandingCount == 0 && !population.IsStalled(999f),
            "stopping releases match-owned debt and its stalled indication");
        Check(!population.IsCurrent(usecTickets[0], 1f)
              && !population.TryReserve(true, 0, 1000f, out _)
              && !population.TryReserve(false, 0, 1000f, out _),
            "Stop invalidates callbacks and prohibits every later reservation");
        population.DispatchFinished(usecTickets[0], handedToNative: true);
        population.Settle(usecTickets[0]);
        population.Stop();
        Check(population.OutstandingCount == 0, "late callbacks and repeated stop cannot resurrect debt");

        var nextMatch = new TeamDeathmatchPopulation(5, true);
        Check(nextMatch.TryReserve(true, 0, 0f, out var nextMatchTicket), "next match starts with fresh capacity");
        Check(nextMatchTicket.Id == usecTickets[0].Id && !nextMatch.IsCurrent(usecTickets[0], 1f),
            "same-number tickets from an old match remain invalid in the next one");
    }

    private static void SynchronousCompletion()
    {
        var population = new TeamDeathmatchPopulation(2, false);
        Check(population.TryReserve(true, 0, 5f, out var ticket), "synchronous dispatch has a reserved request");
        population.Settle(ticket);
        population.DispatchFinished(ticket, handedToNative: true);
        Check(population.OutstandingCount == 0 && !population.IsCurrent(ticket, 6f),
            "synchronous settle before DispatchFinished cannot resurrect a ticket");
        Check(population.TryReserve(true, 1, 6f, out _),
            "synchronous terminal callback releases the next vacant slot");
        Check(population.TryReserve(false, 0, 6f, out _),
            "one faction's load does not block the independent other faction");
        population.DispatchFinished(null, handedToNative: true);
        population.Settle(null);
        Check(population.OutstandingCount == 2 && !population.IsCurrent(null, 6f),
            "missing callbacks cannot consume unrelated reservations");
    }

    private static void DispatchOwnership()
    {
        ActiveRaid();
        var spawner = new BotSpawner();
        var data = new BotCreationData();
        Check(TeamDeathmatchSpawnDispatch.IsActive
              && !TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
            "active exact Team DM rejects unowned external bot dispatches");
        using (var outer = TeamDeathmatchSpawnDispatch.Enter(spawner, data))
        {
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(new BotSpawner(), data)
                  && !TeamDeathmatchSpawnDispatch.CanDispatch(spawner, new BotCreationData()),
                "dispatch ownership uses exact spawner and creation-data references");
            TeamDeathmatchSpawnDispatch.MarkRejected(spawner, new BotCreationData());
            Check(!outer.WasRejected, "a foreign rejection cannot poison the owned dispatch");
            bool anotherThreadAdmitted = true;
            var thread = new Thread(() => anotherThreadAdmitted = TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data));
            thread.Start();
            thread.Join();
            Check(!anotherThreadAdmitted, "synchronous ownership does not flow to another thread");

            var nestedData = new BotCreationData();
            using (var nested = TeamDeathmatchSpawnDispatch.Enter(spawner, nestedData))
            {
                Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                    "nested ownership cannot consume the outer dispatch");
                Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, nestedData)
                      && !TeamDeathmatchSpawnDispatch.CanDispatch(spawner, nestedData),
                    "one lease authorizes exactly one native dispatch");
                TeamDeathmatchSpawnDispatch.MarkRejected(spawner, nestedData);
                Check(nested.WasRejected && !outer.WasRejected,
                    "nested point-filter rejection remains local to the nested lease");
            }
            Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "normal nested unwind restores an unconsumed outer lease");
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "restored parent ownership cannot dispatch a second time");
        }
        Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "disposed scopes do not leak dispatch permission");
        using (TeamDeathmatchSpawnDispatch.Enter(spawner, data))
        {
            var skippedParent = TeamDeathmatchSpawnDispatch.Enter(spawner, new BotCreationData());
            var innermost = TeamDeathmatchSpawnDispatch.Enter(spawner, new BotCreationData());
            skippedParent.Dispose();
            innermost.Dispose();
            Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "out-of-order disposal skips closed ancestors and restores the live grandparent");
        }
        var explicitLease = TeamDeathmatchSpawnDispatch.Enter(spawner, data);
        explicitLease.Dispose();
        explicitLease.Dispose();
        Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "idempotent disposal does not resurrect an older scope");

        try
        {
            using var lease = TeamDeathmatchSpawnDispatch.Enter(spawner, data);
            throw new Exception("simulated native dispatch failure");
        }
        catch (Exception) { }
        Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "exceptional unwind clears synchronous permission");

        using (TeamDeathmatchSpawnDispatch.Enter(spawner, data))
        {
            ActiveRaid();
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "a new exact raid cannot reuse a prior world's dispatch lease");
        }
        using (TeamDeathmatchSpawnDispatch.Enter(spawner, data))
        {
            ArenaSession.OwnedWorld = Singleton<GameWorld>.Instance = new GameWorld();
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "world-only replacement invalidates a prior lease even with the same game");
        }
        using (TeamDeathmatchSpawnDispatch.Enter(spawner, data))
        {
            ArenaSession.OwnedGame = Singleton<AbstractGame>.Instance = new AbstractGame();
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "game-only replacement invalidates a prior lease even with the same world");
        }

        using (TeamDeathmatchSpawnDispatch.Enter(null, data))
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(null, data), "null spawner cannot acquire native dispatch authority");
        using (TeamDeathmatchSpawnDispatch.Enter(spawner, null))
            Check(!TeamDeathmatchSpawnDispatch.CanDispatch(spawner, null), "null creation data cannot acquire dispatch authority");

        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical", null })
        {
            ActiveRaid(mode);
            Check(!TeamDeathmatchSpawnDispatch.IsActive && TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
                "ordinary Arena modes preserve native dispatch behavior");
        }
        ActiveRaid();
        ArenaSession.IsActive = false;
        Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "ordinary raids fail open to native dispatch");
        ActiveRaid();
        Singleton<GameWorld>.Instance = new GameWorld();
        Check(!TeamDeathmatchSpawnDispatch.IsActive && TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
            "stale active-world association cannot block a foreign world's bots");
        ActiveRaid();
        Singleton<AbstractGame>.Instance = new AbstractGame();
        Check(!TeamDeathmatchSpawnDispatch.IsActive && TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data),
            "stale active-game association cannot block a foreign game's bots");
        ActiveRaid();
        Singleton<GameWorld>.Instance = null;
        Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "missing world preserves native dispatch");
        ActiveRaid();
        Singleton<AbstractGame>.Instance = null;
        Check(TeamDeathmatchSpawnDispatch.CanDispatch(spawner, data), "missing game preserves native dispatch");
    }

    private static void CapturedFactionScoresAndAwards()
    {
        foreach (bool localUsec in new[] { true, false })
        {
            var board = Board(localUsec);
            // Capture classification must work before the next roster snapshot.
            board.ReportKill(Kill("ally", "enemy", friendlyKiller: true, friendlyVictim: false));
            Check(Row(board, "ally").Side == ArenaScoreboardSide.Friendly
                  && Row(board, "enemy").Side == ArenaScoreboardSide.Opponent,
                "capture-time faction metadata classifies newly observed bot bodies");
            Check(board.Totals(ArenaScoreboardSide.Friendly).Score == 200
                  && board.Totals(ArenaScoreboardSide.Opponent).Deaths == 1,
                "allied bot points contribute to the friendly faction total");
            board.ReportKill(Kill("enemy-2", "ally", friendlyKiller: false, friendlyVictim: true));
            Check(board.Totals(ArenaScoreboardSide.Opponent).Score == 200
                  && board.Totals(ArenaScoreboardSide.Friendly).Deaths == 1,
                "opposing bot points and allied deaths reach their own faction totals");

            var awards = new ArenaCombatAwards();
            var friendlyKill = Kill("local", "friendly-fire-victim", true, true, scoreEligible: false, headshot: true);
            friendlyKill.AwardEligible = false;
            friendlyKill.KillerBodyId = 1;
            friendlyKill.SingleHitHeadshot = true;
            friendlyKill.VictimBodyId = 100;
            int beforeFriendlyScore = board.Totals(ArenaScoreboardSide.Friendly).Score;
            board.ReportKill(friendlyKill);
            Check(board.Local.Score == 0 && board.Local.Kills == 0 && board.Local.Streak == 0
                  && board.Local.Headshots == 0 && board.Local.BestStreak == 0,
                "friendly kills earn no points, kills, headshots or streak bonuses");
            Check(board.Totals(ArenaScoreboardSide.Friendly).Score == beforeFriendlyScore
                  && Row(board, "friendly-fire-victim").Deaths == 1,
                "friendly-fire death remains visible without awarding its killer");
            Check(awards.ReportKill(friendlyKill, "DOUBLE KILL") == null,
                "captured friendly-kill exclusion suppresses headshot and announcer awards");

            var enemyKill = Kill("local", "enemy-3", true, false, headshot: true);
            enemyKill.AwardEligible = true;
            enemyKill.VictimBodyId = 101;
            Check(awards.ReportKill(enemyKill, null) == "FIRST BLOOD",
                "a suppressed team kill cannot consume the next legitimate First Blood");
            board.ReportKill(enemyKill);
            Check(board.Local.Score == 250 && board.Local.Kills == 1 && board.Local.Headshots == 1,
                "eligible enemy kill preserves normal PMC and headshot points");
            board.ReportKill(Kill("local", "another-friendly-victim", true, true, scoreEligible: false, headshot: true));
            Check(board.Local.Score == 250 && board.Local.Kills == 1 && board.Local.Streak == 1
                  && board.Local.Headshots == 1 && board.Local.BestStreak == 1,
                "a later suppressed friendly kill cannot change an existing personal run");
            board.ReportKill(Kill("enemy-2", "another-opponent-victim", false, false, scoreEligible: false, headshot: true));
            Check(Row(board, "enemy-2").Score == 200 && Row(board, "enemy-2").Kills == 1
                  && Row(board, "another-opponent-victim").Deaths == 1,
                "the opposing faction also receives no score for killing one of its own");
            var localDeath = Kill("enemy-2", "local", false, true);
            board.ReportKill(localDeath);
            Check(board.Local.Deaths == 0, "kill capture defers local death to the respawn event");
            board.ReportLocalDeath();
            Check(board.Local.Deaths == 1 && board.Local.Streak == 0 && !board.Local.IsActive,
                "native local-death event counts exactly once and resets the local streak");
            board.ReportLocalRespawn("local-new-body", "You", localUsec ? "USEC" : "BEAR");
            Check(board.Local.Score == 250 && board.Local.Kills == 1 && board.Local.Deaths == 1 && board.Local.IsActive,
                "local respawn retains its match-long personal identity and score");
        }

        var legacy = Board(true);
        var oldModeKill = Kill("unclassified-killer", "unclassified-victim", null, null);
        Check(oldModeKill.ScoreEligible, "ArenaKill defaults to eligible for unchanged older mode callers");
        legacy.ReportKill(oldModeKill);
        Check(legacy.Totals(ArenaScoreboardSide.Opponent).Score == 200
              && legacy.Totals(ArenaScoreboardSide.Friendly).Score == 0,
            "absent optional classification retains the existing opponent fallback");
        int count = legacy.ParticipantCount;
        int version = legacy.Version;
        legacy.ReportKill(Kill("administrative-killer", "administrative-victim", null, null, scoreEligible: false));
        Check(legacy.ParticipantCount == count && legacy.Version == version,
            "unclassified ineligible events cannot invent scoreboard participants");
    }

    private static void LongMatchFactionTotals()
    {
        const int lives = 1200;
        foreach (bool localUsec in new[] { true, false })
        {
            var board = Board(localUsec);
            int expectedFriendly = 0;
            int expectedOpponent = 0;
            int friendlyHeads = 0;
            int opponentHeads = 0;
            for (int life = 1; life <= lives; life++)
            {
                string ally = "ally-" + life;
                string victim = "enemy-" + life;
                string attacker = "attacker-" + life;
                Check(board.TrySynchronizeRoster(1, Actor("local", true),
                    new[] { Actor(ally, true) }, new[] { Actor(victim, true), Actor(attacker, true) }, out var issue),
                    "continuous team roster accepts distinct replacement bot bodies: " + issue);
                Check(Row(board, ally).Kills == 0 && Row(board, attacker).Score == 0,
                    "generated replacements start their own personal run despite identical display names");
                bool allyHead = life % 3 == 0;
                bool enemyHead = life % 7 == 0;
                bool localHead = life % 10 == 0;
                board.ReportKill(Kill(ally, victim, true, false, headshot: allyHead));
                board.ReportKill(Kill(attacker, ally, false, true, headshot: enemyHead));
                board.ReportKill(Kill("local", attacker, true, false, headshot: localHead));
                expectedFriendly += 400 + (allyHead ? 50 : 0) + (localHead ? 50 : 0) + (life % 5 == 0 ? 100 : 0);
                expectedOpponent += 200 + (enemyHead ? 50 : 0);
                friendlyHeads += (allyHead ? 1 : 0) + (localHead ? 1 : 0);
                opponentHeads += enemyHead ? 1 : 0;
                var friendly = board.Totals(ArenaScoreboardSide.Friendly);
                var opponent = board.Totals(ArenaScoreboardSide.Opponent);
                Check(friendly.Score == expectedFriendly && opponent.Score == expectedOpponent,
                    "both faction score totals survive ordinary bot-history compaction");
                Check(friendly.Kills == life * 2 && friendly.Deaths == life
                      && opponent.Kills == life && opponent.Deaths == life * 2,
                    "both faction kill and death totals remain exact across replacements");
                Check(friendly.Headshots == friendlyHeads && opponent.Headshots == opponentHeads,
                    "archived headshots remain with their original faction");
                Check(board.ParticipantCount <= 36 && board.TrackedProfileCount <= 36,
                    "thousands of bots retain bounded active and historical identity dictionaries");
            }
            Check(board.TrySynchronizeRoster(1, Actor("local", true), Array.Empty<ArenaScoreboardActor>(),
                Array.Empty<ArenaScoreboardActor>(), out _), "last roster retires all ordinary bot runs");
            Check(board.HistoricalParticipantCount == lives * 3 + 1,
                "historical participant count includes every generated bot body");
            Check(board.ParticipantCount <= 33 && board.ArchivedParticipantCount == board.HistoricalParticipantCount - board.ParticipantCount,
                "compaction accounts for every removed row without losing match history");
            Check(board.Totals(ArenaScoreboardSide.Friendly).Score == expectedFriendly
                  && board.Totals(ArenaScoreboardSide.Opponent).Score == expectedOpponent,
                "final retirement preserves exact friendly and opposing team totals");
            Check(board.Local.Kills == lives && board.Local.Streak == lives && board.Local.BestStreak == lives,
                "bot replacement does not reset the local player's ongoing run");
        }
    }

    private static ArenaScoreboard Board(bool localUsec) => new("local", "You", localUsec ? "USEC" : "BEAR");
    private static ArenaScoreboardActor Actor(string id, bool alive) => new(id, "Same generated name", "PMC", alive);
    private static ArenaScoreboardEntry Row(ArenaScoreboard board, string id)
        => board.Standings(null, false, 100, false).Single(entry => entry.Key == "body:" + id);
    private static ArenaKill Kill(string killer, string victim, bool? friendlyKiller, bool? friendlyVictim,
        bool scoreEligible = true, bool headshot = false) => new()
        {
            KillerProfileId = killer, KillerName = "Same generated name", KillerRoleLabel = "PMC",
            VictimProfileId = victim, VictimName = "Same generated name", VictimRoleLabel = "PMC", VictimRole = ArenaRole.Pmc,
            KilledByLocal = killer == "local", LocalIsVictim = victim == "local", Headshot = headshot,
            KillerIsFriendly = friendlyKiller, VictimIsFriendly = friendlyVictim, ScoreEligible = scoreEligible,
        };

    private static void ActiveRaid(string mode = "teamdeathmatch")
    {
        ArenaSession.IsActive = true;
        ArenaSession.ActiveMode = mode;
        ArenaSession.OwnedGame = Singleton<AbstractGame>.Instance = new AbstractGame();
        ArenaSession.OwnedWorld = Singleton<GameWorld>.Instance = new GameWorld();
    }

    private static void Main()
    {
        CapacityAndLoading();
        BoundedDebtAndIndependentFactions();
        SynchronousCompletion();
        DispatchOwnership();
        CapturedFactionScoresAndAwards();
        LongMatchFactionTotals();
        Console.WriteLine($"PASS: {_checks} Team Deathmatch checks (production population, spawn dispatch, scoreboard and awards linked).");
    }
}
