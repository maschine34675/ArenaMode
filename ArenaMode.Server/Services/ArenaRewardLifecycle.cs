using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Services;

/// <summary>
/// Observes successful native profile protection. No profile writes occur here;
/// the reward coordinator receives a positive clone/swap/disk-read chain only.
/// </summary>
internal static class ArenaRewardLifecycle
{
    private static bool _initialized;

    internal static void Initialize(ProfileSnapshotService snapshots, JsonUtil json)
    {
        if (_initialized) return;
        snapshots.SnapshotTaken += (id, profile) =>
        {
            var rewards = RewardRuntimeCoordinator.Current;
            rewards.ObserveSnapshot(id.ToString(), () => json.Serialize(profile)
                ?? throw new InvalidOperationException("Could not serialize reward snapshot."));
        };
        snapshots.SnapshotRestored += (id, profile) =>
            RewardRuntimeCoordinator.Current.ObserveRestored(id.ToString(), () => json.Serialize(profile)
                ?? throw new InvalidOperationException("Could not serialize restored reward snapshot."));
        snapshots.SnapshotDiscarded += id => RewardRuntimeCoordinator.Current.DiscardSnapshot(id.ToString());
        _initialized = true;
    }

    internal static void Persisted(MongoId sessionId, string nativeEndIdentity, bool recoveryOnly = false)
        => Persisted(sessionId, nativeEndIdentity, recoveryOnly, RewardRuntimeCoordinator.Current,
            RewardStartupFiles.Canonical(Directory.GetCurrentDirectory()));

    // Tests: the coordinator and the runtime root this observes.
    internal static void Persisted(MongoId sessionId, string nativeEndIdentity, bool recoveryOnly,
        RewardRuntimeCoordinator coordinator, string root)
    {
        try
        {
            if (!coordinator.HasCapture(sessionId.ToString())) return;
            string id = sessionId.ToString();
            if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("invalid-profile-id");
            string path = Path.Combine(root, "user", "profiles", id + ".json");
            RewardStartupFiles.RequireLocalPath(root, path);
            // SaveProfileAsync may skip an invalid profile or a cached hash, and
            // a previous write can have failed after its cache was updated.
            // A successful Task alone is therefore never restoration evidence.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 64 * 1024 * 1024) throw new RewardDataException("profile-too-large");
            using var reader = new StreamReader(file);
            string persisted = reader.ReadToEnd();
            // Under a profile-write mod the file is not the restored profile but that profile run through the
            // mod's write. Resolve validates the captured input/output pair on this very disk text; without a
            // transformation it is Strict and the checkpoint compares the disk exactly as before.
            var proof = ProfileWriteProof.Resolve(id, persisted);
            string? pipelineInput = proof.Mode == ProfileProofMode.Pipeline ? proof.Comparable : null;
            if (recoveryOnly) coordinator.RecoverPersisted(id, persisted, pipelineInput);
            else coordinator.FinalizePersisted(id, nativeEndIdentity, persisted, pipelineInput);
        }
        catch (Exception e)
        {
            string reason = e is RewardDataException ? e.Message : e.GetType().Name;
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] Reward result withheld ({reason}); the protected Arena end remains complete.");
        }
        finally { Compat.ArenaTarkovaColaState.Release(sessionId.ToString()); }
    }
}
