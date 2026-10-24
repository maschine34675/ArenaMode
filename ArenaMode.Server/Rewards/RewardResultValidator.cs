using System.Text.RegularExpressions;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// Validates a bounded combat report against separately server-owned rules and
/// registered bodies. It cannot observe the local simulation independently.
/// Victories are derived, never copied from an asserted winner/Survived status.
/// </summary>
internal static class RewardResultValidator
{
    internal const int MaxLives = 20_000;
    internal const long MaxDurationMilliseconds = 86_400_000;

    internal static void ValidateAuthorization(RewardRunAuthorization run)
    {
        if (run is null || run.Schema != 1 || run.Binding is null) Fail("invalid-run-authorization");
        run.Binding.Validate();
        if (!RewardIdentity.IsGuid(run.RunId) || run.Sequence is < 1 or > 4096 || !RewardIdentity.IsGuid(run.ArmToken)
            || !RewardIdentity.IsGuid(run.RestorationId) || !RewardIdentity.IsHex(run.LocalParticipantId, 24)
            || !ValidNativeIdentity(run.NativeEndIdentity, run.ArmToken) || !RewardIdentity.IsHex(run.CleanProfileRevision, 64)
            || run.Mode is not ("deathmatch" or "gungame" or "teamdeathmatch" or "tactical" or "zombies"))
            Fail("invalid-run-identity");
        var policy = run.Policy;
        if (policy is null || policy.Version is null || !Regex.IsMatch(policy.Version, "\\A[A-Za-z0-9._-]{1,32}\\z")
            || policy.RoublesPerKill is < 0 or > 10_000_000 || policy.RoublesPerRoundWin is < 0 or > 10_000_000
            || policy.MaxKills is < 1 or > MaxLives || policy.MaxRounds is < 1 or > 100
            || policy.MaxRoubles is < 1 or > 10_000_000)
            Fail("invalid-reward-policy");
        if (run.Rounds is null || run.Rounds.Count is < 1 or > 100 || run.Rounds.Count > policy.MaxRounds)
            Fail("invalid-authorized-round-count");
        var rounds = new HashSet<string>(StringComparer.Ordinal);
        var lives = new HashSet<string>(StringComparer.Ordinal);
        var sides = new Dictionary<string, RewardSide>(StringComparer.Ordinal);
        long duration = 0;
        foreach (var round in run.Rounds)
        {
            if (round is null || !RewardIdentity.IsGuid(round.RoundId) || !rounds.Add(round.RoundId)
                || !Enum.IsDefined(round.Rule) || !RuleFitsMode(run.Mode, round.Rule)
                || round.ObservedDurationMilliseconds is <= 0 or > MaxDurationMilliseconds
                || round.TimeLimitMilliseconds is < 0 or > MaxDurationMilliseconds
                || round.KillTarget is < 0 or > MaxLives || round.Actors is null || round.Actors.Count is < 2 or > MaxLives)
                Fail("invalid-authorized-round");
            if (round.AllowIncomplete && !ReferenceEquals(round, run.Rounds[^1])) Fail("incomplete-round-not-last");
            duration += round.ObservedDurationMilliseconds;
            if (duration > MaxDurationMilliseconds) Fail("run-duration-limit");
            if (round.Rule is RewardRoundRule.LocalTarget or RewardRoundRule.FreeForAllScore or RewardRoundRule.TeamScore)
            {
                if (round.KillTarget == 0 && round.TimeLimitMilliseconds == 0) Fail("missing-round-end-rule");
            }
            else if (round.KillTarget != 0) Fail("unexpected-kill-target");

            var participants = new HashSet<string>(StringComparer.Ordinal);
            var localCount = 0;
            var enemyCount = 0;
            foreach (var actor in round.Actors)
            {
                if (actor is null || !RewardIdentity.IsHex(actor.ParticipantId, 24) || !RewardIdentity.IsGuid(actor.LifeId)
                    || !lives.Add(actor.LifeId) || lives.Count > MaxLives || !Enum.IsDefined(actor.Side)
                    || actor.SpawnedAtMilliseconds < 0 || actor.SpawnedAtMilliseconds > round.ObservedDurationMilliseconds)
                    Fail("invalid-registered-life");
                if (sides.TryGetValue(actor.ParticipantId, out var side) && side != actor.Side) Fail("participant-side-changed");
                sides[actor.ParticipantId] = actor.Side;
                if (round.Rule == RewardRoundRule.FreeForAllScore)
                {
                    if (actor.Side != RewardSide.Individual) Fail("invalid-ffa-side");
                }
                else if (actor.Side == RewardSide.Individual) Fail("missing-team-side");
                if (actor.ParticipantId == run.LocalParticipantId)
                {
                    localCount++;
                    if (actor.Side == RewardSide.Opponent) Fail("local-side-mismatch");
                }
                else if (actor.Side == RewardSide.Opponent || round.Rule == RewardRoundRule.FreeForAllScore) enemyCount++;
                if (round.Rule is RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination)
                {
                    if (!participants.Add(actor.ParticipantId) || actor.SpawnedAtMilliseconds != 0)
                        Fail("elimination-roster-respawn-or-late-spawn");
                    if (round.Rule == RewardRoundRule.SoloElimination && actor.Side == RewardSide.Friendly
                        && actor.ParticipantId != run.LocalParticipantId) Fail("solo-roster-has-allies");
                }
            }
            if (localCount == 0 || enemyCount == 0) Fail("missing-local-or-opponents");
            if (round.Rule is RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination)
            {
                var friends = round.Actors.Count(a => a.Side == RewardSide.Friendly);
                if (enemyCount != 5 || (round.Rule == RewardRoundRule.SoloElimination ? friends != 1 : friends is < 2 or > 5))
                    Fail("incomplete-tactical-roster");
            }
        }
    }

