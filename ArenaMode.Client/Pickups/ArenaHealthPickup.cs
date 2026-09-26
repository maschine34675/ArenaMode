using ArenaMode.Combat;
using EFT;
using EFT.HealthSystem;
using System;

namespace ArenaMode.Pickups;

internal enum ArenaHealingResult { Declined, Applied, Indeterminate }

/// <summary>HP only; native wounds, blacked parts and reduced maxima survive.</summary>
internal static class ArenaHealthPickup
{
    internal const float Amount = 150f;
    // Stabilize vital parts first, then abdomen and limbs. Never heal Common.
    private static readonly EBodyPart[] Parts = { EBodyPart.Head, EBodyPart.Chest,
        EBodyPart.Stomach, EBodyPart.LeftLeg, EBodyPart.RightLeg, EBodyPart.LeftArm, EBodyPart.RightArm };

    private static bool Owns(Player player, ActiveHealthController health, int generation)
        => generation == ArenaPickupRuntime.Generation && ArenaPickupRuntime.IsLiveActor(player)
        && ReferenceEquals(player.ActiveHealthController, health) && ReferenceEquals(health?.Player, player)
        && health.IsAlive && ArenaPickupLayouts.Finite(health.DamageCoeff) && health.DamageCoeff > 0
        && !ArenaAdministrativeDeathGate.IsActive(player.ProfileId);

    internal static float Missing(Player player)
    {
        try
        {
            var health = player?.ActiveHealthController;
            if (!Owns(player, health, ArenaPickupRuntime.Generation)) return 0;
            float total = 0;
            foreach (var part in Parts) total += Missing(health, part);
            return total;
        }
        catch { return 0; }
    }

    private static float Missing(ActiveHealthController health, EBodyPart part)
    {
        var value = health.GetBodyPartHealth(part);
        return !health.IsBodyPartDestroyed(part) && ArenaPickupLayouts.Finite(value.Current)
            && ArenaPickupLayouts.Finite(value.Maximum) && value.Current > 0 && value.Maximum > value.Current
            ? value.Maximum - value.Current : 0;
    }

    internal static ArenaHealingResult Apply(Player player, out float healed)
    {
        healed = 0;
        var health = player?.ActiveHealthController;
        int generation = ArenaPickupRuntime.Generation;
        bool attempted = false;
        float budget = Amount;
        try
        {
            foreach (var part in Parts)
            {
                if (!Owns(player, health, generation) || budget <= .001f) break;
                float amount = Math.Min(budget, Missing(health, part));
                if (amount <= .001f) continue;
                float before = health.GetBodyPartHealth(part).Current;
                attempted = true;
                // Spend requested budget, not net observed HP: a synchronous
                // callback may deal damage or heal too, and must not grant us
                // another full budget worth of native calls.
                budget -= amount;
                Exception failure = null;
                try { health.ChangeHealth(part, amount, DamageHelper.Existence); }
                catch (Exception ex) { failure = ex; }
                float delta = health.GetBodyPartHealth(part).Current - before;
                if (!ArenaPickupLayouts.Finite(delta) || delta <= 0)
                    return healed > 0 ? ArenaHealingResult.Applied : ArenaHealingResult.Indeterminate;
                healed += Math.Min(amount, delta);
                if (failure != null)
                {
                    Plugin.ModLogger.LogWarning("Arena health pickup stopped after native health callback: " + failure.GetBaseException().Message);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("Arena health pickup could not confirm native healing: " + ex.GetBaseException().Message);
            return healed > 0 ? ArenaHealingResult.Applied : attempted ? ArenaHealingResult.Indeterminate : ArenaHealingResult.Declined;
        }
        return healed > 0 ? ArenaHealingResult.Applied : attempted ? ArenaHealingResult.Indeterminate : ArenaHealingResult.Declined;
    }
}
