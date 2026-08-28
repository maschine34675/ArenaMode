using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Per-frame movement work that patches cannot express (ticked by ArenaController):
/// stamina top-off (legs + arms + breath) and the state-object inertia zeroing —
/// the inertia GETTERS alone do not kill the smoothing already latched into the
/// running MovePlayerState (FieldKit finding).
/// </summary>
internal static class MovementRuntime
{
    // NOTE: the FieldKit-style per-frame MovePlayerState direction snap
    // (InertiaDirection/_velocity2/smoothMovementDirectionTime zeroing) was removed:
    // it made walking strafes flip with zero animation blend — the "choppy" feel
    // from the first M4 playtest. The inertia getter patches carry the feature.

    public static void Tick()
    {
        SprintAdsPatches.ClearBypassSafety();

        if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || !Singleton<GameWorld>.Instantiated)
            return;

        var player = Singleton<GameWorld>.Instance.MainPlayer;
        if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            return;

        try
        {
            // Slide controller lives on the player object and dies with each respawn —
            // (re)attach lazily (absorbed CombatSlide behavior, arena-only now).
            if (Settings.SlideEnabled.Value
                && player.MovementContext != null
                && player.GetComponent<ArenaSlideController>() == null)
            {
                var slide = player.gameObject.AddComponent<ArenaSlideController>();
                slide.Init(player, player.MovementContext);
            }

            if (Settings.InfiniteStamina.Value && player.Physical != null)
            {
                player.Physical.Stamina.Current = player.Physical.Stamina.TotalCapacity;
                player.Physical.HandsStamina.Current = player.Physical.HandsStamina.TotalCapacity;
                player.Physical.Oxygen.Current = player.Physical.Oxygen.TotalCapacity;
            }

        }
        catch (Exception)
        {
            // Fail open — one bad frame must never break movement.
        }
    }
}
