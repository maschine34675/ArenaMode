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
    private static readonly System.Random LayoutRandom = new();
    private static Vector3[] _previousFactory;
    private readonly TeamDeathmatchMode _mode;
    private readonly DominationMatchPolicy _rules;
    private readonly GameWorld _world;
    private readonly DominationMapLayout _layout;
    private readonly string _areaId;
    private readonly int[] _candidateUsed;
    private readonly Vector3[] _positions;
    private readonly DominationOccupancy[] _occupancy;
    private readonly DominationObjectivePoint[] _objectives;
    private readonly Hud.DominationPointView[] _views;
    private readonly List<BotOwner> _eligibleBots = new();
    private Compat.DynamicMapsDominationMarkers _mapMarkers;
    private float _nextTick;
    private string _lastOwnership;
    private bool _disposed, _prepared, _teamPathsVerified;
    internal string StatusLine { get; private set; }

    internal DominationRuntime(TeamDeathmatchMode mode, DominationMatchPolicy rules, GameWorld world)
        : this(mode, rules, world, ActiveAreaId, DominationMapLayout.ResolveDomination(world?.LocationId, ActiveAreaId)) { }

    // The layout is resolved for one (map, area) key; the seam also lets a harness run a synthetic
    // layout inside a synthetic bounded area.
    internal DominationRuntime(TeamDeathmatchMode mode, DominationMatchPolicy rules, GameWorld world,
        string areaId, DominationMapLayout layout)
    {
        _mode = mode; _rules = rules; _world = world;
        _areaId = areaId;
        _layout = layout ?? throw new InvalidOperationException("No fixed Domination layout exists for this map and area.");
        int count = _layout.Points.Count;
        if (rules.Points.Count != count) throw new InvalidOperationException("Domination objective count changed during preparation.");
        _candidateUsed = new int[count];
        _positions = new Vector3[count]; _occupancy = new DominationOccupancy[count];
        _objectives = new DominationObjectivePoint[count]; _views = new Hud.DominationPointView[count];
        StatusLine = string.Join("     |     ", _layout.Points.Select(p => p.Id + "  NEUTRAL"));
    }

    /// <summary>The bounded area this raid plays in, null on a whole map; the second half of the layout key.</summary>
    internal static string ActiveAreaId => Areas.ArenaAreaRuntime.CurrentArea?.Id;

    /// <summary>The objective count the match policy is built with: the (map, active area) layout's slots.</summary>
    internal static int PointCount(GameWorld world)
        => DominationMapLayout.ResolveDomination(world?.LocationId, ActiveAreaId).Points.Count;

    internal void Prepare()
    {
        // The layout was resolved for one (map, area) when this runtime was built; an area that
        // appeared or changed since must not borrow it.
        if (_disposed || _prepared || !string.Equals(_areaId, ActiveAreaId, StringComparison.Ordinal))
            throw new InvalidOperationException("Domination requires a fresh objective layout for the active map and area.");
        Vector3 anchor;
        if (_layout.Factory)
        {
            var layout = ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("domination-survey"), _world.LocationId, null);
            if (!ArenaPickupGeometry.TryFactoryAnchor(layout, out anchor))
                throw new InvalidOperationException("Domination could not verify Factory's main navigation network.");
        }
        else if (_layout.Surveyed)
        {
            // The layout's own seeds are the network, not the local player (who may stand on an
            // island or not have landed) and not a team-box catalogue (vanilla maps have none).
            if (!ArenaPickupGeometry.TryPoolAnchor(_layout.NavigationSeeds, out anchor, out var anchorReason))
                throw new InvalidOperationException(
                    $"Domination could not verify the main navigation network of {_world.LocationId}: {anchorReason}.");
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
        if (_layout.Factory)
        {
            var candidates = ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("domination-survey"), _world.LocationId, null)
                .Points.SelectMany(p => new[] { new Vector3(p.X, p.Y, p.Z) }
                    .Concat((p.Alternatives ?? new List<ArenaPickupPosition>()).Select(a => new Vector3(a.X, a.Y, a.Z))))
                .GroupBy(p => (p.x, p.y, p.z)).Select(g => g.First()).ToArray();
            if (candidates.Length > 8) throw new InvalidOperationException("Factory objective pool exceeds its bounded survey.");
            var floors = new List<Vector3>();
            foreach (var candidate in candidates)
                if (ArenaPickupGeometry.TryPlacePoint(candidate, anchor, true, out var floor, out _, out _)
                    && ConnectedBothWays(floor, anchor)
                    && !floors.Any(p => Math.Abs(p.x - floor.x) < .5f && Math.Abs(p.y - floor.y) < .5f && Math.Abs(p.z - floor.z) < .5f))
                    floors.Add(floor);
            var selected = DominationMapLayout.SelectFactory(floors, LayoutRandom, _previousFactory);
            Array.Copy(selected, _positions, selected.Length);
        }
        else if (_layout.Surveyed) PlaceSurveyed(anchor);
        else for (int i = 0; i < _layout.Points.Count; i++)
        {
            var point = _layout.Points[i];
            if (!ArenaPickupGeometry.TryPlacePoint(point.Position, anchor, _layout.Factory, out var floor, out var reason, out _)
                || !ConnectedBothWays(floor, anchor))
                throw new InvalidOperationException($"Domination point {point.Id} is unsafe or unreachable: {reason ?? "return path unavailable"}.");
            _positions[i] = floor;
        }
        // No partial placement: every path must pass before a marker is made.
        for (int i = 0; i < _positions.Length; i++)
            _views[i] = new Hud.DominationPointView(_layout.Points[i].Id, _positions[i], Radius,
                !ArenaMapCatalog.IsOptionalMap(_world.LocationId));
        if (_layout.Factory) _previousFactory = (Vector3[])_positions.Clone();
        _prepared = true;
        _mapMarkers = new Compat.DynamicMapsDominationMarkers(_world.LocationId,
            () => !_disposed && _mode.ObjectiveHudVisible && _mode.IsSearchActive);
        string verified = "Domination points verified: " + string.Join("; ", _positions.Select((p, i) => $"{_layout.Points[i].Id}={p:F3}"))
            + AlternatesUsedNote();
        Plugin.ModLogger.LogDebug(verified);
        Diagnostics.ArenaLog.Developer(verified);
    }

    // Surveyed slots in order: the first candidate (primary, then the alternates) that passes the
    // native floor and path proof from the anchor, is connected both ways and keeps the layout's
    // minimum horizontal separation from the floors already chosen. Deterministic for one scene.
    // A slot without a passing candidate aborts the match: no partial layout, no loosened proof.
    private void PlaceSurveyed(Vector3 anchor)
    {
        float separation = DominationMapLayout.SurveyedMinimumSeparation;
        for (int i = 0; i < _layout.Points.Count; i++)
        {
            var slot = _layout.Points[i];
            string refusal = null;
            int tried = 0, used = -1;
            Vector3 chosen = default;
            foreach (var candidate in slot.Candidates)
            {
                int index = tried++;
                if (!ArenaPickupGeometry.TryPlacePoint(candidate, anchor, true, out var floor, out var reason, out _))
                { refusal = reason; continue; }
                if (!ConnectedBothWays(floor, anchor)) { refusal = "return path unavailable"; continue; }
                int near = -1;
                for (int earlier = 0; earlier < i && near < 0; earlier++)
                {
                    float dx = _positions[earlier].x - floor.x, dz = _positions[earlier].z - floor.z;
                    if (dx * dx + dz * dz < separation * separation) near = earlier;
                }
                if (near >= 0) { refusal = $"closer than {separation:0} m to point {_layout.Points[near].Id}"; continue; }
                chosen = floor; used = index;
                break;
            }
            if (used < 0)
                throw new InvalidOperationException(
                    $"Domination point {slot.Id} is unsafe or unreachable: {refusal ?? "no candidate"} ({tried} candidate(s) tried).");
            _positions[i] = chosen;
            _candidateUsed[i] = used;
        }
    }

    private string AlternatesUsedNote()
    {
        if (!_layout.Surveyed || _candidateUsed.All(c => c == 0)) return "";
        return "; alternates used: " + string.Join(", ", _layout.Points
            .Select((p, i) => (p.Id, Index: _candidateUsed[i])).Where(c => c.Index != 0).Select(c => $"{c.Id}=#{c.Index}"));
    }

    internal void VerifyTeamPaths()
    {
        if (_disposed || !_prepared) throw new InvalidOperationException("Domination points are not prepared.");
        if (_layout.Factory || _teamPathsVerified) return;
        if (_layout.Surveyed) { VerifySurveyedTeamPaths(); return; }
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

    // A surveyed layout on a whole map has no team boxes, no gates and no faction starts (both
    // factions draw from the same native zones), so there is nothing to prove and it returns like
    // Factory. Inside a bounded area with team starts the area runtime has already proved the starts
    // themselves; every point must be connected both ways with at least one start of each faction.
    private void VerifySurveyedTeamPaths()
    {
        if (Areas.ArenaAreaRuntime.CurrentArea == null) return;
        for (int side = 0; side < 2; side++)
        {
            bool usec = side == 0;
            var starts = Areas.ArenaAreaRuntime.TeamStartPositions(usec);
            for (int i = 0; i < _positions.Length; i++)
            {
                Vector3 point = _positions[i];
                if (!starts.Any(start => ConnectedBothWays(start, point)))
                    throw new InvalidOperationException($"Domination point {_layout.Points[i].Id} is disconnected from the {(usec ? "USEC" : "BEAR")} team starts of the area.");
            }
        }
        _teamPathsVerified = true;
        Plugin.ModLogger.LogDebug("Domination objective routes from both area team starts verified.");
        Diagnostics.ArenaLog.Developer("Domination objective routes from both area team starts verified.");
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
        // Advance may have ended the match on this very sample. The adapter checks
        // the post-Advance phase, so the result hold cannot retain live objectives.
        _mapMarkers?.Update(_objectives);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mapMarkers?.Dispose();
        _mapMarkers = null;
        foreach (var view in _views) view?.Dispose();
        _eligibleBots.Clear();
    }
}
