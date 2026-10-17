using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Session;
using ArenaMode.Compat;
using ArenaMode.Shared;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.Game.Spawning;
using EFT.Bots;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Spawning;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.AI;
using Formation = ArenaMode.Shared.ArenaEquatorTeamSpawns;

namespace ArenaMode.Bots;

/// <summary>
/// Equator's general markers serve free-for-all play. Independent TDM instead
/// owns twenty per-raid markers inside the two original TeamFight boxes.
/// MapPort's conservative booth-AABB repair moves even physically clear booth
/// points outside; restore only our exact server-owned records after that repair.
/// Native floor, standing room, connectivity and live reservations remain gates.
/// Native gameplay acceptance is recorded separately in EQUATOR-TEAM-SPAWNS-REVIEW.
/// </summary>
internal static class ArenaEquatorTeamSpawns
{
    private static LocalGame _game;
    private static GameWorld _world;
    private static int _armEpoch = -1;
    private static string _token;
    private static ISpawnPointsCollection _collection;
    private static ArenaMapPortSpawnGates.Lease _gates;
    private static float _openingStartedAt = -1f;
    private static bool _gatesOpened;
    private static readonly Dictionary<ExfiltrationPoint, Player> DeferredExtractions = new();
    private static readonly Dictionary<string, ISpawnPoint> Points = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Lease> Reservations = new(StringComparer.Ordinal);
    private static Lease _playerReservation;
    private static CollectionPatch _collectionPatch;
    private static InitialPatch _initialPatch;
    private static readonly Harmony LoadingPatches = new Harmony(Plugin.PluginGuid + ".EquatorTeamLoading");
    private static readonly Vector3 Atrium = new Vector3(0f, .2f, 2f);
    private const float OccupancySquared = 2.25f;
    internal static bool IsReady { get; private set; }

    // Required is independent of prepared state. Missing hooks/markers may
    // defer or fail a protected match, but must never enable a map-wide fallback.
    internal static bool IsRequired => ArenaSession.IsActive
        && Singleton<AbstractGame>.Instantiated && Singleton<GameWorld>.Instantiated
        && Singleton<AbstractGame>.Instance is LocalGame game
        && Formation.Applies(Singleton<GameWorld>.Instance?.LocationId, ArenaSession.ActiveMode)
        && ReferenceEquals(game.GameWorld, Singleton<GameWorld>.Instance)
        && ArenaSession.IsExactActiveRaid(game, game.GameWorld);

    private static bool Current => IsRequired && IsReady && _game != null && _world != null
        && _armEpoch == ArenaSession.ArmEpoch && !string.IsNullOrEmpty(_token)
        && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
        && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
        && _game.Status == GameStatus.Started && Points.Count == Formation.Points.Count;

    internal static void Enable()
    {
        if (IsReady) return;
        try
        {
            _collectionPatch = new CollectionPatch();
            _collectionPatch.Enable();
            _initialPatch = new InitialPatch();
            _initialPatch.Enable();
            var target = AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "Run",
                new[] { typeof(BotControllerSettings), typeof(string), typeof(InventoryController) });
            if (target == null || target.ReturnType != typeof(Task))
                throw new MissingMethodException("BaseLocalGame.Run load task contract changed.");
            LoadingPatches.Patch(target,
                prefix: new HarmonyMethod(typeof(ArenaEquatorTeamSpawns), nameof(BeforeLoad)),
                postfix: new HarmonyMethod(typeof(ArenaEquatorTeamSpawns), nameof(AfterLoad)),
                finalizer: new HarmonyMethod(typeof(ArenaEquatorTeamSpawns), nameof(FinalizeLoad)));
            var stop = AccessTools.DeclaredMethod(typeof(LocalGame), nameof(LocalGame.Stop),
                new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
            if (stop == null) throw new MissingMethodException("LocalGame.Stop contract changed.");
            LoadingPatches.Patch(stop,
                postfix: new HarmonyMethod(typeof(ArenaEquatorTeamSpawns), nameof(AfterStop)));
            var proceed = AccessTools.DeclaredMethod(typeof(ExfiltrationPoint), nameof(ExfiltrationPoint.Proceed),
                new[] { typeof(Player), typeof(bool) });
            if (proceed == null || proceed.ReturnType != typeof(void))
                throw new MissingMethodException("Native exfiltration Proceed contract changed.");
            LoadingPatches.Patch(proceed,
                prefix: new HarmonyMethod(typeof(ArenaEquatorTeamSpawns), nameof(BeforeExtraction)));
            IsReady = true;
        }
        catch (Exception ex)
        {
            _initialPatch?.Disable(); _collectionPatch?.Disable();
            LoadingPatches.UnpatchSelf();
            _initialPatch = null; _collectionPatch = null; IsReady = false;
            Plugin.ModLogger.LogError("Equator Team DM spawn hooks unavailable: " + ex.Message);
        }
    }

