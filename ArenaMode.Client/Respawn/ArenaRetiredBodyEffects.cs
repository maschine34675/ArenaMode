using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;

namespace ArenaMode.Respawn;

/// <summary>
/// Finishes the visual lifetime of a living body that an Arena kit swap has
/// already replaced. Native death/heavy-bleed removal unregisters blood decals,
/// but Player.Dispose/OnDestroy and GameWorld.UnregisterPlayer do not. Destroying
/// a surviving bleeding body without this leaves UpdatePlayersBleedings reading
/// its destroyed Transform every frame. Never remove by profile ID: the new
/// local body uses the same profile and can have its own bleeding registration.
/// </summary>
internal static class ArenaRetiredBodyEffects
{
    private static readonly AccessTools.FieldRef<Player, EFT.Interactive.Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, EFT.Interactive.Corpse>("Corpse");

    internal static bool TryDestroy(LocalGame game, LocalPlayer retiredBody)
    {
        // The caller retains its captured raid-epoch check before reaching this
        // synchronous terminal step. Also require the current world and a real
        // replacement; never touch a failed swap that is recovering the old body.
        if (!ArenaSession.IsActive || game == null || game.Status != GameStatus.Started
            || !ArenaSession.IsExactActiveRaid(game, game.GameWorld)
            || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, game.GameWorld)
            || retiredBody == null || game.LocalPlayer == null
            || ReferenceEquals(game.LocalPlayer, retiredBody)
            || !ReferenceEquals(game.GameWorld.MainPlayer, game.LocalPlayer)
            || string.IsNullOrEmpty(retiredBody.ProfileId)
            || !string.Equals(game.LocalPlayer.ProfileId, retiredBody.ProfileId, StringComparison.Ordinal)
            || retiredBody.HealthController == null || !retiredBody.HealthController.IsAlive
            || CorpseField(retiredBody) != null)
        {
            return false;
        }

        var health = retiredBody.HealthController;
        bool addedDetached = false, removedDetached = false, diedDetached = false;
        try
        {
            // Exactly Player.UnsubscribeVisualEvents' three delegates. Track the
            // completed removals independently so a failing step cannot duplicate
            // a subscription during rollback. All other health subscribers stay.
            health.EffectStartedEvent -= retiredBody.OnHealthEffectVisualAdded;
            addedDetached = true;
            health.EffectResidualEvent -= retiredBody.OnHealthEffectVisualRemoved;
            removedDetached = true;
            health.DiedEvent -= retiredBody.OnPlayerVisualDied;
            diedDetached = true;

            // StopBleedingForPlayer compares the IPlayer reference and removes
            // only its own entry; there is no global list clear or Update patch.
            if (Singleton<Systems.Effects.Effects>.Instantiated)
            {
                var commutator = Singleton<Systems.Effects.Effects>.Instance.EffectsCommutator;
                if (commutator == null)
                    throw new InvalidOperationException("Retired Arena body has an effects singleton without its bleeding commutator.");
                commutator.StopBleedingForPlayer(retiredBody);
            }

            UnityEngine.Object.Destroy(retiredBody.gameObject);
            return true;
        }
        catch (Exception cleanupFailure)
        {
            // A cleanup failure must not proceed to Destroy. Restore only the
            // removed visual delegates, but never invent a bleeding registration:
            // this body is already retired and might never have been bleeding.
            try
            {
                if (addedDetached) health.EffectStartedEvent += retiredBody.OnHealthEffectVisualAdded;
                if (removedDetached) health.EffectResidualEvent += retiredBody.OnHealthEffectVisualRemoved;
                if (diedDetached) health.DiedEvent += retiredBody.OnPlayerVisualDied;
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Retired Arena body visual cleanup and subscription rollback failed.",
                    cleanupFailure, rollbackFailure);
            }
            throw;
        }
    }
}
