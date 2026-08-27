using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Guard: lostOnDeathConfig.WipeOnRaidStart would wipe the REAL equipped inventory
/// at arena raid start. Arena raids must never touch the real gear.
/// </summary>
public sealed class PreRaidWipeSkipPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "HandlePreRaidInventoryChecks",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static bool Prefix(MongoId sessionId)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null || !sessions.IsInRaid(sessionId))
            return true;

        ArenaContext.Logger?.Info("[ArenaMode] Skipping pre-raid inventory wipe for arena raid.");
        return false;
    }
}
