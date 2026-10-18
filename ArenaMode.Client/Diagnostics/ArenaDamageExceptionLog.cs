using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Text;

namespace ArenaMode.Diagnostics;

/// <summary>
/// Reports the state of both sides of a damage event at the moment it throws.
/// Written for what CORPSE-CLEANUP-REVIEW.md used to call the open grenade
/// finding: four to eight NullReferenceExceptions per Labyrinth raid out of
/// <c>Player.ApplyDamageInfo</c> and <c>ClientGameWorld.ShotDelegate</c>,
/// appearing only since a trimmed corpse is RELEASED rather than hidden. Its
/// reports settled it (section "Settled", then "What held that body"): the
/// dead Labyrinth boss's own tripwires, going off after Arena had released
/// him, fixed by <see cref="Bots.CorpseDamageSources"/>. It stays on as that
/// fix's regression alarm.
///
/// It records and it rethrows. Nothing here changes a damage outcome, and the
/// two questions it exists to answer are the two the game's own log cannot:
///
/// 1. WHICH SIDE IS A DESTROYED OBJECT. "null" and "destroyed" are different
///    states, and almost no native code can tell them apart: every null check
///    the damage path makes through an interface (<c>IPlayer</c>,
///    <c>IObserverToPlayerBridge</c>) is a plain reference check that Unity's
///    lifetime operator never reaches, while the same check written against
///    <c>Player</c> sends a destroyed body down its NULL branch. Before the
///    release, a body destroyed mid-raid was not a thing that happened; now it
///    is routine, so every candidate is reported as one of null / destroyed /
///    live, with its identity and whether Arena released that body.
///
/// 2. WHETHER THE THROW IS OURS. Every trace in this family is logged with a
///    bare "(wrapper dynamic-method)" frame on top and nothing above it, which
///    is why "an Arena prefix or the original body?" cannot be read off the
///    log. Two things produce that, and the log cannot tell them apart: the
///    throw really being in the original body, whose IL Harmony copies INTO the
///    dynamic method, or the trace having been reset by the rethrow at the end
///    of Harmony's finalizer block. Both fit the evidence, and so does the one
///    control the evening provided — of the four exception kinds those raids
///    logged, the three with a bare wrapper frame are all on methods Arena has
///    a finalizer on (Player.ApplyDamageInfo, Player.OnDead,
///    ClientGameWorld.ShotDelegate) and the fourth, whose trace does show its
///    innermost frames, is on an unpatched native coroutine.
///
///    Read HERE, inside the finalizer and before any rethrow, the trace is
///    intact either way, and <see cref="Exception.TargetSite"/> names the
///    throwing method outright.
///
/// Arena-gated like everything else, and reporting-only: a raid that is not an
/// Arena raid returns before anything is read. The cost outside an exception is
/// one null check — the finalizers do not build so much as a string until they
/// have been handed something to report.
/// </summary>
internal static class ArenaDamageExceptionLog
{
    /// <summary>
    /// The settled finding produced eleven reports in its worst raid. The cap is
    /// only here so that an unforeseen storm cannot fill the log, and it reports
    /// that it capped.
    /// </summary>
    private const int ReportsPerRaid = 32;

    /// <summary>Frames after the first report, which carries the whole trace.</summary>
    private const int TraceFrames = 8;

    private static int _reported, _suppressed;
    private static bool _tracedInFull, _failed;

    internal static void Enable()
    {
        new ArenaDamageExceptionLogPlayerDamagePatch().Enable();
        new ArenaDamageExceptionLogShotImpactPatch().Enable();
    }

    internal static void Reset()
    {
        _reported = 0;
        _suppressed = 0;
        _tracedInFull = false;
        _failed = false;

        // The release ledger is reset by the service that writes it
        // (CorpseCleanupService.Reset), not from here.
    }