    internal static void Shutdown()
    {
        IsReady = false; Reset(); LoadingPatches.UnpatchSelf();
        _initialPatch?.Disable(); _collectionPatch?.Disable();
        _initialPatch = null; _collectionPatch = null;
    }

    internal static void Begin(GameWorld world, LocalGame game)
    {
        if (!IsRequired) return;
        if (!Current || !ReferenceEquals(game, _game) || !ReferenceEquals(world, _world)
            || !ArenaSession.Participants.HasValue || ArenaSession.Participants.Value % 2 != 0
            || ArenaSession.Participants.Value < 4 || ArenaSession.Participants.Value > Formation.MaximumParticipants)
            throw new InvalidOperationException("Equator Team DM did not prepare its exact frozen formation.");
        int size = ArenaSession.Participants.Value / 2;
        foreach (bool usec in new[] { true, false })
        {
            int capacity = Points.Values.Count(p => PointCurrent(p, usec) && FloorAndClear(p.Position) && Connected(p.Position));
            Plugin.ModLogger.LogDebug($"Equator team-box capacity: {(usec ? "USEC" : "BEAR")} {capacity}/{Formation.CapacityPerTeam} safe connected places, {size} required.");
            if (capacity < size)
                throw new InvalidOperationException($"Equator {(usec ? "USEC" : "BEAR")} box has {capacity}/{size} safe connected places.");
        }
        var local = world.MainPlayer;
        if (local == null || local.ProfileId != game.Profile.Id || !FloorAndClear(local.Position) || !Connected(local.Position)
            || !Points.Values.Any(p => PointCurrent(p, game.Profile.Info.Side == EPlayerSide.Usec)
                && (p.Position - local.Position).sqrMagnitude <= .5625f && Connected(p.Position)))
            throw new InvalidOperationException("Equator initial PMC did not reach its own connected team box.");
        Plugin.ModLogger.LogDebug($"Equator Team DM formation ready: {size}v{size}, USEC north / BEAR south.");
    }

