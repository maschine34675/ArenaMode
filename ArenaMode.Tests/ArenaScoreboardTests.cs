using ArenaMode.Combat;
using ArenaMode.Modes;

namespace ArenaMode.Tests;

public sealed class ArenaScoreboardTests
{
    [Fact]
    public void EveryContestantUsesTheExistingRoleHeadshotAndStreakRules()
    {
        ArenaScoreboard board = Board();

        for (int index = 1; index <= 5; index++)
        {
            board.ReportKill(Kill(
                "local-profile",
                "Player",
                $"enemy-{index}",
                $"Enemy {index}",
                ArenaRole.Pmc,
                localKiller: true,
                headshot: index == 1));
        }

        Assert.Equal(5, board.Local.Kills);
        Assert.Equal(1, board.Local.Headshots);
        Assert.Equal(5, board.Local.Streak);
        Assert.Equal(5, board.Local.BestStreak);
        Assert.Equal(1_150, board.Local.Score);

        ArenaScoreboardTotals friendly = board.Totals(ArenaScoreboardSide.Friendly);
        ArenaScoreboardTotals opponents = board.Totals(ArenaScoreboardSide.Opponent);
        Assert.Equal(1_150, friendly.Score);
        Assert.Equal(5, friendly.Kills);
        Assert.Equal(5, opponents.Deaths);
    }

    [Fact]
    public void AggressorKillAndLocalDeathSignalCountOneLocalDeath()
    {
        ArenaScoreboard board = Board();
        Assert.True(board.TrySynchronizeRoster(
            1,
            Actor("local-profile", "Player", alive: true),
            Array.Empty<ArenaScoreboardActor>(),
            new[] { Actor("enemy", "Enemy", alive: true) },
            out _));

        board.ReportKill(Kill(
            "enemy",
            "Enemy",
            "local-profile",
            "Player",
            ArenaRole.Pmc,
            localVictim: true,
            headshot: true));

        Assert.Equal(0, board.Local.Deaths);
        ArenaScoreboardEntry enemy = Assert.Single(board.Standings(
            ArenaScoreboardSide.Opponent, false, 10, false));
        Assert.Equal(1, enemy.Kills);
        Assert.Equal(250, enemy.Score);

        board.ReportLocalDeath();
        Assert.Equal(1, board.Local.Deaths);
        Assert.False(board.Local.IsActive);

        board.ReportLocalRespawn("local-profile", "Player", "PMC");
        Assert.Equal(1, board.Local.Deaths);
        Assert.True(board.Local.IsActive);
    }

    [Fact]
    public void StableFollowerAccountCarriesScoreAcrossFreshProfileBodies()
    {
        ArenaScoreboard board = Board();
        Assert.True(SyncTeam(board, 1, "follower-round-1", "enemy-round-1", out _));
        board.ReportKill(Kill(
            "follower-round-1", "Alpha", "enemy-round-1", "Same Name", ArenaRole.Pmc));

        Assert.True(SyncTeam(board, 2, "follower-round-2", "enemy-round-2", out _));
        board.ReportKill(Kill(
            "follower-round-2", "Alpha", "enemy-round-2", "Same Name", ArenaRole.Pmc));

        IReadOnlyList<ArenaScoreboardEntry> friendlies = board.Standings(
            ArenaScoreboardSide.Friendly, false, 10, true);
        ArenaScoreboardEntry follower = Assert.Single(friendlies, entry => !entry.IsLocal);
        Assert.Equal(2, follower.Kills);
        Assert.Equal(400, follower.Score);

        IReadOnlyList<ArenaScoreboardEntry> allOpponents = board.Standings(
            ArenaScoreboardSide.Opponent, false, 10, false);
        Assert.Equal(2, allOpponents.Count);
        Assert.NotEqual(allOpponents[0].Key, allOpponents[1].Key);
        Assert.All(allOpponents, entry => Assert.Equal("Same Name", entry.DisplayName));

        ArenaScoreboardEntry currentOpponent = Assert.Single(board.Standings(
            ArenaScoreboardSide.Opponent, true, 10, false));
        Assert.Equal("body:enemy-round-2", currentOpponent.Key);
        Assert.Equal(2, board.CurrentRosterEpoch);

        ArenaScoreboardTotals opponentTotals = board.Totals(ArenaScoreboardSide.Opponent);
        Assert.Equal(2, opponentTotals.Deaths);
    }

