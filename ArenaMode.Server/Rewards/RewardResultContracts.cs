namespace ArenaMode.Server.Rewards;

// Internal server-owned contracts, NOT HTTP DTOs. A future lifecycle adapter must
// freeze policy/roster at the protected match boundary and positively witness
// restore + disk read-back. Copying these fields from a client would defeat this
// design. Only RewardMatchReport is untrusted combat input. See REWARDS-RESULTS.md.
internal sealed record RewardRatePolicy(string Version, long RoublesPerKill, long RoublesPerRoundWin,
    int MaxKills, int MaxRounds, long MaxRoubles, bool CapRoubles = false);

internal enum RewardRoundRule { KillsOnly, LocalTarget, FreeForAllScore, TeamScore, SoloElimination, TeamElimination }
internal enum RewardSide { Individual, Friendly, Opponent }
internal enum RewardReportState { Completed, Aborted, NoContest }
internal enum RewardDeathKind { Combat, Environment, Administrative, LocalForfeit }
internal enum RewardEvaluationStatus { Eligible, NoReward, Rejected }

// ParticipantId is the native individual identity (not a recycled bot slot),
// LifeId a fresh server-registered physical activation. A player's own respawns
// can reuse the participant but never a life, including across rounds. Replacing
// a bot in FFA must not inherit that previous bot's individual score.
internal sealed record RewardActor(string ParticipantId, string LifeId, RewardSide Side, long SpawnedAtMilliseconds);
internal sealed record RewardRoundAuthorization(string RoundId, RewardRoundRule Rule, long ObservedDurationMilliseconds,
    long TimeLimitMilliseconds, int KillTarget, IReadOnlyList<RewardActor> Actors, bool AllowIncomplete = false);
internal sealed record RewardRunAuthorization(int Schema, RewardBinding Binding, string RunId, long Sequence, string ArmToken,
    string NativeEndIdentity, string RestorationId, string LocalParticipantId, string Mode,
    string CleanProfileRevision, RewardRatePolicy Policy, IReadOnlyList<RewardRoundAuthorization> Rounds);

internal sealed record RewardDeathReport(long Sequence, long AtMilliseconds, string VictimLifeId,
    string? KillerLifeId, RewardDeathKind Kind);
internal sealed record RewardRoundReport(string RoundId, IReadOnlyList<RewardDeathReport> Deaths);
// No amount, points, winning flag, or caller-chosen profile/path enters this type.
internal sealed record RewardMatchReport(int Schema, string RunId, string ArmToken, string NativeEndIdentity,
    RewardReportState State, IReadOnlyList<RewardRoundReport> Rounds);

internal enum RewardRestorationState { Pending, Persisted }
internal sealed record RewardRestorationEvidence(int Schema, RewardBinding Binding, string RunId, string ArmToken,
    string NativeEndIdentity, string RestorationId, RewardRestorationState State,
    string ExpectedProfileRevision, string PersistedProfileRevision);

// The future trusted coordinator writes this envelope to candidate.json. It must
// never offer an endpoint that accepts the whole envelope from a game client.
internal sealed record RewardApprovalCandidate(int Schema, RewardRunAuthorization Authorization,
    RewardMatchReport Report, RewardRestorationEvidence Restoration);
internal sealed record RewardEvaluation(RewardEvaluationStatus Status, string Reason, int EligibleKills, int RoundWins, long Roubles);
internal sealed record RewardApprovalDecision(int Schema, RewardBinding Binding, string RunId, long Sequence, string SourceFingerprint,
    RewardEvaluation Evaluation, RewardStartupApproval? Approval);
