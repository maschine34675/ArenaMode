using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure gate for item 2 of the 2026-10-06 follow-up ("announcer mutator"):
/// whether the Arena announcer (stingers, round/match cues and, since the
/// 2026-10-08 menu polish, the HUD's kill/award callouts such as "DOUBLE
/// KILL" — NOT the HUD's own text callouts; see <c>ZombieMode.Tick</c>'s
/// <c>ReportTextCallout</c>, which <c>ArenaHud</c> receives over a completely
/// separate event that is never touched by this gate) may speak, and show its
/// on-screen text, for the CURRENTLY ATTACHED match.
///
/// Two independent knobs feed it, mirroring the task's own design hint ("keep
/// volume global and make the enable per mode; the global switch, if any,
/// remains a master gate"):
///   - <see cref="Config.Settings.AnnouncerEnabled"/>: the pre-existing single
///     global switch, UNCHANGED, still the master kill switch for every mode.
///   - <see cref="Config.Settings.AnnouncerEntry"/>: the new per-mode switch
///     this follow-up adds, defaulting per <see cref="DefaultEnabled"/>.
///
/// Centralizing the AND here — rather than repeating it at every one of
/// <c>AnnouncerService</c>'s several read sites (attach-time log line,
/// <c>SubmitCallout</c>, <c>Tick</c>'s two checks, <c>Receive</c>) — is
/// deliberate: Stage A's own research for this follow-up flagged "all these
/// call sites must agree on which mode they are gating for the CURRENT match"
/// as a risk, and a single pure function used everywhere cannot disagree with
/// itself.
///
/// <c>_match.Mode.Id</c> itself cannot drift mid-match (<c>ArenaMatch.Mode</c>
/// has no setter, and <c>Attach()</c> only ever runs after <c>ArenaSession.
/// ActiveMode</c> latches), but that alone does not protect the per-mode
/// ENTRY VALUE behind that id from changing mid-match via a live F12 edit —
/// an earlier version of this file argued otherwise and shipped that bug
/// (ANN-1, caught in review the same day this follow-up landed). Every
/// sibling per-mode setting in this codebase freezes its config value once at
/// arm (Killcam's seconds into <c>ArenaReplaySelection</c>, Zombie's own-gear
/// choice into <c>ZombieOwnGearSelection</c>), and this one now does too:
/// <c>AnnouncerService</c> calls <see cref="IsEnabled"/> with <see
/// cref="Session.ArenaSession.AnnouncerEnabledSelection"/> — the per-mode half
/// frozen at <c>ArenaSession.MarkArmed</c> — as the per-mode argument, and the
/// live <see cref="Config.Settings.AnnouncerEnabled"/> master switch as the
/// other, so a mid-raid F12 edit to the per-mode entry can no longer reach an
/// already-armed/active match while the live master switch still can (by the
/// follow-up's own design: "the global switch, if any, remains a master
/// gate"). <see cref="Config.Settings.IsAnnouncerEnabledFor"/> keeps using the
/// LIVE per-mode entry and stays the menu-time display path only.
///
/// The WRITE side still freezes like every other per-mode mutator the other
/// way too: <c>ArenaMutatorSelection.TryToggle</c> refuses every write while
/// <c>ArenaSession.IsActive||ArmedForNextRaid</c>, the exact lock Killcam and
/// the equipment choice use — this file's fix closes the READ side that lock
/// alone could not: F12 writes the <c>ConfigEntry</c> directly and never goes
/// through <c>TryToggle</c>.
/// </summary>
internal static class ArenaAnnouncerGate
{
    /// <summary>
    /// The per-mode default used when first binding each mode's config entry.
    /// Zombie is the one mode whose endless waves have no round/match result
    /// to announce and whose constant melee audio the stingers would compete
    /// with, so it defaults OFF; every other mode keeps the previous
    /// (always-on) behavior as its default. Expressed as a single negative
    /// comparison, not five positive ones, so a sixth mode added later keeps
    /// the announcer on by default without anyone having to remember to list
    /// it — the same reasoning <see cref="ArenaPopulationPolicy"/>'s own doc
    /// comment gives for NOT defaulting an unrecognised mode into the loud
    /// branch there (the opposite failure direction, same underlying point:
    /// a default must be a decision, not an omission).
    /// </summary>
    internal static bool DefaultEnabled(string mode)
        => !string.Equals(mode?.Trim(), ArenaPopulationPolicy.Zombie, StringComparison.OrdinalIgnoreCase);

    /// <summary>The actual runtime decision: both the master and the per-mode switch must agree.</summary>
    internal static bool IsEnabled(bool masterEnabled, bool perModeEnabled) => masterEnabled && perModeEnabled;
}
