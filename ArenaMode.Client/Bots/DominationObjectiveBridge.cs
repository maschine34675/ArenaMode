using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>Scene-validated point snapshot. Ownership is USEC=true, BEAR=false, neutral=null.</summary>
public readonly struct DominationObjectivePoint
{
    public DominationObjectivePoint(string id, Vector3 position, bool? ownerUsec, bool contested)
    { Id = id; Position = position; OwnerUsec = ownerUsec; Contested = contested; }
    public string Id { get; }
    public Vector3 Position { get; }
    public bool? OwnerUsec { get; }
    public bool Contested { get; }
}

/// <summary>A stable physical-body assignment, independent of an interrupted native movement action.</summary>
public sealed class DominationObjectiveLease
{
    internal DominationObjectiveLease(BotOwner bot, DominationObjectivePoint point, bool usec, int generation, float now)
    {
        Bot = bot; Body = bot.GetPlayer; Mover = bot.Mover; Group = bot.BotsGroup;
        ProfileId = bot.ProfileId; PointId = point.Id; Center = point.Position;
        Usec = usec; Generation = generation; AssignedAt = now;
        // Stable per-body/profile/point offsets avoid every bot seeking the same center.
        uint seed = 2166136261;
        foreach (char c in ProfileId + ":" + PointId) seed = (seed ^ c) * 16777619;
        float angle = (seed % 360) * Mathf.Deg2Rad;
        float radius = 1.1f + (seed % 7) * .15f;
        Destination = Center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
    }
    public BotOwner Bot { get; }
    public Player Body { get; }
    internal BotMover Mover { get; }
    internal object Group { get; }
    internal string ProfileId { get; }
    public string PointId { get; }
    public Vector3 Center { get; }
    public Vector3 Destination { get; }
    public const int ApproachCount = 13;
    public Vector3 Approach(int index)
    {
        if (index < 0 || index >= ApproachCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == ApproachCount - 1) return Center;
        var offset = Destination - Center;
        // Try the preferred body-specific direction, then the rest of a bounded
        // ring and a shorter inner ring. Only the companion may authorize a route.
        double angle = index < 8 ? index * Math.PI / 4d : (index - 8) * Math.PI / 2d;
        float scale = index < 8 ? 1f : .45f;
        return Center + new Vector3((float)(offset.x * Math.Cos(angle) - offset.z * Math.Sin(angle)) * scale,
            0f, (float)(offset.x * Math.Sin(angle) + offset.z * Math.Cos(angle)) * scale);
    }
    public bool Usec { get; }
    public int Generation { get; }
    internal float AssignedAt { get; }
    public bool Current => DominationObjectiveBridge.IsCurrent(this);
    public bool Inside(Vector3 position)
    {
        Vector3 delta = position - Center;
        return delta.x * delta.x + delta.z * delta.z <= 16f && Math.Abs(delta.y) <= 1f
            && ArenaPickupGeometry.LineClear(Center + Vector3.up * .8f, position + Vector3.up * .8f);
    }
}

/// <summary>Exact-match coordinate authority; contains no optional BigBrain types or inventory mutation.</summary>
public static class DominationObjectiveBridge
{
    private static ArenaMatch _match;
    private static LocalGame _game;
    private static GameWorld _world;
    private static int _generation;
    private static bool _available, _live;
    private static float _updatedAt, _nextPlan;
    private static readonly List<DominationObjectivePoint> Points = new();
    private static readonly Dictionary<BotOwner, DominationObjectiveLease> Assignments = new();
    private static readonly Dictionary<BotOwner, Dictionary<string, float>> Rejected = new();

    public static bool Available
    {
        get => _available;
        set { _available = value; if (!value) Stop(); }
    }

    public static void Begin(ArenaMatch match, LocalGame game, GameWorld world)
    {
        Stop(); _match = match; _game = game; _world = world;
    }

    public static void Stop()
    {
        _generation++; _live = false; _match = null; _game = null; _world = null;
        _updatedAt = _nextPlan = 0; Assignments.Clear(); Rejected.Clear(); Points.Clear();
    }

    private static bool Context()
        => Available && _match != null && _game != null && _world != null
            && ReferenceEquals(ArenaMatchService.Current, _match)
            && _match.Mode is TeamDeathmatchMode mode && mode.IsDomination && mode.IsSearchActive
            && !_match.Mode.IsComplete && _game.Status == GameStatus.Started
            && Singleton<AbstractGame>.Instantiated && Singleton<GameWorld>.Instantiated
            && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
            && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            && ReferenceEquals(_game.GameWorld, _world) && ArenaSession.IsExactActiveRaid(_game, _world);

    private static bool ValidBot(BotOwner bot)
    {
        try
        {
            var body = bot?.GetPlayer;
            return body != null && body.IsAI && !body.IsYourPlayer && body.HealthController?.IsAlive == true
            && bot.BotState == EBotState.Active && !bot.IsDead && bot.Mover != null && bot.BotsGroup != null
            && ReferenceEquals(body.AIData?.BotOwner, bot) && !string.IsNullOrEmpty(body.ProfileId)
            && ReferenceEquals(_world.GetAlivePlayerByProfileID(body.ProfileId), body)
            && ReferenceEquals(_world.GetEverExistedPlayerByID(body.ProfileId), body)
                && _match.Mode is TeamDeathmatchMode mode && mode.TryClassifyCurrentBody(body, out _)
                && body.Side is EPlayerSide.Usec or EPlayerSide.Bear;
        }
        catch { return false; }
    }

