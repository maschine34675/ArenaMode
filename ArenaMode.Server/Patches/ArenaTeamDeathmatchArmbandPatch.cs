using System.Reflection;
using ArenaMode.Server.Services;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ArenaMode.Server.Patches;

/// <summary>
/// PrepareAndGenerateBot is synchronous: its return includes the completed
/// inner GenerateBot and APBS inventory replacement. The outer controller has
/// not cached the bot or rewritten its side to Savage, and the client has not
/// received it for bundle preloading. No APBS hook/order/config dependency.
/// </summary>
public sealed class ArenaTeamDeathmatchArmbandPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
        => typeof(BotGenerator).GetMethod(nameof(BotGenerator.PrepareAndGenerateBot),
            [typeof(MongoId), typeof(BotGenerationDetails)])!;

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, BotGenerationDetails botGenerationDetails, out string? __state)
    {
        __state = null;
        if (TeamDeathmatchArmbandPolicy.IsPmcRequest(botGenerationDetails)
            && ArenaContext.Sessions?.TryGetTeamDeathmatchBotContext(
                sessionId, botGenerationDetails.Location, out var identity) == true)
            __state = identity;
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, BotGenerationDetails botGenerationDetails,
        string? __state, ref BotBase __result)
    {
        try
        {
            if (__state == null
                || ArenaContext.Sessions?.TryGetTeamDeathmatchBotContext(
                    sessionId, botGenerationDetails?.Location, out var identity) != true
                || !string.Equals(__state, identity, StringComparison.Ordinal)
                || __result?.Id == sessionId)
                return;

            __result = TeamDeathmatchArmbandPolicy.Apply(__result, botGenerationDetails)!;
        }
        catch (Exception ex)
        {
            ArenaContext.Logger?.Warning($"[ArenaMode] Team DM armband skipped: {ex.Message}");
        }
    }
}
