using System;
using System.Collections.Generic;
using System.Linq;
using ArenaMode.Bots;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace ArenaMode.Areas;

internal static partial class ArenaAreaRuntime
{
    private static int _teamSize;
    private static bool _teamStarting;
    private static ISpawnPointsCollection _loadingCollection;
    private static ISpawnPoint _initialTeamPoint;
    private static TeamSpawnLease _playerReservation;
    private static readonly Dictionary<string, TeamSpawnLease> TeamReservations = new Dictionary<string, TeamSpawnLease>();
    internal static int? CurrentTeamSize => HasTeamStarts ? _teamSize : (int?)null;
    // These dynamic faction reservations belong only to independent TDM. All
    // other modes still use the same initial PMC start/connectivity proof, but
    // their own directors determine opponents, followers and wave capacity.
    internal static bool HasTeamStarts => IsCurrentActive() && _area.TeamStarts != null
        && _mode == Modes.ArenaModeCatalog.TeamDeathmatchId;

    internal static bool TryInitialTeamSpawn(SpawnSystem system, ESpawnCategory category,
        EPlayerSide side, string profileId, out ISpawnPoint selected)
    {
        selected = null;
        // PFT's initial-spawn prefix changes the local request from Player to
        // Coop when its authenticated companions are invited. This is still
        // the same one PMC body, not a generic permission for Coop spawns.
        bool localCategory = category == ESpawnCategory.Player
            || category == ESpawnCategory.Coop && Compat.PitFireTeamCompat.HasFrozenTeamRoster
                && (_mode == Modes.ArenaModeCatalog.TacticalId || _mode == Modes.ArenaModeCatalog.ZombieId);
        if (!IsCurrentLoading() || _area.TeamStarts == null || !localCategory
            || profileId != _game.Profile.Id || side != _game.Profile.Info.Side
            || system == null || !ReferenceEquals(system._players, _world)
            || !ReferenceEquals(system._spawnPoints, _loadingCollection)) return false;
        var points = Candidates.Where(p => IsPlayerPoint(p) && !p.SpawnBlocked
            && _area.TeamStarts.Allows(side == EPlayerSide.Usec, true, p.Id)).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("The exact team area has no initial PMC start for " + side);
        selected = points[UnityEngine.Random.Range(0, points.Length)];
        _initialTeamPoint = selected;
        Plugin.ModLogger.LogInfo($"Arena team initial PMC: {side}, {selected.Id}, position {selected.Position}.");
        return true;
    }