    internal static RewardEvaluation Evaluate(RewardRunAuthorization run, RewardMatchReport report)
    {
        ValidateAuthorization(run); // Authority errors must not be reclassified as a client's rejected result.
        try
        {
            if (report is null || report.Schema != 1 || report.RunId != run.RunId || report.ArmToken != run.ArmToken
                || report.NativeEndIdentity != run.NativeEndIdentity || !Enum.IsDefined(report.State)
                || report.Rounds is null || report.Rounds.Count != run.Rounds.Count)
                Fail("result-identity-or-rounds-mismatch");
            if (report.State != RewardReportState.Completed)
                return new(RewardEvaluationStatus.NoReward, "run-not-completed", 0, 0, 0);
            var kills = 0;
            var wins = 0;
            long sequence = 0;
            for (var i = 0; i < run.Rounds.Count; i++)
            {
                var round = run.Rounds[i];
                var result = report.Rounds[i];
                if (result is null || result.RoundId != round.RoundId || result.Deaths is null || result.Deaths.Count > round.Actors.Count)
                    Fail("result-round-order-or-count");
                var actors = round.Actors.ToDictionary(a => a.LifeId, StringComparer.Ordinal);
                var dead = new HashSet<string>(StringComparer.Ordinal);
                var scores = round.Actors.Select(a => a.ParticipantId).Distinct(StringComparer.Ordinal)
                    .ToDictionary(p => p, _ => 0, StringComparer.Ordinal);
                long previousTime = -1;
                var friendlyScore = 0;
                var opponentScore = 0;
                var highestScore = 0;
                bool? terminalWin = null;
                for (var deathIndex = 0; deathIndex < result.Deaths.Count; deathIndex++)
                {
                    var death = result.Deaths[deathIndex];
                    if (terminalWin.HasValue) Fail("events-after-round-result");
                    RewardActor? victim = null;
                    if (death is null || death.Sequence != ++sequence || !Enum.IsDefined(death.Kind)
                        || death.AtMilliseconds < previousTime || death.AtMilliseconds > round.ObservedDurationMilliseconds
                        || !actors.TryGetValue(death.VictimLifeId ?? "", out victim)
                        || victim.SpawnedAtMilliseconds > death.AtMilliseconds || !dead.Add(victim.LifeId))
                        Fail("invalid-or-repeated-death");
                    previousTime = death.AtMilliseconds;
                    // Solo Tactical checks its timeout before elimination; PFT
                    // checks elimination first. Preserve that exact distinction.
                    var scoringDeadline = round.Rule is RewardRoundRule.LocalTarget or RewardRoundRule.FreeForAllScore
                        or RewardRoundRule.TeamScore or RewardRoundRule.SoloElimination;
                    if (round.TimeLimitMilliseconds > 0 && (death.AtMilliseconds > round.TimeLimitMilliseconds
                        || scoringDeadline && death.AtMilliseconds == round.TimeLimitMilliseconds))
                        Fail("event-after-time-limit");
                    RewardActor? killer = null;
                    if (death.Kind == RewardDeathKind.Combat)
                    {
                        if (death.KillerLifeId is null || !actors.TryGetValue(death.KillerLifeId, out killer)
                            || killer.SpawnedAtMilliseconds > death.AtMilliseconds) Fail("unknown-killer-life");
                    }
                    else if (death.KillerLifeId is not null) Fail("noncombat-killer");
                    if (death.Kind == RewardDeathKind.Administrative)
                        return new(RewardEvaluationStatus.NoReward, "administrative-removal", 0, 0, 0);
                    // Delayed grenades/fire can kill after their owner died. Do not
                    // demand a currently living attacker or count it as a respawn.
                    var hostile = killer is not null && killer.ParticipantId != victim.ParticipantId
                        && (round.Rule == RewardRoundRule.FreeForAllScore || killer.Side != victim.Side);
                    if (hostile)
                    {
                        scores[killer!.ParticipantId]++;
                        highestScore = Math.Max(highestScore, scores[killer.ParticipantId]);
                        if (killer.Side == RewardSide.Friendly) friendlyScore++;
                        else if (killer.Side == RewardSide.Opponent) opponentScore++;
                        if (killer.ParticipantId == run.LocalParticipantId) kills++;
                    }
                    // Only the explicit local give-up contributes this virtual
                    // opponent score; ordinary suicides/environment deaths do not.
                    if (death.Kind == RewardDeathKind.LocalForfeit)
                    {
                        if (run.Mode != "teamdeathmatch"
                            || round.Rule is not (RewardRoundRule.TeamScore or RewardRoundRule.KillsOnly)
                            || victim.ParticipantId != run.LocalParticipantId)
                            Fail("invalid-local-forfeit");
                        if (round.Rule == RewardRoundRule.TeamScore) opponentScore++;
                    }
                    if (round.KillTarget > 0)
                    {
                        if (round.Rule == RewardRoundRule.LocalTarget && scores[run.LocalParticipantId] >= round.KillTarget) terminalWin = true;
                        if (round.Rule == RewardRoundRule.FreeForAllScore && highestScore >= round.KillTarget)
                            terminalWin = scores[run.LocalParticipantId] >= round.KillTarget;
                        if (round.Rule == RewardRoundRule.TeamScore && Math.Max(friendlyScore, opponentScore) >= round.KillTarget)
                            terminalWin = friendlyScore > opponentScore;
                    }
                    // Elimination resolves AFTER the entire same-timestamp batch:
                    // simultaneous wipe is a loss, not a premature friendly win.
                    if (round.Rule is RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination
                        && (deathIndex + 1 == result.Deaths.Count || result.Deaths[deathIndex + 1]?.AtMilliseconds != death.AtMilliseconds))
                    {
                        var friendlyAlive = round.Actors.Count(a => a.Side == RewardSide.Friendly && !dead.Contains(a.LifeId));
                        var enemyAlive = round.Actors.Count(a => a.Side == RewardSide.Opponent && !dead.Contains(a.LifeId));
                        if (friendlyAlive == 0) terminalWin = false;
                        else if (enemyAlive == 0) terminalWin = true;
                    }
                }
                if (!terminalWin.HasValue)
                {
                    var timedOut = round.TimeLimitMilliseconds > 0 && round.ObservedDurationMilliseconds >= round.TimeLimitMilliseconds;
                    terminalWin = round.Rule switch
                    {
                        RewardRoundRule.KillsOnly => false,
                        RewardRoundRule.LocalTarget when timedOut => false,
                        RewardRoundRule.SoloElimination when timedOut => false,
                        RewardRoundRule.TeamElimination when timedOut => round.Actors.Count(a => a.Side == RewardSide.Friendly && !dead.Contains(a.LifeId))
                            > round.Actors.Count(a => a.Side == RewardSide.Opponent && !dead.Contains(a.LifeId)),
                        RewardRoundRule.TeamScore when timedOut => friendlyScore > opponentScore,
                        RewardRoundRule.FreeForAllScore when timedOut => scores[run.LocalParticipantId] > 0
                            && scores.All(p => p.Key == run.LocalParticipantId || p.Value < scores[run.LocalParticipantId]),
                        _ => null
                    };
                }
                if (!terminalWin.HasValue && round.AllowIncomplete) terminalWin = false;
                if (!terminalWin.HasValue) Fail("unfinished-round");
                if (terminalWin.Value) wins++;
            }
            if (kills > run.Policy.MaxKills) Fail("reward-kill-limit");
            var amount = checked((long)kills * run.Policy.RoublesPerKill + (long)wins * run.Policy.RoublesPerRoundWin);
            if (amount > run.Policy.MaxRoubles)
            {
                if (!run.Policy.CapRoubles) Fail("reward-amount-limit");
                amount = run.Policy.MaxRoubles;
            }
            return new(amount == 0 ? RewardEvaluationStatus.NoReward : RewardEvaluationStatus.Eligible,
                amount == 0 ? "no-eligible-reward" : "validated-result", kills, wins, amount);
        }
        catch (RewardDataException e) { return new(RewardEvaluationStatus.Rejected, e.Message, 0, 0, 0); }
        catch (OverflowException) { return new(RewardEvaluationStatus.Rejected, "reward-arithmetic-overflow", 0, 0, 0); }
    }

