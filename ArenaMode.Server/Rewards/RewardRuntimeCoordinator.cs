using System.Text.Json;
using ArenaMode.Shared;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// Capture and durable metadata only. This service never writes a live or
/// serialized player profile; enrollment/payment belong to the startup window.
/// Caller hooks catch reward failures without weakening full-profile recovery.
/// </summary>
internal sealed class RewardRuntimeCoordinator
{
    private static readonly Lazy<RewardRuntimeCoordinator> Instance = new(() => new(Directory.GetCurrentDirectory()));
    internal static RewardRuntimeCoordinator Current => Instance.Value;
    internal static readonly RewardRatePolicy Policy = new("arena-v2", 1000, 10_000, 20_000, 100, 200_000, CapRoubles: true,
        ExperiencePerKill: 100, ExperiencePerRoundWin: 1000, MaxExperience: 20_000);
    private readonly string _root;
    private readonly object _sync = new();
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Run> _runs = new(StringComparer.Ordinal);
    // Presentation only: these receipts are already durable in this private
    // process's outbox, but the normal server sees them only after return.
    private readonly Dictionary<string, (long Roubles, long Experience, bool Cola)> _privatePending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _startupFailures = new(StringComparer.Ordinal);
    private string? _startupUnavailable;
    private readonly Func<DateTimeOffset> _now;
    private readonly RewardConsentControl _control;
    private readonly RewardPrivateConsent _privateConsent;
    internal RewardReturnControl ReturnControl { get; }

    internal RewardRuntimeCoordinator(string root, Func<DateTimeOffset>? now = null,
        Func<RewardControlGrant, RewardControlRequest, CancellationToken, Task<RewardControlReply>>? controlTransport = null)
    {
        _root = RewardStartupFiles.Canonical(root);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _control = new(_root, _now);
        _privateConsent = new(_root, _now, controlTransport);
        ReturnControl = new(_root, _now);
    }

    internal bool HasCapture(string id)
    {
        lock (_sync) return _runs.ContainsKey(id) || _snapshots.ContainsKey(id);
    }

    internal void ObserveStartupOutcome(RewardStartupOutcome outcome)
    {
        lock (_sync)
        {
            if (outcome.Result.Status is RewardSettlementStatus.Quarantined or RewardSettlementStatus.Rejected
                || outcome.Result.Reason == "startup-storage-unavailable")
                _startupFailures[outcome.ProfileId] = outcome.Result.Reason;
        }
    }

    internal void ObserveStartupUnavailable(string reason)
    { lock (_sync) _startupUnavailable = reason; }

