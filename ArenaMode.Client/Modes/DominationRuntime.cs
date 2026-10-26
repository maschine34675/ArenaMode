using ArenaMode.Bots;
using ArenaMode.Pickups;
using ArenaMode.Shared;
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
    private readonly DominationMapLayout _layout;
    private readonly Vector3[] _positions;
    private readonly DominationOccupancy[] _occupancy;
    private readonly DominationObjectivePoint[] _objectives;
    private readonly Hud.DominationPointView[] _views;
    private readonly List<BotOwner> _eligibleBots = new();
    private float _nextTick;
    private string _lastOwnership;
    private bool _disposed, _prepared, _teamPathsVerified;
    internal string StatusLine { get; private set; }

    internal DominationRuntime(TeamDeathmatchMode mode, DominationMatchPolicy rules, GameWorld world)
    {
        _mode = mode; _rules = rules; _world = world;
        _layout = DominationMapLayout.Resolve(world?.LocationId);
        int count = _layout.Points.Count;
        if (rules.Points.Count != count) throw new InvalidOperationException("Domination objective count changed during preparation.");
        _positions = new Vector3[count]; _occupancy = new DominationOccupancy[count];
        _objectives = new DominationObjectivePoint[count]; _views = new Hud.DominationPointView[count];
        StatusLine = string.Join("     |     ", _layout.Points.Select(p => p.Id + "  NEUTRAL"));
    }

    internal void Prepare()
    {
        if (_disposed || _prepared || Areas.ArenaAreaRuntime.CurrentArea != null)
            throw new InvalidOperationException("Domination requires a fresh full-map objective layout.");
        Vector3 anchor;
        if (_layout.Factory)
        {
            var layout = ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("domination-survey"), _world.LocationId, null);
            if (!ArenaPickupGeometry.TryFactoryAnchor(layout, out anchor))
                throw new InvalidOperationException("Domination could not verify Factory's main navigation network.");
        }
        else
        {
            // The existing team-box planner proves routes to this main-court
            // anchor after opening the gates. Never use a closed booth as the
            // objective survey's navigation origin or move an authored point.
            var formation = ArenaTeamBoxCatalog.Find(_world.LocationId)
                ?? throw new InvalidOperationException("Domination team formation unavailable.");
            anchor = new Vector3(formation.Destination.X, formation.Destination.Y, formation.Destination.Z);
        }
        for (int i = 0; i < _layout.Points.Count; i++)
        {
            var point = _layout.Points[i];
            if (!ArenaPickupGeometry.TryPlacePoint(point.Position, anchor, _layout.Factory, out var floor, out var reason, out _)
                || !ConnectedBothWays(floor, anchor))
                throw new InvalidOperationException($"Domination point {point.Id} is unsafe or unreachable: {reason ?? "return path unavailable"}.");
            _positions[i] = floor;
        }
        // No partial placement: every path must pass before a marker is made.
        for (int i = 0; i < _positions.Length; i++)
            _views[i] = new Hud.DominationPointView(_layout.Points[i].Id, _positions[i], Radius);
        _prepared = true;
        Plugin.ModLogger.LogDebug("Domination points verified: " + string.Join("; ", _positions.Select((p, i) => $"{_layout.Points[i].Id}={p:F3}")));
        Diagnostics.ArenaLog.Developer("Domination points verified: " + string.Join("; ", _positions.Select((p, i) => $"{_layout.Points[i].Id}={p:F3}")));
    }

    internal void VerifyTeamPaths()
    {
        if (_disposed || !_prepared) throw new InvalidOperationException("Domination points are not prepared.");
        if (_layout.Factory || _teamPathsVerified) return;
        var formation = ArenaTeamBoxCatalog.Find(_world.LocationId);
        foreach (bool usec in new[] { true, false })
        {
            var entry = formation.BoothAnchor(usec);
            var start = new Vector3(entry.X, entry.Y, entry.Z);
            for (int i = 0; i < _positions.Length; i++)
                if (!ConnectedBothWays(start, _positions[i]))
                    throw new InvalidOperationException($"Domination point {_layout.Points[i].Id} is disconnected from the {(usec ? "USEC" : "BEAR")} team start after gate opening.");
        }
        _teamPathsVerified = true;
        Plugin.ModLogger.LogDebug("Domination objective routes from both opened team boxes verified.");
        Diagnostics.ArenaLog.Developer("Domination objective routes from both opened team boxes verified.");
    }

    private static bool ConnectedBothWays(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out var start, .75f, NavMesh.AllAreas)
            || !NavMesh.SamplePosition(to, out var finish, .75f, NavMesh.AllAreas)
            || Math.Abs(start.position.y - from.y) > .5f || Math.Abs(finish.position.y - to.y) > .5f) return false;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(start.position, finish.position, NavMesh.AllAreas, path)
            && path.status == NavMeshPathStatus.PathComplete
            && NavMesh.CalculatePath(finish.position, start.position, NavMesh.AllAreas, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    // Prepared once against this world's geometry before pickup initialization.
    // Keep the same exclusion through ownership changes and player replacements.
    internal bool AllowsPickup(Vector3 floor) => _prepared && !_disposed
        && ArenaObjectivePickupSpacing.Allows(floor, _positions, Radius, HeightTolerance);

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
        var labels = new string[_positions.Length];
        var owners = new string[_positions.Length];
        for (int i = 0; i < _positions.Length; i++)
        {
            var state = _rules.Points[i];
            string id = _layout.Points[i].Id;
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
            string ids = string.Join("/", _layout.Points.Select(p => p.Id));
            Plugin.ModLogger.LogDebug($"Domination ownership: {ids}={ownership}; USEC {_rules.UsecScore}, BEAR {_rules.BearScore}.");
            Diagnostics.ArenaLog.Developer($"Domination ownership: {ids}={ownership}; USEC {_rules.UsecScore}, BEAR {_rules.BearScore}.");
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
