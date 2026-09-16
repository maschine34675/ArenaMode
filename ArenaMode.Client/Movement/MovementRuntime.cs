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
    private static float _lastWeightLimitMultiplier;
    private static PhysicalBase _lastWeightPhysical;
    private static bool _weightValuesApplied;

    public static void Tick()
    {
        ArenaAirVault.Tick();
        SprintAdsPatches.ClearBypassSafety();

        if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || !Singleton<GameWorld>.Instantiated)
        {
            // BSG's vaulting limits go back when the arena is over — not merely
            // when this tick's gate is shut. Restoring on a mid-raid F12 toggle
            // would desync the two gates: the obstacle SEARCH bounds were copied
            // into the vaulting model when the player was built and cannot be
            // taken back, so narrowing only the verdict leaves the player unable
            // to vault things vanilla would allow, until the next respawn.
            if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
                VaultingTuning.Restore();

            if (_weightValuesApplied)
            {
                // Bring the vanilla inertia values back immediately (e.g. the
                // MovementEnabled master switch flipped off mid-raid) — without
                // this they stay latched until the next natural weight update.
                try
                {
                    PhysicalBase currentPhysical = Singleton<GameWorld>.Instantiated
                        ? Singleton<GameWorld>.Instance.MainPlayer?.Physical
                        : null;
                    if (!ReferenceEquals(currentPhysical, _lastWeightPhysical))
                    {
                        // The scaled body has already left this GameWorld. A new
                        // respawn/ordinary-raid body was initialized through its
                        // own gates and must not receive an extra EFT weight event.
                        _weightValuesApplied = false;
                        _lastWeightPhysical = null;
                    }
                    else
                    {
                        // Rebuild the limits as well as the derived values. With
                        // the Arena/master gate now false, ArenaWeightPatches lets
                        // the fresh vanilla limits pass through unchanged.
                        currentPhysical.OnWeightLimitsUpdated();
                        // Commit only after EFT completed the restore. A transient
                        // teardown/config-change exception must be retried on the
                        // next tick while this exact Physical is still current.
                        _weightValuesApplied = false;
                        _lastWeightPhysical = null;
                    }
                }
                catch (Exception)
                {
                    // Keep the pending marker. The next tick retries only while
                    // this exact Physical remains bound; a replacement is untouched.
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
            // Slide controller lives on the player object; a respawn brings a new
            // one, while the old body stays as a corpse (absorbed CombatSlide
            // behavior, arena-only now). Attach lazily — and adopt a component
            // that disabled itself on death, which is the only way this exact
            // body can be live again, since a disabled component is not null and
            // would otherwise never be re-added.
            if (Settings.SlideEnabled.Value && player.MovementContext != null)
            {
                var slide = player.GetComponent<ArenaSlideController>();
                if (slide == null)
                    slide = player.gameObject.AddComponent<ArenaSlideController>();
                else if (slide.enabled)
                    slide = null;
                else
                    slide.enabled = true;

                if (slide != null)
                {
                    slide.Init(player, player.MovementContext);
                    ArenaMovementPatches.RememberSlide(player, slide);
                }
            }

            if (Settings.InfiniteStamina.Value && player.Physical != null)
            {
                player.Physical.Stamina.Current = player.Physical.Stamina.TotalCapacity;
                player.Physical.HandsStamina.Current = player.Physical.HandsStamina.TotalCapacity;
                player.Physical.Oxygen.Current = player.Physical.Oxygen.TotalCapacity;
            }

            // Vaulting limits are deliberately NOT touched here. VaultingReachPatch
            // sets them before the vaulting component is built, which is the only
            // moment the obstacle search reads them; changing them afterwards moves
            // one of the two gates and not the other. An F12 edit therefore lands
            // on the next respawn, which is when a new component is built.

            float weightLimitMultiplier = ArenaWeightTuning.BoundMultiplier(
                Settings.CarryWeightLimitMultiplier.Value);
            if (player.Physical != null
                && (!_weightValuesApplied
                    || !ReferenceEquals(player.Physical, _lastWeightPhysical)
                    || Settings.NoInertia.Value != _lastNoInertia
                    || Math.Abs(weightLimitMultiplier - _lastWeightLimitMultiplier) > 0.0001f))
            {
                // This calls UpdateWeightLimits first (where the Arena postfix
                // scales a fresh vanilla result) and then recomputes every weight-
                // derived movement value, including the inertia postfix.
                player.Physical.OnWeightLimitsUpdated();
                // Commit the snapshot only after EFT completed the rebuild. On a
                // transient failure the unchanged fields make the next tick retry.
                _weightValuesApplied = true;
                _lastNoInertia = Settings.NoInertia.Value;
                _lastWeightLimitMultiplier = weightLimitMultiplier;
                _lastWeightPhysical = player.Physical;
            }
        }
        catch (Exception)
        {
            // Fail open — one bad frame must never break movement.
        }
    }
}
