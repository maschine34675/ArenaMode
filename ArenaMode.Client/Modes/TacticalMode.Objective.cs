using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Pickups;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Modes;

/// <summary>
/// The Tactical objective package (docs/reviews/TACTICAL-OBJECTIVE-20261108.md): one capture
/// point per round, opening lanes for the enemy squad through the shared objective bridge, a
/// visible zone and the door reset at the round start. Everything here is per round and torn
/// down at every round end and teardown; the round clock, the elimination rules, the timer and
/// the PitFireTeam lifecycle stay where they were. The enemy squad is the only driven side;
/// PitFireTeam followers are never given objectives, and in the team arena the squad leaves
/// the objective the moment the local PMC is down (the post-death pursuit owns it then).
/// </summary>
public sealed partial class TacticalMode
{
    private const float ObjectiveRadius = 4f;
    private const float ObjectiveHeightTolerance = 1f;
    private const float LaneSampleRadius = 4f;
    private static readonly System.Random ObjectiveRandom = new();
    // Avoid the same point twice in a row within a process, like Domination's Factory triple.
    private static Vector3? _previousObjectivePoint;

    private bool _objectiveEnabled, _objectiveUnavailable, _objectiveViewFailed, _objectiveBridge, _objectiveClockWarned;
    private TacticalObjectivePolicy _objective;
    private Vector3? _objectivePoint;
    private Hud.TacticalObjectiveView _objectiveView;
    private readonly List<Vector3> _objectiveLanes = new();
    private readonly List<DominationObjectivePoint> _objectivePublished = new();
    private readonly List<BotOwner> _objectiveBots = new();

    // IArenaObjectiveHost: the bridge asks the mode. Live only while a round is live, combat
    // counts, something is published and the squad is wanted on an objective.
    public bool ObjectivesLive => _objectiveBridge && _phase == Phase.Live && !IsComplete
        && _objectivePublished.Count > 0 && (_teamArena || _soloCombatReady) && ObjectiveBotsWanted;

    public int ObjectiveCount => _objectivePublished.Count;

    /// <summary>Only the living enemy squad of the current round; one team for the bridge's spreading.</summary>
    public bool TryClassifyObjectiveBody(Player body, out bool teamA)
    {
        teamA = true;
        if (body == null || !body.IsAI || body.HealthController?.IsAlive != true || _phase != Phase.Live) return false;
        var owner = body.AIData?.BotOwner;
        if (owner == null || !ReferenceEquals(owner.GetPlayer, body) || string.IsNullOrEmpty(body.ProfileId)) return false;
        if (_teamArena)
        {
            for (int i = 0; i < _boundTeamEnemies.Length && i < _boundTeamEnemyProfileIds.Length; i++)
                if (ReferenceEquals(owner, _boundTeamEnemies[i])
                    && string.Equals(body.ProfileId, _boundTeamEnemyProfileIds[i], StringComparison.Ordinal)) return true;
            return false;
        }
        foreach (var bot in _squad) if (ReferenceEquals(bot, owner)) return true;
        return false;
    }

    /// <summary>The team arena hands the squad to the post-death pursuit once the PMC is down.</summary>
    private bool ObjectiveBotsWanted => !_teamArena || (!_playerDown && _boundLocalPlayer?.HealthController?.IsAlive == true);

    /// <summary>The HUD tail under the score: the point's state, or why there is none.</summary>
    internal string ObjectiveStatusTail
    {
        get
        {
            if (!_objectiveEnabled || _objective == null || _phase != Phase.Live) return string.Empty;
            if (_objectiveUnavailable) return "   no capture point here";
            if (!_objective.Started) return string.Empty;
            string text = _objective.Describe(Time.time);
            return string.IsNullOrEmpty(text) ? string.Empty : "   " + text;
        }
    }

    private void InitializeObjective()
    {
        EndObjectiveRound();
        _objectiveUnavailable = false;
        _objectiveClockWarned = false;
        _objectiveEnabled = ArenaSession.TacticalObjectiveSelection;
        _objective = _objectiveEnabled ? NewObjectivePolicy() : null;
        if (_objectiveEnabled)
            Plugin.ModLogger.LogDebug($"Tactical objective: capture point after {_objective.AppearAfterSeconds:0}s of combat, "
                + $"hold {_objective.HoldSeconds:0}s (both read again at every round start); "
                + $"bot objective layer {(DominationObjectiveBridge.Available ? "available" : "absent")}.");
    }

