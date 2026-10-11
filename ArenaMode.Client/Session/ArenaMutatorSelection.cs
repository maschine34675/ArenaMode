using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using BepInEx.Configuration;
using System;

namespace ArenaMode.Session;

internal enum ArenaMutator
{
    FastMovement, DoubleJump, Bunnyhop, AirControl, AirFire, AirVault, Slide,
    InfiniteStamina, NoFallDamage, Backpack, Killcam, Pickups, Tracers, Lasers, Akimbo, Announcer, FriendlyFire,
    AutoQuickslots, InstantSearch, ArenaHud
}

/// <summary>Menu adapter only: no duplicate preferences, defaults or gameplay policy.</summary>
internal sealed class ArenaMutatorSelection
{
    /// <summary>
    /// The N/A reason of the two team-only color switches, COLORED TRACERS and
    /// TEAM LASER COLORS (both follow the same Team DM / protected Tactical 5v5
    /// rule: <see cref="Settings.TracerEntry"/> returns no entry elsewhere,
    /// the laser row checks the mode itself).
    /// </summary>
    internal const string TeamModesOnly = "Requires Team DM or a PFT squad.";

    internal string Mode { get; private set; }
    private bool _team;
    internal bool Locked => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid;

    internal void Synchronize(string mode, bool team)
    { Mode = ArenaModeCatalog.NormalizeId(mode); _team = team; }

    internal static string Title(ArenaMutator id) => id switch
    {
        ArenaMutator.FastMovement => "FAST MOVEMENT",
        ArenaMutator.DoubleJump => "DOUBLE JUMP",
        ArenaMutator.Bunnyhop => "BUNNYHOP",
        ArenaMutator.AirControl => "AIR CONTROL",
        ArenaMutator.AirFire => "AIRBORNE HIP FIRE",
        ArenaMutator.AirVault => "AIR VAULT",
        ArenaMutator.Slide => "SLIDE",
        ArenaMutator.InfiniteStamina => "INFINITE STAMINA",
        ArenaMutator.NoFallDamage => "NO FALL DAMAGE",
        ArenaMutator.Backpack => "BACKPACK ITEMS",
        ArenaMutator.Killcam => "KILLCAM",
        ArenaMutator.Pickups => "PICKUPS",
        ArenaMutator.Tracers => "COLORED TRACERS",
        ArenaMutator.Lasers => "TEAM LASER COLORS",
        ArenaMutator.Akimbo => "AKIMBO PISTOLS",
        ArenaMutator.Announcer => "ANNOUNCER",
        ArenaMutator.FriendlyFire => "FRIENDLY FIRE",
        ArenaMutator.AutoQuickslots => "AUTO QUICKSLOTS",
        ArenaMutator.InstantSearch => "INSTANT SEARCH",
        ArenaMutator.ArenaHud => "ARENA HUD",
        _ => string.Empty
    };

    internal static string Description(ArenaMutator id) => id switch
    {
        ArenaMutator.FastMovement => "Movement speed and abilities.",
        ArenaMutator.DoubleJump => "One extra jump in the air.",
        ArenaMutator.Bunnyhop => "Timed hops: up to +25% speed.",
        ArenaMutator.AirControl => "Steer while airborne.",
        ArenaMutator.AirFire => "Hip fire while airborne.",
        ArenaMutator.AirVault => "Climb mid-jump within reach.",
        ArenaMutator.Slide => "Crouch during a sprint.",
        ArenaMutator.InfiniteStamina => "Unlimited movement/aim stamina.",
        ArenaMutator.NoFallDamage => "Land without taking fall damage.",
        ArenaMutator.Backpack => "Use backpack items/magazines.",
        ArenaMutator.Killcam => "Replay before respawn.",
        ArenaMutator.Pickups => "DM/Team DM with pickup layouts.",
        ArenaMutator.Tracers => "Team colors for every bullet.",
        ArenaMutator.Lasers => "Team colors; same brightness.",
        ArenaMutator.Akimbo => "Dual pistols; shared magazine.",
        // Per-mode, like Killcam: toggles the CURRENTLY SELECTED
        // mode's own switch, not the global master (see Settings.
        // AnnouncerEntry/IsAnnouncerEnabledFor). Zombie starts OFF, every
        // other mode starts ON.
        // Stingers, round cues AND the HUD's kill/award callouts ("DOUBLE
        // KILL"); the "WAVE n" / grenade-taunt text is not announcer output.
        ArenaMutator.Announcer => "Stingers, cues and kill callouts.",
        ArenaMutator.FriendlyFire => "Allow damage between teammates.",
        ArenaMutator.AutoQuickslots => "Bind kit meds to free quickslots.",
        ArenaMutator.InstantSearch => "Search containers instantly.",
        ArenaMutator.ArenaHud => "Killfeed, hits, health and weapons.",
        _ => string.Empty
    };