    /// <summary>
    /// The victim's own entry point: <c>Player.ApplyDamageInfo</c>, which is
    /// where the tripwire finding's exception left the game.
    /// </summary>
    private static void ReportDamage(Exception exception, Player victim, DamageInfo damageInfo,
        EBodyPart bodyPart, EBodyPartColliderType colliderType)
    {
        if (!TryAdmit(exception, "Player.ApplyDamageInfo", out var line))
            return;

        try
        {
            Describe(line, "victim", victim);

            // The aggressor arrives as a bridge, and the bridge is an ordinary
            // managed object that outlives the body behind it. Both are reported
            // because only the second one can be destroyed.
            var bridge = damageInfo.Player;
            line.Append(" bridge=").Append(State(bridge));
            Describe(line, "aggressor", bridge?.iPlayer);

            // The two tables the native code resolves an aggressor through,
            // asked exactly as it asks them. GetAlivePlayerByProfileID is pruned
            // at death (Player.Dispose -> UnregisterPlayer);
            // GetEverExistedPlayerByID is the one table nothing ever cleans, so
            // it is the one that can hand back a destroyed body.
            var profileId = Read(() => bridge?.iPlayer?.ProfileId);
            if (!string.IsNullOrEmpty(profileId) && Singleton<GameWorld>.Instantiated)
            {
                var world = Singleton<GameWorld>.Instance;
                Describe(line, "everExisted", Resolve(() => world.GetEverExistedPlayerByID(profileId)));
                Describe(line, "aliveById", Resolve(() => world.GetAlivePlayerByProfileID(profileId)));
            }

            // Set by ManageAggressor on an earlier hit and never cleared when the
            // body it names goes away; the guarded sibling defect (CorpseAggressors,
            // which owns the binding to that protected field).
            Describe(line, "lastAggressor", Bots.CorpseAggressors.LastAggressorOf(victim));

            line.Append(" damage=").Append(Read(() => damageInfo.DamageType.ToString()))
                .Append('/').Append(Read(() => damageInfo.Damage.ToString("0.#")))
                .Append(" part=").Append(bodyPart)
                .Append(" collider=").Append(colliderType);

            // The collider the explosion picked the victim through. Its profile
            // id is a plain field, so it answers even when the body is destroyed.
            var hit = damageInfo.HittedBallisticCollider;
            line.Append(" hitCollider=").Append(State(hit))
                .Append('/').Append(Read(() => (hit as BodyPartCollider)?.PlayerProfileID));

            Emit(line, exception);
        }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>
    /// Instrumented with the same machinery and in the same raid, because it is
    /// the same open question: <c>ClientGameWorld.ShotDelegate</c> also began
    /// throwing NullReferenceExceptions only in the raids that release bodies
    /// (none in either control raid, three and two in the two release raids),
    /// and its trace is truncated by the same Harmony rethrow — an Arena prefix
    /// sits on it too (ArenaShotAwardHooks.ArenaShotAwardHooksImpactPatch). Recorded, not assumed.
    /// </summary>
    private static void ReportShot(Exception exception, Shot shot)
    {
        if (!TryAdmit(exception, "ClientGameWorld.ShotDelegate", out var line))
            return;

        try
        {
            // The native method tests this with Unity's operator and then reads
            // it again unguarded in its offline-only BTR branch, which every
            // Arena raid takes. A destroyed collider passes the first test as
            // null; whether that is what happens here is what this records.
            var hit = Resolve(() => shot.HittedBallisticCollider);
            line.Append(" hitCollider=").Append(State(hit))
                .Append('/').Append(Read(() => (hit as BodyPartCollider)?.PlayerProfileID))
                .Append(" hitType=").Append(Read(() => hit.HitType.ToString()));
            Describe(line, "victim", Resolve(() => (hit as BodyPartCollider)?.Player));

            var bridge = Resolve(() => shot.Player);
            line.Append(" shooterBridge=").Append(State(bridge));
            Describe(line, "shooter", Resolve(() => bridge?.iPlayer));

            line.Append(" outOfTime=").Append(Read(() => shot.IsFlyingOutOfTime.ToString()))
                .Append(" ammo=").Append(Read(() => shot.Ammo.TemplateId));

            Emit(line, exception);
        }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>
    /// The gate and the budget, in the order that keeps the hot path free: an
    /// event that did not throw never gets here at all, and an event that did is
    /// still only reported inside an Arena raid with the switch on.
    /// </summary>
    private static bool TryAdmit(Exception exception, string site, out StringBuilder line)
    {
        line = null;
        if (exception == null || _failed || !Settings.LogDamageExceptions.Value || !ArenaSession.IsActive)
            return false;

        if (_reported >= ReportsPerRaid)
        {
            if (_suppressed++ == 0)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena damage exception: {ReportsPerRaid} reported this raid, not reporting any more.");
            }

            return false;
        }

        _reported++;
        line = new StringBuilder("Arena damage exception #").Append(_reported)
            .Append(": site=").Append(site)
            .Append(' ').Append(exception.GetType().Name)
            .Append(" \"").Append(exception.Message).Append('"');
        return true;
    }

    /// <summary>
    /// One field per candidate. The state is the point; the identity is so the
    /// sides can be told apart across a raid, and the release verdict is what
    /// ties the exception to this change rather than to the hiding that
    /// preceded it.
    /// </summary>
    private static void Describe(StringBuilder line, string label, object candidate)
    {
        line.Append(' ').Append(label).Append('=').Append(State(candidate));
        if (ReferenceEquals(candidate, null))
            return;

        var player = AsPlayer(candidate);
        if (ReferenceEquals(player, null))
        {
            // Not a Player behind it — an ObservedPlayerView or something else
            // whose lifetime is not what this is looking for. Named, not guessed.
            line.Append('(').Append(Read(() => candidate.GetType().Name)).Append(')');
            return;
        }

        var profileId = Read(() => player.ProfileId);
        line.Append('[').Append(profileId)
            .Append(' ').Append(Read(() => player.Profile.Nickname))
            .Append(Read(() => player.IsAI ? " ai" : " human"))
            .Append(" alive=").Append(Read(() => player.HealthController.IsAlive.ToString()))

            // Two members that Player.ApplyDamageInfo dereferences in its OWN
            // IL, which is where a throw with nothing above the wrapper frame
            // has to be: `player2?.Loyalty.MarkAsAggressor(this)` guards player2
            // and not Loyalty, and Loyalty is only ever assigned by Player.Init
            // — so a body registered as alive before Init finished throws there.
            // _healthController is read unguarded on the method's first line.
            .Append(" loyalty=").Append(Read(() => player.Loyalty == null ? "null" : "set"))
            .Append(" health=").Append(Read(() => player.HealthController == null ? "null" : "set"))
            .Append(' ').Append(CorpseReleaseLedger.Describe(profileId))
            .Append(' ').Append(Bots.CorpseCleanupService.DescribeCorpseState(profileId))
            .Append(']');
    }

    /// <summary>
    /// null / destroyed / live — the distinction the whole diagnostic turns on.
    /// Unity's lifetime operator is reached through the concrete type, because a
    /// destroyed object is a perfectly live managed reference that compares
    /// equal to null only that way.
    /// </summary>
    private static string State(object candidate)
    {
        if (ReferenceEquals(candidate, null))
            return "null";

        if (candidate is UnityEngine.Object unity)
            return unity == null ? "DESTROYED" : "live";

        // A bridge or an interface reference: no lifetime of its own, and its
        // subject is reported as a field of its own.
        return "live";
    }

    /// <summary>The body behind a candidate, whatever shape it arrived in.</summary>
    private static Player AsPlayer(object candidate)
    {
        try
        {
            switch (candidate)
            {
                case Player player: return player;
                case IObserverToPlayerBridge bridge: return bridge.iPlayer as Player;
                case BodyPartCollider collider: return collider.Player as Player;
                default: return candidate as Player;
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// Where the throw came from, and the reason this class exists as a
    /// finalizer rather than a log filter: the trace is intact until Harmony
    /// rethrows, and the first report carries all of it.
    /// </summary>
    private static void Emit(StringBuilder line, Exception exception)
    {
        line.Append(" thrownIn=").Append(Read(() => Name(exception.TargetSite)));

        string trace;
        try { trace = exception.StackTrace; }
        catch { trace = null; }

        // Only a trace that exists may consume the one full-trace slot. The
        // 2026-09-17 raid spent it on the first report, whose StackTrace was
        // empty — Mono leaves both that and TargetSite empty for some of these
        // NREs — and the complete trace of the ones that HAD one was then never
        // printed. The eight-frame form happened to cover the whole chain; that
        // was luck, not design.
        if (string.IsNullOrEmpty(trace))
        {
            line.Append(" origin=none");
        }
        else if (!_tracedInFull)
        {
            _tracedInFull = true;
            line.Append(" fullTrace=\n").Append(trace);
        }
        else
        {
            line.Append(" origin=").Append(Frames(trace));
        }

        Plugin.ModLogger.LogWarning(line.ToString());
    }

    private static string Name(MethodBase method)
    {
        if (method == null)
            return "unknown";

        var owner = method.DeclaringType?.FullName;
        return owner == null ? method.Name : owner + "." + method.Name;
    }

    /// <summary>The innermost frames, without the assembly-hash noise.</summary>
    private static string Frames(string trace)
    {
        if (string.IsNullOrEmpty(trace))
            return "unknown";

        var text = new StringBuilder();
        var frames = 0;
        foreach (var raw in trace.Split('\n'))
        {
            var frame = raw.Trim();
            if (frame.Length == 0)
                continue;

            var noise = frame.IndexOf(" (at ", StringComparison.Ordinal);
            if (noise > 0)
                frame = frame.Substring(0, noise);

            if (frames++ > 0)
                text.Append(" <- ");
            text.Append(frame);
            if (frames >= TraceFrames)
                break;
        }

        return text.ToString();
    }

    /// <summary>
    /// Every read in a report is a read of state that may be half torn down, so
    /// each field degrades on its own rather than costing the whole line. A
    /// field that throws is itself evidence, so the exception's name is printed.
    /// </summary>
    private static string Read(Func<string> read)
    {
        try { return read() ?? "null"; }
        catch (Exception ex) { return "!" + ex.GetType().Name; }
    }

    private static T Resolve<T>(Func<T> read) where T : class
    {
        try { return read(); }
        catch { return null; }
    }

    /// <summary>
    /// A diagnostic must never become the failure it is looking at, and must not
    /// keep trying either: one warning, then it stands down for the raid.
    /// </summary>
    private static void Fail(Exception ex)
    {
        _failed = true;
        try { Plugin.ModLogger.LogWarning($"Arena damage exception log stood down: {ex}"); }
        catch { }
    }

    /// <summary>
    /// Finalizer only. It shares the method with two Arena prefixes
    /// (CombatPatches.ArenaCombatTeamStagingPlayerDamagePatch and
    /// ArenaSelfGrenadeTaunt.ArenaSelfGrenadeTauntDamagePatch) and their own finalizers, all of which
    /// pass the exception through unchanged, so this one sees whatever they saw.
    /// </summary>
    private sealed class ArenaDamageExceptionLogPlayerDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ApplyDamageInfo),
                new[]
                {
                    typeof(DamageInfo),
                    typeof(EBodyPart),
                    typeof(EBodyPartColliderType),
                    typeof(float)
                });

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, Player __instance, DamageInfo damageInfo,
            EBodyPart bodyPartType, EBodyPartColliderType colliderType)
        {
            // Nothing is read, built or gated on until there is something to
            // report: this runs on every hit in every raid, arena or not.
            if (__exception != null)
                ReportDamage(__exception, __instance, damageInfo, bodyPartType, colliderType);

            return __exception;
        }
    }

    private sealed class ArenaDamageExceptionLogShotImpactPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ClientGameWorld), nameof(ClientGameWorld.ShotDelegate),
                new[] { typeof(Shot) });

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, Shot shotResult)
        {
            if (__exception != null)
                ReportShot(__exception, shotResult);

            return __exception;
        }
    }
}
