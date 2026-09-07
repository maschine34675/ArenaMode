using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using Redeploy.Core.GameAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Areas;

/// <summary>
/// Owns one experimental enclosure, from its token-bound loading game through
/// its exact raid end. No scene assets, global collision masks or bot settings
/// are edited. Geometry alone is not readiness: actual paths are checked after
/// carving, and all spawns are restricted to that connected component.
/// </summary>
internal static class ArenaAreaRuntime
{
    private enum Phase { Armed, Loading, Preparing, Ready, Failed }
    private sealed class ActorState
    {
        internal Vector3 LastInside;
        internal bool HasInside;
        internal int Recoveries;
        internal long Rejections;
        internal float WindowStart;
        internal Vector3 WindowPosition;
        internal int WindowRejections;
        internal int WindowScore, StalledWindows;
    }

    private static ArenaAreaDefinition _area;
    private static string _token;
    private static LocalGame _game;
    private static GameWorld _world;
    private static Phase _phase;
    private static string _failure;
    private static GameObject _root;
    private static Material _material;
    private static Mesh _mesh;
    private static readonly object InputOwner = new object();
    private static readonly List<ISpawnPoint> Candidates = new List<ISpawnPoint>();
    private static readonly HashSet<ISpawnPoint> Connected = new HashSet<ISpawnPoint>();
    private static readonly Dictionary<Player, ActorState> Actors = new Dictionary<Player, ActorState>();
    private static readonly List<NavMeshObstacle> Obstacles = new List<NavMeshObstacle>();
    private static readonly List<BoxCollider> Barriers = new List<BoxCollider>();
    private static readonly NavMeshPath ScratchPath = new NavMeshPath();
    private static readonly Func<Vector3, ArenaAreaPoint> ToPoint = p => new ArenaAreaPoint(p.x, p.y, p.z);
    private static Vector3 _anchor;
    private static float _deadline, _nextValidation, _nextActorCheck, _nextRejectionLog;
    private static int _createdFrame, _totalRejections;

    internal static ArenaAreaDefinition CurrentArea => IsCurrentActive() ? _area : null;
    internal static bool IsPending => IsCurrentActive() && _phase != Phase.Ready;
    internal static bool IsReady => IsCurrentActive() && _phase == Phase.Ready;

    internal static void Arm(ArenaAreaDefinition area, string token)
    {
        Reset();
        if (area == null) return;
        if (!Protocol.IsLifecycleRequestId(token))
            throw new InvalidOperationException("Bounded Arena has no exact arm identity.");
        _area = area;
        _token = token;
        _phase = Phase.Armed;
        Plugin.ModLogger.LogInfo($"Arena area armed: {area.Id}; fixed {area.MaxTeamSize}v{area.MaxTeamSize}.");
    }

    private static bool IsCurrentActive()
        => _area != null && _game != null && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && _game.Status == GameStatus.Started
           && ArenaSession.IsExactActiveRaid(_game, _world)
           && ArenaSession.ActiveMode == ArenaModeCatalog.TeamDeathmatchId;

    private static bool BindLoadingGame()
    {
        if (_area == null || !ArenaSession.ArmedForNextRaid
            || ArenaSession.ArmedMode != ArenaModeCatalog.TeamDeathmatchId
            || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame game
            || game.Status != GameStatus.Running
            || !ReferenceEquals(game.GameWorld, Singleton<GameWorld>.Instance)
            || !string.Equals(game.GameWorld.LocationId, _area.MapId, StringComparison.OrdinalIgnoreCase)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        if (settings == null || (!Protocol.IsNativeStartIdentity(settings.serverId, _token)
            && !Protocol.HasNativeEndIdentity(settings.serverId, _token))) return false;
        if (_game != null && (!ReferenceEquals(_game, game) || !ReferenceEquals(_world, game.GameWorld))) return false;
        _game = game;
        _world = game.GameWorld;
        _phase = Phase.Loading;
        return true;
    }

    internal sealed class LoadingLease
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal string Token;
    }