    public static void Update(IReadOnlyList<DominationObjectivePoint> points, IReadOnlyList<BotOwner> bots, bool live)
    {
        if (!live || !Context() || points == null || _match.Mode is not TeamDeathmatchMode mode
            || points.Count != mode.ObjectiveCount || points.Count < 1 || points.Count > 16 || bots == null)
        { _live = false; Assignments.Clear(); Rejected.Clear(); Points.Clear(); return; }
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (string.IsNullOrEmpty(p.Id) || !Finite(p.Position)) { _live = false; Assignments.Clear(); return; }
            for (int j = 0; j < i; j++)
                if (points[j].Id == p.Id) { _live = false; Assignments.Clear(); return; }
        }
        _live = true; _updatedAt = Time.time;
        if (Time.time < _nextPlan) return;
        _nextPlan = Time.time + 1f;
        Points.Clear(); for (int i = 0; i < points.Count; i++) Points.Add(points[i]);
        var current = bots.Where(ValidBot).Distinct().OrderBy(b => b.ProfileId, StringComparer.Ordinal).ToArray();
        foreach (var bot in Assignments.Keys.ToArray())
            if (!current.Any(b => ReferenceEquals(b, bot)) || !IsCurrent(Assignments[bot])) Assignments.Remove(bot);
        foreach (var bot in Rejected.Keys.ToArray())
            if (!current.Any(b => ReferenceEquals(b, bot))) Rejected.Remove(bot);
        foreach (var bot in current) Plan(bot);
    }

    private static void Plan(BotOwner bot)
    {
        Assignments.TryGetValue(bot, out var previous);
        // The assignment survives brief combat/medical handoffs; do not continually retarget moving bots.
        if (previous != null && Time.time - previous.AssignedAt < 8f) return;
        bool usec = bot.GetPlayer.Side == EPlayerSide.Usec;
        float bestScore = float.NegativeInfinity;
        DominationObjectivePoint? best = null;
        foreach (var point in Points)
        {
            if (Rejected.TryGetValue(bot, out var failures) && failures.TryGetValue(point.Id, out float until) && Time.time < until) continue;
            int claimed = Assignments.Values.Count(a => !ReferenceEquals(a.Bot, bot) && a.Usec == usec && a.PointId == point.Id);
            float need = point.OwnerUsec == null ? 24 : point.OwnerUsec != usec ? 28 : point.Contested ? 32 : 12;
            float score = need - 18 * claimed - Mathf.Min(5f, (bot.Position - point.Position).magnitude * .04f);
            if (previous?.PointId == point.Id) score += 5;
            if (score > bestScore) { bestScore = score; best = point; }
        }
        if (!best.HasValue) { Assignments.Remove(bot); return; }
        if (previous != null && previous.PointId == best.Value.Id) return;
        Assignments[bot] = new DominationObjectiveLease(bot, best.Value, usec, _generation, Time.time);
        if (Diagnostics.ArenaLog.DeveloperEnabled)
            Diagnostics.ArenaLog.Developer($"Arena Domination objective assigned: bot={bot.ProfileId}; team={(usec ? "USEC" : "BEAR")}; point={best.Value.Id}; destination={Assignments[bot].Destination}.");
    }

    public static bool TryGet(BotOwner bot, out DominationObjectiveLease lease)
    {
        lease = null;
        if (bot == null || !Assignments.TryGetValue(bot, out var current) || !IsCurrent(current)) return false;
        lease = current; return true;
    }

    public static bool IsCurrent(DominationObjectiveLease lease)
    {
        try { return lease != null && _live && Context() && Time.time - _updatedAt <= 3f
            && lease.Generation == _generation && ValidBot(lease.Bot)
            && ReferenceEquals(lease.Bot.GetPlayer, lease.Body) && ReferenceEquals(lease.Bot.Mover, lease.Mover)
            && ReferenceEquals(lease.Bot.BotsGroup, lease.Group) && lease.ProfileId == lease.Bot.ProfileId
            && (lease.Body.Side == EPlayerSide.Usec) == lease.Usec
            && Assignments.TryGetValue(lease.Bot, out var current) && ReferenceEquals(current, lease)
            && Points.Any(p => p.Id == lease.PointId && (p.Position - lease.Center).sqrMagnitude < .0001f);
        }
        catch { return false; }
    }

    public static void Reject(DominationObjectiveLease lease)
    {
        if (!IsCurrent(lease)) return;
        if (!Rejected.TryGetValue(lease.Bot, out var failures)) Rejected[lease.Bot] = failures = new(StringComparer.Ordinal);
        failures[lease.PointId] = Time.time + 12f;
        Assignments.Remove(lease.Bot);
    }

    private static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x)
        || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z));
}
