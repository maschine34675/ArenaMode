using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardOfflineLease(int Schema, string SessionId, string ProfileId, string RuntimeRoot,
    string PrivateRoot, string MarkerCreatedUtc, int Port, long ConsentRevision, bool Enabled,
    RewardHandoff? Authority, string ControlKey, int NormalServerPid, DateTimeOffset NormalServerStartedUtc,
    string[] NormalServerArguments, string EntryOperationId, int EntryHelperPid, DateTimeOffset EntryHelperStartedUtc);
internal sealed record RewardOfflineReference(int Schema, string SessionId, string LeaseHash,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Activated = false);
internal sealed record RewardOfflineActivation(int Schema, string SessionId, string LeaseHash, int PrivatePid, DateTimeOffset PrivateStartedUtc);
internal sealed record RewardOfflineState(int Schema, string SessionId, string LeaseHash, long Revision, bool Enabled);
internal sealed record RewardOfflineSignedState(int Schema, string Payload, string Signature);
internal sealed record RewardOfflineReceipt(string RunId, string Fingerprint);
internal sealed record RewardOfflineClosure(int Schema, string SessionId, string LeaseHash, RewardOfflineState State,
    bool Activated, RewardOfflineReceipt[] Receipts);
internal sealed record RewardOfflineCompletion(int Schema, string SessionId, string ClosureHash);

// Must propagate past the reward-only refusal: an unknown owner may still write.
internal sealed class RewardOfflineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One ordinary-runtime lease. Only metadata crosses this boundary, never a private profile.</summary>
internal static partial class RewardOfflineSession
{
    internal const string LeaseFile = "arena-offline-session.json";
    internal const string StateFile = "arena-offline-state.json";
    private const int MaximumMetadata = 524288;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, RewardOfflinePrivate> Owners = new(StringComparer.OrdinalIgnoreCase);

    internal static string ActivePath(string root) => Path.Combine(root, RewardStartupFiles.RelativeDirectory, "offline-active.json");
    internal static string SessionDirectory(string root, string id)
    {
        if (!RewardIdentity.IsGuid(id)) throw new RewardDataException("offline-session-id-invalid");
        return Path.Combine(root, RewardStartupFiles.RelativeDirectory, "offline-sessions", id);
    }
    internal static string LeasePath(string root, string id) => Path.Combine(SessionDirectory(root, id), "lease.json");
    internal static string Hash(RewardOfflineLease lease) => RewardIdentity.Hash(RewardRuntimeFiles.Json(lease));
    internal static bool HasActive(string root) => RewardRuntimeFiles.Exists(root, ActivePath(root));

    internal static bool Requested(string root)
    {
        if (RewardRuntimeFiles.Exists(root, Path.Combine(root, LeaseFile))) return true;
        if (RewardStartupFiles.MarkerAbsent(root)) return false;
        var marker = RewardStartupFiles.Read<JsonElement>(Path.Combine(root, "arena-session.json"), 16384);
        return marker.TryGetProperty("offlineSessionId", out var id) && id.ValueKind != JsonValueKind.Null;
    }

    // Called only by the authenticated, quiesced entry operation. Immutable retry
    // evidence binds the same helper operation, not merely a reused session ID.
    internal static RewardOfflineLease CreateReady(string root, RewardEntryGrant grant, RewardEntryRequest request)
    {
        // The shutdown sequence's semaphore does not cover publication. Identical
        // HTTP retries must not race two different random delegations into the lease.
        lock (Sync) return CreateReadyCore(root, grant, request);
    }