    private static bool ValidateTeamStarts(out string problem)
    {
        problem = null;
        if (_initialTeamPoint == null || !Connected.Contains(_initialTeamPoint))
        { problem = "the selected initial team point is not connected"; return false; }
        var zones = (_game as IBotGame)?.BotsController?.BotSpawner?.SpawnZones(false);
        if (zones == null) { problem = "native team spawn zones are unavailable"; return false; }
        var dispatchable = zones.Where(z => z != null && !ArenaBotSpawnFilter.IsExcludedZone(z))
            .SelectMany(z => z.SpawnPoints ?? Enumerable.Empty<ISpawnPoint>())
            .Where(p => p != null && IsBotPoint(p) && !p.SpawnBlocked
                && !ArenaBotSpawnPolicy.IsExcludedZone(_area.MapId, p.BotZoneName)
                && !ArenaBotSpawnPolicy.IsExcludedPoint(_area.MapId, p.Id)).ToArray();
        foreach (bool usec in new[] { true, false })
        {
            var players = Connected.Where(p => !p.SpawnBlocked
                && ArenaAreaSpawnPolicy.IsPlayerPoint(p, usec ? EPlayerSide.Usec : EPlayerSide.Bear)
                && _area.TeamStarts.Allows(usec, true, p.Id)).ToArray();
            var bots = Connected.Where(p => IsBotPoint(p) && !p.SpawnBlocked
                && dispatchable.Any(d => d != null && d.Id == p.Id && (d.Position-p.Position).sqrMagnitude < 0.01f)
                && !ArenaBotSpawnPolicy.IsExcludedPoint(_area.MapId, p.Id)
                && !ArenaBotSpawnPolicy.IsExcludedZone(_area.MapId, p.BotZoneName)
                && _area.TeamStarts.Allows(usec, false, p.Id)).ToArray();
            // Only the local faction places a PMC body. Requiring an unused
            // opponent PMC marker (and an extra allied bot) rejected valid
            // formations before native bot activation could even begin.
            bool localTeam = usec == (_game.Profile.Info.Side == EPlayerSide.Usec);
            int requiredPlayers = localTeam ? 1 : 0;
            int requiredBots = _teamSize - requiredPlayers;
            if (players.Length < requiredPlayers || bots.Length < requiredBots)
            { problem = $"{(usec ? "USEC" : "BEAR")} starts: PMC {players.Length}/{requiredPlayers}, bots {bots.Length}/{requiredBots}"; return false; }
            // Alternatives for the one local PMC are not simultaneous occupants.
            for (int i = 0; i < bots.Length; i++)
            {
                if (players.Any(p => HorizontalSquared(p.Position, bots[i].Position) < 2.25f)
                    || bots.Take(i).Any(p => HorizontalSquared(p.Position, bots[i].Position) < 2.25f))
                { problem = "team start positions overlap"; return false; }
            }
        }
        var west = Connected.Where(p => _area.TeamStarts.Allows(true, true, p.Id)
            || _area.TeamStarts.Allows(true, false, p.Id)).ToArray();
        var east = Connected.Where(p => _area.TeamStarts.Allows(false, true, p.Id)
            || _area.TeamStarts.Allows(false, false, p.Id)).ToArray();
        if (west.Any(w => east.Any(e => HorizontalSquared(w.Position, e.Position) < 625f)))
        { problem = "opposing team starts no longer have 25m separation"; return false; }
        return true;
    }

    internal static void FinishTeamAssembly()
    {
        if (!HasTeamStarts || !_teamStarting) return;
        _teamStarting = false;
        Plugin.ModLogger.LogInfo("Arena team starts released: subsequent placements rank all connected spawns against live enemies.");
    }

    // Only used at a failed preparation deadline. Connectivity diagnostics
    // identify path failures; these explain why a reached marker still cannot
    // participate in the exact native formation (without changing selection).
    private static void LogTeamStartDiagnostics()
    {
        if (_area?.TeamStarts == null || !IsCurrentActive()) return;
        var zones = (_game as IBotGame)?.BotsController?.BotSpawner?.SpawnZones(false);
        var native = (zones ?? Enumerable.Empty<BotZone>())
            .Where(z => z != null && !ArenaBotSpawnFilter.IsExcludedZone(z))
            .SelectMany(z => z.SpawnPoints ?? Enumerable.Empty<ISpawnPoint>()).ToArray();
        foreach (bool usec in new[] { true, false })
        foreach (bool pmc in new[] { true, false })
        {
            var pool = Candidates.Where(p => p != null && _area.TeamStarts.Allows(usec, pmc, p.Id)).ToArray();
            foreach (var point in pool)
            {
                bool category = pmc
                    ? ArenaAreaSpawnPolicy.IsPlayerPoint(point, usec ? EPlayerSide.Usec : EPlayerSide.Bear)
                    : IsBotPoint(point);
                bool dispatch = pmc || native.Any(d => d != null && d.Id == point.Id
                    && (d.Position-point.Position).sqrMagnitude < 0.01f && IsBotPoint(d) && !d.SpawnBlocked
                    && !ArenaBotSpawnPolicy.IsExcludedZone(_area.MapId, d.BotZoneName)
                    && !ArenaBotSpawnPolicy.IsExcludedPoint(_area.MapId, d.Id));
                bool excluded = _area.IsExcludedSpawn(point.Id)
                    || ArenaBotSpawnPolicy.IsExcludedPoint(_area.MapId, point.Id)
                    || (!pmc && ArenaBotSpawnPolicy.IsExcludedZone(_area.MapId, point.BotZoneName));
                Plugin.ModLogger.LogInfo($"Arena team start diagnostic: {(usec ? "USEC" : "BEAR")} {(pmc ? "PMC" : "bot")} "
                    + $"{point.Id} at {point.Position}; connected={Connected.Contains(point)}, blocked={point.SpawnBlocked}, "
                    + $"category/side={category}, native-dispatch={dispatch}, excluded={excluded}.");
            }
            if (pool.Length == 0)
                Plugin.ModLogger.LogInfo($"Arena team start diagnostic: {(usec ? "USEC" : "BEAR")} {(pmc ? "PMC" : "bot")} pool absent from scene candidates.");
        }
    }

