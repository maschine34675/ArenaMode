using ArenaMode.Kits;
using ArenaMode.Respawn;
using EFT;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Thin EFT/Unity-facing adapter for item 3 ("refill between waves"): HOW to
/// re-apply the player's current loadout, never WHEN — that is
/// <see cref="ZombieRefillPolicy"/>'s job. Deliberately writes no new
/// inventory-mutation code: it re-applies the exact same KitDefinition the
/// player is already wearing through <see cref="LiveKitRespawn.AtRoundBreak"/>,
/// the identical live-body respawn path Gun Game and Tactical already use
/// between rounds (TacticalMode.StartRound; see LiveKitRespawn's own doc
/// comment, "Unlike a death respawn there is no corpse to keep"). A full
/// kit re-application is exactly a heal and a resupply through a path that
/// is already tested for it — TacticalMode.StartRound's own doc comment
/// makes the same argument for survivors kept between rounds — and, as an
/// ACCEPTED SUPERSET for now, it also restores armor/weapon durability and
/// relocates the player a short distance (LiveKitRespawn.AtRoundBreak's own
/// NearPositionSpawnSelector, up to RespawnMinDistance/~25m) rather than
/// leaving them standing exactly where the wave ended — the same trade-off
/// Tactical's own round-break swap already makes for every surviving
/// player, now exercised by Zombie's intermission instead of a round break.
///
/// <para><b>Why the player's CURRENT kit, not a fresh random draw.</b>
/// Zombie sets no <see cref="KitCatalog.KitProvider"/> today, so
/// AtRoundBreak's own internal <c>KitCatalog.GetSelectedKit()</c> call would
/// otherwise fall through to <c>GetBaseKit()</c> and hand back a brand-new
/// random kit — the refill would silently swap the player's gear instead of
/// topping it up. <see cref="TryRefill"/> is handed the kit ZombieMode last
/// saw applied (<c>ArenaEvents.OnKitApplied</c>, updated by every raid-start
/// swap and this same refill), and installs it as the
/// provider for the EXACT width of the one synchronous call into
/// AtRoundBreak — which captures its kit via closure on its very first line,
/// BEFORE starting its coroutine (see LiveKitRespawn.AtRoundBreak,
/// <c>var kit = KitCatalog.GetSelectedKit();</c>) — then restores whatever
/// was there before. Solo death now ends survival; this provider is used only
/// for a living intermission refill, never persisted after that one call.</para>
/// </summary>
internal static class ZombieLoadoutRefill
{
    /// <summary>
    /// Attempts the refill for the wave that just ended. Returns true iff
    /// LiveKitRespawn.AtRoundBreak actually dispatched the swap (the hands/
    /// animation and area-pending waits it respects internally, plus the
    /// visible resupply itself, happen asynchronously afterward — this
    /// method only learns "started", matching every other optimistic-commit
    /// path in this codebase); the caller (ZombieMode) reports that back to
    /// ZombieRefillPolicy via ReportStarted/ReportNotStarted. Never throws:
    /// any exception from the respawn path is caught, logged once, and
    /// reported as "did not start" so the policy's own bounded retry (not a
    /// second attempt from here) decides what happens next.
    /// </summary>
    internal static bool TryRefill(
        LocalGame game,
        KitDefinition currentKit,
        int clearedWaveNumber,
        float protectionSeconds,
        Func<bool> stillWanted)
    {
        if (currentKit == null)
        {
            // Nothing remembered yet (e.g. the raid-start swap itself never
            // completed) — not an error, just nothing to re-apply. The
            // policy will retry on its own bounded cadence.
            return false;
        }

        var previousProvider = KitCatalog.KitProvider;
        try
        {
            KitCatalog.KitProvider = () => currentKit;
            return LiveKitRespawn.AtRoundBreak(
                game,
                protectionSeconds,
                stillWanted,
                success =>
                {
                    if (success)
                    {
                        Plugin.ModLogger.LogDebug(
                            $"Zombie refill after wave {clearedWaveNumber}: kit '{currentKit.Name ?? currentKit.Id}' re-applied.");
                    }
                    // A reported failure is already logged by LiveKitRespawn
                    // itself (its own "stood down"/"failed" Info/Error lines)
                    // — no second line needed here.
                });
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Zombie refill after wave {clearedWaveNumber} failed: {ex.Message}");
            return false;
        }
        finally
        {
            KitCatalog.KitProvider = previousProvider;
        }
    }

    /// <summary>
    /// The own-gear (item 4) sibling of <see cref="TryRefill"/>: re-applies
    /// the player's protected ENTRY LOADOUT instead of a kit, through
    /// <see cref="LiveKitRespawn.AtRoundBreakOwnGear"/> — the same live,
    /// in-place body swap TryRefill's AtRoundBreak call uses, just committing
    /// ZombieOwnGearLoadout's restore instead of a KitDefinition. Writes no
    /// new inventory-mutation code of its own, same as TryRefill. Returns
    /// whether the refill actually started, for ZombieRefillPolicy's own
    /// started/not-started bookkeeping — identical contract to TryRefill.
    /// </summary>
    internal static bool TryRefillOwnGear(
        LocalGame game,
        int clearedWaveNumber,
        float protectionSeconds,
        Func<bool> stillWanted)
    {
        if (!ZombieOwnGearLoadout.IsActive)
        {
            // Nothing captured this raid (e.g. the raid-start capture itself
            // failed and already aborted the match) — not an error here,
            // just nothing to restore.
            return false;
        }

        try
        {
            // Captured by the EquipmentStrategy closure below and read again
            // in the onComplete callback — logging there, not inside the
            // closure, matches TryRefill's own pattern: only report success
            // once LiveKitRespawn itself confirms the whole swap landed, not
            // merely that this one step inside it ran.
            int restoredEpoch = 0;
            return LiveKitRespawn.AtRoundBreakOwnGear(
                game,
                (profile, dead) =>
                {
                    AutoQuickSlots.SettleBeforeReissue(profile);

                    if (!ZombieOwnGearLoadout.TryRestore(profile, out restoredEpoch, out string reason, out var nextRuntimeMap))
                    {
                        throw new InvalidOperationException(
                            $"Zombie own-gear refill could not restore the entry loadout: {reason}");
                    }

                    AutoQuickSlots.NoteItemsReissued(nextRuntimeMap);
                },
                protectionSeconds,
                stillWanted,
                success =>
                {
                    if (success)
                    {
                        Plugin.ModLogger.LogDebug(
                            $"Zombie refill after wave {clearedWaveNumber}: own-gear entry loadout re-applied (epoch {restoredEpoch}).");
                    }
                    // A reported failure is already logged by LiveKitRespawn
                    // itself — no second line needed here, same as TryRefill.
                });
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Zombie own-gear refill after wave {clearedWaveNumber} failed: {ex.Message}");
            return false;
        }
    }
}
