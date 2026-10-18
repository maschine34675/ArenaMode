using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Stops bots working over corpses during an arena raid.
///
/// Bots that loot swap gear on the spot and drop what they were wearing.
/// Nothing in an offline raid ever cleans a dropped item up, and this mode makes
/// over two hundred corpses an hour to rummage through, so it is a leak as well
/// as time not spent fighting.
///
/// This was first attempted on the SERVER, by handing out bot difficulty
/// settings with the behaviour switched off. That cannot work: the client
/// fetches every difficulty ONCE per game process and caches the snapshot for
/// the rest of the session. Whichever raid happened to be first decides the
/// setting for every raid after it — so the arena either never got the change,
/// or a normal raid inherited it. The mod's whole promise is that normal raids
/// are untouched, and a setting that leaks across raids breaks it.
///
/// The gate is on BotDeadBodyWork.UpdateCheck, the step in which a bot CHOOSES
/// a body: PatrolActionsLayer.ShallUseNow calls it on every arbitration (and
/// FollowerPatrolLayer.GetDecision for a boss's followers), and while ShallUse
/// is false it is nothing but the search (method_4) that sets _activeBody and
/// ShallUse. Skipping it there means no body is ever chosen, the layer never
/// hands out the dead-body node, and no state is touched — the routine asks
/// nothing that was cached, so the moment the raid ends it works again. Work
/// that is already in progress (ShallUse true) is let through to finish and
/// leave normally.
///
/// One thing the vanilla work did on the side no longer happens for gated
/// bots: finishing a body removed its record from the group's
/// DeadBodiesController. Those records now stay until the corpse trim
/// (CorpseCleanupService.RemoveDeadBodyRecord) takes them, so with MaxCorpses
/// at 0 they last the raid. That is bounded by the corpses the player chose to
/// keep, and the only readers are the choosing step itself, which is skipped,
/// and an exempt teammate's nearest-body scan.
///
/// Gating StartLook instead — the first version, never registered and so never
/// run — would not have been clean: the node sets _isStay after the skipped
/// call, the loot state machine's cursor (_curLookState) is left wherever the
/// previous body left it, and one UpdateWorks tick can run before the layer
/// notices ShallLeave; on a second corpse that tick is the LootWeapon step.
///
/// The player's own teammates are exempt. A friendly-bot mod's followers loot on
/// command, and the reasons above do not apply to them: there are a handful of
/// them rather than two hundred, and what they pick up is picked up because the
/// player asked for it.
/// </summary>
internal sealed class ArenaNoCorpseLootingPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotDeadBodyWork), nameof(BotDeadBodyWork.UpdateCheck));

    [PatchPrefix]
    private static bool Prefix(BotDeadBodyWork __instance)
    {
        if (!ArenaSession.IsActive || !Settings.NoCorpseLooting.Value)
            return true;

        // A body already under work is finished or left the vanilla way; only
        // the choice of a new one is refused.
        if (__instance == null || __instance.ShallUse)
            return true;

        // Faction allies are disposable Arena combatants, not commandable PFT
        // followers. Both TDM sides retain the same corpse-looting restriction.
        if (TeamDeathmatchRelations.Enabled)
            return false;

        return IsPlayerTeammate(__instance);
    }

    /// <summary>
    /// Whether this bot is on the player's side, asked through the group's own
    /// ally list — the same question ArenaHostility asks, and for the same
    /// reason: it needs no reference to whichever teammate mod is installed.
    /// </summary>
    private static bool IsPlayerTeammate(BotDeadBodyWork work)
    {
        try
        {
            var mainPlayer = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer
                : null;

            return mainPlayer != null && ArenaHostility.CountsAsAlly(work?._owner?.BotsGroup, mainPlayer);
        }
        catch
        {
            // Fail toward the arena's own behaviour: blocking a loot is a
            // nuisance, an exception in a per-bot hook is a raid.
            return false;
        }
    }
}
