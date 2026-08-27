using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ArenaMode.Server.Services;

/// <summary>
/// Safety net of the arcade profile protection: a deep clone of the profile is taken
/// when an arena raid is armed and swapped back in when the raid ends — so even
/// changes applied outside the (skipped) post-raid processing can never persist.
/// The disk profile stays clean at all times: layer 1 (HandlePostRaidPmc skip)
/// means the raid-end save writes an unchanged profile even if this layer fails.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ProfileSnapshotService(
    ICloner cloner,
    SaveServer saveServer,
    ISptLogger<ProfileSnapshotService> logger)
{
    private readonly Dictionary<string, SptProfile> _snapshots = new();
    private readonly Lock _lock = new();

    public bool TakeSnapshot(MongoId sessionId)
    {
        try
        {
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
            }

            logger.Info($"[ArenaMode] Profile snapshot taken for session {sessionId}.");
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
    /// is responsible for persistence: in the HandlePostRaidPmc prefix the surrounding
    /// EndLocalRaidAsync saves the profile right afterwards.
    /// </summary>
    public bool RestoreSnapshot(MongoId sessionId)
    {
        SptProfile? snapshot;
        lock (_lock)
        {
            if (!_snapshots.Remove(sessionId.ToString(), out snapshot))
                return false;
        }

        try
        {
            saveServer.DeleteProfileById(sessionId);
            saveServer.AddProfile(snapshot!);
            logger.Info($"[ArenaMode] Profile snapshot restored for session {sessionId}.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Profile snapshot restore FAILED: {ex}");
            return false;
        }
    }

    public void DiscardSnapshot(MongoId sessionId)
    {
        lock (_lock)
        {
            if (_snapshots.Remove(sessionId.ToString()))
                logger.Info($"[ArenaMode] Profile snapshot discarded for session {sessionId}.");
        }
    }
}
