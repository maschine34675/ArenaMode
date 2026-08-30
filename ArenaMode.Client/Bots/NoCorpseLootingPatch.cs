using ArenaMode.Config;
using ArenaMode.Session;
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
/// </summary>
internal sealed class NoCorpseLootingPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotDeadBodyWork), nameof(BotDeadBodyWork.StartLook));

    [PatchPrefix]
    private static bool Prefix()
        => !(ArenaSession.IsActive && Settings.NoCorpseLooting.Value);
}
