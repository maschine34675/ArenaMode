using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Spt.Bots;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Makes the bots' GEAR climb over an arena raid, the way their behaviour
/// already does.
///
/// The progressive bot system decides a bot's whole loadout from a tier, and it
/// derives that tier from the bot's level, which it rolls relative to the
/// PLAYER's level. The arena freezes the profile on purpose, so that number
/// never moves — on a fresh profile every raid is tier one, forever, and the
/// system's entire progression is inert in here.
///
/// It does read one field first, though: a per-map level range that overrides
/// the player-relative roll. That range is rebuilt for EVERY bot generation
/// request, which is what makes this work — raise it as the raid goes on and
/// each new wave is kitted a little better than the last, without a single
/// value of theirs being overwritten.
///
/// Deliberately a postfix on the per-request object rather than a write to the
/// shared config that feeds it: nothing outside this raid can see it, so the
/// mod's "normal raids are untouched" property holds without a snapshot to
/// restore or a failure mode where a crash leaves the setting behind.
///
/// The ramp runs on raid TIME rather than the client's pressure curve. Time is
/// that curve's backbone anyway, and reading it here costs no traffic and
/// cannot desync.
/// </summary>
public sealed class BotTierScalePatch : AbstractPatch
{
    /// <summary>A tier band is ten levels wide; handing out one keeps a wave from being uniform.</summary>
    private const int BandWidth = 9;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotController).GetMethod(
            "GetBotGenerationDetailsForWave",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPostfix]
    public static void Postfix(ref BotGenerationDetails __result, GetRaidConfigurationRequestData? raidSettings)
    {
        try
        {
            var location = raidSettings?.Location;
            if (__result == null || string.IsNullOrEmpty(location))
                return;

            var sessions = ArenaContext.Sessions;
            if (sessions == null || !sessions.TryGetRaidElapsed(location, out var elapsed))
                return;

            if (!ArenaContext.Config.Maps.TryGetValue(location!.ToLowerInvariant(), out var map)
                || !map.ScaleBotTier)
            {
                return;
            }

            var rampMinutes = Math.Max(1, map.BotTierRampMinutes);
            var progress = Math.Clamp(elapsed.TotalMinutes / rampMinutes, 0d, 1d);

            var start = Math.Max(1, map.BotTierStartLevel);
            var end = Math.Max(start, map.BotTierEndLevel);
            var min = (int)Math.Round(start + (end - start) * progress);

            __result.LocationSpecificPmcLevelOverride = new MinMax<int>(min, min + BandWidth);
        }
        catch (Exception ex)
        {
            // Bot generation must never fail over a difficulty curve.
            ArenaContext.Logger?.Warning($"[ArenaMode] Bot tier scaling skipped: {ex.Message}");
        }
    }
}