    private static RewardOfflineLease CreateReadyCore(string root, RewardEntryGrant grant, RewardEntryRequest request)
    {
        root = RewardStartupFiles.Canonical(root);
        if (!RewardStartupFiles.MarkerAbsent(root) || grant.Schema != 1 || grant.Purpose != RewardEntryControl.Purpose
            || request.Schema != 1 || request.SessionId != grant.SessionId || request.Token != grant.Token
            || !RewardRuntimeFiles.SameRoot(root, grant.RuntimeRoot)) throw new RewardDataException("offline-entry-mismatch");
        var path = LeasePath(root, grant.SessionId);
        RewardOfflineLease lease;
        if (RewardRuntimeFiles.Exists(root, path))
        {
            lease = ReadLease(root, path);
            if (lease.ProfileId != grant.ProfileId || lease.EntryOperationId != request.OperationId
                || lease.EntryHelperPid != request.HelperPid || lease.EntryHelperStartedUtc != request.HelperStartedUtc
                || lease.NormalServerPid != grant.ServerPid || lease.NormalServerStartedUtc != grant.ServerStartedUtc
                || !lease.NormalServerArguments.SequenceEqual(grant.ServerArguments)
                || !RewardRuntimeFiles.SameRoot(lease.PrivateRoot, request.PrivateRoot)
                || lease.MarkerCreatedUtc != request.MarkerCreatedUtc || lease.Port != request.Port)
                throw new RewardDataException("offline-entry-replay");
            if (ReadDiscard(root, lease) is not null
                || RewardRuntimeFiles.Exists(root, Path.Combine(SessionDirectory(root, lease.SessionId), "closed.json")))
                throw new RewardDataException("offline-entry-closed");
        }
        else
        {
            if (HasActive(root)) throw new RewardDataException("offline-session-already-active");
            var consent = RewardEnrollment.ReadConsent(root, grant.ProfileId);
            var directory = RewardRuntimeFiles.DirectoryFor(root, grant.ProfileId);
            RewardHandoff? authority = null;
            if (RewardRuntimeFiles.Exists(root, Path.Combine(directory, "enrollment.json")))
            {
                if (!RewardRuntimeFiles.Exists(root, Path.Combine(directory, "consent.json")))
                    throw new RewardDataException("offline-existing-consent-missing");
                var enrollment = RewardEnrollment.Read(root, grant.ProfileId);
                // Read-only validation of the existing ledger; never enroll here.
                string profilePath = Path.Combine(root, "user", "profiles", grant.ProfileId + ".json");
                RewardStartupFiles.RequireLocalPath(root, profilePath);
                _ = RewardProfileDocument.ReadLedger(File.ReadAllText(profilePath), enrollment.Binding, new());
                authority = new(1, true, root, enrollment.Binding, Guid.NewGuid().ToString("N"), Secret(), grant.SessionId);
            }
            lease = new(1, grant.SessionId, grant.ProfileId, root, RewardStartupFiles.Canonical(request.PrivateRoot),
                request.MarkerCreatedUtc, request.Port, consent.Revision, consent.Enabled, authority, Secret(),
                grant.ServerPid, grant.ServerStartedUtc, grant.ServerArguments.ToArray(), request.OperationId,
                request.HelperPid, request.HelperStartedUtc);
            Validate(lease);
            RewardRuntimeFiles.EnsureDirectory(root, SessionDirectory(root, lease.SessionId));
            RewardRuntimeFiles.WriteNew(root, path, lease);
        }
        Validate(lease);
        var reference = new RewardOfflineReference(1, lease.SessionId, Hash(lease));
        if (lease.Authority is { } delegated)
        {
            string directory = RewardRuntimeFiles.DirectoryFor(root, lease.ProfileId);
            string delegates = Path.Combine(directory, "delegations");
            RewardRuntimeFiles.EnsureDirectory(root, delegates);
            RewardRuntimeFiles.WriteNew(root, Path.Combine(delegates, delegated.DelegationId + ".json"), delegated);
            string index = Path.Combine(directory, "offline-delegations");
            RewardRuntimeFiles.EnsureDirectory(root, index);
            RewardRuntimeFiles.WriteNew(root, Path.Combine(index, delegated.DelegationId + ".json"), reference);
        }
        RewardRuntimeFiles.WriteNew(root, ActivePath(root), reference);
        return lease;
    }

    internal static RewardOfflinePrivate? Owner(string root)
    { lock (Sync) return Owners.GetValueOrDefault(RewardStartupFiles.Canonical(root)); }