    private static bool RuleFitsMode(string mode, RewardRoundRule rule) => mode switch
    {
        "deathmatch" => rule is RewardRoundRule.KillsOnly or RewardRoundRule.LocalTarget or RewardRoundRule.FreeForAllScore,
        "teamdeathmatch" => rule is RewardRoundRule.TeamScore or RewardRoundRule.KillsOnly,
        "tactical" => rule is RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination,
        // Ladder skips/demotions and administrative zombie wave retirements need
        // further evidence. Do not invent a round win from a kill total there.
        "gungame" or "zombies" => rule == RewardRoundRule.KillsOnly,
        _ => false
    };

    internal static bool ValidNativeIdentity(string value, string armToken)
    {
        if (value is null || value.Length > 192 || !ArenaMode.Shared.Protocol.HasNativeEndIdentity(value, armToken)) return false;
        var prefix = value[..^(ArenaMode.Shared.Protocol.NativeEndIdentitySeparator.Length + armToken.Length)];
        // Installed SPT 4.1.6 uses "location.PlayerSide unixTimestamp". The
        // space before its decimal timestamp is native, not an Arena delimiter.
        return prefix.Length <= 128 && Regex.IsMatch(prefix, "\\A[A-Za-z0-9_.:-]+(?: [0-9]{1,19})?\\z");
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string reason) => throw new RewardDataException(reason);
}
