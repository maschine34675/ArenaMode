using EFT;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Optional SAIN search admission, separate from movement ownership. A known
/// position alone does not set SAIN's Heard/Seen search prerequisites. Admit one
/// non-danger hearing cue per native contact episode, then let its heard time and SAIN decision
/// delay age naturally. Never synthesize visual contact or force a combat goal.
/// </summary>
internal static class TdmSearchAwareness
{
    private sealed class Cue { internal bool Admitted; }
    private sealed class Actor { internal int Id; }
    private sealed class Observation
    {
        internal float NextLog;
        internal float SampleAt;
        internal Vector3 Position;
        internal bool HasPosition;
        internal int InvalidPathSamples;
        internal float FirstInvalidPathAt;
        internal bool PathDetailLogged;
    }

    private static ConditionalWeakTable<object, Cue> _cues = new();
    private static ConditionalWeakTable<object, Observation> _observations = new();
    private static ConditionalWeakTable<object, Actor> _actors = new();
    private static int _nextActorId;
    private static readonly Dictionary<(Type, string), MemberInfo> Members = new();
    private static Type _botType, _reportType;
    private static PropertyInfo _hearing, _seen, _heard, _visible;
    private static FieldInfo _position, _sound, _place, _danger, _squad;
    private static MethodInfo _setHeard;
    private static object _generic, _hearingPlace;
    private static bool _loggedFailure;
    private const float DiagnosticSeconds = 30f;

    internal static void Reset()
    {
        _cues = new ConditionalWeakTable<object, Cue>();
        _observations = new ConditionalWeakTable<object, Observation>();
        _actors = new ConditionalWeakTable<object, Actor>();
        _nextActorId = 0;
        _loggedFailure = false;
    }

    internal static void Configure(Type enemyType, Type botType)
    {
        _botType = botType;
        _setHeard = null;
        try
        {
            var report = enemyType.Assembly.GetType("SAIN.Models.Structs.SAINHearingReport");
            var hearing = enemyType.GetProperty("Hearing");
            var seen = enemyType.GetProperty("Seen");
            var heard = enemyType.GetProperty("Heard");
            var visible = enemyType.GetProperty("IsVisible");
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            var position = report?.GetField("position", fields);
            var sound = report?.GetField("soundType", fields);
            var place = report?.GetField("placeType", fields);
            var danger = report?.GetField("isDanger", fields);
            var squad = report?.GetField("shallReportToSquad", fields);
            var setHeard = report == null ? null : hearing?.PropertyType.GetMethod("SetHeard",
                new[] { report, typeof(float) });
            if (report?.IsValueType != true || hearing == null || seen?.PropertyType != typeof(bool)
                || heard?.PropertyType != typeof(bool) || visible?.PropertyType != typeof(bool)
                || position?.FieldType != typeof(Vector3) || sound?.FieldType.IsEnum != true
                || place?.FieldType.IsEnum != true || danger?.FieldType != typeof(bool)
                || squad?.FieldType != typeof(bool) || setHeard == null || setHeard.IsStatic
                || setHeard.ReturnType.FullName != "SAIN.SAINComponent.Classes.EnemyClasses.EnemyPlace")
                throw new InvalidOperationException("SAIN hearing layout is unsupported");
            _generic = Enum.Parse(sound.FieldType, "Generic");
            _hearingPlace = Enum.Parse(place.FieldType, "Hearing");
            _reportType = report;
            _hearing = hearing;
            _seen = seen;
            _heard = heard;
            _visible = visible;
            _position = position;
            _sound = sound;
            _place = place;
            _danger = danger;
            _squad = squad;
            _setHeard = setHeard;
            Plugin.ModLogger.LogInfo("Arena TDM: optional SAIN one-shot search hearing available (visual state and search delay unchanged).");
        }
        catch (Exception ex) { Failure(ex); }
    }