    [Fact]
    public void DelayedKillFromOldFollowerBodyStillCreditsStableTeammate()
    {
        ArenaScoreboard board = Board();
        Assert.True(SyncTeam(board, 1, "follower-round-1", "enemy-round-1", out _));
        Assert.True(SyncTeam(board, 2, "follower-round-2", "enemy-round-2", out _));

        board.ReportKill(Kill(
            "follower-round-1",
            "Alpha",
            "enemy-round-2",
            "Enemy",
            ArenaRole.Pmc));

        ArenaScoreboardEntry follower = Assert.Single(
            board.Standings(ArenaScoreboardSide.Friendly, false, 10, true),
            entry => !entry.IsLocal);
        Assert.Equal(1, follower.Kills);
        Assert.Equal(200, follower.Score);
        Assert.Equal(0, board.Totals(ArenaScoreboardSide.Opponent).Kills);
        Assert.DoesNotContain(
            board.Standings(ArenaScoreboardSide.Opponent, false, 10, false),
            entry => entry.Key == "body:follower-round-1");
    }

    [Fact]
    public void OldProfileAliasCannotChangeParticipantOrTeam()
    {
        ArenaScoreboard board = Board();
        Assert.True(SyncTeam(board, 1, "follower", "enemy", out _));

        bool accepted = board.TrySynchronizeRoster(
            2,
            Actor("local-profile", "Player", alive: true),
            new[] { Actor("enemy", "Impostor", alive: true, stableId: "pft-account") },
            Array.Empty<ArenaScoreboardActor>(),
            out string problem);

        Assert.False(accepted);
        Assert.Contains("change scoreboard identity or side", problem);
        Assert.Equal(1, board.CurrentRosterEpoch);
        Assert.Single(board.Standings(ArenaScoreboardSide.Opponent, true, 10, false));
        Assert.Equal(3, board.ParticipantCount);
    }

    [Fact]
    public void LongDeathmatchPinsLocalRowAndBoundsRenderedBotRuns()
    {
        ArenaScoreboard board = Board();
        for (int index = 0; index < 20; index++)
        {
            board.ReportKill(Kill(
                $"bot-{index}",
                $"Bot {index}",
                $"victim-{index}",
                $"Victim {index}",
                ArenaRole.Scav));
        }

        IReadOnlyList<ArenaScoreboardEntry> visible = board.Standings(
            side: null,
            currentRosterOnly: false,
            maxRows: 12,
            pinLocal: true);

        Assert.Equal(12, visible.Count);
        Assert.Contains(board.Local, visible);
        Assert.True(board.ParticipantCount > visible.Count);
    }