    // The entry helper owns original .startup.lock while this acquires .server.lock.
    // Do NOT take original .startup.lock here: that would deadlock the handoff.
    internal static RewardOfflinePrivate StartPrivate(string root, RewardReturnProcess? process = null)
    {
        root = RewardStartupFiles.Canonical(root);
        RewardStartupLifetimeLock? lifetime = null;
        try
        {
            var lease = ReadLease(root, Path.Combine(root, LeaseFile));
            if (!RewardRuntimeFiles.SameRoot(root, lease.PrivateRoot)) throw new RewardDataException("offline-private-root-mismatch");
            ValidateMarker(root, lease);
            lifetime = RewardStartupLifetimeLock.Acquire(lease.RuntimeRoot);
            RequireActive(lease);
            process ??= CurrentPrivateProcess(root);
            var activation = new RewardOfflineActivation(1, lease.SessionId, Hash(lease), process.Pid, process.StartedUtc);
            var activationPath = Path.Combine(SessionDirectory(lease.RuntimeRoot, lease.SessionId), "activation.json");
            // A restarted private process cannot inherit an abandoned control scope.
            if (RewardRuntimeFiles.Exists(lease.RuntimeRoot, activationPath))
                throw new RewardDataException("offline-private-already-activated");
            var owner = new RewardOfflinePrivate(lease, lifetime);
            owner.Initialize();
            RewardRuntimeFiles.WriteNew(lease.RuntimeRoot, activationPath, activation);
            RewardRuntimeFiles.Write(lease.RuntimeRoot, ActivePath(lease.RuntimeRoot),
                new RewardOfflineReference(1, lease.SessionId, Hash(lease), Activated: true));
            lock (Sync)
            {
                if (Owners.ContainsKey(root)) throw new RewardDataException("offline-private-owner-exists");
                Owners.Add(root, owner);
            }
            lifetime = null;
            return owner;
        }
        catch (Exception exception) { lifetime?.Dispose(); throw new RewardOfflineException("Offline Arena ownership could not be acquired; startup stopped.", exception); }
    }

    internal static void Release(RewardOfflinePrivate owner)
    { lock (Sync) if (Owners.GetValueOrDefault(owner.Lease.PrivateRoot) == owner) Owners.Remove(owner.Lease.PrivateRoot); }

    internal static void RequireActive(RewardOfflineLease lease)
    {
        var active = ReadReference(lease.RuntimeRoot, ActivePath(lease.RuntimeRoot));
        var original = ReadLease(lease.RuntimeRoot, LeasePath(lease.RuntimeRoot, lease.SessionId));
        if (active.SessionId != lease.SessionId || active.LeaseHash != Hash(lease) || Hash(original) != Hash(lease)
            || ReadDiscard(lease.RuntimeRoot, lease) is not null
            || RewardRuntimeFiles.Exists(lease.RuntimeRoot, Path.Combine(SessionDirectory(lease.RuntimeRoot, lease.SessionId), "closed.json")))
            throw new RewardDataException("offline-authority-changed-or-closed");
    }

    internal static RewardOfflineLease ReadLease(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        var lease = RewardStartupFiles.Read<RewardOfflineLease>(path, 32768);
        Validate(lease);
        return lease;
    }

    internal static void Validate(RewardOfflineLease lease)
    {
        if (lease.Schema != 1 || !RewardIdentity.IsGuid(lease.SessionId) || !RewardIdentity.IsHex(lease.ProfileId, 24)
            || !RewardIdentity.IsGuid(lease.EntryOperationId) || lease.ConsentRevision < 0
            || lease.Port is < 1 or > 65535 || lease.EntryHelperPid <= 0 || lease.NormalServerPid <= 0
            || lease.EntryHelperStartedUtc == default || lease.NormalServerStartedUtc == default
            || !RewardReturnControl.SupportedArguments(lease.NormalServerArguments)
            || !DateTimeOffset.TryParse(lease.MarkerCreatedUtc, out _)
            || !Path.IsPathFullyQualified(lease.RuntimeRoot) || !Path.IsPathFullyQualified(lease.PrivateRoot)
            || RewardRuntimeFiles.SameRoot(lease.RuntimeRoot, lease.PrivateRoot)
            || Convert.FromBase64String(lease.ControlKey).Length != 32)
            throw new RewardDataException("offline-lease-invalid");
        if (lease.Authority is { } authority)
        {
            RewardRuntimeFiles.ValidateHandoff(authority);
            if (!authority.Enabled || authority.Binding.ProfileId != lease.ProfileId
                || authority.OfflineSessionId != lease.SessionId
                || !RewardRuntimeFiles.SameRoot(authority.RuntimeRoot, lease.RuntimeRoot)
                || authority.Key == lease.ControlKey) throw new RewardDataException("offline-delegation-invalid");
        }
    }