    internal static void Refresh(BotOwner source, Player target, object controller, object enemy, EnemyInfo native)
    {
        string status = "hearing-unavailable";
        try
        {
            if (!TeamDeathmatchRelations.OwnsAwareness(source, target, controller, enemy, native)) return;
            if (_setHeard != null)
            {
                var cue = _cues.GetValue(enemy, _ => new Cue());
                if ((bool)_seen.GetValue(enemy) || (bool)_heard.GetValue(enemy)
                    || (bool)_visible.GetValue(enemy)) status = cue.Admitted ? "preserved" : "existing-contact";
                else
                {
                    // SAIN can forget a still-live wrapper and clear Heard. A
                    // later acquisition may start a new episode, but we never
                    // clear native contact or renew an existing heard timestamp.
                    cue.Admitted = false;
                    var hearing = _hearing.GetValue(enemy);
                    var report = Activator.CreateInstance(_reportType);
                    _position.SetValue(report, target.Position);
                    _sound.SetValue(report, _generic);
                    _place.SetValue(report, _hearingPlace);
                    _danger.SetValue(report, false);
                    _squad.SetValue(report, false);
                    if (hearing == null) status = "hearing-not-ready";
                    else if (TeamDeathmatchRelations.OwnsAwareness(source, target, controller, enemy, native))
                    {
                        if ((bool)_seen.GetValue(enemy) || (bool)_heard.GetValue(enemy)
                            || (bool)_visible.GetValue(enemy))
                        {
                            Observe(source, target, enemy, "existing-contact");
                            return;
                        }
                        // SetHeard itself enforces SAIN's ignore-sounds policy. A
                        // null result is not successful admission; do not bypass it.
                        object result = _setHeard.Invoke(hearing, new[] { report, (object)Time.time });
                        if (!TeamDeathmatchRelations.OwnsAwareness(source, target, controller, enemy, native)) return;
                        cue.Admitted = result != null && (bool)_heard.GetValue(enemy);
                        status = cue.Admitted ? "admitted" : "not-admitted";
                    }
                    else return;
                }
            }
        }
        catch (Exception ex) { status = "hearing-error"; Failure(ex); }
        Observe(source, target, enemy, status);
    }

