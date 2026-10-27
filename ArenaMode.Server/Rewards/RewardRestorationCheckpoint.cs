using System.Text;
using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardCheckpoint(int Schema, RewardBinding Binding, string Generation, string DelegationId, string CaptureRoot,
    string ProfileFingerprint, IReadOnlyDictionary<string, string> Sections);
internal sealed record RewardCheckpointRun(int Schema, string RunId, string ArmToken, string NativeEndIdentity);
internal sealed record RewardCheckpointObservation(int Schema, string CheckpointHash, string ProfileFingerprint);
internal sealed record RewardCheckpointProof(int Schema, string CheckpointHash, string ObservationHash,
    string ProfileFingerprint, string PersistedRevision);
internal sealed record RewardCheckpointFinal(int Schema, RewardRunAuthorization Authorization, RewardMatchReport Report);
internal sealed record RewardCheckpointCompletion(int Schema, string ProofHash, string? FinalHash, string State);

/// <summary>
/// Durable witnesses, never a profile backup or a profile writer. A final report
/// is provisional until an actual clone restore and complete disk readback agree.
/// Startup may finish metadata/publication, but cannot repair the player profile.
/// </summary>
internal static class RewardRestorationCheckpoint
{
    private const int MaxProfileBytes = 64 * 1024 * 1024;

    internal static string DirectoryFor(string root, string id, string generation)
    {
        if (!RewardIdentity.IsHex(id, 24) || !RewardIdentity.IsGuid(generation))
            throw new RewardDataException("reward-checkpoint-identity");
        return Path.Combine(root, "user", "arena-reward-checkpoints", id, generation);
    }

