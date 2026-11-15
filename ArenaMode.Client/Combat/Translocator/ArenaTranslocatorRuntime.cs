#nullable disable
using System;
using ArenaMode.Areas;
using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ArenaMode.Combat.Translocator;

/// <summary>Only the exact live local body owns a disc; switching hands is not a new owner.</summary>
internal static class ArenaTranslocatorRuntime
{
    internal const float DestinationLift = .10f;
    private static ArenaMatch _match;
    private static GameWorld _world;
    private static Player _owner;
    private static Item _device;
    private static ArenaTranslocatorDisc _disc;
    private static ArenaTranslocatorView _view;
    private static int _round;
    private static bool _acting, _warned;
    private static float _nextAction, _nextSupport, _statusUntil;
    private static string _status;
    // Read-only diagnostics; gameplay never depends on diagnostic settings.
    internal static int Launches { get; private set; }
    internal static int Recalls { get; private set; }
    internal static int Teleports { get; private set; }
    internal static int Rejections { get; private set; }
    internal static bool DiscDeployed => _disc != null && _disc.State != ArenaTranslocatorDisc.Flight.Lost;
    internal static Vector3 Destination => _disc == null ? Vector3.zero
        : ArenaTranslocatorGeometry.Unity(_disc.Position) + Vector3.up * DestinationLift;

    private static bool Context(out ArenaMatch match, out GameWorld world)
    {
        match = ArenaMatchService.Current;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        return ArenaSession.IsActive && ArenaTranslocatorSettings.Enabled?.Value == true
            && match?.Mode != null && !match.Mode.IsComplete && world != null && game != null
            && game.Status == GameStatus.Started && ArenaSession.IsExactActiveRaid(game, world)
            && ReferenceEquals(game.GameWorld, world) && !ArenaMatchService.TeamStartAbortPending
            && !ArenaRespawnDriver.StandDown && !ArenaAreaRuntime.IsPending
            && (match.Mode switch
            {
                RoundsMode mode => mode.AssistScoringOpen,
                TeamDeathmatchMode mode => mode.AssistScoringOpen,
                TacticalMode mode => mode.AssistScoringOpen,
                GunGameMode mode => mode.HasActiveLadder,
                ZombieMode mode => mode.IsRecoveryAdmissionOpen,
                _ => false
            });
    }

    private static int Round(ArenaMatch match) => match.Mode switch
    { RoundsMode mode => mode.Round.Number, TacticalMode mode => mode.RoundNumber, _ => 1 };