    /// <summary>The F12 numbers as they are now: a live change applies from the next round.</summary>
    private static TacticalObjectivePolicy NewObjectivePolicy()
        => new(Settings.TacticalObjectiveAfterSeconds?.Value ?? 60, Settings.TacticalObjectiveHoldSeconds?.Value ?? 10);

    /// <summary>
    /// Combat counts from here (solo: the squad is ready; team: the go-live block): place the
    /// point, start its clock, compute the opening lanes and open the bot objective bridge.
    /// </summary>
    private void BeginObjectiveRound()
    {
        EndObjectiveRound();
        if (!_objectiveEnabled || _objective == null || _phase != Phase.Live) return;
        _objective = NewObjectivePolicy();
        float roundLimit = Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
        if (!_objectiveClockWarned && _objective.AppearAfterSeconds + _objective.HoldSeconds > roundLimit)
        {
            _objectiveClockWarned = true;
            Plugin.ModLogger.LogWarning($"Tactical objective: the point appears after {_objective.AppearAfterSeconds:0}s and needs a "
                + $"{_objective.HoldSeconds:0}s hold, but the round timer ends after {roundLimit:0}s: no hold can complete before "
                + "the timer (Tactical/ObjectiveAfterSeconds, ObjectiveHoldSeconds, RoundSeconds).");
        }
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (world == null || game == null) return;
        if (DominationMapLayout.ControlPointCount(world.LocationId) == 0 || Areas.ArenaAreaRuntime.CurrentArea != null)
        {
            if (!_objectiveUnavailable)
                Plugin.ModLogger.LogDebug("Tactical objective: no capture point on this map or inside an area; rounds play by elimination.");
            _objectiveUnavailable = true;
            return;
        }
        _objectivePoint = TryPlaceObjectivePoint(world);
        if (!_objectivePoint.HasValue) return;
        _objective.Begin(Time.time);
        ComputeObjectiveLanes(world);
        if (DominationObjectiveBridge.Available)
        {
            try { DominationObjectiveBridge.Begin(_match, game, world); _objectiveBridge = true; }
            catch (Exception ex) { Plugin.ModLogger.LogWarning("Tactical objective: bot objective layer unavailable, the squad keeps native pacing: " + ex.Message); }
        }
        Plugin.ModLogger.LogDebug($"Tactical round {RoundNumber} objective: point at {_objectivePoint.Value:F1}, "
            + $"{_objectiveLanes.Count} opening lane(s), bots {(_objectiveBridge ? "on the objective layer" : "native")}.");
    }

