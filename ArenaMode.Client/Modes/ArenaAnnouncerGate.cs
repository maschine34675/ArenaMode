using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure gate for item 2 of the 2026-10-06 follow-up ("announcer mutator"):
/// whether the Arena announcer (stingers, round/match cues — NOT the HUD's own
/// text callouts; see <c>ZombieMode.Tick</c>'s <c>ReportTextCallout</c> and
/// <c>ArenaHud</c>, which subscribe to a completely separate event and are
/// never touched by this gate) may speak for the CURRENTLY ATTACHED match.
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
/// itself. See <see cref="Config.Settings.IsAnnouncerEnabledFor"/>, the one
/// caller of <see cref="IsEnabled"/>.
///
/// No <c>ArenaSession</c> frozen capture is needed here, unlike
/// <c>ZombieOwnGearSelection</c>/<c>GunGameLadderSelection</c>:
/// <c>AnnouncerService</c> already keeps its own <c>_match</c> field, and
/// <c>Attach()</c> already runs AFTER <c>ArenaSession.ActiveMode</c> latches
/// (from <c>ArenaMatchService</c>'s raid-start handler), so a live read of
/// <c>_match.Mode.Id</c> can never observe a mode different from the one the
/// running raid actually armed — the mode itself is already immutable for the
/// life of a match (<c>ArenaMatch.Mode</c> has no setter). The WRITE side
/// still freezes like every other per-mode mutator: <c>ArenaMutatorSelection.
/// TryToggle</c> refuses every write while <c>ArenaSession.IsActive||
/// ArmedForNextRaid</c>, the exact lock Killcam and the equipment choice use.
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