    // At most one snapshot per physical bot every 30 seconds, including the
    // initial observation. Read-only optional properties are best-effort; missing
    // diagnostics must never disable native faction/awareness behavior.
    internal static void Observe(BotOwner source, Player target, object enemy, string status)
    {
        try
        {
            if (!TeamDeathmatchRelations.Enabled || source == null) return;
            var observation = _observations.GetValue(source, _ => new Observation());
            float now = Time.time;
            if (now < observation.NextLog) return;
            observation.NextLog = now + DiagnosticSeconds;
            var position = source.Position;
            string moved = observation.HasPosition ? Number(Math.Sqrt((position - observation.Position).sqrMagnitude)) : "n/a";
            string elapsed = observation.HasPosition ? Number(now - observation.SampleAt) : "n/a";
            observation.Position = position;
            observation.SampleAt = now;
            observation.HasPosition = true;
            object component = _botType == null ? null : source.GetComponent(_botType);
            var decision = Read(component, "Decision");
            var combat = Read(decision, "CurrentCombatDecision");
            string sourceId = ActorId(source), targetId = ActorId(target);
            // The hearing cue targets the nearest opponent, which need not be
            // SAIN's selected opponent. Snapshot its filtered GoalEnemy once;
            // all goal-prefixed contact fields below belong to that wrapper.
            var goalEnemy = Read(component, "GoalEnemy", out bool goalAvailable);
            var goalPlayer = Read(goalEnemy, "EnemyPlayer") as Player;
            string sainGoal = !goalAvailable || (goalEnemy != null && goalPlayer == null) ? "n/a" : ActorId(goalPlayer);
            string goalMatchesTarget = goalPlayer == null || target == null ? "n/a" : ReferenceEquals(goalPlayer, target).ToString();
            string goalDistance = goalPlayer == null ? "n/a" : Number(Math.Sqrt((goalPlayer.Position - position).sqrMagnitude));
            var enemyDecisions = Read(decision, "EnemyDecisions");
            // SAIN normally clears this nullable result before decision checks,
            // but no-enemy and grenade-avoidance returns precede even that reset.
            // Reasons are a cached decision sample, never a forced path query.
            var shallSearch = goalPlayer != null && combat != null && Value(combat) != "None" && Value(combat) != "AvoidGrenade"
                ? Read(enemyDecisions, "DebugShallSearch") : null;
            var reason = shallSearch is bool ? Read(enemyDecisions, "DebugSearchReasons") : null;
            var want = Read(reason, "WantSearchReasons");
            string distance = target == null ? "n/a" : Number(Math.Sqrt((target.Position - position).sqrMagnitude));
            Plugin.ModLogger.LogInfo($"Arena TDM awareness: bot={sourceId}, side={source.GetPlayer?.Profile?.Info?.Side}, target={targetId}, distance={distance}m, cue={status}, "
                + $"goal={ActorId(source.Memory?.GoalEnemy?.Person)}, seen={Value(Read(enemy, "Seen"))}, heard={Value(Read(enemy, "Heard"))}, "
                + $"visible={Value(Read(enemy, "IsVisible"))}, known={Value(Read(enemy, "EnemyKnown"))}, heardAge={Value(Read(enemy, "TimeSinceHeard"))}, "
                + $"sainGoal={sainGoal}, goalMatchesTarget={goalMatchesTarget}, goalDistance={goalDistance}m, "
                + $"goalSeen={Value(Read(goalEnemy, "Seen"))}, goalHeard={Value(Read(goalEnemy, "Heard"))}, "
                + $"goalVisible={Value(Read(goalEnemy, "IsVisible"))}, goalKnown={Value(Read(goalEnemy, "EnemyKnown"))}, goalHeardAge={Value(Read(goalEnemy, "TimeSinceHeard"))}, "
                + $"searchDelay={Value(Read(Read(component, "Info"), "TimeBeforeSearch"))}, combat={Value(combat)}, "
                + $"shallSearch={Value(shallSearch)}, searchReason={Value(Read(reason, "NotSearchReason"))}, pathReason={Value(Read(reason, "PathCalcFailReason"))}, "
                + $"want={Value(Read(want, "WantToSearchReason"))}, notWant={Value(Read(want, "NotWantToSearchReason"))}, "
                + $"cantStart={Value(Read(want, "CantStartReason"))}, mover={Value(Read(Read(source, "Mover"), "IsMoving"))}, "
                + $"standby={Value(Read(Read(source, "StandBy"), "StandByType"))}, moved={moved}m/{elapsed}s.");
            ObserveInvalidPath(observation, source, position, component, goalEnemy, goalPlayer, reason, now);
        }
        catch (Exception ex) { Failure(ex); }
    }

    // Once per physical bot/raid, after two invalid-path samples at the existing
    // 30-second cadence. Different selected goals may expose the same bad origin.
    // Read only cached SAIN state: never sample, calculate, clear or repair a path.
    private static void ObserveInvalidPath(Observation observation, BotOwner source, Vector3 position,
        object component, object goalEnemy, Player goalPlayer, object reason, float now)
    {
        if (observation.PathDetailLogged || goalPlayer == null
            || Value(Read(reason, "NotSearchReason")) != "PathCalcFailed"
            || Value(Read(reason, "PathCalcFailReason")) != "path Invalid") return;
        if (observation.InvalidPathSamples++ == 0) observation.FirstInvalidPathAt = now;
        if (observation.InvalidPathSamples < 2) return;

