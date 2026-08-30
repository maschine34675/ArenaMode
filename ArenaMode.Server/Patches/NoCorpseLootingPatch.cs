using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Stops bots rifling through corpses during an arena raid.
///
/// Bots that loot swap gear on the spot and throw what they were wearing on the
/// floor. In a normal raid that is flavour; here it is a leak. A dropped rig is
/// a world item, world items are never cleaned up in an offline raid, and this
/// mode produces over two hundred corpses an hour for them to rummage through.
/// The bots also walk to bodies to do it, which is time not spent fighting —
/// the opposite of the point of an arena.
///
/// It is a data value rather than a patch on behaviour: the bot's own settings
/// carry HOW_WORK_OVER_DEAD_BODY, and at SimpleLook the routine glances at the
/// body and exits before any of the looting states. The server hands out a
/// CLONE of those settings, so changing it here reaches this raid and nothing
/// else — no snapshot to restore, and the shared database is untouched.
///
/// The gate is "an arena raid is starting or running" rather than a session id,
/// because the client fetches every difficulty in one bundle and that request
/// carries no session.
/// </summary>
public sealed class NoCorpseLootingPatch : AbstractPatch
{
    /// <summary>EDeadBodyWorkWay.SimpleLook — look, say something, move on.</summary>
    private const int SimpleLook = 0;

    private static bool _reported;

    public static void ResetReporting() => _reported = false;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotController).GetMethod(
            nameof(BotController.GetBotDifficulty),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(ref DifficultyCategories? __result)
    {
        try
        {
            if (__result?.Mind == null || !ArenaContext.Config.DisableCorpseLooting)
                return;

            var sessions = ArenaContext.Sessions;
            if (sessions == null || !sessions.AnyArmedOrInRaid())
                return;

            __result.Mind.HowWorkOverDeadBody = SimpleLook;

            if (!_reported)
            {
                _reported = true;
                ArenaContext.Logger?.Info("[ArenaMode] Corpse looting switched off for this raid.");
            }
        }
        catch (Exception ex)
        {
            ArenaContext.Logger?.Warning($"[ArenaMode] Corpse looting switch failed: {ex.Message}");
        }
    }
}
