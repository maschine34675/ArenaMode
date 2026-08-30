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
/// The behaviour itself is per-call and asks nothing that was cached, so gating
/// it here is exact: the routine simply does not start while the arena is
/// running, and the moment the raid ends it works again.
///
/// The player's own teammates are exempt. A friendly-bot mod's followers loot on
/// command, and the reasons above do not apply to them: there are a handful of
/// them rather than two hundred, and what they pick up is picked up because the
/// player asked for it.
/// </summary>
internal sealed class NoCorpseLootingPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotDeadBodyWork), nameof(BotDeadBodyWork.StartLook));

    [PatchPrefix]
    private static bool Prefix(BotDeadBodyWork __instance)
    {
        if (!ArenaSession.IsActive || !Settings.NoCorpseLooting.Value)
            return true;

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