    internal static bool IsLivePlayer(Player player)
    {
        if (!Context(out var match, out var world) || player == null || player.IsAI || !player.IsYourPlayer
            || match.Mode is GunGameMode gunGame && !gunGame.AssistScoringOpen
            || player.HealthController?.IsAlive != true || player.MovementContext == null
            || ArenaRespawnDriver.IsRespawnInProgress || ArenaSpectatorService.IsActive
            || Time.timeScale <= 0 || player.IsInventoryOpened || ArenaInputBlocker.Blocked) return false;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        return ReferenceEquals(world.MainPlayer, player) && ReferenceEquals(game?.LocalPlayer, player)
            && ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player);
    }

    private static bool Held(Player player, Item item)
        => item != null && player?.HandsController is Player.ItemHandsController hands
            && ReferenceEquals(hands.Item, item) && ArenaTranslocatorSupply.Owns(player, item)
            && !WeaponSwapService.IsSwapping && player.InventoryController?.IsChangingWeapon != true;

    internal static bool IsOwnedDestination(Player player, Vector3 target)
        => IsLivePlayer(player) && ReferenceEquals(_owner, player) && Held(player, _device)
            && Context(out var match, out var world) && ReferenceEquals(match, _match)
            && ReferenceEquals(world, _world) && _round == Round(match) && DiscDeployed
            && (Destination - target).sqrMagnitude <= .0001f
            && ArenaTranslocatorGeometry.Inside(ArenaTranslocatorGeometry.Unity(_disc.Position), target)
            && ArenaTranslocatorGeometry.Empty(ArenaTranslocatorGeometry.Unity(_disc.Position), player);

    internal static void Tick()
    {
        try
        {
            var availableWorld = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            ArenaTranslocatorSupply.ForgetOutsideRaid(availableWorld);
            if (!Context(out var match, out var world)) { Reset(); return; }
            var player = world.MainPlayer;
            if (!ReferenceEquals(_match, match) || !ReferenceEquals(_world, world) || _round != Round(match))
            {
                Reset(); _match = match; _world = world; _round = Round(match); _warned = false;
                Launches = Recalls = Teleports = Rejections = 0;
            }
            if (_disc != null && (_owner == null || !ReferenceEquals(_owner, player)
                || _owner.HealthController?.IsAlive != true || !ArenaTranslocatorSupply.Owns(_owner, _device)
                || ArenaRespawnDriver.IsRespawnInProgress || ArenaSpectatorService.IsActive)) ClearDisc();
            if (Time.timeScale <= 0f) return;
            if (IsLivePlayer(player)) ArenaTranslocatorSupply.Tick(world, player);
            if (_disc != null)
            {
                if (!ArenaTranslocatorGeometry.Inside(ArenaTranslocatorGeometry.Unity(_disc.Position),
                    ArenaTranslocatorGeometry.Unity(_disc.Position))) _disc.Lose();
                if (_disc.State == ArenaTranslocatorDisc.Flight.Resting && Time.time >= _nextSupport)
                {
                    _nextSupport = Time.time + .1f;
                    if (!ArenaTranslocatorGeometry.Supported(_disc.Position, _owner)) _disc.ReleaseSupport();
                }
                _disc.Advance(Time.deltaTime,
                    (from, to) => ArenaTranslocatorGeometry.Sweep(from, to, _owner),
                    (from, to) => ArenaTranslocatorGeometry.Inside(ArenaTranslocatorGeometry.Unity(from), ArenaTranslocatorGeometry.Unity(to)));
                if (_disc.State == ArenaTranslocatorDisc.Flight.Lost)
                { ClearDisc(); Status("DISC LOST"); }
            }
            _view?.Tick(_disc);
        }
        catch (Exception ex) { ClearDisc(); Warn(ex); }
    }

    internal static void Primary(Player player, Item device)
    {
        if (_acting || !IsLivePlayer(player) || !Held(player, device) || Time.time < _nextAction) return;
        _acting = true;
        try
        {
            Tick();
            if (!IsLivePlayer(player) || !Held(player, device)) return;
            _nextAction = Time.time + .18f;
            if (_disc != null)
            {
                if (!ReferenceEquals(_owner, player) || !ReferenceEquals(_device, device)) { ClearDisc(); return; }
                ClearDisc(); Recalls++; Status("RECALLED");
                Plugin.ModLogger.LogDebug("Arena translocator: disc recalled.");
                return;
            }
            if (!ArenaTranslocatorGeometry.TryLaunch(player, out var launched)) { Reject("launch-blocked"); return; }
            _owner = player; _device = device; _disc = launched; _nextSupport = 0f;
            try { _view ??= new ArenaTranslocatorView(); _view.Tick(_disc); _view.Cue(false); }
            catch (Exception ex) { Warn(ex); } // art cannot invalidate the physics owner
            Launches++; _status = null;
            Plugin.ModLogger.LogDebug("Arena translocator: disc launched.");
        }
        catch (Exception ex) { ClearDisc(); Warn(ex); }
        finally { _acting = false; }
    }

    internal static void Secondary(Player player, Item device)
    {
        if (_acting || !IsLivePlayer(player) || !Held(player, device) || Time.time < _nextAction) return;
        _acting = true;
        try
        {
            _nextAction = Time.time + .18f;
            var disc = _disc; var world = _world; var match = _match;
            if (disc == null) { Status("THROW A DISC"); return; }
            var target = Destination;
            if (!IsOwnedDestination(player, target)) { Reject("destination-unavailable"); return; }
            if (!ArenaTranslocatorTeleport.TryTeleport(player, target, out var reason)) { Reject(reason); return; }
            // Native callbacks may end the raid or change the body. Never consume
            // a replacement owner's disc or publish a success in a later match.
            if (!ReferenceEquals(_disc, disc) || !ReferenceEquals(_world, world)
                || !ReferenceEquals(_match, match) || !ReferenceEquals(_owner, player)) return;
            ClearDisc(); Teleports++; Status("TRANSLOCATED");
            try { _view?.Pulse(target); _view?.Cue(true); }
            catch (Exception ex) { Warn(ex); }
            Plugin.ModLogger.LogDebug("Arena translocator: relocation confirmed.");
        }
        catch (Exception ex) { Warn(ex); }
        finally { _acting = false; }
    }

    private static void Reject(string reason)
    {
        Rejections++; Status(reason == "launch-blocked" ? "THROW BLOCKED" : "TARGET UNAVAILABLE");
        Plugin.ModLogger.LogDebug("Arena translocator refused: " + reason);
    }
    private static void Status(string status) { _status = status; _statusUntil = Time.time + 1.4f; }

    internal static string HeldStatus()
    {
        var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance?.MainPlayer : null;
        var item = (player?.HandsController as Player.ItemHandsController)?.Item;
        if (!IsLivePlayer(player) || !Held(player, item)) return null;
        return _status != null && Time.time < _statusUntil ? _status : DiscDeployed ? "DISC DEPLOYED" : "DISC READY";
    }

    private static void ClearDisc()
    {
        _disc = null; _owner = null; _device = null;
        try { _view?.Tick(null); }
        catch (Exception ex)
        {
            var view = _view; _view = null;
            try { view?.Dispose(); } catch { /* physical ownership is already cleared */ }
            Warn(ex);
        }
    }

    internal static void Reset()
    {
        _disc = null; _owner = null; _device = null; _match = null; _world = null; _round = 0;
        _status = null; _nextAction = _nextSupport = _statusUntil = 0;
        var view = _view; _view = null;
        try { view?.Dispose(); } catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return; _warned = true;
        Plugin.ModLogger.LogWarning("Arena translocator unavailable: " + ex);
    }
}
