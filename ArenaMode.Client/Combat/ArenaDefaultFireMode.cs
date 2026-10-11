using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Engine;
using System;

namespace ArenaMode.Combat;

/// <summary>
/// Arena-only counterpart to the author's standalone DefaultAutoFireMode.
/// Apply at draw completion, never intercept the player's fire-mode selection.
/// Initial hands can finish drawing before Arena's server-confirmed activation;
/// Tick catches that one published controller, including a replacement body.
/// </summary>
internal static class ArenaDefaultFireMode
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".DefaultFireMode");
    private static bool _ready, _warned;
    private static int _epoch = -1;
    private static Player.FirearmController _observed;
    private static Player _body;

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(Player.FirearmController.SpawnOperation),
                nameof(Player.FirearmController.SpawnOperation.WeaponAppeared), Type.EmptyTypes);
            if (target == null || target.ReturnType != typeof(void))
                throw new MissingMethodException("FirearmController.SpawnOperation.WeaponAppeared");
            Patches.Patch(target, postfix: new HarmonyMethod(typeof(ArenaDefaultFireMode), nameof(AfterDraw)));
            RaidLifecycle.OnRaidEnded += Reset;
            _ready = true;
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            Plugin.ModLogger.LogWarning("Arena default fire mode unavailable: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        if (_ready) RaidLifecycle.OnRaidEnded -= Reset;
        _ready = false;
        Patches.UnpatchSelf();
        Reset();
    }

    private static void Reset()
    {
        _observed = null;
        _body = null;
        _epoch = -1;
        _warned = false;
    }

    private static bool Current(out Player.FirearmController hands)
    {
        hands = null;
        if (!_ready || !ArenaSession.IsActive || ArenaMatchService.TeamStartAbortPending
            || Areas.ArenaAreaRuntime.IsPending
            || ArenaRespawnDriver.StandDown || ArenaRespawnDriver.IsRespawnInProgress
            || ArenaSpectatorService.IsActive || !Singleton<GameWorld>.Instantiated
            || !Singleton<AbstractGame>.Instantiated) return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        var player = world?.MainPlayer;
        if (game == null || game.Status != GameStatus.Started
            || !ArenaSession.IsExactActiveRaid(game, world)
            || player == null || player.IsAI || !player.IsYourPlayer
            || !ReferenceEquals(game.LocalPlayer, player)
            || player.HealthController?.IsAlive != true
            || player.HandsController is not Player.FirearmController candidate
            || !ReferenceEquals(candidate._player, player)
            || candidate.FirearmsAnimator == null
            || candidate.CurrentOperation is not Player.FirearmController.Idling) return false;
        hands = candidate;
        return true;
    }

    internal static void Tick()
    {
        if (!_ready || !ArenaSession.IsActive)
        {
            Reset();
            return;
        }
        try
        {
            if (!Current(out var hands)) return;
            if (_epoch == ArenaSession.ArmEpoch && ReferenceEquals(_body, hands._player)
                && ReferenceEquals(_observed, hands)) return;
            Apply(hands, "current hands");
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void AfterDraw(Player.FirearmController.SpawnOperation __instance)
    {
        try
        {
            if (Current(out var hands) && ReferenceEquals(__instance?.Controller, hands))
                Apply(hands, "draw");
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Apply(Player.FirearmController hands, string source)
    {
        // Mark even unchanged/unsupported/malfunctioning weapons: changing the
        // selector or fixing a jam while held must not trigger a later override.
        _observed = hands;
        _body = hands._player;
        _epoch = ArenaSession.ArmEpoch;
        var weapon = hands.Item;
        var mode = weapon?.FireMode;
        if (weapon == null || weapon.IsStationaryWeapon || mode == null
            || weapon.MalfState.State != Weapon.EMalfunctionState.None
            || mode.AvailableEFireModes == null
            || Array.IndexOf(mode.AvailableEFireModes, Weapon.EFireMode.fullauto) < 0
            || mode.FireMode == Weapon.EFireMode.fullauto) return;

        var before = mode.FireMode;
        mode.SetFireMode(Weapon.EFireMode.fullauto);
        // SetFireMode notifies native subscribers. Never animate a retired body
        // if one of those callbacks changed hands or ended the raid.
        if (Current(out var current) && ReferenceEquals(current, hands))
            hands.FirearmsAnimator.SetFireMode(Weapon.EFireMode.fullauto, true);
        Plugin.ModLogger.LogInfo($"Arena default fire mode: {weapon.ShortName} ({weapon.Id}) {before} -> fullauto ({source}).");
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena default fire mode skipped: " + ex.GetBaseException().Message);
    }
}