    private sealed class LoadingLease
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal int Epoch;
        internal string Token;
    }

    private static void BeforeLoad(BaseLocalGame<EftGamePlayerOwner> __instance, out LoadingLease __state)
    {
        __state = null;
        if (!ArenaSession.ArmedForNextRaid || !Formation.Applies(ArenaSession.ArmedMap, ArenaSession.ArmedMode)
            || __instance is not LocalGame game || !Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, game) || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, game.GameWorld)) return;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        string token = ArenaServerClient.PendingArmToken;
        if (settings == null || !string.Equals(settings.location, Formation.MapId, StringComparison.OrdinalIgnoreCase)
            || !Protocol.IsLifecycleRequestId(token)
            || (!Protocol.IsNativeStartIdentity(settings.serverId, token) && !Protocol.HasNativeEndIdentity(settings.serverId, token))) return;
        __state = new LoadingLease { Game = game, World = game.GameWorld, Epoch = ArenaSession.ArmEpoch, Token = token };
    }

    private static void AfterLoad(LoadingLease __state, ref Task __result)
    { if (__state != null && __result != null) __result = ObserveLoad(__result, __state); }

    private static Exception FinalizeLoad(Exception __exception, LoadingLease __state)
    { if (__exception != null) LoadingFailed(__state, __exception); return __exception; }

    private static async Task ObserveLoad(Task original, LoadingLease lease)
    {
        try
        {
            await original;
            // Run completes after the raid-start callbacks. A consumed but
            // rejected arm must not leave an otherwise ordinary raid sealed.
            if (ReferenceEquals(lease.Game, _game) && ReferenceEquals(lease.World, _world)
                && lease.Epoch == _armEpoch && lease.Token == _token && !IsRequired)
                ReleaseGates("ArenaMode Equator start not admitted");
        }
        catch (Exception ex) { LoadingFailed(lease, ex); throw; }
    }

    private static void AfterStop(LocalGame __instance, string profileId)
    {
        // Native Stop can be called for an extracted bot and ignores that
        // profile. Redeploy can also suppress it for a life-only Give Up.
        // Only a committed native shutdown releases this local match's hold.
        if (ReferenceEquals(__instance, _game) && !string.IsNullOrEmpty(profileId)
            && profileId == _game?.Profile?.Id
            && (_game.Status == GameStatus.Stopping || _game.Status == GameStatus.Stopped))
            ReleaseGates("ArenaMode Equator native stop");
    }

    private static void LoadingFailed(LoadingLease lease, Exception failure)
    {
        if (lease == null || ArenaSession.IsActive || !ArenaSession.ArmedForNextRaid
            || lease.Epoch != ArenaSession.ArmEpoch || lease.Token != ArenaServerClient.PendingArmToken
            || !Formation.Applies(ArenaSession.ArmedMap, ArenaSession.ArmedMode)
            || (Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance != null
                && !ReferenceEquals(Singleton<AbstractGame>.Instance, lease.Game))
            || (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null
                && !ReferenceEquals(Singleton<GameWorld>.Instance, lease.World))) return;
        Plugin.ModLogger.LogError("Equator team loading failed before actor release: " + failure.GetBaseException().Message);
        ArenaSession.ClearArmed("Equator team-box loading failed");
        ArenaMenuSelection.ClearCompatibilityState("Equator team-box loading failed");
#pragma warning disable CS0618
        InGameStatus.InRaid = false;
#pragma warning restore CS0618
    }

    internal static void Reset()
    {
        ReleaseGates("ArenaMode Equator reset");
        _gates = null; _openingStartedAt = -1f; _gatesOpened = false;
        Reservations.Clear(); Points.Clear(); _playerReservation = null;
        _game = null; _world = null; _collection = null; _token = null; _armEpoch = -1;
    }

    private static bool TryLoading(out LocalGame game, out string token)
    {
        game = null; token = null;
        if (!ArenaSession.ArmedForNextRaid
            || !Formation.Applies(ArenaSession.ArmedMap, ArenaSession.ArmedMode)
            || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame local
            || local.Status != GameStatus.Running
            || !ReferenceEquals(local.GameWorld, Singleton<GameWorld>.Instance)
            || !string.Equals(local.GameWorld.LocationId, Formation.MapId, StringComparison.OrdinalIgnoreCase)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(local);
        token = ArenaServerClient.PendingArmToken;
        if (!Protocol.IsLifecycleRequestId(token) || settings == null
            || !string.Equals(settings.location, Formation.MapId, StringComparison.OrdinalIgnoreCase)
            || (!Protocol.IsNativeStartIdentity(settings.serverId, token)
                && !Protocol.HasNativeEndIdentity(settings.serverId, token)))
            throw new InvalidOperationException("Equator Team DM loading has no exact server arm identity.");
        game = local;
        return true;
    }

    private static void RestoreCollection(SpawnPointsCollection collection)
    {
        bool loading = TryLoading(out var game, out var token);
        if (!loading && !IsRequired) return;
        if (!IsReady || collection?._dictionary == null)
            throw new InvalidOperationException("Equator Team DM cannot inspect its native spawn collection.");
        if (loading)
        {
            if (_game != null && (!ReferenceEquals(game, _game) || _armEpoch != ArenaSession.ArmEpoch)) Reset();
            _game = game; _world = game.GameWorld; _token = token; _armEpoch = ArenaSession.ArmEpoch;
        }
        else if (!Current)
            throw new InvalidOperationException("Equator Team DM lost its prepared loading owner.");

        var owned = collection._dictionary.Where(p => p.Key != null && Formation.Owns(p.Key.Id)).ToArray();
        if (owned.Length != Formation.Points.Count || owned.Any(p => Formation.Find(p.Key.Id) == null)
            || owned.Select(p => p.Key.Id).Distinct(StringComparer.Ordinal).Count() != owned.Length)
            throw new InvalidOperationException("Equator Team DM formation records are missing, unknown or duplicated.");
        var zones = FindObjectsProxy.FindUnityObjectsOfType<BotZone>()
            .Where(z => z != null && z.name == Formation.Zone).ToArray();
        if (zones.Length != 1)
            throw new InvalidOperationException("Equator Team DM has no unique native BotZone_Main.");
        var zone = zones[0];
        foreach (var pair in owned)
        {
            var spec = Formation.Find(pair.Key.Id);
            var marker = pair.Value;
            if (pair.Key is not SpawnPoint point || marker == null
                || !ReferenceEquals(marker.SpawnPoint, point)
                || zone.SpawnPointMarkers == null || !zone.SpawnPointMarkers.Contains(marker)
                || !HeadersMatch(point, spec)
                || marker.GetComponent<SphereCollider>() is not SphereCollider collider
                || !collider.isTrigger || !collider.enabled || collider.center != Vector3.zero
                || Mathf.Abs(collider.radius - Formation.ColliderRadius) > .0001f)
                throw new InvalidOperationException("Equator Team DM native formation header changed: " + spec.Id);
        }
        // Validate the entire set before restoring any position. Both native
        // CreateFromScene calls and the later Redeploy cache are idempotent.
        foreach (var pair in owned)
        {
            var spec = Formation.Find(pair.Key.Id);
            var point = (SpawnPoint)pair.Key;
            var marker = pair.Value;
            point.Position = Position(spec); point.Rotation = Quaternion.Euler(0f, spec.Yaw, 0f);
            marker.transform.position = point.Position; marker.transform.rotation = point.Rotation;
            marker.BotZone = zone; marker._collider = marker.GetComponent<SphereCollider>();
            point.Collider = marker;
        }
        Physics.SyncTransforms();
        Points.Clear();
        foreach (var pair in owned) Points.Add(pair.Key.Id, pair.Key);
        _collection = collection;
        // MapPort's collection postfix has now offered the real door parts;
        // the native NavMesh is installed later. Repeated collection restores
        // must not re-close gates, including after a later player respawn.
        if (loading && _gates == null && !_gatesOpened)
            _gates = ArenaMapPortSpawnGates.TryHold(_game, _world, _armEpoch, _token);
        Plugin.ModLogger.LogDebug("Equator Team DM: restored 10 USEC north / 10 BEAR south box markers.");
    }

    private static bool HeadersMatch(ISpawnPoint point, Formation.Point spec)
        => spec != null && point.Id == spec.Id
           && point.Sides == (spec.Usec ? EPlayerSideMask.Usec : EPlayerSideMask.Bear)
           && point.Categories == (ESpawnCategoryMask.Player | ESpawnCategoryMask.Bot | ESpawnCategoryMask.BotPmc)
           && point.Infiltration == Formation.Infiltration && point.CorePointId == Formation.CorePointId
           && Mathf.Abs(point.DelayToCanSpawnSec - 4f) < .0001f;

    private static Vector3 Position(Formation.Point spec) => new Vector3(spec.X, spec.Y, spec.Z);
    private static float HorizontalSquared(Vector3 a, Vector3 b)
        => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

    private static bool PointCurrent(ISpawnPoint point, bool usec)
    {
        var spec = Formation.Find(point?.Id);
        return spec != null && spec.Usec == usec && !point.SpawnBlocked && HeadersMatch(point, spec)
            && Points.TryGetValue(point.Id, out var current) && ReferenceEquals(current, point)
            && point.BotZoneName == Formation.Zone && (point.Position - Position(spec)).sqrMagnitude < .0001f
            && Quaternion.Angle(point.Rotation, Quaternion.Euler(0f, spec.Yaw, 0f)) < .1f;
    }

    private static bool FloorAndClear(Vector3 position)
    {
        int mask = LayersMaskController.PlayerStaticCollisionsMask;
        if (!Physics.Raycast(position + Vector3.up * .25f, Vector3.down, out var hit, .5f, mask,
                QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - position.y) > .15f || hit.normal.y < .7f)
            return false;
        var bottom = position + Vector3.up * .45f;
        var top = position + Vector3.up * 1.45f;
        return !Physics.OverlapCapsule(bottom, top, .35f, mask, QueryTriggerInteraction.Ignore)
            .Any(c => c != null && c.GetComponentInParent<Player>() == null);
    }

    private static bool Connected(Vector3 position)
    {
        if (!NavMesh.SamplePosition(position, out var point, .4f, NavMesh.AllAreas)
            || HorizontalSquared(point.position, position) > .09f || Mathf.Abs(point.position.y - position.y) > .3f
            || !NavMesh.SamplePosition(Atrium, out var anchor, .4f, NavMesh.AllAreas)
            || HorizontalSquared(anchor.position, Atrium) > .09f || Mathf.Abs(anchor.position.y - Atrium.y) > .3f)
            return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(point.position, anchor.position, NavMesh.AllAreas, path)) return false;
        if (path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 2) return true;
        // Only our own still-held gates may temporarily split this exact
        // original booth from the atrium. Invalid paths, external holds and
        // positions outside the canonical formation have no exception.
        if (path.status != NavMeshPathStatus.PathPartial || _gates?.IsHeld != true
            || !TryBooth(position, out bool usec)) return false;
        var boothAnchor = Position(Formation.Points[usec ? 2 : 12]);
        if (!FloorAndClear(boothAnchor)
            || !NavMesh.SamplePosition(boothAnchor, out var booth, .4f, NavMesh.AllAreas)
            || HorizontalSquared(booth.position, boothAnchor) > .09f
            || Mathf.Abs(booth.position.y - boothAnchor.y) > .3f) return false;
        var local = new NavMeshPath();
        return NavMesh.CalculatePath(point.position, booth.position, NavMesh.AllAreas, local)
            && local.status == NavMeshPathStatus.PathComplete && local.corners.Length > 0
            && local.corners.All(p => InsideBooth(p, usec));
    }

    private static bool TryBooth(Vector3 position, out bool usec)
    {
        usec = position.z > 0f;
        return InsideBooth(position, usec) && Points.Values.Any(p => PointCurrent(p, position.z > 0f)
            && (p.Position - position).sqrMagnitude <= .5625f);
    }

    // Native SPAWN_BOUND_FIGHTER interiors, inset at the doorway and kept on
    // the surveyed floor; the enclosed south rear compartment is not a spawn.
    private static bool InsideBooth(Vector3 p, bool usec)
        => p.x >= -9.8f && p.x <= 9.8f && p.y >= -.22f && p.y <= .38f
           && (usec ? p.z >= 40.5f && p.z <= 51f : p.z >= -43f && p.z <= -34f);

    internal static bool GatesOpening => _openingStartedAt >= 0f && !_gatesOpened;

    internal static void ReleaseGates(LocalGame game, GameWorld world, int epoch, string reason)
    {
        if (ReferenceEquals(game, _game) && ReferenceEquals(world, _world) && epoch == _armEpoch)
            ReleaseGates(reason);
    }

    private static void ReleaseGates(string reason)
    {
        // Teardown never starts an extraction or retains a local player body.
        DeferredExtractions.Clear();
        _gates?.Release(reason);
    }

    private static bool CurrentLocal(Player player)
        => player != null && _world != null && _game != null
           && ReferenceEquals(player, _world.MainPlayer) && player.ProfileId == _game.Profile.Id
           && player.HealthController?.IsAlive == true
           && ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player);

    private static bool CurrentBoothExit(ExfiltrationPoint point)
        => point != null && (point.Settings?.Name == "Equator North Gate" || point.Settings?.Name == "Equator South Gate")
           && ExfiltrationController.Instance?.ExfiltrationPoints?.Any(p => ReferenceEquals(p, point)) == true;

    private static bool BeforeExtraction(ExfiltrationPoint __instance, Player player)
    {
        if (_gates?.OwnsCurrentRaid != true || (!_gates.IsHeld && !GatesOpening)
            || !(IsRequired || ArenaSession.ArmedForNextRaid
                && Formation.Applies(ArenaSession.ArmedMap, ArenaSession.ArmedMode))
            || !CurrentLocal(player) || !CurrentBoothExit(__instance) || !__instance.Entered.Contains(player)) return true;
        // Both original booth exits overlap genuine team starts. Let native
        // Entered track the body, but do not register an eight-second escape
        // while its only doorway is deliberately sealed for preparation.
        DeferredExtractions[__instance] = player;
        return false;
    }

    private static void ResumeExtractions()
    {
        var deferred = DeferredExtractions.ToArray();
        DeferredExtractions.Clear();
        foreach (var pair in deferred)
            if (Current && _gates?.OwnsCurrentRaid == true && CurrentLocal(pair.Value)
                && CurrentBoothExit(pair.Key) && pair.Key.Entered.Contains(pair.Value))
                pair.Key.Proceed(pair.Value);
    }

    internal static bool TryOpenForCombat()
    {
        if (!IsRequired || _gates == null || _gatesOpened) return true;
        if (!Current) throw new InvalidOperationException("Equator gate opening lost its exact raid owner.");
        if (_openingStartedAt < 0f)
        {
            _openingStartedAt = Time.time;
            // Keep deferred native exit entries through the slide. Cleanup
            // paths use ReleaseGates instead and deliberately discard them.
            _gates.Release("ArenaMode Equator TDM countdown finished");
        }
        // IsHeld becomes false before MapPort's 0..5 s slide removes carvers.
        // Keep damage, score, clock and announcement blocked until native
        // complete paths prove both booths have really reopened.
        bool connected = _gates.Released && Points.Values.All(p =>
            PointCurrent(p, Formation.Find(p.Id).Usec) && FloorAndClear(p.Position) && Connected(p.Position));
        if (connected)
        {
            _gatesOpened = true;
            ResumeExtractions();
            Plugin.ModLogger.LogDebug("Equator Team DM gates open: all twenty formation paths revalidated.");
            return true;
        }
        // Use the same simulation clock as MapPort's slide coroutine: pausing
        // the game must not turn a paused animation into a failed opening.
        if (Time.time - _openingStartedAt >= 10f)
            throw new InvalidOperationException("Equator team boxes did not reconnect after opening their spawn gates.");
        return false;
    }

    private static bool Unoccupied(Vector3 position, Player ignore, Lease own)
    {
        if (_world?.AllAlivePlayersList == null) return false;
        foreach (var player in _world.AllAlivePlayersList.OfType<Player>())
            if (player != null && !ReferenceEquals(player, ignore) && player.HealthController?.IsAlive == true
                && HorizontalSquared(player.Position, position) < OccupancySquared
                && Mathf.Abs(player.Position.y - position.y) < 2f) return false;
        // Pending native requests have no timeout. A slow activation must not
        // make its still-promised position available to a replacement request.
        return !Reservations.Values.Any(r => !ReferenceEquals(r, own)
            && HorizontalSquared(r.Position, position) < OccupancySquared);
    }

    private static float EnemyDistance(Vector3 position, bool usec)
    {
        var side = usec ? EPlayerSide.Usec : EPlayerSide.Bear;
        float distance = float.PositiveInfinity;
        foreach (var player in _world.AllAlivePlayersList.OfType<Player>())
            if (player != null && player.HealthController?.IsAlive == true && player.Profile?.Info?.Side != side)
                distance = Mathf.Min(distance, HorizontalSquared(position, player.Position));
        return distance;
    }

    private static ISpawnPoint Pick(IEnumerable<ISpawnPoint> candidates, bool usec, Vector3 avoid, Player ignore)
        => candidates.Where(p => p != null && PointCurrent(p, usec) && FloorAndClear(p.Position)
                && Connected(p.Position) && Unoccupied(p.Position, ignore, null))
            .OrderByDescending(p => EnemyDistance(p.Position, usec))
            .ThenByDescending(p => HorizontalSquared(p.Position, avoid)).FirstOrDefault();

    internal static bool TryPlanBot(IEnumerable<BotZone> zones, bool usec, bool reserve,
        out BotZone selectedZone, out ISpawnPoint selectedPoint, out Lease lease)
    {
        selectedZone = null; selectedPoint = null; lease = null;
        if (!Current) return false;
        var zone = (zones ?? Enumerable.Empty<BotZone>()).SingleOrDefault(z => z != null && z.name == Formation.Zone);
        if (zone == null || zone.SpawnPoints == null) return false;
        selectedPoint = Pick(zone.SpawnPoints, usec, default, null);
        if (selectedPoint == null) return false;
        selectedZone = zone;
        if (reserve)
        {
            lease = new Lease(selectedPoint, usec);
            Reservations.Add(lease.PointId, lease);
            Plugin.ModLogger.LogDebug($"Equator team bot spawn: {(usec ? "USEC" : "BEAR")}, {lease.PointId}, {lease.Position}.");
        }
        return true;
    }

    internal static (Vector3 position, Quaternion rotation) PickRespawn(LocalGame game, Vector3 avoid)
    {
        if (!Current || !ReferenceEquals(game, _game))
            throw new InvalidOperationException("Equator Team DM respawn has no exact prepared raid.");
        var point = Pick(Points.Values, _game.Profile.Info.Side == EPlayerSide.Usec, avoid, _world.MainPlayer);
        if (point == null) throw new InvalidOperationException("No unoccupied connected Equator team-box respawn is available.");
        return (point.Position, point.Rotation);
    }

    internal static bool TryPrepareRespawn(LocalGame game, ISpawnSelector original, out ISpawnSelector prepared)
    {
        prepared = original;
        if (!IsRequired || original is not Respawn.ArenaSpawnSelector) return true;
        try
        {
            if (!Current || !ReferenceEquals(game, _game))
                throw new InvalidOperationException("The Equator respawn lost its exact raid owner.");
            var position = original.Resolve(game);
            var point = Points.Values.SingleOrDefault(p => (p.Position - position.position).sqrMagnitude < .0001f);
            bool usec = _game.Profile.Info.Side == EPlayerSide.Usec;
            if (point == null || !PointCurrent(point, usec) || !Unoccupied(point.Position, _world.MainPlayer, null))
                throw new InvalidOperationException("The selected Equator respawn is no longer unoccupied.");
            if (_playerReservation != null)
                throw new InvalidOperationException("An earlier Equator player handover still owns a reservation.");
            var lease = new Lease(point, usec);
            Reservations.Add(lease.PointId, lease); _playerReservation = lease;
            prepared = new Prepared(game, lease);
            Plugin.ModLogger.LogDebug($"Equator team player respawn: {lease.PointId}, {lease.Position}.");
            return true;
        }
        catch (Exception ex)
        {
            if (IsRequired) Modes.ArenaMatchService.ScheduleFailedStartAbort(
                "Equator team respawn preflight: " + ex.GetBaseException().Message);
            return false;
        }
    }

    internal static bool ReleasePlayerReservation(Player player)
    {
        if (!IsRequired || _playerReservation == null) return true; // fixed in-place kit swap
        if (!Current || player == null || !ReferenceEquals(player, _world.MainPlayer)
            || player.ProfileId != _game.Profile.Id || !_playerReservation.Accept(player))
        {
            Modes.ArenaMatchService.ScheduleFailedStartAbort("Equator player arrival lost its exact safe team-box reservation.");
            return false;
        }
        _playerReservation.Dispose(); _playerReservation = null;
        return true;
    }

    internal sealed class Lease : IDisposable
    {
        private readonly LocalGame _owner = _game;
        private readonly GameWorld _ownerWorld = _world;
        private readonly int _epoch = _armEpoch;
        private readonly string _armToken = _token;
        private readonly ISpawnPoint _point;
        private readonly bool _usec;
        private bool _arrivalLogged;
        internal readonly string PointId;
        internal readonly Vector3 Position;
        internal Lease(ISpawnPoint point, bool usec)
        { _point = point; _usec = usec; PointId = point.Id; Position = point.Position; }
        private bool Valid => Current && ReferenceEquals(_owner, _game) && ReferenceEquals(_ownerWorld, _world)
            && _epoch == _armEpoch && _armToken == _token
            && Reservations.TryGetValue(PointId, out var lease) && ReferenceEquals(lease, this)
            && PointCurrent(_point, _usec) && (_point.Position - Position).sqrMagnitude < .0001f;
        internal bool Accept(Player player)
        {
            bool accepted = Valid && player != null && player.HealthController?.IsAlive == true
                && player.Profile?.Info?.Side == (_usec ? EPlayerSide.Usec : EPlayerSide.Bear)
                && ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)
                && (player.Position - Position).sqrMagnitude <= .5625f
                && FloorAndClear(Position) && Connected(Position)
                && FloorAndClear(player.Position) && Connected(player.Position)
                && Unoccupied(player.Position, player, this);
            if (accepted && !_arrivalLogged)
            {
                _arrivalLogged = true;
                Plugin.ModLogger.LogDebug($"Equator team arrival: {player.ProfileId}, {player.Profile.Info.Side}, {PointId}, actual {player.Position}.");
            }
            return accepted;
        }
        internal bool CanReleasePlayer => Valid && FloorAndClear(Position) && Connected(Position)
            && Unoccupied(Position, _world.MainPlayer, this);
        internal Quaternion Rotation => _point.Rotation;
        public void Dispose()
        {
            if (Reservations.TryGetValue(PointId, out var lease) && ReferenceEquals(lease, this)) Reservations.Remove(PointId);
        }
    }

    private sealed class Prepared : ISpawnSelector
    {
        private readonly LocalGame _owner;
        private readonly Lease _lease;
        internal Prepared(LocalGame game, Lease lease) { _owner = game; _lease = lease; }
        public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
        {
            if (!ReferenceEquals(game, _owner) || !_lease.CanReleasePlayer)
                throw new InvalidOperationException("The prepared Equator spawn lost its exact safe reservation.");
            return (_lease.Position, _lease.Rotation);
        }
    }

    private sealed class CollectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(SpawnPointsCollection), nameof(SpawnPointsCollection.CreateFromScene),
                new[] { typeof(DateTime?), typeof(SpawnPointParams[]) });
        [PatchPostfix, HarmonyPriority(Priority.Last), HarmonyAfter("com.maschine.ArenaMapPort")]
        private static void Postfix(SpawnPointsCollection __result) => RestoreCollection(__result);
    }

    private sealed class InitialPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var map = typeof(SpawnSystem).GetInterfaceMap(typeof(ISpawnSystem));
            int index = Array.FindIndex(map.InterfaceMethods, m => m.Name == nameof(ISpawnSystem.SelectSpawnPoint));
            if (index < 0) throw new MissingMethodException("Native spawn selection contract changed.");
            return map.TargetMethods[index];
        }
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static bool Prefix(SpawnSystem __instance, ESpawnCategory category, EPlayerSide side,
            string profileId, ref ISpawnPoint __result)
        {
            if (category != ESpawnCategory.Player || !TryLoading(out var game, out _)
                || profileId != game.Profile.Id || side != game.Profile.Info.Side) return true;
            if (!IsReady || !ReferenceEquals(game, _game) || _armEpoch != ArenaSession.ArmEpoch
                || __instance == null || !ReferenceEquals(__instance._players, _world)
                || !ReferenceEquals(__instance._spawnPoints, _collection))
                throw new InvalidOperationException("Equator team initial spawn lost its exact prepared collection.");
            bool usec = side == EPlayerSide.Usec;
            // Native NavMesh loads later in BotsController.Init. Initial entry
            // uses actual physics; each bot/respawn requires the full path later.
            var choices = Points.Values.Where(p => PointCurrent(p, usec) && FloorAndClear(p.Position)).ToArray();
            if (choices.Length == 0) throw new InvalidOperationException("Equator has no physically clear initial team-box point.");
            __result = choices[UnityEngine.Random.Range(0, choices.Length)];
            Plugin.ModLogger.LogDebug($"Equator team initial PMC: {side}, {__result.Id}, {__result.Position}.");
            return false;
        }
    }
}
