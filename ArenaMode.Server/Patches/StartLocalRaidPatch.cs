using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Armed → InRaid when a local raid starts on the armed map. Never skips the
/// original (prefix only — the target is async, postfixes on async methods fire
/// at the first suspension and are therefore off-limits by project rule).
/// </summary>
public sealed class StartLocalRaidPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            nameof(LocationLifecycleService.StartLocalRaidAsync),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, StartLocalRaidRequestData request)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null)
            return;

        if (sessions.TryConsumeArm(sessionId, request?.Location))
        {
            ArenaContext.Logger?.Info(
                $"[ArenaMode] Arena raid starting on {request?.Location} — profile protection active.");
            return;
        }

        // A raid starting WITHOUT consuming an arm while the session still reads InRaid
        // means the previous arena raid never reached its post-raid restore (client
        // crash / Alt-F4). Without this, the post-raid skip patch would eat THIS normal
        // raid's results and roll the profile back to the stale snapshot.
        if (sessions.IsInRaid(sessionId))
        {
            ArenaContext.Logger?.Warning(
                "[ArenaMode] Crashed arena raid detected — restoring stale snapshot before this raid starts.");
            ArenaContext.Snapshots?.RestoreSnapshot(sessionId);
            sessions.MarkIdle(sessionId);
            return;
        }

        // Arm expired or map mismatched (TryConsumeArm idled the session): its snapshot
        // must not survive to be restored over later legitimate progress.
        if (ArenaContext.Snapshots != null && ArenaContext.Snapshots.HasSnapshot(sessionId))
        {
            ArenaContext.Logger?.Info("[ArenaMode] Discarding snapshot of an unconsumed arena arm.");
            ArenaContext.Snapshots.DiscardSnapshot(sessionId);
        }
    }
}
