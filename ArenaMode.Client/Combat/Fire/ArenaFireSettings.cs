using BepInEx.Configuration;

namespace ArenaMode.Combat.Fire;

/// <summary>One saved preference shared by F12, the map menu and fire runtime.</summary>
internal static class ArenaFireSettings
{
    internal static ConfigEntry<bool> Enabled { get; private set; }

    internal static void Bind(ConfigFile config)
    {
        Enabled = config.Bind("Arena incendiary prototype", "Enabled", true,
            $"WIP: your RGD-5 throws become 6-second ground fire in live Arena only (except Zombies). Existing grenade model; bot grenades and ordinary raids are unchanged. {ArenaFirePolicy.DamagePerSecond} base total HP/s; fire does not stack. Requires the matching BigBrain companion for bot escape. May be toggled between throws; disabling extinguishes owned fire and retires owned grenades safely.");
    }
}