    [Fact]
    public void LongDeathmatchCompactsFinishedBodiesButPreservesExactTotals()
    {
        ArenaScoreboard board = Board();
        ArenaScoreboardActor local = Actor("local-profile", "Player", alive: true);

        Assert.True(board.TrySynchronizeRoster(
            1,
            local,
            Array.Empty<ArenaScoreboardActor>(),
            new[] { Actor("bot-0", "Bot 0", alive: true) },
            out _));
        int unchangedVersion = board.Version;
        Assert.True(board.TrySynchronizeRoster(
            1,
            local,
            Array.Empty<ArenaScoreboardActor>(),
            new[] { Actor("bot-0", "Bot 0", alive: true) },
            out _));
        Assert.Equal(unchangedVersion, board.Version);

        const int lives = 10_000;
        for (int index = 0; index < lives; index++)
        {
            Assert.True(board.TrySynchronizeRoster(
                index + 1,
                local,
                Array.Empty<ArenaScoreboardActor>(),
                new[] { Actor($"bot-{index}", $"Bot {index}", alive: true) },
                out _));
            board.ReportKill(Kill(
                $"bot-{index}",
                $"Bot {index}",
                $"victim-{index}",
                $"Victim {index}",
                ArenaRole.Scav));
        }

        Assert.True(board.TrySynchronizeRoster(
            lives + 1,
            local,
            Array.Empty<ArenaScoreboardActor>(),
            Array.Empty<ArenaScoreboardActor>(),
            out _));

        ArenaScoreboardTotals totals = board.Totals(ArenaScoreboardSide.Opponent);
        Assert.Equal(lives * ScoreRules.PointsFor(ArenaRole.Scav), totals.Score);
        Assert.Equal(lives, totals.Kills);
        Assert.Equal(lives, totals.Deaths);
        Assert.Equal(1 + (2 * lives), board.HistoricalParticipantCount);
        Assert.True(board.ArchivedParticipantCount > 0);
        Assert.InRange(board.ParticipantCount, 1, 33);
        Assert.InRange(board.TrackedProfileCount, 1, 33);
    }

    [Fact]
    public void PosthumousLocalKillDoesNotMarkDeadPlayerActive()
    {
        ArenaScoreboard board = Board();
        board.ReportLocalDeath();

        board.ReportKill(Kill(
            "local-profile",
            "Player",
            "enemy",
            "Enemy",
            ArenaRole.Pmc,
            localKiller: true));

        Assert.False(board.Local.IsActive);
        Assert.Equal(1, board.Local.Kills);
    }

    [Fact]
    public void AdministrativeDeathGateIsProfileScopedNestedAndExceptionSafe()
    {
        ArenaAdministrativeDeathGate.Reset();
        Assert.False(ArenaAdministrativeDeathGate.IsActive("bot-a"));
        Assert.False(ArenaAdministrativeDeathGate.IsActive("  "));

        IDisposable outer = ArenaAdministrativeDeathGate.Enter(" bot-a ");
        Assert.True(ArenaAdministrativeDeathGate.IsActive("bot-a"));
        Assert.False(ArenaAdministrativeDeathGate.IsActive("bot-b"));
        using (ArenaAdministrativeDeathGate.Enter("bot-a"))
            Assert.True(ArenaAdministrativeDeathGate.IsActive("bot-a"));
        Assert.True(ArenaAdministrativeDeathGate.IsActive("bot-a"));
        outer.Dispose();
        outer.Dispose();
        Assert.False(ArenaAdministrativeDeathGate.IsActive("bot-a"));

        Assert.Throws<InvalidOperationException>(() => ThrowInsideAdministrativeDeath());
        Assert.False(ArenaAdministrativeDeathGate.IsActive("exception-bot"));

        IDisposable stale = ArenaAdministrativeDeathGate.Enter("stale-bot");
        ArenaAdministrativeDeathGate.Reset();
        IDisposable fresh = ArenaAdministrativeDeathGate.Enter("stale-bot");
        stale.Dispose();
        Assert.True(ArenaAdministrativeDeathGate.IsActive("stale-bot"));
        fresh.Dispose();
        Assert.False(ArenaAdministrativeDeathGate.IsActive("stale-bot"));
        Assert.False(ArenaAdministrativeDeathGate.IsActive(null!));
    }

