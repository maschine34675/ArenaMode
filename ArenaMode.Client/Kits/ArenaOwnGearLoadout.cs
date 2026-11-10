using EFT;
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// The player's own-gear option (Modes.ArenaOwnGearPolicy; Zombie's item 4 of
/// the 2026-10-06 follow-up, offered to every mode but Gun Game since
/// 2026-11-09, docs/reviews/OWN-GEAR-ALL-MODES-20261109.md): the player's real
/// equipment is protected exactly the way Tactical's protected PitFireTeam
/// path protects a PMC's entry loadout — the SAME
/// <see cref="TeamArenaLoadoutSnapshot"/> capture/arm/prepare/commit
/// primitive TacticalMode.OnMatchStart's team branch and
/// ArenaRespawnDriver.RespawnProtectedTeamPlayer already use (see that
/// class's own doc comment and TEAM-LOADOUT-CONSUMPTION-REVIEW.md), just
/// without PitFireTeam's round-transition staging: Zombie has no "hold the
/// pipeline open across several frames while N teammates reassemble" delay,
/// so every restore here arms, prepares AND commits in one synchronous call
/// (<see cref="TryRestore"/>), inside the SAME <c>EquipmentStrategy</c>
/// callback <c>RespawnEngine.RunRespawn</c> already invokes at the exact
/// point <c>KitBuilder.ApplyToProfile</c>/<c>TeamArenaLoadoutSnapshot.
/// TryCommitPrepared</c> run for every other mode — right after the old
/// owner/world registration is torn down but before the profile's equipment
/// is actually rebuilt (RespawnEngine.cs: "profile.equipment" timing mark).
/// The dead/old body's Profile is still exactly the one <see
/// cref="TryCaptureEntryLoadout"/> saw, so there is no async window for it to
/// have changed in between, unlike Tactical's own multi-frame round
/// lifecycle — which is why no separate "prepare ahead of time" stage is
/// needed here.
///
/// One snapshot per raid, captured once by <c>ArenaSession.CompleteRaidStart</c>
/// in place of the ordinary raid-start kit swap (mirroring
/// TacticalMode.OnMatchStart's own team-branch capture); one strictly
/// increasing epoch per use, shared between a death respawn
/// (ArenaRespawnDriver's ordinary path with <see cref="Reissue"/> as its
/// equipment strategy), the round-break reissue of Deathmatch rounds and
/// solo Tactical (LiveKitRespawn.AtRoundBreak, own-gear aware) and Zombie's
/// wave-intermission refill (ZombieLoadoutRefill.TryRefillOwnGear) — they can
/// never race for the same life: every live swap requires
/// HealthController.IsAlive and !IsRespawnInProgress, which a mid-death
/// player never satisfies, and the driver holds the one pipeline lease.
///
/// A failed prepare or commit PERMANENTLY poisons the underlying snapshot's
/// restore gate (<c>TeamArenaLoadoutRestoreGate</c> — by design, the same
/// contract Tactical's protected team path already lives with: see its
/// CompletePrepare/CompleteCommit, which move straight to Poisoned on any
/// failure). Every later restore this raid then fails too, but the two
/// callers do NOT treat that failure the same way — an asymmetry, not an
/// oversight:
///
/// <list type="bullet">
/// <item><description>A death respawn (ArenaRespawnDriver's ordinary path)
/// is fatal for the raid: the <see cref="InvalidOperationException"/>
/// <see cref="Reissue"/> throws surfaces as the engine's failure, and the
/// driver routes it the way it routes any failed body replacement (the Team
/// DM result coordinator, the team-box or area abort, or the death screen),
/// because a death respawn has no body left to fall back to and a player who
/// chose own gear must never be handed a random kit instead.</description></item>
/// <item><description>A live reissue throws the same exception from inside
/// the <c>EquipmentStrategy</c> closure; <c>RespawnEngine.RunRespawn</c>'s
/// failure callback hands it to <c>LiveKitRespawn.SwapRoutine</c>, which runs
/// its ordinary recovery (<c>RecoverOldBody</c> + reporting "did not start"),
/// so that one resupply is skipped and the player keeps their current,
/// unchanged body. What happens next depends on the caller:
/// <c>LiveKitRespawn.AtRoundBreak</c> in an own-gear raid (Deathmatch's
/// round break, solo Tactical's survivors) asks SwapRoutine to end the match
/// at once when the snapshot is poisoned (<see cref="IsPoisoned"/>), because
/// those modes respawn on death and the next respawn is certain to fail;
/// <c>ZombieLoadoutRefill.TryRefillOwnGear</c> does not ask (one life, no
/// death respawn to doom) and the Zombie raid continues as it always did,
/// the error logged once per attempt.</description></item>
/// </list>
/// </summary>
internal static class ArenaOwnGearLoadout
{
    private static TeamArenaLoadoutSnapshot _snapshot;
    private static int _epoch;