    /// <summary>
    /// One random scene-verified point of the map's Domination layout: Factory through the survey
    /// seeds and its proven navigation anchor, the ported maps through their authored anchors and
    /// the team-box destination. The same native floor and path checks as Domination; a refused
    /// point is a Warning and a round without a point, never an aborted match.
    /// </summary>
    private Vector3? TryPlaceObjectivePoint(GameWorld world)
    {
        try
        {
            var layout = DominationMapLayout.Resolve(world.LocationId);
            var candidates = new List<Vector3>();
            Vector3 anchor;
            if (layout.Factory)
            {
                var survey = ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("domination-survey"), world.LocationId, null);
                if (!ArenaPickupGeometry.TryFactoryAnchor(survey, out anchor))
                    throw new InvalidOperationException("Factory's main navigation network could not be verified");
                foreach (var seed in survey.Points.SelectMany(p => new[] { new Vector3(p.X, p.Y, p.Z) }
                    .Concat((p.Alternatives ?? new List<ArenaPickupPosition>()).Select(a => new Vector3(a.X, a.Y, a.Z)))))
                    if (ArenaPickupGeometry.TryPlacePoint(seed, anchor, true, out var floor, out _, out _)
                        && ObjectiveConnectedBothWays(floor, anchor)
                        && !candidates.Any(c => (c - floor).sqrMagnitude < .25f)) candidates.Add(floor);
            }
            else
            {
                var formation = ArenaTeamBoxCatalog.Find(world.LocationId)
                    ?? throw new InvalidOperationException("the map has no team formation for the navigation anchor");
                anchor = new Vector3(formation.Destination.X, formation.Destination.Y, formation.Destination.Z);
                foreach (var point in layout.Points)
                    if (ArenaPickupGeometry.TryPlacePoint(point.Position, anchor, false, out var floor, out _, out _)
                        && ObjectiveConnectedBothWays(floor, anchor)) candidates.Add(floor);
            }
            if (candidates.Count == 0) throw new InvalidOperationException("no authored point passed the floor and path checks");
            if (candidates.Count > 1 && _previousObjectivePoint.HasValue)
                candidates.RemoveAll(c => (c - _previousObjectivePoint.Value).sqrMagnitude < .25f);
            var chosen = candidates[ObjectiveRandom.Next(candidates.Count)];
            _previousObjectivePoint = chosen;
            return chosen;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Tactical round {RoundNumber}: capture point refused, the round plays by elimination: {ex.Message}");
            return null;
        }
    }

    private static bool ObjectiveConnectedBothWays(Vector3 from, Vector3 to)
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

    private IEnumerable<BotOwner> ObjectiveBots()
    {
        IReadOnlyList<BotOwner> source = _teamArena ? (IReadOnlyList<BotOwner>)_boundTeamEnemies : _squad;
        foreach (var bot in source)
            if (bot != null && bot.GetPlayer != null && bot.HealthController?.IsAlive == true) yield return bot;
    }

    private IEnumerable<Player> ObjectivePlayerSide(GameWorld world)
    {
        if (_teamArena)
        {
            bool localAlive = false;
            try { if (!TryReadBoundLocalPlayer(out localAlive, out _)) localAlive = false; }
            catch (Exception) { localAlive = false; }
            if (localAlive && _boundLocalPlayer != null) yield return _boundLocalPlayer;
            foreach (var follower in PitFireTeamCompat.GetLiveFollowers())
                if (follower?.GetPlayer != null && follower.HealthController?.IsAlive == true) yield return follower.GetPlayer;
            yield break;
        }
        var local = world?.MainPlayer;
        if (local != null && !local.IsAI && local.HealthController?.IsAlive == true) yield return local;
    }

    private static TacticalLanePoint? ObjectiveCentroid(IEnumerable<Vector3> positions)
    {
        int count = 0;
        Vector3 sum = Vector3.zero;
        foreach (var position in positions) { sum += position; count++; }
        if (count == 0) return null;
        var centre = sum / count;
        return new TacticalLanePoint(centre.x, centre.y, centre.z);
    }

    private void ComputeObjectiveLanes(GameWorld world)
    {
        _objectiveLanes.Clear();
        var bots = ObjectiveBots().ToList();
        var squad = ObjectiveCentroid(bots.Select(bot => bot.GetPlayer.Position));
        var players = ObjectiveCentroid(ObjectivePlayerSide(world).Select(player => player.Position));
        foreach (var lane in TacticalObjectiveLanes.Compute(squad, players, bots.Count))
        {
            var probe = new Vector3(lane.X, lane.Y, lane.Z);
            if (NavMesh.SamplePosition(probe, out var hit, LaneSampleRadius, NavMesh.AllAreas)) _objectiveLanes.Add(hit.position);
        }
    }

    /// <summary>
    /// Per live tick after the elimination checks: occupancy, the capture clock, the bot
    /// objectives and the marker. Returns true when the point ended the round.
    /// </summary>
    private bool TickObjective()
    {
        if (_objective == null || !_objective.Started || _phase != Phase.Live || !_objectivePoint.HasValue) return false;
        if (SquadAlive == 0) return false; // elimination decides first
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (world == null) return false;
        int friendly = 0, enemy = 0;
        foreach (var player in ObjectivePlayerSide(world)) if (OnObjective(player.Position)) friendly++;
        foreach (var bot in ObjectiveBots()) if (OnObjective(bot.GetPlayer.Position)) enemy++;
        var captured = _objective.Advance(Time.time, friendly, enemy);
        PublishObjectiveBots();
        TickObjectiveView();
        if (captured == TacticalObjectiveSide.None) return false;
        bool friendlyWon = captured == TacticalObjectiveSide.Friendly;
        string reason = $"the capture point was held for {_objective.HoldSeconds:0}s by {(friendlyWon ? "the player side" : "the enemy squad")}";
        Plugin.ModLogger.LogDebug($"Tactical round {RoundNumber}: {reason}.");
        ArenaToasts.Show(friendlyWon ? $"Round {RoundNumber} — point held!" : $"Round {RoundNumber} — the enemy held the point");
        if (_teamArena) EndTeamRound(friendlyWon ? TeamArenaRoundResult.Won : TeamArenaRoundResult.Lost, reason);
        else EndRound(won: friendlyWon);
        return true;
    }

    private bool OnObjective(Vector3 position)
    {
        if (!_objectivePoint.HasValue) return false;
        var delta = position - _objectivePoint.Value;
        if (Math.Abs(delta.y) > ObjectiveHeightTolerance) return false;
        delta.y = 0f;
        return delta.sqrMagnitude <= ObjectiveRadius * ObjectiveRadius
            && ArenaPickupGeometry.LineClear(_objectivePoint.Value + Vector3.up * .8f, position + Vector3.up * .8f);
    }

    /// <summary>Lanes until the point is up, then the point; nothing while the squad is not wanted.</summary>
    private void PublishObjectiveBots()
    {
        if (!_objectiveBridge) return;
        _objectivePublished.Clear();
        _objectiveBots.Clear();
        bool live = _phase == Phase.Live && (_teamArena || _soloCombatReady) && ObjectiveBotsWanted;
        if (live && _objectivePoint.HasValue)
        {
            if (_objective.Active)
            {
                bool? owner = _objective.Holder == TacticalObjectiveSide.Enemy ? true
                    : _objective.Holder == TacticalObjectiveSide.Friendly ? false : (bool?)null;
                _objectivePublished.Add(new DominationObjectivePoint("point", _objectivePoint.Value, owner, _objective.Contested));
            }
            else
                for (int i = 0; i < _objectiveLanes.Count; i++)
                    _objectivePublished.Add(new DominationObjectivePoint("lane-" + i, _objectiveLanes[i], null, false));
            foreach (var bot in ObjectiveBots()) _objectiveBots.Add(bot);
        }
        try { DominationObjectiveBridge.Update(_objectivePublished, _objectiveBots, live && _objectivePublished.Count > 0); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Tactical objective: bot objective update failed: " + ex.Message); }
    }

    private void TickObjectiveView()
    {
        if (!_objective.Active || !_objectivePoint.HasValue || _objectiveViewFailed) return;
        if (_objectiveView == null)
        {
            try
            {
                _objectiveView = new Hud.TacticalObjectiveView(_objectivePoint.Value, ObjectiveRadius);
                Plugin.ModLogger.LogDebug($"Tactical round {RoundNumber}: the capture point is up.");
            }
            catch (Exception ex)
            {
                _objectiveViewFailed = true;
                Plugin.ModLogger.LogWarning("Tactical objective: point marker unavailable, the point stays invisible: " + ex.Message);
                return;
            }
        }
        _objectiveView.Tick(_objective.Holder, _objective.Contested, _objective.Progress);
    }

    /// <summary>Every round end and teardown: no lease, no marker and no clock may outlive the round.</summary>
    private void EndObjectiveRound()
    {
        _objective?.Reset();
        _objectivePoint = null;
        _objectiveLanes.Clear();
        _objectivePublished.Clear();
        _objectiveBots.Clear();
        _objectiveViewFailed = false;
        if (_objectiveBridge)
        {
            _objectiveBridge = false;
            try { DominationObjectiveBridge.Stop(); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug("Tactical objective: bridge stop failed: " + ex.Message); }
        }
        if (_objectiveView != null)
        {
            try { _objectiveView.Dispose(); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug("Tactical objective: marker dispose failed: " + ex.Message); }
            _objectiveView = null;
        }
    }

    /// <summary>The arena as the first round found it: every open, unlocked, intact door shut.</summary>
    private void ResetDoorsForRound()
    {
        if (Settings.TacticalCloseDoorsAtRoundStart?.Value == false || !ArenaSession.IsActive) return;
        int closed = ArenaRoundDoorReset.CloseOpenDoors(out int skipped, out int seen);
        // Always one Debug line: a round that shut nothing is as much a result as one that did.
        Plugin.ModLogger.LogDebug($"Tactical round {RoundNumber}: door reset saw {seen} door(s), shut {closed}, skipped {skipped}.");
    }
}