    internal static void ValidateMarker(string root, RewardOfflineLease lease)
    {
        var path = Path.Combine(root, "arena-session.json");
        RewardStartupFiles.RequireLocalPath(root, path);
        var marker = RewardStartupFiles.Read<JsonElement>(path, 16384);
        if (marker.GetProperty("schema").GetInt32() != 1 || marker.GetProperty("offlineSessionId").GetString() != lease.SessionId
            || marker.GetProperty("profileId").GetString() != lease.ProfileId
            || marker.GetProperty("createdUtc").GetString() != lease.MarkerCreatedUtc
            || marker.GetProperty("port").GetInt32() != lease.Port
            || !RewardRuntimeFiles.SameRoot(marker.GetProperty("sourceRuntime").GetString()!, lease.RuntimeRoot)
            || !RewardRuntimeFiles.SameRoot(marker.GetProperty("serverExe").GetString()!, Path.Combine(root, "SPT.Server.exe")))
            throw new RewardDataException("offline-marker-mismatch");
    }

    internal static RewardOfflineReference ReadReference(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        var value = RewardStartupFiles.Read<RewardOfflineReference>(path, 4096);
        if (value.Schema != 1 || !RewardIdentity.IsGuid(value.SessionId) || !RewardIdentity.IsHex(value.LeaseHash, 64))
            throw new RewardDataException("offline-reference-invalid");
        return value;
    }

    internal static RewardOfflineSignedState Sign(RewardOfflineLease lease, RewardOfflineState state)
    {
        ValidateState(lease, state);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(RewardRuntimeFiles.Json(state)));
        return new(1, payload, Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(lease.ControlKey), Encoding.UTF8.GetBytes(payload))));
    }

    internal static RewardOfflineState ReadState(RewardOfflineLease lease)
    {
        string path = Path.Combine(lease.PrivateRoot, StateFile);
        RewardStartupFiles.RequireLocalPath(lease.PrivateRoot, path);
        var signed = RewardStartupFiles.Read<RewardOfflineSignedState>(path, 8192);
        if (signed.Schema != 1 || signed.Payload is null || signed.Payload.Length > 4096 || !RewardIdentity.IsHex(signed.Signature, 64))
            throw new RewardDataException("offline-state-invalid");
        byte[] signature = HMACSHA256.HashData(Convert.FromBase64String(lease.ControlKey), Encoding.UTF8.GetBytes(signed.Payload));
        if (!CryptographicOperations.FixedTimeEquals(signature, Convert.FromHexString(signed.Signature)))
            throw new RewardDataException("offline-state-signature-invalid");
        using var document = JsonDocument.Parse(Convert.FromBase64String(signed.Payload));
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        var state = document.Deserialize<RewardOfflineState>(RewardSettlementCore.JournalJson) ?? throw new RewardDataException("offline-state-empty");
        ValidateState(lease, state);
        return state;
    }

    private static void ValidateState(RewardOfflineLease lease, RewardOfflineState state)
    {
        if (state.Schema != 1 || state.SessionId != lease.SessionId || state.LeaseHash != Hash(lease)
            || state.Revision < lease.ConsentRevision || state.Revision - lease.ConsentRevision > 4096
            || state.Revision == lease.ConsentRevision && state.Enabled != lease.Enabled)
            throw new RewardDataException("offline-state-binding-invalid");
    }

    private static RewardReturnProcess CurrentPrivateProcess(string root)
    {
        if (!OperatingSystem.IsWindows() || !ArenaRewardStartup.CurrentImageOwnsRoot(root, Environment.ProcessPath)
            || !RewardRuntimeFiles.SameRoot(root, AppContext.BaseDirectory))
            throw new RewardDataException("offline-private-process-unverified");
        using var current = Process.GetCurrentProcess();
        return new(current.Id, current.StartTime.ToUniversalTime(), Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
