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
    /// <summary>
    /// How far a bot's level may sit either side of where the ramp currently is.
    ///
    /// This has to be spent on BOTH halves of the range, because the field we
    /// set is not an instruction — it is a clamp. The system builds its own
    /// window around the PLAYER's level and then clamps that window into ours,
    /// so on a level one profile both ends of the window land on our minimum and
    /// every bot in the raid comes out at exactly the same level. Moving the
    /// player level the window is built around is what makes the range real
    /// again.
    /// </summary>
    private const int Spread = 5;

    /// <summary>
    /// The highest level this may ever claim, and it is a hard safety bound
    /// rather than a taste.
    ///
    /// The ramp writes a level the bot system then looks up in its tier table,
    /// and that lookup throws rather than shrugging when no row matches — inside
    /// their code, well outside this patch's own guard. A config value past the
    /// end of that table would therefore not degrade the ramp, it would kill
    /// every bot in the raid and leave an empty arena with nothing in the log to
    /// explain it. The experience table runs out first, at 79, and past roughly
    /// 84 the range quietly collapses back onto a single level — the exact fault
    /// this class was last fixed for. So 79 it is, and the config is clamped to
    /// it instead of being trusted.
    /// </summary>
    private const int MaxUsableLevel = 79;

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

            var start = Math.Clamp(map.BotTierStartLevel, 1, MaxUsableLevel);
            var end = Math.Clamp(map.BotTierEndLevel, start, MaxUsableLevel);
            var here = (int)Math.Round(start + (end - start) * progress);

            // Both together, and neither works alone: the level the window is
            // built around, and the bounds that window is then clamped into.
            __result.PlayerLevel = here;
            __result.LocationSpecificPmcLevelOverride =
                new MinMax<int>(Math.Max(1, here - Spread), here + Spread);

            Report(location!, elapsed, here);
        }
        catch (Exception ex)
        {
            // Bot generation must never fail over a difficulty curve.
            ArenaContext.Logger?.Warning($"[ArenaMode] Bot tier scaling skipped: {ex.Message}");
        }
    }

    private static int _lastReported = -1;

    /// <summary>
    /// Says where the ramp is, once per level it passes through.
    ///
    /// Bot generation happens many times a minute and the answer barely moves
    /// between two of them, so logging every request would bury the raid. But
    /// logging NOTHING is worse, and was: the first question asked of this
    /// feature after a playtest was what levels it had actually handed out, and
    /// the log had nothing to say.
    /// </summary>
    private static void Report(string location, TimeSpan elapsed, int level)
    {
        if (level == _lastReported)
            return;

        _lastReported = level;
        ArenaContext.Logger?.Info(
            $"[ArenaMode] Bot tier ramp on {location}: {elapsed.TotalMinutes:0} min in, "
            + $"bots now level {Math.Max(1, level - Spread)}-{level + Spread}.");
    }

    /// <summary>A new raid starts the ramp over, so it should report from the bottom again.</summary>
    public static void ResetReporting() => _lastReported = -1;
}
