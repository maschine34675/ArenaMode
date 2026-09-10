using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Spt.Bots;
using System.Reflection;
using System.Text.Json;
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
/// team Arena leaves SPT's real player-relative values alone; the solo modes keep
/// this progression curve.
///
/// Finite TDM transports its frozen live-match clock in the existing native
/// demand request. Other modes/older clients retain the server raid-time ramp.
/// APBS 2.4.1 instead reads its global raid level; the optional range adapter
/// carries this request's explicit Arena bounds through SPT's per-bot clone.
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
    public static void Prefix(MongoId sessionId, BotGenerationDetails botGenerationDetails, GenerateCondition? __1 = null)
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
                    out var identity))
            {
                return;
            }

            // SPT built these details from the live PMC profile and its normal
            // per-map range. PFT teammates retain real raid equipment, so the
            // opposing team must use that same native context instead of the
            // solo/Gun Game progression that starts at level one.
            if (protectedTeam)
                return;

            if (!ArenaContext.Config.Maps.TryGetValue(location!.ToLowerInvariant(), out var map))
            {
                return;
            }

            var rampMinutes = Math.Max(1, map.BotTierRampMinutes);
            int duration = 0;
            if (__1?.ExtensionData?.TryGetValue(ArenaDifficultyClock.WireKey, out var wireClock) == true)
            {
                // The native serializer preserves this condition through Task.Run.
                // Bind its clock to the exact current session/map/arm, never to a
                // map-wide or ambient last-received time. Old clients keep the ramp above.
                var clock = wireClock is JsonElement element ? element.Deserialize<ArenaDifficultyClock>() : null;
                if (clock?.IsValid() != true
                    || __1.Limit <= 0 || botGenerationDetails.BotCountToGenerate <= 0
                    || !sessions.TryGetTeamDeathmatchBotContext(sessionId, location, out var token)
                    || !string.Equals(token, clock.ArmToken, StringComparison.Ordinal)) return;
                duration = clock.TimeLimitSeconds;
                elapsed = TimeSpan.FromSeconds(clock.PlayingElapsedSeconds);
                // Do not fill a 15-profile reserve with early-match gear. This
                // changes only the current finite TDM request, never SPT config.
                //
                // It stays scoped to this branch on purpose. Widening it to every
                // arena request would also shrink the raid-start pool the three
                // scav modes draw from, and their misses are paid on the SPAWN
                // path mid-raid rather than in a loading screen. The saving is
                // real and the risk is unmeasured; measuring it needs a long
                // Endless or Gun Game raid past the twenty-minute mark, which is
                // the one performance question this project already has open.
                botGenerationDetails.BotCountToGenerate = Math.Min(botGenerationDetails.BotCountToGenerate, __1.Limit);
            }
            // The one-profile demand remains bounded even when the user opts
            // out of gear progression; the native level/range then stay intact.
            if (!map.ScaleBotTier) return;
            var progress = ArenaDifficultyClock.Progress(elapsed.TotalSeconds, duration, rampMinutes * 60d);

            var start = Math.Clamp(map.BotTierStartLevel, 1, MaxUsableLevel);
            var end = Math.Clamp(map.BotTierEndLevel, start, MaxUsableLevel);
            var here = (int)Math.Round(start + (end - start) * progress);

            // Native SPT/older APBS use both the anchor and the clamp. The
            // optional APBS 2.4.1 adapter restores these explicit Arena bounds
            // after its global-level/weighted range selection.
            botGenerationDetails.PlayerLevel = here;
            botGenerationDetails.LocationSpecificPmcLevelOverride =
                new MinMax<int>(Math.Max(1, here - Spread), here + Spread);

            if (identity != null)
                ArenaBotTierOwnership.MarkWave(botGenerationDetails, sessions, identity);

            Report(location!, elapsed, here, duration, progress);
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
    private static void Report(string location, TimeSpan elapsed, int level, int duration, double progress)
    {
        if (level == _lastReported)
            return;

        _lastReported = level;
        ArenaContext.Logger?.Info(
            $"[ArenaMode] Bot tier ramp on {location}: {elapsed.TotalMinutes:0} min in, "
            + $"bots now level {Math.Max(1, level - Spread)}-{level + Spread}; "
            + $"clock={(duration > 0 ? "match" : "legacy")}, duration={duration}s, progress={progress:P0}.");
    }

    /// <summary>A new raid starts the ramp over, so it should report from the bottom again.</summary>
    public static void ResetReporting()
    {
        _lastReported = -1;
        ApbsBotTierCompatibility.ResetReporting();
    }
}