        var sourceNav = Read(Read(component, "Transform"), "NavData");
        var goalNav = Read(Read(goalEnemy, "EnemyTransform"), "NavData");
        var knownPosition = Read(Read(goalEnemy, "KnownPlaces"), "LastKnownPosition");
        var path = Read(goalEnemy, "Path");
        string sourceDetails = NavigationSnapshot("source", position, sourceNav, now);
        string goalDetails = NavigationSnapshot("goal", goalPlayer.Position, goalNav, now);
        // Optional getters may fail or change ownership. Do not mix endpoints
        // from different selected wrappers; the ordinary snapshot remains intact.
        if (!ReferenceEquals(Read(component, "GoalEnemy"), goalEnemy)) return;
        observation.PathDetailLogged = true;
        Plugin.ModLogger.LogInfo($"Arena TDM path detail: bot={ActorId(source)}, sainGoal={ActorId(goalPlayer)}, "
            + $"invalidSamples={observation.InvalidPathSamples}, sampleWindow={Number(now - observation.FirstInvalidPathAt)}s, "
            + sourceDetails + ", " + goalDetails
            + $", goalKnown={Point(knownPosition)}, goalKnownMinusNav={Delta(knownPosition, Read(goalNav, "Position"))}, "
            + $"pathStatus={Value(Read(path, "PathToEnemyStatus"))}, pathCorners={Value(Read(path, "PathCornerCount"))}, "
            + $"pathLength={Value(Read(path, "PathLength"))}. Cached SAIN state; lastOnAge is not hit age.");
    }

    private static string NavigationSnapshot(string prefix, Vector3 actual, object nav, float now)
    {
        var position = Read(nav, "Position");
        var hit = Read(nav, "LastValidHit");
        var hitPosition = Read(hit, "position");
        var lastOn = Read(nav, "TimeLastOnNavMesh");
        string age = lastOn is float time && time > 0f && time <= now ? Number(now - time) : "n/a";
        // SAIN can retain old hits and reconstruct Position using current XZ
        // with only the sampled Y. Its status/IsOnNavMesh are not fresh probes.
        return $"{prefix}Actual={Point(actual)}, {prefix}Nav={Point(position)}, "
            + $"{prefix}LastHit={Point(hitPosition)}, {prefix}HitValid={Value(Read(hit, "hit"))}, "
            + $"{prefix}Status={Value(Read(nav, "Status"))}, {prefix}OnNav={Value(Read(nav, "IsOnNavMesh"))}, {prefix}LastOnAge={age}, "
            + $"{prefix}ActualMinusNav={Delta(actual, position)}, {prefix}NavMinusHit={Delta(position, hitPosition)}";
    }

    private static string Point(object value) => value is Vector3 point
        ? $"({point.x.ToString("0.00", CultureInfo.InvariantCulture)};{point.y.ToString("0.00", CultureInfo.InvariantCulture)};{point.z.ToString("0.00", CultureInfo.InvariantCulture)})"
        : "n/a";

    private static string Delta(object first, object second) => first is Vector3 a && second is Vector3 b ? Point(a - b) : "n/a";

    private static object Read(object instance, string name) => Read(instance, name, out _);

    private static object Read(object instance, string name, out bool available)
    {
        available = false;
        if (instance == null) return null;
        try
        {
            var key = (instance.GetType(), name);
            if (!Members.TryGetValue(key, out var member))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                member = (MemberInfo)key.Item1.GetProperty(name, flags) ?? key.Item1.GetField(name, flags);
                Members[key] = member;
            }
            object value = member is PropertyInfo property ? property.GetValue(instance)
                : (member as FieldInfo)?.GetValue(instance);
            available = member != null;
            return value;
        }
        catch { return null; }
    }

    private static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    private static string ActorId(IPlayer player)
    {
        object body = player is BotOwner owner ? owner.GetPlayer : player;
        return body == null ? "none" : _actors.GetValue(body, _ => new Actor { Id = ++_nextActorId }).Id.ToString(CultureInfo.InvariantCulture);
    }
    private static string Value(object value) => value is float number ? Number(number) : value?.ToString() ?? "n/a";
    private static void Failure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        Plugin.ModLogger.LogWarning($"Arena TDM search hearing/diagnostics unavailable (native awareness retained; further messages silenced this raid): {(ex.InnerException ?? ex).Message}");
    }
}
