using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Layer 1 of the arcade profile protection: for an arena raid, skip the ENTIRE
/// post-raid PMC processing (XP, skills, stats, quests, achievements, trader
/// standing, health, insurance-lost events, FiR, inventory) and swap the armed
/// snapshot back in (layer 2). EndLocalRaidAsync saves the profile right after
/// this method returns, persisting the snapshot — no async-postfix hazard.
/// </summary>
public sealed class PostRaidPmcSkipPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "HandlePostRaidPmc",
            BindingFlags.Instance | BindingFlags.NonPublic,
            Type.DefaultBinder,
            [
                typeof(MongoId),
                typeof(SptProfile),
                typeof(PmcData),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(EndLocalRaidRequestData),
                typeof(string)
            ],
            null)!;
    }

    [PatchPrefix]
    public static bool Prefix(MongoId sessionId)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null || !sessions.IsInRaid(sessionId))
            return true;

        var restored = ArenaContext.Snapshots?.RestoreSnapshot(sessionId) ?? false;

        // Normally cleared inside the method we just skipped — compensate here so
        // the bot-details cache does not grow across arena raids.
        ArenaContext.MatchBotCache?.ClearCache();

        sessions.MarkIdle(sessionId);
        ArenaContext.Logger?.Info(
            $"[ArenaMode] Arena raid ended — post-raid processing skipped, snapshot restored: {restored}.");
        return false;
    }
}
