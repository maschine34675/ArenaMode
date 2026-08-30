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
    // NOTE: the FieldKit-style per-frame MovePlayerState direction snap was
    // removed (choppy strafes); direction-blend responsiveness now lives in
    // StrafeBlendPatch (per-frame dial read, no re-trigger needed). Inertia VALUES
    // are written by InertiaValuesPatch on WeightRelatedValuesUpdated; the trigger
    // below runs that recompute once per raid and when NoInertia is toggled,
    // because no natural weight update fires on a config edit.

    private static bool _lastNoInertia;
    private static bool _weightValuesApplied;

    public static void Tick()
    {
        SprintAdsPatches.ClearBypassSafety();

        if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || !Singleton<GameWorld>.Instantiated)
        {
            // BSG's vaulting limits go back the moment the arena stops running.
            // They live in GlobalConfiguration, so leaving them widened would
            // follow the player into the next, ordinary raid.
            VaultingTuning.Restore();

            if (_weightValuesApplied)
            {
                _weightValuesApplied = false;
                // Bring the vanilla inertia values back immediately (e.g. the
                // MovementEnabled master switch flipped off mid-raid) — without
                // this they stay latched until the next natural weight update.
                try
                {
                    var mainPlayer = Singleton<GameWorld>.Instantiated
                        ? Singleton<GameWorld>.Instance.MainPlayer
                        : null;
                    if (mainPlayer != null
                        && mainPlayer.HealthController != null
                        && mainPlayer.HealthController.IsAlive)
                    {
                        mainPlayer.Physical?.OnWeightUpdated();
                    }
                }
                catch (Exception)
                {
                    // Raid teardown — the next raid starts from vanilla anyway.
                }
            }

            ArenaMovementPatches.ResetPerRaidState();
            return;
        }

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

            // Re-applied every tick rather than once: Apply() is a no-op unless
            // the multiplier changed, which is what makes an F12 edit land
            // mid-raid instead of at the next respawn.
            if (VaultingTuning.Wanted)
                VaultingTuning.Apply();
            else
                VaultingTuning.Restore();

            if (player.Physical != null
                && (!_weightValuesApplied || Settings.NoInertia.Value != _lastNoInertia))
            {
                _weightValuesApplied = true;
                _lastNoInertia = Settings.NoInertia.Value;
                player.Physical.OnWeightUpdated();
            }
        }
        catch (Exception)
        {
            // Fail open — one bad frame must never break movement.
        }
    }
}
