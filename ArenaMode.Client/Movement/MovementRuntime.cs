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
    // The SmoothDamp velocity of the direction blend (the FieldKit reference targets
    // field names that no longer exist in 4.1 — _velocity2 is the live one).
    private static readonly AccessTools.FieldRef<MovePlayerState, Vector2> DirectionBlendVelocityField =
        AccessTools.FieldRefAccess<MovePlayerState, Vector2>("_velocity2");

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

            if (Settings.NoInertia.Value
                && player.MovementContext?.CurrentState is MovePlayerState state)
            {
                state.InertiaDirection = state.LastDirectionInput;
                state.LastNonZeroDirectionInput = state.LastDirectionInput;
                state.smoothMovementDirectionTime = 0f;
                state.smoothMovementDirectionDuration = 0f;
                state.TransitionCoef = 1f;
                DirectionBlendVelocityField(state) = Vector2.zero;
            }
        }
        catch (Exception)
        {
            // Fail open — one bad frame must never break movement.
        }
    }
}
