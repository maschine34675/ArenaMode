using BepInEx.Configuration;

namespace ArenaMode.Combat.Fire;

/// <summary>One saved preference shared by F12, the map menu and fire runtime.</summary>
internal static class ArenaFireSettings
{
    internal static ConfigEntry<bool> Enabled { get; private set; }

    internal static void Bind(ConfigFile config)
    {
        Enabled = config.Bind("Arena incendiary prototype", "Enabled", true,
            $"WIP: adds one Molotov per equipment loadout in free rig/pocket space during live Arena (except Zombies); existing grenades are kept. G selects the bonus first; later manual selection is preserved. Uses the RGD-5 model/name, but only the bonus creates 6-second ground fire ({ArenaFirePolicy.DamagePerSecond} base total HP/s, non-stacking). Full rig/pockets: no bonus, nothing replaced. Requires the matching BigBrain companion. Disabling safely extinguishes owned fire; no extra supply from toggling off/on.");
    }
}