    // A reservation spans only native body activation, never the entire match.
    // Exact object/token checks prevent an old callback releasing a newer lease.
    internal sealed class TeamSpawnLease : IDisposable
    {
        internal readonly string Token, PointId;
        internal readonly ISpawnPoint Point;
        internal readonly Vector3 Position;
        internal readonly bool Usec;
        private readonly bool _requireFaction;
        internal readonly float Expires;
        internal TeamSpawnLease(string token, ISpawnPoint point, bool usec, bool requireFaction = true)
        {
            Token = token; Point = point; PointId = point.Id; Position = point.Position;
            Usec = usec; _requireFaction = requireFaction; Expires = Time.time + (requireFaction ? 30f : 120f);
        }
        internal bool IsCurrent => IsCurrentActive() && IsReady && Token == _token && Time.time < Expires
            && Point != null && Point.Id == PointId && (Point.Position - Position).sqrMagnitude < 0.0001f
            && TeamReservations.TryGetValue(PointId, out var current) && ReferenceEquals(current, this);
        internal bool Accept(Player player)
            => IsCurrent && player != null && AllowsSpawn(Point)
               && (!_requireFaction || player.Profile?.Info?.Side == (Usec ? EPlayerSide.Usec : EPlayerSide.Bear))
               && (player.Position - Position).sqrMagnitude <= 4f
               && Contains(player.Position, ArenaAreaCatalog.NavigationInset)
               && TrySample(player.Position, out var sample) && CompleteConnectedPath(sample)
               && IsUnoccupied(player.Position, player, this);
        internal bool CanReleasePlayer => IsCurrent && AllowsSpawn(Point)
            && IsUnoccupied(Position, _world.MainPlayer, this);
        public void Dispose()
        {
            if (TeamReservations.TryGetValue(PointId, out var current) && ReferenceEquals(current, this))
                TeamReservations.Remove(PointId);
        }
    }

    internal static TeamSpawnLease ReservePlayerPosition(Vector3 position)
    {
        if (!IsCurrentActive()) return null;
        if (!IsReady) throw new InvalidOperationException("The team area is not ready for a player reservation.");
        _playerReservation?.Dispose();
        _playerReservation = null;
        var point = Connected.FirstOrDefault(p => IsPlayerPoint(p) && !p.SpawnBlocked
            && (p.Position-position).sqrMagnitude < 0.01f && AllowsSpawn(p));
        if (point == null || !IsUnoccupied(position, _world.MainPlayer, null))
            throw new InvalidOperationException("The selected player spawn is no longer available before handover.");
        var lease = new TeamSpawnLease(_token, point, _game.Profile.Info.Side == EPlayerSide.Usec,
            requireFaction: HasTeamStarts);
        TeamReservations.Add(lease.PointId, lease);
        return _playerReservation = lease;
    }

    internal static void ReleasePlayerReservation(Player player)
    {
        if (!IsCurrentActive() || !ReferenceEquals(player, _world.MainPlayer)) return;
        _playerReservation?.Dispose();
        _playerReservation = null;
    }

