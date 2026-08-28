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
    private static readonly FieldInfo RunDirectionBlendVelocityField =
        AccessTools.Field(typeof(MovePlayerState), "RunDirectionBlendVelocity");

    private static readonly FieldInfo RunDiscreteDirectionDelayField =
        AccessTools.Field(typeof(MovePlayerState), "RunDiscreteDirectionDelay");

    public static void Tick()
    {
        if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || !Singleton<GameWorld>.Instantiated)
            return;

        var player = Singleton<GameWorld>.Instance.MainPlayer;
        if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            return;

        try
        {
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
                RunDirectionBlendVelocityField?.SetValue(state, Vector2.zero);
                RunDiscreteDirectionDelayField?.SetValue(state, 0f);
            }
        }
        catch (Exception)
        {
            // Fail open — one bad frame must never break movement.
        }
    }
}
