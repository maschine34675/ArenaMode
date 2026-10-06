using EFT;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// Zombie's own-gear option (item 4 of the 2026-10-06 follow-up): the
/// player's real equipment is protected exactly the way Tactical's protected
/// PitFireTeam path protects a PMC's entry loadout — the SAME
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
/// One snapshot per raid, captured once by <c>ArenaSession.OnRaidStarted</c>
/// in place of the ordinary raid-start kit swap (mirroring
/// TacticalMode.OnMatchStart's own team-branch capture); one strictly
/// increasing epoch per use, shared between a death respawn
/// (ArenaRespawnDriver.RespawnOwnGearPlayer) and the Stage B wave-
/// intermission refill (ZombieLoadoutRefill.TryRefillOwnGear) — they can
/// never race for the same life, because ZombieMode's own playerEligible
/// gate for the refill already requires HealthController.IsAlive and
/// !IsRespawnInProgress, which a mid-death player never satisfies.
///
/// A failed prepare or commit PERMANENTLY poisons the underlying snapshot's
/// restore gate (<c>TeamArenaLoadoutRestoreGate</c> — by design, the same
/// contract Tactical's protected team path already lives with: see its
/// CompletePrepare/CompleteCommit, which move straight to Poisoned on any
/// failure). Every later restore this raid then fails too. Both callers
/// treat that the same way Tactical's protected-team respawn already does —
/// a fatal, raid-ending abort — because the alternative (silently falling
/// back to a random kit, or to nothing) would hand out or destroy the
/// player's own real gear.
/// </summary>
internal static class ZombieOwnGearLoadout
{
    private static TeamArenaLoadoutSnapshot _snapshot;
    private static int _epoch;

    /// <summary>True once this raid captured an entry-loadout snapshot for Zombie's own-gear option.</summary>
    internal static bool IsActive => _snapshot != null;

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
            reason = "Zombie own gear has no captured entry loadout for this raid";
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
}