    internal static LoadingLease CaptureLoading(LocalGame game)
    {
        if (_area == null || !ArenaSession.ArmedForNextRaid || game == null
            || ArenaSession.ArmedMode != ArenaModeCatalog.TeamDeathmatchId
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, game.GameWorld)) return null;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        if (settings == null || !string.Equals(settings.location, _area.MapId, StringComparison.OrdinalIgnoreCase)
            || (!Protocol.IsNativeStartIdentity(settings.serverId, _token)
                && !Protocol.HasNativeEndIdentity(settings.serverId, _token))) return null;
        if (_game != null && (!ReferenceEquals(_game, game) || !ReferenceEquals(_world, game.GameWorld))) return null;
        _game = game; _world = game.GameWorld; _phase = Phase.Loading;
        return new LoadingLease { Game = game, World = game.GameWorld, Token = _token };
    }

    internal static void LoadingFailed(LoadingLease lease, Exception failure)
    {
        if (lease == null || !ReferenceEquals(lease.Game, _game) || !ReferenceEquals(lease.World, _world)
            || lease.Token != _token || !ArenaSession.ArmedForNextRaid || ArenaSession.IsActive) return;
        // A disposed loading game may already have cleared the globals. A
        // different live game/world, however, must never be cancelled by this
        // old task's delayed exception.
        if ((Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance != null
                && !ReferenceEquals(Singleton<AbstractGame>.Instance, lease.Game))
            || (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null
                && !ReferenceEquals(Singleton<GameWorld>.Instance, lease.World))) return;
        Plugin.ModLogger.LogError("Arena area loading failed before actor release: " + failure.GetBaseException().Message);
        // Run occurs after the server's start task has settled but before its
        // client confirmation. The original token can still recover that exact
        // snapshot. Never issue this cancellation for an active/newer game.
        ArenaSession.ClearArmed("bounded area scene load failed");
        ArenaMenuSelection.ClearCompatibilityState("bounded area scene load failed");
#pragma warning disable CS0618
        InGameStatus.InRaid = false;
#pragma warning restore CS0618
    }

    private static bool IsCurrentLoading()
        => _area != null && _game != null && _world != null && _phase == Phase.Loading
           && ArenaSession.ArmedForNextRaid && ArenaSession.ArmedMode == ArenaModeCatalog.TeamDeathmatchId
           && _game.Status == GameStatus.Running
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ReferenceEquals(_game.GameWorld, _world);

    internal static SpawnPointsCollection FilterCollection(SpawnPointsCollection collection)
    {
        if (_area == null) return collection;
        bool loading = BindLoadingGame();
        if (!loading && !IsCurrentActive()) return collection;
        if (collection?._dictionary == null)
            throw new InvalidOperationException("Arena area cannot inspect the native spawn collection.");
        var allowed = collection._dictionary.Where(pair => pair.Key != null
            && AllowsGeometrySpawn(pair.Key.Position)
            && !ArenaBotSpawnPolicy.IsExcludedPoint(_area.MapId, pair.Key.Id))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (loading)
        {
            Candidates.Clear();
            Candidates.AddRange(allowed.Keys);
        }
        // No InvalidSpawnPoint/world-origin or unrestricted engine fallback.
        if (!allowed.Keys.Any(IsPlayerPoint))
        {
            _failure = "the loaded scene has no allowed player spawn for " + _area.Id;
            _phase = Phase.Failed;
            Plugin.ModLogger.LogError("Arena area load rejected: " + _failure);
            throw new InvalidOperationException(_failure);
        }
        Plugin.ModLogger.LogInfo($"Arena area scene spawns: {allowed.Count}/{collection.Count} markers inside {_area.Id}.");
        return new SpawnPointsCollection(allowed, collection._logger);
    }

    internal static bool Begin(GameWorld world, LocalGame game)
    {
        if (_area == null) return true;
        if (!ReferenceEquals(_world, world) || !ReferenceEquals(_game, game)
            || !IsCurrentActive() || _failure != null || Candidates.Count == 0)
        {
            Abort("the bounded area's exact loading game or spawn collection was not verified");
            return false;
        }
        try
        {
            if (!ArenaAreaNavigationPatches.IsReady || !ArenaAreaLoadPatch.IsReady || !ArenaAreaSpawnPointsPatch.IsReady)
                throw new InvalidOperationException("native area hooks are unavailable");
            _phase = Phase.Preparing;
            Hud.ArenaInputBlocker.Request(InputOwner, true); // Escape/release commands still pass.
            _root = new GameObject("ArenaMode.Area." + _area.Id);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root, world.gameObject.scene);
            BuildEnclosure();
            Physics.SyncTransforms();
            _createdFrame = Time.frameCount;
            _deadline = Time.realtimeSinceStartup + 12f;
            _nextValidation = Time.realtimeSinceStartup;
            Plugin.ModLogger.LogInfo("Arena area preparing: physical enclosure created; waiting for verified navigation.");
            return true;
        }
        catch (Exception ex)
        {
            Abort("enclosure preparation failed: " + ex.GetBaseException().Message);
            return false;
        }
    }

    internal static void Tick()
    {
        if (_area == null) return;
        if (!ReferenceEquals(_game, null) && (_game == null || _world == null
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            || _game.Status == GameStatus.Stopping || _game.Status == GameStatus.Stopped))
        { Reset(); return; }
        if (!IsCurrentActive() || _phase == Phase.Failed) return;
        try
        {
            if (_phase == Phase.Preparing)
            {
                if (Time.frameCount <= _createdFrame + 2 || Time.realtimeSinceStartup < _nextValidation) return;
                _nextValidation = Time.realtimeSinceStartup + 0.5f;
                if (TryValidateScene(out string problem))
                {
                    _phase = Phase.Ready;
                    Hud.ArenaInputBlocker.Request(InputOwner, false);
                    Plugin.ModLogger.LogInfo($"Arena area READY: {_area.Id}; connected player={Connected.Count(IsPlayerPoint)}, "
                        + $"bot={Connected.Count(IsBotPoint)}; boundaries and paths verified, fixed {_area.MaxTeamSize}v{_area.MaxTeamSize}.");
                    ArenaToasts.Show(_area.DisplayName + " — area ready");
                }
                else if (Time.realtimeSinceStartup >= _deadline)
                    Abort("navigation validation timed out: " + problem);
                return;
            }
            if (_phase == Phase.Ready && Time.realtimeSinceStartup >= _nextActorCheck)
            {
                _nextActorCheck = Time.realtimeSinceStartup + 0.2f;
                if (!EnclosureIntact()) { Abort("a boundary collider or navigation obstacle was disabled or removed"); return; }
                CheckActors();
            }
        }
        catch (Exception ex) { Abort("area validation failed: " + ex.GetBaseException().Message); }
    }

    private static bool TryValidateScene(out string problem)
    {
        problem = null;
        if (!EnclosureIntact())
        { problem = "a boundary collider or navigation obstacle is missing"; return false; }
        var local = _world.MainPlayer;
        if (local == null || !AllowsGeometrySpawn(local.Position)
            || !TrySample(local.Position, out _anchor))
        { problem = "the initial player spawn is not on the allowed ground-level navigation surface"; return false; }
        if (!CarvingSealed())
        { problem = "navigation still crosses an enclosing wall"; return false; }
        Connected.Clear();
        foreach (var point in Candidates)
            if (point != null && AllowsGeometrySpawn(point.Position)
                && TrySample(point.Position, out var sample) && CompleteInsidePath(_anchor, sample))
                Connected.Add(point);
        var players = Connected.Where(IsPlayerPoint).ToArray();
        var bots = Connected.Where(IsBotPoint).Select(p => p.Position).Distinct().ToArray();
        int requiredBots = _area.MaxTeamSize * 2 - 1;
        if (players.Length < 2 || bots.Length < requiredBots)
        { problem = $"insufficient connected spawns (player {players.Length}, bot {bots.Length}; need 2/{requiredBots})"; return false; }
        // Require a real alternative away from each starting player cluster.
        foreach (var point in players)
        {
            if (!players.Any(p => Vector3.Distance(p.Position, point.Position) >= 40f)
                || bots.Count(p => Vector3.Distance(p, point.Position) >= BurstSpawnPointSelector.MinSpawnDistance) < requiredBots)
            { problem = "a connected player spawn lacks separated respawn/bot choices"; return false; }
        }
        return true;
    }

    private static bool EnclosureIntact()
        => _root != null && _root.activeInHierarchy && Barriers.Count == 6 && Obstacles.Count == 6
           && Barriers.All(c => c != null && c.enabled && c.gameObject.activeInHierarchy)
           && Obstacles.All(o => o != null && o.enabled && o.carving && o.gameObject.activeInHierarchy);

    private static bool CarvingSealed()
    {
        var b = _area.Bounds;
        // Direct crossing probes distinguish delayed carving from two frames
        // merely having elapsed. Missing floor outside a wall is already closed.
        for (float x = b.MinX + 2; x < b.MaxX; x += 4)
            if (Crosses(new Vector3(x, _anchor.y, b.MinZ + 1), Vector3.back)
                || Crosses(new Vector3(x, _anchor.y, b.MaxZ - 1), Vector3.forward)) return false;
        for (float z = b.MinZ + 2; z < b.MaxZ; z += 4)
            if (Crosses(new Vector3(b.MinX + 1, _anchor.y, z), Vector3.left)
                || Crosses(new Vector3(b.MaxX - 1, _anchor.y, z), Vector3.right)) return false;
        return true;
    }

    private static bool Crosses(Vector3 inside, Vector3 outward)
        => NavMesh.SamplePosition(inside, out var a, 0.5f, NavMesh.AllAreas)
           && NavMesh.SamplePosition(inside + outward * 2f, out var b, 0.5f, NavMesh.AllAreas)
           && !NavMesh.Raycast(a.position, b.position, out _, NavMesh.AllAreas);

    private static bool TrySample(Vector3 point, out Vector3 result)
    {
        result = default;
        if (!NavMesh.SamplePosition(point, out var hit, 0.75f, NavMesh.AllAreas)
            || Mathf.Abs(hit.position.y - point.y) > 0.5f || !Contains(hit.position, ArenaAreaCatalog.NavigationInset)) return false;
        result = hit.position;
        return true;
    }

    private static bool CompleteInsidePath(Vector3 origin, Vector3 target)
    {
        if (!Contains(origin, ArenaAreaCatalog.NavigationInset) || !Contains(target, ArenaAreaCatalog.NavigationInset)) return false;
        if ((origin - target).sqrMagnitude < 0.01f) return true;
        return NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, ScratchPath)
            && ScratchPath.status == NavMeshPathStatus.PathComplete && InsideCorners(origin, ScratchPath.corners);
    }

    private static bool InsideCorners(Vector3 origin, IReadOnlyList<Vector3> corners)
        => _area != null && _area.Bounds.ContainsPath(ToPoint(origin), corners, true, ToPoint,
            ArenaAreaCatalog.NavigationInset);

    private static bool Contains(Vector3 p, float inset = 0f) => _area != null && _area.Bounds.Contains(p.x, p.y, p.z, inset);
    private static bool AllowsGeometrySpawn(Vector3 p) => _area != null && _area.AllowsSpawn(p.x, p.y, p.z);
    // BaseLocalGame.Create publishes Profile before Run constructs scene spawns;
    // MainPlayer does not exist yet at that first filter. Use that same profile
    // side again for readiness and respawns, including after body replacement.
    private static bool IsPlayerPoint(ISpawnPoint p)
        => ArenaAreaSpawnPolicy.IsPlayerPoint(p, _game?.Profile?.Info?.Side);
    private static bool IsBotPoint(ISpawnPoint p) => p != null && !string.IsNullOrEmpty(p.BotZoneName)
        && ((p.Categories & ESpawnCategoryMask.Bot) != 0 || p.Categories.ContainBotPmcCategory());

    internal static bool AllowsSpawn(ISpawnPoint point)
    {
        if (!IsCurrentActive()) return true;
        return IsReady && point != null && AllowsGeometrySpawn(point.Position)
            && Connected.Any(p => p.Id == point.Id && (p.Position - point.Position).sqrMagnitude < 0.01f)
            && TrySample(point.Position, out var sample) && CompleteInsidePath(_anchor, sample);
    }

    internal static bool ShouldBlockLoadingBotDispatch()
        => _area != null && !IsReady && (IsCurrentLoading() || IsCurrentActive());

    internal static (Vector3 position, Quaternion rotation) PickRespawn(Vector3 avoid, float requestedDistance)
    {
        if (!IsReady) throw new InvalidOperationException("Bounded Arena is not ready to release a respawn.");
        var all = Connected.Where(p => IsPlayerPoint(p) && !p.SpawnBlocked && AllowsSpawn(p)).ToList();
        if (all.Count == 0) throw new InvalidOperationException("Bounded Arena has no verified player respawn; no outside fallback is permitted.");
        var distant = all.Where(p => Vector3.Distance(p.Position, avoid) >= requestedDistance).ToList();
        var selected = (distant.Count > 0 ? distant : all);
        var point = selected[UnityEngine.Random.Range(0, selected.Count)];
        return (point.Position, point.Rotation);
    }

    internal static void RequirePosition(Vector3 position)
    {
        if (!IsCurrentActive()) return;
        if (!IsReady || !Contains(position, ArenaAreaCatalog.NavigationInset)
            || !TrySampleBody(position, out var sample) || !CompleteInsidePath(_anchor, sample))
            throw new InvalidOperationException("Bounded Arena replacement position is outside its verified navigation component.");
    }

    private static bool TrySampleBody(Vector3 position, out Vector3 sample)
    {
        if (TrySample(position, out sample)) return true;
        // The initial in-place kit swap may finish during a legitimate jump.
        // Accept the same horizontal position over a verified floor, never a
        // nearby room/floor or an arbitrary radius-based outside fallback.
        if (!NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas)
            || hit.position.y > position.y + 0.2f || position.y - hit.position.y > 3f
            || new Vector2(hit.position.x - position.x, hit.position.z - position.z).sqrMagnitude > 0.09f
            || !Contains(hit.position, ArenaAreaCatalog.NavigationInset)) return false;
        sample = hit.position;
        return true;
    }

    internal static bool TryGetForPlayer(Player player, out ArenaAreaDefinition area)
    {
        area = null;
        if (!IsCurrentActive() || player == null || player.HealthController?.IsAlive != true) return false;
        if (!player.IsAI)
        {
            if (player.ProfileId != _game.ProfileId || (!ReferenceEquals(player, _world.MainPlayer)
                && !ReferenceEquals(player, _game.LocalPlayer))) return false;
        }
        else
        {
            var bot = player.AIData?.BotOwner;
            if (bot == null || !ReferenceEquals(bot.GetPlayer, player)
                || !ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)
                || (bot.Profile?.Info?.Settings?.Role != WildSpawnType.pmcUSEC
                    && bot.Profile?.Info?.Settings?.Role != WildSpawnType.pmcBEAR)) return false;
        }
        area = _area;
        return true;
    }

    internal static bool TryGetForBot(BotOwner bot, out ArenaAreaDefinition area)
        => TryGetForPlayer(bot?.GetPlayer, out area) && bot.GetPlayer.IsAI
           && ReferenceEquals(bot.GetPlayer.AIData?.BotOwner, bot);

    internal static bool AllowDestination(BotOwner bot, Vector3 target)
        => !TryGetForBot(bot, out _) || (IsReady && Contains(target, ArenaAreaCatalog.NavigationInset));

    internal static bool AllowPath(BotOwner bot, IReadOnlyList<Vector3> corners)
        => !TryGetForBot(bot, out _) || (IsReady && InsideCorners(bot.Position, corners));

    internal static bool AllowTeleport(Player player, Vector3 target)
        => !TryGetForPlayer(player, out _) || (IsReady && TrySample(target, out var sample)
            && Contains(target, ArenaAreaCatalog.NavigationInset) && CompleteInsidePath(_anchor, sample));

    private static ActorState State(Player player)
    {
        if (!Actors.TryGetValue(player, out var state))
        {
            state = new ActorState { WindowStart = Time.realtimeSinceStartup, WindowPosition = player.Position,
                WindowScore = CurrentKills() };
            Actors.Add(player, state);
        }
        return state;
    }

    internal static long GetRejectionVersion(BotOwner bot)
        => TryGetForBot(bot, out _) ? State(bot.GetPlayer).Rejections : 0;

    internal static void NoteRejected(BotOwner bot, string reason)
    {
        if (!TryGetForBot(bot, out _)) return;
        var state = State(bot.GetPlayer);
        state.Rejections++;
        state.WindowRejections++;
        _totalRejections++;
        if (_totalRejections <= 6 || Time.realtimeSinceStartup >= _nextRejectionLog)
        {
            _nextRejectionLog = Time.realtimeSinceStartup + 10f;
            Plugin.ModLogger.LogInfo($"Arena area rejected movement #{_totalRejections}: bot={bot.ProfileId}, {reason}.");
        }
    }

    internal static void NoteRejectedTeleport(Player player, string reason)
    {
        if (!TryGetForPlayer(player, out _)) return;
        if (player.IsAI) NoteRejected(player.AIData.BotOwner, reason);
        else Plugin.ModLogger.LogWarning("Arena area blocked player teleport: " + reason);
    }

    private static void CheckActors()
    {
        int score = CurrentKills();
        foreach (var retired in Actors.Keys.Where(p => p == null || p.HealthController?.IsAlive != true
            || !TryGetForPlayer(p, out _)).ToArray()) Actors.Remove(retired);
        var bodies = _world.AllAlivePlayersList.OfType<Player>().ToArray();
        foreach (var player in bodies)
        {
            if (!TryGetForPlayer(player, out _)) continue;
            var state = State(player);
            var p = player.Position;
            if (player.IsAI) CheckCurrentPaths(player.AIData.BotOwner);
            if (!Contains(p))
            {
                // No unbounded every-frame teleport loop. Last known inner
                // position belongs to this exact physical body, never an old ID.
                if (!state.HasInside || ++state.Recoveries > 2 || !AllowTeleport(player, state.LastInside))
                { Abort("actor escaped the area without a safe bounded recovery: " + player.ProfileId); return; }
                if (player.IsAI) { player.AIData.BotOwner.Mover.Stop(); player.AIData.BotOwner.Mover.Teleport(state.LastInside); }
                else player.Teleport(state.LastInside);
                Plugin.ModLogger.LogWarning($"Arena area recovered displaced actor {player.ProfileId}, attempt {state.Recoveries}/2.");
                if (!Contains(player.Position)) { Abort("actor recovery did not restore the physical position"); return; }
            }
            else if (Contains(p, 1f) && TrySample(p, out var sample) && (sample - p).sqrMagnitude < 0.25f)
            {
                state.LastInside = p;
                state.HasInside = true;
            }
            if (Time.realtimeSinceStartup - state.WindowStart >= 20f)
            {
                // A stationary bot can still be fighting. Stop only after three
                // consecutive rejected/stationary windows with no match kills,
                // not merely after an ordinary 20-second exchange from cover.
                bool stalled = player.IsAI && state.WindowRejections >= 20
                    && (p - state.WindowPosition).sqrMagnitude < 1f && score == state.WindowScore;
                state.StalledWindows = stalled ? state.StalledWindows + 1 : 0;
                if (state.StalledWindows >= 3)
                { Abort("bot repeatedly requested forbidden paths without moving or match progress for 60 seconds: " + player.ProfileId); return; }
                if (stalled)
                    Plugin.ModLogger.LogInfo($"Arena area movement watch: bot={player.ProfileId}, stationary/rejected window {state.StalledWindows}/3.");
                state.WindowStart = Time.realtimeSinceStartup;
                state.WindowPosition = p;
                state.WindowScore = score;
                state.WindowRejections = 0;
            }
        }
    }

    private static int CurrentKills()
    {
        var board = ArenaMatchService.Current?.Scoreboard;
        return board == null ? 0 : board.Totals(ArenaScoreboardSide.Friendly).Kills
            + board.Totals(ArenaScoreboardSide.Opponent).Kills;
    }

    private static void CheckCurrentPaths(BotOwner bot)
    {
        var machine = bot.Mover?._moverStateMachine;
        if (machine?._states == null) return;
        foreach (var state in machine._states.Values)
        {
            var controller = state?.PathController;
            var path = controller?.CurPath;
            if (path == null || AllowPath(bot, path._path)) continue;
            // Also catch another mod modifying a previously accepted path array
            // in place, without calling any of the patched path setters.
            NoteRejected(bot, "current path changed outside the area");
            controller.SetCurPath(null);
            if (controller.CurPath == null && ReferenceEquals(machine.CurrentMoverState?.PathController, controller))
                bot.Mover.IsMoving = false;
        }
    }

    internal static void Abort(string reason)
    {
        if (_phase == Phase.Failed && _failure != null) return;
        _failure = reason;
        _phase = Phase.Failed;
        Plugin.ModLogger.LogError("Arena area stopped: " + reason);
        Hud.ArenaInputBlocker.Request(InputOwner, false);
        if (IsCurrentActive()) ArenaMatchService.ScheduleFailedStartAbort("bounded area: " + reason);
    }

    private static void BuildEnclosure()
    {
        int layer = LayerMask.NameToLayer("LowPolyCollider");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (layer < 0 || playerLayer < 0 || Physics.GetIgnoreLayerCollision(layer, playerLayer))
            throw new InvalidOperationException("the existing Player/LowPolyCollider collision matrix does not block bodies");
        var b = _area.Bounds;
        float x = (b.MinX + b.MaxX) / 2, y = (b.MinY + b.MaxY) / 2, z = (b.MinZ + b.MaxZ) / 2;
        float sx = b.MaxX - b.MinX, sy = b.MaxY - b.MinY, sz = b.MaxZ - b.MinZ;
        const float t = 0.6f;
        AddBarrier("west", new Vector3(b.MinX - t / 2, y, z), new Vector3(t, sy + t * 2, sz + t * 2), layer);
        AddBarrier("east", new Vector3(b.MaxX + t / 2, y, z), new Vector3(t, sy + t * 2, sz + t * 2), layer);
        AddBarrier("south", new Vector3(x, y, b.MinZ - t / 2), new Vector3(sx + t * 2, sy + t * 2, t), layer);
        AddBarrier("north", new Vector3(x, y, b.MaxZ + t / 2), new Vector3(sx + t * 2, sy + t * 2, t), layer);
        AddBarrier("bottom", new Vector3(x, b.MinY - t / 2, z), new Vector3(sx + t * 2, t, sz + t * 2), layer);
        AddBarrier("top", new Vector3(x, b.MaxY + t / 2, z), new Vector3(sx + t * 2, t, sz + t * 2), layer);
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) throw new InvalidOperationException("Sprites/Default boundary shader is unavailable");
        _material = new Material(shader) { name = "ArenaMode.AreaGrid", color = new Color(0.15f, 0.85f, 1f, 0.42f) };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        // Thin double-sided ribbons make the grid visible in perspective while
        // respecting ordinary depth occlusion. One mesh/material, not hundreds
        // of independent line renderers or borrowed pooled map materials.
        AddGrid(vertices, triangles, new Vector3(b.MinX, b.MinY, b.MinZ), Vector3.right * sx, Vector3.up * sy);
        AddGrid(vertices, triangles, new Vector3(b.MinX, b.MinY, b.MaxZ), Vector3.right * sx, Vector3.up * sy);
        AddGrid(vertices, triangles, new Vector3(b.MinX, b.MinY, b.MinZ), Vector3.forward * sz, Vector3.up * sy);
        AddGrid(vertices, triangles, new Vector3(b.MaxX, b.MinY, b.MinZ), Vector3.forward * sz, Vector3.up * sy);
        AddGrid(vertices, triangles, new Vector3(b.MinX, b.MinY + 0.01f, b.MinZ), Vector3.right * sx, Vector3.forward * sz);
        AddGrid(vertices, triangles, new Vector3(b.MinX, b.MaxY, b.MinZ), Vector3.right * sx, Vector3.forward * sz);
        _mesh = new Mesh { name = "ArenaMode.AreaGrid" };
        _mesh.SetVertices(vertices); _mesh.SetTriangles(triangles, 0);
        _mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), vertices.Count).ToArray();
        _mesh.RecalculateBounds();
        var visual = new GameObject("Visible boundary");
        visual.transform.SetParent(_root.transform, false);
        visual.transform.position = Vector3.zero;
        visual.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void AddBarrier(string name, Vector3 center, Vector3 size, int layer)
    {
        var go = new GameObject("Arena boundary " + name) { layer = layer };
        go.transform.SetParent(_root.transform, false); go.transform.position = center;
        var collider = go.AddComponent<BoxCollider>(); collider.size = size;
        Barriers.Add(collider);
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = size;
        obstacle.carveOnlyStationary = true; obstacle.carvingTimeToStationary = 0f; obstacle.carving = true;
        Obstacles.Add(obstacle);
    }

    private static void AddGrid(List<Vector3> vertices, List<int> triangles, Vector3 origin, Vector3 u, Vector3 v)
    {
        const float spacing = 3f, width = 0.055f;
        int nu = Mathf.CeilToInt(u.magnitude / spacing), nv = Mathf.CeilToInt(v.magnitude / spacing);
        for (int i = 0; i <= nu; i++) Ribbon(vertices, triangles, origin + u * i / nu, v, u.normalized * width);
        for (int i = 0; i <= nv; i++) Ribbon(vertices, triangles, origin + v * i / nv, u, v.normalized * width);
    }

    private static void Ribbon(List<Vector3> vertices, List<int> indices, Vector3 start, Vector3 direction, Vector3 width)
    {
        int n = vertices.Count;
        vertices.Add(start - width / 2); vertices.Add(start + width / 2);
        vertices.Add(start + direction + width / 2); vertices.Add(start + direction - width / 2);
        indices.Add(n); indices.Add(n + 1); indices.Add(n + 2); indices.Add(n); indices.Add(n + 2); indices.Add(n + 3);
    }

    internal static void Reset()
    {
        Hud.ArenaInputBlocker.Request(InputOwner, false);
        // Disable synchronously; Unity's deferred Destroy must not leave a wall
        // or carver participating in the next frame/raid.
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        _root = null; _material = null; _mesh = null;
        _area = null; _token = null; _game = null; _world = null; _failure = null;
        Candidates.Clear(); Connected.Clear(); Actors.Clear(); Barriers.Clear(); Obstacles.Clear();
        _phase = Phase.Armed; _totalRejections = 0; _nextActorCheck = 0; _nextRejectionLog = 0;
    }
}
