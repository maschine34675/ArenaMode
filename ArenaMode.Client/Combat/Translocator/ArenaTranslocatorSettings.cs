using BepInEx.Configuration;

namespace ArenaMode.Combat.Translocator;

internal static class ArenaTranslocatorSettings
{
    internal static ConfigEntry<bool> Enabled { get; private set; }

    internal static void Bind(ConfigFile config)
        => Enabled = config.Bind("Arena translocator", "Enabled", false,
            "Adds a reusable Translocator in free rig/pocket space. Select it: fire throws/recalls the disc; aim teleports. Arena only.");
}