    /// <summary>True once this raid captured an entry-loadout snapshot for the player's own-gear option.</summary>
    internal static bool IsActive => _snapshot != null;

    /// <summary>
    /// True once a restore failed this raid: the snapshot's gate refuses every
    /// later restore, so the next death respawn is certain to fail. Read by the
    /// live reissue to end the match now rather than one life later.
    /// </summary>
    internal static bool IsPoisoned => _snapshot?.RestorePoisoned == true;

    internal static void Reset()
    {
        _snapshot = null;
        _epoch = 0;
    }

    /// <summary>
    /// Captures the live equipment graph exactly once per raid, the same
    /// moment and the same way TacticalMode.OnMatchStart captures its own
    /// team-branch snapshot — called by ArenaSession instead of the ordinary
    /// raid-start kit swap.
    /// </summary>
    internal static bool TryCaptureEntryLoadout(Profile profile, out string reason)
    {
        _snapshot = null;
        _epoch = 0;
        if (!TeamArenaLoadoutSnapshot.TryCapture(profile, out var snapshot, out reason))
            return false;

        _snapshot = snapshot;
        return true;
    }

    /// <summary>
    /// Arms, prepares and commits one full restore of the captured entry
    /// loadout onto <paramref name="profile"/>'s CURRENT equipment, in one
    /// synchronous call. Used identically by a death respawn and the wave-
    /// intermission refill; see this class's own doc comment for why no
    /// separate prepare-ahead stage is needed here, unlike
    /// TacticalMode.TickTeamRetirement's team equivalent.
    /// </summary>
    internal static bool TryRestore(
        Profile profile,
        out int epoch,
        out string reason,
        out IReadOnlyDictionary<string, string> nextRuntimeByPreviousRuntimeId)
    {
        epoch = 0;
        nextRuntimeByPreviousRuntimeId = null;

        var snapshot = _snapshot;
        if (snapshot == null)
        {
            reason = "own gear has no captured entry loadout for this raid";
            return false;
        }

        epoch = ++_epoch;
        if (!snapshot.TryArm(epoch, out reason))
            return false;

        if (!snapshot.TryPrepare(profile, epoch, out var prepared, out reason))
            return false;

        if (!prepared.TryCommit(profile, out var result))
        {
            reason = result?.Failure ?? "the prepared restore returned no diagnostic";
            return false;
        }

        nextRuntimeByPreviousRuntimeId = prepared.NextRuntimeByPreviousRuntimeId;
        reason = null;
        return true;
    }

    /// <summary>
    /// One complete reissue of the captured entry loadout onto the body being
    /// rebuilt, the way every EquipmentStrategy that restores own gear needs
    /// it: the dead or old body's quick-slot ledger is settled first (its keys
    /// and items are still exact at that seam — see AutoQuickSlots.
    /// SettleBeforeReissue), the restore is armed, prepared and committed, and
    /// the ledger follows the fresh ids. Throws InvalidOperationException on a
    /// failed restore so RespawnEngine.RunRespawn's failure callback sees it
    /// like any other equipment failure; the snapshot's restore gate is
    /// poisoned by then (see the class comment) and every later restore this
    /// raid fails too. Returns the restore epoch for the caller's log line.
    /// </summary>
    internal static int Reissue(Profile profile, string what)
    {
        AutoQuickSlots.SettleBeforeReissue(profile);
        if (!TryRestore(profile, out int epoch, out string reason, out var nextRuntimeByPreviousRuntimeId))
            throw new InvalidOperationException($"{what} could not restore the entry loadout: {reason}");
        AutoQuickSlots.NoteItemsReissued(nextRuntimeByPreviousRuntimeId);
        return epoch;
    }
}
