using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using BepInEx.Configuration;
using System;

namespace ArenaMode.Session;

internal enum ArenaMutator
{
    DoubleJump, AirControl, AirFire, AirVault, Slide, InfiniteStamina,
    NoFallDamage, Backpack, Killcam, Pickups, Tracers, Lasers
}

/// <summary>Menu adapter only: no duplicate preferences, defaults or gameplay policy.</summary>
internal sealed class ArenaMutatorSelection
{
    internal string Mode { get; private set; }
    private bool _team;
    internal bool Locked => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid;

    internal void Synchronize(string mode, bool team)
    { Mode = ArenaModeCatalog.NormalizeId(mode); _team = team; }

    internal static string Title(ArenaMutator id) => id switch
    {
        ArenaMutator.DoubleJump => "DOUBLE JUMP",
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
        _ => string.Empty
    };

    internal static string Description(ArenaMutator id) => id switch
    {
        ArenaMutator.DoubleJump => "One extra jump while airborne.",
        ArenaMutator.AirControl => "Steer your flight with movement input.",
        ArenaMutator.AirFire => "Fire without aiming while airborne.",
        ArenaMutator.AirVault => "Climb reachable obstacles during a jump.",
        ArenaMutator.Slide => "Crouch while sprinting to slide.",
        ArenaMutator.InfiniteStamina => "Keep your stamina while moving and aiming.",
        ArenaMutator.NoFallDamage => "Land without taking fall damage.",
        ArenaMutator.Backpack => "Use worn items and reload from backpack magazines.",
        ArenaMutator.Killcam => "This mode only. Respawn waits for replay; Tactical keeps its rounds.",
        ArenaMutator.Pickups => "Powerups and weapons on maps/areas with a pickup layout.",
        ArenaMutator.Tracers => "Color all bullets by side. Off restores native tracers.",
        ArenaMutator.Lasers => "Team colors for visible lasers. Brightness stays unchanged.",
        _ => string.Empty
    };

    private ConfigEntry<bool> Entry(ArenaMutator id) => id switch
    {
        ArenaMutator.DoubleJump => Settings.DoubleJumpEnabled,
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
        _ => null
    };

    internal bool Enabled(ArenaMutator id) => id == ArenaMutator.Killcam
        ? Settings.GetReplaySeconds(Mode) > 0f : Entry(id)?.Value == true;

    internal string UnavailableReason(ArenaMutator id)
    {
        if (id == ArenaMutator.Killcam)
            return Spectator.KillcamReplayCompat.CanPlay(out string reason) ? null : reason;
        if (Entry(id) == null) return "Setting unavailable.";
        if ((id == ArenaMutator.DoubleJump || id == ArenaMutator.AirControl || id == ArenaMutator.AirFire
            || id == ArenaMutator.AirVault || id == ArenaMutator.Slide
            || id == ArenaMutator.InfiniteStamina || id == ArenaMutator.NoFallDamage)
            && Settings.MovementEnabled?.Value != true)
            return "Requires Arena movement (F12 > Movement).";
        if (id == ArenaMutator.Pickups && (_team || (Mode != ArenaModeCatalog.DeathmatchId
            && Mode != ArenaModeCatalog.TeamDeathmatchId)))
            return "Available in Deathmatch and Team DM.";
        if (id == ArenaMutator.Lasers && Mode != ArenaModeCatalog.TeamDeathmatchId
            && !(Mode == ArenaModeCatalog.TacticalId && _team))
            return "Requires Team DM or Tactical 5v5 (PFT).";
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