    [Fact]
    public void ProductionWiringKeepsBoardClientOnlyAndUsesTheSafeF9Panel()
    {
        string match = Read("ArenaMode.Client", "Modes", "ArenaMatch.cs");
        string service = Read("ArenaMode.Client", "Modes", "ArenaMatchService.cs");
        string tactical = Read("ArenaMode.Client", "Modes", "TacticalMode.cs");
        string panel = Read("ArenaMode.Client", "Hud", "ArenaPanel.cs");
        string scoreboard = Read("ArenaMode.Client", "Modes", "ArenaScoreboard.cs");
        string combatPatches = Read("ArenaMode.Client", "Combat", "CombatPatches.cs");
        string tacticalRetirement = Read("ArenaMode.Client", "Modes", "TacticalMode.cs");
        string burstRetirement = Read("ArenaMode.Client", "Bots", "BurstSpawnService.cs");
        string pftRetirement = Read("ArenaMode.Client", "Compat", "PitFireTeamRoundLifecycle.cs");
        string giveUp = Read("ArenaMode.Client", "Combat", "ArenaGiveUp.cs");

        Assert.Contains("public int Score => Scoreboard.Local.Score", match);
        Assert.Contains("Scoreboard.ReportKill(kill)", match);
        Assert.Contains("Scoreboard.ReportLocalDeath()", match);
        Assert.Contains("SynchronizeScoreboard(match)", service);
        Assert.Contains("follower?.Profile?.AccountId", tactical);
        Assert.Contains("TrySynchronizeScoreboardRoster", tactical);
        Assert.Contains("GUILayout.BeginScrollView", panel);
        Assert.Contains("CheckCurrentScreen(EEftScreenType.BattleUI)", panel);
        Assert.Contains("Settings.PanelHotkey.Value", panel);
        Assert.DoesNotContain("KeyCode.Tab", panel);
        Assert.DoesNotContain("ArenaMode.Server", scoreboard);
        Assert.DoesNotContain("Singleton<", scoreboard);
        int administrativeGateIndex = combatPatches.IndexOf(
            "ArenaAdministrativeDeathGate.IsActive",
            StringComparison.Ordinal);
        int seenVictimIndex = combatPatches.IndexOf("SeenVictims.Add", StringComparison.Ordinal);
        Assert.True(administrativeGateIndex >= 0);
        Assert.True(seenVictimIndex >= 0);
        Assert.True(administrativeGateIndex < seenVictimIndex);
        Assert.Contains("ArenaAdministrativeDeathGate.Enter(bot.ProfileId)", tacticalRetirement);
        Assert.Contains("ArenaAdministrativeDeathGate.Enter(bot.ProfileId)", burstRetirement);
        Assert.Equal(2, CountOccurrences(
            pftRetirement,
            "ArenaAdministrativeDeathGate.Enter(bot.ProfileId)"));
        Assert.Contains("ArenaAdministrativeDeathGate.Enter(player.ProfileId)", giveUp);
    }

    private static ArenaScoreboard Board()
        => new("local-profile", "Player", "PMC");

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoFiles.Root }.Concat(parts).ToArray()));

    private static ArenaScoreboardActor Actor(
        string profile,
        string name,
        bool alive,
        string? stableId = null)
        => new(profile, name, "PMC", alive, stableId);

    private static bool SyncTeam(
        ArenaScoreboard board,
        int epoch,
        string followerProfile,
        string enemyProfile,
        out string problem)
        => board.TrySynchronizeRoster(
            epoch,
            Actor("local-profile", "Player", alive: true),
            new[] { Actor(followerProfile, "Alpha", alive: true, stableId: "pft-account") },
            new[] { Actor(enemyProfile, "Same Name", alive: true) },
            out problem);

    private static ArenaKill Kill(
        string killerProfile,
        string killerName,
        string victimProfile,
        string victimName,
        ArenaRole victimRole,
        bool localKiller = false,
        bool localVictim = false,
        bool headshot = false)
        => new()
        {
            KillerProfileId = killerProfile,
            KillerName = killerName,
            KillerRoleLabel = "PMC",
            VictimProfileId = victimProfile,
            VictimName = victimName,
            VictimRole = victimRole,
            VictimRoleLabel = "PMC",
            KilledByLocal = localKiller,
            LocalIsVictim = localVictim,
            Headshot = headshot,
            Time = 1f,
        };

    private static void ThrowInsideAdministrativeDeath()
    {
        using (ArenaAdministrativeDeathGate.Enter("exception-bot"))
            throw new InvalidOperationException("test");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
