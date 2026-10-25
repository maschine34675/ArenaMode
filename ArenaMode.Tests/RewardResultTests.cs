using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// Synthetic, server-authorized manifests and untrusted combat reports. These
/// tests prove rules and refusal, not physical combat truth or a native caller.
/// </summary>
public sealed class RewardResultTests
{
    [Fact]
    public void LegacyPolicyAndEvaluationSerializeByteForByteWithoutOptionalXpFields()
    {
        const string policy = "{\"version\":\"arena-v1\",\"roublesPerKill\":1000,\"roublesPerRoundWin\":10000,\"maxKills\":20000,\"maxRounds\":100,\"maxRoubles\":200000,\"capRoubles\":true}";
        Assert.Equal(policy, RewardRuntimeFiles.Json(JsonSerializer.Deserialize<RewardRatePolicy>(policy, RewardSettlementCore.JournalJson)));
        const string evaluation = "{\"status\":0,\"reason\":\"validated-result\",\"eligibleKills\":1,\"roundWins\":1,\"roubles\":11000}";
        Assert.Equal(evaluation, RewardRuntimeFiles.Json(JsonSerializer.Deserialize<RewardEvaluation>(evaluation, RewardSettlementCore.JournalJson)));
    }

    [Fact]
    public void XpUsesSameEligibilityAndIndependentFrozenCapAsRoubles()
    {
        var run = Run(RewardRoundRule.LocalTarget, target: 2) with { Policy = RewardRuntimeCoordinator.Policy };
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1));
        Assert.Equal(1200, result.Experience);
        Assert.Equal(12_000, result.Roubles);
        var capped = run with { Policy = run.Policy with { MaxExperience = 1100 } };
        Assert.Equal(1100, Evaluate(capped, Death(1, 2, 1), Death(2, 3, 1)).Experience);
        Assert.Equal(0, RewardResultValidator.Evaluate(run, Report(run) with { State = RewardReportState.NoContest }).Experience);
        var invalid = run with { Policy = run.Policy with { ExperiencePerKill = -1 } };
        Assert.Throws<RewardDataException>(() => Evaluate(invalid));
    }

    [Fact]
    public void ServerPolicyPricesOnlyLocalHostileFinalKills()
    {
        var run = Run(actors: [Actor(1), Actor(2), Actor(3), Actor(4, RewardSide.Friendly), Actor(5), Actor(6)]);
        var result = Evaluate(run,
            Death(1, 2, 1),                 // local hostile final blow
            Death(2, 3, 4),                 // ally final blow: not a local kill/assist reward
            Death(3, 4, 1),                 // local friendly fire
            Death(4, 5, 5),                 // opponent suicide
            Death(5, 6, null, RewardDeathKind.Environment));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(100, result.Roubles);
    }

    [Theory]
    [InlineData("gungame")]
    [InlineData("zombies")]
    public void KillOnlyModesNeverInventLadderOrWaveWins(string mode)
    {
        var run = Run() with { Mode = mode };
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(2, result.EligibleKills);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(200, result.Roubles);
    }

    [Fact]
    public void RegisteredRespawnLivesCountSeparatelyButKeepOneScoringSlot()
    {
        var run = Run(RewardRoundRule.FreeForAllScore, target: 2,
            actors: [Actor(1, RewardSide.Individual), Actor(2, RewardSide.Individual),
                Actor(2, RewardSide.Individual, life: 3, spawned: 1500)]);
        var result = Evaluate(run, Death(1, 2, 1, at: 1000), Death(2, 3, 1, at: 2000));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(2, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1200, result.Roubles);
    }

    [Fact]
    public void DelayedHostileKillAfterAttackerDeathRemainsEligible()
    {
        var run = Run();
        var result = Evaluate(run, Death(1, 1, 2), Death(2, 3, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(100, result.Roubles);
    }

    [Theory]
    [InlineData("Aborted")]
    [InlineData("NoContest")]
    public void AbortedOrNoContestRunCannotMintEarlierKills(string state)
    {
        var run = Run();
        var report = Report(run, Death(1, 2, 1)) with { State = Enum.Parse<RewardReportState>(state) };
        var result = RewardResultValidator.Evaluate(run, report);
        Assert.Equal(RewardEvaluationStatus.NoReward, result.Status);
        Assert.Equal(0, result.Roubles);
        Assert.Equal(0, result.RoundWins);
    }

    [Fact]
    public void AdministrativeRemovalNeverCompletesARewardedElimination()
    {
        var run = Run(RewardRoundRule.SoloElimination);
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 3, null, RewardDeathKind.Administrative));
        Assert.Equal(RewardEvaluationStatus.NoReward, result.Status);
        Assert.Equal("administrative-removal", result.Reason);
        Assert.Equal(0, result.Roubles);
    }

    [Fact]
    public void LocalKillTargetPaysDerivedWinAndKillAmounts()
    {
        var run = Run(RewardRoundRule.LocalTarget, target: 2);
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(2, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1200, result.Roubles);
    }

    [Fact]
    public void AllVersusPlayerTimeoutBeforeTargetIsNotVictory()
    {
        var run = Run(RewardRoundRule.LocalTarget, target: 2, timeLimit: 10000);
        var result = Evaluate(run, Death(1, 2, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(100, result.Roubles);
    }

    [Fact]
    public void AllVersusPlayerTimeOnlyCompletionIsNotVictory()
    {
        var run = Run(RewardRoundRule.LocalTarget, target: 0, timeLimit: 10000);
        var result = Evaluate(run, Death(1, 2, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(100, result.Roubles);
    }

    [Fact]
    public void FfaTimerRewardsOnlyUniqueLeader()
    {
        var run = Run(RewardRoundRule.FreeForAllScore, timeLimit: 10000,
            actors: [Actor(1, RewardSide.Individual), Actor(2, RewardSide.Individual), Actor(3, RewardSide.Individual)]);
        var win = Evaluate(run, Death(1, 2, 1));
        Assert.Equal(1, win.RoundWins);
        Assert.Equal(1100, win.Roubles);
        var draw = Evaluate(run, Death(1, 2, 1), Death(2, 3, 2));
        Assert.Equal(0, draw.RoundWins);
        Assert.Equal(100, draw.Roubles);
        var empty = Evaluate(run);
        Assert.Equal(RewardEvaluationStatus.NoReward, empty.Status);
        Assert.Equal(0, empty.RoundWins);
    }

    [Fact]
    public void FfaOpponentReachingTargetEndsLocalWinEligibility()
    {
        var run = Run(RewardRoundRule.FreeForAllScore, target: 1,
            actors: [Actor(1, RewardSide.Individual), Actor(2, RewardSide.Individual), Actor(3, RewardSide.Individual)]);
        var result = Evaluate(run, Death(1, 3, 2));
        Assert.Equal(RewardEvaluationStatus.NoReward, result.Status);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(0, result.Roubles);
    }

    [Fact]
    public void TdmTeamScoreAwardsWinButNotAlliesPersonalKills()
    {
        var run = Run(RewardRoundRule.TeamScore, target: 2,
            actors: [Actor(1), Actor(2), Actor(3), Actor(4, RewardSide.Friendly)]);
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 3, 4));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1100, result.Roubles);
    }

    [Fact]
    public void TdmSuicideDoesNotCreditOtherTeamOrInventPersonalKill()
    {
        var run = Run(RewardRoundRule.TeamScore, target: 1);
        // A delayed hostile final blow can still reach the target. If suicide
        // credited the opponent, the first event would prematurely end the round.
        var result = Evaluate(run, Death(1, 1, 1), Death(2, 2, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1100, result.Roubles);
    }

    [Fact]
    public void TdmEnvironmentalDeathDoesNotCountTowardsTeamTarget()
    {
        var run = Run(RewardRoundRule.TeamScore, target: 1);
        var result = Evaluate(run, Death(1, 2, null, RewardDeathKind.Environment), Death(2, 3, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(1, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1100, result.Roubles);
    }

    [Fact]
    public void TdmExplicitLocalForfeitCreditsOpponentWithoutAReward()
    {
        var run = Run(RewardRoundRule.TeamScore, target: 1);
        var result = Evaluate(run, Death(1, 1, null, RewardDeathKind.LocalForfeit));
        Assert.Equal(RewardEvaluationStatus.NoReward, result.Status);
        Assert.Equal(0, result.EligibleKills);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(0, result.Roubles);
        Assert.Equal(RewardEvaluationStatus.Rejected,
            Evaluate(run, Death(1, 2, null, RewardDeathKind.LocalForfeit)).Status);
        Assert.Equal(RewardEvaluationStatus.Rejected,
            Evaluate(run, Death(1, 1, 2, RewardDeathKind.LocalForfeit)).Status);
        Assert.Equal(RewardEvaluationStatus.Rejected,
            Evaluate(Run(), Death(1, 1, null, RewardDeathKind.LocalForfeit)).Status);
    }

    [Fact]
    public void TdmTimerTieDoesNotAwardWin()
    {
        var run = Run(RewardRoundRule.TeamScore, timeLimit: 10000);
        var result = Evaluate(run, Death(1, 2, 1), Death(2, 1, 3));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(100, result.Roubles);
    }

    [Theory]
    [InlineData("teamdeathmatch", 1, true)]
    [InlineData("teamdeathmatch", 3, false)]
    [InlineData("deathmatch", 1, false)]
    [InlineData("gungame", 1, false)]
    [InlineData("zombies", 1, false)]
    public void UnlimitedLocalForfeitIsRestrictedToTheLocalTeamDeathmatchPlayer(string mode, int victim, bool accepted)
    {
        var run = Run() with { Mode = mode };
        var result = Evaluate(run, Death(1, 2, 1), Death(2, victim, null, RewardDeathKind.LocalForfeit));
        Assert.Equal(accepted ? RewardEvaluationStatus.Eligible : RewardEvaluationStatus.Rejected, result.Status);
        Assert.Equal(accepted ? 100 : 0, result.Roubles);
        Assert.Equal(0, result.RoundWins);
    }

    [Fact]
    public void SoloEliminationRequiresAllRegisteredEnemiesDead()
    {
        var run = Run(RewardRoundRule.SoloElimination);
        var win = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1), Death(3, 4, 1), Death(4, 5, 1), Death(5, 6, 1));
        Assert.Equal(1, win.RoundWins);
        Assert.Equal(1500, win.Roubles);
        var unfinished = Evaluate(run, Death(1, 2, 1));
        Assert.Equal(RewardEvaluationStatus.Rejected, unfinished.Status);
        Assert.Equal("unfinished-round", unfinished.Reason);
    }

    [Fact]
    public void SoloTimeoutDoesNotAwardWin()
    {
        var run = Run(RewardRoundRule.SoloElimination, timeLimit: 10000);
        var result = Evaluate(run, Death(1, 2, 1));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(0, result.RoundWins);
        Assert.Equal(100, result.Roubles);
    }

    [Fact]
    public void SimultaneousSquadWipeIsLossRegardlessOfReportedOrdering()
    {
        var run = Run(RewardRoundRule.TeamElimination);
        var firstEnemy = Evaluate(run, Death(1, 2, 1, at: 1000), Death(2, 3, 1, at: 1000), Death(3, 4, 1, at: 1000),
            Death(4, 5, 1, at: 1000), Death(5, 6, 1, at: 1000), Death(6, 1, 2, at: 1000), Death(7, 7, 2, at: 1000));
        var firstLocal = Evaluate(run, Death(1, 1, 2, at: 1000), Death(2, 7, 2, at: 1000), Death(3, 2, 1, at: 1000),
            Death(4, 3, 1, at: 1000), Death(5, 4, 1, at: 1000), Death(6, 5, 1, at: 1000), Death(7, 6, 1, at: 1000));
        Assert.Equal(0, firstEnemy.RoundWins);
        Assert.Equal(0, firstLocal.RoundWins);
        Assert.Equal(500, firstEnemy.Roubles);
        Assert.Equal(firstEnemy, firstLocal);
    }

    [Fact]
    public void TeamEliminationCanWinWithLocalDeadAndAllyAlive()
    {
        var run = Run(RewardRoundRule.TeamElimination);
        var result = Evaluate(run, Death(1, 1, 2), Death(2, 2, 7), Death(3, 3, 7), Death(4, 4, 7), Death(5, 5, 7), Death(6, 6, 7));
        Assert.Equal(RewardEvaluationStatus.Eligible, result.Status);
        Assert.Equal(0, result.EligibleKills);
        Assert.Equal(1, result.RoundWins);
        Assert.Equal(1000, result.Roubles);
    }

    [Fact]
    public void TeamEliminationTimeoutComparesSurvivingBodies()
    {
        var run = Run(RewardRoundRule.TeamElimination, timeLimit: 10000);
        var win = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1), Death(3, 4, 1), Death(4, 5, 1));
        Assert.Equal(1, win.RoundWins);
        Assert.Equal(1400, win.Roubles);
        var tie = Evaluate(run, Death(1, 2, 1), Death(2, 3, 1), Death(3, 4, 1), Death(4, 5, 1), Death(5, 7, 6));
        Assert.Equal(0, tie.RoundWins);
        Assert.Equal(400, tie.Roubles);
    }

    [Fact]
    public void ConsecutiveRoundsAggregateOnceWithGlobalEventSequence()
    {
        var run = Run(RewardRoundRule.LocalTarget, target: 1, actors: [Actor(1), Actor(2)]);
        var second = run.Rounds[0] with
        {
            RoundId = G(22),
            Actors = [Actor(1, life: 11), Actor(2, life: 12)]
        };
        run = run with { Rounds = [run.Rounds[0], second] };
        var report = Report(run) with
        {
            Rounds = [new(run.Rounds[0].RoundId, [Death(1, 2, 1)]), new(second.RoundId, [Death(2, 12, 11)])]
        };
        var result = RewardResultValidator.Evaluate(run, report);
        Assert.Equal(2, result.EligibleKills);
        Assert.Equal(2, result.RoundWins);
        Assert.Equal(2200, result.Roubles);
        var repeatedSequence = report with
        {
            Rounds = [report.Rounds[0], new(second.RoundId, [Death(1, 12, 11)])]
        };
        Assert.Equal(RewardEvaluationStatus.Rejected, RewardResultValidator.Evaluate(run, repeatedSequence).Status);
        var reversed = report with { Rounds = [report.Rounds[1], report.Rounds[0]] };
        Assert.Equal(RewardEvaluationStatus.Rejected, RewardResultValidator.Evaluate(run, reversed).Status);
    }

    [Theory]
    [InlineData("duplicate-victim")]
    [InlineData("gap-sequence")]
    [InlineData("backwards-time")]
    [InlineData("future-time")]
    [InlineData("negative-time")]
    [InlineData("unknown-victim")]
    [InlineData("unknown-killer")]
    [InlineData("null-killer")]
    [InlineData("environment-killer")]
    [InlineData("unknown-kind")]
    [InlineData("null-event")]
    [InlineData("too-many-deaths")]
    public void MalformedDeathEvidenceIsRejected(string mutation)
    {
        var run = Run();
        RewardDeathReport[] deaths = [Death(1, 2, 1), Death(2, 3, 1)];
        deaths = mutation switch
        {
            "duplicate-victim" => [deaths[0], Death(2, 2, 1)],
            "gap-sequence" => [Death(2, 2, 1)],
            "backwards-time" => [deaths[0], Death(2, 3, 1, at: 1)],
            "future-time" => [Death(1, 2, 1, at: 10001)],
            "negative-time" => [Death(1, 2, 1, at: -1)],
            "unknown-victim" => [Death(1, 99, 1)],
            "unknown-killer" => [Death(1, 2, 99)],
            "null-killer" => [Death(1, 2, null)],
            "environment-killer" => [Death(1, 2, 1, RewardDeathKind.Environment)],
            "unknown-kind" => [Death(1, 2, 1, (RewardDeathKind)999)],
            "null-event" => [null!],
            "too-many-deaths" => [deaths[0], deaths[1], Death(3, 1, 2), Death(4, 2, 1)],
            _ => throw new InvalidOperationException()
        };
        var result = Evaluate(run, deaths);
        Assert.Equal(RewardEvaluationStatus.Rejected, result.Status);
        Assert.Equal(0, result.Roubles);
    }

    [Theory]
    [InlineData("victim")]
    [InlineData("killer")]
    public void DeathCannotPrecedeServerRegisteredSpawn(string who)
    {
        var run = Run(actors: [Actor(1, spawned: who == "killer" ? 2000 : 0),
            Actor(2, spawned: who == "victim" ? 2000 : 0)]);
        var result = Evaluate(run, Death(1, 2, 1, at: 1000));
        Assert.Equal(RewardEvaluationStatus.Rejected, result.Status);
        Assert.Equal(0, result.Roubles);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("run")]
    [InlineData("sequence")]
    [InlineData("arm")]
    [InlineData("native-end")]
    [InlineData("state")]
    [InlineData("missing-round")]
    [InlineData("null-rounds")]
    [InlineData("round-id")]
    [InlineData("null-round")]
    [InlineData("null-deaths")]
    [InlineData("null-report")]
    public void ReportMustMatchExactServerRun(string mutation)
    {
        var run = Run();
        var report = Report(run, Death(1, 2, 1));
        report = mutation switch
        {
            "schema" => report with { Schema = 2 },
            "run" => report with { RunId = G(99) },
            "sequence" => report with { Rounds = [new(run.Rounds[0].RoundId, [Death(0, 2, 1)])] },
            "arm" => report with { ArmToken = G(99) },
            "native-end" => report with { NativeEndIdentity = "other" },
            "state" => report with { State = (RewardReportState)999 },
            "missing-round" => report with { Rounds = [] },
            "null-rounds" => report with { Rounds = null! },
            "round-id" => report with { Rounds = [report.Rounds[0] with { RoundId = G(99) }] },
            "null-round" => report with { Rounds = [null!] },
            "null-deaths" => report with { Rounds = [report.Rounds[0] with { Deaths = null! }] },
            "null-report" => null!,
            _ => throw new InvalidOperationException()
        };
        var result = RewardResultValidator.Evaluate(run, report);
        Assert.Equal(RewardEvaluationStatus.Rejected, result.Status);
        Assert.Equal(0, result.Roubles);
    }

    [Fact]
    public void ClientCannotAddOwnPayoutOrWinFields()
    {
        var report = JsonNode.Parse(JsonSerializer.Serialize(Report(Run()), RewardSettlementCore.JournalJson))!.AsObject();
        report["roubles"] = 9_999_999;
        Assert.Throws<JsonException>(() => report.Deserialize<RewardMatchReport>(RewardSettlementCore.JournalJson));
        report.Remove("roubles");
        report["won"] = true;
        Assert.Throws<JsonException>(() => report.Deserialize<RewardMatchReport>(RewardSettlementCore.JournalJson));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("runId")]
    [InlineData("armToken")]
    [InlineData("nativeEndIdentity")]
    [InlineData("state")]
    [InlineData("rounds")]
    public void MissingRequiredReportFieldsCannotDefaultToACompletedResult(string missing)
    {
        var report = JsonNode.Parse(JsonSerializer.Serialize(Report(Run()), RewardSettlementCore.JournalJson))!.AsObject();
        report.Remove(missing);
        Assert.Throws<JsonException>(() => report.Deserialize<RewardMatchReport>(RewardSettlementCore.JournalJson));
    }

    [Fact]
    public void EventsPastTargetAndPastTimeLimitAreRejected()
    {
        var target = Run(RewardRoundRule.LocalTarget, target: 1);
        Assert.Equal("events-after-round-result", Evaluate(target, Death(1, 2, 1), Death(2, 3, 1)).Reason);
        var timer = Run(RewardRoundRule.LocalTarget, target: 2, timeLimit: 1500);
        Assert.Equal("event-after-time-limit", Evaluate(timer, Death(1, 2, 1), Death(2, 3, 1)).Reason);
        var unfinished = Run(RewardRoundRule.LocalTarget, target: 2, timeLimit: 20000);
        Assert.Equal("unfinished-round", Evaluate(unfinished, Death(1, 2, 1)).Reason);
    }

    [Theory]
    [InlineData("LocalTarget")]
    [InlineData("FreeForAllScore")]
    [InlineData("TeamScore")]
    public void ScoreDeadlineCannotBeConvertedToALateTargetWin(string name)
    {
        var rule = Enum.Parse<RewardRoundRule>(name);
        var run = Run(rule, target: 1, timeLimit: 1000);
        Assert.Equal(RewardEvaluationStatus.Eligible, Evaluate(run, Death(1, 2, 1, at: 999)).Status);
        var deadline = Evaluate(run, Death(1, 2, 1, at: 1000));
        Assert.Equal(RewardEvaluationStatus.Rejected, deadline.Status);
        Assert.Equal(0, deadline.Roubles);
    }

    [Fact]
    public void NativeEndIdentityMustContainTheExactArmSuffix()
    {
        var run = Run();
        RewardResultValidator.ValidateAuthorization(run);
        foreach (var identity in new[]
                 {
                     "native-server-id", Protocol.BuildNativeEndIdentity("native-server-id", G(999)),
                     Protocol.BuildNativeEndIdentity("", run.ArmToken)
                 })
            Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(run with { NativeEndIdentity = identity }));
    }

    [Theory]
    [InlineData("factory4_day.Pmc 1792870000", true)]
    [InlineData("arena_bay5.Pmc 1", true)]
    [InlineData("native-server-id", true)]
    [InlineData("factory4_day.Pmc\t1792870000", false)]
    [InlineData("factory4_day.Pmc 1792870000\n", false)]
    [InlineData("factory4_day.Pmc  1792870000", false)]
    [InlineData("factory4_day.Pmc text", false)]
    [InlineData("factory4_day.Pmc 1|arenamode:other", false)]
    public void NativeServerTimestampIdentityIsAcceptedWithoutBroadWhitespaceAdmission(string prefix, bool accepted)
    {
        var run = Run();
        var identity = Protocol.BuildNativeEndIdentity(prefix, run.ArmToken);
        Assert.Equal(accepted, RewardResultValidator.ValidNativeIdentity(identity, run.ArmToken));
        if (accepted) RewardResultValidator.ValidateAuthorization(run with { NativeEndIdentity = identity });
    }

    [Fact]
    public void TacticalCannotAuthorizeAnIncompleteOpponentRosterOrMissingSquad()
    {
        foreach (var rule in new[] { RewardRoundRule.SoloElimination, RewardRoundRule.TeamElimination })
        {
            var run = Run(rule);
            RewardResultValidator.ValidateAuthorization(run);
            var shortRoster = run.Rounds[0] with { Actors = run.Rounds[0].Actors.Where(a => a.ParticipantId != P(6)).ToArray() };
            Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(run with { Rounds = [shortRoster] }));
        }
        var team = Run(RewardRoundRule.TeamElimination);
        var noCompanion = team.Rounds[0] with { Actors = team.Rounds[0].Actors.Where(a => a.ParticipantId != P(7)).ToArray() };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(team with { Rounds = [noCompanion] }));
    }

    [Fact]
    public void PolicyLimitsRejectRatherThanOverflowOrSilentlyClip()
    {
        var run = Run();
        var killCap = run with { Policy = run.Policy with { MaxKills = 1 } };
        Assert.Equal("reward-kill-limit", Evaluate(killCap, Death(1, 2, 1), Death(2, 3, 1)).Reason);
        var moneyCap = run with { Policy = run.Policy with { MaxRoubles = 150 } };
        Assert.Equal("reward-amount-limit", Evaluate(moneyCap, Death(1, 2, 1), Death(2, 3, 1)).Reason);
        var maximumRate = run with { Policy = run.Policy with { RoublesPerKill = 10_000_000 } };
        Assert.Equal("reward-amount-limit", Evaluate(maximumRate, Death(1, 2, 1), Death(2, 3, 1)).Reason);
        var disabledRates = run with { Policy = run.Policy with { RoublesPerKill = 0, RoublesPerRoundWin = 0 } };
        var zero = Evaluate(disabledRates, Death(1, 2, 1));
        Assert.Equal(RewardEvaluationStatus.NoReward, zero.Status);
        Assert.Equal(0, zero.Roubles);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("binding")]
    [InlineData("run")]
    [InlineData("sequence")]
    [InlineData("arm")]
    [InlineData("restore")]
    [InlineData("local")]
    [InlineData("native-end")]
    [InlineData("revision")]
    [InlineData("mode")]
    [InlineData("policy")]
    [InlineData("rate")]
    [InlineData("version")]
    [InlineData("kill-cap")]
    [InlineData("round-cap")]
    [InlineData("money-cap")]
    [InlineData("rounds")]
    [InlineData("null-round")]
    [InlineData("round-id")]
    [InlineData("rule")]
    [InlineData("rule-mode")]
    [InlineData("duration")]
    [InlineData("target")]
    [InlineData("actors")]
    [InlineData("null-actor")]
    [InlineData("life")]
    [InlineData("side")]
    [InlineData("spawn")]
    [InlineData("local-opponent")]
    [InlineData("no-local")]
    [InlineData("no-opponents")]
    public void MalformedAuthorityFailsBeforeClientEvaluation(string mutation)
    {
        var run = Run();
        var round = run.Rounds[0];
        run = mutation switch
        {
            "schema" => run with { Schema = 2 },
            "binding" => run with { Binding = null! },
            "run" => run with { RunId = "../other" },
            "sequence" => run with { Sequence = 0 },
            "arm" => run with { ArmToken = "invalid" },
            "restore" => run with { RestorationId = G(0) },
            "local" => run with { LocalParticipantId = "not-profile" },
            "native-end" => run with { NativeEndIdentity = "unbounded/identity" },
            "revision" => run with { CleanProfileRevision = "" },
            "mode" => run with { Mode = "unknown" },
            "policy" => run with { Policy = null! },
            "rate" => run with { Policy = run.Policy with { RoublesPerKill = long.MaxValue } },
            "version" => run with { Policy = run.Policy with { Version = "" } },
            "kill-cap" => run with { Policy = run.Policy with { MaxKills = RewardResultValidator.MaxLives + 1 } },
            "round-cap" => run with { Policy = run.Policy with { MaxRounds = 0 } },
            "money-cap" => run with { Policy = run.Policy with { MaxRoubles = 10_000_001 } },
            "rounds" => run with { Rounds = [] },
            "null-round" => run with { Rounds = [null!] },
            "round-id" => run with { Rounds = [round with { RoundId = "invalid" }] },
            "rule" => run with { Rounds = [round with { Rule = (RewardRoundRule)999 }] },
            "rule-mode" => run with { Mode = "gungame", Rounds = [round with { Rule = RewardRoundRule.LocalTarget, KillTarget = 1 }] },
            "duration" => run with { Rounds = [round with { ObservedDurationMilliseconds = RewardResultValidator.MaxDurationMilliseconds + 1 }] },
            "target" => run with { Rounds = [round with { KillTarget = 1 }] },
            "actors" => run with { Rounds = [round with { Actors = [] }] },
            "null-actor" => run with { Rounds = [round with { Actors = [Actor(1), null!] }] },
            "life" => run with { Rounds = [round with { Actors = [Actor(1), Actor(2, life: 1)] }] },
            "side" => run with { Rounds = [round with { Actors = [Actor(1), Actor(2, (RewardSide)999)] }] },
            "spawn" => run with { Rounds = [round with { Actors = [Actor(1), Actor(2, spawned: -1)] }] },
            "local-opponent" => run with { Rounds = [round with { Actors = [Actor(1, RewardSide.Opponent), Actor(2)] }] },
            "no-local" => run with { Rounds = [round with { Actors = [Actor(2), Actor(3)] }] },
            "no-opponents" => run with { Rounds = [round with { Actors = [Actor(1), Actor(2, RewardSide.Friendly)] }] },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.Evaluate(run, Report(run)));
    }

    [Fact]
    public void BodiesAndRoundIdsCannotBeReusedAcrossAuthorizations()
    {
        var run = Run();
        var duplicateRound = run with { Rounds = [run.Rounds[0], run.Rounds[0]] };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(duplicateRound));
        var reusedLife = run with { Rounds = [run.Rounds[0], run.Rounds[0] with { RoundId = G(22) }] };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(reusedLife));
        var sideChanged = run with
        {
            Rounds = [run.Rounds[0], run.Rounds[0] with
            {
                RoundId = G(22), Actors = [Actor(1, life: 11), Actor(2, RewardSide.Friendly, life: 12), Actor(3, life: 13)]
            }]
        };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(sideChanged));
    }

    [Fact]
    public void EliminationCannotAuthorizeRespawnsLateBodiesOrSoloAllies()
    {
        var run = Run(RewardRoundRule.SoloElimination);
        var round = run.Rounds[0];
        foreach (var actors in new RewardActor[][]
                 {
                     [Actor(1), Actor(2), Actor(2, life: 3), Actor(4), Actor(5), Actor(6)],
                     [Actor(1), Actor(2, spawned: 1), Actor(3), Actor(4), Actor(5), Actor(6)],
                     [Actor(1), Actor(2), Actor(3), Actor(4), Actor(5), Actor(6), Actor(7, RewardSide.Friendly)]
                 })
            Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(run with { Rounds = [round with { Actors = actors }] }));
    }

    private static RewardRunAuthorization Run(RewardRoundRule rule = RewardRoundRule.KillsOnly, int target = 0,
        long timeLimit = 0, IReadOnlyList<RewardActor>? actors = null)
    {
        var mode = rule switch
        {
            RewardRoundRule.TeamScore => "teamdeathmatch",
            RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination => "tactical",
            _ => "deathmatch"
        };
        var side = rule == RewardRoundRule.FreeForAllScore ? RewardSide.Individual : RewardSide.Opponent;
        actors ??= rule switch
        {
            RewardRoundRule.SoloElimination => [Actor(1), Actor(2), Actor(3), Actor(4), Actor(5), Actor(6)],
            RewardRoundRule.TeamElimination => [Actor(1), Actor(2), Actor(3), Actor(4), Actor(5), Actor(6), Actor(7, RewardSide.Friendly)],
            _ => [Actor(1, rule == RewardRoundRule.FreeForAllScore ? RewardSide.Individual : RewardSide.Friendly), Actor(2, side), Actor(3, side)]
        };
        return new(1, new(G(100), P(100), G(101)), G(102), 1, G(103), Protocol.BuildNativeEndIdentity("native-server-id", G(103)), G(105), P(1), mode,
            new string('a', 64), new("synthetic-v1", 100, 1000, 100, 10, 10_000_000),
            [new(G(21), rule, 10000, timeLimit, target, actors)]);
    }

    private static RewardMatchReport Report(RewardRunAuthorization run, params RewardDeathReport[] deaths) =>
        new(1, run.RunId, run.ArmToken, run.NativeEndIdentity, RewardReportState.Completed,
            [new(run.Rounds.Count > 0 && run.Rounds[0] is not null ? run.Rounds[0].RoundId : G(21), deaths)]);

    private static RewardEvaluation Evaluate(RewardRunAuthorization run, params RewardDeathReport[] deaths) =>
        RewardResultValidator.Evaluate(run, Report(run, deaths));

    private static RewardActor Actor(int participant, RewardSide? side = null, int? life = null, long spawned = 0) =>
        new(P(participant), G(life ?? participant), side ?? (participant == 1 ? RewardSide.Friendly : RewardSide.Opponent), spawned);

    private static RewardDeathReport Death(long sequence, int victim, int? killer,
        RewardDeathKind kind = RewardDeathKind.Combat, long? at = null) =>
        new(sequence, at ?? sequence * 1000, G(victim), killer.HasValue ? G(killer.Value) : null, kind);

    private static string G(int value) => value.ToString("x32");
    private static string P(int value) => value.ToString("x24");
}
