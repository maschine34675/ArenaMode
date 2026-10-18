using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Widens how far the player may reach when vaulting and climbing, for the
/// duration of an arena raid.
///
/// This writes to <see cref="GlobalConfiguration"/>, which the rest of this mod
/// is under standing orders never to touch, because bots share it. The exception
/// is argued, not assumed: <c>Player.InitVaultingComponent</c> opens with
/// <c>if (aiControlled) return;</c>, so no bot ever owns a VaultingComponent, and
/// a grep of the decompiled source finds exactly three readers of these fields —
/// VaultMoveModel, ClimbMoveModel and the VaultingMovesModel constructor, all of
/// them inside that player-only subsystem. Nothing else in the game reads them.
///
/// Reach has TWO gates, and widening only the obvious one buys nothing:
///   1. <c>VaultMoveModel.CheckVaultCondition</c> reads MoveRestrictions live at
///      the moment you press the key — the verdict.
///   2. <c>VaultingMovesModel</c>'s CONSTRUCTOR snapshots the same numbers into
///      the bounds that <c>WeightCalculatorModel</c> uses to decide which points
///      are even candidate obstacles — the search.
/// A verdict on an obstacle the search never found is no verdict at all, which is
/// why ArenaMovementVaultingReachPatch applies this before the component is built rather than
/// leaving it to the first frame of the raid.
///
/// The same asymmetry is why nothing calls Apply or Restore mid-raid. The snapshot
/// cannot be taken back once the component exists, so narrowing only the live
/// limits would leave the player refusing obstacles VANILLA vaults - worse than
/// either setting. A changed multiplier lands on the next respawn, when a new
/// player and a new component are built.
/// </summary>
internal static class VaultingTuning
{
    private static bool _applied;
    private static float _appliedMult;

    // The vanilla numbers, captured on first use and never re-captured: capturing
    // again while our own values are installed would enshrine them as "vanilla".
    private static bool _captured;
    private static float _vaultMaxHeight, _vaultMaxLength, _vaultReach;
    private static float _climbMaxHeight, _climbMaxLength, _climbReach;

    public static void Apply()
    {
        var mult = Settings.VaultReachMult.Value;
        if (_applied && Mathf.Approximately(mult, _appliedMult))
            return;

        try
        {
            var settings = Singleton<GlobalConfiguration>.Instance?.VaultingSettings;
            if (settings?.MovesSettings?.VaultSettings?.MoveRestrictions == null
                || settings.MovesSettings.ClimbSettings?.MoveRestrictions == null)
                return;

            Capture(settings);

            if (mult <= 1f)
            {
                Restore();
                return;
            }

            // The search grid is a hard ceiling on HEIGHT: it is 1.8 m tall
            // (globals GridSettings.GridSizeY), and a limit raised past the grid
            // is a limit on nothing, because the obstacle never appears in the
            // point cloud to be measured. Clamping here rather than in the config
            // range keeps the promise honest if BSG reshapes the grid later.
            var maxHeight = settings.GridSettings?.GridSizeY ?? 1.8f;
            var maxDepth = settings.GridSettings?.GridSizeZ ?? 2f;

            var vault = settings.MovesSettings.VaultSettings.MoveRestrictions;
            var climb = settings.MovesSettings.ClimbSettings.MoveRestrictions;

            // Only the upper bounds and the trigger distance move. MinHeight and
            // MinLength stay where BSG put them — dropping the floor would turn
            // kerbs and door sills into vaults, which is a different mod.
            //
            // MaxLength is scaled but NOT clamped to the grid depth. Climb's
            // vanilla MaxLength is 10, far past the 2 m grid, so clamping it
            // would read as cutting the limit by five — when in truth both 10 and
            // 2 are already beyond any length the grid can measure. Scaling a
            // no-op keeps it a no-op instead of dressing one up as a tightening.
            vault.MaxHeight = Mathf.Min(_vaultMaxHeight * mult, maxHeight);
            vault.MaxLength = _vaultMaxLength * mult;
            vault.MinDistantToInteract = Mathf.Min(_vaultReach * mult, maxDepth);

            climb.MaxHeight = Mathf.Min(_climbMaxHeight * mult, maxHeight);
            climb.MaxLength = _climbMaxLength * mult;
            climb.MinDistantToInteract = Mathf.Min(_climbReach * mult, maxDepth);

            if (!_applied)
            {
                // Worth one line: raising the vault ceiling toward the climb
                // ceiling also changes WHICH move you get. GetVaultingStrategy
                // tries vault first, so obstacles that used to force the slow
                // mantle now get the fast hands-over motion instead.
                Plugin.ModLogger.LogDebug(
                    $"Arena vaulting reach x{mult:0.00}: vault to {vault.MaxHeight:0.00}m "
                    + $"(was {_vaultMaxHeight:0.00}), climb to {climb.MaxHeight:0.00}m "
                    + $"(was {_climbMaxHeight:0.00}), trigger distance {vault.MinDistantToInteract:0.00}m.");
            }

            _applied = true;
            _appliedMult = mult;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena vaulting reach could not be applied: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts BSG's numbers back. Called on every tick that finds no arena session
    /// running or armed, so a raid that ends in a crash costs at most one stale
    /// process — and the globals arrive fresh from the server on the next session.
    /// </summary>
    public static void Restore()
    {
        if (!_applied)
            return;

        if (!_captured)
            return;

        var succeeded = false;
        try
        {
            var moves = Singleton<GlobalConfiguration>.Instance?.VaultingSettings?.MovesSettings;
            var vault = moves?.VaultSettings?.MoveRestrictions;
            var climb = moves?.ClimbSettings?.MoveRestrictions;
            if (vault == null || climb == null)
                return;

            vault.MaxHeight = _vaultMaxHeight;
            vault.MaxLength = _vaultMaxLength;
            vault.MinDistantToInteract = _vaultReach;

            climb.MaxHeight = _climbMaxHeight;
            climb.MaxLength = _climbMaxLength;
            climb.MinDistantToInteract = _climbReach;
            succeeded = true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena vaulting reach could not be restored: {ex.Message}");
        }

        if (!ArenaRestoreCommit.ShouldDropRetryState(succeeded))
            return;

        _applied = false;
    }

    /// <summary>True while an arena raid wants the widened limits.</summary>
    public static bool Wanted
        => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid)
           && Settings.MovementEnabled.Value
           && Settings.VaultReachMult.Value > 1f;

    private static void Capture(GlobalConfiguration.VaultingGlobalSettings settings)
    {
        if (_captured)
            return;

        var vault = settings.MovesSettings.VaultSettings.MoveRestrictions;
        var climb = settings.MovesSettings.ClimbSettings.MoveRestrictions;

        _vaultMaxHeight = vault.MaxHeight;
        _vaultMaxLength = vault.MaxLength;
        _vaultReach = vault.MinDistantToInteract;

        _climbMaxHeight = climb.MaxHeight;
        _climbMaxLength = climb.MaxLength;
        _climbReach = climb.MinDistantToInteract;

        _captured = true;
    }
}
