using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Feeds the arena bot cap into /singleplayer/settings/bot/maxCap while an arena
/// raid is running on that location. Math.Max keeps a higher cap set by other
/// mods (e.g. SVM) intact; normal raids are unaffected (session gate).
/// </summary>
public sealed class BotCapPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotController).GetMethod(
            nameof(BotController.GetBotCap),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(string location, ref int __result)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null || !sessions.AnyInRaidOnMap(location))
            return;

        if (ArenaContext.Config.Maps.TryGetValue(location.ToLowerInvariant(), out var mapConfig))
            __result = Math.Max(mapConfig.BotMax, __result);
    }
}
