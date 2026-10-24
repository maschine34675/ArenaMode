using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardConsent(int Schema, bool Enabled);
internal sealed record RewardEnrollmentPlan(int Schema, RewardBinding Binding, string BeforeRevision, string AfterRevision);
internal sealed record RewardHandoff(int Schema, bool Enabled, string RuntimeRoot, RewardBinding Binding,
    string DelegationId, string Key);
internal sealed record RewardEarnedReceipt(int Schema, string DelegationId, RewardApprovalCandidate Evidence);
internal sealed record RewardSignedReceipt(int Schema, string DelegationId, string Payload, string Signature);
internal sealed record RewardAllocation(int Schema, string SourceFingerprint, RewardApprovalCandidate Candidate);
internal sealed record RewardImported(int Schema, string SourceFingerprint, string RunId, long Sequence);
internal sealed record RewardRuntimePending(int Schema, string ArmToken, string RunId);
internal sealed record RewardSnapshotGuard(int Schema, string Generation, string State);

// The normal origin owns the verification key. The helper transports opaque
// signed receipts, never profiles, ledgers, client-chosen RUB or paths.
internal static class RewardRuntimeFiles
{
    internal const int MaxReceiptBytes = 4_194_304;
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson);
    internal static bool SameRoot(string a, string b) => string.Equals(RewardStartupFiles.Canonical(a),
        RewardStartupFiles.Canonical(b), RewardStartupFiles.PathComparison);
    internal static string DirectoryFor(string root, string id)
    {
        if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("invalid-reward-profile");
        return Path.Combine(root, RewardStartupFiles.RelativeDirectory, id);
    }

    internal static bool Exists(string root, string path)
    {
        RequireParents(root, path);
        try { RewardStartupFiles.RequireLocalPath(root, path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    // Validate each existing ancestor BEFORE creation; never follow a link and
    // then discover it after writing inside a foreign tree.
    internal static void EnsureDirectory(string root, string path)
    {
        root = RewardStartupFiles.Canonical(root);
        path = RewardStartupFiles.Canonical(path);
        RequireParents(root, path);
        if (Directory.Exists(path)) { RewardStartupFiles.RequireLocalPath(root, path); return; }
        var parent = Path.GetDirectoryName(path)!;
        if (parent != root) EnsureDirectory(root, parent);
        RewardStartupFiles.RequireLocalPath(root, parent);
        Directory.CreateDirectory(path);
        RewardStartupFiles.RequireLocalPath(root, path);
    }

    private static void RequireParents(string root, string path)
    {
        root = RewardStartupFiles.Canonical(root);
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new RewardDataException("reward-path-outside-root");
        RewardStartupFiles.RequireLocalPath(root, root);
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part is "" or ".") continue;
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new RewardDataException("reward-path-is-linked");
            }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
        }
    }

    internal static void Write<T>(string root, string path, T value)
    {
        var json = Json(value);
        RewardStartupFiles.WriteAtomic(root, path, json);
        if (File.ReadAllText(path) != json) throw new IOException("Reward metadata read-back mismatch.");
    }

    internal static void WriteNew<T>(string root, string path, T value)
    {
        var json = Json(value);
        if (Exists(root, path))
        {
            if (File.ReadAllText(path) != json) throw new RewardDataException("reward-immutable-conflict");
            return;
        }
        // The server is the sole publisher for a run. Transport uses its own
        // create-new/compare protocol after the private server has stopped.
        Write(root, path, value);
    }

    internal static bool SameProfile(string expected, string actual)
    {
        using var a = JsonDocument.Parse(expected);
        using var b = JsonDocument.Parse(actual);
        RewardStartupFiles.RejectDuplicateProperties(a.RootElement);
        RewardStartupFiles.RejectDuplicateProperties(b.RootElement);
        return JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual));
    }

    internal static RewardHandoff ReadHandoff(string root, string path)
    {
        RewardStartupFiles.RequireLocalPath(root, path);
        var value = RewardStartupFiles.Read<RewardHandoff>(path, 16384);
        if (value.Schema != 1 || value.Binding is null || !RewardIdentity.IsGuid(value.DelegationId)
            || !Path.IsPathFullyQualified(value.RuntimeRoot) || Convert.FromBase64String(value.Key).Length != 32)
            throw new RewardDataException("invalid-reward-delegation");
        value.Binding.Validate();
        return value;
    }

    internal static RewardSignedReceipt Sign(RewardHandoff authority, RewardEarnedReceipt receipt)
    {
        if (!authority.Enabled || receipt.Schema != 1 || receipt.DelegationId != authority.DelegationId
            || receipt.Evidence.Authorization.Binding != authority.Binding)
            throw new RewardDataException("reward-signing-binding-mismatch");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json(receipt)));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(authority.Key), Encoding.UTF8.GetBytes(payload)));
        var result = new RewardSignedReceipt(1, authority.DelegationId, payload, signature);
        if (Encoding.UTF8.GetByteCount(Json(result)) > MaxReceiptBytes) throw new RewardDataException("reward-receipt-too-large");
        return result;
    }

    internal static RewardEarnedReceipt Verify(RewardHandoff authority, RewardSignedReceipt signed)
    {
        if (signed.Schema != 1 || signed.DelegationId != authority.DelegationId || signed.Payload is null
            || signed.Payload.Length > MaxReceiptBytes || !RewardIdentity.IsHex(signed.Signature, 64))
            throw new RewardDataException("invalid-signed-reward");
        var expected = HMACSHA256.HashData(Convert.FromBase64String(authority.Key), Encoding.UTF8.GetBytes(signed.Payload));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signed.Signature)))
            throw new RewardDataException("reward-signature-mismatch");
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload));
        using var document = JsonDocument.Parse(json);
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        var receipt = document.Deserialize<RewardEarnedReceipt>(RewardSettlementCore.JournalJson)
            ?? throw new RewardDataException("invalid-earned-reward");
        if (receipt.Schema != 1 || receipt.DelegationId != authority.DelegationId || receipt.Evidence is null
            || receipt.Evidence.Schema != 1 || receipt.Evidence.Authorization?.Binding != authority.Binding)
            throw new RewardDataException("earned-reward-binding-mismatch");
        RequireProof(receipt.Evidence);
        return receipt;
    }

    internal static void RequireProof(RewardApprovalCandidate candidate)
    {
        RewardResultValidator.ValidateAuthorization(candidate.Authorization);
        var a = candidate.Authorization;
        var p = candidate.Restoration;
        if (p is null || p.Schema != 1 || p.Binding != a.Binding || p.RunId != a.RunId || p.ArmToken != a.ArmToken
            || p.NativeEndIdentity != a.NativeEndIdentity || p.RestorationId != a.RestorationId
            || p.State != RewardRestorationState.Persisted || p.ExpectedProfileRevision != a.CleanProfileRevision
            || p.PersistedProfileRevision != a.CleanProfileRevision)
            throw new RewardDataException("earned-restoration-mismatch");
    }
}