    internal RewardStatusResponse GetStatus(string id)
    {
        lock (_sync)
        {
            var result = new RewardStatusResponse
            {
                Available = true, PrivateSession = !RewardStartupFiles.MarkerAbsent(_root),
                AutomaticReturn = ReturnControl.HasGrant(id),
                RoublesPerKill = Policy.RoublesPerKill, RoublesPerRoundWin = Policy.RoublesPerRoundWin, MaxRoubles = Policy.MaxRoubles,
                ExperiencePerKill = Policy.ExperiencePerKill, ExperiencePerRoundWin = Policy.ExperiencePerRoundWin, MaxExperience = Policy.MaxExperience
            };
            result.CanChange = !result.PrivateSession && !_snapshots.ContainsKey(id) && !_runs.ContainsKey(id);
            if (result.PrivateSession && _privateConsent.HasControl)
            {
                result = _privateConsent.Status();
                if (!_privateConsent.IsOffline) result.AutomaticReturn &= RewardReturnControl.PrivateGrantAvailable(_root, id);
                result.CanChange &= !_snapshots.ContainsKey(id) && !_runs.ContainsKey(id);
                if (_runs.TryGetValue(id, out var active))
                {
                    result.State = "earning";
                    if (active.Final is not null) SetPending(result, Evaluate(active, active.Final), active.Cola);
                }
                ApplyPrivatePending(id, result);
                ApplyFailure(id, result);
                return result;
            }
            try
            {
                if (!result.PrivateSession) result.ConsentRevision = RewardEnrollment.ReadConsent(_root, id).Revision;
                var authority = Authority(id, required: false);
                result.Enabled = result.PrivateSession ? authority?.Enabled == true : RewardEnrollment.WantsRewards(_root, id);
                result.Enrolled = authority is not null;
                result.State = !result.Enabled ? "disabled" : !result.Enrolled ? "restart-required" : "ready";
                result.Detail = !result.Enabled ? "Optional profile rewards are off."
                    : !result.Enrolled ? "Restart the normal SPT server once to activate rewards."
                    : "At the next normal server start: RUB by mail, XP credited directly.";
                if (_runs.TryGetValue(id, out var run))
                {
                    result.State = "earning";
                    if (run.Final is not null) SetPending(result, Evaluate(run, run.Final), run.Cola);
                }
                if (!result.PrivateSession && result.Enrolled)
                {
                    var directory = RewardRuntimeFiles.DirectoryFor(_root, id);
                    var profile = RewardRecoveryEvidence.ReadProfile(_root, id);
                    var closures = RewardRecovery.ReadClosures(_root, id, profile);
                    var inbox = Path.Combine(directory, "inbox");
                    if (RewardRuntimeFiles.Exists(_root, inbox))
                    {
                        var pending = Directory.EnumerateFiles(inbox, "*.json").Take(4097).ToArray();
                        if (pending.Length > 4096) throw new RewardDataException("reward-inbox-capacity");
                        foreach (var path in pending)
                        {
                            var name = Path.GetFileNameWithoutExtension(path);
                            if (!RewardIdentity.IsGuid(name)) throw new RewardDataException("invalid-reward-inbox-name");
                            var imported = Path.Combine(directory, "imported", name + ".json");
                            if (RewardRuntimeFiles.Exists(_root, imported)) continue;
                            var earned = RewardRuntimeInbox.ReadReceipt(_root, directory, authority!, path);
                            var allocationPath = Path.Combine(directory, "allocations", name + ".json");
                            RewardAllocation? allocation = null;
                            if (RewardRuntimeFiles.Exists(_root, allocationPath))
                            {
                                allocation = RewardStartupFiles.Read<RewardAllocation>(allocationPath);
                                RewardRuntimeInbox.RequireAllocationSource(allocation, earned, RewardIdentity.Hash(RewardRuntimeFiles.Json(earned)));
                            }
                            if (closures.ClosesReceipt(earned.Evidence)
                                || allocation is not null && closures.ClosesReceipt(allocation.Candidate)) continue;
                            var reward = RewardResultValidator.Evaluate(earned.Evidence.Authorization, earned.Evidence.Report);
                            result.PendingRoubles = checked(result.PendingRoubles + reward.Roubles);
                            result.PendingExperience = checked(result.PendingExperience + reward.Experience);
                            result.PendingCola |= reward.Status == RewardEvaluationStatus.Eligible && earned.Evidence.Authorization.Cola is not null;
                        }
                        if ((result.PendingRoubles > 0 || result.PendingCola) && !_runs.ContainsKey(id)) result.State = "pending";
                    }
                    var acknowledgement = Path.Combine(directory, "acknowledged.json");
                    if (RewardRuntimeFiles.Exists(_root, acknowledgement))
                    {
                        var ack = RewardStartupFiles.Read<RewardStartupAcknowledgement>(acknowledgement, 16384);
                        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(Path.Combine(directory, "journal.json")), authority!.Binding, new());
                        var ledger = RewardProfileDocument.ReadLedger(profile, authority.Binding, new());
                        var payment = journal.Entries.SingleOrDefault(e => e.Payment.ReceiptKey == ack.ReceiptKey
                            && e.Payment.Fingerprint == ack.Fingerprint && e.Phase == RewardJournalPhase.Committed
                            && !closures.ClosesPayment(e) && ledger.Contains(e.Payment))?.Payment;
                        result.LastPaidRoubles = payment?.Roubles ?? 0;
                        result.LastPaidExperience = payment?.Experience ?? 0;
                        result.LastPaidCola = payment?.Cola is not null;
                        if ((result.LastPaidRoubles > 0 || result.LastPaidCola) && result.State == "ready") result.State = "paid";
                    }
                }
                ApplyFailure(id, result);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or RewardDataException or JsonException or FormatException)
            {
                result.State = "withheld";
                result.Detail = "Rewards are temporarily unavailable.";
                result.CanChange = false;
            }
            ApplyPrivatePending(id, result);
            return result;
        }
    }

    private static void SetPending(RewardStatusResponse status, RewardEvaluation reward, string? cola)
    {
        status.PendingRoubles = reward.Roubles;
        status.PendingExperience = reward.Experience;
        status.PendingCola = reward.Status == RewardEvaluationStatus.Eligible && cola is not null;
    }

    private void ApplyPrivatePending(string id, RewardStatusResponse status)
    {
        if (!status.PrivateSession || !_privatePending.TryGetValue(id, out var pending)) return;
        status.PendingRoubles = checked(status.PendingRoubles + pending.Roubles);
        status.PendingExperience = checked(status.PendingExperience + pending.Experience);
        status.PendingCola |= pending.Cola;
        if (status.Available && status.Enabled && status.State is "ready" or "paid"
            && (status.PendingRoubles > 0 || status.PendingExperience > 0 || status.PendingCola)) status.State = "pending";
    }

    private void ApplyFailure(string id, RewardStatusResponse result)
    {
        string? failure = _failures.GetValueOrDefault(id) ?? _startupFailures.GetValueOrDefault(id) ?? _startupUnavailable;
        if (failure is null) return;
        result.State = "withheld";
        result.Detail = _failures.ContainsKey(id) ? "No reward for the last match." : "Rewards are temporarily unavailable.";
    }

    internal RewardStatusResponse SetConsent(string id, bool enabled)
    {
        lock (_sync)
        {
            if (_snapshots.ContainsKey(id) || _runs.ContainsKey(id)) throw new RewardDataException("reward-session-active");
            RewardEnrollment.SetConsent(_root, id, enabled);
            return GetStatus(id);
        }
    }

    internal Task RefreshPrivateConsentAsync(string id, CancellationToken ct = default)
        => _privateConsent.HasControl ? _privateConsent.RefreshAsync(id, null, ct) : Task.CompletedTask;

    internal async Task<RewardStatusResponse> SetConsentAsync(string id, bool enabled, CancellationToken ct, long? expectedRevision = null)
    {
        if (!_privateConsent.HasControl)
        {
            lock (_sync)
            {
                if (expectedRevision.HasValue && expectedRevision != RewardEnrollment.ReadConsent(_root, id).Revision)
                {
                    var status = GetStatus(id);
                    status.State = "changed";
                    status.Detail = "Reward preference changed elsewhere. Review the current setting before trying again.";
                    return status;
                }
                return SetConsent(id, enabled);
            }
        }
        lock (_sync)
            if (_snapshots.ContainsKey(id) || _runs.ContainsKey(id)) throw new RewardDataException("reward-session-active");
        // The route holds ArenaRaidStartGate across this await; no snapshot lock is held.
        await _privateConsent.RefreshAsync(id, enabled, ct, expectedRevision);
        return GetStatus(id);
    }

    internal RewardControlPrepareResponse PrepareControl(string id, string controlId)
    {
        lock (_sync)
        {
            if (!GetStatus(id).CanChange) return new() { Reason = "reward-control-unavailable" };
            var grant = _control.Issue(id, controlId);
            // A separate capability authorizes a graceful local return. Unsupported hosts
            // retain the existing session/consent flow; this never broadens the consent token.
            try
            {
                if (!ReturnControl.Issue(grant))
                    ArenaContext.Logger?.Debug("[ArenaMode] Automatic reward return: entry capability unavailable; startup payout remains enabled.");
            }
            catch (Exception exception)
            {
                ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return: entry capability could not be prepared ({exception.GetType().Name}).");
            }
            return new() { Accepted = true };
        }
    }

    internal RewardControlReply ExchangeControl(string id, RewardControlRequest request, bool canChange)
    {
        lock (_sync)
        {
            if (!_control.Accepts(id, request)) return new(false, "control-expired-or-invalid", 0, null, null);
            var consent = RewardEnrollment.ReadConsent(_root, id);
            string reason = "";
            if (request.Enabled.HasValue)
            {
                if (!canChange || _snapshots.ContainsKey(id) || _runs.ContainsKey(id))
                    return new(false, "reward-session-active", consent.Revision, null, null);
                if (request.ExpectedRevision != consent.Revision) reason = "stale-consent";
                else SetConsent(id, request.Enabled.Value);
            }
            var status = GetStatus(id);
            status.CanChange &= canChange;
            return new(reason.Length == 0, reason, RewardEnrollment.ReadConsent(_root, id).Revision, status,
                status.Available && status.Enabled && status.Enrolled ? Authority(id, false) : null);
        }
    }

    internal void ObserveSnapshot(string id, string json)
        => ObserveSnapshot(id, () => json);

    internal void ObserveSnapshot(string id, Func<string> serialize)
    {
        lock (_sync)
        {
            // The native snapshot service has already taken a NEW clone. Even
            // if serialization/authority/storage fails below, an older reward
            // generation must never authorize this new raid from retained RAM.
            if (_snapshots.Remove(id) | _runs.Remove(id))
                _failures[id] = "reward-previous-generation-unresolved";
            var authority = Authority(id, required: false);
            if (authority is null) return;
            var generation = Guid.NewGuid().ToString("N");
            var snapshot = new Snapshot(serialize(), generation, authority);
            RewardRestorationCheckpoint.Create(_root, id, generation, authority, snapshot.Json);
            var path = SnapshotPath(id, generation);
            RewardRuntimeFiles.EnsureDirectory(_root, Path.GetDirectoryName(path)!);
            RewardRuntimeFiles.WriteNew(_root, path, new RewardSnapshotGuard(1, generation, "pending"));
            _snapshots.Add(id, snapshot);
        }
    }

    internal void DiscardSnapshot(string id)
    {
        lock (_sync)
        {
            if (_runs.Remove(id))
            {
                // Once a run began, a generic discard is not positive evidence
                // that its native end restored and persisted the snapshot.
                _failures[id] = "reward-active-snapshot-discarded";
                _snapshots.Remove(id);
                return;
            }
            if (_snapshots.Remove(id, out var snapshot)) CloseSnapshot(id, snapshot, "cancelled-before-start");
        }
    }

    internal RewardBeginResponse Begin(string id, string expectedArm, string expectedNativeEnd, string localParticipantId,
        string expectedMode, bool expectedTeam, bool expectedFfa, RewardBeginRequest request)
    {
        lock (_sync)
        {
            try
            {
                // Native Arena mode IDs and the internal reward vocabulary are
                // separate. Validate the exact armed ID before normalization.
                var canonicalMode = expectedMode == "zombie" ? "zombies" : expectedMode;
                if (request is null || request.Schema != 1 || request.ArmToken != expectedArm || request.NativeEndIdentity != expectedNativeEnd
                    || request.Mode != expectedMode || !RewardIdentity.IsGuid(expectedArm) || !RewardIdentity.IsHex(localParticipantId, 24)
                    || !RewardResultValidator.ValidNativeIdentity(expectedNativeEnd, expectedArm) || request.TimeLimitMilliseconds is < 0 or > 86_400_000
                    || request.KillTarget is < 0 or > 20_000 || !Enum.TryParse<RewardRoundRule>(request.Rule, false, out var rule)
                    || !Enum.IsDefined(rule) || !RuleAllowed(canonicalMode, expectedTeam, expectedFfa, rule)
                    || rule is RewardRoundRule.KillsOnly or RewardRoundRule.SoloElimination or RewardRoundRule.TeamElimination && request.KillTarget != 0
                    || expectedMode == "teamdeathmatch" && rule == RewardRoundRule.KillsOnly && request.TimeLimitMilliseconds != 0
                    || rule is RewardRoundRule.LocalTarget or RewardRoundRule.FreeForAllScore or RewardRoundRule.TeamScore
                        && request.KillTarget == 0 && request.TimeLimitMilliseconds == 0)
                    throw new RewardDataException("reward-begin-mismatch");
                if (!_snapshots.TryGetValue(id, out var snapshot)) throw new RewardDataException("reward-snapshot-not-observed");
                if (_runs.TryGetValue(id, out var prior))
                {
                    if (prior.ArmToken != expectedArm || prior.NativeEnd != expectedNativeEnd || prior.BeginJson != RewardRuntimeFiles.Json(request))
                        throw new RewardDataException("reward-run-already-active");
                    return new() { Accepted = true, RunId = prior.Id };
                }
                var authority = Authority(id, required: true)!;
                if (snapshot.Binding != authority.Binding) throw new RewardDataException("reward-snapshot-binding-changed");
                var run = new Run(Guid.NewGuid().ToString("N"), expectedArm, expectedNativeEnd, localParticipantId, canonicalMode,
                    rule, request.TimeLimitMilliseconds, request.KillTarget, authority, snapshot, _now(), RewardRuntimeFiles.Json(request));
                var pending = PendingPath(id, run.Id);
                RewardRuntimeFiles.EnsureDirectory(_root, Path.GetDirectoryName(pending)!);
                RewardRestorationCheckpoint.BindRun(_root, id, snapshot.Generation, run.Id, expectedArm, expectedNativeEnd);
                RewardRuntimeFiles.WriteNew(_root, pending, new RewardRuntimePending(1, expectedArm, run.Id));
                _runs.Add(id, run);
                return new() { Accepted = true, RunId = run.Id };
            }
            catch (RewardDataException e) { return new() { Reason = e.Message }; }
        }
    }

    internal RewardCaptureResponse Capture(string id, RewardCaptureRequest request)
    {
        lock (_sync)
        {
            try
            {
                if (!_runs.TryGetValue(id, out var run)) throw new RewardDataException("reward-run-not-active");
                if (request is null || request.Schema != 1 || request.RunId != run.Id || request.ArmToken != run.ArmToken
                    || request.NativeEndIdentity != run.NativeEnd || request.Revision is < 1 or > 100_000
                    || request.Rounds is null || request.Rounds.Count is < 1 or > 100
                    || !Enum.TryParse<RewardReportState>(request.State, false, out var state) || !Enum.IsDefined(state))
                    throw new RewardDataException("reward-capture-identity");
                var json = RewardRuntimeFiles.Json(request);
                if (System.Text.Encoding.UTF8.GetByteCount(json) > 2_000_000) throw new RewardDataException("reward-capture-too-large");
                if (run.Last is not null)
                {
                    if (request.Revision == run.Last.Revision)
                    {
                        if (json != RewardRuntimeFiles.Json(run.Last)) throw new RewardDataException("reward-revision-conflict");
                        return new() { Accepted = true, Revision = request.Revision };
                    }
                    if (run.Final is not null || request.Revision <= run.Last.Revision) throw new RewardDataException("reward-capture-already-final");
                    RequireAppendOnly(run.Last, request);
                }
                // Independent wall clock is an upper bound only; the report's
                // active clock may pause or spend time in countdowns/loading.
                long duration = 0;
                foreach (var round in request.Rounds)
                    duration = checked(duration + round.ObservedDurationMilliseconds);
                if (duration > (_now() - run.Began).TotalMilliseconds + 5000) throw new RewardDataException("reward-active-time-exceeds-session");
                if (!request.Final && request.ColaState is not null) throw new RewardDataException("cola-observation-requires-final");
                var cola = request.Final && state == RewardReportState.Completed
                    ? Compat.ArenaTarkovaColaState.Capture(id, run.ArmToken, run.Mode, request.ColaState) : null;
                var candidate = BuildEvidence(run, request, RewardIdentity.Hash(run.Snapshot.Json), cola);
                RewardResultValidator.ValidateAuthorization(candidate.Authorization);
                if (request.Rounds.Take(request.Rounds.Count - 1).Any(r => !r.Closed)) throw new RewardDataException("reward-earlier-round-open");
                // Validate deaths even while a timed/scoring round is in
                // progress. The only expected nonterminal failure is unfinished.
                var evaluation = RewardResultValidator.Evaluate(candidate.Authorization, candidate.Report);
                if (evaluation.Status == RewardEvaluationStatus.Rejected && (request.Final || evaluation.Reason != "unfinished-round"))
                    throw new RewardDataException(evaluation.Reason);
                // Acknowledging a final result promises it survives lost RAM.
                // This provisional record contains no restoration assertion.
                if (request.Final) RewardRestorationCheckpoint.CaptureFinal(_root, id, run.Snapshot.Generation, candidate);
                run.Last = JsonSerializer.Deserialize<RewardCaptureRequest>(json, RewardSettlementCore.JournalJson)!;
                if (request.Final) { run.Final = run.Last; run.Cola = cola; }
                return new() { Accepted = true, Revision = request.Revision };
            }
            catch (RewardDataException e) { return new() { Reason = e.Message }; }
            catch (OverflowException) { return new() { Reason = "reward-count-overflow" }; }
        }
    }

    internal void ObserveRestored(string id, string json)
        => ObserveRestored(id, () => json);

    internal void ObserveRestored(string id, Func<string> serialize)
    {
        lock (_sync)
        {
            if (!_snapshots.TryGetValue(id, out var snapshot)) return;
            var restored = serialize();
            RewardRestorationCheckpoint.ObserveRestored(_root, id, snapshot.Generation, restored);
            if (!RewardRuntimeFiles.SameProfile(snapshot.Json, restored)) throw new RewardDataException("reward-restored-snapshot-mismatch");
            snapshot.Restored = true;
        }
    }

    // Recovery cannot invent a completed match. A failed start with no final
    // report closes without reward, after positive restore/durable readback.
    // A stale snapshot can still own an already accepted final report: preserve
    // that exact result and its normal startup-only payment path.
    internal void RecoverPersisted(string id, string persistedJson)
    {
        lock (_sync)
        {
            if (!_snapshots.TryGetValue(id, out var snapshot)) return;
            if (!snapshot.Restored)
                throw new RewardDataException("reward-recovery-restoration-unproven");
            RewardRestorationCheckpoint.Persisted(_root, id, snapshot.Generation, persistedJson);
            if (!RewardRuntimeFiles.SameProfile(snapshot.Json, persistedJson))
                throw new RewardDataException("reward-recovery-restoration-unproven");
            RewardRestorationCheckpoint.Complete(_root, id, snapshot.Generation, snapshot.Authority);
            _runs.Remove(id);
            _snapshots.Remove(id);
        }
    }

    internal void FinalizePersisted(string id, string nativeEndIdentity, string persistedJson)
    {
        lock (_sync)
        {
            if (!_runs.TryGetValue(id, out var run)) { RecoverPersisted(id, persistedJson); return; }
            try
            {
                if (run.NativeEnd != nativeEndIdentity || !run.Snapshot.Restored)
                    throw new RewardDataException("reward-durable-restoration-unproven");
                RewardRestorationCheckpoint.Persisted(_root, id, run.Snapshot.Generation, persistedJson);
                if (!RewardRuntimeFiles.SameProfile(run.Snapshot.Json, persistedJson))
                    throw new RewardDataException("reward-durable-restoration-unproven");
                var evaluation = RewardRestorationCheckpoint.Complete(_root, id, run.Snapshot.Generation, run.Authority);
                if (run.Final is null) throw new RewardDataException("reward-final-report-missing");
                var privateSession = !RewardStartupFiles.MarkerAbsent(_root);
                if (evaluation!.Status == RewardEvaluationStatus.Eligible)
                    Compat.ArenaTarkovaColaState.Published(id, run.ArmToken, run.Cola);
                _failures.Remove(id);
                if (privateSession)
                {
                    var previous = _privatePending.GetValueOrDefault(id);
                    _privatePending[id] = (checked(previous.Roubles + evaluation.Roubles),
                        checked(previous.Experience + evaluation.Experience), previous.Cola
                        || evaluation.Status == RewardEvaluationStatus.Eligible && run.Cola is not null);
                }
            }
            catch (Exception e)
            {
                _failures[id] = e is RewardDataException ? e.Message : "reward-storage-unavailable";
                // The durable provisional result/proof survives this failure.
                // Startup retries publication; it never converts storage failure
                // into an automatic waiver of an already accepted final result.
                throw;
            }
            finally
            {
                _runs.Remove(id);
                _snapshots.Remove(id);
            }
        }
    }

    private RewardHandoff? Authority(string id, bool required)
    {
        RewardHandoff? authority = null;
        if (RewardStartupFiles.MarkerAbsent(_root))
        {
            if (RewardEnrollment.WantsRewards(_root, id))
            {
                var path = Path.Combine(RewardRuntimeFiles.DirectoryFor(_root, id), "handoff.json");
                if (RewardRuntimeFiles.Exists(_root, path))
                {
                    authority = RewardRuntimeFiles.ReadHandoff(_root, path);
                    var enrollment = RewardEnrollment.Read(_root, id);
                    if (authority.Binding != enrollment.Binding || !RewardRuntimeFiles.SameRoot(authority.RuntimeRoot, _root))
                        throw new RewardDataException("reward-authority-origin-mismatch");
                }
            }
        }
        else if (_privateConsent.HasControl)
        {
            authority = _privateConsent.Authority(id);
        }
        else
        {
            var path = Path.Combine(_root, "arena-rewards-origin.json");
            if (RewardRuntimeFiles.Exists(_root, path))
            {
                authority = RewardRuntimeFiles.ReadHandoff(_root, path);
                var markerPath = Path.Combine(_root, "arena-session.json");
                RewardStartupFiles.RequireLocalPath(_root, markerPath);
                using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
                RewardStartupFiles.RejectDuplicateProperties(marker.RootElement);
                if (marker.RootElement.GetProperty("schema").GetInt32() != 1
                    || marker.RootElement.GetProperty("profileId").GetString() != id
                    || !string.Equals(RewardStartupFiles.Canonical(marker.RootElement.GetProperty("sourceRuntime").GetString()!),
                        RewardStartupFiles.Canonical(authority.RuntimeRoot), RewardStartupFiles.PathComparison))
                    throw new RewardDataException("reward-private-marker-mismatch");
            }
        }
        if (authority is not null && (authority.Binding.ProfileId != id || !authority.Enabled)) authority = null;
        if (required && authority is null) throw new RewardDataException("rewards-not-enabled-or-enrolled");
        return authority;
    }

    private string PendingPath(string id, string runId) => Path.Combine(_root, "user", "arena-reward-runs", id, runId + ".json");
    private string SnapshotPath(string id, string generation) => Path.Combine(_root, "user", "arena-reward-snapshots", id, generation + ".json");
    private void CloseSnapshot(string id, Snapshot snapshot, string state) => RewardRuntimeFiles.Write(_root,
        SnapshotPath(id, snapshot.Generation), new RewardSnapshotGuard(1, snapshot.Generation, state));
    private static RewardEvaluation Evaluate(Run run, RewardCaptureRequest report)
    {
        var evidence = BuildEvidence(run, report, RewardIdentity.Hash(run.Snapshot.Json));
        return RewardResultValidator.Evaluate(evidence.Authorization, evidence.Report);
    }

    private static RewardApprovalCandidate BuildEvidence(Run run, RewardCaptureRequest capture, string revision, string? cola = null)
    {
        var rounds = new List<RewardRoundAuthorization>();
        var reports = new List<RewardRoundReport>();
        for (var i = 0; i < capture.Rounds.Count; i++)
        {
            var r = capture.Rounds[i];
            var lives = new HashSet<long>();
            if (r is null || r.Number != i + 1 || r.Actors is null || r.Deaths is null || r.Actors.Count > 20_000
                || r.Deaths.Count > 20_000 || r.Actors.Any(a => a is null || a.Body <= 0 || !lives.Add(a.Body)))
                throw new RewardDataException("reward-capture-round-or-body");
            var roundId = RewardIdentity.Hash(run.Id + ":round:" + r.Number)[..32];
            var bodyIds = r.Actors.ToDictionary(a => a.Body, a => RewardIdentity.Hash(run.Id + ":round:" + r.Number + ":life:" + a.Body)[..32]);
            var actors = r.Actors.Select(a => new RewardActor(a.ParticipantId, bodyIds[a.Body], Parse<RewardSide>(a.Side), a.SpawnedAtMilliseconds)).ToArray();
            var deaths = r.Deaths.Select(d => new RewardDeathReport(d.Sequence, d.AtMilliseconds,
                bodyIds.TryGetValue(d.VictimBody, out var victim) ? victim : "",
                d.KillerBody.HasValue ? bodyIds.TryGetValue(d.KillerBody.Value, out var killer) ? killer : "" : null,
                Parse<RewardDeathKind>(d.Kind))).ToArray();
            if (r.ObservedDurationMilliseconds < 0) throw new RewardDataException("reward-negative-duration");
            rounds.Add(new(roundId, run.Rule, Math.Max(1, r.ObservedDurationMilliseconds), run.TimeLimit, run.KillTarget, actors,
                AllowIncomplete: capture.Final && capture.State == "Completed" && !r.Closed && i == capture.Rounds.Count - 1));
            reports.Add(new(roundId, deaths));
        }
        var authorization = new RewardRunAuthorization(1, run.Authority.Binding, run.Id, 1, run.ArmToken, run.NativeEnd,
            run.Snapshot.Generation, run.LocalId, run.Mode, revision, Policy, rounds, cola ?? run.Cola);
        var report = new RewardMatchReport(1, run.Id, run.ArmToken, run.NativeEnd, Parse<RewardReportState>(capture.State), reports);
        return new(1, authorization, report, new(1, run.Authority.Binding, run.Id, run.ArmToken, run.NativeEnd,
            run.Snapshot.Generation, RewardRestorationState.Persisted, revision, revision));
    }

    private static T Parse<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, false, out var parsed) && Enum.IsDefined(parsed)
        ? parsed : throw new RewardDataException("reward-unknown-enum");

    private static void RequireAppendOnly(RewardCaptureRequest previous, RewardCaptureRequest current)
    {
        if (current.Rounds.Count < previous.Rounds.Count) throw new RewardDataException("reward-rounds-removed");
        for (var i = 0; i < previous.Rounds.Count; i++)
        {
            var a = previous.Rounds[i];
            var b = current.Rounds[i];
            if (a.Closed && RewardRuntimeFiles.Json(a) != RewardRuntimeFiles.Json(b)
                || b.Number != a.Number || b.ObservedDurationMilliseconds < a.ObservedDurationMilliseconds
                || b.Actors is null || b.Deaths is null || b.Actors.Count < a.Actors.Count || b.Deaths.Count < a.Deaths.Count
                || !a.Actors.Select(RewardRuntimeFiles.Json).SequenceEqual(b.Actors.Take(a.Actors.Count).Select(RewardRuntimeFiles.Json))
                || !a.Deaths.Select(RewardRuntimeFiles.Json).SequenceEqual(b.Deaths.Take(a.Deaths.Count).Select(RewardRuntimeFiles.Json)))
                throw new RewardDataException("reward-capture-history-changed");
        }
    }

    private static bool RuleAllowed(string mode, bool team, bool ffa, RewardRoundRule rule) => mode switch
    {
        "deathmatch" => rule == RewardRoundRule.KillsOnly || rule == (ffa ? RewardRoundRule.FreeForAllScore : RewardRoundRule.LocalTarget),
        "gungame" or "zombies" => rule == RewardRoundRule.KillsOnly,
        "teamdeathmatch" => rule is RewardRoundRule.TeamScore or RewardRoundRule.KillsOnly,
        "tactical" => rule == (team ? RewardRoundRule.TeamElimination : RewardRoundRule.SoloElimination),
        _ => false
    };

    private sealed class Snapshot(string json, string generation, RewardHandoff authority)
    {
        internal string Json { get; } = json;
        internal string Generation { get; } = generation;
        internal RewardHandoff Authority { get; } = authority;
        internal RewardBinding Binding => Authority.Binding;
        internal bool Restored { get; set; }
    }
    private sealed class Run(string id, string armToken, string nativeEnd, string localId, string mode, RewardRoundRule rule,
        long timeLimit, int killTarget, RewardHandoff authority, Snapshot snapshot, DateTimeOffset began, string beginJson)
    {
        internal string Id { get; } = id;
        internal string ArmToken { get; } = armToken;
        internal string NativeEnd { get; } = nativeEnd;
        internal string LocalId { get; } = localId;
        internal string Mode { get; } = mode;
        internal RewardRoundRule Rule { get; } = rule;
        internal long TimeLimit { get; } = timeLimit;
        internal int KillTarget { get; } = killTarget;
        internal RewardHandoff Authority { get; } = authority;
        internal Snapshot Snapshot { get; } = snapshot;
        internal DateTimeOffset Began { get; } = began;
        internal string BeginJson { get; } = beginJson;
        internal RewardCaptureRequest? Last { get; set; }
        internal RewardCaptureRequest? Final { get; set; }
        internal string? Cola { get; set; }
    }
}
