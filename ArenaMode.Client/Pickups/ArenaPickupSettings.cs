using BepInEx.Configuration;
using System;
using UnityEngine;

namespace ArenaMode.Pickups;

internal static class ArenaPickupSettings
{
    internal static ConfigEntry<bool> Enabled, BotsCollectPowerups, CaptureEnabled;
    internal static ConfigEntry<ArenaPickupKind> CaptureKind;
    internal static ConfigEntry<string> WeaponKit;
    internal static ConfigEntry<float> CaptureCooldown, SoundVolume;
    internal static ConfigEntry<KeyboardShortcut> CaptureFeet, CaptureAim, Undo, Export;

    internal static void Init(ConfigFile config)
    {
        Enabled = config.Bind("Arena pickups", "Enabled", true,
            "Arena-owned pickups in Deathmatch and Team Deathmatch. Factory day/night prototype is built in; optional maschine-ArenaMode/Pickups.json replaces it. Layouts are frozen per match. Disabling clears this match's pickups and effects.");
        SoundVolume = config.Bind("Arena pickups", "SoundVolume", .7f,
            new ConfigDescription("Successful local pickup sound volume; 0 mutes it. Independent of interface, hitmarker and announcer volume; master mute remains respected.", new AcceptableValueRange<float>(0f, 1f)));
        BotsCollectPowerups = config.Bind("Arena pickups", "BotsCollectPowerups", true,
            "Allow living Arena bots on either side to collect damage/shield/health pickups. The bundled companion for BigBrain 1.5.0 or newer also directs idle/searching bots to useful available pickups, yielding to combat and danger. Weapons, Jump Boots and Infinite Ammo are local-player-only.");
        WeaponKit = config.Bind("Arena pickups", "PrototypeWeaponKit", ArenaPickupWeapons.M60Id,
            "Pickup weapon: pickup-m60-retzrestdfg is the copied M60E6 with MRS, blue laser and 100 M993 rounds. Any existing catalogue kit ID also works (weapon only, no spare magazines or armor). Read at match start.");
        var weaponDefaultMigrated = config.Bind("Arena pickups", "M60DefaultMigrated", false,
            "Internal one-time migration of the old spartan prototype default to M60; leave enabled. Other kits and later manual choices are preserved.");
        if (!weaponDefaultMigrated.Value)
        {
            // Bind's new default does not replace a saved value. Migrate on the
            // machine loading this DLL, so a stale mirrored config cannot keep
            // the old prototype on every restart. Do not rewrite custom layouts.
            if (string.Equals(WeaponKit.Value, "spartan", StringComparison.OrdinalIgnoreCase))
                WeaponKit.Value = ArenaPickupWeapons.M60Id;
            weaponDefaultMigrated.Value = true;
        }
        CaptureEnabled = config.Bind("Development - Pickups", "Enabled", false,
            "Read-only point authoring. Aim/feet captures write only to an explicit PickupDrafts export. Normal PMC capture additionally requires Development - Area outline / AllowInNormalPmc. Never grants pickups outside Arena.");
        CaptureKind = config.Bind("Development - Pickups", "Kind", ArenaPickupKind.DoubleDamage,
            $"Kind of the next captured point. Health: up to {ArenaHealthPickup.Amount:0} HP on surviving parts; wounds remain. Shield: 220/30s; Double Damage: 20s; Jump Boots: +100% height/30s with Arena movement enabled; Infinite Ammo: 10s for loaded magazine weapons, including M60. No launchers/revolvers/break-action; native cycling and malfunctions remain. Boots/ammo are local only. Effects do not stack.");
        CaptureCooldown = config.Bind("Development - Pickups", "RespawnSeconds", 30f,
            new ConfigDescription("Pickup reappears this many gameplay seconds after successful collection.", new AcceptableValueRange<float>(10f, 600f)));
        CaptureAim = config.Bind("Development - Pickups", "CaptureAim", new KeyboardShortcut(KeyCode.F6, KeyCode.LeftAlt), "Capture the surface under the crosshair.");
        CaptureFeet = config.Bind("Development - Pickups", "CaptureFeet", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftAlt), "Capture the current feet position.");
        Undo = config.Bind("Development - Pickups", "Undo", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftAlt), "Remove the last pickup draft point.");
        Export = config.Bind("Development - Pickups", "Export", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftAlt), "Write a new draft JSON; never overwrites or activates an existing layout.");
    }
}
