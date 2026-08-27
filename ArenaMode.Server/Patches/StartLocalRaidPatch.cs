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
        }
    }
}
