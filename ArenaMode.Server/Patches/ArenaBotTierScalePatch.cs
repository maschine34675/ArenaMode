using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Spt.Bots;
using System.Reflection;
using ArenaMode.Shared;
using ArenaMode.Server.Services;

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
/// Native SPT and older APBS read a per-map level range that overrides
/// the player-relative roll. That range is rebuilt for EVERY bot generation
/// request, which is what makes this work — raise it as the raid goes on and
/// each new wave is kitted a little better than the last, without a single
/// value of theirs being overwritten.
///
/// Deliberately a prefix on the exact session's wave rather than a write to the
/// shared config that feeds it: nothing outside this raid can see it, so the
/// mod's "normal raids are untouched" property holds without a snapshot to
/// restore or a failure mode where a crash leaves the setting behind. Protected
/// team Arena leaves SPT's real player-relative values alone.
///
/// Only Gun Game ramps (start level to the difficulty step's gear level over the map's
/// ramp minutes, on the server's wall clock since the raid went live); every other mode
/// holds the step's gear level from the first request, so a Team Deathmatch or Domination
/// request is an ordinary request with the fixed band; no client clock rides on it.
/// The one exception is solo Tactical: its first two rounds are lighter than the step's
/// level (ArenaDifficultyPolicy.TacticalGearLevel), by the round the client reported
/// to the exact live raid, round 1 until it did; protected PitFireTeam Tactical stays native.
/// APBS 2.4.1 instead reads its global raid level; the optional range adapter
/// carries this request's explicit Arena bounds through SPT's per-bot clone.
/// </summary>
public sealed class ArenaBotTierScalePatch : AbstractPatch
{
    // The band around the gear level is ArenaDifficultyPolicy.GearBand (the one source, shared with the client's pool purge).
    // How far a bot's level may sit either side of where the ramp currently is.
    //
    // This has to be spent on BOTH halves of the range, because the field we
    // set is not an instruction — it is a clamp. The system builds its own
    // window around the PLAYER's level and then clamps that window into ours,
    // so on a level one profile both ends of the window land on our minimum and
    // every bot in the raid comes out at exactly the same level. Moving the
    // player level the window is built around is what makes the range real
    // again.

    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotController).GetMethod(
            "GenerateBotWave",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(MongoId),
                typeof(GenerateCondition),
                typeof(BotGenerationDetails),
            ],
            modifiers: null)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, BotGenerationDetails botGenerationDetails)
    {
        try
        {
            // Native details can be reused by another caller. An early return
            // must never retain a previous Arena request's ownership marker.
            if (botGenerationDetails != null)
                ArenaBotTierOwnership.ForgetWave(botGenerationDetails);
            var location = botGenerationDetails?.Location;
            if (botGenerationDetails == null || string.IsNullOrEmpty(location))
                return;

            var sessions = ArenaContext.Sessions;
            if (sessions == null
                || !sessions.TryGetBotTierContext(
                    sessionId,
                    location,
                    out var elapsed,
                    out var protectedTeam,
                    out var identity,
                    out var mode,
                    out var step))
            {
                return;
            }

            // SPT built these details from the live PMC profile and its normal
            // per-map range. PFT teammates retain real raid equipment, so the
            // opposing team must use that same native context instead of the
            // Arena band that starts at level one in Gun Game.
            if (protectedTeam)
                return;

            if (!ArenaContext.Config.Maps.TryGetValue(location!.ToLowerInvariant(), out var map))
            {
                return;
            }

            // The user's opt-out keeps the native level and range intact.
            if (!map.ScaleBotTier) return;

            // Gun Game ramps from the start level to the step's gear level over the
            // map's ramp minutes on the server wall clock; every other mode holds
            // the step's gear level from the first request, whatever the time.
            // Solo Tactical is the one mode whose band follows the ROUND instead: the
            // client reports it (/arenamode/tactical-round) and the first two rounds are
            // lighter (ArenaDifficultyPolicy.TacticalGearLevel). Protected PitFireTeam
            // raids returned above, so they never reach this branch. Round 1 until the
            // first report, because the raid-start bot preload that feeds round 1 comes
            // before any report can.
            var progress = ArenaDifficultyPolicy.Progress(mode, elapsed.TotalSeconds, map.BotTierRampMinutes);
            var tacticalRound = ArenaDifficultyPolicy.IsTacticalMode(mode) ? sessions.GetTacticalRound(identity) : 0;
            var here = tacticalRound > 0
                ? ArenaDifficultyPolicy.TacticalGearLevel(step, map.BotTierStartLevel, map.BotTierEndLevel, tacticalRound)
                : ArenaDifficultyPolicy.GearLevelAt(step, map.BotTierStartLevel, map.BotTierEndLevel, progress);

            // Native SPT/older APBS use both the anchor and the clamp. The
            // optional APBS 2.4.1 adapter restores these explicit Arena bounds
            // after its global-level/weighted range selection.
            botGenerationDetails.PlayerLevel = here;
            var band = ArenaDifficultyPolicy.GearBand(here);
            botGenerationDetails.LocationSpecificPmcLevelOverride = new MinMax<int>(band.Min, band.Max);

            if (identity != null)
                ArenaBotTierOwnership.MarkWave(botGenerationDetails, sessions, identity);

            Report(location!, mode, step, elapsed, here, progress, tacticalRound);
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
    private static void Report(string location, string? mode, ArenaDifficultyStep step, TimeSpan elapsed, int level, double progress, int tacticalRound)
    {
        if (level == _lastReported)
            return;

        _lastReported = level;
        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Bot gear band on {location}: {elapsed.TotalMinutes:0} min in, "
            + $"bots now level {ArenaDifficultyPolicy.GearBand(level).Min}-{ArenaDifficultyPolicy.GearBand(level).Max}; "
            + $"mode={mode}, step={ArenaDifficultyPolicy.ToWire(step)}, progress={progress:P0}"
            + (tacticalRound > 0 ? $", round={tacticalRound}." : "."));
    }

    /// <summary>A new raid starts the ramp over, so it should report from the bottom again.</summary>
    public static void ResetReporting()
    {
        _lastReported = -1;
        ApbsBotTierCompatibility.ResetReporting();
    }
}