    internal static void Create(string root, string id, string generation, RewardHandoff authority, string profile)
    {
        var directory = DirectoryFor(root, id, generation);
        RewardRuntimeFiles.ValidateHandoff(authority);
        if (authority.Binding.ProfileId != id) throw new RewardDataException("reward-checkpoint-binding");
        var checkpoint = new RewardCheckpoint(1, authority.Binding, generation, authority.DelegationId, RewardStartupFiles.Canonical(root),
            Fingerprint(profile), Sections(profile));
        RewardRuntimeFiles.EnsureDirectory(root, directory);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "checkpoint.json"), checkpoint);
    }

    internal static void BindRun(string root, string id, string generation, string runId, string arm, string nativeEnd)
    {
        var directory = DirectoryFor(root, id, generation);
        ReadCheckpoint(root, directory, id, generation);
        var run = new RewardCheckpointRun(1, runId, arm, nativeEnd);
        ValidateRun(run);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "run.json"), run);
    }

    internal static void CaptureFinal(string root, string id, string generation, RewardApprovalCandidate candidate)
    {
        var directory = DirectoryFor(root, id, generation);
        var checkpoint = ReadCheckpoint(root, directory, id, generation);
        var run = Read<RewardCheckpointRun>(root, directory, "run.json");
        ValidateRun(run);
        RequireCandidate(checkpoint, run, candidate.Authorization, candidate.Report);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "final.json"),
            new RewardCheckpointFinal(1, candidate.Authorization, candidate.Report));
    }

    internal static void ObserveRestored(string root, string id, string generation, string profile)
    {
        var directory = DirectoryFor(root, id, generation);
        var checkpoint = ReadCheckpoint(root, directory, id, generation);
        RequireProfile(root, directory, checkpoint, profile, "restore-observation");
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "observed.json"),
            new RewardCheckpointObservation(1, Hash(checkpoint), checkpoint.ProfileFingerprint));
    }

    internal static void Persisted(string root, string id, string generation, string profile)
    {
        var directory = DirectoryFor(root, id, generation);
        var checkpoint = ReadCheckpoint(root, directory, id, generation);
        var observed = ReadObservation(root, directory, checkpoint);
        RequireProfile(root, directory, checkpoint, profile, "durable-readback");
        RewardStartupFiles.RequireCleanProfile(profile, checkpoint.Binding);
        if (Exists(root, directory, "persisted.json"))
        {
            // Preserve the first proven serialization on an exact semantic retry.
            ReadProof(root, directory, checkpoint, observed);
            return;
        }
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "persisted.json"),
            new RewardCheckpointProof(1, Hash(checkpoint), Hash(observed), checkpoint.ProfileFingerprint, RewardIdentity.Hash(profile)));
    }

    // Returns null when there was no final report. Such a clean no-pay end is
    // still terminal; a valid final report is never discarded as a recovery shortcut.
    internal static RewardEvaluation? Complete(string root, string id, string generation, RewardHandoff authority)
    {
        var directory = DirectoryFor(root, id, generation);
        var checkpoint = ReadCheckpoint(root, directory, id, generation);
        RequireAuthority(checkpoint, authority);
        var observed = ReadObservation(root, directory, checkpoint);
        var proof = ReadProof(root, directory, checkpoint, observed);
        var run = Exists(root, directory, "run.json") ? Read<RewardCheckpointRun>(root, directory, "run.json") : null;
        if (run is not null) ValidateRun(run);
        var final = Exists(root, directory, "final.json") ? Read<RewardCheckpointFinal>(root, directory, "final.json") : null;
        RewardEvaluation? evaluation = null;
        RewardSignedReceipt? signed = null;
        if (final is not null)
        {
            if (final.Schema != 1 || run is null) throw new RewardDataException("reward-checkpoint-final-invalid");
            RequireCandidate(checkpoint, run, final.Authorization, final.Report);
            var authorization = final.Authorization with { CleanProfileRevision = proof.PersistedRevision };
            var candidate = new RewardApprovalCandidate(1, authorization, final.Report,
                new(1, checkpoint.Binding, run.RunId, run.ArmToken, run.NativeEndIdentity, generation,
                    RewardRestorationState.Persisted, proof.PersistedRevision, proof.PersistedRevision));
            RewardRuntimeFiles.RequireProof(candidate);
            evaluation = RewardResultValidator.Evaluate(authorization, final.Report);
            if (evaluation.Status == RewardEvaluationStatus.Rejected) throw new RewardDataException(evaluation.Reason);
            signed = RewardRuntimeFiles.Sign(authority, new(1, authority.DelegationId, candidate));
        }
        var completion = new RewardCheckpointCompletion(1, Hash(proof), final is null ? null : Hash(final),
            final is null ? "unrewarded-restored" : "restored");
        if (Exists(root, directory, "complete.json"))
        {
            if (Read<RewardCheckpointCompletion>(root, directory, "complete.json") != completion)
                throw new RewardDataException("reward-checkpoint-completion-conflict");
        }
        var guardPath = Path.Combine(root, "user", "arena-reward-snapshots", id, generation + ".json");
        var guard = ReadGuard(root, guardPath, generation);
        if (guard.State is not ("pending" or "restored")) throw new RewardDataException("reward-checkpoint-guard-conflict");
        var pendingPath = run is null ? null : Path.Combine(root, "user", "arena-reward-runs", id, run.RunId + ".json");
        if (run is not null) RequirePending(root, pendingPath!, run);
        if (signed is not null)
        {
            var normal = RewardStartupFiles.MarkerAbsent(root);
            if (normal)
                RewardOfflineSession.RequireClosedReceipt(root, RewardRuntimeFiles.DirectoryFor(root, id), authority, signed, run!.RunId);
            var inbox = normal
                ? Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "inbox")
                : Path.Combine(root, "user", "arena-rewards-outbox");
            RewardRuntimeFiles.EnsureDirectory(root, inbox);
            RewardRuntimeFiles.WriteNew(root, Path.Combine(inbox, run!.RunId + ".json"), signed);
        }
        // Proof and, when present, receipt precede terminal guards. Every crash
        // between these writes can be replayed using the same immutable inputs.
        RewardRuntimeFiles.Write(root, guardPath, new RewardSnapshotGuard(1, generation, "restored"));
        if (run is not null)
        {
            RewardRuntimeFiles.Write(root, pendingPath!, new { schema = 1, armToken = run.ArmToken, runId = run.RunId, state = completion.State });
        }
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "complete.json"), completion);
        return evaluation;
    }

    internal static void RecoverAtStartup(RewardStartupWindow window, string id)
    {
        window.RequireOpen();
        var directories = Directories(window.Root, id);
        if (directories.Length == 0) return;
        var profile = ReadProfile(window.Root, id);
        var closures = RewardRecovery.ReadCheckpointClosures(window.Root, id, profile);
        foreach (var directory in directories)
        {
            window.RequireOpen();
            var generation = Path.GetFileName(directory);
            if (closures.ClosesSnapshot(generation)) continue;
            var guardPath = Path.Combine(window.Root, "user", "arena-reward-snapshots", id, generation + ".json");
            if (!RewardRuntimeFiles.Exists(window.Root, guardPath)) continue;
            if (ReadGuard(window.Root, guardPath, generation).State == "cancelled-before-start") continue;
            var run = Exists(window.Root, directory, "run.json") ? Read<RewardCheckpointRun>(window.Root, directory, "run.json") : null;
            if (run is not null && closures.ClosesRun(run.RunId, run.ArmToken)) continue;
            var checkpoint = ReadCheckpoint(window.Root, directory, id, generation);
            var handoff = Path.Combine(RewardRuntimeFiles.DirectoryFor(window.Root, id), "delegations", checkpoint.DelegationId + ".json");
            // Enrollment retains old delegations. Falling back to the current
            // handoff is allowed only when its exact delegation still matches.
            if (!RewardRuntimeFiles.Exists(window.Root, handoff))
                handoff = Path.Combine(RewardRuntimeFiles.DirectoryFor(window.Root, id), "handoff.json");
            Recover(window.Root, id, directory, RewardRuntimeFiles.ReadHandoff(window.Root, handoff), profile);
        }
    }

    // Only the offline owner may call this after proving process exit and the
    // exact lease/marker, before it freezes Published() into closed.json.
    internal static void RecoverPrivateStopped(string privateRoot, string id, RewardHandoff authority)
    {
        RewardRuntimeFiles.ValidateHandoff(authority);
        if (RewardStartupFiles.MarkerAbsent(privateRoot) || authority.Binding.ProfileId != id)
            throw new RewardDataException("reward-checkpoint-private-boundary");
        var directories = Directories(privateRoot, id);
        if (directories.Length == 0) return;
        string? profile = null;
        try { profile = ReadProfile(privateRoot, id); }
        catch (Exception e) when (e is RewardDataException or IOException or UnauthorizedAccessException) { }
        foreach (var directory in directories)
        {
            try { Recover(privateRoot, id, directory, authority, profile); }
            catch (Exception e) when (e is RewardDataException or JsonException or FormatException
                or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
            {
                if (HasPublishableProof(privateRoot, id, directory, authority)) throw;
                // Reward-only evidence failures cannot block actual offline
                // profile ownership recovery. ArchivePrivateEvidence is a
                // mandatory next step: it either preserves all bounded source
                // bytes/guards or refuses retirement of the private tree.
            }
        }
    }

    // Read-only review hint, never a closure or payment authority. A fully
    // proven restoration (including no-pay completion) should be retried
    // instead of waived.
    internal static bool HasDurableProof(string root, string id, string generation)
        => HasDurableProof(root, id, generation, out _);

    internal static bool HasDurableProof(string root, string id, string generation, out RewardCheckpointRun? run)
    {
        run = null;
        var directory = DirectoryFor(root, id, generation);
        if (!Exists(root, directory, "persisted.json")) return false;
        try
        {
            var checkpoint = ReadCheckpoint(root, directory, id, generation);
            var rewards = RewardRuntimeFiles.DirectoryFor(root, id);
            var authorityPath = Path.Combine(rewards, "delegations", checkpoint.DelegationId + ".json");
            if (!RewardRuntimeFiles.Exists(root, authorityPath)) authorityPath = Path.Combine(rewards, "handoff.json");
            var authority = RewardRuntimeFiles.ReadHandoff(root, authorityPath);
            if (!authority.Enabled || !RewardRuntimeFiles.SameRoot(authority.RuntimeRoot, root)
                || RewardEnrollment.Read(root, id).Binding != authority.Binding) return false;
            RequireAuthority(checkpoint, authority);
            var candidateRun = Exists(root, directory, "run.json") ? Read<RewardCheckpointRun>(root, directory, "run.json") : null;
            var guard = ReadGuard(root, Path.Combine(root, "user", "arena-reward-snapshots", id, generation + ".json"), generation);
            if (guard.State is not ("pending" or "restored")) return false;
            if (candidateRun is not null)
            {
                ValidateRun(candidateRun);
                RequirePending(root, Path.Combine(root, "user", "arena-reward-runs", id, candidateRun.RunId + ".json"), candidateRun);
            }
            var observation = ReadObservation(root, directory, checkpoint);
            var proof = ReadProof(root, directory, checkpoint, observation);
            var final = Exists(root, directory, "final.json") ? Read<RewardCheckpointFinal>(root, directory, "final.json") : null;
            if (final is not null)
            {
                if (final.Schema != 1 || candidateRun is null) return false;
                RequireCandidate(checkpoint, candidateRun, final.Authorization, final.Report);
                var authorization = final.Authorization with { CleanProfileRevision = proof.PersistedRevision };
                if (RewardResultValidator.Evaluate(authorization, final.Report).Status == RewardEvaluationStatus.Rejected) return false;
                var candidate = new RewardApprovalCandidate(1, authorization, final.Report,
                    new(1, checkpoint.Binding, candidateRun.RunId, candidateRun.ArmToken, candidateRun.NativeEndIdentity, generation,
                        RewardRestorationState.Persisted, proof.PersistedRevision, proof.PersistedRevision));
                var signed = RewardRuntimeFiles.Sign(authority, new(1, authority.DelegationId, candidate));
                RewardOfflineSession.RequireClosedReceipt(root, rewards, authority, signed, candidateRun.RunId);
            }
            var completion = new RewardCheckpointCompletion(1, Hash(proof), final is null ? null : Hash(final),
                final is null ? "unrewarded-restored" : "restored");
            if (Exists(root, directory, "complete.json")
                && Read<RewardCheckpointCompletion>(root, directory, "complete.json") != completion) return false;
            run = candidateRun;
            return true;
        }
        catch (Exception e) when (e is RewardDataException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or FileNotFoundException or DirectoryNotFoundException)
        { return false; }
    }

    private static bool HasPublishableProof(string root, string id, string directory, RewardHandoff authority)
    {
        try
        {
            if (!Exists(root, directory, "persisted.json") || !Exists(root, directory, "final.json")) return false;
            var checkpoint = ReadCheckpoint(root, directory, id, Path.GetFileName(directory));
            RequireAuthority(checkpoint, authority);
            var observation = ReadObservation(root, directory, checkpoint);
            ReadProof(root, directory, checkpoint, observation);
            var run = Read<RewardCheckpointRun>(root, directory, "run.json");
            ValidateRun(run);
            var final = Read<RewardCheckpointFinal>(root, directory, "final.json");
            if (final.Schema != 1) return false;
            RequireCandidate(checkpoint, run, final.Authorization, final.Report);
            return RewardResultValidator.Evaluate(final.Authorization, final.Report).Status != RewardEvaluationStatus.Rejected;
        }
        catch (Exception e) when (e is RewardDataException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException) { return false; }
        // Unreadable evidence cannot authorize freezing an incomplete receipt
        // list either. Let I/O errors retain the stopped session for a retry.
    }

    internal static void ArchivePrivateEvidence(string privateRoot, string normalRoot, string id)
    {
        _ = RewardRuntimeFiles.DirectoryFor(normalRoot, id);
        if (RewardRuntimeFiles.SameRoot(privateRoot, normalRoot) || RewardStartupFiles.MarkerAbsent(privateRoot)
            || !RewardStartupFiles.MarkerAbsent(normalRoot))
            throw new RewardDataException("reward-checkpoint-archive-boundary");
        foreach (var directory in Directories(privateRoot, id))
        {
            var generation = Path.GetFileName(directory);
            var target = DirectoryFor(normalRoot, id, generation);
            var files = Directory.EnumerateFiles(directory).Take(17).ToArray();
            if (files.Length > 16 || Directory.EnumerateDirectories(directory).Any())
                throw new RewardDataException("reward-checkpoint-archive-capacity");
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                if (name is not ("checkpoint.json" or "run.json" or "final.json" or "observed.json"
                    or "persisted.json" or "complete.json" or "mismatch.json")
                    && !System.Text.RegularExpressions.Regex.IsMatch(name, "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z"))
                    throw new RewardDataException("reward-checkpoint-archive-file");
                CopyEvidence(privateRoot, normalRoot, path, Path.Combine(target, name));
            }
            var guard = Path.Combine(privateRoot, "user", "arena-reward-snapshots", id, generation + ".json");
            if (RewardRuntimeFiles.Exists(privateRoot, guard))
                CopyEvidence(privateRoot, normalRoot, guard, Path.Combine(normalRoot, "user", "arena-reward-snapshots", id, generation + ".json"));
        }
        // A malformed run.json cannot be used to select a run guard. Preserve
        // the bounded profile-specific guard inventory, including legacy cases,
        // instead of guessing an ID or silently losing its original evidence.
        foreach (var kind in new[] { "arena-reward-snapshots", "arena-reward-runs" })
        {
            var directory = Path.Combine(privateRoot, "user", kind, id);
            if (!RewardRuntimeFiles.Exists(privateRoot, directory)) continue;
            var files = Directory.EnumerateFiles(directory, "*.json").Take(4097).ToArray();
            if (files.Length > 4096) throw new RewardDataException("reward-checkpoint-archive-capacity");
            foreach (var path in files)
            {
                if (!RewardIdentity.IsGuid(Path.GetFileNameWithoutExtension(path)))
                    throw new RewardDataException("reward-checkpoint-archive-name");
                CopyEvidence(privateRoot, normalRoot, path, Path.Combine(normalRoot, "user", kind, id, Path.GetFileName(path)));
            }
        }
    }

    private static void CopyEvidence(string sourceRoot, string targetRoot, string source, string target)
    {
        RewardStartupFiles.RequireLocalPath(sourceRoot, source);
        byte[] bytes;
        using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > RewardRuntimeFiles.MaxReceiptBytes) throw new RewardDataException("reward-state-too-large");
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }
        RewardRuntimeFiles.EnsureDirectory(targetRoot, Path.GetDirectoryName(target)!);
        if (RewardRuntimeFiles.Exists(targetRoot, target))
        {
            using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != bytes.Length) throw new RewardDataException("reward-archive-conflict");
            var existing = new byte[bytes.Length];
            stream.ReadExactly(existing);
            if (!existing.AsSpan().SequenceEqual(bytes)) throw new RewardDataException("reward-archive-conflict");
            return;
        }
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".arena-reward-" + Guid.NewGuid().ToString("N") + ".tmp");
        RewardStartupFiles.RequireLocalPath(targetRoot, temporary, allowMissingLeaf: true);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            RewardStartupFiles.RequireLocalPath(targetRoot, target, allowMissingLeaf: true);
            File.Move(temporary, target, overwrite: false);
            RewardStartupFiles.RequireLocalPath(targetRoot, target);
            if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Reward evidence archive readback mismatch.");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                RewardStartupFiles.RequireLocalPath(targetRoot, temporary);
                File.Delete(temporary);
            }
        }
    }

    private static void Recover(string root, string id, string directory, RewardHandoff authority, string? profile)
    {
        var generation = Path.GetFileName(directory);
        var guardPath = Path.Combine(root, "user", "arena-reward-snapshots", id, generation + ".json");
        // A crash before publishing the initial guard never authorized a run.
        if (!RewardRuntimeFiles.Exists(root, guardPath)) return;
        if (ReadGuard(root, guardPath, generation).State == "cancelled-before-start") return;
        var checkpoint = ReadCheckpoint(root, directory, id, generation);
        RequireAuthority(checkpoint, authority);
        if (!Exists(root, directory, "observed.json")) return;
        if (!Exists(root, directory, "persisted.json"))
        {
            // Archived private evidence cannot use an ordinary profile as a
            // substitute for its private native-save readback.
            if (profile is null || !RewardRuntimeFiles.SameRoot(root, checkpoint.CaptureRoot)) return;
            Persisted(root, id, generation, profile);
        }
        Complete(root, id, generation, authority);
    }

    private static string[] Directories(string root, string id)
    {
        _ = RewardRuntimeFiles.DirectoryFor(root, id);
        var parent = Path.Combine(root, "user", "arena-reward-checkpoints", id);
        if (!RewardRuntimeFiles.Exists(root, parent)) return [];
        var directories = Directory.EnumerateDirectories(parent).Take(4097).ToArray();
        if (directories.Length > 4096) throw new RewardDataException("reward-checkpoint-capacity");
        foreach (var directory in directories)
        {
            if (!RewardIdentity.IsGuid(Path.GetFileName(directory))) throw new RewardDataException("reward-checkpoint-name");
            RewardStartupFiles.RequireLocalPath(root, directory);
        }
        return directories;
    }

    private static RewardCheckpoint ReadCheckpoint(string root, string directory, string id, string generation)
    {
        var checkpoint = Read<RewardCheckpoint>(root, directory, "checkpoint.json");
        if (checkpoint.Schema != 1 || checkpoint.Binding is null || checkpoint.Binding.ProfileId != id
            || checkpoint.Generation != generation || !RewardIdentity.IsGuid(generation)
            || checkpoint.CaptureRoot is null || !Path.IsPathFullyQualified(checkpoint.CaptureRoot)
            || !RewardIdentity.IsGuid(checkpoint.DelegationId) || !RewardIdentity.IsHex(checkpoint.ProfileFingerprint, 64)
            || checkpoint.Sections is null || checkpoint.Sections.Count > 128
            || checkpoint.Sections.Any(s => !RewardIdentity.IsHex(s.Key, 64) || !RewardIdentity.IsHex(s.Value, 64)))
            throw new RewardDataException("reward-checkpoint-invalid");
        checkpoint.Binding.Validate();
        return checkpoint;
    }

    private static RewardCheckpointObservation ReadObservation(string root, string directory, RewardCheckpoint checkpoint)
    {
        var observed = Read<RewardCheckpointObservation>(root, directory, "observed.json");
        if (observed.Schema != 1 || observed.CheckpointHash != Hash(checkpoint) || observed.ProfileFingerprint != checkpoint.ProfileFingerprint)
            throw new RewardDataException("reward-checkpoint-observation-conflict");
        return observed;
    }

    private static RewardCheckpointProof ReadProof(string root, string directory, RewardCheckpoint checkpoint, RewardCheckpointObservation observed)
    {
        var proof = Read<RewardCheckpointProof>(root, directory, "persisted.json");
        if (proof.Schema != 1 || proof.CheckpointHash != Hash(checkpoint) || proof.ObservationHash != Hash(observed)
            || proof.ProfileFingerprint != checkpoint.ProfileFingerprint || !RewardIdentity.IsHex(proof.PersistedRevision, 64))
            throw new RewardDataException("reward-checkpoint-proof-conflict");
        return proof;
    }

    private static void RequireAuthority(RewardCheckpoint checkpoint, RewardHandoff authority)
    {
        RewardRuntimeFiles.ValidateHandoff(authority);
        if (authority.Binding != checkpoint.Binding || authority.DelegationId != checkpoint.DelegationId)
            throw new RewardDataException("reward-checkpoint-authority-conflict");
    }

    private static void ValidateRun(RewardCheckpointRun run)
    {
        if (run.Schema != 1 || !RewardIdentity.IsGuid(run.RunId) || !RewardIdentity.IsGuid(run.ArmToken)
            || !RewardResultValidator.ValidNativeIdentity(run.NativeEndIdentity, run.ArmToken))
            throw new RewardDataException("reward-checkpoint-run-invalid");
    }

    private static void RequireCandidate(RewardCheckpoint checkpoint, RewardCheckpointRun run,
        RewardRunAuthorization authorization, RewardMatchReport report)
    {
        if (authorization is null || report is null) throw new RewardDataException("reward-checkpoint-final-invalid");
        RewardResultValidator.ValidateAuthorization(authorization);
        if (authorization.Binding != checkpoint.Binding || authorization.RestorationId != checkpoint.Generation
            || authorization.RunId != run.RunId || authorization.ArmToken != run.ArmToken || authorization.NativeEndIdentity != run.NativeEndIdentity
            || report.RunId != run.RunId || report.ArmToken != run.ArmToken || report.NativeEndIdentity != run.NativeEndIdentity)
            throw new RewardDataException("reward-checkpoint-final-binding");
    }

    private static RewardSnapshotGuard ReadGuard(string root, string path, string generation)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        var guard = RewardStartupFiles.Read<RewardSnapshotGuard>(path, 16384);
        if (guard.Schema != 1 || guard.Generation != generation) throw new RewardDataException("reward-checkpoint-guard-invalid");
        return guard;
    }

    private static void RequirePending(string root, string path, RewardCheckpointRun run)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        var value = document.RootElement;
        if (value.GetProperty("schema").GetInt32() != 1 || value.GetProperty("runId").GetString() != run.RunId
            || value.GetProperty("armToken").GetString() != run.ArmToken
            || value.TryGetProperty("state", out var state) && state.GetString() is not ("restored" or "unrewarded-restored"))
            throw new RewardDataException("reward-checkpoint-pending-conflict");
    }

    private static void RequireProfile(string root, string directory, RewardCheckpoint expected, string actual, string phase)
    {
        var fingerprint = Fingerprint(actual);
        if (fingerprint == expected.ProfileFingerprint) return;
        var sections = Sections(actual);
        var differing = expected.Sections.Keys.Union(sections.Keys, StringComparer.Ordinal)
            .Where(path => expected.Sections.GetValueOrDefault(path) != sections.GetValueOrDefault(path)).Order(StringComparer.Ordinal).Take(16).ToArray();
        RewardRuntimeFiles.Write(root, Path.Combine(directory, "mismatch.json"), new
        { schema = 1, generation = expected.Generation, phase, expected = expected.ProfileFingerprint, actual = fingerprint, differingPathHashes = differing });
        throw new RewardDataException("reward-checkpoint-profile-mismatch");
    }

    // Stable full-document comparison: every property and array position matters.
    // Numeric spelling is deliberately strict; an alternate spelling may defer,
    // but can never erase an inventory/health/progression difference.
    internal static string Fingerprint(string profile)
    {
        if (Encoding.UTF8.GetByteCount(profile) > MaxProfileBytes) throw new RewardDataException("profile-too-large");
        using var document = JsonDocument.Parse(profile);
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        return Fingerprint(document.RootElement);
    }

    private static string Fingerprint(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return RewardIdentity.Hash(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.String) writer.WriteStringValue(value.GetString());
        else value.WriteTo(writer);
    }

    private static IReadOnlyDictionary<string, string> Sections(string profile)
    {
        using var document = JsonDocument.Parse(profile);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new RewardDataException("reward-profile-not-object");
        return document.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Take(128)
            .ToDictionary(p => RewardIdentity.Hash("$." + p.Name), p => Fingerprint(p.Value), StringComparer.Ordinal);
    }

    private static string ReadProfile(string root, string id)
    {
        var path = Path.Combine(root, "user", "profiles", id + ".json");
        RewardStartupFiles.RequireLocalPath(root, path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxProfileBytes) throw new RewardDataException("profile-too-large");
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    private static string Hash<T>(T value) => RewardIdentity.Hash(RewardRuntimeFiles.Json(value));
    private static bool Exists(string root, string directory, string name) => RewardRuntimeFiles.Exists(root, Path.Combine(directory, name));
    private static T Read<T>(string root, string directory, string name)
    {
        var path = Path.Combine(directory, name);
        RewardStartupFiles.RequireLocalPath(root, path);
        return RewardStartupFiles.Read<T>(path);
    }
}