    private ConfigEntry<bool> Entry(ArenaMutator id) => id switch
    {
        ArenaMutator.FastMovement => Settings.MovementEnabled,
        ArenaMutator.DoubleJump => Settings.DoubleJumpEnabled,
        ArenaMutator.Bunnyhop => Settings.BunnyhopEnabled,
        ArenaMutator.AirControl => Settings.AirControlEnabled,
        ArenaMutator.AirFire => Settings.AirHipFireEnabled,
        ArenaMutator.AirVault => Settings.AirVaultEnabled,
        ArenaMutator.Slide => Settings.SlideEnabled,
        ArenaMutator.InfiniteStamina => Settings.InfiniteStamina,
        ArenaMutator.NoFallDamage => Settings.NoFallDamage,
        ArenaMutator.Backpack => Settings.UseItemsAnywhere,
        ArenaMutator.Pickups => ArenaPickupSettings.Enabled,
        ArenaMutator.Tracers => Settings.TracerEntry(Mode, _team),
        ArenaMutator.Lasers => Settings.TeamLasers,
        ArenaMutator.Akimbo => Settings.AkimboEnabled,
        // Every mode maps to a real entry (see Settings.AnnouncerEntry), so
        // unlike Killcam/the retired OwnGear row this needs no special case
        // in Enabled()/TryToggle() below — the generic bool-entry path
        // already used for Tracers/Lasers/Akimbo handles it as-is.
        ArenaMutator.Announcer => Settings.AnnouncerEntry(Mode),
        ArenaMutator.FriendlyFire => Settings.FriendlyFireEntry(Mode, _team),
        ArenaMutator.AutoQuickslots => Settings.AutoQuickSlots,
        ArenaMutator.InstantSearch => Settings.InstantSearch,
        ArenaMutator.ArenaHud => Settings.CombatHudEnabled,
        _ => null
    };

    internal bool Enabled(ArenaMutator id) => id == ArenaMutator.Killcam
        ? Settings.GetReplaySeconds(Mode) > 0f : Entry(id)?.Value == true;

    internal string UnavailableReason(ArenaMutator id)
    {
        if (id == ArenaMutator.Killcam)
            return Spectator.KillcamReplayCompat.CanPlay(out string reason) ? null : reason;
        // Team-only like the lasers: TracerEntry has no entry outside Team DM and
        // protected Tactical 5v5, which is a mode restriction, not a missing setting.
        if ((id == ArenaMutator.Tracers || id == ArenaMutator.FriendlyFire)
            && Entry(id) == null) return TeamModesOnly;
        if (Entry(id) == null) return "Setting unavailable.";
        if (id == ArenaMutator.ArenaHud && Settings.HudEnabled?.Value != true)
            return "Disabled by HUD master in F12.";
        if ((id == ArenaMutator.DoubleJump || id == ArenaMutator.Bunnyhop || id == ArenaMutator.AirControl || id == ArenaMutator.AirFire
            || id == ArenaMutator.AirVault || id == ArenaMutator.Slide
            || id == ArenaMutator.InfiniteStamina || id == ArenaMutator.NoFallDamage)
            && Settings.MovementEnabled?.Value != true)
            return "Requires FAST MOVEMENT.";
        if (id == ArenaMutator.Pickups && (_team || (Mode != ArenaModeCatalog.DeathmatchId
            && Mode != ArenaModeCatalog.TeamDeathmatchId)))
            return "Available in Deathmatch and Team DM.";
        if (id == ArenaMutator.Lasers && Mode != ArenaModeCatalog.TeamDeathmatchId
            && !((Mode == ArenaModeCatalog.TacticalId || Mode == ArenaModeCatalog.ZombieId) && _team))
            return TeamModesOnly;
        return null;
    }

    internal bool TryToggle(ArenaMutator id)
    {
        if (Locked || UnavailableReason(id) != null) return false;
        if (id == ArenaMutator.Killcam) return Settings.TrySelectKillcam(Mode, !Enabled(id));
        var entry = Entry(id);
        if (entry == null) return false;
        bool requested = !entry.Value;
        try
        {
            entry.Value = requested;
            if (entry.Value != requested) return false;
            if (Settings.File?.SaveOnConfigSet == false) Settings.File.Save();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena mutator {id} could not be saved: {ex.Message}");
            return false;
        }
    }
}
