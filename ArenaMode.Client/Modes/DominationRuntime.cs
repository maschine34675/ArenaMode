using ArenaMode.Bots;
using ArenaMode.Pickups;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Modes;

/// <summary>One exact raid's surveyed volumes, physical occupants and presentation.</summary>
internal sealed class DominationRuntime : IDisposable
{
    internal const float Radius = 4f;
    internal const float HeightTolerance = 1f;
    private readonly TeamDeathmatchMode _mode;
    private readonly DominationMatchPolicy _rules;
    private readonly GameWorld _world;
    private readonly Vector3[] _positions = new Vector3[3];
    private readonly DominationOccupancy[] _occupancy = new DominationOccupancy[3];
    private readonly DominationObjectivePoint[] _objectives = new DominationObjectivePoint[3];
    private readonly Hud.DominationPointView[] _views = new Hud.DominationPointView[3];
    private readonly List<BotOwner> _eligibleBots = new();
    private float _nextTick;
    private string _lastOwnership;
    private bool _disposed;
    internal string StatusLine { get; private set; } = "A  NEUTRAL    |    B  NEUTRAL    |    C  NEUTRAL";

    internal DominationRuntime(TeamDeathmatchMode mode, DominationMatchPolicy rules, GameWorld world)
    { _mode = mode; _rules = rules; _world = world; }

    internal void Prepare()
    {
        if (_world == null || (_world.LocationId != "factory4_day" && _world.LocationId != "factory4_night")
            || Areas.ArenaAreaRuntime.CurrentArea != null)
            throw new InvalidOperationException("Domination prototype requires full Factory day or night.");
        var layout = ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("domination-survey"), _world.LocationId, null);
        if (!ArenaPickupGeometry.TryFactoryAnchor(layout, out var anchor))
            throw new InvalidOperationException("Domination could not verify Factory's main navigation network.");
        var seeds = new[] { new Vector3(-5.89f, .08f, -27.46f), new Vector3(-6.77f, .17f, 10.01f),
            new Vector3(-20.756f, .166f, 22.769f) };
        var path = new NavMeshPath();
        for (int i = 0; i < seeds.Length; i++)
        {
            if (!ArenaPickupGeometry.TryPlacePoint(seeds[i], anchor, true, out var floor, out var reason, out _)
                || !NavMesh.SamplePosition(floor, out var nav, .75f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(nav.position, anchor, NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException($"Domination point {(char)('A' + i)} is unsafe or unreachable: {reason ?? "return path unavailable"}.");
            _positions[i] = floor;
        }
        // No partial placement: all three paths must pass before a marker is made.
        for (int i = 0; i < _positions.Length; i++)
            _views[i] = new Hud.DominationPointView(((char)('A' + i)).ToString(), _positions[i], Radius);
        Plugin.ModLogger.LogDebug("Domination points verified: " + string.Join("; ", _positions.Select((p, i) => $"{(char)('A' + i)}={p:F3}")));
        Diagnostics.ArenaLog.Developer("Domination points verified: " + string.Join("; ", _positions.Select((p, i) => $"{(char)('A' + i)}={p:F3}")));
    }

    internal bool IsNearPoint(Vector3 floor)
    {
        if (_disposed) return false;
        foreach (var position in _positions)
        {
            var delta = floor - position;
            if (Math.Abs(delta.y) <= HeightTolerance && delta.x * delta.x + delta.z * delta.z <= Radius * Radius)
                return true;
        }
        return false;
    }

    internal void Tick(Player local, IReadOnlyList<BotOwner> bots, bool localUsec, float now)
    {
        if (_disposed || !_mode.IsSearchActive) return;
        // Scored combat callbacks also enter here: the occupancy throttle must
        // never let a hit at the exact deadline pass the terminal admission gate.
        bool deadlineReached = _rules.TimeLimitSeconds > 0 && now >= _rules.StartedAt + _rules.TimeLimitSeconds;
        if (now < _nextTick && !deadlineReached)
        {
            for (int i = 0; i < _views.Length; i++) _views[i].Tick(_rules.Points[i]);
            return;
        }
        _nextTick = now + .2f;
        _eligibleBots.Clear();
        foreach (var bot in bots)
            if (bot != null && _mode.TryClassifyCurrentBody(bot.GetPlayer, out _)) _eligibleBots.Add(bot);
        for (int point = 0; point < _positions.Length; point++)
        {
            int usec = 0, bear = 0;
            Count(local);
            foreach (var bot in _eligibleBots) Count(bot.GetPlayer);
            _occupancy[point] = new DominationOccupancy(usec, bear);

            void Count(Player player)
            {
                if (!_mode.TryClassifyCurrentBody(player, out bool friendly)) return;
                Vector3 delta = player.Position - _positions[point];
                if (Math.Abs(delta.y) > HeightTolerance || delta.x * delta.x + delta.z * delta.z > Radius * Radius
                    || !ArenaPickupGeometry.LineClear(_positions[point] + Vector3.up * .8f, player.Position + Vector3.up * .8f)) return;
                if (friendly == localUsec) usec++; else bear++;
            }
        }
        _rules.Advance(now, _occupancy, localUsec);
        var labels = new string[3];
        var owners = new string[3];
        for (int i = 0; i < 3; i++)
        {
            var state = _rules.Points[i];
            string id = ((char)('A' + i)).ToString();
            _objectives[i] = new DominationObjectivePoint(id, _positions[i], state.OwnerUsec, state.Contested);
            _views[i].Tick(state);
            labels[i] = Hud.DominationPointView.Describe(id, state, true);
            owners[i] = state.OwnerUsec.HasValue ? (state.OwnerUsec.Value ? "USEC" : "BEAR") : "neutral";
        }
        StatusLine = string.Join("     |     ", labels);
        string ownership = string.Join("/", owners);
        if (_lastOwnership != ownership)
        {
            _lastOwnership = ownership;
            Plugin.ModLogger.LogDebug($"Domination ownership: A/B/C={ownership}; USEC {_rules.UsecScore}, BEAR {_rules.BearScore}.");
            Diagnostics.ArenaLog.Developer($"Domination ownership: A/B/C={ownership}; USEC {_rules.UsecScore}, BEAR {_rules.BearScore}.");
        }
        DominationObjectiveBridge.Update(_objectives, _eligibleBots, _rules.Phase == TdmMatchPhase.Live);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var view in _views) view?.Dispose();
        _eligibleBots.Clear();
    }
}
