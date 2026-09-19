using BepInEx.Configuration;
using UnityEngine;

namespace ArenaMode.Pickups;

internal static class ArenaPickupSettings
{
    internal static ConfigEntry<bool> Enabled, BotsCollectPowerups, CaptureEnabled;
    internal static ConfigEntry<ArenaPickupKind> CaptureKind;
    internal static ConfigEntry<string> WeaponKit;
    internal static ConfigEntry<float> CaptureCooldown;
    internal static ConfigEntry<KeyboardShortcut> CaptureFeet, CaptureAim, Undo, Export;

    internal static void Init(ConfigFile config)
    {
        Enabled = config.Bind("Arena pickups", "Enabled", true,
            "Arena-owned pickups in Deathmatch and Team Deathmatch. Factory day/night prototype is built in; optional ArenaMode/Pickups.json replaces it. Layouts are frozen per match. Disabling clears this match's pickups and effects.");
        BotsCollectPowerups = config.Bind("Arena pickups", "BotsCollectPowerups", true,
            "Allow living Arena bots on either side to collect damage/shield powerups by walking over them. Does not change their routes. Weapons and Jump Boots are local-player-only.");
        WeaponKit = config.Bind("Arena pickups", "PrototypeWeaponKit", ArenaPickupWeapons.M60Id,
            "Pickup weapon: pickup-m60-retzrestdfg is the copied M60E6 with MRS, blue laser and 100 M993 rounds. Any existing catalogue kit ID also works (weapon only, no spare magazines or armor). Read at match start.");
        CaptureEnabled = config.Bind("Development - Pickups", "Enabled", false,
            "Read-only point authoring. Aim/feet captures write only to an explicit PickupDrafts export. Normal PMC capture additionally requires Development - Area outline / AllowInNormalPmc. Never grants pickups outside Arena.");
        CaptureKind = config.Bind("Development - Pickups", "Kind", ArenaPickupKind.DoubleDamage,
            "Kind of the next captured point. Shield: 220/30s; Double Damage: 20s; Jump Boots: +50% height/30s with Arena movement enabled (local only). Effects do not stack.");
        CaptureCooldown = config.Bind("Development - Pickups", "RespawnSeconds", 30f,
            new ConfigDescription("Pickup reappears this many gameplay seconds after successful collection.", new AcceptableValueRange<float>(10f, 600f)));
        CaptureAim = config.Bind("Development - Pickups", "CaptureAim", new KeyboardShortcut(KeyCode.F6, KeyCode.LeftAlt), "Capture the surface under the crosshair.");
        CaptureFeet = config.Bind("Development - Pickups", "CaptureFeet", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftAlt), "Capture the current feet position.");
        Undo = config.Bind("Development - Pickups", "Undo", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftAlt), "Remove the last pickup draft point.");
        Export = config.Bind("Development - Pickups", "Export", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftAlt), "Write a new draft JSON; never overwrites or activates an existing layout.");
    }
}