    internal static bool TryPlanTeamBot(IEnumerable<BotZone> zones, bool usec, bool reserve,
        out BotZone selectedZone, out ISpawnPoint selectedPoint, out TeamSpawnLease lease)
    {
        selectedZone = null; selectedPoint = null; lease = null;
        if (!HasTeamStarts || !IsReady) return false;
        foreach (var expired in TeamReservations.Values.Where(p => !p.IsCurrent).ToArray()) expired.Dispose();
        var pairs = new List<(BotZone zone, ISpawnPoint point)>();
        foreach (var zone in zones ?? Enumerable.Empty<BotZone>())
        {
            if (zone == null || ArenaBotSpawnFilter.IsExcludedZone(zone) || zone.SpawnPoints == null) continue;
            foreach (var point in zone.SpawnPoints)
                if (point != null && IsBotPoint(point) && !point.SpawnBlocked && AllowsSpawn(point)
                    && !ArenaBotSpawnFilter.IsExcludedPoint(point)
                    && (!_teamStarting || _area.TeamStarts.Allows(usec, false, point.Id)))
                    pairs.Add((zone, point));
        }
        selectedPoint = PickTeamPoint(pairs.Select(p => p.point), usec, default, null);
        if (selectedPoint == null) return false;
        var picked = selectedPoint;
        selectedZone = pairs.First(p => ReferenceEquals(p.point, picked)).zone;
        if (reserve)
        {
            lease = new TeamSpawnLease(_token, selectedPoint, usec);
            TeamReservations.Add(lease.PointId, lease);
            Plugin.ModLogger.LogInfo($"Arena team {(_teamStarting ? "start" : "respawn")}: {(usec ? "USEC" : "BEAR")}, "
                + $"{selectedPoint.Id}; nearest enemy {NearestEnemyDistance(selectedPoint.Position, usec):F1}m.");
        }
        return true;
    }

    private static ISpawnPoint PickTeamPoint(IEnumerable<ISpawnPoint> points, bool usec, Vector3 avoid, Player ignore)
    {
        // Highest minimum enemy distance wins. Death position breaks ties only;
        // no enemy may be mistaken for just the local player's old body.
        return points.Where(p => IsUnoccupied(p.Position, ignore, null))
            .OrderByDescending(p => NearestEnemyDistance(p.Position, usec))
            .ThenByDescending(p => HorizontalSquared(p.Position, avoid)).FirstOrDefault();
    }

    private static bool IsUnoccupied(Vector3 position, Player ignore, TeamSpawnLease own)
    {
        if (_world?.AllAlivePlayersList == null) return false;
        foreach (var player in _world.AllAlivePlayersList.OfType<Player>())
            if (player != null && !ReferenceEquals(player, ignore) && player.HealthController?.IsAlive == true
                && HorizontalSquared(player.Position, position) < 2.25f && Mathf.Abs(player.Position.y - position.y) < 2f)
                return false;
        return !TeamReservations.Values.Any(r => !ReferenceEquals(r, own) && r.IsCurrent
            && HorizontalSquared(r.Position, position) < 2.25f);
    }

    private static float NearestEnemyDistance(Vector3 position, bool usec)
    {
        var side = usec ? EPlayerSide.Usec : EPlayerSide.Bear;
        float nearest = float.PositiveInfinity;
        foreach (var player in _world.AllAlivePlayersList.OfType<Player>())
            if (player != null && player.HealthController?.IsAlive == true && player.Profile?.Info?.Side != side)
                nearest = Mathf.Min(nearest, Mathf.Sqrt(HorizontalSquared(position, player.Position)));
        return nearest;
    }
    private static float HorizontalSquared(Vector3 a, Vector3 b)
        => (a.x-b.x)*(a.x-b.x) + (a.z-b.z)*(a.z-b.z);
    private static void ResetTeamStarts()
    {
        _teamSize = 0; _teamStarting = false; _loadingCollection = null; _initialTeamPoint = null;
        _playerReservation = null;
        TeamReservations.Clear();
    }
}
