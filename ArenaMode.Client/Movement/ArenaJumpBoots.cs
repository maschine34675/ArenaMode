using ArenaMode.Pickups;
using EFT;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Impulse-only bonus: never changes shared physics or an airborne trajectory.</summary>
internal static class ArenaJumpBoots
{
    private sealed class Applied { internal float Factor; }
    private static readonly ConditionalWeakTable<JumpPlayerState, Applied> Entries = new();

    internal static float LiftMultiplier(MovementContext context)
        => ArenaMovementScope.TryGetPlayer(context, out var player)
            && ArenaPowerups.Read(player).JumpSeconds > 0f
                ? Mathf.Sqrt(ArenaPowerupState.JumpHeightMultiplier) : 1f;

    // Called after the existing Arena height multiplier, before DoubleJump caches
    // this entry. Record the factor actually applied, not the later buff state.
    internal static void ApplyEntry(JumpPlayerState jump)
    {
        if (jump == null) return;
        Entries.Remove(jump);
        float factor = LiftMultiplier(jump.MovementContext);
        if (factor <= 1f) return;
        Entries.Add(jump, new Applied { Factor = factor });
        jump._liftForce *= factor;
    }

    internal static Vector3 WithoutEntryBonus(JumpPlayerState jump)
        => jump._liftForce * (Entries.TryGetValue(jump, out var applied) ? 1f / applied.Factor : 1f);
}
