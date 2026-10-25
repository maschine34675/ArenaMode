using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Collections.Concurrent;
using System.Reflection;

namespace ArenaMode.Server.Services;

/// <summary>
/// Safety net of the arcade profile protection: a deep clone of the profile is taken
/// when an arena raid is armed and swapped back in when the raid ends — so even
/// changes applied outside the (skipped) post-raid processing can never persist.
/// The disk profile stays clean at all times: the full native EndLocalRaidAsync
/// replacement skips SPT's ordinary post-raid mutation pipeline and persists
/// only after this exact snapshot has been restored.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ProfileSnapshotService(
    ICloner cloner,
    SaveServer saveServer,
    ISptLogger<ProfileSnapshotService> logger)
{
    private static readonly FieldInfo? ProfileStoreField = typeof(SaveServer).GetField(
        "profiles",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Dictionary<string, SptProfile> _snapshots = new();
    private readonly Lock _lock = new();

    // Observers receive the actual cloned/restored object, never a client result.
    // They are ancillary: a reward failure must not alter profile protection.
    internal event Action<MongoId, SptProfile>? SnapshotTaken;
    internal event Action<MongoId, SptProfile>? SnapshotRestored;
    internal event Action<MongoId>? SnapshotDiscarded;

    public bool TakeSnapshot(MongoId sessionId)
    {
        try
        {
            if (!TryGetProfileStore(out _))
            {
                logger.Error(
                    "[ArenaMode] SaveServer profile-store contract is unavailable — refusing to arm.");
                return false;
            }

            var profile = saveServer.GetProfile(sessionId);
            var clone = cloner.Clone(profile);
            if (clone == null)
            {
                logger.Error("[ArenaMode] Profile clone returned null — refusing to arm.");
                return false;
            }

            lock (_lock)
            {
                _snapshots[sessionId.ToString()] = clone;
                Notify(() => SnapshotTaken?.Invoke(sessionId, clone));
            }

            logger.Debug($"[ArenaMode] Profile snapshot taken for session {sessionId}.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Profile snapshot failed: {ex}");
            return false;
        }
    }

    public bool HasSnapshot(MongoId sessionId)
    {
        lock (_lock)
        {
            return _snapshots.ContainsKey(sessionId.ToString());
        }
    }

    /// <summary>
    /// Swap the snapshot back into the save server (in memory). The caller's context
    /// is responsible for persistence through Arena's protected native-end task.
    /// </summary>
    public bool RestoreSnapshot(MongoId sessionId)
    {
        lock (_lock)
        {
            string key = sessionId.ToString();
            if (!_snapshots.TryGetValue(key, out SptProfile? snapshot))
                return false;

            try
            {
                if (!TryGetProfileStore(out var profiles))
                    throw new InvalidOperationException("SaveServer profile-store contract is unavailable");

                // SPT's public DeleteProfileById/AddProfile pair is two separate
                // operations and AddProfile hides a failed TryAdd. Replace the
                // exact observed value in its ConcurrentDictionary atomically;
                // a concurrent writer wins without creating a missing-profile
                // interval and this snapshot remains available for retry.
                bool replaced;
                if (profiles.TryGetValue(sessionId, out var current))
                    replaced = profiles.TryUpdate(sessionId, snapshot, current);
                else
                    replaced = profiles.TryAdd(sessionId, snapshot);

                if (!replaced || !profiles.TryGetValue(sessionId, out var active)
                    || !ReferenceEquals(active, snapshot))
                {
                    throw new InvalidOperationException(
                        "SaveServer profile changed concurrently; Arena snapshot was not consumed");
                }

                _snapshots.Remove(key);
                Notify(() => SnapshotRestored?.Invoke(sessionId, snapshot));
                logger.Info($"[ArenaMode] Profile snapshot restored for session {sessionId}.");
                return true;
            }
            catch (Exception ex)
            {
                // Retain the snapshot. A stale-start recovery will reject the
                // new raid and retry instead of releasing a half-swapped profile.
                logger.Error($"[ArenaMode] Profile snapshot restore FAILED; retry retained: {ex}");
                return false;
            }
        }
    }

    private bool TryGetProfileStore(
        out ConcurrentDictionary<MongoId, SptProfile> profiles)
    {
        var resolved = ProfileStoreField?.GetValue(saveServer)
            as ConcurrentDictionary<MongoId, SptProfile>;
        profiles = resolved!;
        return resolved != null;
    }

    public void DiscardSnapshot(MongoId sessionId)
    {
        Compat.ArenaTarkovaColaState.Release(sessionId.ToString());
        lock (_lock)
        {
            if (_snapshots.Remove(sessionId.ToString()))
            {
                Notify(() => SnapshotDiscarded?.Invoke(sessionId));
                logger.Debug($"[ArenaMode] Profile snapshot discarded for session {sessionId}.");
            }
        }
    }

    private void Notify(Action observer)
    {
        try { observer(); }
        catch (Exception e)
        {
            logger.Warning($"[ArenaMode] Reward evidence unavailable ({e.GetType().Name}); profile protection continues.");
        }
    }
}
